using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;
using OpenNV.Runtime.World.Physics;

internal static partial class SourceMainScriptCallerContracts
{
    private static void RunPlayerMovementIntegration()
    {
        var main = FalloutMainScriptCallerSource.Read(new(FalloutSourceMainFamily.Fallout3, new('1', 64),
            FalloutImmediateScriptSource.ContractForEngine(FalloutSourceMainFamily.Fallout3)));
        var declaration = FalloutActorProcessRuntimeDeclaration.ForExecutable(main.EngineSha256);
        var stack = new string('2', 64); var player = new FalloutFormKey("MovementIntegrationFixture.esm", 0x14);
        FalloutCombatActorIdentity Identity(FalloutFormKey actor) => new(actor, new("MovementIntegrationFixture.esm", 7),
            "ENGINE_PLAYER", 0, main.EngineSha256, "NPC_", 0, new('4', 64), true);
        foreach (var failure in new[] { "none", "update", "current", "reentry", "capture", "observed", "post-observed" })
        {
            using var common = new FalloutActorProcessCommonState(declaration, stack, Identity,
                _ => throw new InvalidOperationException("Movement integration copied gameplay."),
                _ => throw new InvalidOperationException("Movement integration borrowed a native body."));
            using var actor = new FalloutActorProcessRuntimeState(declaration, stack, player, Identity);
            common.Construct(player, 1, FalloutDetectionProcessLevel.High);
            FalloutStandalonePlayerSceneState? prepared = null;
            var preparationThread = 0;
            var preparationFailure = OnMovementFixtureThread(() =>
            {
                preparationThread = Environment.CurrentManagedThreadId;
                prepared = new(FalloutStandalonePlayerSceneSource.Read(main), stack, Guid.NewGuid());
                if (failure == "observed") prepared.ObserveSourceControlCommand(new(player,
                    "authored-actual-new-game-bootstrap-effect", Guid.NewGuid(), false, [false, false, false, false, true]));
                Require(prepared.Capture() is { MovementCall: null, Failure: null },
                    "Owned-data preparation created a live control invocation.");
            });
            Require(preparationFailure is null && preparationThread != Environment.CurrentManagedThreadId,
                "Movement integration did not prepare its source scene on an independent loading worker.");
            var scene = prepared!;
            var input = FalloutDirectInputState.CreateHeadless(stack, new(_ => null));
            input.Publish(new byte[256], new byte[8], 9, -13, -120);
            var inputBefore = input.Capture();
            var beforePublication = scene.Capture();
            Require(OnMovementFixtureThread(() => scene.BindInputManager(input)) is InvalidOperationException &&
                scene.Capture().Sequence == beforePublication.Sequence,
                "An arbitrary thread bound the current input/control owner before actual gameplay publication.");
            var hud = new FalloutPlayerReferenceHudState(FalloutPlayerReferenceHudSource.Reviewed(main), stack, scene.Process,
                AuthoredReferenceHudComponents());
            var manager = new FalloutStandaloneInterfaceState(FalloutStandaloneInterfaceSource.Read(main), stack, scene.Process);
            var native = new AuthoredInterfaceObject();
            manager.PublishManager(native.Publication(manager, FalloutStandaloneInterfaceObjectRole.PipBoyManager, stack), native.ReadLiving);
            var idle = new AuthoredIdleConsumers { ModelOverride = () => scene.ReadSelectedIdleModel(() => null) };
            scene.BindInputManager(input); scene.BindActorProcessQueries(() => common.ReadSceneQueries(player, 1));
            if (failure is "observed" or "post-observed")
            {
                scene.BindMovementControlConfiguration(new(
                    [new(FalloutPlayerControlConfiguration.InvertYName, FalloutIniCollection.Prefs, 0),
                        new(FalloutPlayerControlConfiguration.WorldFovName, FalloutIniCollection.Main,
                            FalloutPlayerControlConfiguration.WorldFovDefaultBits)], [], stack + ":authored-owned-executable:" + main.EngineSha256));
                Require(OnMovementFixtureThread(() => scene.ObserveSourceControlCommand(new(player,
                    "foreign-effect", Guid.NewGuid(), true, []))) is InvalidOperationException,
                    "Foreign gameplay thread changed source control observations after real input publication.");
            }
            Require(OnMovementFixtureThread(() => scene.Capture()) is InvalidOperationException &&
                OnMovementFixtureThread(() => scene.StoreSceneClock(Guid.NewGuid(), 1, 0x3ca3d70a)) is InvalidOperationException &&
                scene.Capture().Sequence == beforePublication.Sequence,
                "A foreign thread read or entered the source scene after its actual gameplay/input publication.");
            scene.BindProcessSceneUpdate(() => common.UpdatePlayerScene(player, 1));
            scene.BindPostProcessContext(() => common.ReadSceneContext(player, 1));
            scene.BindTimedActionCondition(() => common.ReadPlayerActionCondition(player, 1));
            scene.BindPostActions(read => common.UpdatePostActions(player, 1, read),
                read => common.UpdateDetectionAction(player, 1, read, () => 1.2f));
            scene.BindActorDistances(manager, () => 20, _ => throw new InvalidOperationException("Empty distance list queried a body."), () => 3);
            scene.BindIdleAnimation(_ => common.UpdateIdleAnimation(player, 1, idle), () => common.LastIdleAnimationUpdate);
            scene.BindMovementFlags(() => common.ReadAndClearPlayerMovementFlags(player, 1));
            var admission = FalloutActorPhysicalRegistrationPredicate.Execute(uint.MaxValue,
                new PhysicalRegistrationFixture(FalloutActorPhysicalAdmissionSource.Reviewed(main), player,
                    common.ReadPhysicalActivity(player, 1))
                {
                    ModeReader = () => actor.Life(player),
                    ActivityReader = () => common.ReadPhysicalActivity(player, 1),
                    Model = new(scene.Process, Guid.NewGuid(), 345)
                }, _ => { });
            using var physics = new AuthoredPlayerPhysicsTransport(scene.Source.Identity, stack, scene.Process, admission, failure);
            var foreign = new AuthoredPlayerPhysicsTransport(scene.Source.Identity, stack, Guid.NewGuid(), admission, "none");
            Reject(() => scene.BindMovementPhysics(foreign)); foreign.Dispose();
            scene.BindMovementPhysics(physics);
            Reject(() => scene.BindMovementPhysics(physics));
            if (failure == "reentry") physics.CurrentCallback = () =>
            {
                try { scene.Capture(); } catch (InvalidOperationException) { }
            };
            physics.EnableCurrentFailure();
            var invocation = Guid.NewGuid(); scene.StoreSceneClock(invocation, 1, 0x3ca3d70a); scene.EnterSceneVirtual(invocation, 1);
            Reject(() => scene.ExecuteInitialVirtualPrefix(invocation, 1, () => { }, hud,
                () => throw new InvalidOperationException("Constructed timers unexpectedly polled a provider.")));
            if (failure == "post-observed") scene.ObserveSourceControlCommand(new(player,
                "authored-actual-shared-script-host-effect", Guid.NewGuid(), true, [true, false, false]));
            var saved = scene.Capture(); var call = saved.MovementCall!;
            Require(call.Physics is { } physical && physical.Factory == physics.Factory && physical.CapturedProcess == scene.Process &&
                physical.Admission == admission && call.Returned == false && saved.SceneBracket == 1 &&
                saved.ThreadContextEntry is { CurrentKind: 52, ScopeEntered: true },
                "Player movement lost its physical child history or falsely closed its unfinished frame/thread scope.");
            Require(input.Sample == inputBefore.Sample && input.Revision == inputBefore.Revision,
                "Scene movement replayed input polling instead of consuming the current cached axes.");
            if (failure == "none")
            {
                Require(call is { Step: "movement-control-child", Failure: FalloutPlayerMovementControlState.ConfigurationBoundary,
                    Physics.Frame.Returned: true, Physics.Utility.Phase: "zero-system-update-completed",
                    Control.Call: { ArgumentBits: 0x3ca3d70a, MoverWord: 0, FirstArgument: 0, LastArgument: 0, CacheStores: 2 } } &&
                    call.Control.Call.MouseReads.Select(read => read.Value).SequenceEqual(new[] { 9, -13, -120 }),
                    "Returned Actor physics skipped actual control input or promoted its configuration boundary to success.");
                Reject(() => FalloutStandalonePlayerSceneState.Validate(saved with { MovementCall = call with { Control = null } }));
                Reject(() => FalloutStandalonePlayerSceneState.Validate(saved with
                { MovementCall = call with { Control = call.Control! with { Call = call.Control.Call! with { ArgumentBits = 0 } } } }));
                Reject(() => FalloutStandalonePlayerSceneState.Validate(saved with
                { MovementCall = call with { Control = call.Control! with { Call = call.Control.Call! with { InputFactory = Guid.NewGuid() } } } }));
                Reject(() => FalloutStandalonePlayerSceneState.Validate(saved with
                { MovementCall = call with { Physics = call.Physics! with { Utility = call.Physics.Utility with { Phase = "constructed" } } } }));
                Reject(() => FalloutStandalonePlayerSceneState.Validate(saved with
                { MovementCall = call with { Physics = call.Physics! with { Frame = call.Physics.Frame! with { Main = Guid.NewGuid() } } } }));
            }
            else if (failure == "post-observed")
            {
                Require(call is { Failure: FalloutPlayerMovementControlState.WheelBoundary,
                    Control.Fields: { WordAvailable: false, FlagStores: 0 },
                    Control.Call: { FieldsRead.WordAvailable: true, FieldsRead.DisabledWord: 0, Axes.Wheel: -120 } } &&
                    saved.ControlObservations is [{ Command.Enable: true }],
                    "Post-failure observation erased the genuine historical flag read or promoted its current stale word.");
                Reject(() => FalloutStandalonePlayerSceneState.Validate(saved with { ControlObservations = [] }));
            }
            else if (failure == "observed")
            {
                Require(call is { Step: "movement-control-child", Failure: FalloutPlayerControlFieldsState.ObservedWriterBoundary,
                    Physics.Frame.Returned: true, Control.Fields: { WordAvailable: false, FlagStores: 0, Call: null },
                    Control.Call: { Step: FalloutPlayerMovementControlStep.PlayerFlags, FieldsRead: null, Axes: null, WorldFov: null } } &&
                    saved.ControlObservations is [{ Command.Source.ObjectId: 0x14 }],
                    "Actual bootstrap observation retained stale constructor flags or admitted later world/reference getters.");
                Reject(() => FalloutStandalonePlayerSceneState.Validate(saved with { ControlObservations = [] }));
            }
            else if (failure == "capture")
            {
                Require(call.Step == "movement-current-actor-field-child" && call.Control is null &&
                    call.Physics is { CompleteCapture: false, Frame.Returned: true } && physics.Updates == 1 &&
                    call.Failure!.StartsWith("source-Player-living-physics-current-capture-unavailable:", StringComparison.Ordinal),
                    "Unavailable world capture lost its returned physical history or admitted following controls.");
                Reject(() => FalloutStandalonePlayerSceneState.RequirePlayable(saved));
            }
            else Require(call.Step == "movement-current-actor-field-child" && call.Control is null &&
                call.Failure == (failure == "reentry" ? "Movement consumer swallowed a Player owner reentry refusal." :
                    AuthoredPlayerPhysicsTransport.Failure) &&
                (failure == "update" ? call.Physics!.Frame is { Returned: false, Failure: AuthoredPlayerPhysicsTransport.Failure } :
                    call.Physics!.Frame is null) && physics.Updates == (failure == "update" ? 1 : 0),
                "Failed current physics owner admitted input or discarded the actual reached failure prefix.");
            Reject(() => FalloutStandalonePlayerSceneState.Validate(saved with { MovementCall = call with { Physics = null } }));
            Reject(() => FalloutStandalonePlayerSceneState.Validate(saved with
            { MovementCall = call with { Physics = call.Physics! with { CapturedProcess = Guid.NewGuid() } } }));
            Reject(() => FalloutStandalonePlayerSceneState.RequirePlayable(saved));
            var roundTrip = JsonSerializer.Deserialize<FalloutStandalonePlayerSceneSnapshot>(JsonSerializer.Serialize(saved))!;
            var currentReads = physics.CurrentReads; var updates = physics.Updates;
            FalloutStandalonePlayerSceneState? preparedCold = null;
            Require(OnMovementFixtureThread(() => preparedCold = new(scene.Source, stack, Guid.NewGuid(), roundTrip)) is null,
                "Cold diagnostic history required the old live gameplay thread during owned-data preparation.");
            var cold = preparedCold!;
            Require(cold.Capture().MovementCall == roundTrip.MovementCall && physics.CurrentReads == currentReads && physics.Updates == updates,
                "Cold Player history replayed physical/input owners or promoted historical factories.");
            Reject(() => cold.BindMovementPhysics(physics));
            Reject(() => FalloutStandalonePlayerSceneState.RequirePlayable(cold.Capture()));
            cold.Retire(); scene.Retire(); input.Retire(); hud.Retire(); manager.Retire(); native.Retire();
        }
        Console.WriteLine("OPENNV_PLAYER_MOVEMENT_INTEGRATION_PASS physicsBeforeControls=true failedPhysicalPrefix=true " +
            "sameMainInputAndClock=true workerPreparationThenGameplayPublication=true foreignThreadRefused=true " +
            "coldHistoryOnly=true unfinishedFrameRefused=true nativePhysics=AUTHORED_TRANSPORT gameplay=UNEXECUTED");
    }

