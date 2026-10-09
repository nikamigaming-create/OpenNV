using System.Numerics;

namespace OpenNV.Runtime.Content;

// Plane normals point out of the camera frustum. This is geometric admission;
// alpha samples, unrelated occluders and final pixel coverage remain separate.
internal readonly record struct FalloutMenuClipPlane(Vector3 Normal, float Distance);
internal readonly record struct FalloutMenuWorldTriangle(Vector3 A, Vector3 B, Vector3 C);
internal readonly record struct FalloutMenuProjectedTriangle(FalloutMenuWorldTriangle World, Vector2 A, Vector2 B, Vector2 C)
{
    internal double SignedArea => ((double)B.X - A.X) * ((double)C.Y - A.Y) - ((double)B.Y - A.Y) * ((double)C.X - A.X);
    internal Vector2 Interior => new((float)(((double)A.X + B.X + C.X) / 3), (float)(((double)A.Y + B.Y + C.Y) / 3));
}
internal enum FalloutMenuTriangleCull { None, Back, Front }

internal sealed class FalloutMenuGeometryProjection
{
    private readonly FalloutMenuClipPlane[] _planes;

    internal FalloutMenuGeometryProjection(IReadOnlyList<FalloutMenuClipPlane> planes)
    {
        if (planes.Count != 6) throw new InvalidDataException("Rendered menu requires the actual six camera planes.");
        _planes = planes.ToArray();
        foreach (var plane in _planes)
        {
            Require(plane.Normal);
            if (!float.IsFinite(plane.Distance) || LengthSquared(plane.Normal) == 0)
                throw new InvalidDataException("Menu camera plane is absent or non-finite.");
        }
    }

    internal bool Contains(Vector3 point)
    {
        Require(point);
        return _planes.All(plane => Distance(plane, point) <= 0);
    }

    internal IReadOnlyList<Vector3> Clip(FalloutMenuWorldTriangle triangle)
    {
        Require(triangle.A); Require(triangle.B); Require(triangle.C);
        // Intersecting a triangle with six half spaces creates at most nine
        // vertices. Extra capacity detects invalid growth instead of truncating.
        Span<Vector3> first = stackalloc Vector3[12];
        Span<Vector3> second = stackalloc Vector3[12];
        first[0] = triangle.A; first[1] = triangle.B; first[2] = triangle.C;
        var count = 3;
        foreach (var plane in _planes)
        {
            var next = 0;
            var previous = first[count - 1];
            var previousDistance = Distance(plane, previous);
            for (var index = 0; index < count; index++)
            {
                var current = first[index]; var currentDistance = Distance(plane, current);
                if ((previousDistance <= 0) != (currentDistance <= 0))
                {
                    var amount = previousDistance / (previousDistance - currentDistance);
                    var intersection = new Vector3(
                        (float)(previous.X + ((double)current.X - previous.X) * amount),
                        (float)(previous.Y + ((double)current.Y - previous.Y) * amount),
                        (float)(previous.Z + ((double)current.Z - previous.Z) * amount));
                    Require(intersection);
                    if (next == 0 || second[next - 1] != intersection)
                    {
                        if (next == second.Length) throw new InvalidDataException("Menu clipping exceeded its convex source extent.");
                        second[next++] = intersection;
                    }
                }
                if (currentDistance <= 0)
                {
                    if (next == 0 || second[next - 1] != current)
                    {
                        if (next == second.Length) throw new InvalidDataException("Menu clipping exceeded its convex source extent.");
                        second[next++] = current;
                    }
                }
                previous = current; previousDistance = currentDistance;
            }
            if (next > 1 && second[0] == second[next - 1]) next--;
            if (next < 3) return [];
            second[..next].CopyTo(first); count = next;
        }
        return first[..count].ToArray();
    }

    // The renderer reverses its culling variant for a reflected instance or
    // reflected camera. Applying both reflections restores the original mode.
    internal static FalloutMenuTriangleCull EffectiveCull(FalloutMenuTriangleCull declared, bool meshMirrored, bool cameraMirrored)
    {
        if (!Enum.IsDefined(declared)) throw new InvalidDataException("Menu triangle has an unowned culling mode.");
        if (meshMirrored == cameraMirrored || declared == FalloutMenuTriangleCull.None) return declared;
        return declared == FalloutMenuTriangleCull.Back ? FalloutMenuTriangleCull.Front : FalloutMenuTriangleCull.Back;
    }

