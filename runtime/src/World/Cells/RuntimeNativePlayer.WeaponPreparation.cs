using System.Diagnostics;
using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal partial class RuntimeNativePlayer
{
    private void PrepareWeaponResources()
    {
        if (_firstPerson?.Weapon is not { } weapon || IsMeleeWeapon(weapon) || weapon.IsMine) return;
        var started = Stopwatch.GetTimestamp();
        string? error = null;
        try
        {
            _shot = FalloutWeaponShot.Read(_presentationRecords!, weapon.Form,
                _weaponHandling!.Ammunition(weapon), weapon.HasAmmunitionSource);
            _shot = FalloutWeaponModifications.Apply(_presentationRecords!, _shot, _presentationInventory!.WeaponModMask(weapon.Form));
            _shot.RequireRuntimeAttackOwner();
            PrepareAutoAimShapes(_shot);
            _ = PrepareAttackVariants(weapon, weapon.AttackGroup);
            PrepareWeaponMuzzle(_shot.Projectile);
            // Visual-only fixtures may omit the campaign audio owner. Firing
            // still requires it; preparation never invents another one.
            if (_weaponSoundOwner is not null)
            {
                EnsureWeaponSounds();
                _shotEffects ??= new(_presentationRecords!, RuntimeLiveContentSource.Current!, UnitsToMeters, this, CollisionMask, _weaponSounds);
                if (!_shotEffects.IsInsideTree()) AddChild(_shotEffects);
                _shotEffects.PrepareShell(weapon);
                foreach (var sound in weapon.Sounds.Values.Distinct()) _weaponSounds!.PrepareSound(sound);
            }
        }
        catch (Exception failure) when (failure is IOException or InvalidDataException or NotSupportedException or InvalidOperationException)
        {
            error = failure.Message;
            GD.PushError($"OPENNV_WEAPON_RESOURCE_PREPARATION_UNBOUND weapon={weapon.Form}: {error}");
        }
        _weaponResourcePreparation = new
        {
            weapon = weapon.Form.ToString(),
            milliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            error = error ?? _muzzleError,
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
