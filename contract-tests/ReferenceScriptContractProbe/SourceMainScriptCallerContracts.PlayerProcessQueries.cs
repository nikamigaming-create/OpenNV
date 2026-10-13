using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static partial class SourceMainScriptCallerContracts
{
    internal static void RunPlayerProcessQueryContracts()
    {
        RunPlayerSceneScalarArithmetic();
        RunPlayerSceneReferenceQueue();
        RunPlayerTimedActions();
        RunPlayerPostActionArithmetic();
        RunInterfaceActorDistances();
        RunPlayerActorDistanceTimer();
        RunPlayerIdleAnimation();
        RunPlayerMovementFlags();
        RunPlayerPhysicalAdmission();
        RunPlayerMovementControl();
        RunPlayerMovementIntegration();
        var main = FalloutMainScriptCallerSource.Read(new(FalloutSourceMainFamily.Fallout3, new('1', 64),
            FalloutImmediateScriptSource.ContractForEngine(FalloutSourceMainFamily.Fallout3)));
        var source = FalloutActorProcessRuntimeDeclaration.ForExecutable(main.EngineSha256);
        var stack = new string('2', 64);
        var player = new FalloutFormKey("ProcessFixture.esm", 0x14);
        var npc = new FalloutFormKey("ProcessFixture.esm", 0x101);
        FalloutCombatActorIdentity Identity(FalloutFormKey actor) => new(actor, new("ProcessFixture.esm", 7),
            actor == player ? "ENGINE_PLAYER" : "ACHR", 0, main.EngineSha256, "NPC_", 0, new('4', 64), actor == player);
        FalloutProcessGameplayState Gameplay(FalloutFormKey actor) => throw new InvalidOperationException("A pure getter called gameplay copy.");
        using var common = new FalloutActorProcessCommonState(source, stack, Identity, Gameplay, _ =>
            throw new InvalidOperationException("A pure getter called the native body factory."));
        common.Construct(player, 1, FalloutDetectionProcessLevel.High);
        common.Construct(npc, 1, FalloutDetectionProcessLevel.Low);
        var before = common.Capture();
        var query = common.ReadSceneQueries(player, 1);
        var low = common.ReadSceneQueries(npc, 1);
        Require(query.Level == FalloutDetectionProcessLevel.High && query.WeaponEntry is null && query.AnimationContext is null &&
            low.Level == FalloutDetectionProcessLevel.Low && low.WeaponEntry is null && low.AnimationContext is null &&
            common.Capture().Sequence == before.Sequence, "Actual source nullable process getters mutated or lost their class.");
        Reject(() => common.ReadSceneQueries(player, 2));
        Reject(() => FalloutActorProcessCommonState.Validate(before with
        { Current = before.Current.Select(entry => entry.Source.EnginePlayer ? entry with { SceneFields = null } : entry).ToArray() }));
        var serialized = JsonSerializer.Deserialize<FalloutProcessCommonSnapshot>(JsonSerializer.Serialize(before))!;
        using var cold = new FalloutActorProcessCommonState(source, stack, Identity, Gameplay, _ => null, serialized);
        var coldQuery = cold.ReadSceneQueries(player, 1);
        Require(coldQuery.Process != query.Process && coldQuery.GameplayOwnership == query.GameplayOwnership &&
            coldQuery.WeaponEntry is null && coldQuery.AnimationContext is null,
            "Cold process query reused an old runtime or fabricated a replacement equipment entry.");

        var input = new FalloutDirectInputState(stack, 123, new(_ => null));
        var scene = new FalloutStandalonePlayerSceneState(FalloutStandalonePlayerSceneSource.Read(main), stack, Guid.NewGuid());
        var hud = new FalloutPlayerReferenceHudState(FalloutPlayerReferenceHudSource.Reviewed(main), stack, scene.Process,
            AuthoredReferenceHudComponents());
        scene.BindInputManager(input);
        var reads = 0;
        scene.BindActorProcessQueries(() => { ++reads; return common.ReadSceneQueries(player, 1); });
        scene.BindProcessSceneUpdate(() => common.UpdatePlayerScene(player, 1));
        var contextReads = 0;
        scene.BindPostProcessContext(() => { ++contextReads; return common.ReadSceneContext(player, 1); });
        var actionReads = 0;
        scene.BindTimedActionCondition(() => { ++actionReads; return common.ReadPlayerActionCondition(player, 1); });
        scene.BindPostActions(read => common.UpdatePostActions(player, 1, read),
            read => common.UpdateDetectionAction(player, 1, read, () => 1.2f));
        var manager = new FalloutStandaloneInterfaceState(FalloutStandaloneInterfaceSource.Read(main), stack, scene.Process);
        var managerNative = new AuthoredInterfaceObject();
        manager.PublishManager(managerNative.Publication(manager, FalloutStandaloneInterfaceObjectRole.PipBoyManager, stack), managerNative.ReadLiving);
        manager.IncludeActorDistance(player, 20, _ => 2); manager.IncludeActorDistance(npc, 20, _ => 1);
        var distanceQueries = 0;
        scene.BindActorDistances(manager, () => 20, actor => { ++distanceQueries; return actor == player ? 0 : 9; }, () => 3);
        var idleConsumers = new AuthoredIdleConsumers { ModelOverride = () => scene.ReadSelectedIdleModel(() => null) };
        scene.BindIdleAnimation(_ => common.UpdateIdleAnimation(player, 1, idleConsumers), () => common.LastIdleAnimationUpdate);
        scene.BindMovementFlags(() => common.ReadAndClearPlayerMovementFlags(player, 1));
        Reject(() => scene.BindActorProcessQueries(() => query));
        var invocation = Guid.NewGuid(); scene.StoreSceneClock(invocation, 1, 0x3c800000); scene.EnterSceneVirtual(invocation, 1);
        Reject(() => scene.ExecuteInitialVirtualPrefix(invocation, 1, () => { }, hud,
            () => throw new InvalidOperationException("The zero-time arm queried a scaled timer.")));
        var saved = scene.Capture();
        Require(saved.MovementCall is { ProcessFlags: { Stores: 1, Flags: 0, AfterFlags: 0, Returned: true },
            MoverReads.Count: 2, InitialLowWord: 0, Step: "movement-current-actor-field-child",
            Failure: "source-Player-movement-current-actor-field-child-unbound" } &&
            saved.MoverFields == FalloutPlayerMoverFields.Constructed,
            "Player movement did not retain its source flag clear and two independent mover reads before the unavailable control owner.");
        Reject(() => FalloutStandalonePlayerSceneState.Validate(saved with { MovementCall = null }));
        Reject(() => FalloutStandalonePlayerSceneState.Validate(saved with
        { MovementCall = saved.MovementCall! with { MoverReads = [0] } }));
        Reject(() => FalloutStandalonePlayerSceneState.Validate(saved with
        { MoverFields = saved.MoverFields! with { Flags = 1 } }));
        Reject(() => FalloutStandalonePlayerSceneState.Validate(saved with { ActorMovementFlag = 1 }));
        Reject(() => FalloutStandalonePlayerSceneState.Validate(saved with
        { MovementCall = saved.MovementCall! with { ActorFlag = null } }));
        Require(reads == 2 && contextReads == 4 && actionReads == 1 && saved.ActorProcessQuery is { EntryCalls: 1, AnimationReturned: true } receipt &&
            receipt.Result == query && saved.Prelude?.FailedAt == FalloutStandaloneScenePreludeStep.PlayerSceneContinuation &&
            saved.SceneScalarCall is { Stores: 3, Returned: true, ScaledTimerBits: null } &&
            saved.SceneQueueCall is { Returned: true, Failure: null, Events.Count: 0 } && saved.QueuedReferences is { Count: 0 } &&
            saved.ProcessSceneUpdate?.Result is { Stores: 1, BeforeMask: 0, AfterMask: 0, Returned: true } &&
            saved.PostProcessContext is { Returned: true, Queries.Count: 2 } &&
            saved.TimedActions is { Allocated: false, Entries.Count: 0 } &&
            saved.TimedActionsCall is { Returned: true, Operation: "remove-first-nine", Query: { Condition: 0, SecondCondition: null }, ScaledTimerBits: null, TimerStores: 0 } &&
            saved.PostActionFields == FalloutPlayerPostActionFields.Constructed with { ContextFlag = 0 } &&
            saved.PostActionsCall is { Returned: true, RemovedEight: true, FlagStores: 1, TimerStores: 2, ContextQueries.Count: 2,
                ProcessUpdate: { Stores: 2, ScaledTimerBits: null, AfterScalars: { ValueBits: 0, TimerBits: 0x40400000, Updates: 1 } },
                DetectionUpdate: { Stores: 2, ScaledTimerBits: null, SettingBits: 0x3f99999a, AfterScalars: { Updates: 1 } } } &&
            saved.SceneScalars == FalloutPlayerSceneScalarFields.Constructed,
            "Player did not return its independent current process getters before the next missing child.");
        Require(saved.ActorDistanceTimer is { Bits: 0x40400000, Stores: 1 } &&
            saved.ActorDistanceCall is { Returned: true, ArgumentBits: null, SettingBits: 0x40400000,
                Sort: { Returned: true, DistanceQueries: 2, After.Count: 2 } } && distanceQueries == 2 &&
            manager.Capture().ActorDistances!.Slots[0] == player,
            "Player did not sort a real nonempty shared interface list before resetting its independent timer.");
        Require(saved.IdleAnimationCall is { Returned: true, Result.Trace.Step: "null-model-return" } &&
            saved.IdleAnimationCall.ModelCacheReads is [{ Matched: false, CachedReference: null, Model: null }] &&
            idleConsumers.Calls.SequenceEqual(["model"]), "Actual High idle call did not return its legal null-model arm and independent thread cache.");
        Reject(() => FalloutStandalonePlayerSceneState.Validate(saved with { IdleAnimationCall = null }));
        Reject(() => FalloutStandalonePlayerSceneState.Validate(saved with
        { IdleAnimationCall = saved.IdleAnimationCall! with { ModelCacheReads = [] } }));
        Reject(() => FalloutStandalonePlayerSceneState.Validate(saved with
        { IdleAnimationCall = saved.IdleAnimationCall! with { ModelCacheReads = [saved.IdleAnimationCall.ModelCacheReads![0] with { Matched = true }] } }));
        Reject(() => FalloutStandalonePlayerSceneState.Validate(saved with { ActorDistanceTimer = null }));
        Reject(() => FalloutStandalonePlayerSceneState.Validate(saved with { ActorDistanceCall = null }));
        Reject(() => FalloutStandalonePlayerSceneState.Validate(saved with
        { ActorDistanceCall = saved.ActorDistanceCall! with { SettingBits = 0 } }));
        Reject(() => FalloutStandalonePlayerSceneState.Validate(saved with { ActorProcessQuery = null }));
        Reject(() => FalloutStandalonePlayerSceneState.Validate(saved with { SceneScalarCall = null }));
        Reject(() => FalloutStandalonePlayerSceneState.Validate(saved with { SceneScalars = null }));
        Reject(() => FalloutStandalonePlayerSceneState.Validate(saved with { QueuedReferences = null }));
        Reject(() => FalloutStandalonePlayerSceneState.Validate(saved with { SceneQueueCall = null }));
        Reject(() => FalloutStandalonePlayerSceneState.Validate(saved with { ProcessSceneUpdate = null }));
        Reject(() => FalloutStandalonePlayerSceneState.Validate(saved with { PostProcessContext = null }));
        Reject(() => FalloutStandalonePlayerSceneState.Validate(saved with { TimedActionsCall = null }));
        Reject(() => FalloutStandalonePlayerSceneState.Validate(saved with { TimedActions = null }));
        Reject(() => FalloutStandalonePlayerSceneState.Validate(saved with { PostActionsCall = null }));
        Reject(() => FalloutStandalonePlayerSceneState.Validate(saved with { PostActionFields = null }));
        Reject(() => FalloutStandalonePlayerSceneState.Validate(saved with
        { PostActionsCall = saved.PostActionsCall! with { FlagStores = 0 } }));
        Reject(() => FalloutStandalonePlayerSceneState.Validate(saved with
        { PostActionsCall = saved.PostActionsCall! with { ProcessUpdate = saved.PostActionsCall.ProcessUpdate! with { Stores = 0 } } }));
        Reject(() => FalloutStandalonePlayerSceneState.Validate(saved with
        { PostActionsCall = saved.PostActionsCall! with { SceneArgumentBits = 0 } }));
        Reject(() => FalloutStandalonePlayerSceneState.Validate(saved with
        { TimedActionsCall = saved.TimedActionsCall! with { ScaledTimerBits = 0 } }));
        Reject(() => FalloutStandalonePlayerSceneState.Validate(saved with
        { TimedActionsCall = saved.TimedActionsCall! with { Query = saved.TimedActionsCall.Query! with { SecondCondition = 0 } } }));
        Reject(() => FalloutStandalonePlayerSceneState.Validate(saved with
        { PostProcessContext = saved.PostProcessContext! with { Queries = saved.PostProcessContext.Queries.Take(1).ToArray() } }));
        Reject(() => FalloutStandalonePlayerSceneState.Validate(saved with
        { ProcessSceneUpdate = saved.ProcessSceneUpdate! with { Result = saved.ProcessSceneUpdate.Result! with { Stores = 0 } } }));
        Reject(() => FalloutStandalonePlayerSceneState.Validate(saved with
        { SceneScalarCall = saved.SceneScalarCall! with { Stores = 2 } }));
        Reject(() => FalloutStandalonePlayerSceneState.Validate(saved with
        { SceneScalars = saved.SceneScalars! with { ActorRemainingBits = 0x3f800000 } }));
        Reject(() => FalloutStandalonePlayerSceneState.RequirePlayable(saved));
        var roundTrip = JsonSerializer.Deserialize<FalloutStandalonePlayerSceneSnapshot>(JsonSerializer.Serialize(saved))!;
        var restored = new FalloutStandalonePlayerSceneState(scene.Source, stack, Guid.NewGuid(), roundTrip);
        Require(restored.Capture().ActorProcessQuery == saved.ActorProcessQuery &&
            restored.Capture().SceneScalarCall == saved.SceneScalarCall && reads == 2 && contextReads == 4 && actionReads == 1 &&
            restored.Capture().PostActionsCall == roundTrip.PostActionsCall &&
            restored.Capture().ActorDistanceTimer == saved.ActorDistanceTimer && distanceQueries == 2 &&
            restored.Capture().ActorDistanceCall == roundTrip.ActorDistanceCall &&
            restored.Capture().IdleAnimationCall == roundTrip.IdleAnimationCall && idleConsumers.Calls.Count == 1 &&
            restored.Capture().MovementCall == roundTrip.MovementCall &&
            restored.Capture().MoverFields == saved.MoverFields &&
            restored.Capture().TimedActions!.Entries.SequenceEqual(saved.TimedActions!.Entries),
            "Cold scene history replayed its process getter or rebound the old world.");
        restored.Retire(); scene.Retire(); hud.Retire(); input.Retire(); manager.Retire(); managerNative.Retire();
        var cleared = common.Capture();
        Require(cleared.Sequence == before.Sequence + 4 && common.ReadSceneQueries(player, 1).Changed > query.Changed &&
            cleared.Current.Single(entry => entry.Source.EnginePlayer).SceneFields!.MovementFlags is { Flags: 0, Stores: 1 },
            "The actual High zero-mask and movement virtuals omitted their independent clear stores.");
        var postRead = 0;
        var subtract = common.UpdatePostActions(player, 1, () => { ++postRead; return 4; });
        Require(postRead == 1 && subtract.Stores == 1 && subtract.AfterScalars is { TimerBits: 0xbf800000, ValueBits: 0, Updates: 2 },
            "Current post-action process clamped its actual timer overshoot or read a different provider.");
        var refresh = common.UpdatePostActions(player, 1, () => throw new InvalidOperationException("An expired post-action timer polled Main."));
        Require(refresh.AfterScalars is { TimerBits: 0x40400000, Updates: 3 } && refresh.Stores == 2 && refresh.ScaledTimerBits is null,
            "The following expired post-action timer omitted the real null-context publication.");
        var currentSerialized = JsonSerializer.Deserialize<FalloutProcessCommonSnapshot>(JsonSerializer.Serialize(common.Capture()))!;
        using var currentCold = new FalloutActorProcessCommonState(source, stack, Identity, Gameplay, _ => null, currentSerialized);
        var restoredCurrent = currentCold.Capture().Current.Single(item => item.Source.EnginePlayer);
        Require(restoredCurrent.SceneFields == currentSerialized.Current.Single(item => item.Source.EnginePlayer).SceneFields && postRead == 1,
            "Cold common restoration replayed timer stores or lost dynamic process cells.");
        Reject(() => common.UpdatePostActions(player, 2, () => 1));
        var lowPost = common.UpdatePostActions(npc, 1, () => throw new InvalidOperationException("Low post-action virtual polled a timer."));
        Require(lowPost is { Stores: 0, BeforeScalars: null, AfterScalars: null, ScaledTimerBits: null },
            "Low post-action virtual manufactured High state.");
        Reject(() => common.UpdatePlayerScene(npc, 1));
        Reject(() => FalloutActorProcessCommonState.Validate(cleared with
        { Current = cleared.Current.Select(item => item.Source.Reference == player ? item with
            { SceneFields = item.SceneFields! with { SceneUpdateMask = 1 } } : item).ToArray() }));
        RunFailedPlayerProcessQuery(main, stack, query);
        common.RetainUnownedMutation(player, "actual-equipped-entry-writer-unowned");
        Reject(() => common.ReadSceneQueries(player, 1));
        using var reentryCommon = new FalloutActorProcessCommonState(source, stack, Identity, Gameplay, _ => null);
        reentryCommon.Construct(player, 1, FalloutDetectionProcessLevel.High);
        var reentryIdle = new AuthoredIdleConsumers
        {
            NullModel = true,
            ModelCallback = () => { try { _ = reentryCommon.Capture(); } catch (InvalidOperationException) { } }
        };
        Reject(() => reentryCommon.UpdateIdleAnimation(player, 1, reentryIdle));
        Require(reentryCommon.LastIdleAnimationUpdate is { Trace: { Returned: false, Failure: not null } } &&
            reentryCommon.SaveBlocker is not null, "A swallowed source idle reentry lost its real failed child receipt.");
        FalloutActorProcessCommonState.Validate(reentryCommon.Capture());
        RunPlayerIdleModelSelection(main, stack, source, player, Identity, Gameplay);
        Console.WriteLine("OPENNV_PLAYER_PROCESS_QUERY_PASS actualCommonOwner=true independentGetters=true classAndEpoch=true " +
            "getterNoMutation=true coldNewProcess=true coldNoReplay=true sourceReferenceQueue=true timedActions=true postActionTimers=true nonnullWriters=UNOWNED nativeInput=FIXTURE wholePlayerVirtual=UNRETURNED");
    }

    private static void RunPlayerIdleModelSelection(FalloutMainScriptCallerSource main, string stack,
        FalloutActorProcessRuntimeDeclaration source, FalloutFormKey player,
        Func<FalloutFormKey, FalloutCombatActorIdentity> identity, Func<FalloutFormKey, FalloutProcessGameplayState> gameplay)
    {
        foreach (var retireDuringSecondRead in new[] { false, true })
        {
            using var common = new FalloutActorProcessCommonState(source, stack, identity, gameplay,
                _ => throw new InvalidOperationException("Selected first-person model borrowed the common third-person body."));
            common.Construct(player, 1, FalloutDetectionProcessLevel.High);
            var scene = new FalloutStandalonePlayerSceneState(FalloutStandalonePlayerSceneSource.Read(main), stack, Guid.NewGuid());
            var living = true; var factory = Guid.NewGuid();
            var first = scene.PublishFirstPerson(new(factory, scene.Process, scene.Source.Identity, stack, 987,
                "meshes/characters/_1stperson/skeleton.nif", new('5', 64), "authored-first-person-model-fixture"), () => living);
            var input = new FalloutDirectInputState(stack, 123, new(_ => null));
            var hud = new FalloutPlayerReferenceHudState(FalloutPlayerReferenceHudSource.Reviewed(main), stack, scene.Process,
                AuthoredReferenceHudComponents());
            var manager = new FalloutStandaloneInterfaceState(FalloutStandaloneInterfaceSource.Read(main), stack, scene.Process);
            var native = new AuthoredInterfaceObject();
            manager.PublishManager(native.Publication(manager, FalloutStandaloneInterfaceObjectRole.PipBoyManager, stack), native.ReadLiving);
            scene.BindInputManager(input); scene.BindActorProcessQueries(() => common.ReadSceneQueries(player, 1));
            scene.BindProcessSceneUpdate(() => common.UpdatePlayerScene(player, 1));
            scene.BindPostProcessContext(() => common.ReadSceneContext(player, 1));
            scene.BindTimedActionCondition(() => common.ReadPlayerActionCondition(player, 1));
            scene.BindPostActions(read => common.UpdatePostActions(player, 1, read),
                read => common.UpdateDetectionAction(player, 1, read, () => 1.2f));
            scene.BindActorDistances(manager, () => 20, _ => throw new InvalidOperationException("Empty list queried actor distance."), () => 3);
            var reads = 0;
            var initialProcess = common.ReadSceneQueries(player, 1);
            var consumers = new AuthoredIdleConsumers
            {
                PackageRead = new(Guid.NewGuid(), initialProcess.Process, initialProcess.GameplayOwnership,
                    player, stack, 0, null),
                ModelOverride = () =>
                {
                    if (++reads == 2 && retireDuringSecondRead) living = false;
                    return scene.ReadSelectedIdleModel(() => throw new InvalidOperationException("First-person getter read a fallback body."));
                }
            };
            scene.BindIdleAnimation(_ => common.UpdateIdleAnimation(player, 1, consumers), () => common.LastIdleAnimationUpdate);
            var invocation = Guid.NewGuid(); scene.StoreSceneClock(invocation, 1, 0x3c800000); scene.EnterSceneVirtual(invocation, 1);
            Reject(() => scene.ExecuteInitialVirtualPrefix(invocation, 1, () => { }, hud, () => .5f));
            var saved = scene.Capture();
            Require(reads == 2 && saved.IdleAnimationCall is { Result: { } update } &&
                update.Trace.Models.Count == (retireDuringSecondRead ? 1 : 2) &&
                update.Trace.Models.All(node => node == new FalloutSourceSceneNode(scene.Process, factory, first.Publication.NativeRoot)) &&
                saved.IdleAnimationCall.Returned != retireDuringSecondRead &&
                (retireDuringSecondRead ? update.AfterFields!.Stores == 0 && update.Trace.Failure is not null :
                    update.AfterFields is { WaitBits: 0x3f000000, Stores: 1 } && update.Trace.Returned),
                "Player idle model borrowed another body or reused a first-person field after its real lifetime ended.");
            var nodes = saved.IdleAnimationCall!.Result!.Trace.Models.ToArray();
            nodes[0] = nodes[0]! with { Factory = Guid.NewGuid() };
            Reject(() => FalloutStandalonePlayerSceneState.Validate(saved with
            { IdleAnimationCall = saved.IdleAnimationCall with { Result = saved.IdleAnimationCall.Result with
                { Trace = saved.IdleAnimationCall.Result.Trace with { Models = nodes } } } }));
            var serialized = JsonSerializer.Deserialize<FalloutStandalonePlayerSceneSnapshot>(JsonSerializer.Serialize(saved))!;
            var call = saved.IdleAnimationCall!;
            Reject(() => FalloutStandalonePlayerSceneState.Validate(saved with
            {
                IdleAnimationCall = call with { Result = call.Result! with
                { Trace = call.Result.Trace with { SelectedPackageRead = null } } }
            }));
            var cold = new FalloutStandalonePlayerSceneState(scene.Source, stack, Guid.NewGuid(), serialized);
            Require(cold.Capture().IdleAnimationCall == serialized.IdleAnimationCall && reads == 2,
                "Cold model history replayed an old native first-person getter.");
            cold.Retire(); scene.ReleaseFirstPerson(first); scene.Retire(); input.Retire(); hud.Retire(); manager.Retire(); native.Retire();
        }
    }

    private static void RunPlayerSceneScalarArithmetic()
    {
        var before = new FalloutPlayerSceneScalarFields(0x3fa00000, 77, 55, 3, 4);
        var positive = before.Update(.5f);
        Require(positive == new FalloutPlayerSceneScalarFields(0x3f400000, 77, 55, 0, 0),
            "Player scalar update lost separate scratch stores or retained payload/cache values.");
        var overshot = (before with { ActorRemainingBits = 0x3e800000 }).Update(.5f);
        Require(overshot.ActorRemainingBits == 0xbe800000 && overshot.ActorWord == 77,
            "Player scalar subtraction clamped an overshoot or cleared the independent word too early.");
        Require(overshot.Update(.5f) == overshot with { ActorWord = 0 },
            "The following nonpositive test changed remaining time or retained an expired word.");
        foreach (var bits in new uint[] { 0, 0x80000000, 0xbf800000, 0x7fc00012 })
        {
            var unorderedOrNonpositive = (before with { ActorRemainingBits = bits }).Update(99);
            Require(unorderedOrNonpositive.ActorRemainingBits == bits && unorderedOrNonpositive.ActorWord == 0 &&
                unorderedOrNonpositive.PlayerCachedWord == 55,
                "A nonpositive/unordered scalar arm rewrote its retained bits or independent cached word.");
        }
    }

    private static void RunFailedPlayerProcessQuery(FalloutMainScriptCallerSource main, string stack, FalloutProcessSceneQuery query)
    {
        var input = new FalloutDirectInputState(stack, 123, new(_ => null));
        var scene = new FalloutStandalonePlayerSceneState(FalloutStandalonePlayerSceneSource.Read(main), stack, Guid.NewGuid());
        var hud = new FalloutPlayerReferenceHudState(FalloutPlayerReferenceHudSource.Reviewed(main), stack, scene.Process,
            AuthoredReferenceHudComponents());
        scene.BindInputManager(input);
        var reads = 0;
        scene.BindActorProcessQueries(() => ++reads == 1 ? query :
            throw new NotSupportedException("actual-animation-getter-failure"));
        var invocation = Guid.NewGuid(); scene.StoreSceneClock(invocation, 1, 0x3c800000); scene.EnterSceneVirtual(invocation, 1);
        Reject(() => scene.ExecuteInitialVirtualPrefix(invocation, 1, () => { }, hud));
        var failed = scene.Capture();
        Require(failed.Prelude?.FailedAt == FalloutStandaloneScenePreludeStep.ActorProcessQuery &&
            failed.ActorProcessQuery is { EntryCalls: 1, AnimationReturned: false, Failure: "actual-animation-getter-failure" } partial &&
            partial.Result == query && reads == 2,
            "Failed animation query discarded the returned weapon getter or fabricated its own return.");
        var serialized = JsonSerializer.Deserialize<FalloutStandalonePlayerSceneSnapshot>(JsonSerializer.Serialize(failed))!;
        var cold = new FalloutStandalonePlayerSceneState(scene.Source, stack, Guid.NewGuid(), serialized);
        Require(cold.Capture().ActorProcessQuery == failed.ActorProcessQuery && reads == 2,
            "Cold failure restoration replayed a process child.");
        Reject(() => FalloutStandalonePlayerSceneState.Validate(failed with
        { ActorProcessQuery = failed.ActorProcessQuery! with { AnimationReturned = true, Failure = null } }));
        cold.Retire(); scene.Retire(); hud.Retire(); input.Retire();
    }
}
