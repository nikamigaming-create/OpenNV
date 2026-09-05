namespace OpenNV.Runtime.Content;

internal sealed record FalloutFaceBlinkSettings(float DownSeconds, float UpSeconds,
    float DelayMinimum, float DelayMaximum, float LookDownSuppression)
{
    internal static FalloutFaceBlinkSettings Read(FalloutPluginStack records) => new(
        FalloutGameSettingFloats.Read(records, "fBlinkDownTime"),
        FalloutGameSettingFloats.Read(records, "fBlinkUpTime"),
        FalloutGameSettingFloats.Read(records, "fBlinkDelayMin"),
        FalloutGameSettingFloats.Read(records, "fBlinkDelayMax"),
        FalloutGameSettingFloats.Read(records, "fLookDownDisableBlinkingAmt"));
}

/// <summary>The FaceGen delay/close/open queue, independent of the selected skeletal KF.</summary>
internal sealed class FalloutFaceBlink
{
    private readonly Func<float> _randomUnit;
    private readonly Queue<(float Target, float Duration)> _targets = [];
    private float _elapsed;
    internal FalloutFaceBlinkSettings Settings { get; }
    internal float Weight { get; private set; }
    internal float ElapsedSeconds => _elapsed;
    internal int PendingTargets => _targets.Count;
    internal long Cycles { get; private set; }
    internal float DelaySeconds { get; private set; }

    internal FalloutFaceBlink(FalloutFaceBlinkSettings settings, Func<float> randomUnit)
    {
        if (new[] { settings.DownSeconds, settings.UpSeconds, settings.DelayMinimum,
            settings.DelayMaximum, settings.LookDownSuppression }.Any(value => !float.IsFinite(value)))
            throw new InvalidDataException("Blink settings must be finite.");
        Settings = settings;
        _randomUnit = randomUnit;
    }

    internal void Advance(double seconds, float lookDown)
    {
        if (!double.IsFinite(seconds) || seconds < 0 || seconds > float.MaxValue || !float.IsFinite(lookDown))
            throw new ArgumentOutOfRangeException(nameof(seconds));
        if (_targets.Count == 0 && Settings.DownSeconds > 0 && Settings.UpSeconds > 0 &&
            Settings.DelayMinimum > 0 && Settings.DelayMaximum >= Settings.DelayMinimum && lookDown < Settings.LookDownSuppression)
        {
            var random = _randomUnit();
            if (!float.IsFinite(random) || random < 0 || random > 1) throw new InvalidDataException("Blink RNG is outside the source unit interval.");
            DelaySeconds = (float)((double)random * (Settings.DelayMaximum - Settings.DelayMinimum) + Settings.DelayMinimum);
            _targets.Enqueue((0, DelaySeconds));
            _targets.Enqueue((1, Settings.DownSeconds));
            _targets.Enqueue((0, Settings.UpSeconds));
            Cycles++;
        }
        if (_targets.Count == 0) { _elapsed = 0; return; }
        _elapsed += (float)seconds;
        while (_targets.TryPeek(out var target))
        {
            if (_elapsed < target.Duration)
            {
                // The reviewed FaceGen queue blends from its current value on
                // each publication, using elapsed/target duration. Preserve
                // this incremental update rather than substituting a KF curve.
                var factor = _elapsed / target.Duration;
                Weight = (float)(Weight * (1.0 - factor) + (double)target.Target * factor);
                return;
            }
            Weight = target.Target;
            _elapsed -= target.Duration;
            _targets.Dequeue();
        }
        _elapsed = 0;
        // Native queue creation is checked before advancement. A completed
        // queue is replenished on the next update, not inside this one.
    }
}
