using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

// The World constructs one field owner for the actual source Actor lifetime.
// Pending requests, native poses and scene-node existence supply no fields here.
internal sealed partial class FalloutActorPackageCommandFields
{
    internal const string Schema = "opennv-package-command-fields/v1";
    internal const string PrecedingWriterBoundary = "source-Actor-package-command-preceding-field-writers-unbound";
    internal const string AdjustedPositionBoundary = "source-Actor-package-command-adjusted-position-producer-unbound";
    internal const string WorldspaceBoundary = "source-Actor-package-command-Worldspace-and-persistent-CELL-getter-unbound";
    internal const string ReferenceChangeBoundary = "source-reference-change-mask-800-manager-consumer-unbound";
    internal const string AnimationBoundary = "source-package-start-nonnull-animation-context-consumers-unbound";
    private readonly Action<FalloutPackageCommandFieldInvocation> _requireActualInvocation;
    private readonly List<FalloutPackageCommandFieldPublication> _publications = [];
    private readonly List<FalloutPackageCommandFieldOperation> _operations = [];
    private readonly Dictionary<FalloutPackageCommandFieldLane, FalloutPackageCommandFieldAvailability> _availability = [];
    private FalloutPackageCommandFieldValues _values = FalloutPackageCommandFieldValues.Constructed;
    private long _sequence, _fault;
    private int? _thread;
    private bool _busy, _retired;
    private string? _failure;
    internal FalloutPackageCommandFieldsSource Source { get; }
    internal string Stack { get; }
    internal FalloutCombatActorIdentity Actor { get; }
    internal Guid Process { get; }
    internal long Epoch { get; }
    internal Guid Factory { get; } = Guid.NewGuid();
    internal Guid Constructor { get; }
    internal FalloutPackageCommandFieldConstruction Construction { get; }
    internal FalloutPackageCommandEventMark? LastEventMark { get; private set; }
    internal FalloutPackageCommandAnimationRead? LastAnimationRead { get; private set; }
    internal FalloutPackageCommandSavedPositionCall? LastSavedPositionCall { get; private set; }
    internal string? SaveBlocker => _busy ? "source-package-command-field-consumer-in-flight" : _retired ?
        "source-package-command-field-owner-retired" : _failure ?? _scriptInitializer?.SaveBlocker ??
        (_savedScriptInitialization is not null && _scriptInitializer is null ? "source-cold-Script-initializer-current-binding-unbound" : null) ??
        _availability.Values.FirstOrDefault(value => !value.Available)?.Boundary ?? _lateAnimationWriter ?? _animationNodeMembership?.Writer ??
        (_historicalAnimationTailFields?.Membership is not null && _animationNodeMembership is null ?
            "source-cold-reference-Extra41-publication-unbound" : null);
    internal object State => new
    {
        source = Source.Identity,
        Stack,
        Actor,
        Process,
        Epoch,
        Factory,
        Constructor,
        Construction,
        values = CloneValues(_values),
        availability = _availability.Values.ToArray(),
        sequence = _sequence,
        lastEventMark = LastEventMark,
        lastAnimationRead = LastAnimationRead,
        lastSavedPositionCall = LastSavedPositionCall,
        scriptInitialization = _scriptInitializer?.LastCall,
        failure = _failure,
        retired = _retired,
        saveBlocker = SaveBlocker
    };

