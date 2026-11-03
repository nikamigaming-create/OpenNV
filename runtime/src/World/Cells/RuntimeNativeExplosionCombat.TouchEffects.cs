using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Actors;

namespace OpenNV.Runtime.World.Cells;

internal static partial class RuntimeNativeExplosionCombat
{
    private sealed record TouchBody(Node3D Body, RuntimeNativeActorCombat? Combat);

    private static List<Action> PrepareTouchEnchantments(Node3D owner, FalloutPluginStack records,
        FalloutIngestible source, Vector3 point, uint mask, float units, IReadOnlyList<RuntimeNativeActorCombat> actors,
        RuntimeNativePlayer? player, bool playerContact, FalloutFormKey attacker, int level, FalloutGlobalState globals)
    {
        if (player is null) throw new NotSupportedException("Explosion enchantment has no shared value/condition host.");
        var bodies = new Dictionary<FalloutFormKey, TouchBody>();
        foreach (var actor in actors) bodies.Add(actor.TouchEffectTarget, new(actor.TouchEffectBody, actor));
        var playerReference = records.RuntimeFormKey(0x14);
        if (playerContact) bodies.Add(playerReference, new(player, null));
        var direct = bodies.Keys.ToHashSet();
        var area = FalloutTouchEffectContacts.MaximumAreaRadius(records, source) * units;
        if (area > 0 && direct.Count > 0)
        {
            var radius = bodies.Values.Max(body => body.Body.GlobalPosition.DistanceTo(point)) + area;
            if (!float.IsFinite(radius)) throw new InvalidDataException("Explosion magic area exceeds finite space.");
            using var shape = new SphereShape3D { Radius = radius };
            using var query = new PhysicsShapeQueryParameters3D
            {
                Shape = shape,
                Transform = new Transform3D(Basis.Identity, point),
                CollisionMask = mask,
                CollideWithAreas = true,
                CollideWithBodies = true,
            };
            var space = owner.GetWorld3D().DirectSpaceState;
            var capacity = 256;
            var collisions = space.IntersectShape(query, capacity);
            while (collisions.Count == capacity)
            {
                collisions.Clear(); capacity = checked(capacity * 2);
                collisions = space.IntersectShape(query, capacity);
            }
            foreach (var collision in collisions)
            {
                if (!collision.TryGetValue("collider", out var value) || value.AsGodotObject() is not Node collider) continue;
                if (RuntimeNativeActorCombat.Find(collider) is { CanReceiveExplosionDamage: true } combat)
                    bodies.TryAdd(combat.TouchEffectTarget, new(combat.TouchEffectBody, combat));
                else if ((collider == player || player.IsAncestorOf(collider)) && player.IsDefeated?.Invoke() != true)
                    bodies.TryAdd(playerReference, new(player, null));
            }
        }
        var contacts = bodies.Select(pair =>
        {
            var position = pair.Value.Body.GlobalPosition / units;
            return new FalloutTouchEffectContact(pair.Key, position.X, position.Y, position.Z);
        }).ToArray();
        var selected = FalloutTouchEffectContacts.Select(records, source, contacts, direct, (origin, target) =>
        {
            var from = bodies[origin.Reference].Body; var to = bodies[target.Reference].Body;
            if (from.GlobalPosition.DistanceSquaredTo(to.GlobalPosition) < .000001f) return true;
            using var ray = PhysicsRayQueryParameters3D.Create(from.GlobalPosition, to.GlobalPosition, mask,
                new Godot.Collections.Array<Rid>(CollisionRids(from).Concat(CollisionRids(to)).Distinct()));
            ray.CollideWithAreas = true; ray.CollideWithBodies = true;
            using var obstruction = owner.GetWorld3D().DirectSpaceState.IntersectRay(ray);
            return obstruction.Count == 0;
        });
        var result = new List<Action>();
        foreach (var (reference, effects) in selected)
        {
            var delivered = player.SelectDeliveredTouchEffects(reference, attacker, source with { Effects = effects }, areaDelivered: true);
            if (delivered.Effects.Count == 0) continue;
            var amount = FalloutWeaponTouchEffects.HealthDamage(delivered);
            var values = delivered with { Effects = delivered.Effects.Where(effect => effect.ActorValue != 16).ToArray() };
            var apply = values.Effects.Count == 0 ? null : player.PrepareTouchEnchantment(reference, attacker, values);
            var combat = bodies[reference].Combat;
            var health = combat is null ? player.PrepareTouchHealthDamage(amount) : combat.PrepareTouchHealthDamage(amount, attacker, level, globals);
            var hostile = delivered.Effects.Any(effect => (effect.Flags & 1) != 0);
            result.Add(() =>
            {
                health();
                apply?.Invoke();
                if (hostile && combat is { Dead: false }) combat.ReceiveHostileTouch(attacker);
            });
        }
        return result;
    }
}
