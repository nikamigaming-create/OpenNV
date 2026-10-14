using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutPlayerFollowingTimerFlagObservation(Guid Observation, string Producer, Guid Process, long Changed);
internal sealed record FalloutPlayerFollowingTimerFlagRead(string Source, string Stack, Guid Process, Guid Factory,
    FalloutPlayerSourceCaller Caller, long Revision, byte Value);
internal sealed record FalloutPlayerFollowingTimerFlagSnapshot(string Source, string Stack, Guid Process, Guid Factory,
    Guid Constructor, long Sequence, byte LastOwnedValue, bool Available,
    IReadOnlyList<FalloutPlayerFollowingTimerFlagObservation> Observations,
    FalloutPlayerInhibitionHandoff? Cold, bool Retired, IReadOnlyList<FalloutPlayerFieldPublication>? Publications = null);

// Only the independent source byte has a constructor here. The scalar skipped
// by a zero byte is deliberately absent from this owner and its cold schema.
internal sealed class FalloutPlayerFollowingTimerFlagState
{
    internal const string ObservedWriterBoundary = "source-Player-following-timer-byte-observed-writer-unbound";
    private readonly FalloutPlayerFollowingContinuationSource _source;
    private readonly string _stack;
    private readonly Guid _process, _factory = Guid.NewGuid(), _constructor;
    private readonly Action<FalloutPlayerSourceCaller> _requireActualCaller;
    private readonly List<FalloutPlayerFollowingTimerFlagObservation> _observations = [];
    private readonly HashSet<Guid> _observationIds = [];
    private readonly List<FalloutPlayerFieldPublication> _publications = [];
    private readonly FalloutPlayerInhibitionHandoff? _cold;
    private int _thread = Environment.CurrentManagedThreadId;
    private Guid? _inputFactory;
    private long _sequence, _fault;
    private bool _reading, _retired;
    internal FalloutPlayerFollowingContinuationSource Source => _source;
    internal string Stack => _stack;
    internal Guid Process => _process;
    internal Guid Factory => _factory;
    internal Guid Constructor => _constructor;
    internal bool HasActualInput => _inputFactory is not null;

    internal FalloutPlayerFollowingTimerFlagState(FalloutPlayerFollowingContinuationSource source, string stack,
        Guid process, Guid constructor, Action<FalloutPlayerSourceCaller> requireActualCaller,
        FalloutPlayerFollowingTimerFlagSnapshot? saved = null)
    {
        source.Validate(); ArgumentException.ThrowIfNullOrWhiteSpace(stack); ArgumentNullException.ThrowIfNull(requireActualCaller);
        if (process == Guid.Empty || constructor == Guid.Empty) throw new InvalidDataException("Player timer byte lost its actual selected constructor/process.");
        _source = source; _stack = stack; _process = process; _constructor = constructor; _requireActualCaller = requireActualCaller;
        if (saved is null) { _publications.Add(new(_process, _factory, _constructor)); return; }
        Validate(saved, source, stack);
        if (saved.Process == process || saved.Retired) throw new InvalidDataException("Cold timer byte reused or resurrected the old process.");
        _sequence = saved.Sequence; _observations.AddRange(saved.Observations);
        _observationIds.UnionWith(saved.Observations.Select(value => value.Observation));
        _cold = new(saved.Process, process, saved.Factory, _factory);
        _publications.AddRange(saved.Publications!); _publications.Add(new(_process, _factory, _constructor));
    }
    internal string? SaveBlocker => _reading ? "source-Player-timer-byte-getter-in-flight" :
        _observations.Count != 0 ? ObservedWriterBoundary : null;

