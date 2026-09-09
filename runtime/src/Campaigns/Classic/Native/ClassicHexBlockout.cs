using Godot;
using OpenNV.Runtime.Campaigns.Fallout1;

namespace OpenNV.Runtime.Campaigns.Classic.Native;

/// <summary>Closed joined wall surfaces from source hex occupancy; no placed wall columns.</summary>
internal static class ClassicHexBlockout
{
    private static readonly Vector2I[] CornerOffsets = [new(2, 0), new(1, 1), new(-1, 1), new(-2, 0), new(-1, -1), new(1, -1)];

    internal static MeshInstance3D Build(IReadOnlyDictionary<int, float> walls, Color stone, ClassicWallShape? shape = null, Material? material = null)
    {
        var points = new Dictionary<Vector2I, float>();
        var edgeCounts = new Dictionary<(Vector2I, Vector2I), int>();
        Vector2I Corner(int tile, int index)
        {
            var hex = Fo1HexMath.Coordinate(tile);
            return new Vector2I(hex.X * 3, hex.Y * 2 - (hex.X & 1)) + CornerOffsets[index];
        }
        static (Vector2I, Vector2I) Edge(Vector2I a, Vector2I b) => a.X < b.X || a.X == b.X && a.Y < b.Y ? (a, b) : (b, a);
        foreach (var (tile, height) in walls)
            for (var index = 0; index < 6; index++)
            {
                var a = Corner(tile, index); var b = Corner(tile, (index + 1) % 6);
                points[a] = Math.Max(points.GetValueOrDefault(a), height);
                var edge = Edge(a, b); edgeCounts[edge] = edgeCounts.GetValueOrDefault(edge) + 1;
            }
        var keys = points.Keys.ToArray();
        var lookup = keys.Select((key, index) => (key, index)).ToDictionary(row => row.key, row => row.index);
        var original = keys.Select(key => new Vector2(key.X / (2f * MathF.Sqrt(3)), key.Y / 2f)).ToArray();
        var smooth = original.ToArray();
        var adjacency = Enumerable.Range(0, keys.Length).Select(_ => new HashSet<int>()).ToArray();
        var boundary = Enumerable.Range(0, keys.Length).Select(_ => new HashSet<int>()).ToArray();
        foreach (var (edge, count) in edgeCounts)
        {
            var a = lookup[edge.Item1]; var b = lookup[edge.Item2];
            adjacency[a].Add(b); adjacency[b].Add(a);
            if (count == 1) { boundary[a].Add(b); boundary[b].Add(a); }
        }
        var heights = keys.Select(key => points[key]).ToArray();
        if (shape is not null)
            for (var pass = 0; pass < shape.SmoothPasses; pass++)
            {
                var next = smooth.ToArray(); var nextHeight = heights.ToArray();
                for (var index = 0; index < keys.Length; index++)
                {
                    if (boundary[index].Count == 2)
                    {
                        var average = boundary[index].Aggregate(Vector2.Zero, (sum, neighbor) => sum + smooth[neighbor]) / 2;
                        next[index] = smooth[index].Lerp(average, shape.SmoothStrength);
                        // The construction field still bounds the visible edge.
                        next[index] = original[index] + (next[index] - original[index]).LimitLength(0.22f);
                    }
                    if (adjacency[index].Count > 0)
                        nextHeight[index] = Mathf.Lerp(heights[index], adjacency[index].Average(neighbor => heights[neighbor]), shape.SmoothStrength);
                }
                smooth = next; heights = nextHeight;
            }
        var levels = shape?.Layers ?? 2;
        if (levels < 2) throw new InvalidDataException("Wall shell requires ground and cap levels.");
        var vertices = new Vector3[keys.Length * levels];
        for (var index = 0; index < keys.Length; index++)
        {
            var position = original[index];
            var inward = adjacency[index].Count == 0 ? Vector2.Zero :
                adjacency[index].Aggregate(Vector2.Zero, (sum, neighbor) => sum + original[neighbor]) / adjacency[index].Count - position;
            var outward = boundary[index].Count == 2 ? -inward.Normalized() : Vector2.Zero;
            for (var level = 0; level < levels; level++)
            {
                var fraction = level / (float)(levels - 1);
                var planar = position.Lerp(smooth[index], Math.Min(1, fraction * 4));
                var noise = shape is null ? 0 : MathF.Sin((position.X + position.Y * 0.73f) / shape.NoiseWavelength * Mathf.Tau);
                if (shape is not null) planar += outward * MathF.Sin(fraction * Mathf.Pi) * shape.BulgeMeters * (0.65f + 0.35f * noise);
                var height = heights[index] + (shape?.HeightNoiseMeters ?? 0) * noise;
                vertices[index * levels + level] = new(planar.X, Mathf.Lerp(-0.08f, height, fraction), planar.Y);
            }
        }
        var triangles = new List<int>();
        void Triangle(int a, int b, int c) { triangles.Add(a); triangles.Add(b); triangles.Add(c); }
        foreach (var tile in walls.Keys)
        {
            var corners = Enumerable.Range(0, 6).Select(index => lookup[Corner(tile, index)] * levels).ToArray();
            for (var index = 1; index < 5; index++)
            {
                Triangle(corners[0] + levels - 1, corners[index] + levels - 1, corners[index + 1] + levels - 1);
                Triangle(corners[0], corners[index + 1], corners[index]);
            }
            for (var index = 0; index < 6; index++)
            {
                if (edgeCounts[Edge(Corner(tile, index), Corner(tile, (index + 1) % 6))] != 1) continue;
                var a = corners[index]; var b = corners[(index + 1) % 6];
                for (var level = 0; level < levels - 1; level++)
                { Triangle(a + level, b + level + 1, a + level + 1); Triangle(a + level, b + level, b + level + 1); }
            }
        }
        var normals = new Vector3[vertices.Length];
        for (var index = 0; index < triangles.Count; index += 3)
        {
            var a = triangles[index]; var b = triangles[index + 1]; var c = triangles[index + 2];
            var normal = (vertices[c] - vertices[a]).Cross(vertices[b] - vertices[a]);
            normals[a] += normal; normals[b] += normal; normals[c] += normal;
        }
        for (var index = 0; index < normals.Length; index++) normals[index] = normals[index].Normalized();
        var arrays = new Godot.Collections.Array(); arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.Index] = triangles.ToArray();
        var mesh = new ArrayMesh();
        if (triangles.Count > 0) mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        material ??= new StandardMaterial3D { AlbedoColor = stone, Roughness = 0.96f, CullMode = BaseMaterial3D.CullModeEnum.Back };
        var result = new MeshInstance3D { Name = "SourceJoinedWallBlockout", Mesh = mesh, MaterialOverride = material };
        result.SetMeta("source_wall_hexes", walls.Count);
        result.SetMeta("boundary_edges", edgeCounts.Count(row => row.Value == 1));
        result.SetMeta("presentation", shape is null ? "joined-source-hex-shell" : "molded-source-boundary-with-world-material");
        return result;
    }

    internal static MeshInstance3D Floor(IEnumerable<int> tiles, Material material)
    {
        var vertices = new List<Vector3>(); var indices = new List<int>();
        var corners = new Dictionary<Vector2I, int>();
        foreach (var tile in tiles)
        {
            var coordinate = Fo1HexMath.Coordinate(tile); var center = vertices.Count;
            vertices.Add(Fo1HexMath.Center(tile));
            var ring = new int[6];
            for (var side = 0; side < 6; side++)
            {
                var key = new Vector2I(coordinate.X * 3, coordinate.Y * 2 - (coordinate.X & 1)) + CornerOffsets[side];
                if (!corners.TryGetValue(key, out var index))
                { index = vertices.Count; corners.Add(key, index); vertices.Add(new(key.X / (2f * MathF.Sqrt(3)), 0, key.Y / 2f)); }
                ring[side] = index;
            }
            for (var side = 0; side < 6; side++) { indices.Add(center); indices.Add(ring[side]); indices.Add(ring[(side + 1) % 6]); }
        }
        var arrays = new Godot.Collections.Array(); arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray(); arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = Enumerable.Repeat(Vector3.Up, vertices.Count).ToArray();
        var mesh = new ArrayMesh(); if (indices.Count > 0) mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return new MeshInstance3D { Name = "ClassicContinuousGround", Mesh = mesh, MaterialOverride = material };
    }

    internal static ArrayMesh FloorPatch(Material material)
    {
        var halfWidth = Fo1HexMath.ColumnSpacingMeters;
        Vector3[] vertices = [new(-halfWidth, 0, -1), new(-halfWidth, 0, 1), new(halfWidth, 0, 1), new(halfWidth, 0, -1)];
        Vector2[] uv = [new(0, 0), new(0, 1), new(1, 1), new(1, 0)];
        var arrays = new Godot.Collections.Array(); arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices; arrays[(int)Mesh.ArrayType.TexUV] = uv;
        arrays[(int)Mesh.ArrayType.Normal] = Enumerable.Repeat(Vector3.Up, 4).ToArray();
        arrays[(int)Mesh.ArrayType.Tangent] = new float[] { 1, 0, 0, -1, 1, 0, 0, -1, 1, 0, 0, -1, 1, 0, 0, -1 };
        arrays[(int)Mesh.ArrayType.Index] = new[] { 0, 3, 2, 0, 2, 1 };
        var mesh = new ArrayMesh(); mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays); mesh.SurfaceSetMaterial(0, material);
        return mesh;
    }

    internal static ArrayMesh HexRing(float outer, float inner)
    {
        var vertices = new List<Vector3>(); var indices = new List<int>();
        for (var side = 0; side < 6; side++)
        { vertices.Add(Fo1HexMath.CornerOffset(side, outer)); vertices.Add(Fo1HexMath.CornerOffset(side, inner)); }
        for (var side = 0; side < 6; side++)
        {
            var a = side * 2; var b = (side + 1) % 6 * 2;
            indices.AddRange([a, b, b + 1, a, b + 1, a + 1]);
        }
        var arrays = new Godot.Collections.Array(); arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray(); arrays[(int)Mesh.ArrayType.Normal] = Enumerable.Repeat(Vector3.Up, 12).ToArray();
        arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();
        var mesh = new ArrayMesh(); mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays); return mesh;
    }

    internal static MeshInstance3D Grid(IEnumerable<int> tiles)
    {
        var vertices = new List<Vector3>();
        foreach (var tile in tiles)
        {
            var corners = Fo1HexMath.Corners(tile, 0.98f);
            for (var index = 0; index < 6; index++)
            {
                vertices.Add(corners[index] + Vector3.Up * 0.04f);
                vertices.Add(corners[(index + 1) % 6] + Vector3.Up * 0.04f);
            }
        }
        var arrays = new Godot.Collections.Array(); arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray();
        var mesh = new ArrayMesh();
        if (vertices.Count > 0) mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Lines, arrays);
        return new MeshInstance3D
        {
            Name = "SourceHexGrid",
            Mesh = mesh,
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                AlbedoColor = new Color(0.08f, 0.65f, 0.28f, 0.65f),
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha
            },
        };
    }
}
