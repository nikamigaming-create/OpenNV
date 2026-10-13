using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static partial class SourceMainScriptCallerContracts
{
    internal static int AuditOwnedPlayerSceneEntry(string dataRoot, bool inputBytesFixture = false)
    {
        RuntimeLiveContentSource.Configure(dataRoot, RuntimeLiveContentSource.Fallout3Game);
        using var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        using var runtime = FalloutAdvancementRuntimeSource.Open(records);
        var mainSource = FalloutMainScriptCallerSource.Read(FalloutImmediateScriptSource.Read(runtime.Receipt));
        using var fixture = new StandaloneFixture(selectedRecords: records, selectedRuntime: runtime.Receipt);
        var scene = fixture.Owner.StandalonePlayerScene;
        using var world = new FalloutReferenceWorld(records);
        world.ConfigureCombatGroups(FalloutCombatGroupDeclaration.ForExecutable(mainSource.EngineSha256), content.StackId);
        world.ConfigureActorPerception(FalloutActorPerceptionDeclaration.ForExecutable(mainSource.EngineSha256), content.StackId);
        world.ConfigureActualProcessRuntime(FalloutActorProcessRuntimeDeclaration.ForExecutable(mainSource.EngineSha256), content.StackId);
        world.ConfigureActorProcesses(FalloutActorProcessDeclaration.ForExecutable(mainSource.EngineSha256), content.StackId);
        var player = records.RuntimeFormKey(0x14);
        var sourceInventory = world.Inventory(player, FalloutPlayerActorValueSource.Read(records).Level);
        world.BindPlayerInventory(sourceInventory.Contents);
        scene.BindActorProcessQueries(world.ReadStandaloneSceneProcessQueries);
        scene.BindProcessSceneUpdate(world.UpdateStandalonePlayerProcessScene);
        scene.BindPostProcessContext(world.ReadStandalonePlayerSceneContext);
        scene.BindTimedActionCondition(world.ReadStandalonePlayerActionCondition);
        scene.BindPostActions(world.UpdateStandalonePlayerPostActions, world.UpdateStandalonePlayerDetectionAction);
        scene.BindActorDistances(fixture.Owner.StandaloneInterface, world.ReadStandaloneActorDistanceLimit,
            world.ReadStandaloneActorDistance, world.ReadStandaloneActorDistanceTimerSetting);
        scene.BindIdleAnimation(read => world.UpdateStandalonePlayerIdleAnimation(scene, read), () => world.LastStandalonePlayerIdleAnimation);
        scene.BindMovementFlags(world.ReadAndClearStandalonePlayerMovementFlags);
        var input = inputBytesFixture ? new FalloutDirectInputState(content.StackId, 123, new(_ => null)) : null;
        if (input is not null) scene.BindInputManager(input);
        var hudSource = FalloutExecutableStringTable.ReadPlayerReferenceHud(content.FalloutExecutablePath, mainSource);
        var hud = new FalloutPlayerReferenceHudState(hudSource, scene.Stack, scene.Process, FalloutUiComponentStore.Open(records, content));
        Exception? failure = null;
        try
        {
            fixture.BindActualScenePrefix(hud);
            using var binding = fixture.BindProduction();
            try { fixture.Owner.ExecuteMainScriptCaller(1, .02f).GetAwaiter().GetResult(); }
            catch (Exception error) { failure = error; }
            var state = scene.Capture();
            var blocker = failure?.Message ?? scene.SaveBlocker;
            Console.WriteLine((blocker is null ? "OPENNV_OWNED_PLAYER_SCENE_ENTRY_PASS " : "OPENNV_OWNED_PLAYER_SCENE_ENTRY_BLOCKED ") +
                JsonSerializer.Serialize(new
                {
                    schema = "opennv-offline-player-scene-entry/v10", game = content.Game,
                    source = mainSource.Identity, stack = content.StackId,
                    sourceVirtual = nameof(FalloutStandalonePlayerSceneState.ExecuteInitialVirtualPrefix),
                    blocker, phase = state.Prelude?.Step.ToString(), failedAt = state.Prelude?.FailedAt?.ToString(),
                    liveInputBoundary = !inputBytesFixture &&
                        blocker == "source-Player-scene-current-input-manager-product-factory-unbound" &&
                        state.Prelude is { FailedAt: FalloutStandaloneScenePreludeStep.InputManagerQuery } &&
                        state.ThreadContextEntry is { ScopeEntered: true, InitializerReturned: true } &&
                        state.InputManagerQuery is null,
                    bracket = state.SceneBracket, sharedChildRelease = "actual-CSharp-pending-owner",
                    productionMainCaller = true, productionPlayerCellCaller = true,
                    referenceQueryReturned = state.ReferenceQuery?.Returned,
                    hudReturned = state.ReferenceHudCall?.Step == FalloutPlayerReferenceHudStep.Returned,
                    hudBranch = hud.Branch, hudTemplates = new[] { hud.Bracket, hud.Meter, hud.Name },
                    hudColorBits = state.ReferenceHudCall?.ColorBits,
                    actorScalarStores = state.ActorScalarReset?.Stores,
                    actorScalarResetReturned = state.ActorScalarReset?.Returned,
                    threadInitializerReturned = state.ThreadContextEntry?.InitializerReturned,
                    threadBeforeKind = state.ThreadContextEntry?.BeforeKind,
                    threadCurrentKind = state.ThreadContextEntry?.CurrentKind,
                    inputManagerReturned = state.InputManagerQuery?.Returned,
                    actualActorProcess = state.ActorProcessQuery,
                    actualSceneScalars = state.SceneScalarCall,
                    actualSceneQueue = state.SceneQueueCall,
                    queuedReferences = state.QueuedReferences,
                    actualProcessSceneUpdate = state.ProcessSceneUpdate,
                    postProcessContext = state.PostProcessContext,
                    timedActions = state.TimedActions,
                    timedActionsCall = state.TimedActionsCall,
                    postActionFields = state.PostActionFields,
                    postActionsCall = state.PostActionsCall,
                    actorDistanceTimer = state.ActorDistanceTimer,
                    actorDistanceInitialization = FalloutPlayerActorDistanceTimer.Initialization,
                    actorDistanceCall = state.ActorDistanceCall,
                    idleAnimationCall = state.IdleAnimationCall,
                    movementCall = state.MovementCall,
                    moverFields = state.MoverFields,
                    originalScopeExit = "VERIFIED-prior-kind-restore;not-reached-by-blocked-body",
                    sourceConstructorFields = "implemented-explicit-writers;actor-distance-allocation-difference-reported",
                    fixtureBoundaries = new[] { "OS Tab predicate", "native interface/input/fade and rate consumers", "native Player getter/factory", "native HUD projection" },
                    actualProductInputManager = inputBytesFixture ? "AUTHORED-BYTE-FIXTURE-no-HWND-or-device-factory" :
                        "UNBOUND-headless-requires-living-product-window-and-devices",
                    gameLaunched = false, framesRecorded = false, ordinaryGameplay = "UNEXECUTED",
                    saveQuitColdContinue = "UNEXECUTED",
                }));
            return blocker is null ? 0 : 1;
        }
        finally { hud.Retire(); input?.Retire(); }
    }
}