    internal void BindActualInput(FalloutDirectInputState input)
    {
        ArgumentNullException.ThrowIfNull(input); ObjectDisposedException.ThrowIf(_retired, this);
        if (_reading || _inputFactory is not null) throw new InvalidOperationException("Player timer byte changed its actual input publication.");
        var current = input.ReadManagerOwner();
        if (!ReferenceEquals(current, input) || input.Source != _stack || input.Factory == Guid.Empty ||
            !FalloutDirectInputState.ValidProvider(input.Provider, input.Window))
            throw new InvalidOperationException("Player timer byte cannot transfer to a foreign current input owner.");
        _thread = Environment.CurrentManagedThreadId; _inputFactory = input.Factory;
    }
    internal void RequireActualInput(FalloutDirectInputState input)
    {
        ArgumentNullException.ThrowIfNull(input); RequireOwner();
        var current = input.ReadManagerOwner();
        if (!ReferenceEquals(current, input) || input.Source != _stack || input.Factory != _inputFactory ||
            !FalloutDirectInputState.ValidProvider(input.Provider, input.Window))
            throw new InvalidOperationException("Player timer byte lost its actual published source/input factory.");
    }
    internal FalloutPlayerFollowingTimerFlagRead ReadCurrent(FalloutPlayerSourceCaller caller)
    {
        RequireOwner();
        if (caller is null || caller.Main == Guid.Empty || caller.Ordinal < 1 || caller.Invocation == Guid.Empty)
            throw new InvalidOperationException("Player timer byte lost its actual entered Main caller.");
        _reading = true; var fault = _fault;
        try
        {
            _requireActualCaller(caller);
            if (_thread != Environment.CurrentManagedThreadId || _fault != fault)
                throw new InvalidOperationException("Player timer byte getter swallowed reentry or changed source thread.");
            if (_observations.Count != 0) throw new NotSupportedException(ObservedWriterBoundary);
            return new(_source.Identity, _stack, _process, _factory, caller, _sequence, 0);
        }
        finally { _reading = false; }
    }
    internal void ObserveUnownedWriter(Guid observation, string producer)
    {
        RequireOwner(); ArgumentException.ThrowIfNullOrWhiteSpace(producer);
        if (observation == Guid.Empty || !_observationIds.Add(observation)) throw new InvalidOperationException("Player timer byte replayed its actual writer observation.");
        _observations.Add(new(observation, producer, _process, checked(++_sequence)));
    }
    internal FalloutPlayerFollowingTimerFlagSnapshot Capture()
    {
        RequireImmutableSnapshot();
        var saved = new FalloutPlayerFollowingTimerFlagSnapshot(_source.Identity, _stack, _process, _factory,
            _constructor, _sequence, 0, _observations.Count == 0, _observations.ToArray(), _cold, _retired, _publications.ToArray());
        Validate(saved, _source, _stack); return saved;
    }
    internal void Retire() { RequireImmutableSnapshot(); _retired = true; }
    private void RequireImmutableSnapshot()
    {
        if (_reading || _inputFactory is not null && _thread != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Entered/published Player timer byte cannot be captured or retired from a foreign source thread.");
    }
    private void RequireOwner(bool allowRetired = false)
    {
        if (!allowRetired) ObjectDisposedException.ThrowIf(_retired, this);
        if (_reading || _thread != Environment.CurrentManagedThreadId)
        {
            _fault = checked(_fault + 1); throw new InvalidOperationException("Player timer byte changed source thread or reentered its getter.");
        }
    }
    internal static void Validate(FalloutPlayerFollowingTimerFlagSnapshot saved, FalloutPlayerFollowingContinuationSource source, string stack)
    {
        source.Validate(); ArgumentNullException.ThrowIfNull(saved);
        if (saved.Source != source.Identity || saved.Stack != stack || saved.Process == Guid.Empty || saved.Factory == Guid.Empty ||
            saved.Constructor == Guid.Empty || saved.LastOwnedValue != 0 || saved.Observations is null ||
            saved.Sequence != saved.Observations.Count || saved.Available != (saved.Observations.Count == 0))
            throw new InvalidDataException("Player timer byte lost its constructor or admitted an unowned timer/scalar writer.");
        var ids = new HashSet<Guid>(); long sequence = 0;
        var publications = FalloutPlayerInhibitionState.ValidatePublications(saved.Publications, saved.Process, saved.Factory, saved.Constructor, saved.Cold);
        var processes = publications.Select(pair => pair.Process).ToHashSet();
        foreach (var observation in saved.Observations)
            if (observation is null || observation.Observation == Guid.Empty || !ids.Add(observation.Observation) ||
                string.IsNullOrWhiteSpace(observation.Producer) || !processes.Contains(observation.Process) || observation.Changed != ++sequence)
                throw new InvalidDataException("Player timer byte lost ordered actual/historical writer observations.");
        if (saved.Cold is { } cold && (cold.Process != saved.Process || cold.Factory != saved.Factory ||
            cold.PreviousProcess == Guid.Empty || cold.PreviousProcess == cold.Process ||
            cold.PreviousFactory == Guid.Empty || cold.PreviousFactory == cold.Factory))
            throw new InvalidDataException("Player timer byte lost its explicit cold lifetime handoff.");
    }
    internal static void RequireHistoricalPublication(FalloutPlayerFollowingTimerFlagSnapshot saved,
        FalloutPlayerFollowingContinuationSource source, string stack, Guid process, Guid factory)
    {
        Validate(saved, source, stack);
        if (!saved.Publications!.Any(entry => entry.Process == process && entry.Factory == factory))
            throw new InvalidDataException("Historical timer-byte getter has no actual retained process/factory publication.");
    }
}