    internal FalloutActorPackageCommandFields(FalloutPackageCommandFieldsSource source, string stack,
        FalloutCombatActorIdentity actor, Guid process, long epoch, Guid constructor,
        Action<FalloutPackageCommandFieldInvocation> requireActualInvocation,
        FalloutPackageCommandFieldConstruction? construction = null, FalloutPackageCommandFieldSnapshot? saved = null)
    {
        source.Validate(); actor.Validate(); ArgumentException.ThrowIfNullOrWhiteSpace(stack);
        ArgumentNullException.ThrowIfNull(requireActualInvocation);
        if (process == Guid.Empty || constructor == Guid.Empty || epoch < 1)
            throw new InvalidDataException("Package command constructor lacks its actual Actor/process epoch.");
        Source = source; Stack = stack; Actor = actor; Process = process; Epoch = epoch; Constructor = constructor;
        _requireActualInvocation = requireActualInvocation;
        var admission = construction ?? saved?.Construction ?? new("actual-selected-Actor-package-command-field-constructor");
        Construction = admission with { ClosedLanes = admission.ClosedLanes is { } lanes ? Array.AsReadOnly(lanes.ToArray()) : null };
        RestoreAnimationTailFields(saved?.AnimationTail);
        ValidateConstruction(Construction);
        foreach (var lane in Enum.GetValues<FalloutPackageCommandFieldLane>())
        {
            var admitted = Construction.PrecedingWritersClosed || Construction.ClosedLanes?.Contains(lane) == true;
            _availability.Add(lane, new(lane, admitted, admitted ? null : Construction.Boundary ?? PrecedingWriterBoundary));
        }
        if (saved is null) _publications.Add(new(Process, Factory, Constructor, Epoch));
        else
        {
            Validate(saved, source, stack, actor);
            if (saved.Process == process || saved.Retired || saved.Epoch != epoch || !EquivalentConstruction(saved.Construction, Construction))
                throw new InvalidDataException("Cold package fields reused a process, retired Actor, epoch or constructor admission.");
            _sequence = saved.Sequence; _values = CloneValues(saved.Values);
            foreach (var lane in saved.Availability) _availability[lane.Lane] = lane;
            _publications.AddRange(saved.Publications);
            _publications.Add(new(Process, Factory, Constructor, Epoch, saved.Process, saved.Factory));
            _operations.AddRange(saved.Operations.Select(CloneOperation));
            _savedScriptInitialization = saved.ScriptInitialization is { } script ? FalloutReferenceScriptExtraInitializer.CloneSnapshot(script) : null;
            _scriptInitializationOperations.AddRange((saved.ScriptInitializationOperations ?? []).Select(CloneScriptInitializationOperation));
            _failure = saved.Operations.FirstOrDefault(value => !value.Returned)?.Failure;
            _failure ??= _scriptInitializationOperations.FirstOrDefault(value => !value.Returned)?.Failure;
            LastEventMark = _operations.LastOrDefault(value => value.EventMark is not null)?.EventMark;
            LastAnimationRead = _operations.LastOrDefault(value => value.AnimationRead is not null)?.AnimationRead;
            LastSavedPositionCall = _operations.LastOrDefault(value => value.SavedPositionCall is not null)?.SavedPositionCall;
            LastAnimationInitialization = _operations.LastOrDefault(value => value.AnimationInitialization is not null)?.AnimationInitialization;
            LastSavedPositionInitialization = _operations.LastOrDefault(value => value.SavedPositionInitialization is not null)?.SavedPositionInitialization;
        }
    }

    internal FalloutPackageRawPosition ReadCurrentPosition(FalloutPackageCommandFieldInvocation invocation) =>
        Execute(invocation, "current-position-getter", scope => ReadPosition(scope));

    // These are exact field-store children of actual writers. The enclosing
    // command/placement owner still owns its remaining callbacks and return.
    internal void StoreCurrentPosition(FalloutPackageCommandFieldInvocation invocation, FalloutPackageRawPosition position) =>
        Execute(invocation, "current-position-store", scope =>
        {
            ArgumentNullException.ThrowIfNull(position);
            Store(scope, FalloutPackageCommandField.PositionX, bits: position.X);
            Store(scope, FalloutPackageCommandField.PositionY, bits: position.Y);
            Store(scope, FalloutPackageCommandField.PositionZ, bits: position.Z);
            Admit(FalloutPackageCommandFieldLane.Position); return true;
        });
    internal void StoreCurrentParentCell(FalloutPackageCommandFieldInvocation invocation, FalloutFormKey? cell) =>
        Execute(invocation, "current-ParentCELL-store", scope =>
        { ValidateForm(cell); Store(scope, FalloutPackageCommandField.ParentCell, form: cell); Admit(FalloutPackageCommandFieldLane.ParentCell); return true; });
    internal void StoreRawRotationZ(FalloutPackageCommandFieldInvocation invocation, uint bits) =>
        Execute(invocation, "raw-rotation-Z-store", scope =>
        { Store(scope, FalloutPackageCommandField.RotationZ, bits: bits); Admit(FalloutPackageCommandFieldLane.RotationZ); return true; });

