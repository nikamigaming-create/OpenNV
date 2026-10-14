using System.Buffers.Binary;
using System.Security.Cryptography;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

// The reference fields survive a Common process replacement. Pending transfers,
// rendered transforms and native controller poses are independent consumers.
internal sealed partial class FalloutSourceReferencePositionState
{
    internal const string Schema = "opennv-source-reference-position/v1";
    internal const string PrefixBoundary = "source-reference-position-constructor-current-writer-prefix-unbound";
    internal const string AdjustedBoundary = "source-reference-adjusted-position-bounds-and-scratch-ring-unbound";
    private readonly FalloutPluginStack _records;
    private readonly Action<FalloutReferencePositionInvocation> _requireActual;
    private readonly List<FalloutReferencePositionPublication> _publications = [];
    private readonly List<FalloutReferencePositionCall> _calls = [];
    private readonly HashSet<Guid> _invocations = [];
    private readonly Dictionary<FalloutReferencePositionLane, string> _writers = [];
    private FalloutReferencePositionValues _values = new(new(0, 0, 0), 0,
        FalloutReferencePositionSource.InitialScale, null, false);
    private long _sequence, _fault;
    private int? _thread;
    private Scope? _current;
    private bool _retired;
    internal FalloutReferencePositionSource Source { get; }
    internal string Stack { get; }
    internal FalloutCombatActorIdentity Actor { get; }
    internal Guid Process { get; }
    internal Guid Factory { get; } = Guid.NewGuid();
    internal Guid Constructor { get; }
    internal FalloutReferencePositionPrefix? LastPrefix { get; private set; }
    internal FalloutReferencePositionCall? ConstructorStartup { get; private set; }
    internal FalloutReferencePositionCall? ConfiguredStartup { get; private set; }
    internal bool HasConstructedPrefix => LastPrefix is not null;
    internal string? SaveBlocker => _current is not null ? "source-reference-position-in-flight" :
        _retired ? "source-reference-position-retired" : _calls.FirstOrDefault(value => !value.Returned)?.Failure ??
        _writers.Values.FirstOrDefault();

    internal FalloutSourceReferencePositionState(FalloutReferencePositionSource source, FalloutPluginStack records,
        string stack, FalloutCombatActorIdentity actor, Guid process, Guid constructor,
        Action<FalloutReferencePositionInvocation> requireActual, FalloutReferencePositionSnapshot? saved = null)
    {
        source.Validate(); actor.Validate(); ArgumentNullException.ThrowIfNull(records);
        ArgumentException.ThrowIfNullOrWhiteSpace(stack); ArgumentNullException.ThrowIfNull(requireActual);
        if (process == Guid.Empty || constructor == Guid.Empty)
            throw new InvalidDataException("Reference position lacks its actual reference constructor/process.");
        Source = source; _records = records; Stack = stack; Actor = actor; Process = process; Constructor = constructor;
        _requireActual = requireActual;
        foreach (var lane in Enum.GetValues<FalloutReferencePositionLane>()) _writers.Add(lane, PrefixBoundary);
        if (saved is not null)
        {
            Validate(saved, source, stack, actor); ValidateOwnedSources(saved, records);
            if (saved.Process == process || saved.Factory == Factory || saved.Constructor == constructor || saved.Retired)
                throw new InvalidDataException("Cold reference position reused its prior constructor/process or retirement.");
            _values = saved.Values; _sequence = saved.Sequence;
            _writers.Clear(); foreach (var writer in saved.UnownedWriters) _writers.Add(writer.Key, writer.Value);
            _publications.AddRange(saved.Publications); _calls.AddRange(saved.Calls.Select(CloneCall));
            foreach (var call in _calls) _invocations.Add(call.Invocation.Call);
            LastPrefix = _calls.LastOrDefault(value => value.Prefix is not null)?.Prefix;
            ConstructorStartup = _calls.LastOrDefault(value => value.Kind == "constructor-startup-position");
            ConfiguredStartup = _calls.LastOrDefault(value => value.Kind == "configured-startup-position");
        }
        _publications.Add(new(Process, Factory, Constructor, saved?.Process, saved?.Factory, _sequence));
    }

