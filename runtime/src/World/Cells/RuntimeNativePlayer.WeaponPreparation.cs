using System.Diagnostics;
using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal partial class RuntimeNativePlayer
{
    private void PrepareWeaponResources()
    {
        if (_firstPerson?.Weapon is not { } weapon || IsMeleeWeapon(weapon) || weapon.IsMine)
        {
            _shotEffects?.PrepareProjectileModel(null);
            return;
        }
        var started = Stopwatch.GetTimestamp();
        string? error = null;
        string? impactPreparationError = null;
        try
        {
            _shot = null;
            if (!weapon.IsNonDischargeAction)
            {
                var prepared = FalloutWeaponShot.Read(_presentationRecords!, weapon.Form,
                    _weaponHandling!.Ammunition(weapon), weapon.HasAmmunitionSource);
                prepared = FalloutWeaponModifications.Apply(_presentationRecords!, prepared, _presentationInventory!.WeaponModMask(weapon.Form));
                prepared.RequireRuntimeAttackOwner();
                _shot = prepared;
                PrepareAutoAimShapes(_shot);
                PrepareWeaponMuzzle(_shot.Projectile);
            }
            _ = PrepareAttackVariants(weapon, weapon.AttackGroup);
            // Visual-only fixtures may omit the campaign audio owner. Firing
            // still requires it; preparation never invents another one.
            if (_weaponSoundOwner is not null)
            {
                EnsureWeaponSounds();
                _shotEffects ??= new(_presentationRecords!, RuntimeLiveContentSource.Current!, UnitsToMeters, this, CollisionMask, _weaponSounds);
                if (!_shotEffects.IsInsideTree()) AddChild(_shotEffects);
                _shotEffects.PrepareProjectileModel(_shot?.Projectile);
                if (!weapon.IsNonDischargeAction) _shotEffects.PrepareShell(weapon);
                if (_shot is { Projectile.Type: 4 } shot) _shotEffects.PrepareBeam(shot.Projectile);
                if (_shot?.ImpactDataSet is { } impactSet)
                {
                    try { _shotEffects.PrepareImpactDataSet(impactSet); }
                    catch (Exception failure) when (failure is IOException or InvalidDataException or NotSupportedException or InvalidOperationException)
                    {
                        impactPreparationError = failure.Message;
                        GD.PushWarning($"OPENNV_IMPACT_PREPARATION_UNBOUND weapon={weapon.Form}: {impactPreparationError}");
                    }
                }
                foreach (var sound in weapon.Sounds.Values.Distinct()) _weaponSounds!.PrepareSound(sound);
            }
        }
        catch (Exception failure) when (failure is IOException or InvalidDataException or NotSupportedException or InvalidOperationException)
        {
            _shotEffects?.PrepareProjectileModel(null);
            error = failure.Message;
            GD.PushError($"OPENNV_WEAPON_RESOURCE_PREPARATION_UNBOUND weapon={weapon.Form}: {error}");
        }
        _weaponResourcePreparation = new
        {
            weapon = weapon.Form.ToString(),
            milliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            error = error ?? _muzzleError,
            impactPreparationError,
            owner = "equipped-source-resources;no-action-selection-or-playback"
        };
    }

    private void PrepareWeaponMuzzle(FalloutProjectile projectile)
    {
        if (_muzzleOwner == _firstPerson!.GetInstanceId() && _muzzleProjectile == projectile.Form) return;
        _muzzleOwner = _firstPerson.GetInstanceId(); _muzzleProjectile = projectile.Form; _muzzleError = null;
        try
        {
            if (_weaponSoundOwner is not null) EnsureWeaponSounds();
            _thirdPerson?.PrepareMuzzle(projectile, _weaponSounds);
            _firstPerson.PrepareMuzzle(projectile, _weaponSounds, _xr is null
                ? path => (_thirdPerson ?? throw new NotSupportedException("Flat muzzle audio has no actual world weapon.")).MuzzleAddonSoundEmitter(path)
                : null);
        }
        catch (Exception error)
        {
            _muzzleError = error.Message;
            GD.PushError("OPENNV_MUZZLE_PRESENTATION_UNBOUND " + error.Message);
        }
    }
}
