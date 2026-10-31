using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal static class RuntimeNativeProjectileImpulse
{
    internal static void Apply(FalloutPluginStack records, FalloutProjectile projectile, Node? collider,
        Vector3 point, Vector3 direction, float unitsToMeters)
    {
        // The actual struck body owns motion. Static and animated/kinematic
        // attachments never become dynamic merely because a projectile hit.
        if (collider is not RigidBody3D body || projectile.ImpactForce == 0) return;
        if (!GodotObject.IsInstanceValid(body) || !body.IsInsideTree() || body.IsQueuedForDeletion())
            throw new InvalidOperationException("Projectile impulse target left the scene.");
        if (body.Freeze) return;
        if (!point.IsFinite() || !direction.IsFinite() || !float.IsFinite(unitsToMeters) || unitsToMeters <= 0)
            throw new InvalidDataException("Projectile impulse contact is invalid.");
        if (!body.HasMeta("opennv_collision_havok_layer"))
            throw new NotSupportedException("Projectile rigid contact has no source Havok layer.");
        var layer = body.GetMeta("opennv_collision_havok_layer").AsInt32();
        var force = FalloutGameplayImpulse.Resolve(records, projectile.ImpactForce, body.Mass, layer);
        if (force == 0) return;
        // Same Havok distance conversion as source collision construction;
        // impulse has mass * distance / time units. Godot integrates mass.
        var impulse = direction.Normalized() * (force * 7 * unitsToMeters);
        if (!impulse.IsFinite()) throw new InvalidDataException("Projectile impulse conversion is not finite.");
        body.Sleeping = false;
        body.ApplyImpulse(impulse, point - body.GlobalPosition);
        GD.Print($"OPENNV_PROJECTILE_IMPULSE projectile={projectile.Form} body={body.Name} havokLayer={layer} " +
            $"sourceForce={projectile.ImpactForce:R} mass={body.Mass:R} havokImpulse={force:R} impulse={impulse} point={point}");
    }
}
