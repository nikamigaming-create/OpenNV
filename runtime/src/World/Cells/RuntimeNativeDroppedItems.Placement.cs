using Godot;
using OpenNV.Runtime.SceneGraph;

namespace OpenNV.Runtime.World.Cells;

internal partial class RuntimeNativeDroppedItems
{
    internal readonly record struct DropOrigin(Vector3 Floor, Vector3 Forward, float CapsuleRadius, float CenterHeight, Rid Player);

    private static Transform3D RelativePose(Node3D root, Node3D child)
    {
        var pose = Transform3D.Identity;
        for (Node3D? current = child; current is not null && current != root; current = current.GetParent() as Node3D)
            pose = current.Transform * pose;
        return pose;
    }

    private void PlaceClearOfPlayer(Node3D root, DropOrigin origin)
    {
        var shapes = NodeTraversal.Descendants<CollisionShape3D>(root).Where(shape => !shape.Disabled && shape.Shape is not null)
            .Select(shape => (Shape: shape.Shape!, Pose: RelativePose(root, shape),
                Mask: (shape.GetParent() as CollisionObject3D)?.CollisionMask ?? 1u)).ToArray();
        if (shapes.Length == 0) throw new InvalidDataException("Dropped model has no physical placement bounds.");
        Aabb? bounds = null;
        foreach (var shape in shapes)
        {
            var local = shape.Shape.GetDebugMesh().GetAabb();
            for (var corner = 0; corner < 8; corner++)
            {
                var point = shape.Pose * local.GetEndpoint(corner);
                bounds = bounds is { } previous ? previous.Expand(point) : new Aabb(point, Vector3.Zero);
            }
        }
        var box = bounds!.Value;
        var half = box.Size * .5f;
        var center = box.GetCenter();
        var radius = new Vector2(half.X, half.Z).Length();
        var clearance = Math.Max(.005f, radius * .02f);
        var distance = origin.CapsuleRadius + radius + clearance;
        var floorCenter = origin.Floor + Vector3.Up * Math.Max(origin.CenterHeight, half.Y + clearance);
        var forward = new Vector3(origin.Forward.X, 0, origin.Forward.Z).Normalized();
        var space = GetWorld3D().DirectSpaceState;
        var exclude = new Godot.Collections.Array<Rid> { origin.Player };
        foreach (var turn in new[] { 0, 1, -1, 2, -2, 3, -3, 4 })
        {
            var target = floorCenter + forward.Rotated(Vector3.Up, turn * MathF.PI / 4) * distance;
            using var ray = PhysicsRayQueryParameters3D.Create(floorCenter, target, shapes.Aggregate(0u, (mask, shape) => mask | shape.Mask), exclude);
            if (space.IntersectRay(ray).Count != 0) continue;
            var pose = new Transform3D(root.Basis, target - center);
            var blocked = false;
            foreach (var shape in shapes)
            {
                using var query = new PhysicsShapeQueryParameters3D
                {
                    Shape = shape.Shape,
                    Transform = pose * shape.Pose,
                    CollisionMask = shape.Mask,
                    CollideWithAreas = false,
                    CollideWithBodies = true,
                    Exclude = exclude,
                    Margin = clearance
                };
                if (space.IntersectShape(query, 1).Count == 0) continue;
                blocked = true; break;
            }
            if (blocked) continue;
            root.Transform = pose;
            return;
        }
        throw new InvalidOperationException("There is no collision-free space beside the player to drop this object.");
    }

    internal object State => new
    {
        items = world.DroppedItems.Select(item => new
        {
            identity = item.Identity,
            sourceBase = item.Base.ToString(),
            count = item.Item.Count,
            item.Item.WeaponMods,
            item.Item.Instance,
            item.Item.Magazine,
            cell = item.Placement.Cell.ToString(),
            position = item.Placement.Position,
            resident = _physical.ContainsKey(item.Identity),
            error = _errors.GetValueOrDefault(item.Identity),
            bodies = _physical.TryGetValue(item.Identity, out var owner) ? owner.Bodies.Select(body => new
            {
                body = body.SourceBody,
                position = new[] { body.GlobalPosition.X, body.GlobalPosition.Y, body.GlobalPosition.Z },
                velocity = new[] { body.LinearVelocity.X, body.LinearVelocity.Y, body.LinearVelocity.Z },
                body.Sleeping,
                visual = body.GetParent<Node3D>() is { } visual ? new[] { visual.GlobalPosition.X, visual.GlobalPosition.Y, visual.GlobalPosition.Z } : null
            }).ToArray() : null
        }).ToArray()
    };
}
