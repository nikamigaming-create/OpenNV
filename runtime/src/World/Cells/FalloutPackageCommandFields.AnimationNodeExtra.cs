using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutReferenceAnimationNodeExtraRead(string Source, string Stack, Guid Process,
    Guid FieldsFactory, Guid InitializerFactory, Guid Constructor, FalloutCombatActorIdentity Actor, long Epoch,
    long Sequence, Guid? Extra, FalloutSourceSceneNode? Payload, FalloutPlayerLateFrameInvocation Frame);
internal sealed record FalloutReferenceAnimationNodeMembership(string Source, Guid Process, Guid FieldsFactory,
    Guid Constructor, Guid InitializerFactory, Guid OriginalInitializer, Guid? Extra,
    FalloutSourceSceneNode? Payload, long Sequence, string? Writer,
    Guid? PreviousProcess = null, Guid? PreviousFieldsFactory = null);
internal sealed record FalloutReferenceAnimationNodeWriter(Guid FieldsFactory, Guid InitializerFactory,
    long Sequence, string Boundary, FalloutReferenceScriptExtraInitializationInvocation? InitializerInvocation = null,
    FalloutReferenceScriptExtraWriter? InitializerWriter = null,
    FalloutReferenceModelDataInvocation? ModelInvocation = null);
internal enum FalloutReferenceAnimationLoadWriterKind { EnteredLoad, FirstContextStore }
internal sealed record FalloutReferenceAnimationLoadWriter(FalloutReferenceModelDataInvocation Invocation,
    Guid FieldsFactory, long Entered, long Changed, FalloutReferenceAnimationLoadWriterKind Kind,
    string? Boundary, Guid? Before, Guid? After, long? Stored,
    FalloutSourceAnimationContextConstruction? Construction, bool Returned, string? Failure);
internal sealed record FalloutReferenceAnimationTailFieldSnapshot(FalloutReferenceAnimationNodeMembership? Membership,
    long Sequence, string? AnimationWriter, IReadOnlyList<FalloutReferenceAnimationLoadWriter> LoadWriters,
    FalloutPlayerReturnTailSource Source, IReadOnlyList<FalloutReferenceAnimationNodeWriter> NodeWriters);

// Additional subtype membership in the SAME actual reference Extra owner.
// There is no second list, model-derived membership or default node payload.
internal sealed partial class FalloutActorPackageCommandFields
{
    private FalloutReferenceAnimationNodeMembership? _animationNodeMembership;
    private FalloutReferenceAnimationTailFieldSnapshot? _historicalAnimationTailFields;
    private readonly List<FalloutReferenceAnimationLoadWriter> _animationLoadWriters = [];
    private readonly List<FalloutReferenceAnimationNodeWriter> _animationNodeWriters = [];
    private long _animationTailSequence;
    private string? _lateAnimationWriter;
    private FalloutPlayerReturnTailSource? _animationTailSource;
    internal void BindAnimationNodeExtraMembership(FalloutPlayerReturnTailSource source,
        FalloutReferenceScriptExtraInitializer initializer, Action requireActualPrefix,
        FalloutReferenceAnimationNodeMembership? saved = null)
    {
        RequireOwner(); source.Validate(); ArgumentNullException.ThrowIfNull(initializer); ArgumentNullException.ThrowIfNull(requireActualPrefix);
        requireActualPrefix(); saved ??= _historicalAnimationTailFields?.Membership;
        _animationTailSource = source;
        if (_animationNodeMembership is not null || !ReferenceEquals(_scriptInitializer, initializer) ||
            initializer.FieldsFactory != Factory || source.Movement.Mover.Controller.Fields != Source ||
            initializer.LastCall is not { Returned: true, InvalidatedWriter: null } construction ||
            construction.Selected is null || construction.Selected.Script is not null || construction.Before is not null)
            throw new InvalidOperationException("Animation node membership lacks the same returned source reference constructor/prefix.");
        if (saved is null)
        {
            // This is the independently qualified actual constructor/current
            // prefix. It is not an Extra allocation, model setter or command.
            initializer.RequireAnimationNodeConstructorPrefix();
            _animationNodeMembership = new(source.Identity, Process, Factory, Constructor, initializer.Factory,
                construction.Invocation.Initialization, null, null, _animationTailSequence, null);
        }
        else
        {
            ValidateAnimationNodeMembership(saved, source);
            if (saved.Process == Process || saved.FieldsFactory == Factory || saved.Constructor == Constructor ||
                saved.OriginalInitializer != construction.Invocation.Initialization || saved.Source != source.Identity)
                throw new InvalidDataException("Cold node membership replayed or changed its actual reference initializer.");
            _animationNodeMembership = saved with
            {
                Process = Process,
                FieldsFactory = Factory,
                Constructor = Constructor,
                InitializerFactory = initializer.Factory,
                PreviousProcess = saved.Process,
                PreviousFieldsFactory = saved.FieldsFactory
            };
        }
        requireActualPrefix();
    }

