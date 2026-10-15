using System.Numerics;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Gameplay.State;

internal enum FalloutLoadedAnimationRole { FirstPerson, Common }
internal enum FalloutLoadedAnimationBindStep
{ Actor, Root, NamedNodes, RootFlag, Manager, Resources, AccumulationRoot, InitialIdle, Returned, Failed }
internal sealed record FalloutLoadedAnimationBindCall(FalloutReferenceModelDataInvocation Invocation,
    Guid Context, Guid Factory, long Entered, long Changed, FalloutLoadedAnimationBindStep Step,
    FalloutLoadedAnimationBindStep? FailedAt, Guid RootFactory, Guid LogicalLifetime,
    IReadOnlyList<FalloutNifSceneKey?> NamedNodes, FalloutAnimationRootFlagStore? RootFlag,
    Guid? Manager, bool? InitialIdle, FalloutSourceAnimationKindScope? Thread,
    bool Returned, string? Failure);
internal sealed record FalloutLoadedAnimationUpdate(FalloutPlayerLateFrameInvocation? Frame,
    FalloutReferenceModelDataInvocation? Load, Guid Context, Guid Factory, long Entered, long Changed,
    uint DeltaBits, uint BeforeTimeBits, uint AfterTimeBits, FalloutActorMoverVector BeforeAccumulation,
    FalloutActorMoverVector AfterAccumulation, FalloutActorMoverVector Delta,
    uint? MoverFlags, IReadOnlyList<uint> ScaleBits, FalloutLoadedAnimationRootUpdate? Root,
    bool Returned, string? Failure);
internal sealed record FalloutLoadedAnimationContextSnapshot(string Source,
    FalloutLoadedAnimationBindCall? Bind, FalloutLoadedAnimationManagerSnapshot? Manager,
    uint TimeBits, FalloutActorMoverVector Delta, FalloutActorMoverVector Accumulation,
    IReadOnlyList<ushort> CurrentGroups, IReadOnlyList<ushort> QueuedGroups,
    IReadOnlyList<Guid?> Selected, byte Mode, byte Flags, byte Updated,
    FalloutLoadedAnimationUpdate? LastUpdate);
internal sealed record FalloutLoadedAnimationUpdateOwners(FalloutActorMoverState Mover,
    Func<FalloutPlayerLateFrameInvocation, uint> ReadFrameScaleBits,
    Func<FalloutReferenceModelDataInvocation, uint> ReadLoadScaleBits);

internal sealed partial class FalloutSourceAnimationContextState
{
    private FalloutLoadedAnimationSource? _loadedSource;
    private FalloutLoadedAnimationManagerState? _manager;
    private FalloutLoadedAnimationUpdateOwners? _loadedOwners;
    private FalloutNifSceneObject? _accumulationRoot;
    private readonly ushort[] _currentGroups = Enumerable.Repeat(ushort.MaxValue, 8).ToArray();
    private readonly ushort[] _queuedGroups = Enumerable.Repeat(ushort.MaxValue, 8).ToArray();
    private readonly Guid?[] _selected = new Guid?[8];
    private Vector3 _movementDelta, _accumulation;
    private float _animationTime;
    private byte _mode = byte.MaxValue, _animationFlags, _updated;
    internal FalloutLoadedAnimationBindCall? LastBind { get; private set; }
    internal FalloutLoadedAnimationUpdate? LastLoadedUpdate { get; private set; }

