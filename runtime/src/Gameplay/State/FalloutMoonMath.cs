using System.Numerics;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal static class FalloutMoonMath
{
    internal static uint SourceDays(float days)
    {
        // Both selected getters truncate the actual Float32 global toward zero
        // before returning UInt32. The negative/overflow ISA arms need their
        // separate actual conversion owner; they are never a default phase.
        if (!float.IsFinite(days) || days < 0 || (double)days > uint.MaxValue)
            throw new NotSupportedException("Moon DaysPassed has an unowned source unsigned-conversion arm.");
        return (uint)MathF.Truncate(days);
    }
    internal static int Phase(uint days, int length, int current)
    {
        if (length is < 0 or > 63 || current is < 0 or > 7) throw new InvalidDataException("Moon phase has another source climate/cache extent.");
        return length == 0 ? current : (int)((days % ((uint)length * 8)) / (uint)length);
    }
    internal static float Advance(FalloutMoonSource source, FalloutMoonSettings settings, float angle, float previousHour, float hour)
    {
        settings.Validate();
        if (!float.IsFinite(angle) || !float.IsFinite(previousHour) || !float.IsFinite(hour))
            throw new InvalidDataException("Moon clock/angle lost its actual finite source cells.");
        var elapsed = hour - previousHour;
        if (elapsed < 0) elapsed = source.IsStandalone ? elapsed + 24f : (float)(elapsed + 24.0);
        var next = source.IsStandalone ? (settings.Speed * 60f * elapsed) + angle :
            (float)(settings.Speed * 60.0 * elapsed + angle);
        if (!float.IsFinite(next)) throw new InvalidDataException("Moon source angle overflowed.");
        // Each source loop writes Float32. A modulus would skip those writes.
        while (next < 0)
        {
            var value = source.IsStandalone ? next + 360f : (float)(next + 360.0);
            if (value <= next) throw new NotSupportedException("Moon wrap exceeds source Float32 progress.");
            next = value;
        }
        while (next >= 360f)
        {
            var value = source.IsStandalone ? next - 360f : (float)(next - 360.0);
            if (value >= next) throw new NotSupportedException("Moon wrap exceeds source Float32 progress.");
            next = value;
        }
        return next;
    }
    internal static float Fade(FalloutMoonSource source, FalloutMoonSettings settings, float angle, bool shadow)
    {
        settings.Validate();
        if (!float.IsFinite(angle)) throw new InvalidDataException("Moon fade has a non-finite source angle.");
        if (angle > 180f) return 0;
        var lower = shadow ? settings.FadeEnd - settings.ShadowEarly : settings.FadeEnd;
        var upper = shadow ? settings.FadeEnd : settings.FadeStart;
        var afterStart = 180f - upper; var afterEnd = 180f - lower;
        if (angle <= upper && angle >= lower)
            return source.IsStandalone ? (angle - lower) / (upper - lower) : (float)((angle - (double)lower) / (upper - (double)lower));
        if (angle >= afterStart && angle <= afterEnd)
            return source.IsStandalone ? (afterEnd - angle) / (afterEnd - afterStart) : (float)((afterEnd - (double)angle) / (afterEnd - (double)afterStart));
        return angle > upper && angle < afterStart ? 1f : 0f;
    }
    internal static FalloutMoonMeshInputs Geometry(uint size, bool shadow)
    {
        var extent = (float)size;
        Vector3[] vertices = [new(-extent, extent, 0), new(-extent, -extent, 0), new(extent, extent, 0), new(extent, -extent, 0)];
        Vector2[] uv = [new(0, 0), new(0, 1), new(1, 0), new(1, 1)];
        Vector4[] colors = [shadow ? Vector4.One : new(1, 0, 0, 1), Vector4.One, Vector4.One, Vector4.One];
        return new(vertices, uv, colors, [0, 1, 2, 2, 1, 3]);
    }
}