    internal IReadOnlyList<FalloutMenuProjectedTriangle> Project(FalloutMenuWorldTriangle source,
        FalloutMenuTriangleCull cull, Func<Vector3, Vector2> project)
    {
        if (!Enum.IsDefined(cull)) throw new InvalidDataException("Menu triangle has an unowned culling mode.");
        var polygon = Clip(source);
        if (polygon.Count < 3) return [];
        var result = new List<FalloutMenuProjectedTriangle>(polygon.Count - 2);
        var first = Project(polygon[0]);
        for (var index = 1; index < polygon.Count - 1; index++)
        {
            var triangle = new FalloutMenuProjectedTriangle(new(polygon[0], polygon[index], polygon[index + 1]),
                first, Project(polygon[index]), Project(polygon[index + 1]));
            // Render-pixel Y grows down. Godot's front-face winding is clockwise.
            var area = triangle.SignedArea;
            if (area == 0 || cull == FalloutMenuTriangleCull.Back && area < 0 ||
                cull == FalloutMenuTriangleCull.Front && area > 0) continue;
            result.Add(triangle);
        }
        return result;

        Vector2 Project(Vector3 point)
        {
            var value = project(point);
            if (!float.IsFinite(value.X) || !float.IsFinite(value.Y))
                throw new InvalidDataException("Clipped menu triangle produced a non-finite projection.");
            return value;
        }
    }

    internal static bool RayTriangle(Vector3 origin, Vector3 direction, FalloutMenuWorldTriangle triangle, out double distance)
    {
        Require(origin); Require(direction); Require(triangle.A); Require(triangle.B); Require(triangle.C);
        if (LengthSquared(direction) == 0) throw new InvalidDataException("Menu pointer ray has no direction.");
        distance = 0;
        // Use doubles for the determinant and barycentrics, retaining closed
        // shared edges without normalizing a transformed ray's depth domain.
        var first = Sub(triangle.B, triangle.A); var second = Sub(triangle.C, triangle.A);
        var ray = Tuple(direction); var cross = Cross(ray, second); var determinant = Dot(first, cross);
        if (determinant == 0) return false;
        var offset = Sub(origin, triangle.A); var u = Dot(offset, cross) / determinant;
        const double edgeTolerance = 1e-5;
        if (u < -edgeTolerance || u > 1 + edgeTolerance) return false;
        var q = Cross(offset, first); var v = Dot(ray, q) / determinant;
        if (v < -edgeTolerance || u + v > 1 + edgeTolerance) return false;
        distance = Dot(second, q) / determinant;
        return double.IsFinite(distance) && distance >= 0;
    }

    private static double Distance(FalloutMenuClipPlane plane, Vector3 point) =>
        (double)plane.Normal.X * point.X + (double)plane.Normal.Y * point.Y + (double)plane.Normal.Z * point.Z - plane.Distance;
    private static void Require(Vector3 point)
    {
        if (!float.IsFinite(point.X) || !float.IsFinite(point.Y) || !float.IsFinite(point.Z))
            throw new InvalidDataException("Menu geometry contains a non-finite world coordinate.");
    }
    private static double LengthSquared(Vector3 point) => (double)point.X * point.X + (double)point.Y * point.Y + (double)point.Z * point.Z;
    private static (double X, double Y, double Z) Tuple(Vector3 value) => (value.X, value.Y, value.Z);
    private static (double X, double Y, double Z) Sub(Vector3 left, Vector3 right) => ((double)left.X - right.X, (double)left.Y - right.Y, (double)left.Z - right.Z);
    private static (double X, double Y, double Z) Cross((double X, double Y, double Z) left, (double X, double Y, double Z) right) =>
        (left.Y * right.Z - left.Z * right.Y, left.Z * right.X - left.X * right.Z, left.X * right.Y - left.Y * right.X);
    private static double Dot((double X, double Y, double Z) left, (double X, double Y, double Z) right) => left.X * right.X + left.Y * right.Y + left.Z * right.Z;
}
