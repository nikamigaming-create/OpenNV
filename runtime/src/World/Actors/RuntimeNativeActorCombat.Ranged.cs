using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;
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
        var resolvedDamage = _enemyDamage!.Resolve(_enemyShot);
        if (!handling.ConsumeShot(weapon, _enemyShot, _records)) return;
        (_enemyMuzzle ?? throw new InvalidOperationException("Actor muzzle is absent.")).Flash();
        if (weapon.Sounds.TryGetValue("shoot", out var sound)) _enemySounds!.DispatchSound(sound);
        var before = _context!.Vitals().ExactHitPoints;
        Node? lastCollider = null;
        byte? lastPart = null;
        var totalLimbDamage = 0.0f;
        var hits = 0;
        var pellets = new List<object>(_enemyShot.Projectiles);
        for (var pellet = 0; pellet < _enemyShot.Projectiles; pellet++)
        {
            var pelletDirection = FalloutWeaponSpread.Deviate(direction, _enemyShot.MinimumSpread, handling.NextShotRandomUnit);
            using var query = PhysicsRayQueryParameters3D.Create(from,
                from + pelletDirection * (_enemyShot.Projectile.Range * _skeleton.UnitsToMetres), _mask, _enemyRayExclusions);
            query.CollideWithAreas = true;
            using var collision = _actor.GetWorld3D().DirectSpaceState.IntersectRay(query);
            Node? collider = collision.Count == 0 ? null : collision["collider"].AsGodotObject() as Node;
            var point = collision.Count == 0 ? Vector3.Zero : collision["position"].AsVector3();
            var normal = collision.Count == 0 ? Vector3.Zero : collision["normal"].AsVector3();
            byte? part = null;
            float? healthBefore = null;
            float? healthAfter = null;
            if (collider == player || collider is not null && player.IsAncestorOf(collider))
            {
                part = collider == player ? (byte)0 : player.CombatHitPart(collider!);
                healthBefore = _context.Vitals().ExactHitPoints;
                _context.DamagePlayer(resolvedDamage.Amount, part.Value, resolvedDamage.LimbMultiplier);
                healthAfter = _context.Vitals().ExactHitPoints;
                totalLimbDamage += resolvedDamage.Amount * resolvedDamage.LimbMultiplier;
                ++hits;
                ++_hits;
            }
            if (collider is not null && collision.Count > 0 && _enemyShot.ImpactDataSet is { } set)
            {
                var material = collider == player || player.IsAncestorOf(collider)
                    ? 6
                    : FalloutImpact.MaterialIndex(NativeNifCollisionBuilder.HitMaterial(collision));
                if (FalloutImpact.Resolve(_records, set, material) is { } impact)
                    _enemyShotEffects.Impact(impact, point, normal, pelletDirection, collider as Node3D);
            }
            pellets.Add(new
            {
                index = pellet,
                collider = collider?.GetPath().ToString(),
                part,
                point = collision.Count == 0 ? (float[]?)null : new[] { point.X, point.Y, point.Z },
                damage = part is null ? (float?)null : resolvedDamage.Amount,
                limbDamage = part is null ? (float?)null : resolvedDamage.Amount * resolvedDamage.LimbMultiplier,
                healthBefore,
                healthAfter,
                direction = new[] { pelletDirection.X, pelletDirection.Y, pelletDirection.Z }
            });
            if (collider is not null) lastCollider = collider;
            if (part is not null) lastPart = part;
        }
        _enemyShotEffects.EjectCasing(_enemyObject.Socket(_skeleton, "ShellCasingNode"), player.Camera.GlobalPosition);
        var after = _context.Vitals().ExactHitPoints;
        _lastAttack = new
        {
            kind = "source-weapon-Hit",
            weapon = weapon.Form.ToString(),
            loaded = _enemyWeaponHandling.Loaded(weapon.Form),
            collider = lastCollider?.GetPath().ToString(),
            part = lastPart,
            projectiles = _enemyShot.Projectiles,
            hits,
            damage = resolvedDamage.Amount,
            limbDamage = totalLimbDamage,
            healthBefore = before,
            healthAfter = after,
            pellets,
            boundary = "stationary-aim-at-source-target;weapon-spread-skill-modifiers,cover,projectile-flight-and-retail-cadence-unmatched"
        };
        GD.Print($"OPENNV_ACTOR_ATTACK reference={_state.Reference} kind=ranged projectiles={_enemyShot.Projectiles} hits={hits} health={before:R}->{after:R}");
    }
}
