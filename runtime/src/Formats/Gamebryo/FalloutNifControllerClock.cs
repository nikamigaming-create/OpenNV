namespace OpenNV.Runtime.Formats.Gamebryo;

internal static class FalloutNifControllerClock
{
    internal static void Validate(FalloutNifTimeController source)
    {
        if (!float.IsFinite(source.StartTime) || !float.IsFinite(source.StopTime) || source.StopTime <= source.StartTime ||
            !float.IsFinite(source.Frequency) || source.Frequency <= 0 || !float.IsFinite(source.Phase) ||
            (source.Flags & 0x38) != 0x08 || ((source.Flags >> 1) & 3) is not (0 or 2))
            throw new NotSupportedException("Direct NIF clock requires an active forward controller with no manager.");
    }

    internal static double Resolve(FalloutNifTimeController source, double elapsed)
    {
        if (!double.IsFinite(elapsed) || elapsed < 0) throw new InvalidDataException("Direct NIF elapsed time is invalid.");
        var scaled = elapsed * source.Frequency + source.Phase;
        if (((source.Flags >> 1) & 3) == 2) return Math.Clamp(scaled, source.StartTime, source.StopTime);
        var span = (double)source.StopTime - source.StartTime;
        var phase = (scaled - source.StartTime) % span;
        return source.StartTime + (phase < 0 ? phase + span : phase);
    }

    internal static double ElapsedAt(FalloutNifTimeController source, double sourceSeconds)
    {
        Validate(source);
        if (!double.IsFinite(sourceSeconds)) throw new ArgumentOutOfRangeException(nameof(sourceSeconds));
        var target = Math.Clamp(sourceSeconds, source.StartTime, source.StopTime);
        if (((source.Flags >> 1) & 3) == 0)
        {
            var span = (double)source.StopTime - source.StartTime;
            if (target < source.Phase) target += Math.Ceiling((source.Phase - target) / span) * span;
        }
        else if (target < Resolve(source, 0))
            throw new NotSupportedException("The requested clamped source time precedes this controller's initial phase.");
        return Math.Max(0, (target - source.Phase) / source.Frequency);
    }
}
