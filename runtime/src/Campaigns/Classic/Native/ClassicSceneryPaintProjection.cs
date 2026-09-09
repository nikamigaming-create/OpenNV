using Godot;

namespace OpenNV.Runtime.Campaigns.Classic.Native;

/// <summary>
/// Attaches the original view to real mesh faces, with a first-surface depth
/// atlas. Hidden undersides and back faces keep their own material. Everything
/// is transient and object-local; neither camera motion nor MAP translation
/// can move the paint, and no source image is written into a launch cache.
/// </summary>
internal sealed class ClassicSceneryPaintProjection
{
    internal sealed record Part(MeshInstance3D Mesh, Transform3D Meters, Transform3D Source);
    internal IReadOnlyList<Part> Parts { get; }
    internal Vector3 View { get; }
    internal ImageTexture Depth { get; }
    internal float DepthTolerance { get; }
    private readonly Vector3 _screenX;
    private readonly Vector3 _screenY;
    private readonly Vector2 _center;
    private readonly Vector2 _size;

    internal ClassicSceneryPaintProjection(Node3D instance, Transform3D orientation, float pixelsPerMeter,
        int width, int height, float depthTolerancePixels, ImageTexture? sharedDepth = null)
    {
        var parts = new List<Part>();
        void Visit(Node3D node, Transform3D parent)
        {
            var meters = parent * node.Transform;
            if (node is MeshInstance3D mesh && mesh.Mesh is not null)
                parts.Add(new(mesh, meters, orientation * meters));
            foreach (var child in node.GetChildren().OfType<Node3D>()) Visit(child, meters);
        }
        Visit(instance, Transform3D.Identity);
        Parts = parts;
        _screenX = ClassicSceneryPlacement.ScreenX;
        _screenY = ClassicSceneryPlacement.ScreenY(pixelsPerMeter);
        _size = new(width, height);
        _center = ClassicSceneryPlacement.Project(instance, pixelsPerMeter, orientation).GetCenter();
        View = orientation.Basis.Inverse() * _screenX.Cross(_screenY).Normalized();
        DepthTolerance = depthTolerancePixels / Math.Min(_screenX.Length(), _screenY.Length());
        Depth = sharedDepth ?? BuildDepth(width, height);
    }

    internal void Bind(Part part)
    {
        Vector4 Row(Vector3 screen, float size, float center) => new(
            screen.Dot(part.Source.Basis.X) / size, screen.Dot(part.Source.Basis.Y) / size,
            screen.Dot(part.Source.Basis.Z) / size, (screen.Dot(part.Source.Origin) - center) / size + 0.5f);
        var meters = part.Meters;
        part.Mesh.SetInstanceShaderParameter("surface_x", new Vector4(meters.Basis.X.X, meters.Basis.Y.X, meters.Basis.Z.X, meters.Origin.X));
        part.Mesh.SetInstanceShaderParameter("surface_y", new Vector4(meters.Basis.X.Y, meters.Basis.Y.Y, meters.Basis.Z.Y, meters.Origin.Y));
        part.Mesh.SetInstanceShaderParameter("surface_z", new Vector4(meters.Basis.X.Z, meters.Basis.Y.Z, meters.Basis.Z.Z, meters.Origin.Z));
        part.Mesh.SetInstanceShaderParameter("source_u", Row(_screenX, _size.X, _center.X));
        part.Mesh.SetInstanceShaderParameter("source_v", Row(_screenY, _size.Y, _center.Y));
        part.Mesh.SetInstanceShaderParameter("source_view", View);
    }

    private ImageTexture BuildDepth(int width, int height)
    {
        var depth = Enumerable.Repeat(float.MinValue, checked(width * height)).ToArray();
        var canonicalView = _screenX.Cross(_screenY).Normalized();
        Vector3 Project(Vector3 point) => new(_screenX.Dot(point) - _center.X + width / 2f,
            _screenY.Dot(point) - _center.Y + height / 2f, canonicalView.Dot(point));
        foreach (var part in Parts)
            for (var surface = 0; surface < part.Mesh.Mesh.GetSurfaceCount(); surface++)
            {
                if (part.Mesh.Mesh is not ArrayMesh sourceMesh || sourceMesh.SurfaceGetPrimitiveType(surface) != Mesh.PrimitiveType.Triangles)
                    throw new InvalidDataException("Classic source paint requires triangle geometry.");
                var arrays = part.Mesh.Mesh.SurfaceGetArrays(surface);
                var vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                var points = vertices.Select(point => Project(part.Source * point)).ToArray();
                var indices = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
                var count = indices.Length == 0 ? points.Length : indices.Length;
                if (count % 3 != 0) throw new InvalidDataException("Classic source paint triangle indices are incomplete.");
                for (var at = 0; at < count; at += 3)
                {
                    Vector3 Point(int index) => points[indices.Length == 0 ? index : indices[index]];
                    Rasterize(Point(at), Point(at + 1), Point(at + 2), depth, width, height);
                }
            }
        var bytes = new byte[depth.Length * sizeof(float)];
        for (var index = 0; index < depth.Length; index++)
            System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(index * sizeof(float)), depth[index]);
        using var image = Image.CreateFromData(width, height, false, Image.Format.Rf, bytes);
        return ImageTexture.CreateFromImage(image);
    }

    private static void Rasterize(Vector3 a, Vector3 b, Vector3 c, float[] depth, int width, int height)
    {
        static float Edge(Vector3 first, Vector3 second, float x, float y) =>
            (second.X - first.X) * (y - first.Y) - (second.Y - first.Y) * (x - first.X);
        var area = Edge(a, b, c.X, c.Y);
        if (Math.Abs(area) < 1e-7f) return;
        var left = Math.Max(0, (int)Math.Floor(Math.Min(a.X, Math.Min(b.X, c.X))));
        var right = Math.Min(width - 1, (int)Math.Ceiling(Math.Max(a.X, Math.Max(b.X, c.X))));
        var top = Math.Max(0, (int)Math.Floor(Math.Min(a.Y, Math.Min(b.Y, c.Y))));
        var bottom = Math.Min(height - 1, (int)Math.Ceiling(Math.Max(a.Y, Math.Max(b.Y, c.Y))));
        for (var y = top; y <= bottom; y++)
            for (var x = left; x <= right; x++)
            {
                var wa = Edge(b, c, x + 0.5f, y + 0.5f) / area;
                var wb = Edge(c, a, x + 0.5f, y + 0.5f) / area;
                var wc = 1 - wa - wb;
                if (wa < 0 || wb < 0 || wc < 0) continue;
                var sample = wa * a.Z + wb * b.Z + wc * c.Z;
                var index = y * width + x;
                depth[index] = Math.Max(depth[index], sample);
            }
    }
}
