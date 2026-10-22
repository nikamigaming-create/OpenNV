using Godot;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class CellNavigationGraph
{
    private (Vector3 Start, Vector3 End)[]? _openBoundaryEdges;

    // Source-space radius against open NAVM edges. An authored adjacency is
    // not a wall, including an external edge whose other mesh is not resident.
    // This is destination clearance, not actor collision or edge admission.
    internal bool HasWanderBoundaryClearance(Vector3 point, float radius)
    {
        if (!point.IsFinite() || !float.IsFinite(radius) || radius < 0)
            throw new InvalidDataException("Wander clearance requires a finite point and nonnegative radius.");
        var edges = _openBoundaryEdges ??= OpenBoundaryEdges().ToArray();
        var radiusSquared = (double)radius * radius;
        foreach (var (start, end) in edges)
        {
            // The source broad phase selects nearby triangles. Segment bounds
            // provide an equivalent rejection before the full 3D distance.
            if ((double)point.X + radius < Math.Min(start.X, end.X) ||
                (double)point.X - radius > Math.Max(start.X, end.X) ||
                (double)point.Y + radius < Math.Min(start.Y, end.Y) ||
                (double)point.Y - radius > Math.Max(start.Y, end.Y) ||
                (double)point.Z + radius < Math.Min(start.Z, end.Z) ||
                (double)point.Z - radius > Math.Max(start.Z, end.Z)) continue;
            var edge = end - start;
            var lengthSquared = edge.LengthSquared();
            var fraction = lengthSquared > 0 ? Math.Clamp((point - start).Dot(edge) / lengthSquared, 0, 1) : 0;
            var closest = start + edge * fraction;
            // Tangency is allowed; only a strictly interior radius overlaps.
            if (point.DistanceSquaredTo(closest) < radiusSquared) return false;
        }
        return true;
    }

    private IEnumerable<(Vector3 Start, Vector3 End)> OpenBoundaryEdges()
    {
        foreach (var mesh in _navmeshes)
            foreach (var triangle in mesh.Triangles)
                for (var edge = 0; edge < TriangleEdgeCount; edge++)
                    if (triangle.AdjacentTriangles[edge] == NoAdjacentTriangle)
                        yield return (mesh.Vertices[triangle.VertexIndices[edge]],
                            mesh.Vertices[triangle.VertexIndices[(edge + 1) % TriangleEdgeCount]]);
    }
}
