using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal static class RuntimeNativeProjectileCollision
{
    internal const int TransparentSmallHavokLayer = 26;
    private const string HavokLayerMetadata = "opennv_collision_havok_layer";
    private const float ContactClearanceMeters = .001f;
    private const int MaximumTransparentContactsPerRay = 128;

    internal static bool TryGetHavokLayer(Node? collider, out int layer)
    {
        for (var node = collider; node is not null; node = node.GetParent())
        {
            if (!node.HasMeta(HavokLayerMetadata)) continue;
            layer = node.GetMeta(HavokLayerMetadata).AsInt32();
            if (layer is < byte.MinValue or > byte.MaxValue)
                throw new InvalidDataException($"Collision {node.GetPath()} has invalid source Havok layer {layer}.");
            return true;
        }

        layer = -1;
        return false;
    }

    internal static Godot.Collections.Dictionary CastThroughSmallTransparent(
        World3D world,
        FalloutProjectile projectile,
        Vector3 from,
        Vector3 to,
        uint collisionMask,
        Godot.Collections.Array<Rid> exclusions,
        out int transparentContactsPassedThrough,
        out int unresolvedLayerContacts,
        out int? terminalHavokLayer)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(projectile);
        ArgumentNullException.ThrowIfNull(exclusions);
        transparentContactsPassedThrough = 0;
        unresolvedLayerContacts = 0;
        terminalHavokLayer = null;
        var displacement = to - from;
        var length = displacement.Length();
        if (!from.IsFinite() || !to.IsFinite() || !float.IsFinite(length) || length <= 0 || collisionMask == 0)
            throw new InvalidDataException("Projectile collision ray is invalid.");
        if (!projectile.PassesThroughSmallTransparent)
            return Cast(from);

        var direction = displacement / length;
        var start = from;
        for (var contactIndex = 0; contactIndex < MaximumTransparentContactsPerRay; contactIndex++)
        {
            var collision = Cast(start);
            if (collision.Count == 0) return collision;
            var collider = collision.TryGetValue("collider", out var value)
                ? value.AsGodotObject() as Node
                : null;
            if (!TryGetHavokLayer(collider, out var layer))
            {
                unresolvedLayerContacts++;
                return collision;
            }

            terminalHavokLayer = layer;
            if (layer != TransparentSmallHavokLayer) return collision;
            if (collider is not CollisionObject3D collisionObject)
            {
                unresolvedLayerContacts++;
                terminalHavokLayer = null;
                return collision;
            }

            var rid = collisionObject.GetRid();
            if (exclusions.Contains(rid))
            {
                unresolvedLayerContacts++;
                return collision;
            }

            var point = collision.TryGetValue("position", out value) ? value.AsVector3() : to;
            collision.Dispose();
            if (!point.IsFinite()) throw new InvalidDataException("Transparent collision point is invalid.");
            transparentContactsPassedThrough++;
            var next = point + direction * ContactClearanceMeters;
            if (!next.IsFinite() || next.DistanceTo(to) >= start.DistanceTo(to))
                return new();
            exclusions.Add(rid);
            start = next;
        }

        throw new InvalidDataException(
            $"Projectile {projectile.Form} exceeded {MaximumTransparentContactsPerRay} transparent collision contacts on one ray.");

        Godot.Collections.Dictionary Cast(Vector3 rayStart)
        {
            using var query = PhysicsRayQueryParameters3D.Create(rayStart, to, collisionMask, exclusions);
            query.CollideWithAreas = true;
            return world.DirectSpaceState.IntersectRay(query);
        }
    }
}
