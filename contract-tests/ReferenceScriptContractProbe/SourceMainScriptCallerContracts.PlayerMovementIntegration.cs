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
        foreach (var failure in new[] { "none", "update", "current", "reentry", "capture", "observed", "post-observed", "returned", "opacity", "opacity-failed", "weapon", "weapon-failed", "following", "following-input" })
        {
            var movementReturns = failure is "returned" or "opacity" or "opacity-failed" or "weapon" or "weapon-failed" or "following" or "following-input";
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
            var input = FalloutDirectInputState.CreateHeadless(stack, new(_ => null,
                defaults: _ => new(byte.MaxValue, byte.MaxValue)));
            input.Publish(new byte[256], new byte[8], movementReturns ? 0 : 9,
                movementReturns ? 0 : -13, movementReturns ? 0 : -120);
            if (failure == "following-input") BindMovementIntegrationOriginalInput(scene, input);
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
            if (failure is "observed" or "post-observed" || movementReturns)
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
                OnMovementFixtureThread(() => scene.StoreSceneClock(Guid.NewGuid(), 1, () => AuthoredSceneCachedDelta(scene, 0x3ca3d70a))) is InvalidOperationException &&
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
            var orientation = movementReturns ? BindMovementIntegrationOrientation(scene, player) : null;
            if (failure is "opacity" or "opacity-failed" or "weapon" or "weapon-failed" or "following" or "following-input")
                BindMovementIntegrationOpacity(scene, common, player, failure == "opacity-failed");
            if (failure is "weapon" or "weapon-failed" or "following" or "following-input")
                BindMovementIntegrationWeapon(scene, common, player, failure == "weapon-failed");
            if (failure is "following" or "following-input")
                BindMovementIntegrationFollowing(scene, common, actor, player, failure == "following-input");
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
            var invocation = Guid.NewGuid(); scene.StoreSceneClock(invocation, 1, () => AuthoredSceneCachedDelta(scene, 0x3ca3d70a)); scene.EnterSceneVirtual(invocation, 1);
            Reject(() => scene.ExecuteInitialVirtualPrefix(invocation, 1, () => { }, hud,
                () => failure is "opacity" or "opacity-failed" or "weapon" or "weapon-failed" or "following" or "following-input" ? .25f :
                    throw new InvalidOperationException("Constructed timers unexpectedly polled a provider.")));
            if (failure == "post-observed") scene.ObserveSourceControlCommand(new(player,
                "authored-actual-shared-script-host-effect", Guid.NewGuid(), true, [true, false, false]));
            var saved = scene.Capture(); var call = saved.MovementCall!;
            Require(call.Physics is { } physical && physical.Factory == physics.Factory && physical.CapturedProcess == scene.Process &&
                physical.Admission == admission && call.Returned == movementReturns && saved.SceneBracket == 1 &&
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
            else if (movementReturns)
            {
                Require(call is { Step: "returned", Returned: true, Failure: null,
                    Physics.Frame.Returned: true, Control: { Failure: null, Call: { Returned: true, Result: false,
                        Step: FalloutPlayerMovementControlStep.Returned, MouseReads.Count: 3 } } } &&
                    saved.Failure == (failure switch
                    {
                        "returned" => "source-Player-following-movement-frame-services-unbound",
                        "opacity-failed" => "authored-current-opacity-branch-failed",
                        "weapon" => "source-Player-following-weapon-frame-services-unbound",
                        "weapon-failed" => "source-Player-following-activity-or-status-source-effects-unbound",
                        "following" => FalloutPlayerFollowingContinuation.ReadyItemBoundary,
                        "following-input" => FalloutPlayerFollowingContinuation.ReadyItemGlobalsBoundary,
                        _ => "source-Player-following-teleport-frame-services-unbound"
                    }),
                    "Returned movement children were relabeled as failed or promoted the unfinished whole Player frame.");
                if (failure != "returned") RequireMovementIntegrationOpacity(saved, failure == "opacity-failed");
                if (failure is "weapon" or "weapon-failed" or "following" or "following-input")
                {
                    var weapon = saved.WeaponContinuation!;
                    Require(weapon is { Action.Value: 0, Activity.Value: 0, Status.PoolQueried: true, Status.AfterFlags: 0x20 } &&
                        weapon.Returned == (failure is "weapon" or "following" or "following-input") && weapon.EffectsRequired == (failure == "weapon-failed") &&
                        weapon.Invocation == saved.FollowingServices!.Invocation,
                        "Following weapon services lost the actual constructor, current status cache or failed effects prefix.");
                    Reject(() => FalloutStandalonePlayerSceneState.Validate(saved with
                    { WeaponContinuation = weapon with { Invocation = Guid.NewGuid() } }));
                }
                if (failure is "following" or "following-input") RequireMovementIntegrationFollowing(saved, failure == "following-input");
                Reject(() => FalloutStandalonePlayerSceneState.Validate(saved with
                { MovementCall = call with { Returned = false } }));
                Reject(() => FalloutStandalonePlayerSceneState.Validate(saved with
                { MovementCall = call with { Control = call.Control! with { Call = call.Control.Call! with { Returned = false } } } }));
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
            Require(cold.Capture().MovementCall == roundTrip.MovementCall && cold.Capture().FollowingServices == roundTrip.FollowingServices &&
                cold.Capture().Opacity == roundTrip.Opacity && cold.Capture().WeaponContinuation == roundTrip.WeaponContinuation &&
                cold.Capture().FollowingContinuation == roundTrip.FollowingContinuation &&
                physics.CurrentReads == currentReads && physics.Updates == updates,
                "Cold Player history replayed physical/input owners or promoted historical factories.");
            Reject(() => cold.BindMovementPhysics(physics));
            Reject(() => FalloutStandalonePlayerSceneState.RequirePlayable(cold.Capture()));
            if (failure is "following" or "following-input")
            {
                var historical = cold.Capture();
                for (var cycle = 0; cycle < 2; ++cycle)
                {
                    FalloutStandalonePlayerSceneState? followingCold = null;
                    Require(OnMovementFixtureThread(() => followingCold = new(scene.Source, stack, Guid.NewGuid(), historical)) is null,
                        "Further cold source history required a retired caller or replayed constructors.");
                    var next = followingCold!;
                    var retained = next.Capture();
                    Require(retained.FollowingContinuation == historical.FollowingContinuation &&
                        retained.Inhibition!.History.Count == historical.Inhibition!.History.Count &&
                        retained.FollowingTimerFlag!.Sequence == historical.FollowingTimerFlag!.Sequence &&
                        retained.Inhibition.Publications!.Count == historical.Inhibition.Publications!.Count + 1 &&
                        physics.CurrentReads == currentReads && physics.Updates == updates,
                        "Three cold component cycles replayed fields, input or native physics instead of retaining history.");
                    RequireMovementIntegrationFollowingHistory(retained);
                    next.Retire(); historical = retained;
                }
            }
            cold.Retire(); scene.Retire(); input.Retire(); hud.Retire(); manager.Retire(); native.Retire();
            orientation?.RetireCurrent();
        }
        Console.WriteLine("OPENNV_PLAYER_MOVEMENT_INTEGRATION_PASS physicsBeforeControls=true failedPhysicalPrefix=true " +
            "sameMainInputAndClock=true workerPreparationThenGameplayPublication=true foreignThreadRefused=true " +
            "returnedChildrenRetained=true followingSharedFields=true threeColdComponentHistories=true " +
            "coldHistoryOnly=true unfinishedFrameRefused=true nativePhysics=AUTHORED_TRANSPORT gameplay=UNEXECUTED");
    }

    private static void BindMovementIntegrationOpacity(FalloutStandalonePlayerSceneState scene,
        FalloutActorProcessCommonState common, FalloutFormKey player, bool fail)
    {
        var source = FalloutPlayerOpacitySource.Reviewed(FalloutPlayerMovementControlSource.Reviewed(scene.Source.Main));
        var opacity = new FalloutPlayerOpacityState(source, scene.Stack, scene.Process,
            scene.ReadOpacityFirstPerson, () => null,
            scene.ReadOpacityScaledTimerBits, _ => fail ? throw new NotSupportedException("authored-current-opacity-branch-failed") :
                BitConverter.SingleToUInt32Bits(1), scene.RequireOpacityInvocation);
        scene.BindPlayerOpacity(opacity, (main, ordinal, invocation, owner) =>
        {
            scene.RequireOpacityInvocation(main, ordinal);
            Require(!owner.SkipTeleportUpdate(() => throw new InvalidOperationException("Zero teleport gates queried current state.")),
                "Constructor-zero gates skipped actual High update.");
            try
            {
                return common.UpdateTeleportFade(source, player, 1, invocation,
                    (_, read, store) => owner.BindTeleportConsumers(main, ordinal, invocation, read, store));
            }
            finally
            {
                if (common.ReadTeleportFadeReceipt(player, 1) is { } receipt)
                    scene.ObservePlayerTeleportFade(receipt);
            }
        });
    }

    private static void RequireMovementIntegrationOpacity(FalloutStandalonePlayerSceneSnapshot saved, bool failed)
    {
        var call = saved.FollowingServices!;
        Require(call is { ControlResult: false, MovementWord: 0, Teleport: not null } &&
            call.Teleport.Trace.Invocation == call.Invocation && call.Main == saved.MovementCall!.Main &&
            call.MainOrdinal == saved.MovementCall.MainOrdinal && saved.Opacity is not null,
            "Following services lost their actual movement Boolean or independent fade invocation.");
        if (failed) Require(call is { Step: FalloutStandaloneFollowingServicesStep.Failed,
            FailedAt: FalloutStandaloneFollowingServicesStep.TeleportFade,
            Teleport.Trace: { Step: FalloutActorTeleportFadeStep.Failed, FailedAt: FalloutActorTeleportFadeStep.Timer,
                TimerBits: 0x3e800000, SecondsBits: null, Result: null } },
            "Failed setting child erased its current cached timer prefix or invented an opacity return.");
        else Require(call is { Step: FalloutStandaloneFollowingServicesStep.Returned, TeleportSkipped: false,
            Teleport: { AfterFields.FadeBits: 0x3e800000, Trace: { Step: FalloutActorTeleportFadeStep.Returned, OpacityReturned: true } } } &&
            saved.Opacity!.Call is { EffectiveStored: true, Step: FalloutPlayerOpacityStep.Returned, FirstPersonFactory: null },
            "Actual High/opacity children failed to return before the later whole Player boundary.");
        Reject(() => FalloutStandalonePlayerSceneState.Validate(saved with
        { FollowingServices = call with { Invocation = Guid.NewGuid() } }));
        Reject(() => FalloutStandalonePlayerSceneState.Validate(saved with
        { FollowingServices = call with { MovementWord = 1 } }));
        RequireFollowingTeleportReceiptNegatives(saved);
    }

    private static void BindMovementIntegrationWeapon(FalloutStandalonePlayerSceneState scene,
        FalloutActorProcessCommonState common, FalloutFormKey player, bool positiveStatus)
    {
        var source = FalloutPlayerWeaponContinuationSource.Reviewed(FalloutPlayerMovementControlSource.Reviewed(scene.Source.Main));
        common.InitializePlayerWeaponActionConstructor(player, 1, source, scene.Capture().Constructor,
            () => Require(scene.Capture().Clock is null, "Authored weapon constructor replayed in a frame."));
        FalloutPlayerWeaponContinuationCall? latest = null;
        var consumers = new MovementWeaponConsumers(scene, common, player, positiveStatus);
        scene.BindPlayerWeaponContinuation(source, (main, ordinal, invocation) =>
            FalloutPlayerWeaponContinuation.Execute(source, scene.Stack, scene.Process, main, ordinal, invocation,
                consumers, call => latest = call), () => latest);
    }

    private sealed class MovementWeaponConsumers(FalloutStandalonePlayerSceneState scene,
        FalloutActorProcessCommonState common, FalloutFormKey player, bool positiveStatus) : IFalloutPlayerWeaponContinuationConsumers
    {
        public void RequireCurrent(Guid main, long ordinal, Guid invocation) => scene.RequireWeaponContinuationInvocation(main, ordinal, invocation);
        public FalloutProcessWeaponActionRead ReadAction() => common.ReadPlayerWeaponAction(player, 1);
        public bool ReadSuppression() => throw new InvalidOperationException("Constructor-zero action read skipped global.");
        public FalloutProcessSceneQuery ReadWeaponEntry() => throw new InvalidOperationException("Constructor-zero action read skipped weapon entry.");
        public FalloutProcessWeaponActivityRead ReadActivity() => common.ReadPlayerWeaponActivity(player, 1);
        public FalloutProcessActorStatusRead ReadStatus() => common.ReadPlayerFollowingStatus(player, 1, () => positiveStatus ? 1 : 0);
    }

    private static FalloutWorldProjectionPublication BindMovementIntegrationOrientation(
        FalloutStandalonePlayerSceneState scene, FalloutFormKey player)
    {
        var source = FalloutPlayerWorldOrientationSource.Reviewed(FalloutPlayerMovementControlSource.Reviewed(scene.Source.Main));
        var settings = new FalloutNumericIniSettings(
            [new(FalloutPlayerWorldOrientationSource.ConstructorFovName, FalloutIniCollection.Main, 0x42960000),
                new(FalloutPlayerControlConfiguration.WorldFovName, FalloutIniCollection.Main, 0x42960000),
                new(FalloutWorldProjectionInputs.MinimumName, FalloutIniCollection.Main, 0x44d48000),
                new(FalloutWorldProjectionInputs.MaximumName, FalloutIniCollection.Main, 0x46200000),
                new(FalloutWorldProjectionInputs.FractionName, FalloutIniCollection.Main, 0x3f800000),
                new(FalloutWorldProjectionInputs.NearName, FalloutIniCollection.Main, 0x40a00000)], [],
            scene.Stack + ":authored-current-movement-projection:" + scene.Source.Main.EngineSha256);
        var world = Guid.NewGuid(); var target = Guid.NewGuid();
        var root = new FalloutWorldProjectionOwner(source, scene.Stack, world, () => true);
        var consumers = new FalloutWorldProjectionConsumers(root, FalloutWorldProjectionInputs.Read(source, scene.Stack, settings),
            () => new(source.Identity, scene.Stack, world, target, false, null, null, "authored-current-absent-renderer"));
        root.ConstructOrdinary(settings, consumers);
        var publication = new FalloutWorldProjectionPublication(source, scene.Stack, world, () => true);
        publication.PublishReturned(root);
        var reference = new FalloutPlayerReferenceOrientationOwner(source, scene.Stack, scene.Process,
            new(player, new(player.OwnerPlugin, 7), "NPC_", 0, true), publication, scene.ControlFields,
            () => true, scene.RequireMovementOrientationInvocation);
        reference.InitializePlayerFov(settings); scene.BindPlayerReferenceOrientation(reference);
        return publication;
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
