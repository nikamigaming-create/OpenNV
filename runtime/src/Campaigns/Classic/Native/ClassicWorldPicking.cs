using Godot;

namespace OpenNV.Runtime.Campaigns.Classic.Native;

internal sealed record ClassicWorldPick(int? Serial = null, string? ItemId = null);

/// <summary>Resolve visible static item geometry to its original MAP object, without donor physics.</summary>
internal static class ClassicWorldPicking
{
    internal static ClassicWorldPick? Item(ClassicWorldPreview world, ClassicPlayerSession player, Vector2 point)
    {
        if (!world.ShowModels) return null;
        var eligible = player.Level.Objects.TopLevelObjects.Where(row => (ClassicInventory.IsLootHost(row) || ClassicDoorWorld.IsDoor(row)) &&
            row.Elevation == player.Elevation && (row.Flags & 1) == 0 && !player.Inventory.Taken(player.MapPath, row.Serial))
            .Select(row => row.Serial).ToHashSet();
        var origin = world.Camera.ProjectRayOrigin(point); var direction = world.Camera.ProjectRayNormal(point);
        var ground = player.Inventory.GroundItems.Select(row => row.Id).ToHashSet();
        var nearest = float.PositiveInfinity; ClassicWorldPick? selected = null; Node3D? selectedRoot = null;
        foreach (var root in world.GetChildren().OfType<Node3D>().Where(node => node.HasMeta("source_serial")))
        {
            var serial = root.GetMeta("source_serial").AsInt32();
            var itemId = root.HasMeta("source_item_id") ? root.GetMeta("source_item_id").AsString() : null;
            if (!(itemId is null ? eligible.Contains(serial) : ground.Contains(itemId)) || !root.IsVisibleInTree()) continue;
            foreach (var mesh in root.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>())
            {
                if (Hit(mesh, origin, direction, nearest) is not { } distance) continue;
                nearest = distance; selected = new(itemId is null ? serial : null, itemId); selectedRoot = root;
            }
        }
        if (selected is null) return null;
        // Joined walls do not have individual source serials. Include them in
        // visibility rejection; a ray through a wall cannot select hidden loot.
        foreach (var mesh in world.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>())
        {
            Node? parent = mesh;
            while (parent is not null && parent != world && !parent.HasMeta("source_serial")) parent = parent.GetParent();
            if (parent == selectedRoot) continue;
            if (Hit(mesh, origin, direction, nearest - 0.001f) is not null) return null;
        }
        return selected;
    }

    private static float? Hit(MeshInstance3D mesh, Vector3 origin, Vector3 direction, float nearest)
    {
        if (!mesh.IsVisibleInTree() || (mesh.Layers & (ClassicWorldPreview.ModelLayer | ClassicWorldPreview.SharedLayer)) == 0 ||
            mesh.Mesh is null || mesh.Skin is not null) return null;
        var inverse = mesh.GlobalTransform.AffineInverse();
        var localOrigin = inverse * origin; var localDirection = inverse.Basis * direction;
        if (!Intersects(mesh.GetAabb(), localOrigin, localDirection, nearest)) return null;
        float? result = null;
        var faces = mesh.Mesh.GetFaces();
        for (var index = 0; index + 2 < faces.Length; index += 3)
        {
            var edge1 = faces[index + 1] - faces[index]; var edge2 = faces[index + 2] - faces[index];
            var p = localDirection.Cross(edge2); var determinant = edge1.Dot(p);
            if (Math.Abs(determinant) < 0.0000001f) continue;
            var t = localOrigin - faces[index]; var u = t.Dot(p) / determinant;
            if (u < 0 || u > 1) continue;
            var q = t.Cross(edge1); var v = localDirection.Dot(q) / determinant;
            if (v < 0 || u + v > 1) continue;
            var distance = edge2.Dot(q) / determinant;
            if (distance < 0 || distance >= nearest) continue;
            nearest = distance; result = distance;
        }
        return result;
    }

    private static bool Intersects(Aabb bounds, Vector3 origin, Vector3 direction, float limit)
    {
        var near = 0f; var far = limit;
        for (var axis = 0; axis < 3; axis++)
        {
            if (Math.Abs(direction[axis]) < 0.0000001f)
            { if (origin[axis] < bounds.Position[axis] || origin[axis] > bounds.End[axis]) return false; continue; }
            var first = (bounds.Position[axis] - origin[axis]) / direction[axis];
            var last = (bounds.End[axis] - origin[axis]) / direction[axis];
            near = Math.Max(near, Math.Min(first, last)); far = Math.Min(far, Math.Max(first, last));
            if (near > far) return false;
        }
        return true;
    }
}
