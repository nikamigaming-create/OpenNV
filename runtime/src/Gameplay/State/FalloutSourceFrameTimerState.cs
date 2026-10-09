using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal interface IFalloutSourceTickCounter
{
    string Owner { get; }
    uint Read();
    void Wait(uint milliseconds);
}

internal sealed record FalloutSourceFrameTimerSnapshot(FalloutSourceFrameTimer Source, Guid CapturedProcess,
    long Epoch, long Sequence, uint CachedMilliseconds, uint OriginAgeMilliseconds, uint RawAtCapture, byte PauseCount,
    float FixedMilliseconds, float FractionalMilliseconds, float ScaledSeconds, float UnscaledSeconds,
    float Rate, float TargetRate, bool UseRateWrites, bool ZeroElapsed, long Updated, string? Failure,
    string? EnteredOperation, long EnteredAt, long ReturnedThrough);

// Raw OS time, the cached UInt32 value, animation delta and game calendar are
// separate fields. In particular the zero-elapsed and minimum-wait branches
// do not change the cached UInt32 value into an artificial accumulated delta.
internal sealed class FalloutSourceFrameTimerState : IDisposable
{
    private readonly FalloutSourceFrameTimer _source;
    private readonly IFalloutSourceTickCounter _counter;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private readonly Guid _process;
    private long _epoch, _sequence, _updated;
    private uint _origin, _cached;
    private byte _pause;
    private float _fixed, _fraction, _scaled, _unscaled, _rate, _target;
    private bool _useRateWrites, _zeroElapsed, _disposed, _entered;
    private string? _failure;
    private string? _operation;
    private long _enteredAt, _returnedThrough;

    internal FalloutSourceFrameTimerState(FalloutSourceFrameTimer source, IFalloutSourceTickCounter counter, Guid ownerProcess,
        FalloutSourceFrameTimerSnapshot? restored = null)
    {
        source.Validate(); ArgumentNullException.ThrowIfNull(counter);
        ArgumentException.ThrowIfNullOrWhiteSpace(counter.Owner);
        if (ownerProcess == Guid.Empty) throw new InvalidDataException("Source timer has no actual owning Main process.");
        _source = source; _counter = counter; _process = ownerProcess;
        var current = counter.Read();
        _origin = current; _rate = _target = source.InitialRate;
        // A process-local owner identity is not a gameplay random draw. Do not
        // publish the same epoch for unrelated timer constructions.
        _epoch = BitConverter.ToInt64(_process.ToByteArray()) & long.MaxValue;
        if (_epoch == 0) _epoch = long.MaxValue;
        if (restored is null) return;
        Validate(restored);
        if (restored.Source != source || restored.CapturedProcess == _process)
            throw new InvalidDataException("Cold cached timer differs from its selected source/current process.");
        _epoch = restored.Epoch; _sequence = restored.Sequence; _updated = restored.Updated;
        _pause = restored.PauseCount;
        _cached = _pause == 0 ? restored.CachedMilliseconds :
            unchecked(restored.CachedMilliseconds + current - restored.RawAtCapture);
        _origin = _pause == 0 ? unchecked(current - restored.OriginAgeMilliseconds) : restored.OriginAgeMilliseconds;
        _fixed = restored.FixedMilliseconds; _fraction = restored.FractionalMilliseconds;
        _scaled = restored.ScaledSeconds; _unscaled = restored.UnscaledSeconds;
        _rate = restored.Rate; _target = restored.TargetRate;
        _useRateWrites = restored.UseRateWrites; _zeroElapsed = restored.ZeroElapsed; _failure = restored.Failure;
        _operation = restored.EnteredOperation; _enteredAt = restored.EnteredAt; _returnedThrough = restored.ReturnedThrough;
    }

