using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal partial class RuntimeNativePlayer
{
    private (FalloutFormKey Weapon, Guid Instance, int LoadedBeforeDischarge)? _automaticReloadAfterDischarge;
    private bool _weaponActionReloadMaterialized;

    // The source consumes the last round, materializes process AMMO, then
    // selects the reload group. This is not an input event or an empty query.
    private void PublishAutomaticReloadAfterDischarge()
    {
        if (_automaticReloadAfterDischarge is not { } request) return;
        _automaticReloadAfterDischarge = null;
        try
        {
            if (_weaponActionError is not null || _firePreparationError is not null)
                throw new NotSupportedException("Automatic reload follows an unresolved source discharge.");
            var weapon = _firstPerson?.Weapon ?? throw new InvalidOperationException("Automatic reload lost its source weapon.");
            if (weapon.Form != request.Weapon || _presentationInventory!.SelectedWeapon(weapon.Form).Extra.Instance != request.Instance ||
                VatsExecuting || !_weaponHandling!.Drawn || _pendingShotCount != 0 ||
                _weaponAction?.StartsWith("attack", StringComparison.Ordinal) != true)
                throw new NotSupportedException("Automatic reload lost its discharged process/action owner.");
            if (!_weaponHandling.NeedsAutomaticReload(weapon, request.LoadedBeforeDischarge)) return;
            _ = PlayerReloadSpeed();
            if (weapon.ReloadAnimation is >= 19 and <= 22)
            {
                var insertion = PrepareIncrementalReload(weapon);
                CancelWeaponAction();
                PublishIncrementalReloadClip(insertion, weapon, opening: true);
                _incrementalReload = insertion; _weaponActionShotsAtStart = _shotsFired; _aiming = false;
                GD.Print($"OPENNV_WEAPON_AUTO_RELOAD weapon={weapon.Form} group={weapon.ReloadGroup} incremental=true");
                return;
            }
            var group = weapon.ReloadGroup;
            var clip = _firstPerson.PrepareAction(group);
            var timeline = ActionTimeline(clip, weapon, attack: false);
            if (_thirdPerson is not null) _ = ActionTimeline(_thirdPerson.PrepareAction(group), weapon, attack: false);
            // All source resources are admitted before either ammo or pose
            // publication. The committed discharge is never rolled back.
            _weaponHandling.CompleteReload(weapon);
            CancelWeaponAction();
            _weaponAction = group; _weaponActionClip = clip; _weaponActionKeys = timeline;
            _weaponActionSeconds = 0; _weaponActionHitCount = 0; _weaponActionVariant = null;
            _weaponActionShotsAtStart = _shotsFired; _weaponActionReloadMaterialized = true;
            _weaponActionStartPending = true; _weaponActionLooping = false; _aiming = false;
            _firstPerson.SetAction(group, 0); _thirdPerson?.SetAction(group, 0);
            GD.Print($"OPENNV_WEAPON_AUTO_RELOAD weapon={weapon.Form} group={group}");
        }
        catch (Exception error)
        {
            _weaponActionError = error.Message;
            GD.PushError("OPENNV_WEAPON_AUTO_RELOAD_UNBOUND " + error.Message);
        }
    }
}
