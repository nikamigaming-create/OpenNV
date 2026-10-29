namespace OpenNV.Runtime.Content;

internal sealed record FalloutIdleDeactivationSnapshot(float DurationSeconds, double ElapsedSeconds, bool Started)
{
    internal void Validate()
    {
        if (!float.IsFinite(DurationSeconds) || DurationSeconds < 0 || !double.IsFinite(ElapsedSeconds) ||
            ElapsedSeconds < 0 || ElapsedSeconds > DurationSeconds || !Started && ElapsedSeconds != 0)
            throw new InvalidDataException("Idle deactivation has an invalid retained ease-out clock.");
    }
}

// Sequence deactivation is independent of the selected KF playback clock.
// Its first animation update anchors the unset source start time.
internal sealed class FalloutIdleDeactivation
{
    internal float DurationSeconds { get; }
    internal double ElapsedSeconds { get; private set; }
    internal bool Started { get; private set; }
    internal bool Complete => DurationSeconds == 0 || Started && ElapsedSeconds >= DurationSeconds;
    internal float Weight => Complete ? 0 : (float)((DurationSeconds - ElapsedSeconds) / DurationSeconds);

    internal FalloutIdleDeactivation(FalloutIdleTransition.Metadata source, FalloutIdleTransition.Settings settings,
        FalloutIdleTransition.Metadata? incoming = null)
    {
        if (!float.IsFinite(settings.DefaultBlendSeconds) || !float.IsFinite(settings.AnimationMultiplier) ||
            settings.AnimationMultiplier <= 0)
            throw new InvalidDataException("Idle deactivation requires finite source animation settings.");
        var count = Math.Max(source.Blend, source.BlendOut);
        if (incoming is { } next) count = Math.Max(count, Math.Max(next.Blend, next.BlendIn));
        DurationSeconds = Math.Max(0, (count == 0 ? settings.DefaultBlendSeconds : count / 30f) / settings.AnimationMultiplier);
        if (!float.IsFinite(DurationSeconds)) throw new InvalidDataException("Idle deactivation duration is not finite.");
    }

    internal void Advance(double delta)
    {
        if (!double.IsFinite(delta) || delta < 0) throw new ArgumentOutOfRangeException(nameof(delta));
        if (!Started) { Started = true; return; }
        if (!Complete) ElapsedSeconds = Math.Min(DurationSeconds, ElapsedSeconds + delta);
    }

    internal FalloutIdleDeactivationSnapshot Capture() => new(DurationSeconds, ElapsedSeconds, Started);

    internal void Restore(FalloutIdleDeactivationSnapshot saved)
    {
        saved.Validate();
        if (saved.DurationSeconds != DurationSeconds)
            throw new InvalidDataException("Saved idle deactivation differs from its source duration.");
        ElapsedSeconds = saved.ElapsedSeconds; Started = saved.Started;
    }
}
