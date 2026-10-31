using System.Numerics;

namespace OpenNV.Runtime.Content;

// Source camera admission is independent of LAND retention and final GPU culling.
internal static class FalloutGrassVisibility
{
    internal readonly record struct View(Vector3 Eye, Vector3 Forward, float ReferenceFov,
        float Aspect, float Near, float Far, float LodAdjustment);

    internal static int CameraCell(float coordinate) => checked((int)coordinate) / 4096 - (coordinate < 0 ? 1 : 0);

    internal static float DirectionThreshold(View view)
    {
        var right = MathF.Tan(view.ReferenceFov * MathF.PI / 360) * .75f * view.Aspect;
        return 1 - MathF.Atan(right) / view.LodAdjustment / 8;
    }

    internal static bool CellAdmitted(FalloutLandscapeTransport land, Vector3 eye, float range)
    {
        var x = land.ActiveCoordinates.X * 4096f;
        var y = land.ActiveCoordinates.Y * 4096f;
        var dx = eye.X - Math.Clamp(eye.X, x, x + 4096);
        var dy = eye.Y - Math.Clamp(eye.Y, y, y + 4096);
        return MathF.Sqrt(dx * dx + dy * dy) < range;
    }

    internal static bool QuadrantAdmitted(FalloutLandscapeTransport land, byte quadrant, View view, float range)
    {
        var x0 = quadrant % 2 * 16;
        var y0 = quadrant / 2 * 16;
        var minimum = float.PositiveInfinity;
        var maximum = float.NegativeInfinity;
        for (var y = y0; y <= y0 + 16; y++)
            for (var x = x0; x <= x0 + 16; x++)
            {
                var z = land.Heights[y * 33 + x];
                minimum = Math.Min(minimum, z); maximum = Math.Max(maximum, z);
            }
        var half = new Vector3(1024, 1024, (maximum - minimum) * .5f);
        var center = new Vector3(land.ActiveCoordinates.X * 4096f + x0 * 128 + 1024,
            land.ActiveCoordinates.Y * 4096f + y0 * 128 + 1024, (minimum + maximum) * .5f);
        var relative = center - view.Eye;
        if (relative.Length() - half.Length() >= range) return false;
        var forward = Vector3.Normalize(new(view.Forward.X, view.Forward.Y, 0));
        if (!float.IsFinite(forward.X)) throw new NotSupportedException("Grass has no finite flattened camera direction.");
        var right = new Vector3(forward.Y, -forward.X, 0);
        var angle = view.ReferenceFov * MathF.PI / 360 + 2 * MathF.Acos(DirectionThreshold(view));
        var vertical = MathF.Tan(angle) * .75f;
        var horizontal = vertical * view.Aspect;
        return Inside(forward, -view.Near) && Inside(-forward, view.Far) &&
            Inside(forward * horizontal + right, 0) && Inside(forward * horizontal - right, 0) &&
            Inside(forward * vertical + Vector3.UnitZ, 0) && Inside(forward * vertical - Vector3.UnitZ, 0);

        bool Inside(Vector3 normal, float offset) => Vector3.Dot(normal, relative) + offset +
            MathF.Abs(normal.X) * half.X + MathF.Abs(normal.Y) * half.Y + MathF.Abs(normal.Z) * half.Z >= 0;
    }
}
