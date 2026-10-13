using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal enum FalloutRendererClockPrefix { InputStored, AccumulatedStored, Returned }
internal sealed record FalloutRendererClockPublication(Guid Main, long Ordinal, uint RateBits, uint MaximumTimeBits,
    bool SceneIsComplex, FalloutRendererClockPrefix Prefix, string? Failure);
internal sealed record FalloutRendererClockSnapshot(string Schema, FalloutRendererClockConfiguration Configuration,
    Guid CapturedProcess, long Publications, uint MainMode, uint InputBits, uint AccumulatedBits,
    uint PreviousBits, uint RemainderBits, uint DeltaBits, uint Steps, FalloutRendererClockPublication? LastPublication);

// The original renderer publishes its own retained remainder, substep delta
// and count. It consumes the one actual source timer rate; it never samples
// another OS clock or advances the calendar.
internal sealed class FalloutRendererClockState : IDisposable
{
    internal const string Schema = "opennv-source-renderer-clock/v1";
    internal FalloutRendererClockConfiguration Configuration { get; }
    private readonly Guid _process;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private long _publications;
    private readonly uint _mainMode;
    private float _input, _accumulated, _previous, _remainder, _delta;
    private uint _steps;
    private FalloutRendererClockPublication? _last;
    private bool _disposed, _entered;
    internal string? SaveBlocker => _entered ? "source-renderer-clock-writer-entered" :
        _last?.Failure is not null ? "source-renderer-clock-writer-failed" :
        _last?.Prefix != FalloutRendererClockPrefix.Returned ? "source-renderer-clock-Main-writer-unreturned" : null;
    internal object State => new
    {
        source = Configuration.Source.Identity,
        process = _process,
        publications = _publications,
        updateType = Configuration.CopiedUpdateType,
        mainMode = _mainMode,
        input = _input,
        accumulated = _accumulated,
        previous = _previous,
        remainder = _remainder,
        delta = _delta,
        steps = _steps,
        last = _last,
        saveBlocker = SaveBlocker,
        remainingModeWriters = "independent-source-mode-transition-owner-unbound",
    };
    internal FalloutRendererClockState(FalloutRendererClockConfiguration configuration, Guid process,
        FalloutRendererClockSnapshot? saved = null)
    {
        configuration.Validate();
        if (process == Guid.Empty) throw new InvalidDataException("Renderer clock omitted its actual Main process.");
        Configuration = configuration; _process = process;
        var source = configuration.Source;
        _mainMode = source.LoadedMainMode;
        _input = Float(source.LoadedInputBits); _accumulated = Float(source.LoadedAccumulatedBits);
        _previous = Float(source.LoadedPreviousBits); _remainder = Float(source.LoadedRemainderBits);
        _delta = Float(source.LoadedDeltaBits); _steps = source.LoadedSteps;
        if (saved is null) return;
        Validate(saved);
        if (saved.Configuration != configuration || saved.CapturedProcess == process || saved.LastPublication?.Failure is not null ||
            saved.LastPublication is not { Prefix: FalloutRendererClockPrefix.Returned })
            throw new InvalidDataException("Cold renderer clock changed source or replayed an unreturned/failed publication.");
        _publications = saved.Publications; _input = Float(saved.InputBits); _accumulated = Float(saved.AccumulatedBits);
        _previous = Float(saved.PreviousBits); _remainder = Float(saved.RemainderBits); _delta = Float(saved.DeltaBits);
        _steps = saved.Steps; _last = saved.LastPublication;
    }
    internal void Publish(Guid main, long ordinal, float scaledSeconds, float currentRate,
        float maximumTime, bool sceneIsComplex)
    {
        Require();
        if (main == Guid.Empty || ordinal < 1 || _last?.Main == main || _last is { } prior && ordinal <= prior.Ordinal)
            throw new InvalidOperationException("Renderer clock repeated its actual source Main publication.");
        _entered = true; _publications = checked(_publications + 1);
        _last = new(main, ordinal, Bits(currentRate), Bits(maximumTime), sceneIsComplex, FalloutRendererClockPrefix.InputStored, null);
        try
        {
            _input = scaledSeconds;
            if (!float.IsFinite(_input) || !float.IsFinite(currentRate) || !float.IsFinite(maximumTime))
                throw new NotSupportedException("Renderer clock has an unowned nonfinite source input.");
            _accumulated = _input + _remainder;
            if (!float.IsFinite(_accumulated)) throw new NotSupportedException("Renderer clock accumulated a nonfinite source value.");
            if (_accumulated > Configuration.Source.MaximumAccumulatedSeconds)
                _accumulated = Configuration.Source.MaximumAccumulatedSeconds;
            _last = _last with { Prefix = FalloutRendererClockPrefix.AccumulatedStored };
            if (_accumulated <= 0) { _steps = 0; _delta = 0; }
            else
            {
                var force = _mainMode == 4 || Configuration.Source.LoadedForce != 0;
                var limit = sceneIsComplex ? 1u : 3u;
                switch (FalloutRendererClockSource.Mode(Configuration.CopiedUpdateType))
                {
                    case FalloutRendererClockMode.Smoothed:
                        var difference = _accumulated - _previous;
                        var adjustment = difference * Configuration.CopiedChaseMultiplier;
                        _previous = adjustment + _previous;
                        _delta = _previous; _remainder = 0; _steps = 1;
                        break;
                    case FalloutRendererClockMode.Raw:
                        _delta = _input; _steps = 1;
                        break;
                    case FalloutRendererClockMode.Fixed:
                        Fixed(maximumTime, currentRate, limit, force);
                        break;
                    case FalloutRendererClockMode.Adaptive:
                        Adaptive(maximumTime, limit, force);
                        break;
                    default: throw new InvalidDataException("Renderer clock source mode is undefined.");
                }
            }
            if (!float.IsFinite(_previous) || !float.IsFinite(_remainder) || !float.IsFinite(_delta) || !float.IsFinite(_accumulated))
                throw new NotSupportedException("Renderer clock returned unowned nonfinite arithmetic.");
            _last = _last with { Prefix = FalloutRendererClockPrefix.Returned };
        }
        catch (Exception error) { _last = _last with { Failure = error.ToString() }; throw; }
        finally { _entered = false; }
    }
    private void Fixed(float maximumTime, float currentRate, uint limit, bool force)
    {
        var quantum = maximumTime * currentRate;
        _delta = quantum;
        _steps = Math.Min(TruncatedSignedAsUnsigned(_accumulated / quantum), limit);
        if (_steps != 0)
        {
            var consumed = (float)_steps * quantum;
            var residual = _accumulated - consumed;
            _accumulated -= residual;
            _remainder = residual;
            if (!(maximumTime > residual)) _remainder = maximumTime;
            return;
        }
        var threshold = quantum * Configuration.Source.FixedStepFraction;
        if (!force && threshold > _accumulated)
        { _remainder = _accumulated; _accumulated = 0; _delta = 0; return; }
        _delta = _accumulated; _remainder = 0; _steps = 1;
    }
    private void Adaptive(float maximumTime, uint limit, bool force)
    {
        if (!force && Configuration.Source.DeferredSeconds > _accumulated)
        { _remainder = _accumulated; _accumulated = 0; _delta = 0; _steps = 0; return; }
        _remainder = 0;
        _steps = Math.Min(TruncatedSignedAsUnsigned(_accumulated / maximumTime), limit);
        if (_steps == 0) { _delta = _accumulated; _steps = 1; }
        else _delta = _accumulated / (float)_steps;
    }
    // CVTTSS2SI returns the signed-indefinite word on overflow/NaN. The
    // following source comparison interprets that word as unsigned.
    private static uint TruncatedSignedAsUnsigned(float value) => !float.IsFinite(value) || value < int.MinValue || (double)value >= 2147483648d
        ? 0x80000000u : unchecked((uint)(int)value);
    private static uint Bits(float value) => BitConverter.SingleToUInt32Bits(value);
    private static float Float(uint value) => BitConverter.UInt32BitsToSingle(value);
    internal FalloutRendererClockSnapshot Capture()
    {
        Require(allowFailed: true);
        var value = new FalloutRendererClockSnapshot(Schema, Configuration, _process, _publications, _mainMode,
            Bits(_input), Bits(_accumulated), Bits(_previous), Bits(_remainder), Bits(_delta), _steps, _last);
        Validate(value); return value;
    }
    internal static void Validate(FalloutRendererClockSnapshot value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Schema != Schema || value.Configuration is null || value.CapturedProcess == Guid.Empty || value.Publications < 0 ||
            value.MainMode != value.Configuration.Source.LoadedMainMode ||
            (value.Publications == 0) != (value.LastPublication is null) ||
            value.LastPublication is { } last && (last.Main == Guid.Empty || last.Ordinal < value.Publications ||
                !Enum.IsDefined(last.Prefix) || last.Failure is null && last.Prefix != FalloutRendererClockPrefix.Returned ||
                last.Failure is not null && string.IsNullOrWhiteSpace(last.Failure)))
            throw new InvalidDataException("Renderer clock omitted its actual source/current/cold publication prefix.");
        value.Configuration.Validate();
        var source = value.Configuration.Source;
        if (value.Publications == 0 && (value.InputBits != source.LoadedInputBits || value.AccumulatedBits != source.LoadedAccumulatedBits ||
            value.PreviousBits != source.LoadedPreviousBits || value.RemainderBits != source.LoadedRemainderBits ||
            value.DeltaBits != source.LoadedDeltaBits || value.Steps != source.LoadedSteps))
            throw new InvalidDataException("Fresh renderer clock changed its actual loaded constructor cells.");
        if (value.LastPublication?.Failure is null &&
            new[] { value.InputBits, value.AccumulatedBits, value.PreviousBits, value.RemainderBits, value.DeltaBits }.Any(word => !float.IsFinite(Float(word))))
            throw new InvalidDataException("Renderer clock contains nonfinite fields without a retained source failure.");
        if (value.LastPublication is { Failure: null } publication && value.Steps > (publication.SceneIsComplex ? 1u : 3u))
            throw new InvalidDataException("Renderer clock substeps exceeded its source scene-complexity limit.");
    }
    private void Require(bool allowFailed = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_thread != Environment.CurrentManagedThreadId || _entered)
            throw new InvalidOperationException("Renderer clock changed its actual Main thread or reentered.");
        if (!allowFailed && _last?.Failure is { } failure)
            throw new InvalidOperationException("Renderer clock retains its original failed source publication: " + failure);
    }
    public void Dispose() { if (_disposed) return; Require(allowFailed: true); _disposed = true; }
}
