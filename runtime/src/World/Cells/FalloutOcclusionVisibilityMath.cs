using OpenNV.Runtime.Content;
using V3 = System.Numerics.Vector3;

namespace OpenNV.Runtime.World.Cells;

internal enum FalloutOcclusionCullMode { Automatic, Always }
internal sealed record FalloutOcclusionGeometry(FalloutFormKey Reference, bool Enabled,
    FalloutOcclusionCullMode Mode, IReadOnlyList<V3> Corners);

// Slots are the original six-plane slots. Slot one (the opposite rectangle
// plane) is inactive; the source creates a shadow volume, not a solid slab.
internal sealed record FalloutOcclusionCone(IReadOnlyList<FalloutVisibilityPlane?> Planes, int CameraSide)
{
    internal bool Contains(FalloutVisibilitySphere sphere)
    {
        sphere.Validate();
        if (Planes.Count != 6 || Planes[0] is null || Planes[1] is not null)
            throw new InvalidDataException("Source occlusion volume lost its physical plane slots.");
        foreach (var plane in Planes)
            if (plane is { } active)
            {
                active.Validate();
                if (active.Signed(sphere.Center) < sphere.Radius * active.Normal.Length()) return false;
            }
        return true;
    }
    internal FalloutOcclusionCone Without(int physicalPlane)
    {
        if (physicalPlane is < 2 or > 5)
            throw new InvalidDataException("Linked plane attempted to drop a non-side boundary.");
        var copy = Planes.ToArray(); copy[physicalPlane] = null;
        return new(Array.AsReadOnly(copy), CameraSide);
    }
}

internal abstract record FalloutOcclusionExpression
{
    internal abstract bool Sees(FalloutVisibilitySphere sphere);
    internal sealed record Negative(FalloutOcclusionCone Cone) : FalloutOcclusionExpression
    {
        internal override bool Sees(FalloutVisibilitySphere sphere) => !Cone.Contains(sphere);
    }
    internal sealed record All(IReadOnlyList<FalloutOcclusionExpression> Children) : FalloutOcclusionExpression
    {
        internal override bool Sees(FalloutVisibilitySphere sphere) => Children.All(child => child.Sees(sphere));
    }
    internal sealed record Any(IReadOnlyList<FalloutOcclusionExpression> Children) : FalloutOcclusionExpression
    {
        internal override bool Sees(FalloutVisibilitySphere sphere) => Children.Any(child => child.Sees(sphere));
    }
}