    // This closes only unchanged constructor lanes from actual winning inputs.
    // XYZ becomes available through an actual scalar writer, not this receipt.
    internal FalloutReferencePositionPrefix InitializeConstructedPlayerFields(FalloutReferencePositionInvocation invocation,
        FalloutInstallationSettings settings)
    {
        if (LastPrefix is not null) throw new InvalidOperationException("Reference constructor prefix cannot replay.");
        Run(invocation, "constructed-reference-field-prefix", scope =>
        {
            ArgumentNullException.ThrowIfNull(settings);
            scope.Step = FalloutReferencePositionStep.SourcePrefix;
            var prefix = new FalloutReferencePositionPrefix(Source.Identity, Factory, Constructor, invocation,
                Actor.BaseSha256, null, null, false, false, null);
            scope.Prefix = prefix;
            if (!Actor.EnginePlayer || _writers.Any(value => value.Key != FalloutReferencePositionLane.Position && value.Value != PrefixBoundary) ||
                _calls.Any(value => value.Kind != "constructor-startup-position" || !value.Returned))
                throw new NotSupportedException(PrefixBoundary);
            var referencePresent = _records.TryGetWinner(Actor.Reference, out _);
            scope.Prefix = prefix with { ReferenceRecordPresent = referencePresent };
            if (referencePresent) throw new NotSupportedException("source-engine-reference-record-position-and-Extra-writer-unbound");
            var dependencies = ReadPrefixDependencies(); Guard(scope);
            scope.Prefix = scope.Prefix with { Dependencies = dependencies };
            scope.Step = FalloutReferencePositionStep.ConfiguredCell;
            var cell = settings.Require("General", "SStartingCell"); Guard(scope);
            scope.Prefix = scope.Prefix with { ConfiguredCell = cell };
            if (cell.Length != 0) throw new NotSupportedException("source-configured-CELL-reference-publication-unbound");
            scope.Step = FalloutReferencePositionStep.ConfiguredX;
            var x = settings.Require("General", "SStartingCellX"); Guard(scope);
            scope.Prefix = scope.Prefix with { ConfiguredX = x };
            if (x.Length != 0) throw new NotSupportedException("source-configured-grid-reference-publication-unbound");
        });
        foreach (var lane in new[] { FalloutReferencePositionLane.PositionMode,
            FalloutReferencePositionLane.Scale, FalloutReferencePositionLane.PersistentCell, FalloutReferencePositionLane.ActorYawMode,
            FalloutReferencePositionLane.ActorAssignmentByte }) _writers.Remove(lane);
        return LastPrefix!;
    }

    internal long ReadCurrentBorrowSequence(FalloutReferencePositionInvocation invocation)
    {
        RequireRead(invocation);
        return _sequence;
    }

