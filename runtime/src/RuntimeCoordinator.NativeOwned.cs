using System.Text.Json;
using System.Diagnostics;
using Godot;
using OpenNV.Runtime.Campaigns.NewVegas.Opening;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.Presentation.Rendering;
using OpenNV.Runtime.World.Cells;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.Diagnostics.Parity;
using OpenNV.Runtime.Presentation.Ui;
using OpenNV.Runtime.World;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    private const int NativeMenuCanvasLayer = 100;
    private FalloutPluginStack? _nativePluginStack;
    private FalloutQuestState? _nativeQuestState;
    private FalloutReferenceWorld? _nativeReferences;
    private FalloutScriptStorage? _nativeScriptStorage;
    private FalloutUiComponentStore? _nativeUi;
    private RuntimeNativeReferenceEvents? _nativeReferenceEvents;
    private RuntimeNativeQuestScripts? _nativeQuestScripts;
    private FalloutGlobalState? _nativeGlobals;
    private FalloutGameTime? _nativeGameTime;
    private FalloutSkyLightingState? _nativeSkyLighting;
    private RuntimeNativeGameTime? _nativeGameTimeAdapter;
    private string? _nativeGameTimeUnbound;
    private readonly FalloutPlayerInventory _nativeInventory = new();
    private FalloutCellScene? _nativeActiveCell;
    private Node3D? _nativeCurrentCellRoot;
    private readonly Dictionary<string, RuntimeNativeNifPrototype> _nativeNifPrototypes =
        new(StringComparer.OrdinalIgnoreCase);
    private Node3D? _nativePrewarmedInitialCellRoot;
    private RuntimeNativePlayer? _nativePlayer;
    private FalloutOpeningControlGraph? _nativeOpeningControls;
    private FalloutOpeningStageTransitionGraph? _nativeOpeningTransitions;
    private FalloutNativeRaceSexContract? _nativeRaceSexContract;
    private FalloutNativeCampaignRestore? _nativeOpeningRestore;
    private RuntimeNativeOpeningStageDriver? _nativeOpeningStageDriver;
    private bool _nativeContinueOpening;
    private readonly FalloutImageSpaceState _nativeImageSpaceState = new();
    private World.RuntimeNativeImageSpaceClock? _nativeImageSpaceClock;
    private readonly Dictionary<string, string> _nativeActorDivergences = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _nativeReferenceDivergences = new(StringComparer.Ordinal);

    private object CaptureNativeDriveState(bool detailed = true)
    {
        static float[] Vector(Vector3 value) => [value.X, value.Y, value.Z];
        static object? MovieField(Node movie, string key) => movie.HasMeta(key) ? movie.GetMeta(key).Obj : null;
        var camera = GetViewport().GetCamera3D();
        var cellChildren = _nativeCurrentCellRoot?.GetChildren();
        var cellNodes = cellChildren?.ToArray() ?? [];
        return new
        {
            paused = GetTree().Paused,
            imageSpaceClock = _nativeImageSpaceClock?.State,
            botPlanning = NativeBotPlanningState,
            manualSave = new { receipt = _nativeManualSaves.Receipt, history = detailed ? _nativeManualSaves.History : null },
            combatWheel = _nativeCombatWheel?.State,
            xr = _nativeXr?.State,
            detail = detailed ? "complete-runtime-snapshot" : "live-summary;request-state-for-reference-controller-and-quest-details",
            reviewScope = !detailed || _nativeActiveCell is null ? null : new
            {
                sourceCompatibilityId = RuntimeLiveContentSource.Current!.SaveCompatibilityId,
                runtimeBuild = typeof(RuntimeCoordinator).Assembly.ManifestModule.ModuleVersionId,
                capturedUtc = DateTime.UtcNow,
                references = _nativeActiveCell.References.Select(reference => reference.FormKey.ToString()).ToArray(),
            },
            referenceEvents = _nativeReferenceEvents?.State,
            rigidBodies = detailed ? CaptureNativeRigidBodies() : null,
            nativePluginRetirementFailure = _nativePluginRetirementFailure,
            modelConstraints = detailed ? _nativeReferencePresentation?.Nodes.SelectMany(reference =>
                OpenNV.Runtime.SceneGraph.NodeTraversal.Descendants<RuntimeNifHingeJoint>(reference.Value)
                    .Select(joint => new { reference = reference.Key.ToString(), joint = joint.State })).ToArray() : null,
            references = _nativeReferences is null ? null : new
            {
                _nativeReferences.InstanceCount,
                _nativeReferences.ResidentCellCount,
                _nativeReferences.ScriptDefinitionCount,
                pendingPackageEvents = _nativeReferences.PendingPackageEventCount,
                pendingProcedureCaptures = _nativeReferences.PendingProcedureCaptureCount,
                pendingProcedureCaptureOwners = _nativeReferences.PendingProcedureCaptures,
                pendingAnimationSoundCaptures = _nativeReferences.PendingAnimationSoundCaptureCount,
                pendingAnimationSoundCaptureOwners = _nativeReferences.PendingAnimationSoundCaptures,
                detection = _nativeReferences.DetectionState,
                stoppedScriptFrames = _nativeReferences.StoppedScriptFrames,
                stoppedPackageBindings = _nativeReferences.StoppedPackageBindingCount,
                talkingActivatorBindings = _nativeReferences.TalkingActivatorBindings,
                unloadedActorPackages = _nativeReferences.UnloadedPackages?.State,
                state = detailed && !_nativeReferences.PlayerMoves.Pending && _nativeReferences.PendingPackageEventCount == 0 &&
                    _nativeReferences.PendingProcedureCaptureCount == 0 && _nativeReferences.PendingAnimationSoundCaptureCount == 0
                    ? _nativeReferences.Capture() : null,
                actorOverrides = detailed && !_nativeReferences.PlayerMoves.Pending ? _nativeReferences.CaptureActorOverrides() : null
            },
            ui = _nativeUi?.State,
            terminal = _nativeTerminalMenu?.Observation,
            terminalSessions = _nativeOpeningStageDriver?.TerminalState,
            bootstrap = _nativeBootstrap?.State,
            loading = _nativeLoadingScreens?.State,
            loadingFeedback = _loadingScreen?.State ?? _nativeLoadingProgress?.State,
            cell = _nativeActiveCell?.Cell.FormKey.ToString(),
            exteriorLod = cellNodes.OfType<RuntimeNativeExteriorLod>().SingleOrDefault()?.State,
            exteriorStreaming = NativeStreamingState,
            resourceCache = RuntimeLiveContentSource.Current?.CacheState,
            player = _nativePlayer is null ? null : new
            {
                position = Vector(_nativePlayer.GlobalPosition),
                viewPitchRadians = _nativePlayer.ViewPitchRadians,
                velocity = Vector(_nativePlayer.Velocity),
                onFloor = _nativePlayer.IsOnFloor(),
                sprinting = _nativePlayer.Sprinting,
                jumpCount = _nativePlayer.JumpCount,
                stepCount = _nativePlayer.StepCount,
                stepBlocked = _nativePlayer.StepBlocked,
                maximumWalkableSlopeDegrees = Mathf.RadToDeg(_nativePlayer.FloorMaxAngle),
                blockingShape = _nativePlayer.BlockingShape,
                collisionResident = _nativePlayer.CollisionResident,
                collisionContacts = _nativePlayer.CollisionContacts,
                modalInput = _nativePlayer.ModalInput,
                input = _nativePlayer.InputState,
                furniture = _nativePlayer.FurnitureState,
                movementEnabled = _nativePlayer.GetMeta("opennv_source_movement_enabled", false).AsBool(),
                lookingEnabled = _nativePlayer.GetMeta("opennv_source_looking_enabled", false).AsBool(),
            },
            camera = camera is null ? null : new
            {
                position = Vector(camera.GlobalPosition),
                forward = Vector(-camera.GlobalBasis.Z),
                fov = camera.Fov,
                near = camera.Near,
                far = camera.Far,
            },
            opening = _nativeOpeningStageDriver is null ? null : new
            {
                quest = _nativeOpeningStageDriver.QuestEditorId,
                stage = _nativeOpeningStageDriver.Stage,
                timerSeconds = _nativeOpeningStageDriver.TimerSeconds,
                pending = _nativeOpeningStageDriver.PendingBlockers.ToArray(),
                headTrackingCommands = _nativeOpeningStageDriver.HeadTrackingCommands,
                error = _nativeOpeningStageDriver.ExecutionError,
                blockingError = _nativeOpeningStageDriver.BlockingExecutionError,
                stageResults = _nativeOpeningStageDriver.StageResultState,
                saveRequest = _nativeOpeningStageDriver.SaveRequestState,
                playerProgress = _nativeOpeningStageDriver.PlayerProgressState,
                experienceNotifications = _nativeOpeningStageDriver.ExperienceNotificationState,
                interfaceActivationFrames = _nativeOpeningStageDriver.InterfaceActivationFrameState,
                combatGroups = _nativeOpeningStageDriver.CombatGroupState,
                actorPerception = _nativeOpeningStageDriver.ActorPerceptionState,
                actorProcesses = _nativeOpeningStageDriver.ActorProcessState,
                actualProcessRuntime = _nativeOpeningStageDriver.ActualProcessRuntimeState,
                actualProcessCommon = _nativeOpeningStageDriver.ActualProcessCommonState,
                actorUpdates = _nativeOpeningStageDriver.ActorUpdateState,
                cellProcesses = _nativeOpeningStageDriver.CellProcessState,
                advancementRuntime = _nativeOpeningStageDriver.PlayerAdvancementRuntimeState,
                playerPhysical = _nativePlayer?.PlayerPhysicalState,
                nativePlugins = _nativeOpeningStageDriver.NativePluginExecutionState,
                levelUp = _nativeOpeningStageDriver.PlayerLevelUpMenuState,
            },
            movies = GetChildren().OfType<NativeGamebryoMovie>()
                .Concat(_nativeOpeningStageDriver?.GetChildren().OfType<NativeGamebryoMovie>() ?? [])
                .Where(movie => !movie.IsQueuedForDeletion()).Select(movie => new
                {
                    source = MovieField(movie, "opennv_movie_source"),
                    frame = MovieField(movie, "opennv_movie_frame"),
                    seconds = MovieField(movie, "opennv_movie_seconds"),
                    audioUnderruns = MovieField(movie, "opennv_movie_audio_underruns"),
                    audioFramesQueued = MovieField(movie, "opennv_movie_audio_frames_queued"),
                    error = MovieField(movie, "opennv_movie_error"),
                }).ToArray(),
            missingRuntimeReferences = _nativeActiveCell is null ? [] : _parityObservations.Coverage().Missing.ToArray(),
            actorDivergences = _nativeActorDivergences.ToArray(),
            referenceDivergences = _nativeReferenceDivergences.ToArray(),
            playerMoves = _nativeReferences?.PlayerMoves.State,
            mapMarkers = _nativeReferences?.KnownMapMarkers.Select(marker => new
            {
                reference = marker.Source.Reference.ToString(),
                marker.Source.Name,
                marker.Source.Type,
                marker.State.Visible,
                marker.State.CanTravel,
            }).ToArray(),
            speech = _nativeOpeningStageDriver?.SpeechState,
            soundVoices = _nativePluginStack?.SoundVoices.State,
            soundPaths = _nativePluginStack?.SoundPaths.State,
            questProgress = _nativeOpeningStageDriver?.Quests.ProgressState,
            conversation = _nativeOpeningStageDriver?.ConversationState,
            questScripts = _nativeQuestScripts?.Observe(detailed),
            numericGameSettings = _nativePluginStack?.NumericSettings.State,
            gameTime = _nativeGameTimeAdapter?.State,
            gameTimeUnbound = _nativeGameTimeUnbound,
            skyLighting = _nativeSkyLighting?.Unbound is null ? _nativeSkyLighting?.Capture() : null,
            skyLightingUnbound = _nativeSkyLighting?.Unbound,
            wind = cellChildren?.OfType<RuntimeNativeWind>().SingleOrDefault()?.State,
            playerInventory = _nativeInventory.Items,
            ingestibles = _nativeOpeningStageDriver?.IngestibleState,
            gameplayHud = _nativeGameplayHud?.State,
            hudMessages = _nativeHudMessages?.State,
            subtitles = _nativeSubtitles?.State,
            pipBoy = _nativePipBoy?.State,
            playerPresentation = _nativePlayer?.PresentationState,
            questScriptsUnbound = _nativeContinueOpening && _nativeOpeningRestore?.State.Scripts is null ? "Current campaign save has no quest script state." : null,
            playerPackage = _nativeOpeningStageDriver?.PlayerPackageState,
            characterCreation = _nativeOpeningStageDriver?.CharacterCreationState,
            traitMenu = _nativeOpeningStageDriver?.TraitMenuState,
            levelUpMenu = _nativeOpeningStageDriver?.PlayerLevelUpMenuState,
            vigor = _nativeOpeningStageDriver?.VigorState,
            specialBook = _nativeOpeningStageDriver?.SpecialBookState,
            imageSpace = cellChildren is null ? null : cellNodes.OfType<RuntimeNativeImageSpace>().Select(presenter => new
            {
                active = presenter.Frame?.Active.Select(modifier => new
                {
                    form = modifier.Source.Form.ToString(),
                    modifier.Source.EditorId,
                    modifier.Source.SourceSha256,
                    modifier.ElapsedSeconds,
                    modifier.Source.Duration,
                }).ToArray(),
                unbound = presenter.Frame?.UnboundChannels,
                finalStageOperational = presenter.GetMeta("opennv_image_space_operational", false).AsBool(),
            }).ToArray(),
            actors = _nativeReferencePresentation?.Actors
                .Select(actor =>
                {
                    var rotation = actor.GlobalBasis.GetRotationQuaternion();
                    return new
                    {
                        reference = actor.Appearance.Reference?.ToString(),
                        position = new[] { actor.GlobalPosition.X, actor.GlobalPosition.Y, actor.GlobalPosition.Z },
                        rotation = new[] { rotation.X, rotation.Y, rotation.Z, rotation.W },
                        animation = actor.AnimationState,
                    };
                }).ToArray(),
            creatures = _nativeReferencePresentation?.Nodes.Values.OfType<RuntimeNativeCreature>()
                .Select(actor => new
                {
                    position = new[] { actor.GlobalPosition.X, actor.GlobalPosition.Y, actor.GlobalPosition.Z },
                    scale = new[] { actor.Scale.X, actor.Scale.Y, actor.Scale.Z },
                    animation = actor.Observation,
                }).ToArray(),
            lights = cellChildren is null ? null : cellNodes.OfType<OmniLight3D>().Select(light => new
            {
                reference = light.GetMeta("opennv_ligh_reference", "").AsString(),
                emittance = light.GetMeta("opennv_ligh_emittance", "").AsString(),
                shaderRgb = light.GetMeta("opennv_ligh_shader_rgb").AsFloat32Array(),
                position = Vector(light.GlobalPosition),
                light.LightEnergy,
                light.OmniRange,
                light.ShadowEnabled,
            }).ToArray(),
            objectAnimations = !detailed ? null : _nativeCurrentCellRoot?.FindChildren("*", "", true, false).OfType<RuntimeNifControllerPlayer>()
                .Select(controller => new
                {
                    path = controller.GetPath().ToString(),
                    sequence = controller.ActiveSequence,
                    sourceSeconds = controller.SourceTimeSeconds,
                    registered = controller.SequenceNames.ToArray(),
                }).ToArray(),
            modelSounds = !detailed ? null : _nativeCurrentCellRoot?.FindChildren("*", "", true, false)
                .OfType<NativeOwnedAnimationSoundPlayer>().Select(sounds => new
                {
                    path = sounds.GetPath().ToString(),
                    state = sounds.State,
                }).ToArray(),
        };
    }

    private async void LoadNativeLiveStack()
    {
        try { await LoadNativeLiveStackAsync(); }
        catch (Exception failure)
        {
            if (_pendingSaveActivation is not null) RejectNativePendingLoad(failure);
            GD.PushError($"OPENNV_GODOT_RUNTIME_FAIL {failure}");
            GetTree().Quit(1);
        }
    }

    private async Task LoadNativeLiveStackAsync()
    {
        var source = RuntimeLiveContentSource.Current ??
            throw new InvalidOperationException("Live retail source was not configured.");
        SetLoadingStatus("Reading game data");
        if (DisplayServer.GetName() == "headless" || _options.ContainsKey("new-game"))
        {
            _nativeMenuRead = Task.Run(() => IndexNativeLiveStack(source.PluginSources));
            await _nativeMenuRead;
            if (_options.ContainsKey("new-game"))
            {
                InitializeNativePlayerInventory();
                CreateNativeQuestScripts();
                await BootstrapNativeNewGame();
                _nativeMenuRead = LoadNativeInitialCell();
                await _nativeMenuRead;
            }
            if (DisplayServer.GetName() == "headless")
                GetTree().Quit(0);
            return;
        }
        ShowNativeLiveMenu(source.PluginSources);
    }

    private void IndexNativeLiveStack(IReadOnlyList<FalloutPluginSource> sources)
    {
        var content = RuntimeLiveContentSource.Current ??
            throw new InvalidOperationException("Live retail source was not configured.");
        _nativePluginStack = FalloutPluginStack.Load(sources, out var loadMetrics);
        FalloutAddonNodes.Bind(content, _nativePluginStack);
        _nativeQuestState = new(_nativePluginStack, _nativeInventory.Notifications);
        var savePath = Path.GetFullPath(RequireOption(_options, "save-path"));
        var scriptOverlay = Path.Combine(Path.GetDirectoryName(savePath) ??
            throw new InvalidDataException("Native save path has no profile directory."), "script-config");
        _nativeScriptStorage = FalloutScriptStorage.Open(content, scriptOverlay);
        _nativeUi = FalloutUiComponentStore.Open(_nativePluginStack);
        _nativeReferences?.Dispose();
        _nativeReferences = new(_nativePluginStack, auxiliary: _nativeScriptStorage.Auxiliary,
            ini: _nativeScriptStorage.Ini, ui: _nativeUi, controls: _nativeScriptStorage.Controls);
        _nativeReferences.ConfigureCombatGroups(FalloutCombatGroupDeclaration.ReadExecutable(content.FalloutExecutablePath), content.StackId);
        _nativeReferences.ConfigureActualActorCellProducers(content.StackId);
        _nativeReferences.ConfigureActorPerception(FalloutActorPerceptionDeclaration.Read(content.FalloutExecutablePath), content.StackId);
        _nativeReferences.ConfigureActualProcessRuntime(FalloutActorProcessRuntimeDeclaration.ForExecutable(
            FalloutActorProcessDeclaration.Read(content.FalloutExecutablePath).ExecutableSha256), content.StackId);
        _nativeReferences.ConfigureActorProcesses(FalloutActorProcessDeclaration.Read(content.FalloutExecutablePath), content.StackId);
        _nativeStartingQuest = FalloutNewGameBootstrap.StartingQuest(_nativePluginStack, FalloutInstallationSettings.Read(content));
        _nativeGlobals = FalloutGlobalState.Read(_nativePluginStack);
        _nativeGameTime = new(_nativeGlobals, FalloutGameTimeBindings.Read(_nativePluginStack),
            FalloutCalendar.Read(content.FalloutExecutablePath));
        _nativeSkyLighting = new(_nativePluginStack, FalloutGameSettingFloats.ReadRetained(_nativePluginStack, "fDaytimeColorExtension", nameof(FalloutSkyLightingState)));
        _nativeRaceSexContract = FalloutNativeRaceSexResolver.Resolve(_nativePluginStack);
        _nativeOpeningControls = new(new Dictionary<string, IReadOnlyDictionary<short, FalloutOpeningControlStage>>(StringComparer.OrdinalIgnoreCase), ResultDriven: true);
        _nativeOpeningTransitions = new([]);
        _nativeOpeningRestore = null;
        if (File.Exists(savePath))
        {
            try
            {
                _nativeOpeningRestore = FalloutNativeCampaignSave.Read(
                    savePath,
                    content.SaveCompatibilityId,
                    _nativePluginStack);
                if (_nativeOpeningRestore.State.PlayerSkillValues is null || _nativeOpeningRestore.State.PlayerAbilityScripts is null)
                    throw new InvalidDataException("Continue requires current player skill/effect authority.");
            }
            catch (Exception exception) when (
                exception is IOException or InvalidDataException or JsonException or NotSupportedException)
            {
                _nativeOpeningRestore = null;
                GD.PushWarning($"OPENNV_NATIVE_CONTINUE_REJECTED {exception.Message}");
            }
        }
        var archiveWarmupWait = Stopwatch.StartNew();
        content.ArchiveWarmup.GetAwaiter().GetResult();
        archiveWarmupWait.Stop();
        GD.Print(
            $"OPENNV_NATIVE_STACK_READY edition={content.Edition} campaign={content.Campaign} " +
            $"game={content.Game} plugins={_nativePluginStack.Plugins.Count} " +
            $"records={_nativePluginStack.EffectiveRecordCount} cell=source-startup-pending " +
            $"pluginOpenMs={loadMetrics.PluginHeaderScan.TotalMilliseconds:F1} " +
            $"winnerIndexMs={loadMetrics.WinnerConstruction.TotalMilliseconds:F1} " +
            $"archiveWinnerWaitMs={archiveWarmupWait.Elapsed.TotalMilliseconds:F1}");
    }

    private async Task IndexNativeLiveStackForMenu(
        IReadOnlyList<FalloutPluginSource> sources,
        NativeGamebryoStartMenu menu)
    {
        try
        {
            await Task.Run(() => IndexNativeLiveStack(sources));
            if (_nativeSessionTransitioning) { CancelNativeLauncherEntry(); return; }
            var stack = _nativePluginStack ??
                throw new InvalidOperationException("Native plugin stack was not indexed.");
            if (_nativeOpeningControls is not null) CreateNativeQuestScripts();
            menu.SetReady(stack, _nativeOpeningRestore is not null);
            if (_continueAfterRestart && _nativeOpeningRestore is not null)
            {
                _continueAfterRestart = false; _nativeStartingGame = true; _nativeContinueOpening = true;
                await StartNativeGameFromMenu(menu.GetParent<CanvasLayer>(), "sLoad");
            }
            else
            {
                if (_continueAfterRestart)
                    _sessionLoadFailure = RejectNativePendingLoad(new InvalidDataException("The selected save could not be restored from this source stack."));
                DismissLoadingScreen();
                if (_sessionLoadFailure is { } failure)
                {
                    CancelNativeLauncherEntry();
                    menu.ShowLoadFailure(failure, canRetry: true);
                }
                else ConsumeNativeLauncherEntry(menu);
            }
        }
        catch (Exception exception)
        {
            CancelNativeLauncherEntry();
            var failure = RejectNativePendingLoad(exception);
            DismissLoadingScreen();
            menu.ShowLoadFailure(failure, canRetry: false);
        }
    }

    private void ShowNativeLiveMenu(IReadOnlyList<FalloutPluginSource> sources)
    {
        GetTree().AutoAcceptQuit = false;
        AttachNativeCloseRequest();
        var layer = new CanvasLayer { Name = "NativeLiveMenu", Layer = NativeMenuCanvasLayer };
        var menu = new NativeGamebryoStartMenu(action =>
        {
            if (_nativeSessionTransitioning) return;
            if (action == "sQuit")
                QuitNativeSession();
            else if (action == "sLoad")
                OpenNativeSessionMenu(showSaves: true, layer.GetChild<Control>(0));
            else if (action is "sNew" or "sContinue")
            {
                if (_nativeStartingGame) return;
                _nativeStartingGame = true;
                _nativeContinueOpening = action != "sNew";
                _nativeMenuRead = StartNativeGameFromMenu(layer, action);
            }
            else
            {
                SetMeta("opennv_ui_divergence", $"StartMenu action has no retail-equivalent owner: {action}");
                GD.PushError($"OPENNV_UI_DIVERGENCE menu=StartMenu action={action} owner=missing");
            }
        });
        layer.AddChild(menu);
        AddChild(layer);
        _nativeStartMenuLayer = layer;
        _nativeMenuRead = IndexNativeLiveStackForMenu(sources, menu);
    }

    private bool _nativeStartingGame;
    private async Task StartNativeGameFromMenu(CanvasLayer layer, string action)
    {
        var menu = layer.GetChild<NativeGamebryoStartMenu>(0);
        menu.ShowLoading(_nativeContinueOpening);
        BeginLoadingScreen(_nativeContinueOpening ? "Loading saved game" : "Starting a new game");
        GD.Print($"OPENNV_NATIVE_MENU_LOAD action={action} save={_nativeContinueOpening}");
        try
        {
            // Publish the loading state in flat and the shared XR menu surface
            // before synchronous native scene publication starts.
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            if (!_nativeContinueOpening)
            {
                InitializeNativePlayerInventory();
                await BootstrapNativeNewGame();
            }
            await LoadNativeInitialCell();
            if (ReferenceEquals(_nativeStartMenuLayer, layer)) _nativeStartMenuLayer = null;
            layer.QueueFree();
        }
        catch (Exception error)
        {
            _nextSessionLoadFailure = RejectNativePendingLoad(error);
            // Reload from clean owners before offering another load. This task
            // cannot await itself while retiring the failed partial world.
            _nativeMenuRead = null;
            menu.ShowLoadFailure(_nextSessionLoadFailure, canRetry: false);
            RestartNativeSession(false);
        }
    }

    private async Task LoadNativeInitialCell()
    {
        _pendingSaveActivation?.RequireSelected(Path.GetFullPath(RequireOption(_options, "save-path")));
        var wasPaused = GetTree().Paused;
        GetTree().Paused = true;
        try
        {
            SetLoadingStatus(_nativeContinueOpening ? "Restoring saved game" : "Preparing the world");
            if (DisplayServer.GetName() != "headless")
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            await PopulateNativeInitialCell();
        }
        finally
        {
            GetTree().Paused = wasPaused || _nativeSessionTransitioning || _retiringNativeSession;
        }
        if (_pauseAfterCheckpointLoad)
        {
            _pauseAfterCheckpointLoad = false;
            OpenNativeSessionMenu(showSaves: false);
        }
        _restoredNativeCheckpoint = _pendingCheckpointRestore;
        _pendingCheckpointRestore = null;
        _pendingSaveActivation?.Commit(); _pendingSaveActivation = null;
        DismissLoadingScreen();
    }

    private async Task PopulateNativeInitialCell()
    {
        _ = RuntimeLiveContentSource.Current ??
            throw new InvalidOperationException("Live retail source was cleared during startup.");
        var stack = _nativePluginStack ??
            throw new InvalidOperationException("Native plugin stack was not indexed.");
        var startup = _nativeContinueOpening ? null : (_nativeBootstrap ??
            throw new InvalidOperationException("New Game has no configured source bootstrap.")).PreparePlacement() ??
            throw new InvalidOperationException("New Game has no admitted original player movement.");
        var startupPlacement = startup?.Placement;
        var cell = FalloutCellSceneReader.Read(stack, _nativeContinueOpening ?
            _nativeOpeningRestore!.State.ActiveCell : startupPlacement!.Cell);
        FalloutDoorTransition? transition = null;
        var restore = _nativeContinueOpening
            ? _nativeOpeningRestore ?? throw new InvalidOperationException(
                "Native Continue was selected without a valid cold save.")
            : null;
        if (restore is not null)
        {
            // Prewarmed presentation captured the new-game reference owner.
            // Restore builds presentation against the restored world instead.
            FreeNativeSourceCellRoot(_nativePrewarmedInitialCellRoot);
            _nativePrewarmedInitialCellRoot = null;
            RetireNativePluginCampaign();
            _nativeReferences?.Dispose();
            _nativeReferences = new(stack, auxiliary: _nativeScriptStorage?.Auxiliary,
                ini: _nativeScriptStorage?.Ini, ui: _nativeUi, controls: _nativeScriptStorage?.Controls);
            _nativeReferences.RestoreEncounterZones(restore.State.EncounterZones);
            _nativeReferences.Restore(restore.State.References ??
                throw new InvalidDataException("Current campaign has no retained reference state."));
            _nativeReferences.RestoreActorOverrides(restore.State.ActorOverrides);
            _nativeReferences.RestoreDetection(restore.State.DetectionEvents);
            _nativeReferences.RestoreFactionRelations(restore.State.FactionRelations);
            var content = RuntimeLiveContentSource.Current ?? throw new InvalidOperationException("Cold combat groups have no selected source.");
            _nativeReferences.ConfigureCombatGroups(FalloutCombatGroupDeclaration.ReadExecutable(content.FalloutExecutablePath), content.StackId,
                restore.State.CombatGroups ?? throw new InvalidDataException("Current campaign has no combat group continuation."));
            _nativeReferences.ConfigureActualActorCellProducers(content.StackId,
                restore.State.ActorUpdates ?? throw new InvalidDataException("Current actor update continuation is absent."),
                restore.State.CellProcesses ?? throw new InvalidDataException("Current CELL process continuation is absent."));
            _nativeReferences.ConfigureActorPerception(FalloutActorPerceptionDeclaration.Read(content.FalloutExecutablePath), content.StackId,
                restore.State.ActorPerception ?? throw new InvalidDataException("Current campaign has no actor perception continuation."));
            _nativeReferences.ConfigureActualProcessRuntime(FalloutActorProcessRuntimeDeclaration.ForExecutable(
                FalloutActorProcessDeclaration.Read(content.FalloutExecutablePath).ExecutableSha256), content.StackId,
                restore.State.ActorProcessRuntime ?? throw new InvalidDataException("Current process runtime continuation is absent."),
                restore.State.ActorProcessCommon ?? throw new InvalidDataException("Current common process continuation is absent."));
            _nativeReferences.ConfigureActorProcesses(FalloutActorProcessDeclaration.Read(content.FalloutExecutablePath), content.StackId,
                restore.State.ActorProcesses ?? throw new InvalidDataException("Current campaign has no actor process continuation."));
        }
        else if (_nativeBootstrap is null)
        {
            // New Game starts a new save lifetime even when the title/menu
            // owner has remained alive in the same process.
            _nativeScriptStorage?.Auxiliary.ResetForNewGame();
            _nativeUi?.Reset();
        }
        {
            if (restore is not null)
            {
                if (restore.State.SkyLighting is { } sky) _nativeSkyLighting!.Restore(sky);
                else _nativeSkyLighting!.MarkUnbound("Legacy save has no sky/climate state; region emittance cannot be reconstructed.");
            }
            if (restore is null)
            {
                if (_nativeBootstrap is null) _nativeGameTime!.InitializeNewGame();
            }
            else if (restore.State.Globals is { } globals && restore.State.GameTime is { } gameTime)
            {
                _nativeGlobals!.Restore(globals);
                _nativeGameTime!.Restore(gameTime);
            }
            else
            {
                _nativeGameTimeUnbound = "Legacy save has no global/calendar state; its clock cannot be reconstructed.";
                _nativeGlobals = null;
                _nativeGameTime = null;
            }
            if (_nativeGameTime is not null)
            {
                _nativeGameTimeAdapter = new(_nativeGameTime);
                AddChild(_nativeGameTimeAdapter);
            }
        }
        if (restore is not null)
        {
            _nativeInventory.Restore(restore.Inventory, restore.State.EquippedRuntimeFormIds.ToArray(), restore.State.InventoryRandomState);
            _nativeReferences!.BindPlayerInventory(_nativeInventory);
            if (restore.State.Quests is not null) _nativeQuestState!.Restore(restore.State.Quests);
        }
        var activeCell = restore?.State.ActiveCell ?? cell.Cell.FormKey;
        var sourceSide = activeCell == cell.Cell.FormKey && !_nativeContinueOpening;
        var activeScene = cell;
        FalloutExteriorGridScene? grid = null;
        float[]? position = null;
        if (startupPlacement is not null) position = startupPlacement.Position;
        else if (restore is not null)
        {
            var restored = FalloutNativeCampaignSave.RestorePlayerPosition(restore.State);
            var units = _configuration.World.GameUnitsToMeters;
            position = [restored[0] / units, -restored[2] / units, restored[1] / units];
        }
        if (activeScene.Cell.Worldspace is { } world)
        {
            grid = ResolveExterior(world, position ?? throw new InvalidDataException("First exterior placement has no authoritative position."));
            if (activeCell != grid.Scene.Cell.FormKey && activeCell != grid.PersistentCell)
                throw new InvalidDataException($"Source CELL {activeCell} does not contain the admitted exterior position.");
            activeScene = grid.Scene;
        }
        activeScene = _nativeReferences!.ComposeResidency(activeScene, grid?.Cells);
        if (grid is not null) grid = grid with { Scene = activeScene };
        _nativeSkyLighting?.EnterCell(activeScene.Cell, _nativeGlobals, position);
        SetLoadingStatus("Loading the world");
        var root = sourceSide && _nativePrewarmedInitialCellRoot is not null
            ? _nativePrewarmedInitialCellRoot
            : await BuildNativeCellRootResponsive(activeScene, transition, sourceSide,
                grid is null ? null : grid.Cells.Select(definition => definition.FormKey).Append(grid.PersistentCell).Distinct(FalloutFormKeyComparer.Instance).ToArray());
        _nativePrewarmedInitialCellRoot = null;
        SetLoadingStatus("Preparing characters and controls");
        if (DisplayServer.GetName() != "headless")
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        if (_nativeSessionTransitioning || _retiringNativeSession)
        {
            FreeNativeSourceCellRoot(root);
            throw new OperationCanceledException("Initial CELL session retired.");
        }
        if (grid is not null) AddExteriorLandscape(root, grid);
        AddChild(root);
        foreach (var sounds in root.FindChildren("*", "", true, false).OfType<NativeOwnedAnimationSoundPlayer>())
            sounds.RequirePcmRestored();
        // Cold publication requires the current script owner and its clocks.
        if (restore is not null || _nativeQuestScripts is null)
            CreateNativeQuestScripts(restore is null ? null : restore.State.Scripts ??
                throw new InvalidDataException("Current campaign save has no script continuation owner."));
        if (activeScene.Cell.Lighting is not null)
            AddNativeCellEnvironment(root, activeScene);
        else if (grid is not null)
            AddExteriorEnvironment(root, activeScene.Cell);
        AddNativePlayer(activeScene, startupPlacement);
        SetNativeActiveCell(root, activeScene);
        _nativeBootstrap?.AttachPlayerPackages(_nativeOpeningStageDriver!.AttachBootstrapPlayerPackage);
        AddNativeGameplayHud();
        if (grid is not null)
        {
            SetLoadingStatus("Preparing the distant world");
            await root.GetChildren().OfType<RuntimeNativeExteriorLod>().Single()
                .PrepareInitialSelection(GetViewport().GetCamera3D().GlobalPosition);
        }
        if (startup is not null) _nativeBootstrap!.CompletePlacement(startup);
        if (_nativeQuestScripts is not null) _nativeQuestScripts.ActivateWorld(restore is not null);
        GD.Print(
            $"OPENNV_NATIVE_ACTIVE_CELL cell={activeScene.Cell.FormKey} " +
            $"restored={(restore is not null)} sourceSide={sourceSide}");
    }

    private void CreateNativeQuestScripts(FalloutQuestScriptsSnapshot? restore = null)
    {
        RetireNativePluginCampaign();
        var claimed = _nativeOpeningControls!.Quests.Values.Select(stages => stages.Values.First().Quest).ToHashSet();
        var scripts = new RuntimeNativeQuestScripts(_nativePluginStack!, _nativeQuestState!, claimed, _nativeInventory, _nativeGlobals,
            _nativeReferences, NativeScriptEvents(), _nativeScriptStorage);
        scripts.EvaluateMessageCondition = condition => _nativeOpeningStageDriver is { } driver
            ? driver.EvaluateMessageCondition(condition) : (_nativeBootstrap ??
                throw new NotSupportedException("Pre-world message conditions have no source bootstrap owner.")).EvaluateCondition(condition);
        scripts.ActiveMenus = NativeActiveMenus;
        scripts.StartupMenus = NativeStartupMenus;
        scripts.SoundUnitsToMetres = _configuration.World.GameUnitsToMeters;
        scripts.SoundReference = reference => _nativePluginStack!.RuntimeFormId(reference) == 0x14 ? _nativePlayer :
            (_nativeReferencePresentation ?? throw new NotSupportedException("Script sound world presentation is not resident."))
                .Nodes.GetValueOrDefault(reference);
        if (restore is not null) scripts.Scripts.Restore(restore);
        if (_nativeQuestScripts is not null)
        {
            RemoveChild(_nativeQuestScripts);
            _nativeQuestScripts.QueueFree();
        }
        _nativeQuestScripts = scripts;
        BindNativePluginCampaign();
        AddChild(scripts);
    }

    private Node3D BuildNativeCellRoot(
        FalloutCellScene cell,
        FalloutDoorTransition? transition,
        bool sourceSide, IReadOnlyList<FalloutFormKey>? sourceCells = null)
    {
        var root = new Node3D { Name = $"NativeCell_{cell.Cell.FormKey}" };
        try
        {
            PopulateNativeCellRoot(root, cell, transition, sourceSide, sourceCells);
            return root;
        }
        catch (Exception error)
        {
            RetireFailedNativeSourceCellRoot(root, error);
            throw;
        }
    }

    private void PopulateNativeCellRoot(Node3D root, FalloutCellScene cell,
        FalloutDoorTransition? transition, bool sourceSide, IReadOnlyList<FalloutFormKey>? sourceCells)
    {
        BeginNativeCellRoot(root, cell, sourceCells);
        foreach (var reference in cell.References) PlaceNativeCellReference(root, cell, reference);
        CompleteNativeCellRoot(root, cell);
    }

    private async Task<Node3D> BuildNativeCellRootResponsive(FalloutCellScene cell,
        FalloutDoorTransition? transition, bool sourceSide, IReadOnlyList<FalloutFormKey>? sourceCells = null)
    {
        _ = transition;
        _ = sourceSide;
        var root = new Node3D { Name = $"NativeCell_{cell.Cell.FormKey}" };
        var total = Stopwatch.StartNew();
        var slice = Stopwatch.StartNew();
        var referenceWatch = new Stopwatch();
        var yields = 0;
        double maximumReferenceMilliseconds = 0;
        FalloutFormKey? maximumReference = null;
        try
        {
            BeginNativeCellRoot(root, cell, sourceCells);
            foreach (var reference in cell.References)
            {
                if (_nativeSessionTransitioning || _retiringNativeSession)
                    throw new OperationCanceledException("CELL construction session retired.");
                referenceWatch.Restart();
                PlaceNativeCellReference(root, cell, reference);
                referenceWatch.Stop();
                if (referenceWatch.Elapsed.TotalMilliseconds > maximumReferenceMilliseconds)
                {
                    maximumReferenceMilliseconds = referenceWatch.Elapsed.TotalMilliseconds;
                    maximumReference = reference.FormKey;
                }
                // Each reference remains atomic. Publish the same detached root
                // only after all references finish, while letting loading UI draw.
                if (slice.Elapsed.TotalMilliseconds >= 8 && DisplayServer.GetName() != "headless")
                {
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    slice.Restart();
                    yields++;
                }
            }
            if (_nativeSessionTransitioning || _retiringNativeSession)
                throw new OperationCanceledException("CELL construction session retired.");
            CompleteNativeCellRoot(root, cell);
            GD.Print($"OPENNV_NATIVE_CELL_BUILD cell={cell.Cell.FormKey} references={cell.References.Count} " +
                $"elapsedMs={total.Elapsed.TotalMilliseconds:F1} frameYields={yields} " +
                $"maxReferenceMs={maximumReferenceMilliseconds:F1} maxReference={maximumReference}");
            return root;
        }
        catch (Exception error)
        {
            RetireFailedNativeSourceCellRoot(root, error);
            throw;
        }
    }

    private void BeginNativeCellRoot(Node3D root, FalloutCellScene cell, IReadOnlyList<FalloutFormKey>? sourceCells)
    {
        _ = RuntimeLiveContentSource.Current ??
            throw new InvalidOperationException("Live retail source was cleared during CELL streaming.");
        _nativeReferences!.EnterEncounterCell(cell.Cell.FormKey,
            _nativeOpeningStageDriver?.PlayerLevel ?? _nativeOpeningRestore?.State.Vitals?.Level ?? 1);
        DiscoverNativeCellReferences(cell);
        EnterNativeSourceCellAttachment(root, cell, sourceCells);
        _nativeActorDivergences.Clear();
        _nativeReferenceDivergences.Clear();
    }

    private void PlaceNativeCellReference(Node3D root, FalloutCellScene cell, FalloutPlacedReference reference)
    {
        var previousChildren = root.GetChildCount();
        try
        {
            PlaceNativeReference(root, cell, reference);
            CompleteNativeSourceCellReference(root, cell, reference, previousChildren);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or NotSupportedException or InvalidOperationException)
        {
            // Reject the whole failed reference. Other source references
            // retain independent ownership; the rejected identity remains
            // missing in the parity denominator, never a substitute draw.
            while (root.GetChildCount() > previousChildren) root.GetChild(previousChildren).Free();
            FailNativeSourceCellReference(root, reference, error, previousChildren);
            _nativeReferenceDivergences[reference.FormKey.ToString()] = error.Message;
            GD.PushError($"OPENNV_NATIVE_REFERENCE_DIVERGENCE reference={reference.FormKey} base={reference.Base}: {error.Message}");
        }
    }

    private void CompleteNativeCellRoot(Node3D root, FalloutCellScene cell)
    {
        const string parityScope = "world/active-cell";
        var presentation = new RuntimeNativeReferencePresentation(_nativeReferences!, cell.References,
            reference => MaterializeNativeReference(root, cell, reference));
        var identities = cell.References.ToDictionary(reference => reference.FormKey.ToString(), reference => reference.FormKey);
        foreach (var node in root.GetChildren().OfType<Node3D>())
        {
            var key = node is RuntimeNativeNpc actor ? actor.Appearance.Reference :
                identities.TryGetValue(node.GetMeta("opennv_reference_form_key", "").AsString(), out var identity) ? identity : (FalloutFormKey?)null;
            if (key is { } found) presentation.Register(found, node);
        }
        root.AddChild(presentation);
        var coverage = _parityObservations.Snapshot();
        var missing = coverage.Missing.Count(identity =>
            identity.StartsWith(parityScope + "/", StringComparison.Ordinal));
        GD.Print(
            $"OPENNV_NATIVE_CELL_READY cell={cell.Cell.FormKey} " +
            $"residentPrototypes={_nativeNifPrototypes.Count} discovered={cell.References.Count} " +
            $"observed={cell.References.Count - missing} missing={missing} " +
            $"runtimePresence={(missing == 0 ? "complete" : "incomplete")} parity=unmeasured " +
            "source=live-retail-files");

    }

    private void PlaceNativeReference(Node3D root, FalloutCellScene cell, FalloutPlacedReference reference,
        bool materializeDisabled = false, bool observe = true, FalloutNifFile? preparedModel = null, RuntimeNativeNpc? preparedNpc = null)
    {
        var source = RuntimeLiveContentSource.Current ?? throw new InvalidOperationException("Owned source is absent.");
        const string parityScope = "world/active-cell";
        string ParityIdentity(FalloutPlacedReference value) => $"{cell.Cell.FormKey}/{value.FormKey}";
        void Observe(string scope, string identity, byte[] state)
        {
            if (observe) _parityObservations.Observe(scope, identity, state);
        }
        if (!cell.BaseObjects.TryGetValue(reference.Base, out var baseObject))
            throw new InvalidDataException(
                $"Live CELL reference {reference.FormKey} has no decoded base object.");
        if (!materializeDisabled && !_nativeReferences!.IsEnabled(reference.FormKey))
        {
            Observe(
                parityScope,
                ParityIdentity(reference),
                NativeReferenceState(reference, baseObject, "disabled"));
            return;
        }
        if (FalloutNewVegasBuiltinForms.IsInternalStatic(baseObject.Signature,
                (_nativePluginStack ?? throw new InvalidOperationException("Native stack is absent."))
                    .RuntimeFormId(baseObject.FormKey)))
        {
            // Internal markers remain real source references for packages,
            // placement and scripts. Their editor meshes are not game draws.
            var marker = new Node3D
            {
                Name = $"Reference_{reference.FormKey}",
                Transform = ReferenceTransform(reference),
            };
            marker.SetMeta("opennv_reference_form_key", reference.FormKey.ToString());
            marker.SetMeta("opennv_internal_static", true);
            root.AddChild(marker);
            Observe(parityScope, ParityIdentity(reference),
                NativeReferenceState(reference, baseObject, "internal-static"));
            return;
        }
        if (baseObject.Light is not null)
        {
            AddNativePlacedLight(root, reference, baseObject);
            Observe(
                parityScope,
                ParityIdentity(reference),
                NativeReferenceState(reference, baseObject, "light"));
            return;
        }
        if (baseObject.Signature == "NPC_")
        {
            try
            {
                var selection = _nativeReferences!.InitializeActorTemplates(reference.FormKey,
                    _nativeOpeningStageDriver?.PlayerLevel ?? _nativeOpeningRestore?.State.Vitals?.Level ?? 1, _nativeGlobals);
                if (selection.Absent)
                {
                    Observe(parityScope, ParityIdentity(reference), NativeReferenceState(reference, baseObject, "source-leveled-none"));
                    return;
                }
                var equippedArmor = _nativeReferences!.EquippedArmor(reference.FormKey,
                    _nativeOpeningStageDriver?.PlayerLevel ?? _nativeOpeningRestore?.State.Vitals?.Level ?? 1, _nativeGlobals);
                var actor = preparedNpc ?? RuntimeNativeNpc.Create(_nativePluginStack!, source, reference,
                    _configuration.World.GameUnitsToMeters, (appearance, part, nif, geometry) =>
                        NativeNpcMaterial.Resolve(appearance, part, nif, geometry, _nativePluginStack!,
                            NativeAmbient(cell.Cell)), equippedArmor, selection, _nativeReferences.ActorAppearanceOverride(reference.FormKey));
                if (preparedNpc is not null) actor.BindSourceBehavior(_nativePluginStack!, selection);
                SynchronizeNativeNpcAppearance(actor, cell);
                actor.Transform = ReferenceTransform(reference);
                try { actor.ConfigureContactShapes(_configuration.Player.CollisionLayer); }
                catch (Exception error) when (error is InvalidDataException or NotSupportedException)
                {
                    actor.SetMeta("opennv_contact_divergence", error.Message);
                    GD.PushError($"OPENNV_NATIVE_NPC_CONTACT_DIVERGENCE reference={reference.FormKey}: {error.Message}");
                }
                actor.ConfigureHeadTracking(_nativePluginStack!, source, target =>
                {
                    if (_nativePluginStack!.RuntimeFormId(target) == 0x14)
                        return _nativePlayer?.Camera.GlobalPosition;
                    return (_nativeReferencePresentation?.Nodes.GetValueOrDefault(target) as RuntimeNativeNpc)?.HeadTargetPoint;
                });
                actor.ResolveDialogueTarget = target => _nativePluginStack!.RuntimeFormId(target) == 0x14 ?
                    _nativePlayer : _nativeReferencePresentation?.Nodes.GetValueOrDefault(target);
                actor.BeginPackageDialogue = (package, completed) => (_nativeOpeningStageDriver ??
                    throw new InvalidOperationException("Package dialogue has no gameplay owner."))
                    .RequestPackageDialogue(reference.FormKey, package, completed);
                if (_nativeOpeningStageDriver is not null)
                {
                    actor.PackageSpeechBusy = () => _nativeOpeningStageDriver.IsDialogueBusy(reference.FormKey);
                    actor.NpcDialogueActive = () => _nativeOpeningStageDriver.IsNpcDialogueActive(reference.FormKey);
                    actor.ExecutePackageEvent = (program, caller) => _nativeOpeningStageDriver.ExecutePackageEvent(program, caller);
                }
                actor.Combat = RuntimeNativeActorCombat.Attach(actor, actor.Skeleton, actor.Appearance.SkeletonPath,
                    _nativeReferences!, _nativeReferences!.Get(reference.FormKey), _nativePluginStack!, source,
                    _configuration.Player.CollisionLayer, _configuration.Player.CollisionMask | _configuration.Player.CollisionLayer, NativeCombatContext);
                actor.ConfigureAi(_nativePluginStack!, _nativeQuestState!, cell, ReferenceTransform,
                    () => _nativeReferences!.ActorFactions(reference.FormKey), _nativeGameTime, _nativeGlobals, _nativeReferences,
                    NativeActorItemCount);
                root.AddChild(actor);
                AddNativeReferenceEmittance(actor, reference);
                _nativeActorDivergences[reference.FormKey.ToString()] =
                    "animation-selection-blending, face-pose, gameplay, material-lighting-output parity unbound";
                Observe(parityScope, ParityIdentity(reference),
                    NativeReferenceState(reference, baseObject, "skinned-npc-presentation"));
                GD.Print($"OPENNV_NATIVE_NPC_READY reference={reference.FormKey} npc={reference.Base} " +
                    $"bones={actor.Skeleton.Node.GetBoneCount()} parts={actor.Parts.Count} " +
                    "animation=unresolved gameplay=unresolved parity=unmeasured");
            }
            catch (Exception error) when (error is InvalidDataException or NotSupportedException or FileNotFoundException)
            {
                _nativeActorDivergences[reference.FormKey.ToString()] = error.Message;
                GD.PushError($"OPENNV_NATIVE_NPC_DIVERGENCE reference={reference.FormKey}: {error.Message}");
            }
            return;
        }
        if (baseObject.Signature == "CREA")
        {
            try
            {
                var selection = _nativeReferences!.InitializeActorTemplates(reference.FormKey,
                    _nativeOpeningStageDriver?.PlayerLevel ?? _nativeOpeningRestore?.State.Vitals?.Level ?? 1, _nativeGlobals);
                if (selection.Absent)
                {
                    Observe(parityScope, ParityIdentity(reference), NativeReferenceState(reference, baseObject, "source-leveled-none"));
                    return;
                }
                var actor = RuntimeNativeCreature.Create(_nativePluginStack!, source, reference,
                    _nativeReferences!.Get(reference.FormKey), _configuration.World.GameUnitsToMeters);
                actor.Transform = ReferenceTransform(reference);
                try { RuntimeNativeActorContacts.Configure(actor, actor.Skeleton, _configuration.Player.CollisionLayer); }
                catch (Exception error) when (error is InvalidDataException or NotSupportedException)
                {
                    actor.SetMeta("opennv_contact_divergence", error.Message);
                    GD.PushError($"OPENNV_NATIVE_CREATURE_CONTACT_DIVERGENCE reference={reference.FormKey}: {error.Message}");
                }
                root.AddChild(actor);
                actor.Combat = RuntimeNativeActorCombat.Attach(actor, actor.Skeleton, actor.Appearance.SkeletonPath,
                    _nativeReferences!, _nativeReferences.Get(reference.FormKey), _nativePluginStack!, source,
                    _configuration.Player.CollisionLayer, _configuration.Player.CollisionMask | _configuration.Player.CollisionLayer, NativeCombatContext);
                actor.ConfigureAi(_nativePluginStack!, _nativeQuestState!, _nativeReferences, _nativeGameTime, _nativeGlobals,
                    NativeActorItemCount);
                actor.ExecutePackageEvent = (program, caller) => (_nativeOpeningStageDriver ??
                    throw new InvalidOperationException("Creature package results have no gameplay owner."))
                    .ExecutePackageEvent(program, caller);
                actor.BeginPackageDialogue = (package, completed) => (_nativeOpeningStageDriver ??
                    throw new InvalidOperationException("Creature dialogue has no gameplay owner."))
                    .RequestPackageDialogue(reference.FormKey, package, completed);
                AddNativeReferenceEmittance(actor, reference);
                _nativeActorDivergences[reference.FormKey.ToString()] = string.Join("; ", actor.Unbound);
                Observe(parityScope, ParityIdentity(reference), NativeReferenceState(reference, baseObject, "skinned-creature-presentation"));
                GD.Print($"OPENNV_NATIVE_CREATURE_READY reference={reference.FormKey} creature={reference.Base} " +
                    $"bones={actor.Skeleton.Node.GetBoneCount()} parts={actor.Parts.Count} idle=source-KF gameplay=unresolved parity=unmeasured");
            }
            catch (Exception error) when (error is InvalidDataException or NotSupportedException or FileNotFoundException)
            {
                _nativeActorDivergences[reference.FormKey.ToString()] = error.Message;
                GD.PushError($"OPENNV_NATIVE_CREATURE_DIVERGENCE reference={reference.FormKey}: {error.Message}");
            }
            return;
        }
        if (baseObject.ModelPath is null)
            return;
        if (!_nativeNifPrototypes.TryGetValue(baseObject.ModelPath, out var prototype))
        {
            if (!source.TryRead(baseObject.ModelPath, null, out var nif, out var nifSource))
                throw new FileNotFoundException(
                    $"Winning model {baseObject.ModelPath} for {baseObject.FormKey} is missing.");
            GD.Print(
                $"OPENNV_NATIVE_NIF_LOADING model={baseObject.ModelPath} source={nifSource} " +
                $"base={baseObject.FormKey}");
            if (baseObject.ModelPath.EndsWith(".spt", StringComparison.OrdinalIgnoreCase))
                throw new NotSupportedException($"Owned SpeedTree model {baseObject.ModelPath} is present at {nifSource}; its procedural geometry decoder is unbound.");
            prototype = preparedModel is null ? new RuntimeNativeNifPrototype(nif, _configuration.World.GameUnitsToMeters) :
                new RuntimeNativeNifPrototype(preparedModel, _configuration.World.GameUnitsToMeters);
            var built = prototype.Scene;
            prototype.Scene.Root.Name = $"Prototype_{baseObject.FormKey}";
            _nativeNifPrototypes.Add(baseObject.ModelPath, prototype);
            GD.Print(
                $"OPENNV_NATIVE_NIF_READY model={baseObject.ModelPath} source={nifSource} " +
                $"nodes={built.Nodes} surfaces={built.Surfaces} vertices={built.Vertices} triangles={built.Triangles} " +
                $"collisionBodies={built.CollisionBodies} collisionShapes={built.CollisionShapes} " +
                $"collisionTriangles={built.CollisionTriangles}");
        }
        var instance = prototype.InstantiatePlaced(ReferenceTransform(reference));
        instance.Name = $"Reference_{reference.FormKey}";
        instance.SetMeta("opennv_reference_form_key", reference.FormKey.ToString());
        instance.SetMeta("opennv_source_model", baseObject.ModelPath);
        instance.SetMeta("opennv_source_form", baseObject.FormKey.ToString());
        if (baseObject.Signature == "FURN" && FalloutFurnitureSource.ReadKind(_nativePluginStack!.GetEffective(baseObject.FormKey)) ==
            FalloutPlayerFurnitureKind.Sleeping)
        {
            try { RuntimeNativeRestFurniturePublication.Attach(_nativePluginStack!, reference, prototype.Source, instance); }
            catch (Exception original)
            {
                try { instance.Free(); }
                catch (Exception retirement)
                { throw new AggregateException("Source bed factory and native retirement both failed.", original, retirement); }
                throw;
            }
        }
        root.AddChild(instance);
        RuntimeNativeDestructible.Attach(instance, _nativeReferences!.Get(reference.FormKey), _nativePluginStack!, source,
            _configuration.World.GameUnitsToMeters, _configuration.Player.CollisionMask | _configuration.Player.CollisionLayer, NativeCombatContext);
        if (cell.Cell.Worldspace is not null && (baseObject.Signature is "STAT" or "SCOL" or "TREE") &&
            (reference.Flags & 0x00008000) != 0)
            NativeExteriorDetailBlend.Bind(instance, terrain: false);
        AddNativeReferenceEmittance(instance, reference);
        var controllers = instance.FindChildren("*", "", true, false).OfType<RuntimeNifControllerPlayer>().ToArray();
        if (controllers.Any(controller => controller.HasTextKeys))
        {
            var sounds = new NativeOwnedAnimationSoundPlayer(_nativePluginStack!, source, instance,
                _configuration.World.GameUnitsToMeters, _nativeReferences!.Get(reference.FormKey).SoundRandom,
                _nativeReferences.Get(reference.FormKey).AnimationSoundEvents);
            instance.AddChild(sounds);
            foreach (var controller in controllers.Where(controller => controller.HasTextKeys))
                controller.TextKeyHandler = sounds.Dispatch;
        }
        if (controllers.Length != 0)
            GD.Print($"OPENNV_NATIVE_REFERENCE_CONTROLLERS source={reference.FormKey} " +
                $"sequences={string.Join(',', controllers.SelectMany(controller => controller.SequenceNames))} binding=per-instance");
        Observe(
            parityScope,
            ParityIdentity(reference),
            NativeReferenceState(reference, baseObject, "model"));
        if (baseObject.Signature == "DOOR" && reference.Teleport is not null)
            AddNativeDoorPortal(instance, reference);
        else if (baseObject.Signature == "DOOR" || RuntimeNativeDoorMotion.HasOpenClose(controllers))
            RuntimeNativeDoorMotion.Attach(instance, _nativeReferences!.Get(reference.FormKey), controllers, () => RequestNativeInteractionSave(reference.FormKey));
    }

    private static ParityCategory ParityCategoryFor(string signature) => signature switch
    {
        "NPC_" or "CREA" => ParityCategory.Actor,
        "LIGH" => ParityCategory.Renderer,
        "SOUN" => ParityCategory.Audio,
        _ => ParityCategory.World,
    };

    private static byte[] NativeReferenceState(
        FalloutPlacedReference reference,
        FalloutBaseObjectDefinition baseObject,
        string disposition) => NativeReferenceObservation.Serialize(reference, baseObject, disposition);

    private void AddNativeDoorPortal(
        Node3D doorInstance,
        FalloutPlacedReference reference)
    {
        var destination = _nativePluginStack!.GetEffective(reference.Teleport!.Door);
        var destinationCell = FalloutCellSceneReader.ParentCell(destination) ?? throw new InvalidDataException("XTEL destination has no CELL.");
        var destinationWorld = FalloutCellSceneReader.ParentWorldspace(destination);
        var portal = new RuntimeNativeDoorPortal();
        portal.Configure(
            reference.FormKey,
            destination.FormKey,
            destinationCell,
            destinationWorld,
            () => RequestNativeDoorTransition(reference));
        doorInstance.AddChild(portal);
    }

    private bool _nativeDoorLoading;
    private void RequestNativeDoorTransition(FalloutPlacedReference reference)
    {
        if (_nativeDoorLoading || _nativeSessionTransitioning || _retiringNativeSession) return;
        _nativeDoorLoading = true;
        var player = _nativePlayer!;
        var previousModal = player.ModalInput;
        player.SetModalInput(true);
        // Activation queues streaming. A presentation failure must not become a
        // permanent source-script fault or consume the reciprocal door's state.
        _nativeDoorRead = ConsumeNativeDoorTransition(reference, player, previousModal);
    }

    private async Task ConsumeNativeDoorTransition(FalloutPlacedReference reference,
        RuntimeNativePlayer player, bool previousModal)
    {
        try
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (_nativeSessionTransitioning || _retiringNativeSession) throw new OperationCanceledException("Door session retired.");
            await StreamNativeDoorTransition(reference);
        }
        catch (Exception error)
        {
            SetMeta("opennv_door_stream_error", error.Message);
            GD.PushError($"OPENNV_NATIVE_DOOR_STREAM_FAIL {error}");
        }
        finally { _nativeDoorLoading = false; player.SetModalInput(previousModal); }
    }

    private async System.Threading.Tasks.Task StreamNativeDoorTransition(
        FalloutPlacedReference reference)
    {
        var current = _nativeCurrentCellRoot ??
            throw new InvalidOperationException("Native door activation has no current CELL root.");
        var active = _nativeActiveCell ??
            throw new InvalidOperationException("Native door activation has no authoritative CELL state.");
        if (!active.References.Any(value => value.FormKey == reference.FormKey))
            throw new InvalidOperationException(
                $"Native door activation has mismatched source CELL {active.Cell.FormKey}.");
        var transition = FalloutDoorDestinationResolver.Resolve(_nativePluginStack!, reference);
        var entry = reference.Teleport!;
        _ = _nativePlayer ??
            throw new InvalidOperationException("Native door activation has no authoritative player.");
        var sky = _nativeSkyLighting ?? throw new InvalidOperationException("Door transition has no sky owner.");
        var previousSky = sky.Capture();
        var grid = transition.DestinationScene.Cell.Worldspace is { } world ? ResolveExterior(world, entry.Position) : null;
        var targetScene = grid?.Scene ?? transition.DestinationScene;
        var followers = BeginFollowerDoorTransfer(current, targetScene.Cell.FormKey, entry);
        Node3D? targetRoot = null;
        (CollisionObject3D Body, CollisionObject3D.DisableModeEnum Mode)[] arrivalCollision = [];
        try
        {
            await ShowNativeLoadingScreens(targetScene.Cell.FormKey);
            targetScene = _nativeReferences!.ComposeResidency(targetScene, grid?.Cells);
            if (grid is not null) grid = grid with { Scene = targetScene };
            sky.EnterCell(targetScene.Cell, _nativeGlobals, entry.Position);
            SetLoadingStatus("Loading the world");
            targetRoot = await BuildNativeCellRootResponsive(targetScene, null, sourceSide: false,
                sourceCells: grid is null ? null : grid.Cells.Select(definition => definition.FormKey).Append(grid.PersistentCell).Distinct(FalloutFormKeyComparer.Instance).ToArray());
            targetRoot.ProcessMode = ProcessModeEnum.Disabled;
            if (grid is not null) AddExteriorLandscape(targetRoot, grid);
            AddChild(targetRoot);
            if (targetScene.Cell.Lighting is not null) AddNativeCellEnvironment(targetRoot, targetScene);
            else AddExteriorEnvironment(targetRoot, targetScene.Cell);
            if (followers.Count != 0)
            {
                current.ProcessMode = ProcessModeEnum.Disabled;
                foreach (var actor in targetRoot.FindChildren("*", "", true, false).OfType<RuntimeNativeCreature>()
                    .Where(actor => followers.Any(follower => follower.Reference == actor.Appearance.Reference)))
                    actor.Combat!.PreparePortalArrival();
                // Gameplay remains stopped while arrival is checked. Godot's
                // default DisableMode removes bodies from physics as well, so
                // explicitly retain destination collision during this query.
                arrivalCollision = targetRoot.FindChildren("*", "", true, false).OfType<CollisionObject3D>()
                    .Select(body => (body, body.DisableMode)).ToArray();
                foreach (var (body, _) in arrivalCollision) body.DisableMode = CollisionObject3D.DisableModeEnum.KeepActive;
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                PlaceDoorFollowers(targetRoot, targetScene, grid?.Cells, entry, followers);
            }
            var arrival = TeleportTransform(entry);
            if (grid is not null)
            {
                SetLoadingStatus("Preparing the distant world");
                await targetRoot.GetChildren().OfType<RuntimeNativeExteriorLod>().Single().PrepareInitialSelection(arrival.Origin);
            }
            CommitNativeWorldTransfer(current, active, targetRoot, targetScene, arrival);
        }
        catch
        {
            FreeNativeSourceCellRoot(targetRoot);
            sky.Restore(previousSky);
            RollbackFollowerDoorTransfer(followers);
            current.ProcessMode = ProcessModeEnum.Inherit;
            DiscoverNativeCellReferences(active);
            ObserveNativeResidentReferences(active);
            throw;
        }
        finally { CloseNativeLoadingScreens(); }
        foreach (var (body, mode) in arrivalCollision) body.DisableMode = mode;
        RequestNativeDoorTransportSave(active.Cell.FormKey, reference.FormKey, entry.Door);
        GD.Print(
            $"OPENNV_NATIVE_DOOR_STREAM source={active.Cell.FormKey} destination={targetScene.Cell.FormKey} " +
            $"door={reference.FormKey} " +
            $"entry={entry.Door} world={targetScene.Cell.Worldspace} " +
            "source=live-retail-files");
    }

    private void SetNativeActiveCell(Node3D root, FalloutCellScene cell)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(cell);
        RequireNativeSourceCellSelection(root, cell);
        if (_nativeReferences is { } references)
        {
            if (_nativeActiveCell?.Cell.FormKey != cell.Cell.FormKey) _nativeReferenceEvents?.SetProcess(false);
            if (!references.IsCellResident(cell.Cell.FormKey)) references.LoadCell(cell);
            if (_nativeActiveCell is { } previous && previous.Cell.FormKey != cell.Cell.FormKey &&
                references.IsCellResident(previous.Cell.FormKey)) references.UnloadCell(previous.Cell.FormKey);
        }
        _nativeCurrentCellRoot = root;
        _nativeActiveCell = cell;
        _nativeWalkableGrid.Clear();
        foreach (var land in root.GetChildren().OfType<RuntimeNativeLandscapeTransport>()) _nativeWalkableGrid.Add(land.Source.ActiveCoordinates);
        if (_nativePlayer is not null) BindNativeReferenceEvents(root, cell);
    }

    private void AddNativeReferenceEmittance(Node3D instance, FalloutPlacedReference reference)
    {
        if (reference.Emittance is not { } emittance) return;
        var source = new FalloutExternalEmittance(_nativePluginStack!, emittance,
            region => (_nativeSkyLighting ?? throw new InvalidOperationException("Sky lighting state is absent."))
                .RegionEmittance(region, (_nativeGameTime ?? throw new InvalidOperationException("Sky has no simulation clock.")).Hour));
        var binding = new RuntimeNativeReferenceEmittance { Name = "NativeMaterialEmittance" };
        binding.Configure(source.Sample);
        binding.SetMeta("opennv_material_emittance_source", emittance.ToString());
        instance.AddChild(binding);
    }

    private void AddNativePlacedLight(
        Node3D root,
        FalloutPlacedReference reference,
        FalloutBaseObjectDefinition baseObject)
    {
        var light = RuntimeNativePlacedLightBuilder.Build(
            reference,
            baseObject,
            ReferenceTransform(reference),
            _configuration.World.GameUnitsToMeters,
            _configuration.Renderer.PointLightEnergyScale,
            _configuration.Renderer.MinimumPointLightEnergy,
            _configuration.Renderer.AuthoredPointLightShadows,
            _nativePluginStack,
            region => (_nativeSkyLighting ?? throw new InvalidOperationException("Sky lighting state is absent."))
                .RegionEmittance(region, (_nativeGameTime ?? throw new InvalidOperationException("Sky has no simulation clock.")).Hour));
        light.SetMeta("opennv_reference_form_key", reference.FormKey.ToString());
        root.AddChild(light);
    }

    private void AddNativePlayer(FalloutCellScene initialCell, FalloutReferencePlacement? startupPlacement = null)
    {
        if (_nativePlayer is not null)
            throw new InvalidOperationException("Native player was already created.");
        if (!_nativeContinueOpening && startupPlacement is null)
            throw new InvalidOperationException("New Game player has no admitted source placement.");
        var transform = startupPlacement is not null ? NativePlayerPlacementTransform(startupPlacement) : Transform3D.Identity;
        _nativePlayer = new RuntimeNativePlayer();
        _nativePlayer.ActivateReference = collider => _nativeReferenceEvents?.TryActivate(collider) == true;
        _nativePlayer.NoActivationFeedback = () =>
            (_nativeQuestScripts ?? throw new InvalidOperationException("Native activation feedback has no quest session."))
                .Scripts.NoActivationSound.RejectActivation(_nativePluginStack!.RuntimeFormKey(0x14));
        _nativePlayer.SaveGame = SaveNativeManualSlot;
        _nativePlayer.OpenPauseMenu += ToggleNativeSessionMenu;
        _nativePlayer.Configure(_configuration, transform);
        _nativePlayer.ConfigureInputControls(_nativeScriptStorage?.Controls ??
            throw new InvalidOperationException("Native input has no profile control owner."),
            key => _nativeQuestScripts?.Scripts.Events.IsKeyPressed(key) == true);
        _nativePlayer.ConfigureLocomotion(_nativePluginStack!, () => _nativeOpeningStageDriver?.Vitals);
        _nativePlayer.ConfigurePresentation(_nativePluginStack!, _nativeInventory,
            () => _nativeOpeningStageDriver!.PlayerAppearance, () => NativeAmbient(_nativeActiveCell!.Cell),
            _nativeContinueOpening ? _nativeOpeningRestore?.State.WeaponHandling : null,
            () => _nativeOpeningStageDriver!.PlayerAppearanceRevision,
            () => (_nativeQuestScripts ?? throw new InvalidOperationException("Player presentation has no shared script session.")).Scripts.Session);
        _nativePlayer.ConfigureCombat(_nativePluginStack!, _nativeGlobals!,
            () => _nativeOpeningStageDriver!.PlayerLevel, value => _nativeOpeningStageDriver!.PlayerCombatValue(value),
            () => _nativeOpeningStageDriver!.PlayerPerkEntries,
            condition => _nativeOpeningStageDriver!.EvaluateRecipeCondition(condition),
            (damage, part) => _nativeOpeningStageDriver!.DamagePlayer(damage, part));
        _nativePlayer.ConfigureHitEvents(_nativeReferences!, collider => _nativeReferenceEvents?.CollisionReference(collider));
        _nativePlayer.CanOccupyPosition = NativeCollisionResident;
        _nativePlayer.IsDefeated = () => _nativeOpeningStageDriver?.Vitals.HitPoints == 0;
        var restore = _nativeContinueOpening
            ? _nativeOpeningRestore ?? throw new InvalidOperationException(
                "Native Continue was selected without a valid cold save.")
            : null;
        if (restore is not null)
            _nativePlayer.RestoreTransform(
                FalloutNativeCampaignSave.RestorePlayerPosition(restore.State),
                restore.State.PlayerRotation, FalloutNativeCampaignSave.RestorePlayerViewPitch(restore.State));
        AddChild(_nativePlayer);
        if (_nativeXr is not null) _nativePlayer.AttachXr(_nativeXr);
        _nativeSubtitles = new(_nativePluginStack!, () => _nativeOpeningStageDriver?.Subtitle,
            () => _nativeXr is null && !_nativeDoorLoading && !GetTree().Paused, _nativeUi);
        _nativeOpeningStageDriver = new RuntimeNativeOpeningStageDriver
        {
            PrepareSubtitle = _nativeSubtitles.Prepare,
            SourceManualSaveWriter = CreateNativeCheckpoint,
            SourceManualSaveBlocker = NativeSourceManualSaveBlocker,
            OrderedSaveQueueChanged = PumpNativeOrderedManualSave,
            ReferencePresentation = () => _nativeReferencePresentation ??
                throw new InvalidOperationException("Native reference presentation is absent."),
            SayToCompleted = receipt => _nativeOpeningStageDriver!.DispatchSpeechCompletion(receipt),
        };
        _nativeOpeningStageDriver.Configure(
            _nativeOpeningTransitions ??
                throw new InvalidOperationException("Native opening transition graph was not resolved."),
            _nativeOpeningControls ??
                throw new InvalidOperationException("Native opening control graph was not resolved."),
            _nativePlayer,
            _nativeRaceSexContract ??
                throw new InvalidOperationException("Native race/sex contract was not resolved."),
            _nativePluginStack ??
                throw new InvalidOperationException("Native plugin stack was not indexed."),
            RequireOption(_options, "save-path"),
            RuntimeLiveContentSource.Current?.SaveCompatibilityId ??
                throw new InvalidOperationException("Native save compatibility identity is absent."),
            initialCell.Cell.FormKey,
            restore,
            _configuration.ActorCompiler.FaceGenAnimation.Lip,
            _nativeImageSpaceState,
            _nativeQuestState!,
            _nativeQuestScripts?.Scripts ?? throw new InvalidOperationException("Native quest script owner is absent."),
            _nativeInventory,
            () => _nativeQuestScripts?.Capture(),
            _nativeGlobals,
            _nativeGameTime,
            _nativeSkyLighting,
            () => _nativeCurrentCellRoot?.GetChildren().OfType<RuntimeNativeImageSpace>().SingleOrDefault() ??
                throw new InvalidOperationException("Rendered creation has no world image-space owner."),
            _nativeStartingQuest?.ReadSubrecords().Where(field => field.Signature == "EDID")
                .Select(field => FalloutDialogueTopic.Text(field.Data.Span)).Single() ??
                throw new NotSupportedException("Configured startup quest has no source identity for the current gameplay owner."),
            0, _nativeBootstrap?.Controls, restore is null ? _nativeBootstrap?.CaptureStageResults() : null,
            restore is null ? _nativeBootstrapCharacter : null);
        AddChild(_nativeOpeningStageDriver);
        _nativeOpeningStageDriver.AttachExperiencePauseClock();
        AttachCurrentNativePlayerCell();
        _nativeOpeningStageDriver.ConfigureCurrentPlayerAdvancement();
        _nativeOpeningStageDriver.ConfigureCurrentPlayerPhysicalActivity(restore is null ? null :
            restore.State.PlayerPhysical ?? throw new InvalidDataException("Current campaign has no player physical continuation."),
            reference => (_nativeReferenceEvents ?? throw new NotSupportedException("Player furniture has no resident presentation owner."))
                .PlayerFurniturePlacement(reference));
        _nativeOpeningStageDriver.ConfigureCurrentCampaignRest(_nativeGameTimeAdapter ??
            throw new NotSupportedException("Rest has no actual native world clock."), () => CreateNativeSaveSite(), restore?.State);
        _nativePlayer.PublishRequiredPlayerPhysicalBody();
        // This owner is part of the load transaction. Native bridge dispatch
        // logs _Ready exceptions without propagating them to that transaction.
        _nativeOpeningStageDriver.InitializeOwnedState();
        _nativeOpeningStageDriver.PublishPendingSourceRestMenu();
        _nativeOpeningStageDriver.AttachInterfaceActivationFrames();
        _nativeOpeningStageDriver.AttachSourceCombatGroups();
        AttachNativeExperienceHud(_nativeOpeningStageDriver);
        _nativeImageSpaceClock = new(_nativeImageSpaceState);
        AddChild(_nativeImageSpaceClock);
        GD.Print(
            $"OPENNV_NATIVE_PLAYER_START reference={_nativeReferences!.PlayerMoves.Next?.Destination} " +
            $"startupQuest={_nativeStartingQuest!.FormKey} startupStage={_nativeQuestState!.Stage(_nativeStartingQuest.FormKey)} " +
            $"restored={(restore is not null)} inventory={restore?.Inventory.Items.Count ?? 0} " +
            "owner=character-body controls=actual-shared-result-effects source=live-retail-files");
    }

    private void AddNativeCellEnvironment(Node3D root, FalloutCellScene cell)
    {
        var lighting = cell.Cell.Lighting ??
            throw new InvalidDataException(
                $"Native CELL {cell.Cell.FormKey} has no resolved XCLL/LGTM lighting.");
        var materialEnvironment = new RuntimeNativeCellLighting { Name = "NativeMaterialEnvironment" };
        materialEnvironment.Configure(lighting, _configuration.World.GameUnitsToMeters);
        materialEnvironment.SetMeta("opennv_cell_lighting_source", cell.Cell.FormKey.ToString());
        root.AddChild(materialEnvironment);
        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = ByteColor(lighting.FogRgb),
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = ByteColor(lighting.AmbientRgb),
            AmbientLightEnergy = _configuration.Renderer.AmbientEnergyScale,
            TonemapMode = RuntimeRendering.ParseToneMapper(_configuration.Renderer.ToneMapper),
            FogEnabled = true,
            FogMode = Godot.Environment.FogModeEnum.Depth,
            FogLightColor = ByteColor(lighting.FogRgb),
            FogLightEnergy = _configuration.Renderer.FogLightEnergy,
            FogDensity = _configuration.Renderer.FogDensity,
            FogDepthBegin = lighting.FogNear * _configuration.World.GameUnitsToMeters,
            FogDepthEnd = lighting.FogFar * _configuration.World.GameUnitsToMeters,
            FogDepthCurve = lighting.FogPower,
        };
        var world = new WorldEnvironment
        {
            Name = $"NativeEnvironment_{cell.Cell.FormKey}",
            Environment = environment,
        };
        world.Compositor = AddNativeImageSpace(root, FalloutImageSpaceReader.ForCell(_nativePluginStack!, cell.Cell.FormKey));
        root.AddChild(world);
        var surfaceToLight = RetailLighting.SurfaceToLightFromXcllDegrees(
            lighting.DirectionalXDegrees,
            lighting.DirectionalZDegrees);
        root.AddChild(new DirectionalLight3D
        {
            Name = $"NativeDirectional_{cell.Cell.FormKey}",
            Transform = new Transform3D(
                RetailLighting.DirectionalLightBasis(surfaceToLight), Vector3.Zero),
            LightColor = RetailLighting.GodotLightColor(ByteColor(lighting.DirectionalRgb)),
            LightEnergy = lighting.DirectionalFade * _configuration.Renderer.DirectionalEnergyScale,
            ShadowEnabled = _configuration.ActorReview.DirectionalShadows,
        });
    }

    private Compositor? AddNativeImageSpace(Node3D root, FalloutImageSpace? imageSpace)
    {
        if (imageSpace is null) return null;
        var application = RetailImageSpaceRenderer.CreateFromSource(imageSpace,
            _configuration.FalloutEnvironment.ImageSpace, _configuration.Capture,
            _configuration.ActorCompiler.FaceGenMaterial.RuntimeAlbedoTransfer);
        var presenter = new RuntimeNativeImageSpace();
        presenter.Configure(imageSpace, _nativeImageSpaceState, application.Effect, _nativeGameTime);
        root.AddChild(presenter);
        root.SetMeta("opennv_source_image_space", imageSpace.Form.ToString());
        root.SetMeta("opennv_source_image_space_version", imageSpace.FormVersion);
        root.SetMeta("opennv_source_image_space_dnam_sha256", imageSpace.DnamSha256);
        root.SetMeta("opennv_image_space_cinematic", application.Cinematic);
        root.SetMeta("opennv_image_space_tint", application.Tint);
        GD.Print($"OPENNV_NATIVE_IMAGE_SPACE source={imageSpace.Form} version={imageSpace.FormVersion} " +
            $"cinematic={application.Cinematic} tint={application.Tint} " +
            "adaptation=runtime hdrParameterCoverage=partial depthOfField=unbound parity=unmeasured");
        return application.Compositor;
    }

    private Transform3D ReferenceTransform(FalloutPlacedReference reference) => new(
        GamebryoCoordinate.ConvertReferenceEuler(
            new Vector3(reference.RotationRadians[0], reference.RotationRadians[1], reference.RotationRadians[2]),
            reference.Scale),
        GamebryoCoordinate.ConvertVector(
            new Vector3(reference.Position[0], reference.Position[1], reference.Position[2])) *
        _configuration.World.GameUnitsToMeters);

    private Transform3D TeleportTransform(FalloutTeleportDestination destination) => new(
        GamebryoCoordinate.ConvertReferenceEuler(
            new Vector3(
                destination.RotationRadians[0],
                destination.RotationRadians[1],
                destination.RotationRadians[2]),
            1.0f),
        GamebryoCoordinate.ConvertVector(new Vector3(
            destination.Position[0], destination.Position[1], destination.Position[2])) *
        _configuration.World.GameUnitsToMeters);

    private static Color ByteColor(IReadOnlyList<byte> rgb) => new(
        rgb[0] / (float)byte.MaxValue,
        rgb[1] / (float)byte.MaxValue,
        rgb[2] / (float)byte.MaxValue);
}
