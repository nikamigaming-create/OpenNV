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

    private object CaptureNativeDriveState()
    {
        static float[] Vector(Vector3 value) => [value.X, value.Y, value.Z];
        static object? MovieField(Node movie, string key) => movie.HasMeta(key) ? movie.GetMeta(key).Obj : null;
        var camera = GetViewport().GetCamera3D();
        return new
        {
            paused = GetTree().Paused,
            references = _nativeReferences is null ? null : new
            {
                _nativeReferences.InstanceCount,
                _nativeReferences.ResidentCellCount,
                _nativeReferences.ScriptDefinitionCount,
                state = _nativeReferences.Capture()
            },
            cell = _nativeActiveCell?.Cell.FormKey.ToString(),
            player = _nativePlayer is null ? null : new
            {
                position = Vector(_nativePlayer.GlobalPosition),
                velocity = Vector(_nativePlayer.Velocity),
                onFloor = _nativePlayer.IsOnFloor(),
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
                    error = MovieField(movie, "opennv_movie_error"),
                }).ToArray(),
            missingRuntimeReferences = _nativeActiveCell is null ? [] : _parityObservations.Snapshot().Missing.ToArray(),
            actorDivergences = _nativeActorDivergences.ToArray(),
            speech = _nativeOpeningStageDriver?.SpeechState,
            questScripts = _nativeQuestScripts?.State,
            gameTime = _nativeGameTimeAdapter?.State,
            gameTimeUnbound = _nativeGameTimeUnbound,
            skyLighting = _nativeSkyLighting?.Unbound is null ? _nativeSkyLighting?.Capture() : null,
            skyLightingUnbound = _nativeSkyLighting?.Unbound,
            playerInventory = _nativeInventory.Items,
            questScriptsUnbound = _nativeContinueOpening && _nativeOpeningRestore?.State.Scripts is null ? "Legacy save has no quest script state." : null,
            playerPackage = _nativeOpeningStageDriver?.PlayerPackageState,
            characterCreation = _nativeOpeningStageDriver?.CharacterCreationState,
            imageSpace = _nativeCurrentCellRoot?.GetChildren().OfType<RuntimeNativeImageSpace>().Select(presenter => new
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
            actors = _nativeCurrentCellRoot?.FindChildren("*", "", true, false).OfType<RuntimeNativeNpc>()
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
            lights = _nativeCurrentCellRoot?.GetChildren().OfType<OmniLight3D>().Select(light => new
            {
                reference = light.GetMeta("opennv_ligh_reference", "").AsString(),
                emittance = light.GetMeta("opennv_ligh_emittance", "").AsString(),
                shaderRgb = light.GetMeta("opennv_ligh_shader_rgb").AsFloat32Array(),
                position = Vector(light.GlobalPosition),
                light.LightEnergy,
                light.OmniRange,
                light.ShadowEnabled,
            }).ToArray(),
            objectAnimations = _nativeCurrentCellRoot?.FindChildren("*", "", true, false).OfType<RuntimeNifControllerPlayer>()
                .Select(controller => new
                {
                    path = controller.GetPath().ToString(),
                    sequence = controller.ActiveSequence,
                    sourceSeconds = controller.SourceTimeSeconds,
                    registered = controller.SequenceNames.ToArray(),
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
        _nativeQuestState = new(_nativePluginStack);
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
            _nativePrewarmedInitialCellRoot = BuildNativeCellRoot(
                initialCell,
                transition,
                sourceSide: true);
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
            _nativeReferences?.Dispose();
            _nativeReferences = new(stack);
            if (restore.State.References is { } savedReferences) _nativeReferences.Restore(savedReferences);
            else SetMeta("opennv_reference_state_divergence", "Legacy save has no reference-instance state.");
        }
        var activeCell = restore?.State.ActiveCell ?? cell.Cell.FormKey;
        var sourceSide = true;
        var activeScene = cell;
        if (!fallout3 && activeCell != cell.Cell.FormKey)
        {
            if (transition is null || activeCell != transition.DestinationScene.Cell.FormKey)
                throw new InvalidDataException(
                    $"Native Continue active CELL is outside the admitted route: {activeCell}.");
            activeScene = transition.DestinationScene;
            sourceSide = false;
        }
        var root = sourceSide && activeScene.Cell.FormKey == cell.Cell.FormKey &&
            _nativePrewarmedInitialCellRoot is not null
            ? _nativePrewarmedInitialCellRoot
            : BuildNativeCellRoot(activeScene, transition, sourceSide);
        _nativePrewarmedInitialCellRoot = null;
        if (!sourceSide && transition is not null)
            root.AddChild(RuntimeNativeLandscapeTransportBuilder.Build(
                FalloutLandscapeTransportResolver.Resolve(stack, transition),
                _configuration.World.GameUnitsToMeters));
        AddChild(root);
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
            _nativeInventory.Restore(restore.Inventory, restore.State.EquippedRuntimeFormIds.ToArray());
            if (restore.State.Quests is not null) _nativeQuestState!.Restore(restore.State.Quests);
        }
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
        else if (transition is not null)
            root.SetMeta(
                "opennv_exterior_environment_pending",
                transition.DestinationWorldspace.ToString());
        if (fallout3)
            AddNativeFallout3PlayerCamera(root, cell);
        else
            AddNativePlayer(root, cell, sourceSide);
        SetNativeActiveCell(root, activeScene);
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
        var source = RuntimeLiveContentSource.Current ??
            throw new InvalidOperationException("Live retail source was cleared during CELL streaming.");
        var root = new Node3D { Name = $"NativeCell_{cell.Cell.FormKey}" };
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
        var placed = 0;
        var placedLights = 0;
        var internalObjects = 0;
        _nativeActorDivergences.Clear();
        foreach (var reference in cell.References)
        {
            if (!cell.BaseObjects.TryGetValue(reference.Base, out var baseObject))
                throw new InvalidDataException(
                    $"Live CELL reference {reference.FormKey} has no decoded base object.");
            if (FalloutCellSceneReader.IsInitiallyDisabled(reference))
            {
                _parityObservations.Observe(
                    parityScope,
                    ParityIdentity(reference),
                    NativeReferenceState(reference, baseObject, "disabled"));
                continue;
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
                _parityObservations.Observe(parityScope, ParityIdentity(reference),
                    NativeReferenceState(reference, baseObject, "internal-static"));
                internalObjects++;
                continue;
            }
            if (baseObject.Light is not null)
            {
                AddNativePlacedLight(root, reference, baseObject);
                _parityObservations.Observe(
                    parityScope,
                    ParityIdentity(reference),
                    NativeReferenceState(reference, baseObject, "light"));
                placedLights++;
                continue;
            }
            if (baseObject.Signature == "NPC_")
            {
                try
                {
                    var actor = RuntimeNativeNpc.Create(_nativePluginStack!, source, reference,
                        _configuration.World.GameUnitsToMeters, (appearance, part, nif, geometry) =>
                            NativeNpcMaterial.Resolve(appearance, part, nif, geometry, _nativePluginStack!,
                                ByteColor((cell.Cell.Lighting ?? throw new InvalidDataException("NPC CELL lighting is absent.")).AmbientRgb)));
                    actor.Transform = ReferenceTransform(reference);
                    actor.ConfigureHeadTracking(_nativePluginStack!, source, target =>
                    {
                        if (_nativePluginStack!.RuntimeFormId(target) == 0x14)
                            return _nativePlayer?.Camera.GlobalPosition;
                        return root.FindChildren("*", "", true, false).OfType<RuntimeNativeNpc>()
                            .SingleOrDefault(value => value.Appearance.Reference == target)?.HeadTargetPoint;
                    });
                    actor.ConfigureAi(_nativePluginStack!, _nativeQuestState!, cell, ReferenceTransform);
                    root.AddChild(actor);
                    AddNativeReferenceEmittance(actor, reference);
                    _nativeActorDivergences[reference.FormKey.ToString()] =
                        "animation-selection-blending, face-pose, gameplay, material-lighting-output parity unbound";
                    _parityObservations.Observe(parityScope, ParityIdentity(reference),
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
                continue;
            }
            if (baseObject.ModelPath is null)
                continue;
            if (!_nativeNifPrototypes.TryGetValue(baseObject.ModelPath, out var prototype))
            {
                if (!source.TryRead(baseObject.ModelPath, null, out var nif, out var nifSource))
                    throw new FileNotFoundException(
                        $"Winning model {baseObject.ModelPath} for {baseObject.FormKey} is missing.");
                GD.Print(
                    $"OPENNV_NATIVE_NIF_LOADING model={baseObject.ModelPath} source={nifSource} " +
                    $"base={baseObject.FormKey}");
                prototype = new RuntimeNativeNifPrototype(nif, _configuration.World.GameUnitsToMeters);
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
            if (controllers.Length != 0)
                GD.Print($"OPENNV_NATIVE_REFERENCE_CONTROLLERS source={reference.FormKey} " +
                    $"sequences={string.Join(',', controllers.SelectMany(controller => controller.SequenceNames))} binding=per-instance");
            _parityObservations.Observe(
                parityScope,
                ParityIdentity(reference),
                NativeReferenceState(reference, baseObject, "model"));
            var portalReference = transition is null
                ? null
                : sourceSide ? transition.SourceDoor : transition.DestinationDoor;
            if (transition is not null && reference.FormKey == portalReference!.FormKey)
                AddNativeDoorPortal(instance, transition, sourceSide);
            placed++;
        }
        var coverage = _parityObservations.Snapshot();
        var missing = coverage.Missing.Count(identity =>
            identity.StartsWith(parityScope + "/", StringComparison.Ordinal));
        GD.Print(
            $"OPENNV_NATIVE_CELL_READY cell={cell.Cell.FormKey} placed={placed} lights={placedLights} internalObjects={internalObjects} " +
            $"residentPrototypes={_nativeNifPrototypes.Count} discovered={cell.References.Count} " +
            $"observed={cell.References.Count - missing} missing={missing} " +
            $"runtimePresence={(missing == 0 ? "complete" : "incomplete")} parity=unmeasured " +
            "source=live-retail-files");
        return root;
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
        FalloutDoorTransition transition,
        bool sourceSide)
    {
        var reference = sourceSide ? transition.SourceDoor : transition.DestinationDoor;
        var destination = sourceSide ? transition.DestinationDoor : transition.SourceDoor;
        var destinationScene = sourceSide ? transition.DestinationScene : transition.SourceScene;
        var destinationWorld = sourceSide
            ? transition.DestinationWorldspace
            : transition.SourceScene.Cell.Worldspace;
        var portal = new RuntimeNativeDoorPortal();
        portal.Configure(
            reference.FormKey,
            destination.FormKey,
            destinationScene.Cell.FormKey,
            destinationWorld,
            () => StreamNativeDoorTransition(transition, toDestination: sourceSide));
        doorInstance.AddChild(portal);
    }

    private void StreamNativeDoorTransition(
        FalloutDoorTransition transition,
        bool toDestination)
    {
        if (_nativeOpeningStageDriver is not { Stage: FalloutNativeCampaignSave.CompletedOpeningStage })
        {
            GD.Print(
                $"OPENNV_NATIVE_DOOR_STREAM_BLOCKED stage=" +
                $"{_nativeOpeningStageDriver?.QuestEditorId}:" +
                $"{_nativeOpeningStageDriver?.Stage} required=VCG01:" +
                $"{FalloutNativeCampaignSave.CompletedOpeningStage}");
            return;
        }
        var current = _nativeCurrentCellRoot ??
            throw new InvalidOperationException("Native door activation has no current CELL root.");
        var active = _nativeActiveCell ??
            throw new InvalidOperationException("Native door activation has no authoritative CELL state.");
        var expectedCell = toDestination
            ? transition.SourceScene.Cell.FormKey
            : transition.DestinationScene.Cell.FormKey;
        if (active.Cell.FormKey != expectedCell)
            throw new InvalidOperationException(
                $"Native door activation expected CELL {expectedCell}, found {active.Cell.FormKey}.");
        var targetScene = toDestination ? transition.DestinationScene : transition.SourceScene;
        var targetRoot = BuildNativeCellRoot(
            targetScene,
            transition,
            sourceSide: !toDestination);
        if (toDestination)
        {
            var stack = _nativePluginStack ??
                throw new InvalidOperationException("Native plugin stack was not indexed.");
            targetRoot.AddChild(RuntimeNativeLandscapeTransportBuilder.Build(
                FalloutLandscapeTransportResolver.Resolve(stack, transition),
                _configuration.World.GameUnitsToMeters));
        }
        AddChild(targetRoot);
        if (targetScene.Cell.Lighting is not null)
            AddNativeCellEnvironment(targetRoot, targetScene);
        else
            targetRoot.SetMeta(
                "opennv_exterior_environment_pending",
                transition.DestinationWorldspace.ToString());
        var entry = toDestination
            ? transition.SourceDoor.Teleport!
            : transition.DestinationDoor.Teleport!;
        var player = _nativePlayer ??
            throw new InvalidOperationException("Native door activation has no authoritative player.");
        player.Teleport(TeleportTransform(entry));
        SetNativeActiveCell(targetRoot, targetScene);
        current.QueueFree();
        _nativeOpeningStageDriver.PersistWorldState(targetScene.Cell.FormKey);
        GD.Print(
            $"OPENNV_NATIVE_DOOR_STREAM source={expectedCell} destination={targetScene.Cell.FormKey} " +
            $"door={(toDestination ? transition.SourceDoor.FormKey : transition.DestinationDoor.FormKey)} " +
            $"entry={entry.Door} world={transition.DestinationWorldspace} " +
            "source=live-retail-files");
    }

    private void SetNativeActiveCell(Node3D root, FalloutCellScene cell)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(cell);
        if (_nativeReferences is { } references && _nativeActiveCell?.Cell.FormKey != cell.Cell.FormKey)
        {
            if (_nativeActiveCell is { } previous) references.UnloadCell(previous.Cell.FormKey);
            references.LoadCell(cell);
        }
        _nativeCurrentCellRoot = root;
        _nativeActiveCell = cell;
        _nativeSkyLighting?.EnterCell(cell.Cell);
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
        root.AddChild(RuntimeNativePlacedLightBuilder.Build(
            reference,
            baseObject,
            ReferenceTransform(reference),
            _configuration.World.GameUnitsToMeters,
            _configuration.Renderer.PointLightEnergyScale,
            _configuration.Renderer.MinimumPointLightEnergy,
            _configuration.Renderer.AuthoredPointLightShadows,
            _nativePluginStack,
            region => (_nativeSkyLighting ?? throw new InvalidOperationException("Sky lighting state is absent."))
                .RegionEmittance(region, (_nativeGameTime ?? throw new InvalidOperationException("Sky has no simulation clock.")).Hour)));
    }

    private void AddNativePlayer(
        Node3D cellRoot,
        FalloutCellScene initialCell,
        bool addOpeningInteractions)
    {
        if (_nativePlayer is not null)
            throw new InvalidOperationException("Native player was already created.");
        var start = FalloutNewGamePlayerStartResolver.Resolve(
            _nativePluginStack ?? throw new InvalidOperationException("Native plugin stack was not indexed."),
            initialCell);
        var marker = start.Reference;
        _nativePlayer = new RuntimeNativePlayer();
        _nativePlayer.Configure(_configuration, ReferenceTransform(marker));
        var restore = _nativeContinueOpening
            ? _nativeOpeningRestore ?? throw new InvalidOperationException(
                "Native Continue was selected without a valid cold save.")
            : null;
        if (restore is not null)
            _nativePlayer.RestoreTransform(
                FalloutNativeCampaignSave.RestorePlayerPosition(restore.State,
                    _configuration.Player.SpawnCenterHeightMeters),
                restore.State.PlayerRotation);
        AddChild(_nativePlayer);
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
        if (addOpeningInteractions)
        {
            AddNativeVigorInteraction(
                cellRoot,
                _nativeVigorContract ??
                    throw new InvalidOperationException("Native Vigor contract was not resolved."),
                _nativeOpeningStageDriver);
            AddNativeFarewellInteraction(
                cellRoot,
                _nativeTraitFarewellContract ??
                    throw new InvalidOperationException("Native trait/farewell contract was not resolved."),
                _nativeOpeningStageDriver);
        }
        GD.Print(
            $"OPENNV_NATIVE_PLAYER_START reference={marker.FormKey} editorId={marker.EditorId} " +
            $"quest={start.Quest} stage={start.Stage} candidates={start.Candidates.Count} " +
            $"packageLinked={start.Candidates.Count(value => value.DirectPackageLocationCount > 0)} " +
            $"restored={(restore is not null)} inventory={restore?.Inventory.Items.Count ?? 0} " +
            "owner=character-body controls=live-qust-sctx source=live-retail-files");
    }

    private void AddNativeVigorInteraction(
        Node3D cellRoot,
        FalloutNativeVigorContract contract,
        RuntimeNativeOpeningStageDriver driver)
    {
        var dimensions = contract.TriggerDimensionsGameUnits;
        var trigger = new RuntimeNativeVigorTrigger
        {
            Transform = ReferenceTransform(contract.TriggerReference),
        };
        trigger.Configure(
            new Vector3(dimensions[0], dimensions[2], dimensions[1]) *
                _configuration.World.GameUnitsToMeters,
            _configuration.Player.CollisionLayer,
            driver.EnterVigorTrigger);
        cellRoot.AddChild(trigger);
        var tester = cellRoot.GetChildren().OfType<Node3D>()
            .SingleOrDefault(value =>
                value.HasMeta("opennv_reference_form_key") &&
                value.GetMeta("opennv_reference_form_key").AsString() ==
                    contract.TesterReference.FormKey.ToString());
        if (tester is null)
        {
            var baseObject = _nativeInitialCell?.BaseObjects.GetValueOrDefault(contract.TesterReference.Base);
            throw new InvalidDataException(
                $"Native Vigor tester presentation is absent: {contract.TesterReference.FormKey} " +
                $"base={contract.TesterReference.Base} flags=0x{contract.TesterReference.Flags:x8} " +
                $"initiallyDisabled={FalloutCellSceneReader.IsInitiallyDisabled(contract.TesterReference)} " +
                $"signature={baseObject?.Signature ?? "missing"} model={baseObject?.ModelPath ?? "none"}.");
        }
        var activator = new RuntimeNativeVigorActivator();
        activator.Configure(driver.ActivateVigorTester);
        tester.AddChild(activator);
        GD.Print(
            $"OPENNV_NATIVE_VIGOR_READY trigger={contract.TriggerReference.FormKey} " +
            $"tester={contract.TesterReference.FormKey} total={contract.RequiredTotal} " +
            "source=live-refr-acti-scpt-xprm writes=0");
    }

    private void AddNativeFarewellInteraction(
        Node3D cellRoot,
        FalloutNativeTraitFarewellContract contract,
        RuntimeNativeOpeningStageDriver driver)
    {
        var dimensions = contract.ExitTriggerDimensionsGameUnits;
        var trigger = new RuntimeNativeFarewellTrigger
        {
            Transform = ReferenceTransform(contract.ExitTriggerReference),
        };
        trigger.Configure(
            new Vector3(dimensions[0], dimensions[2], dimensions[1]) *
                _configuration.World.GameUnitsToMeters,
            _configuration.Player.CollisionLayer,
            driver.EnterFarewellTrigger);
        cellRoot.AddChild(trigger);
        GD.Print(
            $"OPENNV_NATIVE_FAREWELL_READY trigger={contract.ExitTriggerReference.FormKey} " +
            $"traits={contract.Traits.Count} maximum={contract.MaximumTraits} " +
            $"stage={contract.ExitTriggerFromStage}->{contract.FarewellStage}->" +
            $"{contract.CompletedStage} source=live-refr-acti-scpt-info-qust writes=0");
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
        var imageSpace = FalloutImageSpaceReader.ForCell(_nativePluginStack!, cell.Cell.FormKey);
        if (imageSpace is not null)
        {
            var application = RetailImageSpaceRenderer.CreateFromSource(imageSpace,
                _configuration.FalloutEnvironment.ImageSpace, _configuration.Capture,
                _configuration.ActorCompiler.FaceGenMaterial.RuntimeAlbedoTransfer);
            world.Compositor = application.Compositor;
            var presenter = new RuntimeNativeImageSpace();
            presenter.Configure(imageSpace, _nativeImageSpaceState, application.Effect, _nativeGameTime);
            root.AddChild(presenter);
            world.SetMeta("opennv_source_image_space", imageSpace.Form.ToString());
            world.SetMeta("opennv_source_image_space_version", imageSpace.FormVersion);
            world.SetMeta("opennv_source_image_space_dnam_sha256", imageSpace.DnamSha256);
            world.SetMeta("opennv_image_space_cinematic", application.Cinematic);
            world.SetMeta("opennv_image_space_tint", application.Tint);
            GD.Print($"OPENNV_NATIVE_IMAGE_SPACE source={imageSpace.Form} version={imageSpace.FormVersion} " +
                $"cinematic={application.Cinematic} tint={application.Tint} " +
                "adaptation=runtime hdrParameterCoverage=partial depthOfField=unbound parity=unmeasured");
        }
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
