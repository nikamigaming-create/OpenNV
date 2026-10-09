using System.Numerics;

namespace OpenNV.Runtime.Content;

// Actual viewport and Control clip rectangles can become rotated quadrilaterals
// in the input owner's coordinates. Their intersection stays convex.
internal sealed class FalloutMenuDisplayClip
{
    private readonly record struct Edge(Vector2 Start, Vector2 End, double Sign);
    private readonly List<Edge> _edges = [];

    internal FalloutMenuDisplayClip(IReadOnlyList<IReadOnlyList<Vector2>> rectangles)
    {
        if (rectangles.Count == 0) throw new InvalidDataException("Displayed menu has no actual viewport clipping owner.");
        foreach (var rectangle in rectangles)
        {
            if (rectangle.Count != 4) throw new InvalidDataException("Menu clip requires a complete transformed control rectangle.");
            foreach (var point in rectangle) Require(point);
            var cross = Area(rectangle[0], rectangle[1], rectangle[2]);
            if (cross == 0) throw new InvalidDataException("Menu clip rectangle is singular.");
            var sign = cross > 0 ? 1 : -1;
            for (var index = 0; index < 4; index++)
            {
                var a = rectangle[index]; var b = rectangle[(index + 1) % 4]; var c = rectangle[(index + 2) % 4];
                if (Area(a, b, c) * sign <= 0)
                    throw new InvalidDataException("Menu clip rectangle is not convex or has an incomplete extent.");
                _edges.Add(new(a, b, sign));
            }
        }
    }

    internal bool Contains(Vector2 point)
    {
        Require(point);
        return _edges.All(edge => Distance(edge, point) >= 0);
    }

    internal IReadOnlyList<Vector2> Clip(Vector2 a, Vector2 b, Vector2 c)
    {
        Require(a); Require(b); Require(c);
        List<Vector2> polygon = [a, b, c];
        foreach (var edge in _edges)
        {
            var next = new List<Vector2>(polygon.Count + 1);
            var previous = polygon[^1]; var previousDistance = Distance(edge, previous);
            foreach (var current in polygon)
            {
                var currentDistance = Distance(edge, current);
                if ((previousDistance >= 0) != (currentDistance >= 0))
                {
                    var amount = previousDistance / (previousDistance - currentDistance);
                    Add(new((float)(previous.X + ((double)current.X - previous.X) * amount),
                        (float)(previous.Y + ((double)current.Y - previous.Y) * amount)));
                }
                if (currentDistance >= 0) Add(current);
                previous = current; previousDistance = currentDistance;
            }
            if (next.Count > 1 && next[0] == next[^1]) next.RemoveAt(next.Count - 1);
            if (next.Count < 3) return [];
            polygon = next;
            void Add(Vector2 point)
            {
                Require(point);
                if (next.Count == 0 || next[^1] != point) next.Add(point);
            }
        }
        return polygon;
    }

    internal static double Area(Vector2 a, Vector2 b, Vector2 c) =>
        ((double)b.X - a.X) * ((double)c.Y - a.Y) - ((double)b.Y - a.Y) * ((double)c.X - a.X);
    private static double Distance(Edge edge, Vector2 point) =>
        (((double)edge.End.X - edge.Start.X) * ((double)point.Y - edge.Start.Y) -
         ((double)edge.End.Y - edge.Start.Y) * ((double)point.X - edge.Start.X)) * edge.Sign;
    private static void Require(Vector2 point)
    {
        if (!float.IsFinite(point.X) || !float.IsFinite(point.Y)) throw new InvalidDataException("Menu display clipping coordinate is non-finite.");
    }
}
