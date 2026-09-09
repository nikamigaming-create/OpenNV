using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Campaigns.Classic.Native;

/// <summary>Source doorway trim with real thickness and its original transparent openings.</summary>
internal static class ClassicArtExtrusion
{
    internal static Node3D Build(Fallout1NativeFrmFrame frame, Vector3 anchor,
        ClassicBlockoutPolicy policy, Material edgeMaterial, Material faceMaterial, Fallout1NativeFrmFrame? alignmentFrame = null)
    {
        bool Solid(int x, int y) => x >= 0 && y >= 0 && x < frame.Width && y < frame.Height && frame.PaletteIndexes[y * frame.Width + x] != 0;
        var alignment = alignmentFrame ?? frame;
        var bottom = new List<(int X, int Y)>();
        for (var x = 0; x < alignment.Width; x++)
            for (var y = alignment.Height - 1; y >= 0; y--)
                if (alignment.PaletteIndexes[y * alignment.Width + x] != 0) { bottom.Add((x, y)); break; }
        if (bottom.Count < 4) throw new InvalidDataException("Source doorway has no usable contour.");
        var fit = new[] { -0.25f, 0.75f }.Select(slope =>
        {
            var values = bottom.Select(point => point.Y - slope * point.X).Order().ToArray();
            // Posts reach the ground; the bottom of a suspended lintel does
            // not. Use the lower envelope instead of averaging across its hole.
            var intercept = values[values.Length * 19 / 20];
            var support = values.Count(value => Math.Abs(value - intercept) <= policy.WallBaselineTolerancePixels);
            return (Slope: slope, Intercept: intercept, Support: support);
        }).MaxBy(candidate => candidate.Support);
        var left = -frame.Width / 2f + frame.DirectionX + frame.FrameX;
        var top = -frame.Height + frame.DirectionY + frame.FrameY;
        var baselineOffset = -alignment.Height + alignment.DirectionY + alignment.FrameY - top + fit.Slope *
            (left - (-alignment.Width / 2f + alignment.DirectionX + alignment.FrameX));
        Vector3 Point(float x, float y)
        {
            var baseline = fit.Intercept + fit.Slope * x + baselineOffset;
            var ground = ClassicMapProjection.World(4816 + left + x, 11 + top + baseline);
            return new((float)ground.X, (baseline - y) / policy.PixelsPerMeter, (float)ground.Z);
        }
        var along = (Point(1, fit.Intercept + fit.Slope) - Point(0, fit.Intercept)).Normalized();
        var normal = along.Cross(Vector3.Up).Normalized();
        var extrusion = normal * (policy.VaultWallThickness / 2);
        var mesh = new ArrayMesh();
        void Surface(List<Vector3> vertices, List<Vector3> normals, List<Vector2> uv, List<int> indices, Material material)
        {
            using var arrays = new Godot.Collections.Array(); arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray(); arrays[(int)Mesh.ArrayType.Normal] = normals.ToArray();
            arrays[(int)Mesh.ArrayType.TexUV] = uv.ToArray(); arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays); mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, material);
        }
        var vertices = new List<Vector3>(); var normals = new List<Vector3>(); var uv = new List<Vector2>(); var indices = new List<int>();
        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
        {
            var start = vertices.Count; vertices.AddRange([a, b, c, d]); normals.AddRange([outward, outward, outward, outward]);
            uv.AddRange([ua, ub, uc, ud]);
            if ((c - a).Cross(b - a).Dot(outward) > 0) indices.AddRange([start, start + 1, start + 2, start, start + 2, start + 3]);
            else indices.AddRange([start, start + 2, start + 1, start, start + 3, start + 2]);
        }
        foreach (var sign in new[] { -1, 1 })
            Quad(Point(-0.5f, -0.5f) + extrusion * sign, Point(frame.Width - 0.5f, -0.5f) + extrusion * sign,
                Point(frame.Width - 0.5f, frame.Height - 0.5f) + extrusion * sign, Point(-0.5f, frame.Height - 0.5f) + extrusion * sign,
                normal * sign, new(0, 0), new(1, 0), new(1, 1), new(0, 1));
        Surface(vertices, normals, uv, indices, faceMaterial);
        vertices.Clear(); normals.Clear(); uv.Clear(); indices.Clear();
        for (var y = 0; y < frame.Height; y++)
            for (var x = 0; x < frame.Width; x++)
            {
                if (!Solid(x, y)) continue;
                Vector2[] corners = [new(x - 0.5f, y - 0.5f), new(x + 0.5f, y - 0.5f), new(x + 0.5f, y + 0.5f), new(x - 0.5f, y + 0.5f)];
                Vector2I[] neighbors = [new(x, y - 1), new(x + 1, y), new(x, y + 1), new(x - 1, y)];
                for (var side = 0; side < 4; side++)
                {
                    if (Solid(neighbors[side].X, neighbors[side].Y)) continue;
                    var a = Point(corners[side].X, corners[side].Y); var b = Point(corners[(side + 1) % 4].X, corners[(side + 1) % 4].Y);
                    var outward = (Point(neighbors[side].X, neighbors[side].Y) - Point(x, y)).Normalized();
                    Quad(a - extrusion, b - extrusion, b + extrusion, a + extrusion, outward, Vector2.Zero, Vector2.Zero, Vector2.Zero, Vector2.Zero);
                }
            }
        Surface(vertices, normals, uv, indices, edgeMaterial);
        return new MeshInstance3D { Mesh = mesh, Position = anchor };
    }
}
