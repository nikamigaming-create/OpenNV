using System.Text.Json;
using System.Diagnostics;
using System.Text;
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
    private const string NativeNewVegasInitialCellPlugin = "FalloutNV" + ".esm";
    private const string NativeFallout3InitialCellPlugin = "Fallout3" + ".esm";
    private const uint NativeFallout3InitialCellObjectId = 0x28138;
    private const uint NativeFallout3PlayerStartObjectId = 0x39562;
    private const int NativeMenuCanvasLayer = 100;
    private FalloutPluginStack? _nativePluginStack;
    private FalloutQuestState? _nativeQuestState;
    private FalloutReferenceWorld? _nativeReferences;
    private RuntimeNativeReferenceEvents? _nativeReferenceEvents;
    private RuntimeNativeQuestScripts? _nativeQuestScripts;
    private FalloutGlobalState? _nativeGlobals;
    private FalloutGameTime? _nativeGameTime;
    private FalloutSkyLightingState? _nativeSkyLighting;
    private RuntimeNativeGameTime? _nativeGameTimeAdapter;
    private string? _nativeGameTimeUnbound;
    private readonly FalloutPlayerInventory _nativeInventory = new();
    private FalloutCellScene? _nativeInitialCell;
    private FalloutCellScene? _nativeActiveCell;
    private Node3D? _nativeCurrentCellRoot;
    private readonly Dictionary<string, RuntimeNativeNifPrototype> _nativeNifPrototypes =
        new(StringComparer.OrdinalIgnoreCase);
    private Node3D? _nativePrewarmedInitialCellRoot;
    private RuntimeNativePlayer? _nativePlayer;
    private FalloutOpeningControlGraph? _nativeOpeningControls;
    private FalloutOpeningStageTransitionGraph? _nativeOpeningTransitions;
    private FalloutOpeningInventoryGrant? _nativeOpeningGrant;
    private FalloutNativeRaceSexContract? _nativeRaceSexContract;
    private FalloutNativeVigorContract? _nativeVigorContract;
    private FalloutNativeTagSkillContract? _nativeTagSkillContract;
    private FalloutNativeTraitFarewellContract? _nativeTraitFarewellContract;
    private FalloutNativeCampaignRestore? _nativeOpeningRestore;
    private RuntimeNativeOpeningStageDriver? _nativeOpeningStageDriver;
    private bool _nativeContinueOpening;
    private readonly FalloutImageSpaceState _nativeImageSpaceState = new();
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
            xr = _nativeXr?.State,
            detail = detailed ? "complete-runtime-snapshot" : "live-summary;request-state-for-reference-and-controller-details",
            reviewScope = !detailed || _nativeActiveCell is null ? null : new
            {
                sourceCompatibilityId = RuntimeLiveContentSource.Current!.SaveCompatibilityId,
                runtimeBuild = typeof(RuntimeCoordinator).Assembly.ManifestModule.ModuleVersionId,
                capturedUtc = DateTime.UtcNow,
                references = _nativeActiveCell.References.Select(reference => reference.FormKey.ToString()).ToArray(),
            },
            referenceEvents = _nativeReferenceEvents?.State,
            references = _nativeReferences is null ? null : new
            {
                _nativeReferences.InstanceCount,
                _nativeReferences.ResidentCellCount,
                _nativeReferences.ScriptDefinitionCount,
                state = detailed ? _nativeReferences.Capture() : null
            },
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
                blockingShape = _nativePlayer.BlockingShape,
                collisionResident = _nativePlayer.CollisionResident,
                collisionContacts = _nativePlayer.CollisionContacts,
                modalInput = _nativePlayer.ModalInput,
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
            },
            movies = _nativeOpeningStageDriver?.GetChildren().OfType<NativeGamebryoMovie>()
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
            mapMarkers = _nativeReferences?.KnownMapMarkers.Select(marker => new
            {
                reference = marker.Source.Reference.ToString(),
                marker.Source.Name,
                marker.Source.Type,
                marker.State.Visible,
                marker.State.CanTravel,
            }).ToArray(),
            speech = _nativeOpeningStageDriver?.SpeechState,
            conversation = _nativeOpeningStageDriver?.ConversationState,
            questScripts = _nativeQuestScripts?.State,
            gameTime = _nativeGameTimeAdapter?.State,
            gameTimeUnbound = _nativeGameTimeUnbound,
            skyLighting = _nativeSkyLighting?.Unbound is null ? _nativeSkyLighting?.Capture() : null,
            skyLightingUnbound = _nativeSkyLighting?.Unbound,
            playerInventory = _nativeInventory.Items,
            gameplayHud = _nativeGameplayHud?.State,
            hudMessages = _nativeHudMessages?.State,
            pipBoy = _nativePipBoy?.State,
            playerPresentation = _nativePlayer?.PresentationState,
            questScriptsUnbound = _nativeContinueOpening && _nativeOpeningRestore?.State.Scripts is null ? "Legacy save has no quest script state." : null,
            playerPackage = _nativeOpeningStageDriver?.PlayerPackageState,
            characterCreation = _nativeOpeningStageDriver?.CharacterCreationState,
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

    private void LoadNativeLiveStack()
    {
        var source = RuntimeLiveContentSource.Current ??
            throw new InvalidOperationException("Live retail source was not configured.");
        SetLoadingStatus("INDEXING LIVE PLUGINS");
        if (DisplayServer.GetName() == "headless" || _options.ContainsKey("new-game"))
        {
            IndexNativeLiveStack(source.PluginSources);
            if (_options.ContainsKey("new-game"))
                LoadNativeInitialCell();
            if (DisplayServer.GetName() == "headless")
                GetTree().Quit(0);
            return;
        }
        ShowNativeLiveMenu(source.PluginSources);
        DismissLoadingScreen();
    }

    private void IndexNativeLiveStack(IReadOnlyList<FalloutPluginSource> sources)
    {
        var content = RuntimeLiveContentSource.Current ??
            throw new InvalidOperationException("Live retail source was not configured.");
        _nativePluginStack = FalloutPluginStack.Load(sources, out var loadMetrics);
        FalloutAddonNodes.Bind(content, _nativePluginStack);
        _nativeQuestState = new(_nativePluginStack, _nativeInventory.Notifications);
        _nativeReferences?.Dispose();
        _nativeReferences = new(_nativePluginStack);
        var initialCell = content.Campaign == RuntimeLiveContentSource.Fallout3Game
            ? new FalloutFormKey(NativeFallout3InitialCellPlugin, NativeFallout3InitialCellObjectId)
            : new FalloutFormKey(NativeNewVegasInitialCellPlugin, 0x103df9);
        _nativeInitialCell = FalloutCellSceneReader.Read(
            _nativePluginStack,
            initialCell);
        if (content.Campaign == RuntimeLiveContentSource.FalloutNewVegasGame)
        {
            _nativeGlobals = FalloutGlobalState.Read(_nativePluginStack);
            _nativeGameTime = new(_nativeGlobals, FalloutGameTimeBindings.Read(_nativePluginStack),
                FalloutCalendar.Read(Path.Combine(Path.GetDirectoryName(content.ContentRoot)!, "FalloutNV.exe")));
            _nativeSkyLighting = new(_nativePluginStack, FalloutGameSettingFloats.Read(_nativePluginStack, "fDaytimeColorExtension"));
            _nativeOpeningControls = FalloutOpeningPlayerControlResolver.Resolve(
                _nativePluginStack,
                ["VCG00", "VCG01"]);
            _nativeOpeningGrant = FalloutOpeningInventoryGrantResolver.Resolve(
                _nativePluginStack,
                _nativeOpeningControls,
                "VCG01");
            _nativeRaceSexContract = FalloutNativeRaceSexResolver.Resolve(_nativePluginStack);
            _nativeVigorContract = FalloutNativeVigorResolver.Resolve(
                _nativePluginStack,
                _nativeInitialCell);
            _nativeOpeningTransitions = FalloutOpeningStageTransitionResolver.Resolve(
                _nativePluginStack,
                _nativeOpeningControls, executeGameMode: true);
            _nativeOpeningTransitions = FalloutOpeningStageTransitionResolver.AddDialogueWaits(
                _nativeOpeningControls, _nativeOpeningTransitions);
            _nativeTagSkillContract = FalloutNativeTagSkillResolver.Resolve(
                _nativePluginStack, _nativeOpeningControls);
            _nativeTraitFarewellContract = FalloutNativeTraitFarewellResolver.Resolve(
                _nativePluginStack,
                _nativeOpeningControls,
                _nativeInitialCell);
            var savePath = Path.GetFullPath(RequireOption(_options, "save-path"));
            _nativeOpeningRestore = null;
            if (File.Exists(savePath))
            {
                try
                {
                    _nativeOpeningRestore = FalloutNativeCampaignSave.Read(
                        savePath,
                        content.SaveCompatibilityId,
                        _nativePluginStack,
                        _nativeVigorContract ?? throw new InvalidOperationException(
                            "Native Vigor contract was not resolved."),
                        _nativeTagSkillContract ?? throw new InvalidOperationException(
                            "Native tag-skill contract was not resolved."),
                        _nativeOpeningGrant ?? throw new InvalidOperationException(
                            "Native opening inventory grant was not resolved."),
                        _nativeTraitFarewellContract ?? throw new InvalidOperationException(
                            "Native trait/farewell contract was not resolved."));
                }
                catch (Exception exception) when (
                    exception is IOException or InvalidDataException or JsonException or NotSupportedException)
                {
                    GD.PushWarning($"OPENNV_NATIVE_CONTINUE_REJECTED {exception.Message}");
                }
            }
        }
        var archiveWarmupWait = Stopwatch.StartNew();
        content.ArchiveWarmup.GetAwaiter().GetResult();
        archiveWarmupWait.Stop();
        GD.Print(
            $"OPENNV_NATIVE_STACK_READY edition={content.Edition} campaign={content.Campaign} " +
            $"game={content.Game} plugins={_nativePluginStack.Plugins.Count} " +
            $"records={_nativePluginStack.EffectiveRecordCount} cell={_nativeInitialCell.Cell.FormKey} " +
            $"references={_nativeInitialCell.References.Count} " +
            $"models={_nativeInitialCell.BaseObjects.Values.Count(value => value.ModelPath is not null)} " +
            $"pluginOpenMs={loadMetrics.PluginHeaderScan.TotalMilliseconds:F1} " +
            $"winnerIndexMs={loadMetrics.WinnerConstruction.TotalMilliseconds:F1} " +
            $"archiveWinnerWaitMs={archiveWarmupWait.Elapsed.TotalMilliseconds:F1}");
    }

    private async void IndexNativeLiveStackForMenu(
        IReadOnlyList<FalloutPluginSource> sources,
        NativeGamebryoStartMenu menu)
    {
        try
        {
            await Task.Run(() => IndexNativeLiveStack(sources));
            var initialCell = _nativeInitialCell ??
                throw new InvalidOperationException("Native initial CELL was not decoded.");
            var stack = _nativePluginStack ??
                throw new InvalidOperationException("Native plugin stack was not indexed.");
            var transition = RuntimeLiveContentSource.Current?.Campaign ==
                RuntimeLiveContentSource.FalloutNewVegasGame
                ? FalloutDoorTransitionResolver.ResolveInteriorExits(stack, initialCell).Single()
                : null;
            // Continue does not need an unused new-game house and actor build.
            if (_nativeOpeningRestore is null)
                _nativePrewarmedInitialCellRoot = BuildNativeCellRoot(initialCell, transition, sourceSide: true);
            if (_nativeOpeningControls is not null) CreateNativeQuestScripts();
            menu.SetReady(stack, _nativeOpeningRestore is not null);
        }
        catch (Exception exception)
        {
            GD.PushError($"OPENNV_NATIVE_STACK_FAIL {exception}");
            GetTree().Quit(1);
        }
    }

    private void ShowNativeLiveMenu(IReadOnlyList<FalloutPluginSource> sources)
    {
        var layer = new CanvasLayer { Name = "NativeLiveMenu", Layer = NativeMenuCanvasLayer };
        var menu = new NativeGamebryoStartMenu(action =>
        {
            if (action == "sQuit")
                GetTree().Quit();
            else if (action is "sNew" or "sContinue")
            {
                _nativeContinueOpening = action == "sContinue";
                layer.QueueFree();
                Callable.From(LoadNativeInitialCell).CallDeferred();
            }
            else
            {
                SetMeta("opennv_ui_divergence", $"StartMenu action has no retail-equivalent owner: {action}");
                GD.PushError($"OPENNV_UI_DIVERGENCE menu=StartMenu action={action} owner=missing");
            }
        });
        layer.AddChild(menu);
        AddChild(layer);
        IndexNativeLiveStackForMenu(sources, menu);
    }

    private void LoadNativeInitialCell()
    {
        _ = RuntimeLiveContentSource.Current ??
            throw new InvalidOperationException("Live retail source was cleared during startup.");
        var cell = _nativeInitialCell ??
            throw new InvalidOperationException("Native initial CELL was not decoded.");
        var stack = _nativePluginStack ??
            throw new InvalidOperationException("Native plugin stack was not indexed.");
        var fallout3 = RuntimeLiveContentSource.Current.Campaign ==
            RuntimeLiveContentSource.Fallout3Game;
        var transition = fallout3
            ? null
            : FalloutDoorTransitionResolver.ResolveInteriorExits(stack, cell).Single();
        var restore = _nativeContinueOpening
            ? _nativeOpeningRestore ?? throw new InvalidOperationException(
                "Native Continue was selected without a valid cold save.")
            : null;
        if (restore is not null)
        {
            // Prewarmed presentation captured the new-game reference owner.
            // Restore builds presentation against the restored world instead.
            _nativePrewarmedInitialCellRoot?.Free();
            _nativePrewarmedInitialCellRoot = null;
            _nativeReferences?.Dispose();
            _nativeReferences = new(stack);
            if (restore.State.References is { } savedReferences) _nativeReferences.Restore(savedReferences);
            else SetMeta("opennv_reference_state_divergence", "Legacy save has no reference-instance state.");
        }
        if (!fallout3)
        {
            if (restore is not null)
            {
                if (restore.State.SkyLighting is { } sky) _nativeSkyLighting!.Restore(sky);
                else _nativeSkyLighting!.MarkUnbound("Legacy save has no sky/climate state; region emittance cannot be reconstructed.");
            }
            if (restore is null) _nativeGameTime!.InitializeNewGame();
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
            if (restore.State.Quests is not null) _nativeQuestState!.Restore(restore.State.Quests);
        }
        var activeCell = restore?.State.ActiveCell ?? cell.Cell.FormKey;
        var sourceSide = activeCell == cell.Cell.FormKey;
        var activeScene = cell;
        FalloutExteriorGridScene? grid = null;
        float[]? position = null;
        if (!sourceSide)
        {
            if (fallout3 || restore is null)
                throw new NotSupportedException($"Saved CELL {activeCell} has no world streaming owner.");
            activeScene = FalloutCellSceneReader.Read(stack, activeCell);
            var restored = FalloutNativeCampaignSave.RestorePlayerPosition(restore.State, _configuration.Player.SpawnCenterHeightMeters);
            var units = _configuration.World.GameUnitsToMeters;
            position = [restored[0] / units, -restored[2] / units, restored[1] / units];
            if (activeScene.Cell.Worldspace is { } world)
            {
                grid = ResolveExterior(world, position);
                if (activeCell != grid.Scene.Cell.FormKey && activeCell != grid.PersistentCell)
                    throw new InvalidDataException($"Saved CELL {activeCell} does not contain the saved exterior position.");
                activeScene = grid.Scene;
            }
        }
        _nativeSkyLighting?.EnterCell(activeScene.Cell, _nativeGlobals, position);
        var root = sourceSide && _nativePrewarmedInitialCellRoot is not null
            ? _nativePrewarmedInitialCellRoot
            : BuildNativeCellRoot(activeScene, transition, sourceSide);
        _nativePrewarmedInitialCellRoot = null;
        if (grid is not null) AddExteriorLandscape(root, grid);
        AddChild(root);
        if (!fallout3 && (restore is null || restore.State.Scripts is not null))
        {
            // New Game retains the timers already running behind StartMenu.
            // Continue replaces that menu session with the saved clocks.
            if (restore is not null || _nativeQuestScripts is null) CreateNativeQuestScripts(restore?.State.Scripts);
            _nativeQuestScripts!.ActivateWorld();
        }
        else if (_nativeQuestScripts is not null)
        {
            RemoveChild(_nativeQuestScripts);
            _nativeQuestScripts.QueueFree();
            _nativeQuestScripts = null;
        }
        if (activeScene.Cell.Lighting is not null)
            AddNativeCellEnvironment(root, activeScene);
        else if (grid is not null)
            AddExteriorEnvironment(root, activeScene.Cell);
        if (fallout3)
            AddNativeFallout3PlayerCamera(root, cell);
        else
            AddNativePlayer(cell);
        SetNativeActiveCell(root, activeScene);
        if (!fallout3) AddNativeGameplayHud();
        GD.Print(
            $"OPENNV_NATIVE_ACTIVE_CELL cell={activeScene.Cell.FormKey} " +
            $"restored={(restore is not null)} sourceSide={sourceSide}");
        DismissLoadingScreen();
    }

    private void CreateNativeQuestScripts(FalloutQuestScriptsSnapshot? restore = null)
    {
        var claimed = _nativeOpeningControls!.Quests.Values.Select(stages => stages.Values.First().Quest).ToHashSet();
        var scripts = new RuntimeNativeQuestScripts(_nativePluginStack!, _nativeQuestState!, claimed, _nativeInventory, _nativeGlobals, _nativeReferences);
        if (restore is not null) scripts.Scripts.Restore(restore);
        if (_nativeQuestScripts is not null)
        {
            RemoveChild(_nativeQuestScripts);
            _nativeQuestScripts.QueueFree();
        }
        _nativeQuestScripts = scripts;
        AddChild(scripts);
    }

    private Node3D BuildNativeCellRoot(
        FalloutCellScene cell,
        FalloutDoorTransition? transition,
        bool sourceSide)
    {
        var root = new Node3D { Name = $"NativeCell_{cell.Cell.FormKey}" };
        try
        {
            PopulateNativeCellRoot(root, cell, transition, sourceSide);
            return root;
        }
        catch
        {
            root.Free();
            throw;
        }
    }

    private void PopulateNativeCellRoot(Node3D root, FalloutCellScene cell,
        FalloutDoorTransition? transition, bool sourceSide)
    {
        var source = RuntimeLiveContentSource.Current ??
            throw new InvalidOperationException("Live retail source was cleared during CELL streaming.");
        const string parityScope = "world/active-cell";
        string ParityIdentity(FalloutPlacedReference reference) =>
            $"{cell.Cell.FormKey}/{reference.FormKey}";
        _parityObservations.ReplaceScope(
            parityScope,
            cell.References.Select(reference =>
            {
                var baseObject = cell.BaseObjects[reference.Base];
                return (
                    ParityIdentity(reference),
                    ParityCategoryFor(baseObject.Signature),
                    NativeReferenceState(reference, baseObject, "source"));
            }));
        _nativeActorDivergences.Clear();
        _nativeReferenceDivergences.Clear();
        foreach (var reference in cell.References)
        {
            var previousChildren = root.GetChildCount();
            try { PlaceNativeReference(root, cell, reference); }
            catch (Exception error) when (error is IOException or InvalidDataException or NotSupportedException or InvalidOperationException)
            {
                // Reject the whole failed reference. Other source references
                // retain independent ownership; the rejected identity remains
                // missing in the parity denominator, never a substitute draw.
                while (root.GetChildCount() > previousChildren) root.GetChild(previousChildren).Free();
                _nativeReferenceDivergences[reference.FormKey.ToString()] = error.Message;
                GD.PushError($"OPENNV_NATIVE_REFERENCE_DIVERGENCE reference={reference.FormKey} base={reference.Base}: {error.Message}");
            }
        }
        var presentation = new RuntimeNativeReferencePresentation(_nativeReferences!, cell.References, reference =>
        {
            Node3D? Find() => root.GetChildren().OfType<Node3D>().SingleOrDefault(node =>
                node is RuntimeNativeNpc actor ? actor.Appearance.Reference == reference.FormKey :
                node.GetMeta("opennv_reference_form_key", "").AsString() == reference.FormKey.ToString());
            if (Find() is { } existing) return existing;
            var previousChildren = root.GetChildCount();
            try { PlaceNativeReference(root, cell, reference, materializeDisabled: true); return Find(); }
            catch
            {
                while (root.GetChildCount() > previousChildren) root.GetChild(previousChildren).Free();
                throw;
            }
        });
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
        bool materializeDisabled = false, bool observe = true, FalloutNifFile? preparedModel = null)
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
        if (source.Game == RuntimeLiveContentSource.FalloutNewVegasGame &&
            FalloutNewVegasBuiltinForms.IsInternalStatic(baseObject.Signature,
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
                var equippedArmor = _nativeReferences!.EquippedArmor(reference.FormKey,
                    _nativeOpeningStageDriver?.PlayerLevel ?? _nativeOpeningRestore?.State.Vitals?.Level ?? 1, _nativeGlobals);
                var actor = RuntimeNativeNpc.Create(_nativePluginStack!, source, reference,
                    _configuration.World.GameUnitsToMeters, (appearance, part, nif, geometry) =>
                        NativeNpcMaterial.Resolve(appearance, part, nif, geometry, _nativePluginStack!,
                            NativeAmbient(cell.Cell)), equippedArmor);
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
                    return root.FindChildren("*", "", true, false).OfType<RuntimeNativeNpc>()
                        .SingleOrDefault(value => value.Appearance.Reference == target)?.HeadTargetPoint;
                });
                actor.ConfigureAi(_nativePluginStack!, _nativeQuestState!, cell, ReferenceTransform);
                root.AddChild(actor);
                actor.Combat = RuntimeNativeActorCombat.Attach(actor, actor.Skeleton, actor.Appearance.SkeletonPath,
                    _nativeReferences!, _nativeReferences!.Get(reference.FormKey), _nativePluginStack!, source,
                    _configuration.Player.CollisionLayer, _configuration.Player.CollisionMask | _configuration.Player.CollisionLayer);
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
                    _configuration.Player.CollisionLayer, _configuration.Player.CollisionMask | _configuration.Player.CollisionLayer);
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
        root.AddChild(instance);
        AddNativeReferenceEmittance(instance, reference);
        var controllers = instance.FindChildren("*", "", true, false).OfType<RuntimeNifControllerPlayer>().ToArray();
        if (controllers.Any(controller => controller.HasTextKeys))
        {
            var sounds = new NativeOwnedAnimationSoundPlayer(_nativePluginStack!, source, instance,
                _configuration.World.GameUnitsToMeters, _nativeReferences!.Get(reference.FormKey).SoundRandom);
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
        else if (baseObject.Signature == "DOOR")
            instance.AddChild(new RuntimeNativeDoorMotion(_nativeReferences!.Get(reference.FormKey), controllers, SaveNativeInteraction));
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
        string disposition)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        WriteText(writer, reference.FormKey.ToString());
        WriteText(writer, reference.EditorId);
        WriteText(writer, reference.Base.ToString());
        WriteText(writer, baseObject.Signature);
        WriteText(writer, baseObject.EditorId);
        WriteText(writer, disposition);
        writer.Write(reference.Flags);
        foreach (var value in reference.Position)
            writer.Write(value);
        foreach (var value in reference.RotationRadians)
            writer.Write(value);
        writer.Write(reference.Scale);
        WriteText(writer, baseObject.ModelPath ?? string.Empty);
        writer.Flush();
        return stream.ToArray();
    }

    private static void WriteText(BinaryWriter writer, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }

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
        if (_nativeDoorLoading) return;
        _nativeDoorLoading = true;
        _nativePlayer!.SetModalInput(true);
        // Activation queues streaming. A presentation failure must not become a
        // permanent source-script fault or consume the reciprocal door's state.
        Callable.From(() =>
        {
            try { StreamNativeDoorTransition(reference); }
            catch (Exception error)
            {
                SetMeta("opennv_door_stream_error", error.Message);
                GD.PushError($"OPENNV_NATIVE_DOOR_STREAM_FAIL {error}");
            }
            finally { _nativeDoorLoading = false; _nativePlayer!.SetModalInput(false); }
        }).CallDeferred();
    }

    private void StreamNativeDoorTransition(
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
        var player = _nativePlayer ??
            throw new InvalidOperationException("Native door activation has no authoritative player.");
        var sky = _nativeSkyLighting ?? throw new InvalidOperationException("Door transition has no sky owner.");
        var previousSky = sky.Capture();
        var grid = transition.DestinationScene.Cell.Worldspace is { } world ? ResolveExterior(world, entry.Position) : null;
        var targetScene = grid?.Scene ?? transition.DestinationScene;
        Node3D? targetRoot = null;
        try
        {
            sky.EnterCell(targetScene.Cell, _nativeGlobals, entry.Position);
            targetRoot = BuildNativeCellRoot(targetScene, null, sourceSide: false);
            if (grid is not null) AddExteriorLandscape(targetRoot, grid);
            AddChild(targetRoot);
            if (targetScene.Cell.Lighting is not null) AddNativeCellEnvironment(targetRoot, targetScene);
            else AddExteriorEnvironment(targetRoot, targetScene.Cell);
        }
        catch
        {
            targetRoot?.Free();
            sky.Restore(previousSky);
            throw;
        }
        player.Teleport(TeleportTransform(entry));
        SetNativeActiveCell(targetRoot, targetScene);
        current.ProcessMode = ProcessModeEnum.Disabled;
        current.QueueFree();
        _nativeOpeningStageDriver!.PersistWorldState(targetScene.Cell.FormKey);
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
        if (_nativeReferences is { } references && _nativeActiveCell?.Cell.FormKey != cell.Cell.FormKey)
        {
            _nativeReferenceEvents?.SetProcess(false);
            references.LoadCell(cell);
            if (_nativeActiveCell is { } previous) references.UnloadCell(previous.Cell.FormKey);
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

    private void AddNativePlayer(FalloutCellScene initialCell)
    {
        if (_nativePlayer is not null)
            throw new InvalidOperationException("Native player was already created.");
        var start = FalloutNewGamePlayerStartResolver.Resolve(
            _nativePluginStack ?? throw new InvalidOperationException("Native plugin stack was not indexed."),
            initialCell);
        var marker = start.Reference;
        _nativePlayer = new RuntimeNativePlayer();
        _nativePlayer.CreateFurnitureBody = () => RuntimeNativeNpc.Create(
            (_nativeOpeningStageDriver ?? throw new InvalidOperationException("Player appearance owner is absent.")).PlayerAppearance
                with
            { Reference = _nativePluginStack!.RuntimeFormKey(0x14) },
            RuntimeLiveContentSource.Current!, _configuration.World.GameUnitsToMeters,
            (appearance, part, nif, geometry) => NativeNpcMaterial.Resolve(appearance, part, nif, geometry, _nativePluginStack!,
                NativeAmbient(_nativeActiveCell?.Cell ?? throw new InvalidOperationException("Player body has no active cell."))));
        _nativePlayer.ActivateReference = collider => _nativeReferenceEvents?.TryActivate(collider) == true;
        _nativePlayer.SaveGame = SaveNativeInteraction;
        _nativePlayer.Configure(_configuration, ReferenceTransform(marker));
        _nativePlayer.ConfigureLocomotion(_nativePluginStack!);
        _nativePlayer.ConfigurePresentation(_nativePluginStack!, _nativeInventory,
            () => _nativeOpeningStageDriver!.PlayerAppearance, () => NativeAmbient(_nativeActiveCell!.Cell),
            _nativeContinueOpening ? _nativeOpeningRestore?.State.WeaponHandling : null);
        _nativePlayer.ConfigureCombat(_nativePluginStack!, _nativeGlobals!,
            () => _nativeOpeningStageDriver!.PlayerLevel, value => _nativeOpeningStageDriver!.PlayerCombatValue(value),
            () => _nativeOpeningStageDriver!.PlayerPerkEntries);
        _nativePlayer.CanOccupyPosition = NativeCollisionResident;
        var restore = _nativeContinueOpening
            ? _nativeOpeningRestore ?? throw new InvalidOperationException(
                "Native Continue was selected without a valid cold save.")
            : null;
        if (restore is not null)
            _nativePlayer.RestoreTransform(
                FalloutNativeCampaignSave.RestorePlayerPosition(restore.State,
                    _configuration.Player.SpawnCenterHeightMeters),
                restore.State.PlayerRotation, FalloutNativeCampaignSave.RestorePlayerViewPitch(restore.State));
        AddChild(_nativePlayer);
        if (_nativeXr is not null) _nativePlayer.AttachXr(_nativeXr);
        _nativeOpeningStageDriver = new RuntimeNativeOpeningStageDriver();
        _nativeOpeningStageDriver.Configure(
            _nativeOpeningTransitions ??
                throw new InvalidOperationException("Native opening transition graph was not resolved."),
            _nativeOpeningControls ??
                throw new InvalidOperationException("Native opening control graph was not resolved."),
            _nativePlayer,
            _nativeOpeningGrant ??
                throw new InvalidOperationException("Native opening inventory grant was not resolved."),
            _nativeRaceSexContract ??
                throw new InvalidOperationException("Native race/sex contract was not resolved."),
            _nativeVigorContract ??
                throw new InvalidOperationException("Native Vigor contract was not resolved."),
            _nativeTagSkillContract ??
                throw new InvalidOperationException("Native tag-skill contract was not resolved."),
            _nativeTraitFarewellContract ??
                throw new InvalidOperationException("Native trait/farewell contract was not resolved."),
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
            "VCG00",
            0);
        AddChild(_nativeOpeningStageDriver);
        GD.Print(
            $"OPENNV_NATIVE_PLAYER_START reference={marker.FormKey} editorId={marker.EditorId} " +
            $"quest={start.Quest} stage={start.Stage} candidates={start.Candidates.Count} " +
            $"packageLinked={start.Candidates.Count(value => value.DirectPackageLocationCount > 0)} " +
            $"restored={(restore is not null)} inventory={restore?.Inventory.Items.Count ?? 0} " +
            "owner=character-body controls=live-qust-sctx source=live-retail-files");
    }

    private void AddNativeFallout3PlayerCamera(Node3D root, FalloutCellScene cell)
    {
        var expected = new FalloutFormKey(
            NativeFallout3InitialCellPlugin,
            NativeFallout3PlayerStartObjectId);
        var marker = cell.References.SingleOrDefault(reference => reference.FormKey == expected) ??
            throw new InvalidDataException(
                $"Fallout 3 initial CELL {cell.Cell.FormKey} has no exact player start {expected}.");
        var camera = new Camera3D
        {
            Name = $"NativePlayerStart_{marker.FormKey}",
            Transform = ReferenceTransform(marker),
            Current = true,
        };
        root.AddChild(camera);
        GD.Print(
            $"OPENNV_NATIVE_FO3_PLAYER_START reference={marker.FormKey} editorId={marker.EditorId} " +
            "source=standalone-fallout3");
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