    internal object State => new
    {
        source = _source,
        process = _process,
        epoch = _epoch,
        sequence = _sequence,
        milliseconds = _cached,
        paused = _pause,
        scaledSeconds = _scaled,
        unscaledSeconds = _unscaled,
        updated = _updated,
        operation = _operation,
        enteredAt = _enteredAt,
        returnedThrough = _returnedThrough,
        failure = _failure
    };
    internal string? SaveBlocker => _entered ? "source-cached-timer-writer-entered" :
        _failure is not null ? "source-cached-timer-writer-failed" : null;
    internal bool HasReturnedUpdate => _updated > 0 && _failure is null && !_entered;
    internal FalloutSandboxTimerSample ReadCached()
    {
        RequireHealthy();
        return new(_source.EngineSha256, _epoch, _cached);
    }
    internal float ScaledSeconds { get { RequireHealthy(); return _scaled; } }
    internal void UpdateFromMain(FalloutSourceFrameTimerConfiguration configuration, bool cachedMainMenuGate)
    {
        configuration.Validate();
        if (configuration.Source != _source)
            throw new InvalidDataException("Source Main clock phase changed the selected timer/INI owner.");
        // This argument is the living Main cached field. It is not a query of
        // the product's generic pause/menu state or the distinct channel sample.
        SetUseRateWrites(configuration.ChangeRateSlowly);
        if (!cachedMainMenuGate) AdjustRate();
        Update();
    }

    internal void SetFixedMilliseconds(float milliseconds)
    {
        ExecuteWriter("fixed-millisecond-store", () =>
        {
            if (!float.IsFinite(milliseconds) || milliseconds >= uint.MaxValue)
                throw new NotSupportedException("Source fixed timer step has no owned unsigned millisecond representation.");
            _fixed = milliseconds;
        });
    }
    internal void SetUseRateWrites(bool enabled) => ExecuteWriter("source-INI-rate-mode-store", () => _useRateWrites = enabled);
    internal void SetRate(float target, bool setCurrent)
    {
        ExecuteWriter("source-rate-target-store", () =>
        {
            if (!float.IsFinite(target) || target <= 0)
                throw new NotSupportedException("Source timer rate has no finite positive division owner.");
            _target = target;
            if (_useRateWrites)
            {
                if (setCurrent) _rate = _target;
            }
            else if (_rate != target)
            {
                _scaled = (float)(_scaled / (double)_rate);
                _rate = target;
                _scaled = (float)((double)_scaled * _rate);
            }
        });
    }
    internal void AdjustRate()
    {
        ExecuteWriter("source-rate-adjustment", () =>
        {
            if (_target > _rate) _rate = MathF.Min(_target,
                (float)((1d + _source.InitialRateAdjustment) * _rate));
            else if (_target < _rate) _rate = MathF.Max(_target,
                (float)((1d - _source.InitialRateAdjustment) * _rate));
        });
    }

    internal void Pause()
    {
        ExecuteWriter("nested-source-pause", () =>
        {
            var before = _pause; _pause = unchecked((byte)(_pause + 1));
            if (before == 0)
            {
                var now = _counter.Read();
                _cached = unchecked(now - _cached); _origin = unchecked(now - _origin);
            }
        });
    }
    internal void Resume()
    {
        ExecuteWriter("nested-source-resume", () =>
        {
            if (_pause == 0) return; // Actual source return performs no OS sample or field conversion.
            if (--_pause == 0)
            {
                var now = _counter.Read();
                _cached = unchecked(now - _cached); _origin = unchecked(now - _origin);
            }
        });
    }