    internal FalloutReferencePositionRead ReadCurrentPosition(FalloutReferencePositionInvocation invocation)
    {
        RequireRead(invocation); RequireLane(FalloutReferencePositionLane.PositionMode);
        if (_values.PositionMode != 0) throw new NotSupportedException(AdjustedBoundary);
        RequireLane(FalloutReferencePositionLane.Position);
        var read = new FalloutReferencePositionRead(Source.Identity, Stack, Process, Factory, Constructor,
            Actor, _sequence, _values.PositionMode, _values.Position);
        RequireRead(invocation); return read;
    }
    internal FalloutReferenceConstructorPositionRead ReadConstructorPosition(FalloutReferencePositionInvocation invocation)
    {
        RequireConstructorRead(invocation, FalloutReferencePositionLane.Position,
            FalloutReferencePositionStep.PositionX, FalloutReferencePositionStep.PositionY, FalloutReferencePositionStep.PositionZ);
        return new(Source.Identity, Stack, Process, Factory, Constructor, Actor, _sequence, _values.Position);
    }
    internal FalloutReferenceParentCellRead ReadCurrentParentCell(FalloutReferencePositionInvocation invocation,
        IFalloutReferencePositionConsumers consumers)
    {
        RequireRead(invocation); consumers.RequireCurrent(invocation);
        var read = consumers.ReadCurrentParentCell(invocation);
        consumers.RequireCurrent(invocation);
        if (string.IsNullOrWhiteSpace(read.Source) || read.Stack != Stack || read.Actor != Actor || read.Process == Guid.Empty ||
            read.Factory == Guid.Empty || read.Constructor == Guid.Empty || read.Sequence < 0 ||
            read.ExecutableSha256 != Source.Fields.Controls.Main.EngineSha256)
            throw new InvalidDataException("Reference Worldspace query lost its actual independent World ParentCELL owner.");
        ValidateForm(read.Cell);
        RequireRead(invocation); return read;
    }
    internal FalloutReferenceScaleRead ReadReferenceScale(FalloutReferencePositionInvocation invocation)
    {
        var constructor = invocation.Ingress == FalloutReferencePositionIngress.ControllerConstruction;
        if (constructor) RequireConstructorRead(invocation, FalloutReferencePositionLane.Scale, FalloutReferencePositionStep.Scale);
        else { RequireRead(invocation); RequireLane(FalloutReferencePositionLane.Scale); }
        var read = new FalloutReferenceScaleRead(Source.Identity, Stack, Process, Factory, Constructor, Actor, _sequence, _values.Scale, constructor);
        RequireRead(invocation); return read;
    }
    internal FalloutReferenceActorYawModeRead ReadActorYawMode(FalloutReferencePositionInvocation invocation)
    {
        RequireRead(invocation); RequireLane(FalloutReferencePositionLane.ActorYawMode);
        var read = new FalloutReferenceActorYawModeRead(Source.Identity, Stack, Process, Factory, Constructor, Actor, _sequence, _values.ActorYawMode);
        RequireRead(invocation); return read;
    }
    private void RequireConstructorRead(FalloutReferencePositionInvocation invocation, FalloutReferencePositionLane lane,
        params FalloutReferencePositionStep[] fields)
    {
        RequireRead(invocation);
        if (invocation.Ingress != FalloutReferencePositionIngress.ControllerConstruction || _publications.Count != 1 ||
            _calls.Any(value => value.InvalidatedLane == lane || value.Stores.Any(store => fields.Contains(store.Step))) ||
            _writers.TryGetValue(lane, out var boundary) && boundary != PrefixBoundary)
            throw new InvalidOperationException("Reference constructor read crossed a current writer/load or repeated cold construction.");
        // Root's actual constructor guard is mandatory. This read does not
        // change availability and cannot become a command-time current getter.
        RequireRead(invocation);
    }

    internal FalloutReferenceWorldspaceRead ReadWorldspace(FalloutReferencePositionInvocation invocation,
        IFalloutReferencePositionConsumers consumers)
    {
        RequireRead(invocation); consumers.RequireCurrent(invocation);
        var parent = ReadCurrentParentCell(invocation, consumers); FalloutReferenceFlagRead? flags = null;
        var persistentLookup = false; FalloutFormKey? cell = parent.Cell;
        if (cell is null)
        {
            flags = consumers.ReadReferenceFlags(invocation); RequireFlags(invocation, flags);
            // With null raw ParentCELL, the actual selected persistent getter's
            // two predicate arms both return null or this same Extra12 payload.
            // The persistent predicate is still entered in source order.
            var persistent = consumers.ReadPersistentReference(invocation, flags);
            consumers.RequireCurrent(invocation); RequireRead(invocation);
            persistentLookup = persistent || (flags.Flags & 0x4000) != 0;
            if (persistentLookup)
            { RequireLane(FalloutReferencePositionLane.PersistentCell); cell = _values.PersistentCellPresent ? _values.PersistentCell : null; }
        }
        var worldspace = cell is { } actual ? consumers.ReadCellWorldspace(invocation, actual) : null;
        consumers.RequireCurrent(invocation); RequireRead(invocation); ValidateForm(worldspace);
        return new(Source.Identity, Stack, Process, Factory, Constructor, Actor, _sequence, parent, flags,
            persistentLookup, _values.PersistentCellPresent, _values.PersistentCell, worldspace);
    }

    internal FalloutReferencePositionCall StoreBasePosition(FalloutReferencePositionInvocation invocation,
        FalloutPackageRawPosition position, IFalloutReferencePositionConsumers consumers) =>
        Run(invocation, "base-reference-position", scope => BasePosition(scope, position, consumers));
    internal FalloutReferencePositionCall StoreActorPosition(FalloutReferencePositionInvocation invocation,
        FalloutPackageRawPosition position, IFalloutReferencePositionConsumers consumers) =>
        Run(invocation, "Actor-reference-position", scope => ActorPosition(scope, position, consumers));