    internal void ObserveUnownedWriter(FalloutPackageCommandFieldInvocation invocation,
        FalloutPackageCommandFieldLane lane, string boundary) => Execute(invocation, "observed-unowned-writer", scope =>
        {
            if (!Enum.IsDefined(lane)) throw new InvalidDataException("Unknown package-command writer lane.");
            ArgumentException.ThrowIfNullOrWhiteSpace(boundary);
            scope.InvalidatedLane = lane; scope.InvalidationBoundary = boundary;
            _availability[lane] = new(lane, false, boundary); return true;
        });

    internal FalloutPackageCommandFieldSnapshot Capture()
    {
        RequireSnapshot();
        var snapshot = new FalloutPackageCommandFieldSnapshot(Schema, Source, Stack, Actor, Process, Factory, Constructor,
            Epoch, Construction, _sequence, CloneValues(_values), Array.AsReadOnly(_availability.Values.ToArray()),
            Array.AsReadOnly(_publications.ToArray()), Array.AsReadOnly(_operations.Select(CloneOperation).ToArray()), _retired,
            _scriptInitializer?.Capture() ?? (_savedScriptInitialization is { } script ? FalloutReferenceScriptExtraInitializer.CloneSnapshot(script) : null),
            _scriptInitializationOperations.Count == 0 ? null : Array.AsReadOnly(_scriptInitializationOperations.Select(CloneScriptInitializationOperation).ToArray()),
            CaptureAnimationTailFields());
        Validate(snapshot, Source, Stack, Actor); return snapshot;
    }
    internal void Retire() { RequireSnapshot(); _retired = true; }

