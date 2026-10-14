using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

// Constructed at the actual selected Player loading lifetime. Both consumers
// borrow this owner. The only permitted loading-thread transfer is publication
// by the genuine current input manager; getters do not bind their first caller.
internal sealed class FalloutPlayerInhibitionState
{
    internal const string Schema = "opennv-Player-inhibition/v1";
    internal const string ObservedWriterBoundary = "source-Player-current-inhibition-observed-writer-unbound";
    private readonly FalloutPlayerFollowingContinuationSource _source;
    private readonly string _stack;
    private readonly Guid _process, _factory = Guid.NewGuid();
    private readonly Guid _constructor;
    private readonly Action<FalloutPlayerInhibitionConsumer, FalloutPlayerSourceCaller> _requireActualCaller;
    private readonly List<FalloutPlayerInhibitionEntry> _history = [];
    private readonly HashSet<Guid> _observations = [];
    private readonly List<FalloutPlayerFieldPublication> _publications = [];
    private readonly FalloutPlayerInhibitionByte[] _bytes = new FalloutPlayerInhibitionByte[4];
    private readonly FalloutPlayerInhibitionHandoff? _cold;
    private int _thread = Environment.CurrentManagedThreadId;
    private Guid? _inputFactory;
    private long _sequence, _fault;
    private bool _reading, _writing, _retired;
    internal FalloutPlayerFollowingContinuationSource Source => _source;
    internal string Stack => _stack;
    internal Guid Process => _process;
    internal Guid Factory => _factory;
    internal Guid Constructor => _constructor;
    internal bool HasActualInput => _inputFactory is not null;

    internal FalloutPlayerInhibitionState(FalloutPlayerFollowingContinuationSource source, string stack, Guid process,
        Guid constructor, Action<FalloutPlayerInhibitionConsumer, FalloutPlayerSourceCaller> requireActualCaller,
        FalloutPlayerInhibitionSnapshot? saved = null)
    {
        source.Validate(); ArgumentException.ThrowIfNullOrWhiteSpace(stack); ArgumentNullException.ThrowIfNull(requireActualCaller);
        if (process == Guid.Empty || constructor == Guid.Empty) throw new InvalidDataException("Player inhibition requires its actual constructor/process.");
        _source = source; _stack = stack; _process = process; _constructor = constructor; _requireActualCaller = requireActualCaller;
        if (saved is not null)
        {
            Validate(saved, source, stack);
            if (saved.Process == process || saved.Retired) throw new InvalidDataException("Cold Player inhibition reused or resurrected its prior source process.");
            _sequence = saved.Sequence; _history.AddRange(saved.History);
            _observations.UnionWith(saved.History.Where(entry => entry.Observation != Guid.Empty).Select(entry => entry.Observation));
            for (var index = 0; index < _bytes.Length; ++index) _bytes[index] = saved.Bytes[index];
            _cold = new(saved.Process, process, saved.Factory, _factory);
            _publications.AddRange(saved.Publications!); _publications.Add(new(_process, _factory, _constructor));
            return;
        }
        _publications.Add(new(_process, _factory, _constructor));
        // This is the source-selected void callback, not an empty external
        // delegate. Its reviewed implementation reads neither receiver nor
        // argument and has no model/process branch. The first constructor's
        // argument is intentionally unclaimed because its sibling is unwritten.
        ConstructorStore(FalloutPlayerInhibitionLane.Third, FalloutPlayerInhibitionOperation.ConstructorLiteral);
        ConstructorStore(FalloutPlayerInhibitionLane.Fourth, FalloutPlayerInhibitionOperation.ConstructorCompletedLoop);
        ConstructorStore(FalloutPlayerInhibitionLane.Second, FalloutPlayerInhibitionOperation.ConstructorLiteral);
        ConstructorCallback(FalloutPlayerInhibitionLane.Second, null);
        ConstructorStore(FalloutPlayerInhibitionLane.First, FalloutPlayerInhibitionOperation.ConstructorLiteral);
        ConstructorCallback(FalloutPlayerInhibitionLane.First, true);
    }

    internal string? SaveBlocker => _reading || _writing ? "source-Player-inhibition-owner-in-flight" :
        _bytes.Any(value => !value.Available) ? ObservedWriterBoundary : null;

    internal void BindActualInput(FalloutDirectInputState input)
    {
        ArgumentNullException.ThrowIfNull(input); ObjectDisposedException.ThrowIf(_retired, this);
        if (_reading || _writing || _inputFactory is not null) throw new InvalidOperationException("Player inhibition changed its actual publication.");
        var current = input.ReadManagerOwner();
        if (!ReferenceEquals(current, input) || input.Source != _stack || input.Factory == Guid.Empty ||
            !FalloutDirectInputState.ValidProvider(input.Provider, input.Window))
            throw new InvalidOperationException("Player inhibition cannot transfer to foreign input/source/thread ownership.");
        _thread = Environment.CurrentManagedThreadId; _inputFactory = input.Factory;
    }

