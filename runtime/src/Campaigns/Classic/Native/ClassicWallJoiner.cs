using Godot;

namespace OpenNV.Runtime.Campaigns.Classic.Native;

/// <summary>Union of orthogonal source wall footprints, with no internal end caps.</summary>
internal static class ClassicWallJoiner
{
    internal static MeshInstance3D Build(IReadOnlyList<Aabb> bounds, Material material, float tolerance)
    {
        static float[] Coordinates(IEnumerable<float> source, float tolerance)
        {
            var sorted = source.Order().ToArray(); var result = new List<float>();
            for (var index = 0; index < sorted.Length;)
            {
                var end = index + 1;
                while (end < sorted.Length && sorted[end] - sorted[index] <= tolerance) end++;
                result.Add(sorted[index..end].Average()); index = end;
            }
            return result.ToArray();
        }
        var xs = Coordinates(bounds.SelectMany(box => new[] { box.Position.X, box.End.X }), tolerance);
        var zs = Coordinates(bounds.SelectMany(box => new[] { box.Position.Z, box.End.Z }), tolerance);
        var width = xs.Length - 1; var depth = zs.Length - 1;
        var occupied = new bool[width, depth]; var height = bounds.Max(box => box.End.Y);
        int Nearest(float[] values, float value) => Enumerable.Range(0, values.Length).MinBy(index => Math.Abs(values[index] - value));
        foreach (var box in bounds)
        {
            var firstX = Nearest(xs, box.Position.X); var lastX = Nearest(xs, box.End.X);
            var firstZ = Nearest(zs, box.Position.Z); var lastZ = Nearest(zs, box.End.Z);
            for (var x = firstX; x < lastX; x++)
                for (var z = firstZ; z < lastZ; z++) occupied[x, z] = true;
        }
        var vertices = new List<Vector3>(); var normals = new List<Vector3>(); var indices = new List<int>();
        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal)
        {
            var start = vertices.Count; vertices.AddRange([a, b, c, d]);
            normals.AddRange([normal, normal, normal, normal]);
            if ((c - a).Cross(b - a).Dot(normal) > 0)
                indices.AddRange([start, start + 1, start + 2, start, start + 2, start + 3]);
            else indices.AddRange([start, start + 2, start + 1, start, start + 3, start + 2]);
        }
        for (var x = 0; x < width; x++)
            for (var z = 0; z < depth; z++)
            {
                if (!occupied[x, z]) continue;
                var a = new Vector3(xs[x], 0, zs[z]); var b = new Vector3(xs[x + 1], 0, zs[z]);
                var c = new Vector3(xs[x + 1], 0, zs[z + 1]); var d = new Vector3(xs[x], 0, zs[z + 1]);
                var up = Vector3.Up * height;
                Quad(a + up, b + up, c + up, d + up, Vector3.Up); Quad(d, c, b, a, Vector3.Down);
                if (z == 0 || !occupied[x, z - 1]) Quad(a, b, b + up, a + up, Vector3.Forward);
                if (x == width - 1 || !occupied[x + 1, z]) Quad(b, c, c + up, b + up, Vector3.Right);
                if (z == depth - 1 || !occupied[x, z + 1]) Quad(c, d, d + up, c + up, Vector3.Back);
                if (x == 0 || !occupied[x - 1, z]) Quad(d, a, a + up, d + up, Vector3.Left);
            }
        using var arrays = new Godot.Collections.Array(); arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray(); arrays[(int)Mesh.ArrayType.Normal] = normals.ToArray();
        arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();
        var mesh = new ArrayMesh(); mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return new MeshInstance3D { Name = "JoinedSourceVaultWalls", Mesh = mesh, MaterialOverride = material };
    }
}
