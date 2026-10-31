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
            return OnTriangleFloor(triangle, point, horizontalTolerance, heightTolerance);
        };
    }

    private Func<Vector3, bool> ReachableCorridorFloor(NavigationNode start, IReadOnlyList<NavigationNode> path,
        Func<Vector3, bool>? permittedPortal,
        float horizontalTolerance, float heightTolerance)
    {
        if (!float.IsFinite(horizontalTolerance) || horizontalTolerance < 0 ||
            !float.IsFinite(heightTolerance) || heightTolerance <= 0)
            throw new InvalidDataException("Source corridor requires finite authored floor tolerances.");
        var anchors = path.GroupBy(node => node.NavMesh).ToDictionary(group => group.Key, group => group.ToArray());
        HashSet<NavigationNode>? reached = null;
        bool Reachable(NavigationNode target)
        {
            // Reuse the source mesh's existing exact internal reachability
            // fast path from already reached source path anchors. Only off-path
            // meshes or portal-filtered queries need the same
            // directed graph traversal already used by source arrival regions.
            if (permittedPortal is null && anchors.TryGetValue(target.NavMesh, out var entries) &&
                entries.Any(entry => entry.NavMesh.CanReach(entry.TriangleIndex, target.TriangleIndex))) return true;
            if (reached is null)
            {
                reached = [];
                var pending = new Queue<NavigationNode>(); pending.Enqueue(start);
                while (pending.TryDequeue(out var current))
                    if (reached.Add(current))
                        foreach (var next in Neighbors(current))
                            if (permittedPortal?.Invoke(Portal(current, next)) != false) pending.Enqueue(next);
            }
            return reached.Contains(target);
        }
        return point =>
        {
            if (!point.IsFinite()) return false;
            var floor = NearestNode(point).Node;
            return Reachable(floor) && OnTriangleFloor(floor, point, horizontalTolerance, heightTolerance);
        };
    }

    private static bool OnTriangleFloor(NavigationNode triangle, Vector3 point,
        float horizontalTolerance, float heightTolerance)
    {
        var plane = triangle.NavMesh.VerticalPlanePoint(triangle.TriangleIndex, point);
        if (plane is null) return false;
        var projected = triangle.NavMesh.ClosestPoint(triangle.TriangleIndex, plane.Value);
        return new Vector2(point.X - projected.X, point.Y - projected.Y).LengthSquared() <= horizontalTolerance * horizontalTolerance &&
            MathF.Abs(point.Z - projected.Z) < heightTolerance;
    }
}
