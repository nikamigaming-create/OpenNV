using Godot;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class CellNavigationGraph
{
    // Portal midpoints guide a route; the destination triangle owns its actual
    // crossing width. This is a source-floor region, never collision clearance.
    private static Func<Vector3, bool> PrefixRegion(Vector3 start, IReadOnlyList<Vector3> path,
        IReadOnlyList<NavigationNode> triangles, Vector3 target, int resume, float horizontalTolerance, float heightTolerance)
    {
        if (!target.IsFinite() || resume < 0 || resume >= triangles.Count ||
            !float.IsFinite(horizontalTolerance) || horizontalTolerance < 0 ||
            !float.IsFinite(heightTolerance) || heightTolerance <= 0 ||
            resume > 0 && target.DistanceTo(path[resume - 1]) > horizontalTolerance)
            throw new InvalidDataException("Source corridor prefix differs from its authored portal or floor.");
        var triangle = triangles[resume];
        var direction = new Vector2(target.X - start.X, target.Y - start.Y).Normalized();
        return point =>
        {
            if (!point.IsFinite()) return false;
            // An eight-metre slice inside the initial triangle must advance
            // past its cut plane. Later prefixes require the next triangle.
            if (resume == 0 && new Vector2(point.X - target.X, point.Y - target.Y).Dot(direction) < -horizontalTolerance)
                return false;
            var plane = triangle.NavMesh.VerticalPlanePoint(triangle.TriangleIndex, point);
            if (plane is null) return false;
            var projected = triangle.NavMesh.ClosestPoint(triangle.TriangleIndex, plane.Value);
            return new Vector2(point.X - projected.X, point.Y - projected.Y).LengthSquared() <= horizontalTolerance * horizontalTolerance &&
                MathF.Abs(point.Z - projected.Z) < heightTolerance;
        };
    }
}