    private static Exception? OnMovementFixtureThread(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception error) { failure = error; } });
        thread.Start(); thread.Join(); return failure;
    }

    // This fixture checks transport/call ownership. Its model snapshots are
    // authored receipts; separate native probes must prove actual Godot bodies.
    private sealed class AuthoredPlayerPhysicsTransport : IFalloutPlayerLivingPhysicsOwner, IDisposable
    {
        internal const string Failure = "authored-current-Player-physics-owner-refusal";
        private readonly string _failureKind;
        private readonly FalloutActorPhysicalRegistrationTrace _admission;
        private readonly FalloutActorPenetrationUtility _utility;
        private readonly IReadOnlyList<FalloutLivingModelSnapshot> _models =
            [new("authored-view", new('5', 64), []), new("authored-reference", new('6', 64), [])];
        private long _revision;
        private FalloutPlayerPhysicsFrameCall? _frame;
        private string? _failure;
        private bool _enableCurrentFailure;
        internal AuthoredPlayerPhysicsTransport(string source, string stack, Guid process,
            FalloutActorPhysicalRegistrationTrace admission, string failureKind)
        {
            Source = source; Stack = stack; Process = process; _admission = admission; _failureKind = failureKind;
            _utility = new(() => throw new InvalidOperationException("Unregistered utility read a model."),
                () => throw new InvalidOperationException("Unregistered utility read a controller."),
                () => throw new InvalidOperationException("Zero-system utility read settings."), new UnusedPlayerPhysicsClock(),
                new FalloutBodyPenetrationBackend(_ => throw new InvalidOperationException("Zero-system utility constructed a system.")));
        }
        public Guid Process { get; }
        public string Source { get; }
        public string Stack { get; }
        public Guid Factory { get; } = Guid.NewGuid();
        internal int CurrentReads { get; private set; }
        internal int Updates { get; private set; }
        internal Action? CurrentCallback { get; set; }
        internal void EnableCurrentFailure() => _enableCurrentFailure = true;
        public void RequireCurrent()
        {
            ++CurrentReads;
            CurrentCallback?.Invoke();
            if (_failureKind == "current" && _enableCurrentFailure) throw new NotSupportedException(Failure);
        }
        public void Update(Guid main, long ordinal)
        {
            ++Updates; var entered = ++_revision;
            _frame = new(main, ordinal, entered, entered, "actor-penetration-utility", false, null);
            if (_failureKind == "update")
            {
                _failure = Failure; _frame = _frame with { Changed = ++_revision, Failure = _failure };
                throw new NotSupportedException(Failure);
            }
            _utility.Update(); _frame = _frame with { Changed = ++_revision, Returned = true, Step = "returned" };
        }
        public FalloutPlayerLivingPhysicsSnapshot Capture() => new(FalloutPlayerLivingPhysicsSnapshot.CurrentSchema,
            Source, Stack, Process, Factory, _revision, FalloutActorPhysicalAdmissionFields.Constructed,
            _admission, _models, _utility.Capture(), _frame, _failure,
            CaptureRefusals: _failureKind == "capture" && _frame is not null
                ? [new("world", "authored-unavailable-world", nameof(NotSupportedException), "authored-pending-world-capture")]
                : []);
        public void Dispose() => _utility.Dispose();
    }

    private sealed class UnusedPlayerPhysicsClock : IFalloutPenetrationClock
    {
        public long ReadCounterTicks() => throw new InvalidOperationException("Zero-system utility read the clock.");
        public uint ReadTicksPerMillisecondBits() => throw new InvalidOperationException("Zero-system utility read clock scale.");
        public long ConvertDurationTicks(uint scaledFloat32Bits) => throw new InvalidOperationException("Zero-system utility converted duration.");
    }
}
