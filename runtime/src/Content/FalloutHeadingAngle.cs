namespace OpenNV.Runtime.Content;

internal static class FalloutHeadingAngle
{
    // Source XY is horizontal, +Y is forward at zero heading, and placed
    // reference Z angles turn clockwise. Target height does not affect bearing.
    internal static float Between(IReadOnlyList<float> from, IReadOnlyList<float> to, float headingRadians)
    {
        if (from.Count != 3 || to.Count != 3 || from.Any(value => !float.IsFinite(value)) ||
            to.Any(value => !float.IsFinite(value)) || !float.IsFinite(headingRadians))
            throw new InvalidDataException("Heading angle has an invalid source placement.");
        var x = (double)to[0] - from[0];
        var y = (double)to[1] - from[1];
        if (x == 0 && y == 0) return 0;
        var degrees = Math.Atan2(x, y) * 180 / Math.PI - headingRadians * 180 / Math.PI;
        var result = Math.IEEERemainder(degrees, 360);
        return (float)result;
    }
}