    internal void RequireActualInput(FalloutDirectInputState input)
    {
        ArgumentNullException.ThrowIfNull(input); RequireOwner();
        var current = input.ReadManagerOwner();
        if (!ReferenceEquals(current, input) || input.Source != _stack || input.Factory != _inputFactory ||
            !FalloutDirectInputState.ValidProvider(input.Provider, input.Window))
            throw new InvalidOperationException("Player inhibition lost its actual published source/input factory.");
    }

    internal FalloutPlayerInhibitionPredicate ReadCurrent(FalloutPlayerInhibitionConsumer consumer,
        FalloutPlayerSourceCaller caller, Action<FalloutPlayerInhibitionRead>? observe = null)
    {
        RequireOwner(); _reading = true; var fault = _fault;
        var reads = new List<FalloutPlayerInhibitionRead>(4);
        try
        {
            RequireCaller(consumer, caller);
            if (_thread != Environment.CurrentManagedThreadId || _fault != fault)
                throw new InvalidOperationException("Player inhibition caller swallowed reentry or changed source thread.");
            for (var index = 0; index < _bytes.Length; ++index)
            {
                if (!_bytes[index].Available) throw new NotSupportedException(ObservedWriterBoundary + ":" + (FalloutPlayerInhibitionLane)index);
                var read = new FalloutPlayerInhibitionRead(_source.Identity, _stack, _process, _factory, caller,
                    consumer, _sequence, (FalloutPlayerInhibitionLane)index, _bytes[index].Value);
                reads.Add(read); observe?.Invoke(read); Check();
                if (read.Value != 0) return new(reads.ToArray(), true);
            }
            return new(reads.ToArray(), false);
        }
        finally { _reading = false; }
        void Check()
        {
            _requireActualCaller(consumer, caller);
            if (_thread != Environment.CurrentManagedThreadId || _fault != fault || _sequence != reads[^1].Revision)
                throw new InvalidOperationException("Player inhibition getter swallowed reentry or changed its current field owner.");
        }
    }

    // These two source zero setters store their byte before the selected void
    // callback. Callers must enter them at the actual source setter, rather than
    // declaring a larger service's preceding/following effects returned.
    internal void ApplySourceZeroSetter(Guid observation, string producer, FalloutPlayerInhibitionLane lane)
    {
        RequireOwner(); ArgumentException.ThrowIfNullOrWhiteSpace(producer);
        if (lane is not (FalloutPlayerInhibitionLane.First or FalloutPlayerInhibitionLane.Second))
            throw new NotSupportedException("source-Player-other-inhibition-byte-writer-effects-unbound");
        RequireObservation(observation); _writing = true;
        try
        {
            _bytes[(int)lane] = new(0, true);
            _history.Add(new(observation, producer, _process, _factory, Next(), FalloutPlayerInhibitionOperation.ZeroSetter, lane, 0, null, false));
            var others = _bytes.Where((_, index) => index != (int)lane).ToArray();
            bool? argument = others.Any(value => value.Available && value.Value != 0) ? false :
                others.All(value => value.Available) ? true : null;
            _history.Add(new(Guid.Empty, producer, _process, _factory, Next(), FalloutPlayerInhibitionOperation.SelectedVoidCallback,
                lane, null, argument, true));
        }
        finally { _writing = false; }
    }

    internal void ObserveUnownedWriter(Guid observation, string producer, FalloutPlayerInhibitionLane lane)
    {
        RequireOwner(); ArgumentException.ThrowIfNullOrWhiteSpace(producer);
        if (!Enum.IsDefined(lane)) throw new InvalidDataException("Unknown current inhibition writer lane.");
        RequireObservation(observation);
        _bytes[(int)lane] = _bytes[(int)lane] with { Available = false };
        _history.Add(new(observation, producer, _process, _factory, Next(), FalloutPlayerInhibitionOperation.UnownedWriter, lane, null, null, false));
    }

    internal FalloutPlayerInhibitionSnapshot Capture()
    {
        RequireImmutableSnapshot();
        var saved = new FalloutPlayerInhibitionSnapshot(Schema, _source.Identity, _stack, _process, _factory,
            _constructor, _sequence, _bytes.ToArray(), _history.ToArray(), _cold, _retired, _publications.ToArray());
        Validate(saved, _source, _stack); return saved;
    }
    internal void Retire() { RequireImmutableSnapshot(); _retired = true; }

