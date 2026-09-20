using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Actors;

namespace OpenNV.Runtime.World.Cells;

/// <summary>Applies source EXPL radius hits through the shared actor and player damage owners.</summary>
internal static class RuntimeNativeExplosionCombat
{
    private const int MaximumCollisionResults = 1024;

    internal static object Detonate(Node3D owner, Node3D shooter, FalloutPluginStack records,
        FalloutExplosion explosion, FalloutWeaponDamage damage, Vector3 point, uint collisionMask,
        float unitsToMeters, FalloutFormKey attacker, int level, FalloutGlobalState globals,
        RuntimeNativePlayer? player, Action<FalloutWeaponDamage, byte>? damagePlayer)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(shooter);
        explosion.RequireRuntimeDamageOwner();
        if (!point.IsFinite() || !float.IsFinite(unitsToMeters) || unitsToMeters <= 0 || collisionMask == 0)
            throw new InvalidDataException("Explosion query inputs are invalid.");

        var radius = explosion.Radius * unitsToMeters;
        if (!float.IsFinite(radius)) throw new InvalidDataException("Explosion radius is not finite.");
        if (radius == 0)
            return new
            {
                explosion = explosion.Form.ToString(),
                radiusMeters = radius,
                candidates = 0,
                actorHits = Array.Empty<object>(),
                playerPart = (byte?)null,
                visualUnbound = explosion.HasUnpresentedVisuals
            };

        using var shape = new SphereShape3D { Radius = radius };
        using var parameters = new PhysicsShapeQueryParameters3D
        {
            Shape = shape,
            Transform = new Transform3D(Basis.Identity, point),
            CollisionMask = collisionMask,
            CollideWithAreas = true,
            CollideWithBodies = true,
        };
        var space = owner.GetWorld3D().DirectSpaceState;
        var collisions = space.IntersectShape(parameters, MaximumCollisionResults);
        if (collisions.Count >= MaximumCollisionResults)
            throw new NotSupportedException("Explosion overlap exceeded the admitted collision result extent.");

        var actorTargets = new Dictionary<RuntimeNativeActorCombat, (Node Collider, float Distance)>();
        var playerTargets = new List<(Node Collider, float Distance)>();
        foreach (var collision in collisions)
        {
            if (!collision.TryGetValue("collider", out var value) || value.AsGodotObject() is not Node collider || collider is not Node3D target)
                continue;
            var distance = target.GlobalPosition.DistanceTo(point);
            if (RuntimeNativeActorCombat.Find(collider) is { CanReceiveExplosionDamage: true } combat)
            {
                if (!actorTargets.TryGetValue(combat, out var prior) || distance < prior.Distance)
                    actorTargets[combat] = (collider, distance);
            }
            else if (player is not null && (collider == player || player.IsAncestorOf(collider)))
                playerTargets.Add((collider, distance));
        }

        byte? playerPart = null;
        if (player is not null && playerTargets.Count != 0)
        {
            var selected = playerTargets.OrderBy(target => target.Distance).First();
            playerPart = explosion.IgnoresLineOfSight
                ? selected.Collider == player ? (byte)0 : player.CombatHitPart(selected.Collider)
                : player.CombatHitPartFrom(point, shooter);
            if (playerPart is { } part)
            {
                if (damagePlayer is null) throw new NotSupportedException("Explosion hit the player without a player damage owner.");
                damagePlayer(damage, part);
            }
        }

        var actorHits = new List<object>();
        foreach (var target in actorTargets.OrderBy(pair => pair.Value.Distance))
        {
            var combat = target.Key;
            var collider = target.Value.Collider;
            if (!explosion.IgnoresLineOfSight && !ClearLineOfSight(space, point,
                ((Node3D)collider).GlobalPosition, collisionMask, shooter, combat)) continue;
            var hit = combat.Hit(collider, damage, attacker, level, globals);
            actorHits.Add(new
            {
                reference = hit.Reference,
                part = hit.Part,
                distanceMeters = target.Value.Distance,
                damage = hit.HealthDamage,
                healthBefore = hit.HealthBefore,
                healthAfter = hit.HealthAfter,
            });
        }

        return new
        {
            explosion = explosion.Form.ToString(),
            center = new[] { point.X, point.Y, point.Z },
            radiusMeters = radius,
            sourceDamage = explosion.Damage,
            appliedDamage = damage.Amount,
            damageDistanceScale = "full inside radius;source attenuation not yet admitted",
            candidates = collisions.Count,
            actorHits,
            playerPart,
            visualUnbound = explosion.HasUnpresentedVisuals,
            boundary = "source EXPL radius and LOS;damage attenuation, force, radiation, enchantment, placed objects, explosion visuals, and retail parity remain open",
        };
    }

    private static bool ClearLineOfSight(PhysicsDirectSpaceState3D space, Vector3 from, Vector3 to,
        uint collisionMask, Node3D shooter, RuntimeNativeActorCombat target)
    {
        if (from.DistanceSquaredTo(to) < 0.000001f) return true;
        var excluded = new HashSet<Rid>(CollisionRids(shooter));
        excluded.UnionWith(target.CollisionRids);
        var rids = new Godot.Collections.Array<Rid>();
        foreach (var rid in excluded) rids.Add(rid);
        using var query = PhysicsRayQueryParameters3D.Create(from, to, collisionMask, rids);
        query.CollideWithAreas = true;
        query.CollideWithBodies = true;
        using var obstruction = space.IntersectRay(query);
        return obstruction.Count == 0;
    }

    private static IEnumerable<Rid> CollisionRids(Node3D root) =>
        root.FindChildren("*", "", true, false).OfType<CollisionObject3D>()
            .Prepend(root as CollisionObject3D).Where(value => value is not null).Select(value => value!.GetRid());
}
