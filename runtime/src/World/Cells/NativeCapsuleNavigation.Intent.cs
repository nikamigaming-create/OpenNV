using Godot;

namespace OpenNV.Runtime.World.Cells;

internal sealed record NativeNavigationIntent(Vector3 Target, int Resume, float ArrivalRadius,
    bool ReferenceApproach, IReadOnlyList<Vector3> Corridor, NativeNavigationProjectionRegion? Projection = null);

internal sealed record NativeNavigationProjectionRegion(Vector3 Requested, Vector3 Selected, float Radius);

internal static partial class NativeCapsuleNavigation
{
    // A cut within the first source triangle may use its forward region. A
    // later region belongs to its actual preceding portal, not an interpolated
    // point in the following segment.
    internal static bool CanUsePrefixRegion(Vector3 target, int resume, IReadOnlyList<Vector3> path) =>
        target.IsFinite() && resume >= 0 && resume < path.Count &&
        (resume == 0 || target.DistanceTo(path[resume - 1]) <= .01f);

    internal static Vector3[] TerminalRegionCorridor(IReadOnlyList<Vector3> path, Func<Vector3, bool> arrival)
    {
        if (path.Count == 0 || !arrival(path[^1]))
            throw new InvalidDataException("Source corridor has no terminal arrival region.");
        var end = path.Count - 1;
        // Portal midpoints wholly inside the final reachable region are seeds,
        // not compulsory positions inside the target's collision body. Retain
        // every preceding point outside that region, including folded routes.
        while (end > 0 && arrival(path[end - 1])) end--;
        return path.Take(end + 1).ToArray();
    }

    internal static NativeNavigationIntent? ApproachAfterProjection(Vector3 start, Vector3 accepted,
        Vector3 reference, float radius, Func<Vector3, bool> sourceFloor)
    {
        if (!start.IsFinite() || !accepted.IsFinite() || !reference.IsFinite() || !float.IsFinite(radius) || radius <= 0)
            throw new InvalidDataException("Projected reference approach requires finite observed poses and a positive radius.");
        // The steering owner must have walked the complete verified prefix.
        // A point elsewhere in the source region cannot consume that route.
        if (new Vector2(start.X - accepted.X, start.Z - accepted.Z).Length() > .2f ||
            Math.Abs(start.Y - accepted.Y) >= SourceHeightTolerance || !sourceFloor(start)) return null;
        return new(reference, 1, radius, true, []);
    }

    // A nearby reference can still sit across a wall or another floor. Follow
    // required source portals before refining its reference region. A final
    // source segment that ends at that same goal retains the caller's radius;
    // requiring its exact endpoint first would contradict that arrival region.
    internal static NativeNavigationIntent Intent(Vector3 start, IReadOnlyList<Vector3> path,
        Vector3? referenceTarget = null, float referenceRadius = 0, float length = 8,
        NativeNavigationProjectionRegion? projection = null)
    {
        if (!start.IsFinite() || path.Any(point => !point.IsFinite()) || referenceTarget is { } referencePose && !referencePose.IsFinite() ||
            !float.IsFinite(referenceRadius) || referenceRadius < 0)
            throw new InvalidDataException("Native navigation intent requires finite source poses and a nonnegative reference radius.");
        if (projection is not null && (!projection.Requested.IsFinite() || !projection.Selected.IsFinite() ||
            !float.IsFinite(projection.Radius) || projection.Radius <= 0 || path.Count == 0 || projection.Selected != path[^1]))
            throw new InvalidDataException("Native source projection region differs from its requested radius or selected endpoint.");
        var (target, resume) = CorridorPrefix(start, path, length);
        var corridor = path.Take(resume).Append(target).ToArray();
        var previous = start;
        var remaining = 0f;
        foreach (var point in corridor) { remaining += previous.DistanceTo(point); previous = point; }
        if (referenceTarget is { } reference && resume == path.Count && remaining <= .2f)
            return new(reference, resume, referenceRadius, true, []);
        if (projection is null && referenceTarget is { } terminal && referenceRadius > 0 &&
            resume == path.Count && terminal == path[^1] &&
            Math.Abs(start.Y - terminal.Y) < SourceHeightTolerance)
        {
            var required = path.Take(path.Count - 1).ToArray();
            previous = start;
            var requiredDistance = 0f;
            foreach (var point in required) { requiredDistance += previous.DistanceTo(point); previous = point; }
            // Keep the existing source-prefix handoff threshold. Endpoint
            // proximity alone cannot consume an untraversed folded corridor.
            if (requiredDistance <= .2f) return new(terminal, resume, referenceRadius, true, []);
            (target, resume) = CorridorPrefix(start, required, length);
            return new(target, resume, 0, false, required.Take(resume).Append(target).ToArray());
        }
        return new(target, resume, 0, false, corridor, resume == path.Count ? projection : null);
    }
}
