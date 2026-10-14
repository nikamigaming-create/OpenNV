using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static partial class SourceMainScriptCallerContracts
{
    // Real component owners execute under an authored entered frame. The Scene
    // DTO supplies only the outer link for the production aggregate join. This
    // does not execute or certify the whole Main, a game, a save or Continue.
    private static void RunMoverActorFailureRetention()
    {
        using var fixture = new MoverDeltaFixture(failActorFormKind: true);
        fixture.AllocateData();
        Reject(() => fixture.Frame(1));
        var actualMover = fixture.Delta.Capture();
        var actualActor = fixture.FailureRetentionActor.Capture();
        var parent = actualMover.LastCall ?? throw new InvalidDataException("Mover lost its reached failed Actor child.");
        var child = actualActor.LastCall ?? throw new InvalidDataException("Actual Actor owner never entered.");
        Require(fixture.FailureFormKindReads == 1 &&
            parent is { Returned: false, FailedAt: FalloutPlayerMoverDeltaStep.ActorMovement,
                Failure: FalloutSourceReferencePositionState.PrefixBoundary, Data.Data: not null,
                ActorMovement.Returned: false, MovementReturned: null, Parent: null } &&
            child is { Returned: false, FailedAt: FalloutPlayerActorMovementStep.FormKind,
                Failure: FalloutSourceReferencePositionState.PrefixBoundary, Context: not null,
                FormKind: null, RawPosition: null, BasePosition: null, FinalPosition: null } &&
            parent.ActorMovement == child && child.Invocation.Frame == parent.Invocation &&
            child.Invocation.Data == parent.Data && child.Invocation.Data.Fields?.Model is null,
            "Mover omitted its actual failed Actor prefix or fabricated a downstream movement return.");

        // This is an authored outer Scene link around genuine captured child
        // owners. Exercise the same aggregate predicate used by Scene.Validate.
        var following = FalloutPlayerFollowingContinuationSource.Reviewed(fixture.Source.Controller.Fields.Controls);
        var prefix = FalloutPlayerLateFramePrefixSource.Reviewed(following);
        var outer = new FalloutPlayerLateFrameCall(prefix.Identity, fixture.Stack, parent.Invocation, 1, 2,
            FalloutPlayerLateFrameStep.Failed, FalloutPlayerLateFrameStep.MoverDelta,
            MoverDelta: parent, Failure: parent.Failure);
        var scene = fixture.Scene.Capture() with
        {
            LateFrame = new(prefix, fixture.Stack, fixture.Scene.Process, fixture.Scene.Constructor,
                0, 0, true, null, outer),
            MoverDelta = actualMover,
            ActorMovement = actualActor
        };
        FalloutStandalonePlayerSceneState.ValidatePlayerMoverOwners(scene);
        var cold = JsonSerializer.Deserialize<FalloutStandalonePlayerSceneSnapshot>(JsonSerializer.Serialize(scene))!;
        Require(!ReferenceEquals(cold.ActorMovement!.LastCall!.Positions,
                cold.MoverDelta!.LastCall!.ActorMovement!.Positions) &&
            !ReferenceEquals(cold.MoverDelta.LastCall.ProcessReads, cold.LateFrame!.LastCall!.MoverDelta!.ProcessReads),
            "The JSON aggregate test accidentally shared child collection identities.");
        FalloutStandalonePlayerSceneState.ValidatePlayerMoverOwners(cold);

        var mover = cold.MoverDelta!;
        var actor = cold.ActorMovement!;
        var call = mover.LastCall!;
        var retained = actor.LastCall!;
        Reject(() => FalloutStandalonePlayerSceneState.ValidatePlayerMoverOwners(cold with
        { MoverDelta = mover with { LastCall = call with { ActorMovement = null } } }));
        Reject(() => FalloutStandalonePlayerSceneState.ValidatePlayerMoverOwners(cold with { ActorMovement = null }));
        Reject(() => FalloutStandalonePlayerSceneState.ValidatePlayerMoverOwners(cold with
        { ActorMovement = actor with { LastCall = retained with { Invocation = retained.Invocation with { Call = Guid.NewGuid() } } } }));
        Reject(() => FalloutStandalonePlayerSceneState.ValidatePlayerMoverOwners(cold with
        { ActorMovement = actor with { Factory = Guid.NewGuid() } }));
        Reject(() => FalloutStandalonePlayerSceneState.ValidatePlayerMoverOwners(cold with
        { MoverDelta = mover with { LastCall = call with { MovementReturned = true, Returned = true } } }));
        var before = JsonSerializer.Serialize(fixture.Delta.Capture());
        Reject(() => fixture.Frame(2));
        Require(JsonSerializer.Serialize(fixture.Delta.Capture()) == before && fixture.FailureFormKindReads == 1,
            "A failed child was cleared or retried as a successful later frame.");
        Console.WriteLine("OPENNV_MOVER_ACTOR_FAILURE_RETENTION_PASS actualChildFailure=true actualReferenceGetter=true " +
            "sameFrameDataOwner=true noDownstreamReturn=true sceneAggregateJoin=true independentJsonObjects=true " +
            "omittedOrForeignChildRefused=true failedReplayRefused=true outerSceneLink=AUTHORED wholeMain=UNEXECUTED " +
            "nativeGameplay=UNEXECUTED saveContinue=UNEXECUTED");
    }

    private sealed partial class MoverDeltaFixture
    {
        private FalloutPlayerActorMovementState? _failureActor;
        private FalloutReferencePositionInvocation? _failureReference;
        private FalloutSourceInitialPlacementCells? _failureCells;
        private FalloutPlayerMoverSpeedCacheState? _failureSpeed;
        private FalloutWorldProjectionPublication? _failureProjection;
        internal int FailureFormKindReads { get; private set; }
        internal FalloutPlayerActorMovementState FailureRetentionActor => _failureActor ??
            throw new InvalidOperationException("The actual failed Actor owner is absent.");

        private FalloutPluginStack LoadFailureRetentionRecords(bool bindActor)
        {
            if (!bindActor) return FalloutPluginStack.Load(_directory, ["MoverDeltaFixture.esm"]);
            var settings = FalloutInstallationSettings.ReadIniLayers(() => [], [], "authored-failed-Actor-current-INI-owner");
            return FalloutPluginStack.Load([new("MoverDeltaFixture.esm", Path.Combine(_directory, "MoverDeltaFixture.esm"))],
                false, out _, settings);
        }

        private FalloutPlayerActorMovementState CreateFailureRetentionActor()
        {
            var orientationSource = FalloutPlayerWorldOrientationSource.Reviewed(Source.Controller.Fields.Controls);
            var cellsSource = FalloutInitialPlacementCellsSource.Reviewed(_position.Source);
            _failureProjection = BindMovementIntegrationOrientation(Scene, Actor.Reference);
            _failureCells = new(cellsSource, Records, Stack, Scene.Process, Scene.Constructor, RequireFailureReference);
            _failureSpeed = new(Source, Records, Stack, Runtime, Scene.Constructor, Actor, RequireLoad, RequireLife);
            var source = FalloutPlayerActorMovementSource.Reviewed(Source, cellsSource, orientationSource);
            var owners = new FalloutPlayerActorMovementOwners(Records, Common, Runtime, Mover, _position, Data,
                Scene.RequirePlayerReferenceOrientation(), Scene.ControlFields, _failureProjection, _failureCells,
                _failureSpeed, Records.IniSettings, ReadFailureFormKind,
                _ => throw UnreachedFailureSuffix(), _ => throw UnreachedFailureSuffix(),
                _ => throw UnreachedFailureSuffix(), _ => throw UnreachedFailureSuffix(),
                (_, _) => throw UnreachedFailureSuffix(), (_, _) => throw UnreachedFailureSuffix(),
                new UnreachedFailureRotation(orientationSource, Stack, Scene.Process, Actor.Reference));
            _failureActor = new(source, Stack, Scene.Process, Scene.Constructor, Current(), owners,
                RequireFailureActor, Lifetime);
            return _failureActor;
        }

        private void RequireFailureActor(FalloutPlayerActorMovementInvocation invocation)
        {
            RequireFrame(invocation.Frame);
            var parent = Delta.LastCall;
            if (parent is not { Step: FalloutPlayerMoverDeltaStep.ActorMovement, Failure: null } ||
                parent.Invocation != invocation.Frame || parent.Data != invocation.Data ||
                parent.Delta != invocation.Delta || parent.Mover.Flags != invocation.MoverWord)
                throw new InvalidOperationException("Actual failed Actor left its entered mover/frame/data scope.");
        }

        private FalloutReferenceFormKindRead ReadFailureFormKind(FalloutPlayerLateFrameInvocation frame)
        {
            var child = FailureRetentionActor.LastCall ?? throw UnreachedFailureSuffix();
            RequireFailureActor(child.Invocation);
            if (child.Step != FalloutPlayerActorMovementStep.FormKind || child.Invocation.Frame != frame)
                throw new InvalidOperationException("Actual reference getter entered outside its Actor FormKind child.");
            _failureReference = new(Guid.NewGuid(), Actor, Scene.Process, Scene.Constructor,
                frame.Common.Process, frame.Common.Epoch, _thread, FalloutReferencePositionIngress.ActorPosition,
                Frame: new(child.Invocation, FalloutReferenceFrameOperation.Borrow));
            ++FailureFormKindReads;
            try { return _position.ReadActorMovementFormKind(_failureReference); }
            finally { _failureReference = null; }
        }

        private void RequireFailureReference(FalloutReferencePositionInvocation invocation)
        {
            Lifetime();
            if (_failureReference != invocation || invocation.Frame?.Parent != FailureRetentionActor.LastCall?.Invocation)
                throw new InvalidOperationException("Reference getter lost its actual entered failed Actor parent.");
            RequireFailureActor(invocation.Frame!.Parent);
        }

        private void RetireFailureRetentionOwners()
        { _failureActor?.Retire(); _failureSpeed?.Retire(); _failureCells?.Retire(); }
        private static Exception UnreachedFailureSuffix() =>
            new InvalidOperationException("The actual failed FormKind getter must not reach a downstream Actor child.");
    }

    private sealed class UnreachedFailureRotation(FalloutPlayerWorldOrientationSource source, string stack,
        Guid process, FalloutFormKey reference) : IFalloutReferenceOrientationChangeConsumer
    {
        public FalloutPlayerWorldOrientationSource Source => source;
        public string Stack => stack;
        public Guid Process => process;
        public Guid Factory { get; } = Guid.NewGuid();
        public FalloutFormKey Reference => reference;
        public void RequireCurrent() => throw new InvalidOperationException("Failed FormKind child reached rotation admission.");
        public void Changed(Guid invocation, byte mask) => throw new InvalidOperationException("Failed FormKind child wrote rotation.");
    }
}