    private void RequireImmutableSnapshot()
    {
        // Loading diagnostics may borrow completed immutable preparation from
        // another thread before actual input publication. This neither binds
        // a gameplay thread nor permits a field getter or writer there.
        if (_reading || _writing || _inputFactory is not null && _thread != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Entered/published Player inhibition cannot be captured or retired from a foreign source thread.");
    }

    private void ConstructorStore(FalloutPlayerInhibitionLane lane, FalloutPlayerInhibitionOperation operation)
    {
        _bytes[(int)lane] = new(0, true);
        _history.Add(new(Guid.Empty, "actual-selected-Player-constructor", _process, _factory, Next(), operation, lane, 0, null, false));
    }
    private void ConstructorCallback(FalloutPlayerInhibitionLane lane, bool? argument) =>
        _history.Add(new(Guid.Empty, "actual-selected-Player-constructor", _process, _factory, Next(),
            FalloutPlayerInhibitionOperation.SelectedVoidCallback, lane, null, argument, true));
    private void RequireObservation(Guid observation)
    {
        if (observation == Guid.Empty || !_observations.Add(observation))
            throw new InvalidOperationException("Player inhibition writer replayed its actual observation.");
    }
    private void RequireCaller(FalloutPlayerInhibitionConsumer consumer, FalloutPlayerSourceCaller caller)
    {
        if (!Enum.IsDefined(consumer) || caller is null || caller.Main == Guid.Empty || caller.Ordinal < 1 || caller.Invocation == Guid.Empty)
            throw new InvalidOperationException("Player inhibition lost its actual entered source caller.");
        _requireActualCaller(consumer, caller);
    }
    private void RequireOwner(bool allowRetired = false)
    {
        if (!allowRetired) ObjectDisposedException.ThrowIf(_retired, this);
        if (_thread != Environment.CurrentManagedThreadId || _reading || _writing)
        {
            _fault = checked(_fault + 1); throw new InvalidOperationException("Player inhibition changed its source thread or reentered its owner.");
        }
    }
    private long Next() => _sequence = checked(_sequence + 1);

    internal static void Validate(FalloutPlayerInhibitionSnapshot saved, FalloutPlayerFollowingContinuationSource source, string stack)
    {
        source.Validate(); ArgumentNullException.ThrowIfNull(saved);
        if (saved.Schema != Schema || saved.Source != source.Identity || saved.Stack != stack || saved.Process == Guid.Empty ||
            saved.Factory == Guid.Empty || saved.Constructor == Guid.Empty || saved.Bytes is not { Count: 4 } ||
            saved.History is not { Count: >= 6 } || saved.History[0] is null ||
            saved.Sequence != saved.History.Count || saved.Bytes.Any(value => value is null))
            throw new InvalidDataException("Player inhibition lost its selected constructor, complete field lanes or ordered history.");
        var values = new FalloutPlayerInhibitionByte[4]; var observed = new HashSet<Guid>();
        var publications = ValidatePublications(saved.Publications, saved.Process, saved.Factory, saved.Constructor, saved.Cold);
        if (saved.History[0].Process != saved.Publications![0].Process || saved.History[0].Factory != saved.Publications[0].Factory)
            throw new InvalidDataException("Inhibition constructor history changed its actual initial field publication.");
        var construction = new[] { FalloutPlayerInhibitionLane.Third, FalloutPlayerInhibitionLane.Fourth,
            FalloutPlayerInhibitionLane.Second, FalloutPlayerInhibitionLane.Second, FalloutPlayerInhibitionLane.First, FalloutPlayerInhibitionLane.First };
        for (var index = 0; index < saved.History.Count; ++index)
        {
            var entry = saved.History[index] ?? throw new InvalidDataException("Player inhibition contains a null field-history row.");
            if (entry.Process == Guid.Empty || entry.Factory == Guid.Empty || string.IsNullOrWhiteSpace(entry.Producer) ||
                entry.Changed != index + 1 || !Enum.IsDefined(entry.Operation) || !Enum.IsDefined(entry.Lane) ||
                !publications.Contains((entry.Process, entry.Factory)) ||
                entry.Observation != Guid.Empty && !observed.Add(entry.Observation))
                throw new InvalidDataException("Player inhibition changed its actual historical writer/process identity or order.");
            if (index < 6)
            {
                var callback = index is 3 or 5;
                var expected = callback ? FalloutPlayerInhibitionOperation.SelectedVoidCallback :
                    index == 1 ? FalloutPlayerInhibitionOperation.ConstructorCompletedLoop : FalloutPlayerInhibitionOperation.ConstructorLiteral;
                if (entry.Process != saved.History[0].Process || entry.Factory != saved.History[0].Factory ||
                    entry.Producer != "actual-selected-Player-constructor" || entry.Lane != construction[index] || entry.Operation != expected || entry.Observation != Guid.Empty ||
                    entry.CallbackReturned != callback || entry.Value != (callback ? null : (byte?)0) ||
                    entry.CallbackArgument != (index == 5 ? true : null))
                    throw new InvalidDataException("Player inhibition changed source constructor store/callback order or invented an unwritten argument.");
            }
            else if (entry.Operation is FalloutPlayerInhibitionOperation.ConstructorLiteral or FalloutPlayerInhibitionOperation.ConstructorCompletedLoop ||
                entry.Operation == FalloutPlayerInhibitionOperation.SelectedVoidCallback &&
                    (entry.Observation != Guid.Empty || entry.Value is not null || !entry.CallbackReturned ||
                        saved.History[index - 1] is not { Operation: FalloutPlayerInhibitionOperation.ZeroSetter } setter || setter.Lane != entry.Lane ||
                        setter.Process != entry.Process || setter.Factory != entry.Factory || setter.Producer != entry.Producer) ||
                entry.Operation == FalloutPlayerInhibitionOperation.ZeroSetter &&
                    (entry.Lane is not (FalloutPlayerInhibitionLane.First or FalloutPlayerInhibitionLane.Second) || entry.Value != 0 ||
                        entry.CallbackReturned || entry.CallbackArgument is not null || entry.Observation == Guid.Empty ||
                        index + 1 == saved.History.Count || saved.History[index + 1].Operation != FalloutPlayerInhibitionOperation.SelectedVoidCallback) ||
                entry.Operation == FalloutPlayerInhibitionOperation.UnownedWriter &&
                    (entry.Value is not null || entry.CallbackReturned || entry.CallbackArgument is not null || entry.Observation == Guid.Empty))
                throw new InvalidDataException("Player inhibition admitted an unowned writer or lost its source store-before-callback prefix.");
            if (entry.Value is { } value) values[(int)entry.Lane] = new(value, true);
            if (entry.Operation == FalloutPlayerInhibitionOperation.UnownedWriter)
                values[(int)entry.Lane] = values[(int)entry.Lane] with { Available = false };
            if (index >= 6 && entry.Operation == FalloutPlayerInhibitionOperation.SelectedVoidCallback)
            {
                var others = values.Where((_, lane) => lane != (int)entry.Lane).ToArray();
                bool? argument = others.Any(value => value.Available && value.Value != 0) ? false :
                    others.All(value => value.Available) ? true : null;
                if (entry.CallbackArgument != argument) throw new InvalidDataException("Player inhibition invented its current setter's ignored callback argument.");
            }
        }
        if (!values.SequenceEqual(saved.Bytes) || saved.Cold is { } cold &&
            (cold.PreviousProcess == Guid.Empty || cold.Process != saved.Process || cold.PreviousProcess == cold.Process ||
                cold.PreviousFactory == Guid.Empty || cold.Factory != saved.Factory || cold.PreviousFactory == cold.Factory))
            throw new InvalidDataException("Player inhibition current fields differ from retained source writes or cold publication.");
    }

    internal static void RequireHistoricalPublication(FalloutPlayerInhibitionSnapshot saved,
        FalloutPlayerFollowingContinuationSource source, string stack, Guid process, Guid factory)
    {
        Validate(saved, source, stack);
        if (!saved.Publications!.Any(entry => entry.Process == process && entry.Factory == factory))
            throw new InvalidDataException("Historical inhibition read has no actual retained process/factory publication.");
    }

    internal static HashSet<(Guid Process, Guid Factory)> ValidatePublications(IReadOnlyList<FalloutPlayerFieldPublication>? publications,
        Guid process, Guid factory, Guid constructor, FalloutPlayerInhibitionHandoff? cold)
    {
        if (publications is not { Count: > 0 } || publications[^1] != new FalloutPlayerFieldPublication(process, factory, constructor))
            throw new InvalidDataException("Player source field lost its complete current/cold publication lineage.");
        var processes = new HashSet<Guid>(); var factories = new HashSet<Guid>(); var constructors = new HashSet<Guid>();
        var pairs = new HashSet<(Guid Process, Guid Factory)>();
        foreach (var entry in publications)
            if (entry is null || entry.Process == Guid.Empty || entry.Factory == Guid.Empty || entry.Constructor == Guid.Empty ||
                !processes.Add(entry.Process) || !factories.Add(entry.Factory) || !constructors.Add(entry.Constructor) ||
                !pairs.Add((entry.Process, entry.Factory)))
                throw new InvalidDataException("Player source field publication lineage contains foreign or duplicate lifetime identities.");
        if (publications.Count == 1 ? cold is not null : cold is null || cold.PreviousProcess != publications[^2].Process ||
            cold.PreviousFactory != publications[^2].Factory || cold.Process != process || cold.Factory != factory)
            throw new InvalidDataException("Player source field immediate handoff differs from its ordered complete publication lineage.");
        return pairs;
    }
}
