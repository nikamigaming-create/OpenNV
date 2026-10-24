namespace OpenNV.Runtime.Content;

// Retained phase owner for a finite transition from current target transforms.
// The caller owns the snapshot pose, source bindings, and timed source events.
internal sealed class FalloutIdleTransition
{
    internal readonly record struct Metadata(byte Blend, byte BlendIn, byte BlendOut);
    internal readonly record struct Admission(byte RecordGroup, bool HasActiveOutgoing,
        bool HasSynchronization, bool MenuOverride, bool InstantOverride, bool ApplicationOverride);
    internal readonly record struct Settings(float DefaultBlendSeconds, float AnimationMultiplier);

    internal readonly record struct ExecutionSlot
    {
        public byte Index { get; }
        [System.Text.Json.Serialization.JsonConstructor]
        public ExecutionSlot(byte index)
        {
            if (index > 7) throw new NotSupportedException($"Animation execution slot {index} is unsupported.");
            Index = index;
        }

        internal static ExecutionSlot FromRecord(byte recordGroup)
        {
            var slot = recordGroup & 0x3f;
            return new((byte)(slot switch { 20 => 1, 21 => 4, _ => slot }));
        }
    }

    internal readonly record struct BlendFactors(float SnapshotWeight, float IncomingWeight,
        float SnapshotEase, float IncomingEase);
    internal sealed record Snapshot(ExecutionSlot Slot, float DurationSeconds, double ElapsedSeconds, bool Complete, bool Started);

    // This is the source's promoted Float32 threshold, compared as Float64.
    private const double DirectActivationThreshold = 0.009999999776482582;
    internal ExecutionSlot Slot { get; }
    internal float DurationSeconds { get; }
    internal double ElapsedSeconds { get; private set; }
    internal bool Started { get; private set; }
    internal bool Complete => ElapsedSeconds == DurationSeconds;
    internal float IncomingHeldPhase => 0;

    private FalloutIdleTransition(ExecutionSlot slot, float durationSeconds)
    { Slot = slot; DurationSeconds = durationSeconds; }

    internal static FalloutIdleTransition CreateSnapshot(Admission admission, Metadata incoming,
        Metadata? outgoing, Settings settings)
    {
        var slot = ExecutionSlot.FromRecord(admission.RecordGroup);
        if (admission.HasActiveOutgoing || admission.HasSynchronization || admission.MenuOverride ||
            admission.InstantOverride || admission.ApplicationOverride)
            throw new NotSupportedException("Snapshot transition requires an inactive execution slot and no transition overrides or synchronization.");
        var count = Math.Max(incoming.Blend, incoming.BlendIn);
        if (outgoing is { } previous)
            count = Math.Max(count, Math.Max(previous.Blend, previous.BlendOut));
        // A nonzero authored count bypasses the configured fallback. Retain a
        // finite fallback for serialization without requiring it to be used.
        if (!float.IsFinite(settings.DefaultBlendSeconds) ||
            !float.IsFinite(settings.AnimationMultiplier) || settings.AnimationMultiplier <= 0)
            throw new NotSupportedException("Snapshot transition requires finite animation settings and a positive multiplier.");
        var duration = count == 0 ? settings.DefaultBlendSeconds : (float)(count / 30.0);
        duration /= settings.AnimationMultiplier;
        if (!float.IsFinite(duration) || duration <= DirectActivationThreshold)
            throw new NotSupportedException("Snapshot transition duration selects an unsupported direct activation path.");
        return new(slot, duration);
    }

    internal BlendFactors Factors
    {
        get
        {
            var incoming = Complete ? 1 : (float)(ElapsedSeconds / DurationSeconds);
            return new(1 - incoming, incoming, 1, 1);
        }
    }

    // True means this update belongs to the held-phase transition, including
    // its completion update. The next update may advance the source clock.
    // Completion anchors at the actual update time; it consumes any overshoot.
    internal bool Advance(double delta)
    {
        if (!double.IsFinite(delta) || delta < 0) throw new ArgumentOutOfRangeException(nameof(delta));
        if (!Started)
        {
            // Native activation leaves the start unset until its first update.
            // That update establishes the origin and has zero blend progress.
            Started = true;
            return true;
        }
        if (Complete) return false;
        var remaining = DurationSeconds - ElapsedSeconds;
        ElapsedSeconds = delta >= remaining ? DurationSeconds : ElapsedSeconds + delta;
        return true;
    }

    internal Snapshot Capture() => new(Slot, DurationSeconds, ElapsedSeconds, Complete, Started);

    internal static FalloutIdleTransition Restore(Snapshot snapshot, Admission admission,
        Metadata incoming, Metadata? outgoing, Settings settings)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        // Recompute source admission before accepting any retained phase.
        var result = CreateSnapshot(admission, incoming, outgoing, settings);
        if (snapshot.Slot != result.Slot || snapshot.DurationSeconds != result.DurationSeconds ||
            !double.IsFinite(snapshot.ElapsedSeconds) || snapshot.ElapsedSeconds < 0 ||
            snapshot.ElapsedSeconds > result.DurationSeconds ||
            snapshot.Complete != (snapshot.ElapsedSeconds == result.DurationSeconds) ||
            !snapshot.Started && (snapshot.ElapsedSeconds != 0 || snapshot.Complete))
            throw new InvalidDataException("Saved snapshot transition differs from its source admission or phase bounds.");
        result.ElapsedSeconds = snapshot.ElapsedSeconds;
        result.Started = snapshot.Started;
        return result;
    }
}