    private T Execute<T>(FalloutPackageCommandFieldInvocation invocation, string kind, Func<FieldScope, T> action,
        Action<FieldScope>? entered = null)
    {
        RequireOwner(); ArgumentNullException.ThrowIfNull(invocation);
        var scope = new FieldScope(invocation, kind, Next(), _fault); _busy = true;
        try { entered?.Invoke(scope); Guard(scope); var result = action(scope); Guard(scope); Complete(scope, true, null); return result; }
        catch (Exception failure)
        {
            var message = string.IsNullOrWhiteSpace(failure.Message) ? failure.GetType().Name : failure.Message;
            _failure ??= message; Complete(scope, false, message); throw;
        }
        finally { _busy = false; }
    }
    private void Complete(FieldScope scope, bool returned, string? failure)
    {
        var changed = Next();
        var stores = Array.AsReadOnly(scope.Stores.ToArray());
        if (scope.EventMark is { } mark)
            LastEventMark = scope.EventMark = mark with { Changed = changed, Stores = stores, Returned = returned, Failure = failure };
        if (scope.AnimationRead is { } animation)
            LastAnimationRead = scope.AnimationRead = animation with { Changed = changed, Returned = returned, Failure = failure };
        if (scope.SavedPositionCall is { } saved)
            LastSavedPositionCall = scope.SavedPositionCall = saved with
            { Changed = changed, After = _values.SavedPosition, Stores = stores, Returned = returned, Failure = failure };
        if (scope.AnimationInitialization is { } initialization)
            LastAnimationInitialization = scope.AnimationInitialization = initialization with { Changed = changed, Returned = returned, Failure = failure };
        if (scope.SavedPositionInitialization is { } savedInitialization)
            LastSavedPositionInitialization = scope.SavedPositionInitialization = savedInitialization with { Changed = changed, Returned = returned, Failure = failure };
        scope.Completed = new(scope.Invocation, scope.Kind, scope.Entered, changed,
            stores, returned, failure, scope.InvalidatedLane,
            scope.InvalidationBoundary, scope.PublishedRows is { } rows ? Array.AsReadOnly(rows.ToArray()) : null, scope.EventFactory,
            scope.EventMark, scope.AnimationRead, scope.SavedPositionCall, scope.AnimationInitialization, scope.SavedPositionInitialization);
        _operations.Add(scope.Completed);
    }
    private void Guard(FieldScope scope)
    {
        var invocation = scope.Invocation;
        if (_retired || invocation.SourceCommand == Guid.Empty || invocation.Actor != Actor || invocation.Process != Process ||
            invocation.Epoch != Epoch || invocation.ThreadId != Environment.CurrentManagedThreadId ||
            _thread is { } thread && thread != invocation.ThreadId || _fault != scope.Fault)
            throw new InvalidOperationException("Package command fields lost the actual command, Actor, process, epoch or source thread.");
        _requireActualInvocation(invocation);
        if (_retired || _fault != scope.Fault || invocation.ThreadId != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Package command field guard swallowed reentry or changed its current owner.");
        _thread ??= invocation.ThreadId;
    }
    private FalloutPackageRawPosition ReadPosition(FieldScope scope)
    {
        RequireLane(FalloutPackageCommandFieldLane.PositionMode);
        if (_values.PositionMode != 0) throw new NotSupportedException(AdjustedPositionBoundary);
        RequireLane(FalloutPackageCommandFieldLane.Position); Guard(scope); return _values.Position;
    }
    private void RequireLane(FalloutPackageCommandFieldLane lane)
    {
        if (lane == FalloutPackageCommandFieldLane.Animation && _lateAnimationWriter is { } animationWriter)
            throw new NotSupportedException(animationWriter);
        if (lane == FalloutPackageCommandFieldLane.ScriptExtra && _savedScriptInitialization is not null && _scriptInitializer is null)
            throw new NotSupportedException("source-cold-Script-initializer-current-binding-unbound");
        if (lane == FalloutPackageCommandFieldLane.ScriptExtra && _scriptInitializer?.SaveBlocker is { } scriptFailure)
            throw new NotSupportedException(scriptFailure);
        if (!_availability[lane].Available) throw new NotSupportedException(_availability[lane].Boundary);
    }
    private void Admit(FalloutPackageCommandFieldLane lane) => _availability[lane] = new(lane, true, null);
    private long Next() => _sequence = checked(_sequence + 1);
    private void RequireOwner()
    {
        ObjectDisposedException.ThrowIf(_retired, this);
        if (_busy) { _fault = checked(_fault + 1); throw new InvalidOperationException("Package command field consumer reentered."); }
    }
    private void RequireSnapshot()
    { if (_busy) { _fault = checked(_fault + 1); throw new InvalidOperationException("Entered package command fields cannot be captured or retired."); } }
    private sealed class FieldScope(FalloutPackageCommandFieldInvocation invocation, string kind, long entered, long fault)
    {
        internal FalloutPackageCommandFieldInvocation Invocation { get; } = invocation;
        internal string Kind { get; } = kind;
        internal long Entered { get; } = entered;
        internal long Fault { get; } = fault;
        internal List<FalloutPackageCommandFieldStore> Stores { get; } = [];
        internal FalloutPackageCommandFieldLane? InvalidatedLane { get; set; }
        internal string? InvalidationBoundary { get; set; }
        internal IReadOnlyList<FalloutPackageScriptEventRow?>? PublishedRows { get; set; }
        internal Guid? EventFactory { get; set; }
        internal FalloutPackageCommandEventMark? EventMark { get; set; }
        internal FalloutPackageCommandAnimationRead? AnimationRead { get; set; }
        internal FalloutPackageCommandAnimationInitialization? AnimationInitialization { get; set; }
        internal FalloutPackageCommandSavedPositionCall? SavedPositionCall { get; set; }
        internal FalloutPackageCommandSavedPositionInitialization? SavedPositionInitialization { get; set; }
        internal FalloutPackageCommandFieldOperation? Completed { get; set; }
    }
}