    internal FalloutLoadedAnimationBindCall BindLoadedModel(FalloutLoadedAnimationSource source,
        FalloutReferenceModelDataInvocation invocation, FalloutReferenceModelDataNode root,
        FalloutLoadedAnimationResources resources,
        FalloutLoadedAnimationUpdateOwners owners, FalloutPlayerThreadContextState thread,
        Action<FalloutReferenceModelDataInvocation> requireActual, Action requireModelLifetime)
    {
        RequireIdle(); source.Validate(); Check();
        if (_writer is { } writer) throw new NotSupportedException(writer);
        if (_root is not null || _manager is not null || LastBind is not null ||
            source.Tail.Identity != Source.Identity || resources.Source.Identity != source.Identity ||
            root.Node.Factory != resources.Model.Factory || root.Node.Process != Process || owners.Mover.Process != Process ||
            owners.Mover.Constructor != Construction.Invocation.ReferenceConstructor ||
            thread.Source.Main != Source.Scene.Main)
            throw new InvalidOperationException("Animation bind changed its actual constructor, model, mover or thread owner.");
        _loadedSource = source; _loadedOwners = owners;
        LastBind = new(invocation, Identity, Factory, Next(), _sequence, FalloutLoadedAnimationBindStep.Actor,
            null, root.Node.Factory, root.LogicalScene.Lifetime, [], null, null, null, null, false, null);
        var scope = thread.EnterAnimationInitialization(Guid.NewGuid(), Factory,
            FalloutLoadedAnimationScopeKind.Bind, () => Check());
        LastBind = LastBind with { Thread = scope, Changed = Next() };
        _busy = true;
        try
        {
            Step(FalloutLoadedAnimationBindStep.Root);
            root.Retain(() => { _root = root; Next(); });
            Step(FalloutLoadedAnimationBindStep.NamedNodes);
            var scene = root.LogicalScene;
            var names = new Dictionary<string, FalloutNifSceneObject>(StringComparer.Ordinal);
            foreach (var node in scene.Objects.Where(node => node.IsNode)) names.TryAdd(node.Name, node);
            LastBind = LastBind with
            {
                NamedNodes = source.NodeNames.Select(name => name is null ? null : names.GetValueOrDefault(name)?.Key).ToArray(),
                Changed = Next()
            };
            Step(FalloutLoadedAnimationBindStep.RootFlag);
            LastBind = LastBind with { RootFlag = scene.ClearAnimationBindFlag(invocation, requireActual), Changed = Next() };
            Step(FalloutLoadedAnimationBindStep.Manager);
            // Publication precedes sequence imports. A failed import retains
            // the real manager and its already registered sequence children.
            _manager = new(source, resources, scene, requireModelLifetime);
            LastBind = LastBind with { Manager = _manager.Factory, Changed = Next() };
            Step(FalloutLoadedAnimationBindStep.Resources); _manager.RegisterResources();
            foreach (var pair in _manager.ReadGroupIdentities()) _groups.Add(pair.Key, pair.Value);
            Step(FalloutLoadedAnimationBindStep.AccumulationRoot);
            _accumulationRoot = _manager.AccumulationRoot;
            if (_accumulationRoot is { } accumulation)
                scene.PublishLocalPose(accumulation.Key, new(new(0, 0, 0), [1, 0, 0, 0, 1, 0, 0, 0, 1], 1));
            Step(FalloutLoadedAnimationBindStep.InitialIdle);
            var idle = _manager.ApplyInitialIdle(); LastBind = LastBind with { InitialIdle = idle, Changed = Next() };
            Step(FalloutLoadedAnimationBindStep.Returned);
            scope = thread.ReturnAnimationInitialization(scope, () => Check());
            LastBind = LastBind with { Thread = scope, Returned = true, Changed = Next() };
            return LastBind;
        }
        catch (Exception error)
        {
            _failure ??= error.Message;
            scope = thread.RetainAnimationInitializationFailure(scope, error);
            LastBind = LastBind! with
            {
                Thread = scope,
                FailedAt = LastBind!.Step,
                Step = FalloutLoadedAnimationBindStep.Failed,
                Failure = error.Message,
                Changed = Next()
            };
            throw;
        }
        finally { _busy = false; }
        void Check()
        {
            requireActual(invocation); _requireLifetime(); root.RequireCurrent();
            if (_retired || invocation != Construction.Invocation || invocation.ThreadId != _thread ||
                Environment.CurrentManagedThreadId != _thread)
                throw new InvalidOperationException("Loaded animation left its actual source model initialization.");
        }
        void Step(FalloutLoadedAnimationBindStep step) { Check(); LastBind = LastBind! with { Step = step, Changed = Next() }; }
    }

    // The Reference loader performs this genuine zero-delta child after the
    // Common context bind. It is not represented by a fabricated Player frame.
    internal FalloutLoadedAnimationUpdate UpdateAfterLoad(FalloutReferenceModelDataInvocation invocation,
        Action<FalloutReferenceModelDataInvocation> requireActual)
    {
        RequireIdle();
        if (invocation != Construction.Invocation || Construction.Role != FalloutLoadedAnimationRole.Common)
            throw new InvalidOperationException("Animation load update changed its actual Common constructor.");
        _busy = true;
        try
        {
            return UpdateLoadedRoot(0, () => requireActual(invocation),
            () => _loadedOwners!.ReadLoadScaleBits(invocation), load: invocation);
        }
        catch (Exception error) { _failure ??= error.Message; throw; }
        finally { _busy = false; }
    }

    private uint ReadFrameScale(FalloutPlayerLateFrameInvocation frame) =>
        (_loadedOwners ?? throw new NotSupportedException("source-AnimData-bound-update-current-leaves-unavailable")).ReadFrameScaleBits(frame);

    internal void StopInitialGroupZero(FalloutReferenceModelDataInvocation invocation,
        Action<FalloutReferenceModelDataInvocation> requireActual)
    {
        RequireIdle(); requireActual(invocation);
        if (invocation != Construction.Invocation || _selected[0] is not null)
            throw new NotSupportedException("source-AnimData-current-selected-zero-group-deactivation-child-unowned");
        _selected[0] = null; _currentGroups[0] = _queuedGroups[0] = FalloutPlayerReturnTailSource.NoAnimationGroup; Next();
        requireActual(invocation);
    }

