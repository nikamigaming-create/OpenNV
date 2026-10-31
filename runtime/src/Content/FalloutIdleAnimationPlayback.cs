namespace OpenNV.Runtime.Content;

internal readonly record struct FalloutIdleAnimationInterval(float From, float To, bool IncludeFrom);

// With GroupMarkerState, SourceSeconds retains the raw group clock. Render
// and event samples are resolved separately by the admitted sequence policy.
internal sealed record FalloutIdleAnimationPlaybackSnapshot(double SourceSeconds, byte SelectedAdditionalLoops,
    byte RemainingAdditionalLoops, long CompletedRepeats, bool IncludeStart, bool Complete, int? GroupMarkerState = null)
{
    internal void Validate()
    {
        if (!double.IsFinite(SourceSeconds) || CompletedRepeats < 0 || GroupMarkerState is < 0 or > 3 ||
            (SelectedAdditionalLoops == byte.MaxValue) != (RemainingAdditionalLoops == byte.MaxValue) ||
            SelectedAdditionalLoops != byte.MaxValue && RemainingAdditionalLoops > SelectedAdditionalLoops ||
            Complete && (GroupMarkerState is null && RemainingAdditionalLoops != 0 || IncludeStart))
            throw new InvalidDataException("Saved idle playback phase is invalid.");
    }
}

/// <summary>Source KF phase with an intro, authored repeat interval, and finite outro.</summary>
internal sealed class FalloutIdleAnimationPlayback
{
    private readonly float _start;
    private readonly float _stop;
    private readonly float _frequency;
    private readonly bool _cycle;
    private readonly float? _loopStart;
    private readonly float? _loopEnd;
    private readonly FalloutIdleGroupTiming? _group;
    private int _groupMarkerState;
    private double _phase;
    private bool _includeStart = true;
    internal byte SelectedAdditionalLoops { get; }
    internal byte AdditionalLoops { get; private set; }
    internal float SourceSeconds => _group is null ? (float)_phase : Math.Clamp((float)_phase, _start, _stop);
    internal bool HasGroupTiming => _group is not null;
    internal bool Complete { get; private set; }
    internal long CompletedRepeats { get; private set; }
    internal float? LoopStart => _loopStart;
    internal float? LoopEnd => _loopEnd;
    internal bool Endless => _cycle || SelectedAdditionalLoops == byte.MaxValue;
    internal double ElapsedSeconds => (_cycle ? CompletedRepeats * ((double)_stop - _start) + _phase - _start :
        CompletedRepeats == 0 ? _phase - _start :
        (double)_loopEnd!.Value - _start + (CompletedRepeats - 1) * ((double)_loopEnd.Value - _loopStart!.Value) +
        _phase - _loopStart.Value) / _frequency;

    internal FalloutIdleAnimationPlayback(float start, float stop, float frequency, uint cycleType,
        IReadOnlyList<(float Time, string Value)> keys, byte additionalLoops, string? sequenceName = null)
    {
        if (!float.IsFinite(start) || !float.IsFinite(stop) || stop <= start || !float.IsFinite(frequency) || frequency <= 0)
            throw new InvalidDataException("Idle animation has an invalid source clock.");
        if (cycleType is not (0 or 2))
            throw new NotSupportedException($"Idle animation cycle {cycleType} has no phase owner.");
        _start = start;
        _stop = stop;
        _frequency = frequency;
        _cycle = cycleType == 0;
        _group = !_cycle && sequenceName is not null ? FalloutIdleGroupTiming.Read(sequenceName, keys) : null;
        if (_group is { } group)
        {
            _loopStart = group.StartLoop;
            _loopEnd = group.EndLoop;
        }
        else
        {
            _loopStart = Marker("StartLoop");
            _loopEnd = Marker("EndLoop");
            if ((_loopStart is null) != (_loopEnd is null) || _loopStart is { } first &&
                (_loopEnd <= first || first < _start || _loopEnd > _stop))
                throw new InvalidDataException("Idle animation has an incomplete or invalid repeat interval.");
            if (additionalLoops != 0 && _loopStart is null)
                throw new NotSupportedException("Repeating IDLE has no authored StartLoop/EndLoop interval.");
            if (_cycle && additionalLoops != 0)
                throw new NotSupportedException("A cycling KF with finite IDLE repetitions requires its native group owner.");
        }
        SelectedAdditionalLoops = additionalLoops;
        AdditionalLoops = additionalLoops;
        _phase = _start;

        float? Marker(string name)
        {
            var found = keys.Where(key => key.Value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Any(value => value.Trim().Equals(name, StringComparison.OrdinalIgnoreCase))).ToArray();
            if (found.Length > 1 || found.Any(key => !float.IsFinite(key.Time)))
                throw new InvalidDataException($"Idle animation has an invalid {name} marker.");
            return found.Length == 0 ? null : found[0].Time;
        }
    }

