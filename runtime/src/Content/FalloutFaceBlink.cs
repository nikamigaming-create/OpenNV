namespace OpenNV.Runtime.Content;

internal sealed record FalloutFaceBlinkSettings(float DownSeconds, float UpSeconds,
    float DelayMinimum, float DelayMaximum, float LookDownSuppression)
{
    internal static FalloutFaceBlinkSettings Read(FalloutPluginStack records) => new(
        FalloutGameSettingFloats.ReadRetained(records, "fBlinkDownTime", nameof(FalloutFaceBlinkSettings)),
        FalloutGameSettingFloats.ReadRetained(records, "fBlinkUpTime", nameof(FalloutFaceBlinkSettings)),
        FalloutGameSettingFloats.ReadRetained(records, "fBlinkDelayMin", nameof(FalloutFaceBlinkSettings)),
        FalloutGameSettingFloats.ReadRetained(records, "fBlinkDelayMax", nameof(FalloutFaceBlinkSettings)),
        FalloutGameSettingFloats.ReadRetained(records, "fLookDownDisableBlinkingAmt", nameof(FalloutFaceBlinkSettings)));
}

internal sealed record FalloutFaceBlinkTarget(float Weight, float Duration);

internal sealed record FalloutFaceBlinkSnapshot(FalloutFaceBlinkSettings Settings, float Weight,
    float ElapsedSeconds, long Cycles, float DelaySeconds, IReadOnlyList<FalloutFaceBlinkTarget> Targets)
{
    internal void Validate()
    {
        if (Settings is null || new[] { Settings.DownSeconds, Settings.UpSeconds, Settings.DelayMinimum,
            Settings.DelayMaximum, Settings.LookDownSuppression, Weight, ElapsedSeconds, DelaySeconds }.Any(value => !float.IsFinite(value)) ||
            Weight is < 0 or > 1 || ElapsedSeconds < 0 || Cycles < 0 || DelaySeconds < 0 || Targets is null || Targets.Count > 3 ||
            Targets.Any(value => value is null || !float.IsFinite(value.Weight) || !float.IsFinite(value.Duration) || value.Duration <= 0) ||
            Targets.Count == 0 && ElapsedSeconds != 0 || Targets.Count > 0 && ElapsedSeconds >= Targets[0].Duration ||
            Cycles == 0 && (Targets.Count != 0 || Weight != 0 || DelaySeconds != 0) ||
            Targets.Count > 0 && (Cycles == 0 || DelaySeconds < Settings.DelayMinimum || DelaySeconds > Settings.DelayMaximum))
            throw new InvalidDataException("Saved blink queue is invalid.");
        var authored = new[] { new FalloutFaceBlinkTarget(0, DelaySeconds),
            new FalloutFaceBlinkTarget(1, Settings.DownSeconds), new FalloutFaceBlinkTarget(0, Settings.UpSeconds) };
        if (!Targets.SequenceEqual(authored.Skip(3 - Targets.Count)))
            throw new InvalidDataException("Saved blink targets differ from the source queue.");
    }

    internal FalloutFaceBlinkSnapshot Copy() => this with { Targets = Targets.ToArray() };
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

    internal FalloutFaceBlinkSnapshot Capture() => new(Settings, Weight, _elapsed, Cycles, DelaySeconds,
        _targets.Select(value => new FalloutFaceBlinkTarget(value.Target, value.Duration)).ToArray());

    internal void Restore(FalloutFaceBlinkSnapshot snapshot)
    {
        snapshot.Validate();
        if (snapshot.Settings != Settings) throw new NotSupportedException("Saved blink settings differ from the winning source.");
        _targets.Clear();
        foreach (var target in snapshot.Targets) _targets.Enqueue((target.Weight, target.Duration));
        Weight = snapshot.Weight; _elapsed = snapshot.ElapsedSeconds;
        Cycles = snapshot.Cycles; DelaySeconds = snapshot.DelaySeconds;
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
