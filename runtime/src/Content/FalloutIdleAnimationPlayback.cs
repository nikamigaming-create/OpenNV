namespace OpenNV.Runtime.Content;

internal readonly record struct FalloutIdleAnimationInterval(float From, float To, bool IncludeFrom);

/// <summary>Source KF phase with an intro, authored repeat interval, and finite outro.</summary>
internal sealed class FalloutIdleAnimationPlayback
{
    private readonly float _start;
    private readonly float _stop;
    private readonly float _frequency;
    private readonly bool _cycle;
    private readonly float? _loopStart;
    private readonly float? _loopEnd;
    private double _phase;
    private bool _includeStart = true;
    internal byte AdditionalLoops { get; private set; }
    internal float SourceSeconds => (float)_phase;
    internal bool Complete { get; private set; }
    internal long CompletedRepeats { get; private set; }
    internal float? LoopStart => _loopStart;
    internal float? LoopEnd => _loopEnd;

    internal FalloutIdleAnimationPlayback(float start, float stop, float frequency, uint cycleType,
        IReadOnlyList<(float Time, string Value)> keys, byte additionalLoops)
    {
        if (!float.IsFinite(start) || !float.IsFinite(stop) || stop <= start || !float.IsFinite(frequency) || frequency <= 0)
            throw new InvalidDataException("Idle animation has an invalid source clock.");
        if (cycleType is not (0 or 2))
            throw new NotSupportedException($"Idle animation cycle {cycleType} has no phase owner.");
        _start = start;
        _stop = stop;
        _frequency = frequency;
        _cycle = cycleType == 0;
        _loopStart = Marker("StartLoop");
        _loopEnd = Marker("EndLoop");
        if ((_loopStart is null) != (_loopEnd is null) || _loopStart is { } first &&
            (_loopEnd <= first || first < _start || _loopEnd > _stop))
            throw new InvalidDataException("Idle animation has an incomplete or invalid repeat interval.");
        if (additionalLoops != 0 && _loopStart is null)
            throw new NotSupportedException("Repeating IDLE has no authored StartLoop/EndLoop interval.");
        if (_cycle && additionalLoops != 0)
            throw new NotSupportedException("A cycling KF with finite IDLE repetitions requires its native group owner.");
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

    /// <returns>Unconsumed simulation time after a finite animation completes.</returns>
    internal double Advance(double seconds, Action<FalloutIdleAnimationInterval>? traversed = null)
    {
        if (!double.IsFinite(seconds) || seconds < 0 || !double.IsFinite(seconds * _frequency))
            throw new ArgumentOutOfRangeException(nameof(seconds));
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
                CompletedRepeats++;
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
}
