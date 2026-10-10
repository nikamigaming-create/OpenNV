using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal enum FalloutGlobalTimePrefix
{
    Settled, TimerEntered, TimerReturned, AudioPolicyStored, PresentationEntered,
}

internal sealed record FalloutGlobalTimeSnapshot(FalloutGlobalTimeSource Source, Guid CapturedProcess,
    long TimerEpoch, long TimerSequence, float CurrentRate, float TargetRate, byte? KeepSoundRate,
    long Sequence, long Revision, long NativePublications, FalloutGlobalTimePrefix Prefix, string? Failure);
internal sealed record FalloutGlobalTimePublication(long Ordinal, long Revision, float CurrentRate,
    float TargetRate, float AudioRate, byte? KeepSoundRate);
internal sealed record FalloutGlobalTimeNativeValues(double SimulationRate, float AudioRate);
internal sealed record FalloutGlobalTimeNativeBinding(object Owner, string Identity,
    Func<FalloutGlobalTimeNativeValues> Read, Action<FalloutGlobalTimePublication> Publish, Action Retire);

// The one actual Main timer owns current/target rate. This owner adds the
// selected source audio policy and correlates actual presentation consumers; it never
// creates an independent tick counter, elapsed clock or duplicate timer.
internal sealed class FalloutGlobalTimeState : IDisposable
{
    private readonly FalloutGlobalTimeSource _source;
    private readonly FalloutSourceFrameTimerState _timer;
    private readonly Guid _process;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private byte? _keepSoundRate;
    private long _sequence, _revision, _nativePublications;
    private bool _disposed, _entered;
    private FalloutGlobalTimePrefix _prefix;
    private string? _failure;
    private FalloutGlobalTimeNativeBinding? _binding;
    private FalloutGlobalTimePublication? _lastPublication;
    private string? _retirementFailure;

    internal FalloutGlobalTimeState(FalloutGlobalTimeSource source, FalloutSourceFrameTimerState actualTimer,
        FalloutGlobalTimeSnapshot? saved = null)
    {
        source.Validate(); ArgumentNullException.ThrowIfNull(actualTimer);
        var rates = actualTimer.ReadRates();
        if (rates.Source != source.Timer)
            throw new InvalidDataException("Global time changed the actual constructed Main timer source.");
        _source = source; _timer = actualTimer; _process = rates.Process;
        _keepSoundRate = source.InitialKeepSoundRate;
        if (saved is null) return;
        Validate(saved);
        if (saved.Source != source || saved.CapturedProcess == _process || saved.TimerEpoch != rates.Epoch ||
            saved.TimerSequence != rates.Sequence || !FalloutSourceFrameTimer.SameFloat(saved.CurrentRate, rates.Current) || !FalloutSourceFrameTimer.SameFloat(saved.TargetRate, rates.Target))
            throw new InvalidDataException("Cold global time differs from its actual restored timer cells/source/process.");
        if (saved.Failure is not null || saved.Prefix != FalloutGlobalTimePrefix.Settled)
            throw new InvalidDataException("Cold global time cannot promote an entered or failed source command.");
        _keepSoundRate = saved.KeepSoundRate; _sequence = saved.Sequence; _revision = saved.Revision;
        // Native objects are constructed anew. This count retains source
        // history, not a live consumer or a previously published pointer.
        _nativePublications = saved.NativePublications;
    }

    internal FalloutGlobalTimeSource Source => _source;
    internal string? SaveBlocker => _entered ? "source-global-time-command-entered" :
        _failure is not null ? "source-global-time-command-failed" :
        _retirementFailure is not null ? "source-global-time-retirement-failed" :
        _binding is null ? "source-global-time-presentation-unbound" : null;
    internal object State => new
    {
        source = _source,
        process = _process,
        sequence = _sequence,
        revision = _revision,
        keepSoundRate = _keepSoundRate,
        prefix = _prefix,
        failure = _failure,
        nativePublications = _nativePublications,
        nativeOwner = _binding?.Identity,
        lastPublication = _lastPublication,
        retirementFailure = _retirementFailure,
        originalLoadResetParity = "uninspected",
        originalAudioUpdatePhaseParity = "unmatched",
        originalPerVoiceRateEligibility = "unowned-source-flags-threshold-and-direction-consumers",
        nonfiniteSourceRateRepresentation = "unowned;finite-zero-and-negative-retained",
    };

