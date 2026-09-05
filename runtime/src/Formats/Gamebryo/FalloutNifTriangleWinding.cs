namespace OpenNV.Runtime.Formats.Gamebryo;

internal static class FalloutNifTriangleWinding
{
    internal static int[] ToGodotIndices(IReadOnlyList<FalloutNifTriangle> triangles)
    {
        var indices = new List<int>(checked(triangles.Count * 3));
        foreach (var triangle in triangles)
        {
            if (triangle.A == triangle.B || triangle.B == triangle.C || triangle.C == triangle.A)
                continue;
            // NIF front faces are counterclockwise. The (x,z,-y) coordinate
            // conversion preserves handedness; Godot needs clockwise indices.
            // Keep source normals, UVs, skin weights, and vertex order unchanged.
            indices.Add(triangle.A);
            indices.Add(triangle.C);
            indices.Add(triangle.B);
        }
        return indices.ToArray();
    }
}
