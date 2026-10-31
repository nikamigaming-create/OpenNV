namespace OpenNV.Runtime.Diagnostics.Parity;

// The adapter's state-publication interval is a separate evidence lane from
// client receipt time and from the game's later consumption of that state.
internal sealed record RecordedInputCommit(long Request, int Process, long BeginTicks, long EndTicks, long Frequency)
{
    internal void Validate(long request, int process, long sentTicks, long observedTicks, long frequency)
    {
        if (Request != request || Process != process || process <= 0 || request <= 0 ||
            Frequency <= 0 || Frequency != frequency || sentTicks < 0 ||
            BeginTicks < sentTicks || EndTicks < BeginTicks || EndTicks > observedTicks)
            throw new InvalidDataException("The native adapter commit does not belong to this request, process and observed monotonic interval.");
    }

    internal long DurationMicroseconds => checked((long)decimal.Ceiling((decimal)(EndTicks - BeginTicks) * 1_000_000 / Frequency));

    internal long SegmentMicroseconds(long clockOriginTicks, long segmentOffsetMicroseconds)
    {
        if (Frequency <= 0 || clockOriginTicks < 0 || EndTicks < clockOriginTicks || segmentOffsetMicroseconds < 0)
            throw new InvalidDataException("The adapter commit has no valid segment clock.");
        var value = checked((long)((decimal)(EndTicks - clockOriginTicks) * 1_000_000 / Frequency) - segmentOffsetMicroseconds);
        return value >= 0 ? value : throw new InvalidDataException("The adapter commit predates this input segment.");
    }
}
