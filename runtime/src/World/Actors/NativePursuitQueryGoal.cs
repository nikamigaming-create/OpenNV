using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Actors;

// Planning evidence, never persistent gameplay or arrival authority.
internal readonly record struct NativePursuitQueryGoal(Vector3 Target, float Radius,
    FalloutFormKey? Package, string? SourceSha256, FalloutFormKey? CombatTarget)
{
    internal bool Matches(NativePursuitQueryGoal current, float targetMotionAllowance)
    {
        if (!Target.IsFinite() || !current.Target.IsFinite() || !float.IsFinite(Radius) || Radius < 0 ||
            !float.IsFinite(current.Radius) || current.Radius < 0 ||
            !float.IsFinite(targetMotionAllowance) || targetMotionAllowance <= 0)
            throw new ArgumentOutOfRangeException(nameof(current), "Pursuit query goals need finite poses, nonnegative radii and the actual positive actor radius.");
        return Package == current.Package && CombatTarget == current.CombatTarget && Radius == current.Radius &&
            StringComparer.OrdinalIgnoreCase.Equals(SourceSha256, current.SourceSha256) &&
            Target.DistanceTo(current.Target) <= targetMotionAllowance;
    }

    internal bool RetireIfObsolete<T>(IEnumerator<T> query, NativePursuitQueryGoal current, float targetMotionAllowance)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (Matches(current, targetMotionAllowance)) return false;
        // Disposal unregisters scheduled navigation work and releases iterator
        // scratch before a replacement may acquire the existing shared budget.
        query.Dispose();
        return true;
    }
}