    internal FalloutIdleAnimationPlaybackSnapshot Capture() => new(_phase, SelectedAdditionalLoops,
        AdditionalLoops, CompletedRepeats, _includeStart, Complete, _group is null ? null : _groupMarkerState);

    internal void DispatchHeldBoundary(Action<FalloutIdleAnimationInterval> traversed)
    {
        ArgumentNullException.ThrowIfNull(traversed);
        if (!_includeStart || Complete) return;
        _includeStart = false;
        traversed(new((float)_phase, (float)_phase, true));
    }

    internal void Restore(FalloutIdleAnimationPlaybackSnapshot saved)
    {
        saved.Validate();
        if (_group is not null)
        {
            if (saved.GroupMarkerState is not { } marker || saved.SelectedAdditionalLoops != SelectedAdditionalLoops ||
                saved.SourceSeconds < (saved.CompletedRepeats == 0 ? _start : _group.StartLoop) ||
                SelectedAdditionalLoops != byte.MaxValue &&
                (saved.CompletedRepeats > SelectedAdditionalLoops ||
                    saved.RemainingAdditionalLoops != SelectedAdditionalLoops - saved.CompletedRepeats) ||
                saved.Complete && saved.SourceSeconds < _group.End ||
                saved.IncludeStart && (saved.SourceSeconds != _start || saved.CompletedRepeats != 0))
                throw new InvalidDataException("Saved idle lacks or contradicts its ordered group phase owner.");
            _groupMarkerState = marker;
        }
        else if (saved.GroupMarkerState is not null || saved.SelectedAdditionalLoops != SelectedAdditionalLoops || saved.SourceSeconds < _start || saved.SourceSeconds > _stop ||
            _cycle && (saved.Complete || saved.SourceSeconds == _stop ||
                saved.IncludeStart && saved.SourceSeconds != _start) ||
            !_cycle && (SelectedAdditionalLoops != byte.MaxValue && saved.CompletedRepeats > SelectedAdditionalLoops ||
                saved.RemainingAdditionalLoops != (SelectedAdditionalLoops == byte.MaxValue ? byte.MaxValue :
                    SelectedAdditionalLoops - saved.CompletedRepeats) ||
                saved.Complete != (saved.SourceSeconds == _stop && saved.RemainingAdditionalLoops == 0) ||
                saved.RemainingAdditionalLoops != 0 && saved.SourceSeconds >= _loopEnd ||
                saved.CompletedRepeats > 0 && saved.SourceSeconds < _loopStart ||
                saved.IncludeStart && saved.SourceSeconds != (saved.CompletedRepeats == 0 ? _start : _loopStart)))
            throw new InvalidDataException("Saved idle playback differs from its source repeat interval.");
        _phase = saved.SourceSeconds;
        AdditionalLoops = saved.RemainingAdditionalLoops;
        CompletedRepeats = saved.CompletedRepeats;
        _includeStart = saved.IncludeStart;
        Complete = saved.Complete;
    }

