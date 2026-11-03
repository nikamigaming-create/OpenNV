using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal partial class RuntimeNativePlayer
{
    internal Action<FalloutWeaponShot>? PrepareMineDeployment { get; set; }
    internal Func<FalloutWeaponShot, FalloutWeaponDamage, Transform3D, float, uint>? DeployMine { get; set; }

    private void RequestMineWeaponFire(FalloutWeaponPresentation weapon)
    {
        if (VatsExecuting) throw new NotSupportedException("Mine deployment has no VATS target-action owner.");
        var prepared = FalloutWeaponModifications.Apply(_presentationRecords!,
            FalloutWeaponShot.Read(_presentationRecords!, weapon.Form, null, false), _presentationInventory!.WeaponModMask(weapon.Form));
        (PrepareMineDeployment ?? throw new NotSupportedException("Mine has no persistent world deployment owner."))(prepared);
        _ = PrepareAttackVariants(weapon, weapon.AttackGroup);
        EnsureWeaponSounds();
        foreach (var sound in weapon.Sounds.Values.Distinct()) _weaponSounds!.PrepareSound(sound);
        _shot = prepared;
        _firePreparationError = null;
        RequestWeaponAction(weapon.AttackGroup);
    }

    private void PublishMineRelease(FalloutWeaponPresentation weapon, int pending)
    {
        if (pending != 1) throw new NotSupportedException("Mine source action requires one projectile release event.");
        var shot = _shot ?? throw new InvalidOperationException("Mine source definition is absent.");
        if (shot.Weapon != weapon.Form) throw new InvalidOperationException("Mine changed before its source release.");
        var actor = WeaponActor ?? throw new InvalidOperationException("Mine source attachment is absent.");
        var release = actor.ProjectileTransform();
        if (_xr is null && !_thirdPersonMode)
            release = _camera.GlobalTransform * _firstPerson!.SourceCamera.AffineInverse() * release;
        var speed = shot.Projectile.Speed;
        foreach (var perk in (_combatPerks ?? throw new InvalidOperationException("Mine perk owner is absent."))().Where(perk => perk.Entry == 59))
        {
            perk.RequireActorConditionScope();
            if (perk.Conditions.Count != 0 && !FalloutCondition.AllPass(perk.Conditions,
                _combatCondition ?? throw new NotSupportedException("Mine projectile speed perk has no condition owner."))) continue;
            speed = perk.Function switch
            {
                1 => perk.Value,
                2 => speed + perk.Value,
                3 => speed * perk.Value,
                _ => throw new NotSupportedException("Mine projectile speed perk operation is unbound.")
            };
        }
        var damage = (_damage ?? throw new InvalidOperationException("Mine damage owner is absent."))
            .Resolve(shot, shot.Projectile.ExplosionSource!.Damage);
        var id = (DeployMine ?? throw new NotSupportedException("Mine world owner is absent."))(shot, damage, release, speed);
        _shotsFired++;
        EnsureWeaponSounds();
        if (weapon.Sounds.TryGetValue("shoot", out var sound)) _weaponSounds!.DispatchSound(sound);
        _lastShot = new
        {
            ordinal = _shotsFired,
            weapon = weapon.Form.ToString(),
            projectile = shot.Projectile.Form.ToString(),
            runtimeReference = id,
            origin = new[] { release.Origin.X, release.Origin.Y, release.Origin.Z },
            owner = "persistent-source-PROJ;authored-NIF-body;source-release-event"
        };
    }
}
