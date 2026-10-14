using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    private FalloutPlayerMoverDeltaState? _sourcePlayerMoverDelta;
    private FalloutPlayerActorMovementState? _sourcePlayerActorMovement;
    private FalloutPlayerMoverSpeedCacheState? _sourcePlayerMoverSpeed;
    private FalloutPlayerLateFrameInvocation? _enteredPlayerMoverFrame;

    internal void PrepareStandalonePlayerMoverDelta(FalloutNumericIniSettings settings, Action requireLifetime)
    {
        RequireStandaloneControlPublication();
        if (_sourcePlayerMoverDelta is not null || _sourcePlayerActorMovement is not null)
            throw new InvalidOperationException("The actual Player mover body is already bound.");
        var scene = CampaignStandalonePlayerScene;
        var source = _sourcePlayerMoverSource ?? throw new NotSupportedException("source-Player-mover-source-unbound");
        var data = _sourcePlayerModelData ?? throw new NotSupportedException("source-reference-data-owner-unbound");
        var position = _sourcePlayerReferencePosition ?? throw new NotSupportedException("source-reference-position-owner-unbound");
        var cells = _sourceInitialPlacementCells ?? throw new NotSupportedException("source-TES-current-CELL-owner-unbound");
        var owned = records.OwnedSource ?? throw new NotSupportedException("source-Player-Actor-movement-owned-image-unbound");
        var actorSource = FalloutExecutableStringTable.ReadPlayerActorMovement(owned.FalloutExecutablePath,
            source, cells.Source, _standaloneWorldOrientationSource ??
                throw new NotSupportedException("source-Player-current-world-orientation-source-unbound"));
        var actor = ActorProcesses.Read(_enginePlayer);
        var common = ProcessCommon.ReadSceneQueries(_enginePlayer, actor.Epoch);
        var saved = scene.Capture();
        void Living()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            requireLifetime();
            if (!ReferenceEquals(_sourcePlayerModelData, data) || !ReferenceEquals(_sourcePlayerReferencePosition, position) ||
                !ReferenceEquals(_sourceInitialPlacementCells, cells))
                throw new InvalidOperationException("Player movement lost its actual reference/TES/data owners.");
        }
        _sourcePlayerMoverSpeed = new(source, records, scene.Stack, ProcessRuntime, scene.Constructor, actor.Source,
            RequireActualReferenceModel, Living, _processCommonRestore?.ReferenceOwners?.MoverSpeed);
        var consumers = new SourceReferencePositionConsumers(this);
        var actorOwners = new FalloutPlayerActorMovementOwners(records, ProcessCommon, ProcessRuntime, scene.ActorMover,
            position, data, scene.RequirePlayerReferenceOrientation(), scene.ControlFields,
            CampaignStandaloneWorldProjection, cells, _sourcePlayerMoverSpeed, settings,
            frame => WithPlayerFramePosition(frame, FalloutReferenceFrameOperation.Borrow, position.ReadActorMovementFormKind),
            frame => WithPlayerFramePosition(frame, FalloutReferenceFrameOperation.Borrow, position.ReadActorMovementRawPosition),
            frame => WithPlayerFramePosition(frame, FalloutReferenceFrameOperation.Borrow, position.ReadCurrentPosition),
            frame => WithPlayerFramePosition(frame, FalloutReferenceFrameOperation.Borrow, position.ReadActorYawMode),
            frame => WithPlayerFramePosition(frame, FalloutReferenceFrameOperation.Borrow, cells.ReadActorMovementCurrentCell),
            (frame, value) => WithPlayerFramePosition(frame, FalloutReferenceFrameOperation.CorrectPosition,
                invocation => position.StoreActorPosition(invocation, value, consumers)),
            (frame, value) => WithPlayerFramePosition(frame, FalloutReferenceFrameOperation.BasePosition,
                invocation => position.StoreBasePosition(invocation, value, consumers)),
            new PlayerFrameRotationChanges(this));
        _sourcePlayerActorMovement = new(actorSource, scene.Stack, scene.Process, scene.Constructor, common,
            actorOwners, RequireActualPlayerActorMovement, Living, saved.ActorMovement);
        var moverOwners = new FalloutPlayerMoverDeltaOwners(scene.ActorMover, ProcessCommon, ProcessRuntime,
            _sourcePackageFields[_enginePlayer], _sourceReferenceScriptInitializer ??
                throw new NotSupportedException("source-reference-flags-owner-unbound"), data, StandaloneInterface,
            frame => scene.ReadMoverDeltaView(frame, RequireActualPlayerMoverFrame),
            frame =>
            {
                RequireActualPlayerMoverFrame(frame);
                return WithActualReferencePosition(FalloutReferencePositionIngress.ActorPosition, consumers.ReadCurrentParentCell);
            },
            (frame, body) =>
            {
                RequireActualPlayerMoverFrame(frame);
                return WithActualReferencePosition(FalloutReferencePositionIngress.ActorPosition, body);
            }, position, consumers, _sourcePlayerActorMovement);
        _sourcePlayerMoverDelta = new(source, records, scene.Stack, scene.Process, scene.Constructor,
            common, moverOwners, RequireActualPlayerMoverFrame, Living, saved.MoverDelta);
        scene.BindLateMoverDelta(_sourcePlayerMoverDelta, _sourcePlayerActorMovement, UpdateStandalonePlayerMoverDelta);
    }

    private FalloutPlayerMoverDeltaCall UpdateStandalonePlayerMoverDelta(FalloutPlayerLateFrameInvocation frame)
    {
        if (_enteredPlayerMoverFrame is not null)
            throw new InvalidOperationException("Player mover reentered the current Main frame.");
        _enteredPlayerMoverFrame = frame;
        try
        {
            RequireActualPlayerMoverFrame(frame);
            return (_sourcePlayerMoverDelta ?? throw new NotSupportedException("source-Player-mover-body-unbound")).Update(frame);
        }
        finally { _enteredPlayerMoverFrame = null; }
    }

    private void RequireActualPlayerMoverFrame(FalloutPlayerLateFrameInvocation frame)
    {
        RequireStandaloneControlPublication();
        CampaignStandalonePlayerScene.RequireLateMoverDeltaInvocation(frame);
        if (_enteredPlayerMoverFrame != frame || frame.ThreadId != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Player mover left its actual entered Main/frame/thread.");
        var actor = ActorProcesses.Read(_enginePlayer);
        var common = ProcessCommon.ReadSceneQueries(_enginePlayer, actor.Epoch);
        if (actor.Source != frame.Common.Actor || actor.Epoch != frame.Common.Epoch || actor.Retired ||
            common.Process != frame.Common.Process || common.GameplayOwnership != frame.Common.GameplayOwnership)
            throw new InvalidOperationException("Player mover changed its current Actor/Common epoch.");
    }

    private void RequireActualPlayerActorMovement(FalloutPlayerActorMovementInvocation invocation)
    {
        RequireActualPlayerMoverFrame(invocation.Frame);
        if (_sourcePlayerMoverDelta?.LastCall is not { Step: FalloutPlayerMoverDeltaStep.ActorMovement, Failure: null } call ||
            call.Invocation != invocation.Frame || call.Data != invocation.Data || call.Delta != invocation.Delta ||
            call.Mover.Flags != invocation.MoverWord)
            throw new InvalidOperationException("Actor movement left its actual entered Player mover child.");
    }

    private T WithPlayerFramePosition<T>(FalloutPlayerLateFrameInvocation frame,
        FalloutReferenceFrameOperation operation, Func<FalloutReferencePositionInvocation, T> action)
    {
        RequireActualPlayerMoverFrame(frame);
        var call = _sourcePlayerActorMovement?.LastCall ??
            throw new InvalidOperationException("Reference field borrow has no actual entered Actor movement child.");
        RequireActualPlayerActorMovement(call.Invocation);
        if (call.Invocation.Frame != frame || call.Failure is not null || call.Returned)
            throw new InvalidOperationException("Movement fields borrowed another frame's reference scope.");
        return WithActualReferencePosition(FalloutReferencePositionIngress.ActorPosition, action,
            new FalloutReferenceFrameReceipt(call.Invocation, operation));
    }

    private sealed class PlayerFrameRotationChanges(FalloutReferenceWorld world) : IFalloutReferenceOrientationChangeConsumer
    {
        public FalloutPlayerWorldOrientationSource Source => world._standaloneWorldOrientationSource!;
        public string Stack => world.CampaignStandalonePlayerScene.Stack;
        public Guid Process => world.CampaignStandalonePlayerScene.Process;
        public Guid Factory => world._sourceReferenceChanges!.Factory;
        public FalloutFormKey Reference => world._enginePlayer;
        public void RequireCurrent() => world.RequireActualPlayerMoverFrame(world._enteredPlayerMoverFrame ??
            throw new InvalidOperationException("Rotation change lost its actual entered Player frame."));
        public void Changed(Guid call, byte mask)
        {
            var frame = world._enteredPlayerMoverFrame ?? throw new InvalidOperationException("Rotation change lost its frame.");
            if (call != frame.Caller.Invocation || mask != FalloutReferencePositionSource.PositionChangeMask)
                throw new InvalidDataException("Movement rotation changed its source callback or mask.");
            _ = world.WithPlayerFramePosition(frame, FalloutReferenceFrameOperation.AngleReset, invocation =>
                world._sourceReferenceChanges!.ReferenceChanged(invocation, mask, false, new SourceReferencePositionConsumers(world)));
        }
    }
}
