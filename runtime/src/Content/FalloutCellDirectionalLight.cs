using System.Numerics;

namespace OpenNV.Runtime.Content;

internal static class FalloutCellDirectionalLight
{
    // XCLL's first rotation turns around negative Z; the second turns around
    // negative Y. NiDirectionalLight emits along the resulting positive X
    // column. Presentation needs the opposite vector, from surface to light.
    internal static Vector3 RayDirection(float xDegrees, float zDegrees)
    {
        if (!float.IsFinite(xDegrees) || !float.IsFinite(zDegrees))
            throw new InvalidDataException("CELL directional rotations must be finite.");
        var x = xDegrees * (MathF.PI / 180f);
        var z = zDegrees * (MathF.PI / 180f);
        return new(MathF.Cos(x) * MathF.Cos(z), -MathF.Sin(x) * MathF.Cos(z), MathF.Sin(z));
    }
}
