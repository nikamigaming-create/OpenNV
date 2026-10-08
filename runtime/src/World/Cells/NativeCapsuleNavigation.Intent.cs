using Godot;

namespace OpenNV.Runtime.World.Cells;

internal sealed record NativeNavigationIntent(Vector3 Target, int Resume, float ArrivalRadius,
    bool ReferenceApproach, IReadOnlyList<Vector3> Corridor, NativeNavigationProjectionRegion? Projection = null);

internal sealed record NativeNavigationProjectionRegion(Vector3 Requested, Vector3 Selected, float Radius);

internal static partial class NativeCapsuleNavigation
{
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
