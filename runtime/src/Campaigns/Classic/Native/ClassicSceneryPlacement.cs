using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Campaigns.Classic.Native;

internal static class ClassicSceneryPlacement
{
    internal static Aabb MeshBounds(MeshInstance3D mesh, Transform3D? placement = null)
    {
        var transform = placement ?? Transform3D.Identity;
        if (mesh.Skin is not { } skin) return transform * mesh.GetAabb();
        var skeleton = mesh.GetNode<Skeleton3D>(mesh.Skeleton);
        var palette = Enumerable.Range(0, skin.GetBindCount())
            .Select(bind => skeleton.GetBoneGlobalPose(skin.GetBindBone(bind)) * skin.GetBindPose(bind)).ToArray();
        Aabb? bounds = null;
        for (var surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
        {
            var arrays = mesh.Mesh.SurfaceGetArrays(surface);
            var vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var bones = arrays[(int)Mesh.ArrayType.Bones].AsInt32Array();
            var weights = arrays[(int)Mesh.ArrayType.Weights].AsFloat32Array();
            var influences = bones.Length / vertices.Length;
            for (var vertex = 0; vertex < vertices.Length; vertex++)
            {
                var point = Vector3.Zero;
                for (var influence = 0; influence < influences; influence++)
                {
                    var offset = vertex * influences + influence;
                    point += (palette[bones[offset]] * vertices[vertex]) * weights[offset];
                }
                point = transform * point;
                bounds = bounds?.Expand(point) ?? new Aabb(point, Vector3.Zero);
            }
        }
        return bounds ?? throw new InvalidDataException("Classic skin has no posed geometry.");
    }

    internal static Aabb Bounds(Node3D root)
    {
        Aabb? result = null;
        void Visit(Node3D node, Transform3D transform)
        {
            if (!node.Visible) return;
            transform *= node.Transform;
            if (node is MeshInstance3D mesh && mesh.Mesh is not null)
            {
                var bounds = transform * MeshBounds(mesh);
                result = result is null ? bounds : result.Value.Merge(bounds);
            }
            foreach (var child in node.GetChildren().OfType<Node3D>()) Visit(child, transform);
        }
        Visit(root, Transform3D.Identity);
        return result ?? throw new InvalidDataException("Classic scenery model has no mesh bounds.");
    }

    internal static Vector3 ScreenX => new(-16 * MathF.Sqrt(3), 0, 16);
    internal static Vector3 ScreenY(float pixelsPerMeter) => new(4 * MathF.Sqrt(3), -pixelsPerMeter, 12);

    internal static Rect2 Project(Aabb bounds, float pixelsPerMeter, Transform3D? transform = null)
    {
        Vector2 ProjectPoint(Vector3 point)
        {
            point = (transform ?? Transform3D.Identity) * point;
            return new(ScreenX.Dot(point), ScreenY(pixelsPerMeter).Dot(point));
        }
        var result = new Rect2(ProjectPoint(bounds.GetEndpoint(0)), Vector2.Zero);
        for (var corner = 1; corner < 8; corner++) result = result.Expand(ProjectPoint(bounds.GetEndpoint(corner)));
        return result;
    }

    internal static Rect2 Project(Node3D root, float pixelsPerMeter, Transform3D? transform = null)
    {
        Rect2? result = null;
        void Visit(Node3D node, Transform3D parent)
        {
            if (!node.Visible) return;
            var placed = parent * node.Transform;
            if (node is MeshInstance3D mesh && mesh.Mesh is not null)
            {
                // Project each transformed part directly. Projecting a new
                // axis-aligned box around the rotated assembly makes narrow
                // furniture artificially wider and shrinks it to fit the FRM.
                var rectangle = Project(MeshBounds(mesh), pixelsPerMeter, placed);
                result = result?.Merge(rectangle) ?? rectangle;
            }
            foreach (var child in node.GetChildren().OfType<Node3D>()) Visit(child, placed);
        }
        Visit(root, transform ?? Transform3D.Identity);
        return result ?? throw new InvalidDataException("Classic scenery has no projected mesh bounds.");
    }

    internal static Vector3 ArtOffset(Node3D prop, Fallout1NativeFrmFrame frame, float pixelsPerMeter)
    {
        // A physical item is already bottom-centered on its authoritative hex.
        // Its pickup FRM is an icon, not a second ground-placement transform.
        if (prop.HasMeta("physical_item_anchor") && prop.GetMeta("physical_item_anchor").AsBool()) return Vector3.Zero;
        // The original art rectangle is centered at DirectionX, with its
        // bottom at DirectionY. A model centered blindly on the hex drops
        // these offsets (bed5 alone has X=-35), moving the entire prop.
        var bounds = Project(prop, pixelsPerMeter);
        var desiredCenter = new Vector2(frame.DirectionX + frame.FrameX,
            frame.DirectionY + frame.FrameY - frame.Height / 2f);
        var delta = desiredCenter - bounds.GetCenter();
        var world = ClassicMapProjection.World(4816 + delta.X, 11 + delta.Y);
        return new((float)world.X, 0, (float)world.Z);
    }
}
