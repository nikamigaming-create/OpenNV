using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Gameplay.State;

// One source PlayerMover owns its inherited ActorMover cells and its distinct
// movement cells. Package reset and Player getters borrow this same object.
internal sealed partial class FalloutActorMoverState
{
    internal const string Schema = "opennv-Actor-mover/v1";
    private const string PackageResetKind = "package-assignment-reset";
    private const string UnownedWriterKind = "observed-unowned-writer";
    private readonly Action _requireLifetime;
    private readonly List<FalloutActorMoverStore> _constructorStores = [];
    private readonly List<FalloutActorMoverOperation> _operations = [];
    private readonly List<FalloutActorMoverPublication> _publications = [];
    private readonly Dictionary<FalloutActorMoverLane, FalloutActorMoverAvailability> _availability = [];
    private readonly HashSet<(Guid, string, FalloutActorMoverLane?)> _observations = [];
    private FalloutActorMoverValues _values = new(FalloutActorMoverVector.Default, FalloutActorMoverVector.Default,
        FalloutActorMoverSource.InitialInheritedFlags, 0, FalloutPlayerMoverFields.Constructed);
    private FalloutCombatActorIdentity? _actor;
    private Action<FalloutPackageCommandFieldInvocation>? _requirePackageInvocation;
    private Func<FalloutPackageCommandFieldInvocation, FalloutActorMoverResetJoin>? _readReturnedResetJoin;
    private long _sequence, _fault;
    private int _thread = Environment.CurrentManagedThreadId;
    private Guid? _inputFactory;
    private string? _failure;
    private bool _published, _busy, _retired;
    internal FalloutActorMoverSource Source { get; }
    internal string Stack { get; }
    internal Guid Process { get; }
    internal Guid Factory { get; } = Guid.NewGuid();
    internal Guid Constructor { get; }
    internal FalloutActorMoverOperation? LastPackageReset { get; private set; }
    internal string? SaveBlocker => _retired ? "source-Actor-mover-retired" : _busy ? "source-Actor-mover-in-flight" :
        _failure ?? _frameFailure ??
        _availability.Values.FirstOrDefault(value => !value.Available)?.Boundary;
    internal object State => new
    {
        source = Source.Identity,
        Stack,
        Process,
        Factory,
        Constructor,
        values = _values,
        sequence = _sequence,
        lastPackageReset = LastPackageReset,
        failure = _failure,
        retired = _retired,
        saveBlocker = SaveBlocker
    };

    internal FalloutActorMoverState(FalloutActorMoverSource source, string stack, Guid process, Guid constructor,
        Action requireLifetime, FalloutActorMoverSnapshot? saved = null)
    {
        source.Validate(); ArgumentException.ThrowIfNullOrWhiteSpace(stack); ArgumentNullException.ThrowIfNull(requireLifetime);
        if (process == Guid.Empty || constructor == Guid.Empty)
            throw new InvalidDataException("Actor mover requires its genuine Player constructor and process lifetime.");
        Source = source; Stack = stack; Process = process; Constructor = constructor; _requireLifetime = requireLifetime;
        foreach (var lane in Enum.GetValues<FalloutActorMoverLane>()) _availability.Add(lane, new(lane, true, null));
        if (saved is null)
        {
            _publications.Add(new(Process, Factory, Constructor));
            foreach (var store in InitialStores()) _constructorStores.Add(store);
            _sequence = _constructorStores[^1].Changed;
            return;
        }
        Validate(saved, source, stack);
        if (saved.Process == process || saved.Retired)
            throw new InvalidDataException("Cold Actor mover reused or resurrected its prior process.");
        _sequence = saved.Sequence; _values = saved.Values; _actor = saved.Actor;
        _constructorStores.AddRange(saved.ConstructorStores);
        foreach (var available in saved.Availability) _availability[available.Lane] = available;
        _publications.AddRange(saved.Publications); _publications.Add(new(Process, Factory, Constructor, saved.Process, saved.Factory));
        _operations.AddRange(saved.Operations.Select(Clone));
        _observations.UnionWith(saved.Operations.Select(operation => (operation.Invocation.SourceCommand, operation.Kind, operation.InvalidatedLane)));
        _failure = saved.Operations.FirstOrDefault(operation => !operation.Returned)?.Failure;
        LastPackageReset = _operations.LastOrDefault(operation => operation.Kind == PackageResetKind);
        RestorePlayerFrame(saved.PlayerFrame);
    }

