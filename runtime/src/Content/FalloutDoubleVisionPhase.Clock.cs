using System.Numerics;

namespace OpenNV.Runtime.Content;

internal sealed partial record FalloutDoubleVisionPhase
{
    public bool SinglePrecisionArithmetic { get; init; }
    public bool StoredSingleTrigonometricResult { get; init; }
    public float? SourceHourFactor { get; init; }
    public uint? LoadedClockBits { get; init; }

    internal float ClockSeconds(float sourceHour)
    {
        if (!float.IsFinite(sourceHour) || sourceHour < 0)
            throw new InvalidDataException("Double-vision clock requires the actual finite source game hour.");
        float result;
        if (SourceHourFactor is not null)
        {
            var factor = SourceHourFactor ?? throw new NotSupportedException("Double-vision clock has no actual installed source-hour callback.");
            result = (float)((double)sourceHour * factor * factor);
        }
        else result = (float)(sourceHour * SecondsPerHour);
        if (!float.IsFinite(result)) throw new InvalidDataException("Double-vision source clock overflowed its Float32 cache.");
        return result;
    }

    internal float AngleSeconds(float storedSeconds)
    {
        if (!float.IsFinite(storedSeconds)) throw new InvalidDataException("Double-vision phase has no finite source clock store.");
        if (SinglePrecisionArithmetic)
        {
            var quotient = storedSeconds / (float)SecondsPerHour;
            return quotient * (float)RadiansPerTurn;
        }
        return (float)(storedSeconds / SecondsPerHour * RadiansPerTurn);
    }

    internal Vector2 Offset(float angle, float amount)
    {
        if (!float.IsFinite(angle) || !float.IsFinite(amount))
            throw new InvalidDataException("Double-vision parameters are nonfinite.");
        if (SinglePrecisionArithmetic || StoredSingleTrigonometricResult)
        {
            var cosine = (float)Math.Cos(angle);
            var sine = (float)Math.Sin(angle);
            return new(cosine * amount, sine * amount);
        }
        return new((float)(Math.Cos(angle) * amount), (float)(Math.Sin(angle) * amount));
    }
}