internal static class FalloutOcclusionVisibilityMath
{
    internal static FalloutOcclusionCone? Observe(FalloutOcclusionGeometry geometry,
        FalloutRoomCamera camera, float coplanarDistance)
    {
        var corners = geometry.Corners;
        if (corners is null || corners.Count != 4 || corners.Any(point => !FalloutPortalVisibilityMath.Finite(point)) ||
            !FalloutPortalVisibilityMath.Finite(camera.Origin) || camera.Frustum.Count != 6 ||
            !float.IsFinite(coplanarDistance) || coplanarDistance < 0)
            throw new InvalidDataException("Occlusion observation lost its actual rectangle/camera/frustum.");
        foreach (var plane in camera.Frustum) plane.Validate();
        if (!geometry.Enabled) return null;
        if (geometry.Mode is not (FalloutOcclusionCullMode.Automatic or FalloutOcclusionCullMode.Always))
            throw new NotSupportedException("Source occlusion culling mode has no actual producer.");
        var center = (corners[0] + corners[1] + corners[2] + corners[3]) * .25f;
        var normal = V3.Cross(corners[1] - corners[0], corners[2] - corners[0]);
        if (!FalloutPortalVisibilityMath.Finite(normal) || normal.LengthSquared() == 0)
            throw new InvalidDataException("Source occlusion rectangle cannot construct its actual plane.");
        normal = V3.Normalize(normal);
        var rectangle = new FalloutVisibilityPlane(normal, V3.Dot(normal, corners[0]));
        var distance = rectangle.Signed(camera.Origin);
        var outside = new uint[4];
        for (var ordinal = 0; ordinal < camera.Frustum.Count; ordinal++)
        {
            for (var corner = 0; corner < corners.Count; corner++)
                if (camera.Frustum[ordinal].Signed(corners[corner]) < 0) outside[corner] |= 1u << ordinal;
        }
        if (geometry.Mode == FalloutOcclusionCullMode.Automatic)
        {
            // Source near/coplanar branch uses the current near halfspace and
            // the source rectangle plane, not a guessed readiness flag.
            var nearDistance = camera.Frustum[0].Signed(camera.Origin) + distance;
            var projected = camera.Origin - normal * distance;
            if (nearDistance >= -coplanarDistance && nearDistance < coplanarDistance && InsideRectangle(projected, corners, normal))
                return null;
            if ((outside[0] & outside[1] & outside[2] & outside[3]) != 0 ||
                new FalloutVisibilityWindow(camera.Frustum).Clip(corners).Length < 3) return null;
        }
        if (distance == 0)
            throw new NotSupportedException("Coplanar source occlusion edge view has no finite camera-cone consumer.");
        // A point beyond the rectangle on its center ray is inside every
        // camera edge halfspace even for an oblique, off-center camera.
        var inside = center + (center - camera.Origin);
        if (!FalloutPortalVisibilityMath.Finite(inside))
            throw new InvalidDataException("Source occlusion interior point is not finite.");
        FalloutVisibilityPlane?[] planes = new FalloutVisibilityPlane?[6];
        planes[0] = FalloutVisibilityPlane.Through(corners[0], corners[1], corners[2], inside);
        // These physical pairs also select the original edge-clipping masks.
        (int First, int Second)[] edges = [(0, 1), (2, 3), (0, 3), (1, 2)];
        for (var edge = 0; edge < edges.Length; edge++)
        {
            var pair = edges[edge];
            if ((outside[pair.First] & outside[pair.Second]) != 0) continue;
            planes[edge + 2] = FalloutVisibilityPlane.Through(camera.Origin, corners[pair.First], corners[pair.Second], inside);
        }
        return new(Array.AsReadOnly(planes), distance < 0 ? -1 : 1);
    }

    private static bool InsideRectangle(V3 point, IReadOnlyList<V3> corners, V3 normal)
    {
        for (var edge = 0; edge < corners.Count; edge++)
        {
            var first = corners[edge]; var second = corners[(edge + 1) % corners.Count];
            var inward = V3.Cross(normal, second - first);
            if (V3.Dot(inward, point - first) < 0) return false;
        }
        return true;
    }

    internal static FalloutOcclusionExpression? Link(FalloutDynamicPrimitive source,
        FalloutOcclusionCone own, IReadOnlyDictionary<FalloutFormKey, FalloutOcclusionCone> current,
        IReadOnlyDictionary<FalloutFormKey, FalloutOcclusionGeometry> geometries)
    {
        if (source.OrderedOcclusionPlanes.Count == 0) return new FalloutOcclusionExpression.Negative(own);
        if (source.OrderedOcclusionPlanes.Count != 4)
            throw new InvalidDataException("Source occlusion link stream lost its four physical slots.");
        var links = source.OrderedOcclusionPlanes;
        var leading = links[0] ?? links[2];
        var trailing = links[1] ?? links[3];
        var paired = leading ?? trailing;
        if (paired is not { } key || !current.TryGetValue(key, out var other) ||
            geometries[key].Mode != FalloutOcclusionCullMode.Always || own.CameraSide != other.CameraSide)
            return new FalloutOcclusionExpression.Negative(own);
        // An actual leading-side member delegates the paired compound to its
        // trailing-side member. This is the source link direction, not a
        // location-dependent duplicate removal or an arbitrary identity sort.
        if (leading is not null) return null;
        var parentSide = links[1] is not null ? 2 : 3;
        var childSide = links[1] is not null ? 3 : 4;
        return new FalloutOcclusionExpression.All(Array.AsReadOnly(new FalloutOcclusionExpression[]
        {
            new FalloutOcclusionExpression.Negative(own),
            new FalloutOcclusionExpression.Negative(other),
            new FalloutOcclusionExpression.Any(Array.AsReadOnly(new FalloutOcclusionExpression[]
            {
                new FalloutOcclusionExpression.Negative(own.Without(parentSide)),
                new FalloutOcclusionExpression.Negative(other.Without(childSide))
            }))
        }));
    }
}
