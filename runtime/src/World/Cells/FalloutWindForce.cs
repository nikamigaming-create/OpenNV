using System.Numerics;

namespace OpenNV.Runtime.World.Cells;

// Implementation-neutral wind-listener contract. The force is horizontal in
// source Havok coordinates; the native physics adapter converts its units once.
internal static class FalloutWindForce
{
    internal const uint ResponsiveBodyFlag = 1;
    internal const float InitialHeading = 1;
    private const float MaximumForce = 250;
    private const double ReferenceStep = 0.016699999570846558;

    internal static Vector3 Sample(float speed, float heading, double seconds, float gust, float direction)
    {
        if (!float.IsFinite(speed) || speed is < 0 or > 1 || !float.IsFinite(heading) ||
            !double.IsFinite(seconds) || seconds < 0 ||
            !float.IsFinite(gust) || gust is < 0 or > 1 ||
            !float.IsFinite(direction) || direction is < 0 or > 1)
            throw new InvalidDataException("Wind force inputs are invalid.");
        if (speed == 0 || seconds == 0) return Vector3.Zero;
        var magnitude = Math.Clamp((2 * gust - .75f) * MaximumForce * speed, 0, MaximumForce)
            * (1 + Math.Floor(seconds / ReferenceStep));
        var angle = heading + (2 * direction - 1) * (MathF.PI / 4);
        var force = new Vector3(-(float)magnitude * MathF.Sin(angle), (float)magnitude * MathF.Cos(angle), 0);
        if (!float.IsFinite(force.X) || !float.IsFinite(force.Y))
            throw new InvalidDataException("Wind force cannot be represented by the physics owner.");
        return force;
    }
}
