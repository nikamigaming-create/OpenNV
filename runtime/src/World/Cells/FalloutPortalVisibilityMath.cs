using V3 = System.Numerics.Vector3;

namespace OpenNV.Runtime.World.Cells;

internal readonly record struct FalloutVisibilityPlane(V3 Normal, float Distance)
{
    internal float Signed(V3 point) => V3.Dot(Normal, point) - Distance;
    internal FalloutVisibilityPlane Reversed => new(-Normal, -Distance);
    internal void Validate()
    {
        if (!FalloutPortalVisibilityMath.Finite(Normal) || !float.IsFinite(Distance) || Normal.LengthSquared() == 0)
            throw new InvalidDataException("Visibility plane has no finite nonzero normal.");
    }
    internal static FalloutVisibilityPlane Through(V3 first, V3 second, V3 third, V3 inside)
    {
        var cross = V3.Cross(second - first, third - first);
        if (!FalloutPortalVisibilityMath.Finite(cross) || cross.LengthSquared() == 0)
            throw new NotSupportedException("Source visibility plane has degenerate camera/rectangle geometry.");
        var normal = V3.Normalize(cross);
        var plane = new FalloutVisibilityPlane(normal, V3.Dot(normal, first));
        return plane.Signed(inside) < 0 ? plane.Reversed : plane;
    }
}

internal sealed record FalloutVisibilitySphere(V3 Center, float Radius)
{
    internal void Validate()
    {
        if (!FalloutPortalVisibilityMath.Finite(Center) || !float.IsFinite(Radius) || Radius < 0)
            throw new InvalidDataException("Native visibility bound is not finite.");
    }
}

internal sealed record FalloutVisibilityBound(V3 Center, V3 X, V3 Y, V3 Z, V3 Half, bool Sphere)
{
    internal void Validate()
    {
        if (!FalloutPortalVisibilityMath.Finite(Center) || !FalloutPortalVisibilityMath.Finite(Half) ||
            Half.X <= 0 || !Sphere && (Half.Y <= 0 || Half.Z <= 0) ||
            !FalloutPortalVisibilityMath.Finite(X) || !FalloutPortalVisibilityMath.Finite(Y) ||
            !FalloutPortalVisibilityMath.Finite(Z) || X.LengthSquared() == 0 || Y.LengthSquared() == 0 || Z.LengthSquared() == 0)
            throw new InvalidDataException("Source room bound is invalid.");
    }
    internal bool Contains(V3 point)
    {
        Validate();
        var offset = point - Center;
        if (Sphere) return offset.LengthSquared() <= Half.X * Half.X;
        return MathF.Abs(V3.Dot(offset, X)) <= Half.X && MathF.Abs(V3.Dot(offset, Y)) <= Half.Y &&
            MathF.Abs(V3.Dot(offset, Z)) <= Half.Z;
    }
    // The source stores a bounding sphere for scene-node culling. The native
    // adapter supplies the current native bound, rather than an editor pivot.
    internal bool Contains(FalloutVisibilitySphere sphere)
    {
        Validate(); sphere.Validate();
        if (Sphere) return (sphere.Center - Center).Length() + sphere.Radius <= Half.X;
        var offset = sphere.Center - Center;
        return MathF.Abs(V3.Dot(offset, X)) + sphere.Radius <= Half.X &&
            MathF.Abs(V3.Dot(offset, Y)) + sphere.Radius <= Half.Y &&
            MathF.Abs(V3.Dot(offset, Z)) + sphere.Radius <= Half.Z;
    }
}

internal sealed class FalloutVisibilityWindow(IReadOnlyList<FalloutVisibilityPlane> planes)
{
    internal IReadOnlyList<FalloutVisibilityPlane> Planes { get; } = Array.AsReadOnly(planes.ToArray());
    internal bool Sees(FalloutVisibilitySphere sphere)
    {
        sphere.Validate();
        foreach (var plane in Planes)
        {
            plane.Validate();
            if (plane.Signed(sphere.Center) < -sphere.Radius * plane.Normal.Length()) return false;
        }
        return true;
    }
    internal V3[] Clip(IReadOnlyList<V3> vertices)
    {
        if (vertices.Count < 3 || vertices.Any(point => !FalloutPortalVisibilityMath.Finite(point)))
            throw new InvalidDataException("Source visibility polygon is invalid.");
        var polygon = vertices.ToList();
        foreach (var plane in Planes)
        {
            plane.Validate();
            if (polygon.Count == 0) break;
            var output = new List<V3>();
            var previous = polygon[^1]; var previousDistance = plane.Signed(previous);
            foreach (var point in polygon)
            {
                var distance = plane.Signed(point);
                if ((distance >= 0) != (previousDistance >= 0))
                {
                    var fraction = previousDistance / (previousDistance - distance);
                    var intersection = previous + (point - previous) * fraction;
                    if (!FalloutPortalVisibilityMath.Finite(intersection))
                        throw new InvalidDataException("Visibility clipping produced a nonfinite source intersection.");
                    output.Add(intersection);
                }
                if (distance >= 0) output.Add(point);
                previous = point; previousDistance = distance;
            }
            polygon = output;
        }
        return polygon.Count < 3 ? [] : polygon.ToArray();
    }
    internal FalloutVisibilityWindow Intersect(IReadOnlyList<FalloutVisibilityPlane> other) => new(Planes.Concat(other).ToArray());
}

internal static class FalloutPortalVisibilityMath
{
    internal static bool Finite(V3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    internal static FalloutVisibilityPlane[] Cone(V3 camera, IReadOnlyList<V3> rectangle, float coplanarDistance)
    {
        if (!Finite(camera) || rectangle.Count != 4 || rectangle.Any(point => !Finite(point)) ||
            !float.IsFinite(coplanarDistance) || coplanarDistance < 0)
            throw new InvalidDataException("Portal window has no valid current camera/source rectangle.");
        var center = (rectangle[0] + rectangle[1] + rectangle[2] + rectangle[3]) * .25f;
        var normal = V3.Cross(rectangle[1] - rectangle[0], rectangle[2] - rectangle[0]);
        if (normal.LengthSquared() == 0 || !Finite(normal))
            throw new InvalidDataException("Source portal rectangle is degenerate.");
        normal = V3.Normalize(normal);
        if (MathF.Abs(V3.Dot(normal, camera - rectangle[0])) <= coplanarDistance) return [];
        // One rectangle plane and four camera-edge planes are active. The
        // opposite rectangle plane is absent; the source does not make a slab.
        var inside = center + V3.Normalize(center - camera);
        var result = new List<FalloutVisibilityPlane>
        { FalloutVisibilityPlane.Through(rectangle[0], rectangle[1], rectangle[2], inside) };
        for (var edge = 0; edge < rectangle.Count; edge++)
            result.Add(FalloutVisibilityPlane.Through(camera, rectangle[edge], rectangle[(edge + 1) % 4], inside));
        return result.ToArray();
    }
}
