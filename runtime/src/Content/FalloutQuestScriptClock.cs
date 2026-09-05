namespace OpenNV.Runtime.Content;

internal sealed record FalloutQuestScriptClockSnapshot(float Remaining, float Elapsed, long Invocations)
{
    internal bool HasSameBits(FalloutQuestScriptClockSnapshot other) =>
        BitConverter.SingleToInt32Bits(Remaining) == BitConverter.SingleToInt32Bits(other.Remaining) &&
        BitConverter.SingleToInt32Bits(Elapsed) == BitConverter.SingleToInt32Bits(other.Elapsed) &&
        Invocations == other.Invocations;

    internal void Validate()
    {
        if (!float.IsFinite(Remaining) || !float.IsFinite(Elapsed) || Elapsed < 0 || Invocations < 0)
            throw new InvalidDataException("Saved quest script clock is invalid.");
    }
}

// Recurrence is separate from initial script linking and block admission.
// This owner receives its initial phase; it does not infer an initialization
// counter from a selected subset of scripts or from a reference game's clock.
internal sealed class FalloutQuestScriptClock
{
    private readonly float _defaultDelay;
    internal float Interval { get; }
    internal float Remaining { get; private set; }
    internal float Elapsed { get; private set; }
    internal long Invocations { get; private set; }

    internal FalloutQuestScriptClock(float defaultDelay, float? authoredDelay, float initialPhase)
    {
        if (!float.IsFinite(defaultDelay) || authoredDelay is { } delay && !float.IsFinite(delay) ||
            !float.IsFinite(initialPhase) || initialPhase < 0)
            throw new InvalidDataException("Quest script clock input is invalid.");
        _defaultDelay = defaultDelay;
        Interval = defaultDelay <= 0 ? 0 : authoredDelay is > 0 ? authoredDelay.Value : defaultDelay;
        Remaining = initialPhase;
    }

    internal bool Advance(float seconds)
    {
        if (!float.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        // An already-due invocation does not accrue this frame's delta again.
        if (Remaining <= 0) return true;
        var remaining = (float)((double)Remaining - seconds);
        var elapsed = (float)((double)Elapsed + seconds);
        if (!float.IsFinite(remaining) || !float.IsFinite(elapsed))
            throw new InvalidDataException("Quest script clock exceeds Float32 storage.");
        Remaining = remaining;
        Elapsed = elapsed;
        return Remaining <= 0;
    }

    internal void CompleteInvocation()
    {
        if (Remaining > 0) throw new InvalidOperationException("Quest script is not due.");
        var invocations = checked(Invocations + 1);
        if (_defaultDelay > 0)
        {
            // Retain a late frame's overshoot. Do not drift by restarting a
            // whole interval, or execute an artificial catch-up loop this frame.
            Remaining = (float)((double)Remaining + Interval);
            Elapsed = 0;
        }
        Invocations = invocations;
    }

    internal FalloutQuestScriptClockSnapshot Capture() => new(Remaining, Elapsed, Invocations);

    internal void Validate(FalloutQuestScriptClockSnapshot snapshot)
    {
        snapshot.Validate();
        if (snapshot.Invocations > 0 && snapshot.Remaining > Interval)
            throw new InvalidDataException("Saved recurrence exceeds its source interval.");
    }

    internal void Restore(FalloutQuestScriptClockSnapshot snapshot)
    {
        Validate(snapshot);
        Remaining = snapshot.Remaining;
        Elapsed = snapshot.Elapsed;
        Invocations = snapshot.Invocations;
    }
}