    internal FalloutReferencePositionCall StoreConstructorStartupPosition(FalloutReferencePositionInvocation invocation,
        IFalloutReferencePositionConsumers consumers)
    {
        if (ConstructorStartup is not null) throw new InvalidOperationException("Constructor startup position cannot replay.");
        if (invocation.Ingress != FalloutReferencePositionIngress.ConstructorStartup)
            throw new InvalidOperationException("Constructor position has another original ingress.");
        return Run(invocation, "constructor-startup-position", scope =>
            ActorPosition(scope, new(0x45000000, 0x45000000, 0), consumers));
    }
    internal FalloutReferencePositionCall StoreConfiguredStartupPosition(FalloutReferencePositionInvocation invocation,
        IFalloutReferencePositionConsumers consumers)
    {
        if (ConfiguredStartup is not null) throw new InvalidOperationException("Configured startup position cannot replay.");
        if (invocation.Ingress != FalloutReferencePositionIngress.ConfiguredStartup)
            throw new InvalidOperationException("Configured position has another original ingress.");
        return Run(invocation, "configured-startup-position", scope =>
        {
            if (ConstructorStartup is not { Returned: true } || LastPrefix is not { Returned: true })
                throw new NotSupportedException("source-configured-position-preceding-startup-child-unreturned");
            consumers.RequireCurrent(invocation); consumers.ClearStartupChangeSuppression(invocation); Guard(scope);
            scope.Step = FalloutReferencePositionStep.InitialPlacement;
            var position = consumers.SelectInitialPlacement(invocation, new(0x45000000, 0x45000000, 0x43000000));
            consumers.RequireCurrent(invocation); Guard(scope);
            consumers.SetInitialPlacementYaw(invocation); consumers.RequireCurrent(invocation); Guard(scope);
            ActorPosition(scope, position, consumers);
        });
    }

    private void ActorPosition(Scope scope, FalloutPackageRawPosition position, IFalloutReferencePositionConsumers consumers)
    {
        BasePosition(scope, position, consumers);
        scope.Step = FalloutReferencePositionStep.ControllerReset;
        consumers.ResetCurrentControllerAndModel(scope.Invocation, position);
        consumers.RequireCurrent(scope.Invocation); Guard(scope);
        scope.Step = FalloutReferencePositionStep.ModelTranslation;
    }
    private void BasePosition(Scope scope, FalloutPackageRawPosition position, IFalloutReferencePositionConsumers consumers)
    {
        ArgumentNullException.ThrowIfNull(position); ArgumentNullException.ThrowIfNull(consumers);
        if (consumers.Factory == Guid.Empty) throw new InvalidDataException("Reference setter lacks its actual effect consumer.");
        consumers.RequireCurrent(scope.Invocation); Guard(scope);
        scope.Step = FalloutReferencePositionStep.PersistentPredicate;
        scope.Flags = consumers.ReadReferenceFlags(scope.Invocation); RequireFlags(scope.Invocation, scope.Flags);
        scope.Persistent = consumers.ReadPersistentReference(scope.Invocation, scope.Flags);
        consumers.RequireCurrent(scope.Invocation); Guard(scope);
        if (scope.Persistent == true)
        {
            scope.Step = FalloutReferencePositionStep.KindPredicate;
            scope.BaseKind = consumers.ReadBaseFormKind(scope.Invocation); consumers.RequireCurrent(scope.Invocation); Guard(scope);
            if (FalloutReferencePositionSource.UsesWorldspacePositionList(scope.BaseKind.Value))
            {
                scope.Step = FalloutReferencePositionStep.Worldspace;
                scope.Worldspace = ReadWorldspace(scope.Invocation, consumers);
                if (scope.Worldspace.Worldspace is { } space)
                {
                    consumers.DetachWorldspace(scope.Invocation, space); consumers.RequireCurrent(scope.Invocation); Guard(scope);
                    scope.Step = FalloutReferencePositionStep.Detached;
                }
            }
        }
        StorePositionPair(scope, position);
        Store(scope, FalloutReferencePositionStep.PositionZ, position.Z);
        _writers.Remove(FalloutReferencePositionLane.Position);
        if (scope.Worldspace?.Worldspace is { } attached)
        { consumers.AttachWorldspace(scope.Invocation, attached); consumers.RequireCurrent(scope.Invocation); Guard(scope); }
        scope.Step = FalloutReferencePositionStep.ReferenceChange;
        scope.Change = consumers.ReferenceChanged(scope.Invocation, FalloutReferencePositionSource.PositionChangeMask);
        consumers.RequireCurrent(scope.Invocation); Guard(scope);
        if (!scope.Change.Returned) throw new InvalidOperationException("Reference setter received an unreturned mask2 child.");
    }

