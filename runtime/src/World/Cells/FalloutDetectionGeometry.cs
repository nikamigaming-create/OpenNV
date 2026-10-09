namespace OpenNV.Runtime.World.Cells;

// Source-space geometry kernels. Their inputs require independent process,
// bounds, animation-node and visibility-query owners. A HeadTracking bone or a
// Godot camera position is not an implicit binding of a sight-node slot.
internal static class FalloutDetectionGeometry
{
    private const float Pi = (float)Math.PI;
    private const float Tau = Pi * 2;
    private const float RadiansPerDegree = (float)(Math.PI / 180);

    internal static bool InCone(float[] sourceOrigin, float[] sourceTarget,
        float sourceFacingRadians, float viewConeDegrees)
    {
        Vector(sourceOrigin); Vector(sourceTarget);
        var x = Store((double)sourceTarget[0] - sourceOrigin[0]);
        var y = Store((double)sourceTarget[1] - sourceOrigin[1]);
        // The source's zero-horizontal-vector branch selects -pi/2. It does
        // not special-case coincident reference positions as zero bearing.
        var bearing = y == 0 ? x > 0 ? Pi / 2 : -Pi / 2 : MathF.Atan(Store(x / (double)y));
        if (y < 0) bearing = Store(bearing + (double)Pi);
        bearing = Normalize(bearing);
        return InConeFromBearing(bearing, sourceFacingRadians, viewConeDegrees);
    }

    internal static bool InConeFromBearing(float sourceBearingRadians,
        float sourceFacingRadians, float viewConeDegrees)
    {
        if (!float.IsFinite(sourceBearingRadians) || !float.IsFinite(sourceFacingRadians) ||
            !float.IsFinite(viewConeDegrees))
            throw new InvalidDataException("Detection cone has no finite source angle.");
        var difference = Store((double)sourceBearingRadians - sourceFacingRadians);
        if (difference < -Pi) difference = Store(difference + (double)Tau);
        else if (difference > Pi) difference = Store(Tau - (double)difference);
        var coneRadians = Store(viewConeDegrees * (double)RadiansPerDegree);
        return Math.Abs((double)difference) < coneRadians * .5d;
    }

    // The real caller selects one sample normally and three for a player or
    // its actual current target. Sight-node positions are explicit values or
    // proven nulls. Unknown slots must be refused by the source adapter.
    internal static float[][] TargetPoints(float[] sourcePosition, float sourceScaledBoundsHeight,
        int sourceSampleCount, float[]? firstSightNode, float[]? secondSightNode)
    {
        Vector(sourcePosition);
        if (!float.IsFinite(sourceScaledBoundsHeight) || sourceScaledBoundsHeight < 0 ||
            sourceSampleCount is not (1 or 3))
            throw new InvalidDataException("Detection target has invalid source bounds or sample count.");
        if (firstSightNode is not null) Vector(firstSightNode);
        if (secondSightNode is not null) Vector(secondSightNode);
        float[] fractions = [.75f, .5f, .25f];
        var samples = new float[sourceSampleCount][];
        for (var index = 0; index < samples.Length; index++)
        {
            var boundNode = index switch { 0 => firstSightNode, 1 => secondSightNode, _ => null };
            samples[index] = boundNode?.ToArray() ?? [sourcePosition[0], sourcePosition[1],
                Store(sourcePosition[2] + (double)sourceScaledBoundsHeight * fractions[index])];
        }
        return samples;
    }

    private static float Normalize(float angle)
    {
        if (angle < 0) return Store((Store(angle + (double)Tau)) % (double)Tau);
        return angle >= Tau ? Store(angle % (double)Tau) : angle;
    }

    private static void Vector(float[]? value)
    {
        if (value is not { Length: 3 } || value.Any(number => !float.IsFinite(number)))
            throw new InvalidDataException("Detection geometry has no finite source vector.");
    }
    private static float Store(double value)
    {
        var result = (float)value;
        return double.IsFinite(value) && float.IsFinite(result) ? result :
            throw new NotSupportedException("Detection geometry exceeds its finite Float32 domain.");
    }
}
