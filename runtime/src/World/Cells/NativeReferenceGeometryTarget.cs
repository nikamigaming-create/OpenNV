using Godot;

namespace OpenNV.Runtime.World.Cells;

internal sealed record NativeReferenceGeometryObservation(Vector3 Target, Vector3 Aim,
    Aabb Bounds, ulong AimCollider, int AimShape, ulong FloorCollider, int FloorShape);
internal sealed record NativeReferenceSurfaceObservation(Vector3 Aim, Aabb Bounds, ulong Collider, int Shape);

// A bot may approach a presentation, but it cannot replace its authored pivot,
// collision, floor or activation ray. These queries only select a live goal.
internal static class NativeReferenceGeometryTarget
{
    internal static NativeReferenceGeometryObservation Observe(Node3D reference, CharacterBody3D player,
        Vector3 camera, IEnumerable<Rid> excluded, Func<Node, bool> belongsToReference,
        Func<Vector3, bool> resident, float floorReach)
    {
        if (!float.IsFinite(floorReach) || floorReach <= 0)
            throw new NotSupportedException("Reference geometry goal has no finite floor query owner.");
        var surface = ObserveSurface(reference, player, camera, excluded, belongsToReference, resident);
        var merged = surface.Bounds;
        var center = merged.GetCenter();
        var space = player.GetWorld3D().DirectSpaceState;
        // A model can extend below its placed floor. Start at the higher of
        // its bottom and authored pivot, including the controller's real snap
        // interval. The target still comes only from a native floor contact.
        // Source DOOR approaches use their authored pivot and the independent
        // NAVM/capsule floor owner instead of this model-floor projection.
        var floorStart = Math.Max(merged.Position.Y, reference.GlobalPosition.Y) +
            Math.Max(player.SafeMargin, player.FloorSnapLength);
        using var query = PhysicsRayQueryParameters3D.Create(
            new(center.X, floorStart, center.Z),
            new(center.X, floorStart - floorReach, center.Z), player.CollisionMask);
        query.Exclude = new Godot.Collections.Array<Rid>(excluded);
        query.CollideWithAreas = false;
        // Keep the same floor-ray flags as NativeCharacterStep.TrySupport;
        // interaction surface culling is a separate query below.
        using var floor = space.IntersectRay(query);
        if (floor.Count == 0)
            throw new NotSupportedException($"Reference geometry goal has no native floor below its live model: " +
                $"from={query.From} to={query.To} pivot={reference.GlobalPosition} bounds={merged} surface={surface.Aim}.");
        var target = floor["position"].AsVector3(); var normal = floor["normal"].AsVector3();
        if (!target.IsFinite() || !normal.IsFinite() || normal.Dot(Vector3.Up) < MathF.Cos(player.FloorMaxAngle) || !resident(target))
            throw new NotSupportedException("Reference geometry goal has no resident walkable native floor.");
        return new(target, surface.Aim, merged, surface.Collider, surface.Shape,
            checked((ulong)floor["collider_id"].AsInt64()), floor["shape"].AsInt32());
    }

    internal static NativeReferenceSurfaceObservation ObserveSurface(Node3D reference, CharacterBody3D player,
        Vector3 camera, IEnumerable<Rid> excluded, Func<Node, bool> belongsToReference, Func<Vector3, bool> resident)
    {
        if (!reference.IsInsideTree() || !reference.IsVisibleInTree() || !camera.IsFinite() ||
            !reference.GlobalPosition.IsFinite())
            throw new NotSupportedException("Reference geometry goal has no resident finite query owner.");
        var bounds = reference.FindChildren("*", nameof(MeshInstance3D), true, false).OfType<MeshInstance3D>()
            .Where(mesh => mesh.Mesh is not null && mesh.IsVisibleInTree())
            .Select(mesh => mesh.GlobalTransform * mesh.GetAabb()).ToArray();
        if (bounds.Length == 0 || bounds.Any(bound => !bound.Position.IsFinite() || !bound.Size.IsFinite()))
            throw new NotSupportedException("Reference geometry goal has no finite live visible mesh.");
        // Recompute on observation: source object controllers can move a child
        // independently of the REFR wrapper, including after warm reuse.
        var merged = bounds.Aggregate((left, right) => left.Merge(right));
        var center = merged.GetCenter();
        var space = player.GetWorld3D().DirectSpaceState;
        using var query = PhysicsRayQueryParameters3D.Create(camera, center, player.CollisionMask | player.CollisionLayer);
        query.Exclude = new Godot.Collections.Array<Rid>(excluded);
        query.CollideWithAreas = true;
        query.HitBackFaces = false;
        (Vector3 Point, ulong Collider, int Shape)? selected = null;
        bool Pick(Vector3 from, Vector3 to)
        {
            if (from.IsEqualApprox(to)) return false;
            query.From = from; query.To = to;
            using var hit = space.IntersectRay(query);
            if (!hit.TryGetValue("collider", out var value) || value.AsGodotObject() is not Node node ||
                !belongsToReference(node)) return false;
            var point = hit["position"].AsVector3();
            if (!point.IsFinite() || !resident(point)) return false;
            selected = (point, node.GetInstanceId(), hit["shape"].AsInt32());
            return true;
        }
        foreach (var bound in bounds.OrderBy(bound => camera.DistanceSquaredTo(bound.GetCenter())))
            if (Pick(camera, bound.GetCenter())) break;
        if (selected is null)
        {
            // A wall can occlude the present view before ordinary navigation
            // approaches the object. Local rays still require that object's
            // actual pickable native surface; they do not grant activation.
            foreach (var bound in bounds.OrderBy(bound => camera.DistanceSquaredTo(bound.GetCenter())))
            {
                var middle = bound.GetCenter();
                var reach = bound.Size.Length() + player.SafeMargin * 4;
                var directions = new[] { (camera - middle).Normalized(), Vector3.Forward, Vector3.Back,
                    Vector3.Left, Vector3.Right, Vector3.Up, Vector3.Down };
                if (directions.Any(direction => Pick(middle + direction * reach, middle))) break;
            }
        }
        if (selected is not { } surface)
            throw new NotSupportedException("Reference geometry goal has no matching native pickable surface.");
        return new(surface.Point, merged, surface.Collider, surface.Shape);
    }
}