    internal void BindActualInput(FalloutDirectInputState input)
    {
        ArgumentNullException.ThrowIfNull(input); ObjectDisposedException.ThrowIf(_retired, this);
        if (_busy) throw new InvalidOperationException("Actor mover changed its actual source publication.");
        if (!ReferenceEquals(input.ReadManagerOwner(), input) || input.Source != Stack || input.Factory == Guid.Empty ||
            !FalloutDirectInputState.ValidProvider(input.Provider, input.Window))
            throw new InvalidOperationException("Actor mover cannot publish through foreign input/source ownership.");
        _requireLifetime();
        if (_published)
        {
            if (_inputFactory != input.Factory || _thread != Environment.CurrentManagedThreadId)
                throw new InvalidOperationException("Actor mover changed its published actual manager or thread.");
            return;
        }
        _thread = Environment.CurrentManagedThreadId; _inputFactory = input.Factory; _published = true;
    }

    internal void BindPackageReset(FalloutCombatActorIdentity actor,
        Action<FalloutPackageCommandFieldInvocation> requireCurrent,
        Func<FalloutPackageCommandFieldInvocation, FalloutActorMoverResetJoin> readReturnedPredecessors)
    {
        RequireOwner(); actor.Validate(); ArgumentNullException.ThrowIfNull(requireCurrent); ArgumentNullException.ThrowIfNull(readReturnedPredecessors);
        if (!actor.EnginePlayer || _requirePackageInvocation is not null || _actor is not null && _actor != actor)
            throw new InvalidOperationException("Actor mover changed its selected actual Player owner or reset publication.");
        _actor = actor; _requirePackageInvocation = requireCurrent; _readReturnedResetJoin = readReturnedPredecessors;
    }

    internal FalloutPlayerMoverFields ReadPlayerFields()
    {
        RequireOwner(); _requireLifetime(); RequireAvailable(FalloutActorMoverLane.PlayerFields); return _values.Player;
    }
    internal FalloutPlayerMoverFields ReadImmutablePlayerFields() { RequireSnapshot(); return _values.Player; }

    internal void ResetForPackageAssignment(FalloutPackageCommandFieldInvocation invocation) =>
        Execute(invocation, PackageResetKind, scope =>
        {
            var join = (_readReturnedResetJoin ?? throw new NotSupportedException("source-Actor-mover-package-reset-predecessors-unbound"))(invocation);
            Guard(scope); ValidateJoin(join, invocation, Source, Stack); scope.Join = CloneJoin(join);
            RequireAvailable(FalloutActorMoverLane.DefaultVector);
            // These are the original pure stores. The enclosing Actor package
            // caller still owns its gates and following follower mutations.
            Store(scope, FalloutActorMoverField.InheritedVectorXY, 0, 0);
            Store(scope, FalloutActorMoverField.InheritedVectorZ, 0);
            Store(scope, FalloutActorMoverField.ResetByte, 0);
        });

    internal void ObserveUnownedWriter(FalloutPackageCommandFieldInvocation invocation,
        FalloutActorMoverLane lane, string boundary) => Execute(invocation, UnownedWriterKind, scope =>
        {
            if (!Enum.IsDefined(lane)) throw new InvalidDataException("Unknown Actor mover writer lane.");
            ArgumentException.ThrowIfNullOrWhiteSpace(boundary);
            scope.InvalidatedLane = lane; scope.InvalidationBoundary = boundary;
            _availability[lane] = new(lane, false, boundary);
        }, lane);

    internal FalloutActorMoverSnapshot Capture()
    {
        RequireSnapshot();
        var saved = new FalloutActorMoverSnapshot(Schema, Source, Stack, Process, Factory, Constructor, _sequence, _values,
            Array.AsReadOnly(_constructorStores.ToArray()), Array.AsReadOnly(_availability.Values.ToArray()),
            Array.AsReadOnly(_publications.ToArray()), Array.AsReadOnly(_operations.Select(Clone).ToArray()), _retired, _actor,
            CapturePlayerFrame());
        Validate(saved, Source, Stack); return saved;
    }
    internal void Retire() { RequireSnapshot(); _retired = true; }

