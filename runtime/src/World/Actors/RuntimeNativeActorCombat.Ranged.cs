using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Actors;

internal sealed partial class RuntimeNativeActorCombat
{
    private FalloutWeaponShot? _enemyShot;
    private RuntimeNativeShotEffects? _enemyShotEffects;
    private NativeActorMuzzle? _enemyMuzzle;
    private Godot.Collections.Array<Rid>? _enemyRayExclusions;

    private void ShootPlayer(RuntimeNativePlayer player)
    {
        var weapon = _enemyWeapon!;
        var handling = _enemyWeaponHandling ?? throw new InvalidOperationException("Actor weapon handling is absent.");
        var ammo = _enemyWeaponHandling!.Ammunition(weapon) ?? throw new InvalidOperationException("Actor has no source ammunition.");
        if (_enemyShot?.Ammunition != ammo)
        {
            _enemyShot = FalloutWeaponShot.Read(_records, weapon.Form, ammo);
            _enemyShot.RequireHitscan();
            if (_enemyShot.Projectiles != 1) throw new NotSupportedException("Actor multi-projectile spread requires its shot sampler.");
            var socket = _enemyObject!.Nodes.Single(node => node.GetMeta("opennv_nif_source_name", "").AsString() == "ProjectileNode");
            _enemyMuzzle ??= new(_records, _content, socket, _skeleton.UnitsToMetres);
            _enemyMuzzle.Prepare(_enemyShot.Projectile, encoded: false);
        }
        _enemyShotEffects ??= new(_records, _content, _skeleton.UnitsToMetres, _mover, _mask);
        if (!_enemyShotEffects.IsInsideTree()) _actor.AddChild(_enemyShotEffects);
        _enemyShotEffects.PrepareShell(weapon);
        _enemyRayExclusions ??= new(_actor.FindChildren("*", "", true, false).OfType<CollisionObject3D>()
            .Select(body => body.GetRid()).Append(_mover!.GetRid()));
        var socketPose = _enemyObject!.Socket(_skeleton, "ProjectileNode");
        var from = socketPose.Origin;
        var direction = (player.CombatTargetPoint - from).Normalized();
        using var query = PhysicsRayQueryParameters3D.Create(from, from + direction * (_enemyShot.Projectile.Range * _skeleton.UnitsToMetres), _mask, _enemyRayExclusions);
        query.CollideWithAreas = true;
        using var collision = _actor.GetWorld3D().DirectSpaceState.IntersectRay(query);
        var resolvedDamage = _enemyDamage!.Resolve(_enemyShot);
        if (!handling.ConsumeShot(weapon, _enemyShot, _records)) return;
        (_enemyMuzzle ?? throw new InvalidOperationException("Actor muzzle is absent.")).Flash();
        if (weapon.Sounds.TryGetValue("shoot", out var sound)) _enemySounds!.DispatchSound(sound);
        var before = _context!.Vitals().ExactHitPoints;
        Node? collider = collision.Count == 0 ? null : collision["collider"].AsGodotObject() as Node;
        byte? part = null;
        if (collider == player || collider is not null && player.IsAncestorOf(collider))
        {
            part = collider == player ? (byte)0 : player.CombatHitPart(collider!);
            _context.DamagePlayer(resolvedDamage.Amount, part.Value, resolvedDamage.LimbMultiplier);
            ++_hits;
        }
        _enemyShotEffects.EjectCasing(_enemyObject.Socket(_skeleton, "ShellCasingNode"), player.Camera.GlobalPosition);
        if (collider is not null && _enemyShot.ImpactDataSet is { } set)
        {
            var material = collider == player || player.IsAncestorOf(collider) ? 6 :
                FalloutImpact.MaterialIndex(NativeNifCollisionBuilder.HitMaterial(collision));
            if (FalloutImpact.Resolve(_records, set, material) is { } impact)
                _enemyShotEffects.Impact(impact, collision["position"].AsVector3(), collision["normal"].AsVector3(), direction, collider as Node3D);
        }
        _lastAttack = new { kind = "source-weapon-Hit", weapon = weapon.Form.ToString(), loaded = _enemyWeaponHandling.Loaded(weapon.Form),
            collider = collider?.GetPath().ToString(), part, damage = resolvedDamage.Amount,
            limbDamage = part is null ? (float?)null : resolvedDamage.Amount * resolvedDamage.LimbMultiplier,
            healthBefore = before, healthAfter = _context.Vitals().ExactHitPoints,
            boundary = "stationary-aim-at-source-target;NPC-spread-cover-and-retail-cadence-unmatched" };
        GD.Print($"OPENNV_ACTOR_ATTACK reference={_state.Reference} kind=ranged health={before:R}->{_context.Vitals().ExactHitPoints:R}");
    }
}