    // Exact scalar children of independent original setters. Their enclosing
    // load/attachment/mode/scale owners retain all remaining callbacks and return.
    internal FalloutReferencePositionCall StoreCurrentMode(FalloutReferencePositionInvocation invocation, byte mode) =>
        Run(invocation, "raw-adjusted-position-mode-store", scope => { Store(scope, FalloutReferencePositionStep.PositionMode, mode); _writers.Remove(FalloutReferencePositionLane.PositionMode); });
    internal FalloutReferencePositionCall StoreRawReferenceScale(FalloutReferencePositionInvocation invocation, uint bits) =>
        Run(invocation, "raw-reference-scale-store", scope => { Store(scope, FalloutReferencePositionStep.Scale, bits); _writers.Remove(FalloutReferencePositionLane.Scale); });
    internal FalloutReferencePositionCall StoreActorYawMode(FalloutReferencePositionInvocation invocation, uint mode) =>
        Run(invocation, "raw-Actor-yaw-selection-word-store", scope => { Store(scope, FalloutReferencePositionStep.ActorYawMode, mode); _writers.Remove(FalloutReferencePositionLane.ActorYawMode); });
    internal FalloutReferencePositionCall StorePersistentCell(FalloutReferencePositionInvocation invocation, bool present, FalloutFormKey? cell) =>
        Run(invocation, "persistent-CELL-Extra-twelve-store", scope =>
        {
            ValidateForm(cell); if (!present && cell is not null) throw new InvalidDataException("Absent persistent Extra retained a CELL.");
            Store(scope, FalloutReferencePositionStep.PersistentCell, form: cell, present: present); _writers.Remove(FalloutReferencePositionLane.PersistentCell);
        });
    internal FalloutReferencePositionCall ObserveUnownedWriter(FalloutReferencePositionInvocation invocation,
        FalloutReferencePositionLane lane, string boundary) => Run(invocation, "observed-reference-field-writer", scope =>
        {
            if (!Enum.IsDefined(lane)) throw new InvalidDataException("Reference writer has an unknown lane.");
            ArgumentException.ThrowIfNullOrWhiteSpace(boundary); scope.Invalidated = lane;
            scope.WriterBoundary = boundary;
            scope.Step = FalloutReferencePositionStep.UnownedWriter; _writers[lane] = boundary;
        });