    private void Execute(FalloutPackageCommandFieldInvocation invocation, string kind, Action<Scope> action,
        FalloutActorMoverLane? observationLane = null)
    {
        RequireOwner(); ArgumentNullException.ThrowIfNull(invocation);
        FalloutPackageRuntimeRegistry.RequireInvocation(invocation);
        if (_actor != invocation.Actor || _requirePackageInvocation is null)
            throw new NotSupportedException("source-Actor-mover-actual-package-caller-unbound");
        if (_observations.Contains((invocation.SourceCommand, kind, observationLane)))
            throw new InvalidOperationException("Actor mover replayed its actual source operation.");
        _requirePackageInvocation(invocation); _requireLifetime();
        var scope = new Scope(invocation, kind, Next(), _fault); _busy = true;
        try
        {
            Guard(scope);
            _observations.Add((invocation.SourceCommand, kind, observationLane));
            action(scope); Guard(scope); Complete(scope, true, null);
        }
        catch (Exception failure) { Complete(scope, false, failure.Message); throw; }
        finally { _busy = false; }
    }
    private void Guard(Scope scope)
    {
        FalloutPackageRuntimeRegistry.RequireInvocation(scope.Invocation);
        if (_actor != scope.Invocation.Actor || _requirePackageInvocation is null)
            throw new NotSupportedException("source-Actor-mover-actual-package-caller-unbound");
        _requirePackageInvocation(scope.Invocation); _requireLifetime();
        if (_retired || _thread != Environment.CurrentManagedThreadId || _fault != scope.Fault)
            throw new InvalidOperationException("Actor mover callback swallowed reentry or changed its actual thread/lifetime.");
    }
    private void Store(Scope scope, FalloutActorMoverField field, uint bits, uint? secondBits = null)
    {
        Guard(scope); var store = new FalloutActorMoverStore(field, Next(), bits, secondBits);
        _values = Apply(_values, store); scope.Stores.Add(store);
        if (field == FalloutActorMoverField.InheritedVectorZ) _availability[FalloutActorMoverLane.InheritedVector] = new(FalloutActorMoverLane.InheritedVector, true, null);
        if (field == FalloutActorMoverField.ResetByte) _availability[FalloutActorMoverLane.ResetByte] = new(FalloutActorMoverLane.ResetByte, true, null);
        Guard(scope);
    }
    private void Complete(Scope scope, bool returned, string? failure)
    {
        var operation = new FalloutActorMoverOperation(scope.Invocation, Process, Factory, scope.Kind, scope.Entered, Next(),
            Array.AsReadOnly(scope.Stores.ToArray()), returned, failure, scope.Join, scope.InvalidatedLane, scope.InvalidationBoundary);
        _operations.Add(operation); if (scope.Kind == PackageResetKind) LastPackageReset = operation;
        if (!returned) _failure ??= failure;
    }
    private void RequireAvailable(FalloutActorMoverLane lane)
    {
        if (_availability[lane] is { Available: false, Boundary: { } boundary }) throw new NotSupportedException(boundary);
    }
    private void RequireOwner()
    {
        ObjectDisposedException.ThrowIf(_retired, this);
        if (_thread != Environment.CurrentManagedThreadId || _busy)
        { _fault = checked(_fault + 1); throw new InvalidOperationException("Actor mover changed its source thread or reentered its actual owner."); }
    }
    private void RequireSnapshot()
    {
        if (_busy || _published && _thread != Environment.CurrentManagedThreadId)
        { _fault = checked(_fault + 1); throw new InvalidOperationException("Actor mover capture/retirement reentered or escaped its published thread."); }
    }
    private long Next() => _sequence = checked(_sequence + 1);
    private sealed class Scope(FalloutPackageCommandFieldInvocation invocation, string kind, long entered, long fault)
    {
        internal FalloutPackageCommandFieldInvocation Invocation { get; } = invocation;
        internal string Kind { get; } = kind;
        internal long Entered { get; } = entered;
        internal long Fault { get; } = fault;
        internal List<FalloutActorMoverStore> Stores { get; } = [];
        internal FalloutActorMoverResetJoin? Join;
        internal FalloutActorMoverLane? InvalidatedLane;
        internal string? InvalidationBoundary;
    }
}
