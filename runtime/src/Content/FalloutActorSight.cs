using System.Numerics;

namespace OpenNV.Runtime.Content;

// Loaded actor sight candidates: cached source target nodes replace the first
// two bounds fractions; the third remains the actual root quarter-height.
internal static class FalloutActorSight
{
    internal static Vector3[] Points(Vector3 root, float height, Vector3? head, Vector3? torso)
    {
        if (!Finite(root) || !float.IsFinite(height) || height < 0 ||
            head is { } h && !Finite(h) || torso is { } t && !Finite(t))
            throw new InvalidDataException("Actor sight source points are invalid.");
        Vector3[] points = [head ?? root + Vector3.UnitY * (height * .75f),
            torso ?? root + Vector3.UnitY * (height * .5f), root + Vector3.UnitY * (height * .25f)];
        if (points.Any(point => !Finite(point))) throw new InvalidDataException("Actor sight source points overflowed.");
        return points;
    }

    internal static bool AnyClear(IReadOnlyList<Vector3> points, Func<Vector3, bool> clear)
    {
        if (points.Count != 3 || points.Any(point => !Finite(point)))
            throw new InvalidDataException("Actor sight requires its three actual source candidates.");
        foreach (var point in points) if (clear(point)) return true;
        return false;
    }

    internal static bool InCone(Vector3 offset, Vector3 forward, float degrees)
    {
        if (!Finite(offset) || !Finite(forward) || !float.IsFinite(degrees))
            throw new InvalidDataException("Actor sight view cone is invalid.");
        var length = Math.Sqrt((double)offset.X * offset.X + (double)offset.Z * offset.Z);
        var facingLength = Math.Sqrt((double)forward.X * forward.X + (double)forward.Z * forward.Z);
        if (facingLength == 0) throw new InvalidDataException("Actor sight has no horizontal facing.");
        if (length == 0) return degrees >= 0;
        var dot = ((double)offset.X * forward.X + (double)offset.Z * forward.Z) / (length * facingLength);
        return Math.Acos(Math.Clamp(dot, -1, 1)) <= degrees * Math.PI / 360;
    }

    private static bool Finite(Vector3 point) => float.IsFinite(point.X) && float.IsFinite(point.Y) && float.IsFinite(point.Z);
}