    private FalloutReferencePositionCall Run(FalloutReferencePositionInvocation invocation, string kind, Action<Scope> action)
    {
        ObjectDisposedException.ThrowIf(_retired, this);
        if (_current is not null) { _fault++; throw new InvalidOperationException("Reference position reentered its actual writer."); }
        if (!_invocations.Add(invocation.Call)) throw new InvalidOperationException("Reference position replayed an original invocation.");
        var scope = new Scope(invocation, kind, ++_sequence, _values, _fault);
        if (kind == "constructed-reference-field-prefix")
            scope.Prefix = new(Source.Identity, Factory, Constructor, invocation, Actor.BaseSha256, null, null, false, false, null);
        _current = scope;
        try { Guard(scope); action(scope); Guard(scope); scope.Step = FalloutReferencePositionStep.Returned; return Complete(scope, true, null); }
        catch (Exception error) { Complete(scope, false, string.IsNullOrWhiteSpace(error.Message) ? error.GetType().Name : error.Message); throw; }
        finally { _current = null; }
    }
    private FalloutReferencePositionCall Complete(Scope scope, bool returned, string? failure)
    {
        var changed = checked(++_sequence);
        if (scope.Prefix is { } prefix) LastPrefix = scope.Prefix = prefix with { Returned = returned, Failure = failure };
        var call = new FalloutReferencePositionCall(scope.Invocation, Source.Identity, Factory, scope.Entered,
            changed, scope.Kind, scope.Step, scope.Before, _values, Array.AsReadOnly(scope.Stores.ToArray()), returned,
            failure, scope.Flags, scope.BaseKind, scope.Persistent, scope.Worldspace, scope.Change, scope.Prefix, scope.Invalidated, scope.WriterBoundary);
        _calls.Add(call);
        if (scope.Kind == "constructor-startup-position") ConstructorStartup = call;
        if (scope.Kind == "configured-startup-position") ConfiguredStartup = call;
        return call;
    }
    private void Store(Scope scope, FalloutReferencePositionStep step, uint? bits = null, FalloutFormKey? form = null, bool? present = null)
    {
        Guard(scope); var store = new FalloutReferencePositionStore(step, checked(++_sequence), bits, form, present);
        _values = Apply(_values, store); scope.Stores.Add(store); scope.Step = step;
    }
    private void StorePositionPair(Scope scope, FalloutPackageRawPosition position)
    {
        Guard(scope);
        // The original paired write has no callable boundary between X and Y.
        // Retain both scalar fields in history without inserting a guard there.
        var x = new FalloutReferencePositionStore(FalloutReferencePositionStep.PositionX, checked(++_sequence), position.X);
        var y = new FalloutReferencePositionStore(FalloutReferencePositionStep.PositionY, checked(++_sequence), position.Y);
        _values = _values with { Position = _values.Position with { X = position.X, Y = position.Y } };
        scope.Stores.Add(x); scope.Stores.Add(y); scope.Step = FalloutReferencePositionStep.PositionY;
    }
    private void Guard(Scope scope)
    {
        RequireRead(scope.Invocation);
        if (_current != scope || _fault != scope.Fault) throw new InvalidOperationException("Reference writer changed its entered scope or swallowed reentry.");
    }
    private void RequireRead(FalloutReferencePositionInvocation invocation)
    {
        ObjectDisposedException.ThrowIf(_retired, this); ArgumentNullException.ThrowIfNull(invocation);
        invocation.ValidatePackageCaller();
        if (invocation.Call == Guid.Empty || invocation.Actor != Actor || invocation.ReferenceProcess != Process ||
            invocation.ReferenceConstructor != Constructor || invocation.CommonProcess == Guid.Empty || invocation.Epoch < 1 ||
            invocation.ThreadId != Environment.CurrentManagedThreadId || !Enum.IsDefined(invocation.Ingress) ||
            _thread is { } thread && thread != invocation.ThreadId || _current is { } current && current.Invocation != invocation)
            throw new InvalidOperationException("Reference fields lost their actual source Actor, constructor, process or thread.");
        var fault = _fault; _requireActual(invocation);
        if (_retired || _fault != fault) throw new InvalidOperationException("Reference guard swallowed reentry or replaced its current owner.");
        _thread ??= invocation.ThreadId;
    }
    private void RequireFlags(FalloutReferencePositionInvocation invocation, FalloutReferenceFlagRead flags)
    {
        if (flags.Source != FalloutReferenceScriptExtraInitializerSource.Reviewed(Source.Fields).Identity || flags.Stack != Stack ||
            flags.Actor != Actor || flags.Process != invocation.CommonProcess || flags.Constructor != Constructor || flags.Epoch != invocation.Epoch ||
            flags.Factory == Guid.Empty || flags.FieldsFactory == Guid.Empty || flags.Sequence < 0)
            throw new InvalidDataException("Reference setter borrowed another flag authority or current Common epoch.");
    }
    private void RequireLane(FalloutReferencePositionLane lane)
    { if (_writers.TryGetValue(lane, out var boundary)) throw new NotSupportedException(boundary); }
    private sealed class Scope(FalloutReferencePositionInvocation invocation, string kind, long entered,
        FalloutReferencePositionValues before, long fault)
    {
        internal FalloutReferencePositionInvocation Invocation = invocation;
        internal string Kind = kind;
        internal long Entered = entered, Fault = fault;
        internal FalloutReferencePositionValues Before = before;
        internal FalloutReferencePositionStep Step;
        internal List<FalloutReferencePositionStore> Stores = [];
        internal FalloutReferenceFlagRead? Flags;
        internal byte? BaseKind;
        internal bool? Persistent;
        internal FalloutReferenceWorldspaceRead? Worldspace;
        internal FalloutReferenceChangeCall? Change;
        internal FalloutReferencePositionPrefix? Prefix;
        internal FalloutReferencePositionLane? Invalidated;
        internal string? WriterBoundary;
    }
}