    internal FalloutReferenceAnimationNodeExtraRead ReadAnimationNodeExtra(FalloutPlayerReturnTailSource source,
        FalloutPlayerLateFrameInvocation frame, Guid initializerFactory,
        Action<FalloutPlayerLateFrameInvocation> requireActual)
    {
        var fault = _fault; Check();
        var value = _animationNodeMembership ?? throw new NotSupportedException("source-reference-Extra41-current-membership-prefix-unbound");
        if (value.Source != source.Identity || value.InitializerFactory != initializerFactory)
            throw new InvalidOperationException("Node Extra query changed its actual source/initializer binding.");
        if (value.Writer is { } writer) throw new NotSupportedException(writer);
        Check(); return new(source.Identity, Stack, Process, Factory, initializerFactory, Constructor,
            Actor, Epoch, value.Sequence, value.Extra, value.Payload, frame);
        void Check()
        {
            if (_busy) { ++_fault; throw new InvalidOperationException("Animation node Extra read reentered the real list owner."); }
            requireActual(frame);
            if (_retired || _fault != fault || frame.Common.Process != Process || frame.Common.Actor != Actor ||
                frame.Common.Epoch != Epoch || frame.PlayerConstructor != Constructor || frame.ThreadId != Environment.CurrentManagedThreadId ||
                _scriptInitializer?.Factory != initializerFactory || _thread is { } thread && thread != frame.ThreadId)
                throw new InvalidOperationException("Animation node Extra read lost its current reference/source/thread.");
        }
    }
    internal void InvalidateAnimationNodeMembershipFromInitializer(FalloutReferenceScriptExtraInitializationInvocation invocation,
        Guid initializerFactory, FalloutReferenceScriptExtraWriter writer, string boundary)
    {
        if (writer is not (FalloutReferenceScriptExtraWriter.ExtraCopyOrLoad or FalloutReferenceScriptExtraWriter.ScriptCallback)) return;
        if (_animationNodeMembership is { } current)
        {
            if (invocation.Process != Process || invocation.Constructor != Constructor || invocation.Actor != Actor ||
                invocation.Epoch != Epoch || invocation.ThreadId != Environment.CurrentManagedThreadId ||
                current.InitializerFactory != initializerFactory || _scriptInitializer?.Factory != initializerFactory)
                throw new InvalidOperationException("Animation node invalidation changed its entered actual initializer/list owner.");
            var sequence = checked(++_animationTailSequence);
            _animationNodeWriters.Add(new(Factory, initializerFactory, sequence, boundary, invocation, writer));
            _animationNodeMembership = current with { Writer = boundary, Sequence = sequence };
        }
    }
    internal void ObserveUnownedAnimationNodeMembershipWriter(FalloutReferenceModelDataInvocation invocation,
        string producer, Action<FalloutReferenceModelDataInvocation> requireActualWriter)
    {
        RequireOwner(); ArgumentException.ThrowIfNullOrWhiteSpace(producer); ArgumentNullException.ThrowIfNull(requireActualWriter);
        requireActualWriter(invocation);
        if (invocation.Actor != Actor || invocation.CommonProcess != Process || invocation.Epoch != Epoch ||
            invocation.ReferenceConstructor != Constructor || invocation.Call == Guid.Empty || invocation.ThreadId != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Extra list writer changed its actual entered reference caller.");
        var current = _animationNodeMembership ?? throw new InvalidOperationException("Extra41 membership was not bound at its actual constructor prefix.");
        var sequence = checked(++_animationTailSequence);
        var boundary = "source-reference-Extra41-writer-unowned:" + producer;
        _animationNodeWriters.Add(new(Factory, current.InitializerFactory, sequence, boundary, ModelInvocation: invocation));
        _animationNodeMembership = current with { Writer = boundary, Sequence = sequence };
        requireActualWriter(invocation);
    }
    internal void ObserveEnteredFirstPersonAnimationLoad(FalloutReferenceModelDataInvocation invocation,
        Action<FalloutReferenceModelDataInvocation> requireActualLoad)
    {
        RequireAnimationLoad(invocation, requireActualLoad);
        if (_animationLoadWriters.Any(row => row.Invocation.Call == invocation.Call && row.Kind == FalloutReferenceAnimationLoadWriterKind.EnteredLoad))
            throw new InvalidOperationException("Animation pointer writer replayed the same entered source load.");
        RecordAnimationLoad(invocation, FalloutReferenceAnimationLoadWriterKind.EnteredLoad, null, requireActualLoad);
    }
    internal FalloutReferenceAnimationLoadWriter StoreConstructedFirstPersonAnimation(FalloutReferenceModelDataInvocation invocation,
        FalloutSourceAnimationContextState context, Action<FalloutReferenceModelDataInvocation> requireActualStore)
    {
        RequireAnimationLoad(invocation, requireActualStore); ArgumentNullException.ThrowIfNull(context);
        if (context.Source != _animationTailSource || context.Actor != Actor || context.Process != invocation.ReferenceProcess ||
            context.CommonProcess != Process || context.Construction.Invocation != invocation || !context.Construction.Returned ||
            !_animationLoadWriters.Any(row => row.Invocation == invocation && row.Kind == FalloutReferenceAnimationLoadWriterKind.EnteredLoad && row.Returned) ||
            _animationLoadWriters.Any(row => row.Invocation == invocation && row.Kind == FalloutReferenceAnimationLoadWriterKind.FirstContextStore))
            throw new InvalidOperationException("First animation pointer store lacks its actual returned source constructor and entered loader.");
        return RecordAnimationLoad(invocation, FalloutReferenceAnimationLoadWriterKind.FirstContextStore, context, requireActualStore);
    }
    private void RequireAnimationLoad(FalloutReferenceModelDataInvocation invocation,
        Action<FalloutReferenceModelDataInvocation> requireActual)
    {
        RequireOwner(); ArgumentNullException.ThrowIfNull(requireActual); requireActual(invocation);
        if (_animationTailSource is null || invocation.Ingress != FalloutReferenceModelDataIngress.ThreeDLoad || invocation.Actor != Actor ||
            invocation.CommonProcess != Process || invocation.ReferenceConstructor != Constructor || invocation.Epoch != Epoch ||
            invocation.ReferenceProcess == Guid.Empty || invocation.Call == Guid.Empty || invocation.ThreadId != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("First AnimData writer lost its actual entered selected Player load.");
    }
    private FalloutReferenceAnimationLoadWriter RecordAnimationLoad(FalloutReferenceModelDataInvocation invocation,
        FalloutReferenceAnimationLoadWriterKind kind, FalloutSourceAnimationContextState? context,
        Action<FalloutReferenceModelDataInvocation> requireActual)
    {
        var entered = Next(); var fault = _fault; var before = _values.FirstPersonAnimation;
        long? stored = null; string? failure = null; _busy = true;
        try
        {
            Check();
            if (kind == FalloutReferenceAnimationLoadWriterKind.EnteredLoad)
                _lateAnimationWriter = "source-Player-three-D-load-first-AnimData-constructor-and-store-unowned";
            else
            {
                if (before is not null) throw new NotSupportedException("source-Player-old-first-AnimData-retirement-and-load-replacement-unowned");
                _values = _values with { FirstPersonAnimation = context!.Identity }; stored = Next();
            }
            Check();
            if (kind == FalloutReferenceAnimationLoadWriterKind.FirstContextStore) _lateAnimationWriter = null;
        }
        catch (Exception error) { failure = error.Message; _failure ??= failure; throw; }
        finally
        {
            _animationLoadWriters.Add(new(invocation, Factory, entered, Next(), kind, _lateAnimationWriter,
                before, _values.FirstPersonAnimation, stored, context?.Construction, failure is null, failure));
            _busy = false;
        }
        return _animationLoadWriters[^1];
        void Check()
        {
            requireActual(invocation);
            if (_retired || _fault != fault || invocation.ThreadId != Environment.CurrentManagedThreadId)
                throw new InvalidOperationException("First AnimData pointer writer swallowed actual source reentry.");
        }
    }
    internal FalloutReferenceAnimationNodeMembership? CaptureAnimationNodeMembership() => _animationNodeMembership;
    private void RestoreAnimationTailFields(FalloutReferenceAnimationTailFieldSnapshot? saved)
    {
        if (saved is null) return;
        _historicalAnimationTailFields = saved; _animationTailSequence = saved.Sequence;
        _animationTailSource = saved.Source;
        _lateAnimationWriter = saved.AnimationWriter; _animationLoadWriters.AddRange(saved.LoadWriters);
        _animationNodeWriters.AddRange(saved.NodeWriters);
    }
    private FalloutReferenceAnimationTailFieldSnapshot? CaptureAnimationTailFields() =>
        _animationNodeMembership is null && _historicalAnimationTailFields is null && _animationLoadWriters.Count == 0 ? null :
            new(_animationNodeMembership ?? _historicalAnimationTailFields?.Membership, _animationTailSequence,
                _lateAnimationWriter, _animationLoadWriters.ToArray(), _animationTailSource!, _animationNodeWriters.ToArray());
    private static void ValidateAnimationTailFields(FalloutPackageCommandFieldSnapshot saved)
    {
        if (saved.AnimationTail is not { } fields) return;
        fields.Source.Validate();
        if (fields.Source.Movement.Mover.Controller.Fields != saved.Source || fields.Sequence < 0 ||
            fields.AnimationWriter is { Length: 0 } || fields.LoadWriters is null || fields.NodeWriters is null)
            throw new InvalidDataException("Animation pointer/list writer history omitted its actual metadata.");
        var ids = new HashSet<Guid>(); long sequence = 0;
        var publications = saved.Publications.ToDictionary(value => value.Factory);
        foreach (var writer in fields.LoadWriters)
        {
            var invocation = writer.Invocation;
            if (writer.Entered <= sequence || writer.Changed <= writer.Entered || writer.Changed > saved.Sequence ||
                !ids.Add((writer.Kind == FalloutReferenceAnimationLoadWriterKind.EnteredLoad ? invocation.Call : writer.Construction?.Identity) ?? Guid.Empty) ||
                invocation.Call == Guid.Empty || invocation.Actor != saved.Actor ||
                invocation.Ingress != FalloutReferenceModelDataIngress.ThreeDLoad || invocation.Epoch != saved.Epoch || invocation.ThreadId < 1 ||
                invocation.ReferenceProcess == Guid.Empty || !publications.TryGetValue(writer.FieldsFactory, out var publication) ||
                invocation.CommonProcess != publication.Process || invocation.ReferenceConstructor != publication.Constructor)
                throw new InvalidDataException("Animation pointer invalidation replaced its actual entered load/reference factory.");
            sequence = writer.Changed;
        }
        if (fields.AnimationWriter != fields.LoadWriters.LastOrDefault()?.Boundary)
            throw new InvalidDataException("Animation pointer availability differs from its last actual entered load.");
        if (fields.Membership is { } membership)
        {
            if (!publications.TryGetValue(membership.FieldsFactory, out var publication) ||
                membership.Process != publication.Process || membership.Constructor != publication.Constructor ||
                membership.Sequence > fields.Sequence || saved.ScriptInitialization is not { } initializer ||
                !initializer.Publications.Any(row => row.Factory == membership.InitializerFactory && row.FieldsFactory == membership.FieldsFactory) ||
                !initializer.Calls.Any(row => row.Invocation.Initialization == membership.OriginalInitializer && row.Returned &&
                    row.Selected is { Script: null } && row.Before is null && row.InvalidatedWriter is null))
                throw new InvalidDataException("Extra41 membership replaced its actual same-list initializer history.");
            ValidateAnimationNodeMembership(membership, fields.Source);
            var baseline = initializer.Calls.Single(row => row.Invocation.Initialization == membership.OriginalInitializer);
            var initializerWriters = initializer.Calls.Where(row => row.Entered > baseline.Entered && row.InvalidatedWriter is
                FalloutReferenceScriptExtraWriter.ExtraCopyOrLoad or FalloutReferenceScriptExtraWriter.ScriptCallback)
                .ToDictionary(row => row.Invocation.Initialization);
            var observed = new HashSet<Guid>(); long nodeSequence = 0;
            foreach (var writer in fields.NodeWriters)
            {
                if (writer.Sequence != ++nodeSequence || string.IsNullOrWhiteSpace(writer.Boundary) ||
                    !publications.TryGetValue(writer.FieldsFactory, out var writerPublication) ||
                    !initializer.Publications.Any(row => row.Factory == writer.InitializerFactory && row.FieldsFactory == writer.FieldsFactory))
                    throw new InvalidDataException("Extra41 writer lost its actual entered list factory and component order.");
                if (writer.InitializerInvocation is { } invocation)
                {
                    if (writer.ModelInvocation is not null || writer.InitializerWriter is not
                        (FalloutReferenceScriptExtraWriter.ExtraCopyOrLoad or FalloutReferenceScriptExtraWriter.ScriptCallback) ||
                        !initializerWriters.TryGetValue(invocation.Initialization, out var actual) || !observed.Add(invocation.Initialization) ||
                        actual.Invocation != invocation || actual.Factory != writer.InitializerFactory || actual.FieldsFactory != writer.FieldsFactory ||
                        actual.InvalidatedWriter != writer.InitializerWriter || actual.WriterBoundary != writer.Boundary)
                        throw new InvalidDataException("Extra41 initializer invalidation has no actual entered callback/list-copy receipt.");
                }
                else if (writer.ModelInvocation is not { } model || writer.InitializerWriter is not null ||
                    model.Call == Guid.Empty || model.Actor != saved.Actor || model.CommonProcess != writerPublication.Process ||
                    model.ReferenceProcess == Guid.Empty || model.ReferenceConstructor != writerPublication.Constructor ||
                    model.Epoch != saved.Epoch || model.ThreadId < 1 || !Enum.IsDefined(model.Ingress))
                    throw new InvalidDataException("Extra41 model invalidation lost its actual entered reference invocation.");
            }
            if (observed.Count != initializerWriters.Count || fields.Sequence != nodeSequence ||
                membership.Sequence != nodeSequence || membership.Writer != fields.NodeWriters.LastOrDefault()?.Boundary)
                throw new InvalidDataException("Extra41 current availability erased an actual entered writer.");
        }
        else if (fields.NodeWriters.Count != 0 || fields.Sequence != 0)
            throw new InvalidDataException("Extra41 writer history omitted its same-list membership owner.");
    }
    private static void ValidateAnimationPointerOperation(FalloutReferenceAnimationLoadWriter operation,
        FalloutPackageCommandFieldSnapshot saved, ref FalloutPackageCommandFieldValues values, ref long sequence,
        IDictionary<Guid, int> sourceThreads, IReadOnlyDictionary<Guid, int> publicationOrder, ref int lastPublication)
    {
        var invocation = operation.Invocation;
        var publication = saved.Publications.SingleOrDefault(row => row.Factory == operation.FieldsFactory);
        if (publication is null || invocation.Call == Guid.Empty || invocation.Actor != saved.Actor || invocation.CommonProcess != publication.Process ||
            invocation.ReferenceConstructor != publication.Constructor || invocation.Epoch != saved.Epoch || invocation.ThreadId < 1 ||
            invocation.Ingress != FalloutReferenceModelDataIngress.ThreeDLoad || !Enum.IsDefined(operation.Kind) ||
            operation.Entered != ++sequence || operation.Before != values.FirstPersonAnimation ||
            operation.Returned != (operation.Failure is null) || operation.Failure is { Length: 0 })
            throw new InvalidDataException("Animation pointer history lost its actual load factory/caller and original current words.");
        var ordinal = publicationOrder[invocation.CommonProcess];
        if (ordinal < lastPublication || sourceThreads.TryGetValue(invocation.CommonProcess, out var thread) && thread != invocation.ThreadId)
            throw new InvalidDataException("Animation pointer store crossed source process or thread history.");
        lastPublication = ordinal; sourceThreads.TryAdd(invocation.CommonProcess, invocation.ThreadId);
        if (operation.Kind == FalloutReferenceAnimationLoadWriterKind.EnteredLoad)
        {
            if (operation.Construction is not null || operation.Stored is not null || operation.Before != operation.After ||
                string.IsNullOrWhiteSpace(operation.Boundary))
                throw new InvalidDataException("Entered animation load invented a context constructor or pointer store.");
        }
        else
        {
            if (operation.Construction is not { } construction || construction.Invocation != invocation ||
                construction.Source != saved.AnimationTail!.Source.Identity || construction.Identity == Guid.Empty || construction.Factory == Guid.Empty ||
                !construction.Returned || construction.GroupBuckets != FalloutPlayerReturnTailSource.AnimationGroupBuckets)
                throw new InvalidDataException("First pointer publication has no actual returned independent AnimData constructor.");
            if (operation.Stored is { } stored)
            {
                if (stored != ++sequence || operation.Before is not null || operation.After != construction.Identity)
                    throw new InvalidDataException("First pointer writer omitted or fabricated its actual fresh constructor store.");
                values = values with { FirstPersonAnimation = construction.Identity };
            }
            else if (operation.Returned || operation.After != operation.Before)
                throw new InvalidDataException("First pointer publication returned without its actual store.");
            if (operation.Returned && operation.Boundary is not null)
                throw new InvalidDataException("Returned first pointer publication retained an unowned pointer writer.");
        }
        if (operation.Changed != ++sequence)
            throw new InvalidDataException("Animation pointer writer erased an actual failed/returned prefix extent.");
    }
    internal static void ValidateAnimationNodeMembership(FalloutReferenceAnimationNodeMembership saved, FalloutPlayerReturnTailSource source)
    {
        source.Validate();
        if (saved.Source != source.Identity || saved.Process == Guid.Empty || saved.FieldsFactory == Guid.Empty ||
            saved.Constructor == Guid.Empty || saved.InitializerFactory == Guid.Empty || saved.OriginalInitializer == Guid.Empty ||
            saved.Extra is not null || saved.Payload is not null || saved.Sequence < 0 || saved.Writer is { Length: 0 } ||
            (saved.PreviousProcess is null) != (saved.PreviousFieldsFactory is null) || saved.PreviousProcess == saved.Process ||
            saved.PreviousFieldsFactory == saved.FieldsFactory)
            throw new InvalidDataException("Animation node Extra lost its actual same-list constructor/null/current-writer provenance.");
    }
}
