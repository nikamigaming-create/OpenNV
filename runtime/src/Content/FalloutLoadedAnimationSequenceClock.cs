using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutLoadedAnimationClockSnapshot(uint Active, uint Weight, uint StartAbsolute,
    uint LastAbsolute, uint Elapsed, uint SourceTime, long Activations, long Updates, long Deactivations);

// This clock is driven only by the actual source AnimData absolute argument.
// Godot delta, the presentation idle clock and a host stopwatch never enter it.
internal sealed class FalloutLoadedAnimationSequenceClock
{
    private readonly FalloutNifControllerSequence _sequence;
    private uint _active, _weight = 0x3f800000, _start = FalloutLoadedAnimationSource.NoTime,
        _last = FalloutLoadedAnimationSource.NoTime, _elapsed, _time;
    private long _activations, _updates, _deactivations;
    internal bool Active => _active != 0;
    internal float SourceTime => BitConverter.UInt32BitsToSingle(_time);
    internal FalloutLoadedAnimationSequenceClock(FalloutNifControllerSequence sequence,
        FalloutLoadedAnimationClockSnapshot? saved = null)
    {
        ArgumentNullException.ThrowIfNull(sequence);
        if (!float.IsFinite(sequence.StartTime) || !float.IsFinite(sequence.StopTime) || sequence.StopTime < sequence.StartTime ||
            !float.IsFinite(sequence.Frequency) || sequence.CycleType > 2)
            throw new NotSupportedException("source-loaded-sequence-current-cycle-frequency-or-range-unowned");
        _sequence = sequence; _time = Bits(sequence.StartTime);
        if (saved is null) return;
        Validate(saved, sequence); _active = saved.Active; _weight = saved.Weight; _start = saved.StartAbsolute;
        _last = saved.LastAbsolute; _elapsed = saved.Elapsed; _time = saved.SourceTime;
        _activations = saved.Activations; _updates = saved.Updates; _deactivations = saved.Deactivations;
    }
    internal bool Activate(uint weightBits, bool restart)
    {
        if (Active) return false;
        var weight = Value(weightBits);
        if (!float.IsFinite(weight) || weight < 0) throw new InvalidDataException("Sequence activation weight is not a current finite source scalar.");
        _active = 1; _weight = weightBits; _last = FalloutLoadedAnimationSource.NoTime;
        if (restart) { _start = FalloutLoadedAnimationSource.NoTime; _elapsed = 0; _time = Bits(_sequence.StartTime); }
        _activations = checked(_activations + 1); return true;
    }
    internal float Update(uint absoluteBits)
    {
        if (!Active) throw new InvalidOperationException("Inactive sequence has no reached interpolation update.");
        var absolute = Value(absoluteBits);
        if (!float.IsFinite(absolute) || absoluteBits == FalloutLoadedAnimationSource.NoTime)
            throw new InvalidDataException("Sequence update lost its actual finite source time argument.");
        if (_start == FalloutLoadedAnimationSource.NoTime) _start = absoluteBits;
        var elapsed = (absolute - Value(_start)) * _sequence.Frequency;
        if (!float.IsFinite(elapsed) || elapsed < 0)
            throw new NotSupportedException("source-sequence-backward-or-overflowed-absolute-clock-transition-unowned");
        var duration = _sequence.StopTime - _sequence.StartTime;
        var phase = duration <= 0 ? 0 : _sequence.CycleType switch
        {
            0 => elapsed % duration,
            1 => PingPong(elapsed, duration),
            _ => Math.Min(elapsed, duration)
        };
        _elapsed = Bits(elapsed); _time = Bits(_sequence.StartTime + phase); _last = absoluteBits;
        _updates = checked(_updates + 1); return SourceTime;
    }
    internal bool DeactivateImmediately()
    {
        if (!Active) return false;
        _active = 0; _deactivations = checked(_deactivations + 1); return true;
    }
    internal FalloutLoadedAnimationClockSnapshot Capture() => new(_active, _weight, _start, _last,
        _elapsed, _time, _activations, _updates, _deactivations);
    internal static void Validate(FalloutLoadedAnimationClockSnapshot saved, FalloutNifControllerSequence sequence)
    {
        if (saved.Active > 1 || !float.IsFinite(Value(saved.Weight)) || Value(saved.Weight) < 0 ||
            !float.IsFinite(Value(saved.Elapsed)) || Value(saved.Elapsed) < 0 || !float.IsFinite(Value(saved.SourceTime)) ||
            Value(saved.SourceTime) < sequence.StartTime || Value(saved.SourceTime) > sequence.StopTime ||
            saved.Activations < 0 || saved.Updates < 0 || saved.Deactivations < 0 ||
            saved.Activations - saved.Deactivations != saved.Active ||
            saved.StartAbsolute != FalloutLoadedAnimationSource.NoTime && !float.IsFinite(Value(saved.StartAbsolute)) ||
            saved.LastAbsolute != FalloutLoadedAnimationSource.NoTime && !float.IsFinite(Value(saved.LastAbsolute)) ||
            saved.Updates == 0 && saved.LastAbsolute != FalloutLoadedAnimationSource.NoTime)
            throw new InvalidDataException("Loaded sequence clock lost its actual source timing and activation history.");
    }
    private static uint Bits(float value) => BitConverter.SingleToUInt32Bits(value);
    private static float PingPong(float elapsed, float duration)
    { var phase = elapsed % (duration * 2); return phase <= duration ? phase : duration * 2 - phase; }
    private static float Value(uint value) => BitConverter.UInt32BitsToSingle(value);
}