    internal void Update()
    {
        ExecuteWriter("cached-timer-update", () =>
        {
            var now = _counter.Read();
            if (_pause != 0) { _scaled = 0; _updated = Next(); return; }
            var cached = unchecked(now - _origin);
            if (_fixed > 0)
            {
                _fraction = (float)((double)_fraction + _fixed);
                if (!float.IsFinite(_fraction) || _fraction >= uint.MaxValue)
                    throw new NotSupportedException("Fixed source timer fraction has no owned unsigned conversion.");
                var whole = (uint)Math.Truncate(_fraction);
                _fraction = (float)((double)_fraction - whole);
                cached = unchecked(_cached + whole);
                _scaled = (float)(_fixed * _source.MillisecondsToSeconds);
            }
            else
            {
                var elapsed = unchecked(cached - _cached);
                _zeroElapsed = elapsed == 0;
                if (_zeroElapsed) elapsed = 1;
                elapsed = Math.Min(elapsed, _source.MaximumFrameMilliseconds);
                if (elapsed < _source.MinimumFrameMilliseconds)
                {
                    _counter.Wait(_source.MinimumFrameMilliseconds - elapsed);
                    elapsed = _source.MinimumFrameMilliseconds;
                }
                _scaled = (float)(elapsed * _source.MillisecondsToSeconds);
            }
            _cached = cached; _unscaled = _scaled;
            _scaled = (float)((double)_scaled * _rate);
            _updated = Next();
        });
    }
    internal FalloutSourceFrameTimerSnapshot Capture()
    {
        RequireHealthy();
        if (_entered) throw new NotSupportedException("Source cached timer update has not returned.");
        var now = _counter.Read();
        // Rebase at an actual pause boundary, not wall time during cold loading.
        // While paused the origin/cached fields are already source ages.
        var result = new FalloutSourceFrameTimerSnapshot(_source, _process, _epoch, _sequence, _cached,
            _pause == 0 ? unchecked(now - _origin) : _origin,
            now, _pause, _fixed, _fraction, _scaled, _unscaled, _rate, _target,
            _useRateWrites, _zeroElapsed, _updated, _failure, _operation, _enteredAt, _returnedThrough);
        Validate(result); return result;
    }
    internal static void Validate(FalloutSourceFrameTimerSnapshot saved)
    {
        ArgumentNullException.ThrowIfNull(saved);
        if (saved.Source is null) throw new InvalidDataException("Saved cached timer omitted its selected source.");
        saved.Source.Validate();
        if (saved.CapturedProcess == Guid.Empty || saved.Epoch <= 0 || saved.Sequence < 0 ||
            saved.Updated < 0 || saved.Updated > saved.Sequence || saved.EnteredAt < 0 || saved.EnteredAt > saved.Sequence ||
            saved.ReturnedThrough < 0 || saved.ReturnedThrough > saved.Sequence ||
            saved.EnteredOperation is not null && (string.IsNullOrWhiteSpace(saved.EnteredOperation) || saved.Failure is null || saved.EnteredAt <= saved.ReturnedThrough) ||
            saved.EnteredOperation is null && (saved.Failure is not null || saved.EnteredAt > saved.ReturnedThrough) ||
            !float.IsFinite(saved.FixedMilliseconds) || saved.FixedMilliseconds >= uint.MaxValue ||
            !float.IsFinite(saved.FractionalMilliseconds) || saved.FractionalMilliseconds is < 0 or >= 1 ||
            !float.IsFinite(saved.ScaledSeconds) || saved.ScaledSeconds < 0 ||
            !float.IsFinite(saved.UnscaledSeconds) || saved.UnscaledSeconds < 0 ||
            !float.IsFinite(saved.Rate) || saved.Rate <= 0 || !float.IsFinite(saved.TargetRate) || saved.TargetRate <= 0 ||
            saved.Failure is not null && string.IsNullOrWhiteSpace(saved.Failure))
            throw new InvalidDataException("Saved source cached timer fields/committed update are invalid.");
    }
    private void ExecuteWriter(string operation, Action writer)
    {
        RequireHealthy();
        _entered = true; _operation = operation; _enteredAt = Next();
        try
        {
            writer(); _returnedThrough = Next(); _operation = null;
        }
        catch (Exception error) { _failure ??= error.ToString(); throw; }
        finally { _entered = false; }
    }
    private long Next() => _sequence = checked(_sequence + 1);
    private void RequireHealthy()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Environment.CurrentManagedThreadId != _thread) throw new InvalidOperationException("Source timer changed its actual caller thread.");
        if (_entered) throw new InvalidOperationException("Source timer writer cannot reenter/capture its entered prefix.");
        if (_failure is not null) throw new InvalidOperationException("Source timer retains its failed writer: " + _failure);
    }
    public void Dispose()
    {
        if (_disposed) return;
        if (_entered) throw new InvalidOperationException("Source cached timer cannot retire an entered writer.");
        if (Environment.CurrentManagedThreadId != _thread) throw new InvalidOperationException("Source timer retirement changed its actual caller thread.");
        _disposed = true;
    }
}