    /// <returns>Unconsumed simulation time after a finite animation completes.</returns>
    internal double Advance(double seconds, Action<FalloutIdleAnimationInterval>? traversed = null, bool processGroup = true)
    {
        if (!double.IsFinite(seconds) || seconds < 0 || !double.IsFinite(seconds * _frequency))
            throw new ArgumentOutOfRangeException(nameof(seconds));
        if (_group is not null) return AdvanceGroup(seconds, traversed, processGroup);
        if (Complete) return seconds;
        while (seconds > 0)
        {
            var repeat = AdditionalLoops != 0;
            var end = repeat ? _loopEnd!.Value : _stop;
            var untilEnd = (end - _phase) / _frequency;
            var consumed = Math.Min(seconds, untilEnd);
            var before = (float)_phase;
            _phase = consumed == untilEnd ? end : _phase + consumed * _frequency;
            traversed?.Invoke(new(before, (float)_phase, _includeStart));
            _includeStart = false;
            seconds -= consumed;
            if (consumed < untilEnd) break;
            if (repeat || _cycle)
            {
                _phase = repeat ? _loopStart!.Value : _start;
                if (repeat && AdditionalLoops != byte.MaxValue) AdditionalLoops--;
                CompletedRepeats = checked(CompletedRepeats + 1);
                _includeStart = true;
            }
            else
            {
                Complete = true;
                break;
            }
        }
        return seconds;
    }
    private double AdvanceGroup(double seconds, Action<FalloutIdleAnimationInterval>? traversed, bool processGroup)
    {
        if (Complete && processGroup) return seconds;
        // Native event dispatch starts from the prior resolved sample. Group
        // seeks shift that endpoint, while the new endpoint uses NiCalcTime.
        var previous = (double)SourceSeconds;
        _phase += seconds * _frequency;
        if (!double.IsFinite(_phase) || Math.Abs(_phase) > float.MaxValue)
            throw new InvalidDataException("Idle group source clock overflowed.");
        if (processGroup && !Complete)
        {
            var group = _group!;
            while (_groupMarkerState < 3 && _phase > group.Marker(_groupMarkerState)) _groupMarkerState++;
            if (_phase >= group.EndLoop && AdditionalLoops != 0)
            {
                // The native group consumes one signed interval per eligible
                // update. Both event endpoints move with the sequence clock.
                var interval = (double)group.EndLoop - group.StartLoop;
                previous -= interval;
                _phase -= interval;
                _groupMarkerState = 1;
                if (AdditionalLoops != byte.MaxValue) AdditionalLoops--;
                CompletedRepeats = checked(CompletedRepeats + 1);
            }
            else if (_phase >= group.End) Complete = true;
        }
        traversed?.Invoke(new((float)previous, SourceSeconds, _includeStart));
        _includeStart = false;
        return 0;
    }
}

// SpecialIdle's source group compiler is ordered and prefix-based. Unwritten
// fields retain zero; a missing marker is not replaced by the KF endpoint.
internal sealed record FalloutIdleGroupTiming(float Start, float StartLoop, float EndLoop, float End)
{
    private static readonly string[] Markers = ["Start", "StartLoop", "EndLoop", "End"];
    internal float Marker(int index) => index switch
    { 0 => Start, 1 => StartLoop, 2 => EndLoop, 3 => End, _ => throw new ArgumentOutOfRangeException(nameof(index)) };

    internal static bool IsSpecialIdle(string sequenceName)
    {
        var separator = sequenceName.IndexOf('_');
        var group = separator < 0 ? sequenceName : sequenceName[..separator];
        return group.All(value => value <= 127) && group.Equals("SpecialIdle", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool TryConsume(string text, ref int expected, out int slot)
    {
        slot = expected;
        if (expected >= Markers.Length || text.Any(value => value > 127)) return false;
        if (expected == 1 && text.StartsWith("End", StringComparison.OrdinalIgnoreCase)) slot = 3;
        if (!text.StartsWith(Markers[slot], StringComparison.OrdinalIgnoreCase)) return false;
        expected = slot + 1;
        return true;
    }

    internal static FalloutIdleGroupTiming? Read(string sequenceName, IReadOnlyList<(float Time, string Value)> keys)
    {
        if (!IsSpecialIdle(sequenceName)) return null;
        var values = new float[4];
        var expected = 0;
        foreach (var key in keys)
            foreach (var text in key.Value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Select(value => value.Trim()))
                if (TryConsume(text, ref expected, out var slot))
                {
                    if (!float.IsFinite(key.Time)) throw new InvalidDataException("Idle group marker time is not finite.");
                    if (key.Time > -1) values[slot] = key.Time;
                }
        if (values[0] > values[1]) values[1] = values[0];
        if (values[1] >= values[2]) values[2] = values[3];
        return new(values[0], values[1], values[2], values[3]);
    }
}
