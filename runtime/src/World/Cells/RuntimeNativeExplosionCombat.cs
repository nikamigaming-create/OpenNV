using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Actors;

namespace OpenNV.Runtime.World.Cells;

/// <summary>Applies source EXPL radius hits through the shared actor and player damage owners.</summary>
internal static class RuntimeNativeExplosionCombat
{
    internal static object Detonate(Node3D owner, Node3D shooter, FalloutPluginStack records,
        FalloutExplosion explosion, FalloutWeaponDamage damage, Vector3 point, uint collisionMask,
        float unitsToMeters, FalloutFormKey attacker, int level, FalloutGlobalState globals,
        RuntimeNativePlayer? player, Action<FalloutWeaponDamage, byte>? damagePlayer,
        uint? weaponOnHitBehavior = null, Func<float>? nextWeaponRandomUnit = null)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(shooter);
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        explosion.RequireRuntimeDamageOwner();
        if (!point.IsFinite() || !float.IsFinite(unitsToMeters) || unitsToMeters <= 0 || collisionMask == 0)
            throw new InvalidDataException("Explosion query inputs are invalid.");
        player?.ReceiveExplosionExposure(explosion, point);
        var exposureDone = System.Diagnostics.Stopwatch.GetTimestamp();

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
        var capacity = 256;
        var collisions = space.IntersectShape(parameters, capacity);
        // A dense source cell can overlap thousands of terrain, prop and bone
        // shapes. A full buffer is incomplete, not an unsupported explosion.
        while (collisions.Count == capacity)
        {
            collisions.Clear();
            capacity = checked(capacity * 2);
            collisions = space.IntersectShape(parameters, capacity);
        }
        var queryDone = System.Diagnostics.Stopwatch.GetTimestamp();

        var actorTargets = new Dictionary<RuntimeNativeActorCombat, (Node Collider, float Distance)>();
        var playerTargets = new List<(Node Collider, float Distance)>();
        var rigidTargets = new HashSet<RigidBody3D>();
        var destructibleTargets = new Dictionary<RuntimeNativeDestructible, Node3D>();
        var uniqueColliders = new HashSet<Node3D>();
        foreach (var collision in collisions)
        {
            if (!collision.TryGetValue("collider", out var value) || value.AsGodotObject() is not Node3D target || !uniqueColliders.Add(target))
                continue;
            Node collider = target;
            var distance = target.GlobalPosition.DistanceTo(point);
            if (RuntimeNativeDestructible.Find(collider) is { } destructible)
                destructibleTargets.TryAdd(destructible, target);
            var actor = RuntimeNativeActorCombat.Find(collider);
            if (actor is { CanReceiveExplosionDamage: true } combat)
            {
                if (!actorTargets.TryGetValue(combat, out var prior) || distance < prior.Distance)
                    actorTargets[combat] = (collider, distance);
            }
            else if (player is not null && (collider == player || player.IsAncestorOf(collider)))
                playerTargets.Add((collider, distance));
            if (collider is RigidBody3D { Freeze: false } rigid && actor is null or { Dead: true })
                rigidTargets.Add(rigid);
        }
        var targetsDone = System.Diagnostics.Stopwatch.GetTimestamp();

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
                player.ApplyPlayerExplosionDamage(explosion, damage, part, point,
                    !explosion.PushesSourceOnly || player == shooter || shooter.IsAncestorOf(player), damagePlayer);
            }
        }

        var actorHits = new List<object>();
        foreach (var target in actorTargets.OrderBy(pair => pair.Value.Distance))
        {
            var combat = target.Key;
            var collider = target.Value.Collider;
            if (!explosion.IgnoresLineOfSight && !ClearLineOfSight(space, point,
                ((Node3D)collider).GlobalPosition, collisionMask, shooter, combat)) continue;
            var hit = combat.Hit(collider, damage, attacker, level, globals,
                weaponOnHitBehavior, nextWeaponRandomUnit, explosionDamage: true);
            combat.ApplyExplosionPhysics(explosion, hit, point, !explosion.PushesSourceOnly || shooter.IsAncestorOf(combat));
            actorHits.Add(new
            {
                reference = hit.Reference,
                part = hit.Part,
                distanceMeters = target.Value.Distance,
                damage = hit.HealthDamage,
                healthBefore = hit.HealthBefore,
                healthAfter = hit.HealthAfter,
                knockedDown = combat.KnockedDown,
            });
        }

        var pushedBodies = 0;
        var objectHits = new List<object>();
        foreach (var (destructible, target) in destructibleTargets)
        {
            if (!IsValid(target) || !IsValid(destructible)) continue;
            if (!explosion.IgnoresLineOfSight)
            {
                var excluded = new Godot.Collections.Array<Rid>(CollisionRids((Node3D)destructible.GetParent()).Concat(CollisionRids(shooter)));
                using var ray = PhysicsRayQueryParameters3D.Create(point, target.GlobalPosition, collisionMask, excluded);
                using var obstruction = space.IntersectRay(ray);
                if (obstruction.Count != 0) continue;
            }
            var applied = destructible.Hit(damage.Amount, attacker);
            objectHits.Add(new { damage = applied, state = destructible.Observation });
        }
        foreach (var body in rigidTargets)
        {
            if (!IsValid(body)) continue;
            if (explosion.Force == 0 || explosion.PushesSourceOnly && body != shooter && !shooter.IsAncestorOf(body)) continue;
            var offset = body.GlobalPosition - point;
            if (!explosion.IgnoresLineOfSight)
            {
                using var ray = PhysicsRayQueryParameters3D.Create(point, body.GlobalPosition, collisionMask,
                    new Godot.Collections.Array<Rid> { body.GetRid() });
                using var obstruction = space.IntersectRay(ray);
                if (obstruction.Count != 0) continue;
            }
            body.Sleeping = false;
            body.ApplyCentralImpulse((offset.IsZeroApprox() ? Vector3.Up : offset.Normalized()) * (explosion.Force * unitsToMeters));
            pushedBodies++;
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
            uniqueColliders = uniqueColliders.Count,
            timing = new
            {
                exposureMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(started, exposureDone).TotalMilliseconds,
                queryMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(exposureDone, queryDone).TotalMilliseconds,
                classifyMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(queryDone, targetsDone).TotalMilliseconds,
                applyMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(targetsDone).TotalMilliseconds,
                totalMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            },
            actorHits,
            objectHits,
            pushedBodies,
            playerPart,
            visualUnbound = explosion.HasUnpresentedVisuals,
            boundary = "source EXPL radius, LOS, typed actor/player knockdown, rigid impulses and object damage;distance attenuation, player death, enchantment, placed objects, IPDS projection and retail parity remain open",
        };
    }

    private static bool IsValid(Node node) => GodotObject.IsInstanceValid(node) && node.IsInsideTree() && !node.IsQueuedForDeletion();

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