    internal IDisposable BindPresentation(FalloutGlobalTimeNativeBinding binding)
    {
        RequireHealthy(); ArgumentNullException.ThrowIfNull(binding); ArgumentNullException.ThrowIfNull(binding.Owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(binding.Identity);
        ArgumentNullException.ThrowIfNull(binding.Read); ArgumentNullException.ThrowIfNull(binding.Publish);
        ArgumentNullException.ThrowIfNull(binding.Retire);
        if (_binding is not null) throw new InvalidOperationException("Global time already has an actual native consumer.");
        _binding = binding;
        try { PublishCurrent(force: true); }
        catch (Exception original)
        {
            _failure ??= original.ToString();
            try { Retire(binding); }
            catch (Exception cleanup)
            {
                throw new AggregateException("Global rate construction retains source publication and native retirement failures.", original, cleanup);
            }
            throw;
        }
        return new Scope(() => Retire(binding));
    }

    internal void Set(double multiplier, double? optionalSoundFlag = null)
    {
        RequireHealthy();
        var rate = SourceMultiplier(multiplier);
        if (!_source.HasSoundOverride && optionalSoundFlag is not null)
            throw new InvalidDataException("Selected original command declares one Float and no audio policy argument.");
        byte? flag = _source.HasSoundOverride ? SoundFlag(optionalSoundFlag ?? 0) : null;
        if (_binding is null)
            throw new NotSupportedException("SetGlobalTimeMultiplier has no actual native simulation/audio consumer.");
        _entered = true; _sequence = checked(_sequence + 1);
        try
        {
            _prefix = FalloutGlobalTimePrefix.TimerEntered;
            _timer.SetRate(rate, setCurrent: true);
            _prefix = FalloutGlobalTimePrefix.TimerReturned;
            if (_source.HasSoundOverride) _keepSoundRate = flag;
            _revision = checked(_revision + 1);
            if (_source.HasSoundOverride) _prefix = FalloutGlobalTimePrefix.AudioPolicyStored;
            PublishCurrent(force: true);
            _prefix = FalloutGlobalTimePrefix.Settled; _sequence = checked(_sequence + 1);
        }
        catch (Exception error) { _failure ??= error.ToString(); throw; }
        finally { _entered = false; }
    }

    // Called from the living native presentation phase after current source
    // timer adjustment. No Godot delta is used as the rate or raw clock.
    internal void RefreshPresentation()
    {
        RequireHealthy();
        if (_binding is null) throw new NotSupportedException("Source rate has no current native presentation lease.");
        try { PublishCurrent(force: false); }
        catch (Exception error) { _failure ??= error.ToString(); throw; }
    }

    private void PublishCurrent(bool force)
    {
        var binding = _binding ?? throw new NotSupportedException("Global rate lost its actual native consumer.");
        var rates = _timer.ReadRates();
        if (rates.Source != _source.Timer || rates.Process != _process)
            throw new InvalidDataException("Global rate publication changed its actual timer lifetime.");
        var audioRate = _source.AudioRate(rates.Current, _keepSoundRate);
        if (!force && _lastPublication is { } previous && FalloutSourceFrameTimer.SameFloat(previous.CurrentRate, rates.Current) &&
            FalloutSourceFrameTimer.SameFloat(previous.TargetRate, rates.Target) && previous.AudioRate == audioRate && previous.Revision == _revision)
        {
            RequireReadback(binding, previous);
            return;
        }
        var publication = new FalloutGlobalTimePublication(checked(_nativePublications + 1), _revision,
            rates.Current, rates.Target, audioRate, _keepSoundRate);
        _prefix = FalloutGlobalTimePrefix.PresentationEntered;
        binding.Publish(publication);
        RequireReadback(binding, publication);
        _lastPublication = publication; _nativePublications = publication.Ordinal;
        if (!_entered) _prefix = FalloutGlobalTimePrefix.Settled;
    }

    private static void RequireReadback(FalloutGlobalTimeNativeBinding binding, FalloutGlobalTimePublication publication)
    {
        var actual = binding.Read() ?? throw new InvalidDataException("Native global rate omitted its actual readback.");
        if (BitConverter.DoubleToInt64Bits(actual.SimulationRate) != BitConverter.DoubleToInt64Bits(publication.CurrentRate) ||
            !FalloutSourceFrameTimer.SameFloat(actual.AudioRate, publication.AudioRate))
            throw new InvalidDataException("Native simulation/audio rates differ from their source publication.");
    }

    internal FalloutGlobalTimeSnapshot Capture(FalloutSourceFrameTimerSnapshot timer)
    {
        RequireHealthy();
        if (SaveBlocker is { } blocked) throw new NotSupportedException(blocked);
        FalloutSourceFrameTimerState.Validate(timer);
        var rates = _timer.ReadRates();
        if (timer.Source != rates.Source || timer.CapturedProcess != _process || timer.Epoch != rates.Epoch ||
            timer.Sequence != rates.Sequence || !FalloutSourceFrameTimer.SameFloat(timer.Rate, rates.Current) || !FalloutSourceFrameTimer.SameFloat(timer.TargetRate, rates.Target))
            throw new InvalidDataException("Global time capture changed the same already-captured timer cells.");
        RefreshPresentation();
        var result = new FalloutGlobalTimeSnapshot(_source, _process, rates.Epoch, rates.Sequence, rates.Current,
            rates.Target, _keepSoundRate, _sequence, _revision, _nativePublications, _prefix, _failure);
        Validate(result); return result;
    }

    internal static void Validate(FalloutGlobalTimeSnapshot saved)
    {
        ArgumentNullException.ThrowIfNull(saved);
        if (saved.Source is null) throw new InvalidDataException("Global time omitted its selected source.");
        saved.Source.Validate();
        if (saved.CapturedProcess == Guid.Empty || saved.TimerEpoch <= 0 || saved.TimerSequence < 0 ||
            (saved.Source.HasSoundOverride ? saved.KeepSoundRate is null or > 1 : saved.KeepSoundRate is not null) || saved.Sequence < 0 || saved.Revision < 0 || saved.NativePublications < 0 ||
            saved.Revision > saved.Sequence || !float.IsFinite(saved.CurrentRate) ||
            !float.IsFinite(saved.TargetRate) || !Enum.IsDefined(saved.Prefix) ||
            saved.Failure is not null && string.IsNullOrWhiteSpace(saved.Failure))
            throw new InvalidDataException("Saved global time has invalid source cells or command history.");
        if (saved.Prefix != FalloutGlobalTimePrefix.Settled || saved.Failure is not null)
            throw new InvalidDataException("Saved global time contains an unsettled or failed source prefix.");
    }

    internal static float SourceMultiplier(double value)
    {
        var source = (float)value;
        if (!double.IsFinite(value) || !float.IsFinite(source))
            throw new NotSupportedException("Global time has no complete nonfinite Float32 source/state consumer.");
        return source;
    }

    internal static byte SoundFlag(double value)
    {
        if (!double.IsFinite(value) || value != Math.Truncate(value) || value < int.MinValue || value > int.MaxValue)
            throw new InvalidDataException("Global time sound flag is not a signed Int32 source argument.");
        return value == 0 ? (byte)0 : (byte)1;
    }

    // A failed constructor may throw before returning its IDisposable. The
    // same actual native owner still has to retire the retained binding.
    internal void RetirePresentation(object actualOwner)
    {
        RequireThread(); ArgumentNullException.ThrowIfNull(actualOwner);
        if (_binding is not { } binding) return;
        if (!ReferenceEquals(binding.Owner, actualOwner))
            throw new InvalidDataException("Global time cleanup received a foreign native owner.");
        Retire(binding);
    }

    private void Retire(FalloutGlobalTimeNativeBinding binding)
    {
        RequireThread();
        if (_entered) throw new InvalidOperationException("Global time cannot retire an entered command.");
        if (!ReferenceEquals(_binding, binding))
        {
            if (_binding is null && _retirementFailure is null) return;
            throw new InvalidOperationException("Global time retirement changed the actual native lease.");
        }
        try { binding.Retire(); }
        catch (Exception error) { _retirementFailure ??= error.ToString(); throw; }
        _binding = null; _lastPublication = null; _retirementFailure = null;
    }

    private void RequireThread()
    {
        if (Environment.CurrentManagedThreadId != _thread)
            throw new InvalidOperationException("Global time changed its actual constructed caller thread.");
    }
    private void RequireHealthy()
    {
        RequireThread(); ObjectDisposedException.ThrowIf(_disposed, this);
        if (_entered) throw new InvalidOperationException("Global time cannot reenter an entered source command.");
        if (_failure is not null || _retirementFailure is not null)
            throw new InvalidOperationException("Global time retains a failed source/native prefix: " + (_failure ?? _retirementFailure));
    }
    public void Dispose()
    {
        RequireThread();
        if (_disposed) return;
        if (_entered || _binding is not null)
            throw new InvalidOperationException("Global time must close its native consumer before source retirement.");
        _disposed = true;
    }

    private sealed class Scope(Action release) : IDisposable
    {
        private Action? _release = release;
        public void Dispose() { _release?.Invoke(); _release = null; }
    }
}