    private FalloutLoadedAnimationUpdate UpdateLoadedRoot(uint deltaBits, Action requireActual,
        Func<uint> readScale, FalloutPlayerLateFrameInvocation? frame = null, FalloutReferenceModelDataInvocation? load = null)
    {
        requireActual(); _root!.RequireCurrent();
        if (LastBind is not { Returned: true } || _manager is null || _loadedSource is null || _loadedOwners is null)
            throw new NotSupportedException("source-AnimData-entered-bind-resource-manager-not-returned");
        var entered = Next(); var beforeTime = _animationTime; var beforeAccumulation = _accumulation;
        uint? flags = null; var scales = new List<uint>(2); FalloutLoadedAnimationRootUpdate? rootUpdate = null; string? failure = null;
        var managerSequence = _manager.LastUpdate?.Changed ?? 0;
        try
        {
            _animationFlags &= 0xfe; _movementDelta.X = 0; _movementDelta.Y = 0; Next();
            if (_selected.Any(value => value is not null) || _currentGroups.Any(value => (value & 0xff) != 0xff) ||
                _queuedGroups.Any(value => (value & 0xff) != 0xff))
                throw new NotSupportedException("source-AnimData-current-selected-group-transition-and-event-children-unowned");
            var delta = BitConverter.UInt32BitsToSingle(deltaBits);
            if (!float.IsFinite(delta)) throw new NotSupportedException("source-AnimData-current-nonfinite-delta-unowned");
            _animationTime += delta; Next();
            if (_accumulationRoot is { } accumulation)
            {
                flags = _loadedOwners.Mover.ReadPlayerFields().Flags;
                // Even nonzero movement flags skip the correction body when
                // the SAME current selected slot is null after registration.
                requireActual(); var scene = _root.LogicalScene;
                scene.PublishLocalPose(accumulation.Key, accumulation.Local with
                { Translation = new(_accumulation.X, _accumulation.Y, _accumulation.Z) });
                rootUpdate = _manager.Update(BitConverter.SingleToUInt32Bits(_animationTime)); requireActual();
                var translation = accumulation.Local.Translation;
                _accumulation = new(translation.X, translation.Y, translation.Z); Next();
                scene.PublishLocalPose(accumulation.Key, accumulation.Local with { Translation = new(0, 0, 0) });
                _movementDelta.X = _accumulation.X - beforeAccumulation.X;
                _movementDelta.Y = _accumulation.Y - beforeAccumulation.Y; Next();
                var xScale = readScale(); scales.Add(xScale); requireActual();
                _movementDelta.X *= Finite(xScale);
                var yScale = readScale(); scales.Add(yScale); requireActual();
                _movementDelta.Y *= Finite(yScale); Next();
            }
            // The source's named-node event loop reads current selected
            // sequence slots. Registration did not publish any selected slot.
            _updated = 0; _mode = byte.MaxValue; Next(); requireActual();
        }
        catch (Exception error) { failure = error.Message; throw; }
        finally
        {
            LastLoadedUpdate = new(frame, load, Identity, Factory, entered, Next(), deltaBits,
                BitConverter.SingleToUInt32Bits(beforeTime), BitConverter.SingleToUInt32Bits(_animationTime),
                Vector(beforeAccumulation), Vector(_accumulation), Vector(_movementDelta), flags,
                scales.ToArray(), rootUpdate ?? (_manager.LastUpdate is { } retained && retained.Entered > managerSequence ? retained : null), failure is null, failure);
        }
        return LastLoadedUpdate!;
        static float Finite(uint bits) => float.IsFinite(BitConverter.UInt32BitsToSingle(bits)) ?
            BitConverter.UInt32BitsToSingle(bits) : throw new NotSupportedException("source-AnimData-nonfinite-current-reference-scale-unowned");
    }
    internal ushort ReadMovementGroupWord(FalloutPlayerLateFrameInvocation frame,
        Action<FalloutPlayerLateFrameInvocation> requireActual)
    { RequireIdle(); requireActual(frame); if (_writer is { } writer) throw new NotSupportedException(writer); return _currentGroups[1]; }
    private FalloutLoadedAnimationContextSnapshot? CaptureLoaded() => _loadedSource is null ? null :
        new(_loadedSource.Identity, LastBind, _manager?.Capture(), BitConverter.SingleToUInt32Bits(_animationTime),
            Vector(_movementDelta), Vector(_accumulation), _currentGroups.ToArray(), _queuedGroups.ToArray(),
            _selected.ToArray(), _mode, _animationFlags, _updated, LastLoadedUpdate);
    private static FalloutActorMoverVector Vector(Vector3 value) => new(BitConverter.SingleToUInt32Bits(value.X),
        BitConverter.SingleToUInt32Bits(value.Y), BitConverter.SingleToUInt32Bits(value.Z));
}
