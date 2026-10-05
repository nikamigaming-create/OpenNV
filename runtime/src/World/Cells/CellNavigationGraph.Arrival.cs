using Godot;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class CellNavigationGraph
{
    // Source destination radius is a region on the reachable authored floor.
    // Native support is still mandatory; this predicate cannot create it.
    internal Func<Vector3, bool> ArrivalRegion(Vector3 start, Vector3 requested, Vector3 selected,
        float radius, float horizontalTolerance, float heightTolerance)
    {
        if (!start.IsFinite() || !requested.IsFinite() || !selected.IsFinite() ||
            !float.IsFinite(radius) || radius <= 0 || !float.IsFinite(horizontalTolerance) || horizontalTolerance < 0 ||
            !float.IsFinite(heightTolerance) || heightTolerance <= 0)
            throw new InvalidDataException("Source arrival region requires finite source floor/radius ownership.");
        var reached = new HashSet<NavigationNode>();
        var queue = new Queue<NavigationNode>(); queue.Enqueue(NearestNode(start).Node);
        while (queue.TryDequeue(out var current))
            if (reached.Add(current)) foreach (var neighbor in Neighbors(current)) queue.Enqueue(neighbor);
        var selectedFloor = NearestNode(selected);
        if (!reached.Contains(selectedFloor.Node) || selected.DistanceSquaredTo(requested) > radius * radius)
            throw new InvalidDataException("Selected source endpoint lies outside its reachable requested region.");
        return point =>
        {
            if (!point.IsFinite() || point.DistanceSquaredTo(requested) > radius * radius) return false;
            var floor = NearestNode(point);
            var horizontal = new Vector2(point.X - floor.Point.X, point.Y - floor.Point.Y);
            return reached.Contains(floor.Node) && horizontal.LengthSquared() <= horizontalTolerance * horizontalTolerance &&
                MathF.Abs(point.Z - floor.Point.Z) < heightTolerance &&
                MathF.Abs(floor.Point.Z - selectedFloor.Point.Z) < heightTolerance;
        };
    }
}
