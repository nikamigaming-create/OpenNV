using System.Text.Json;
using Godot;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator : Node3D
{
    private Dictionary<string, string> _options = new(StringComparer.OrdinalIgnoreCase);
    private LegalAssetSetupView? _setupView;
    private LoadingScreen? _loadingScreen;
    private ulong _loadingStartedMilliseconds;
    private const double MinimumLoadingScreenSeconds = 0.85;

    public override void _Ready()
    {
        if (DisplayServer.GetName() != "headless")
        {
            _loadingScreen = new LoadingScreen();
            _loadingScreen.Configure("STARTING VERIFIED RUNTIME");
            AddChild(_loadingScreen);
            _loadingStartedMilliseconds = Time.GetTicksMsec();
        }
        Callable.From(StartRuntimeAfterLoadingFrame).CallDeferred();
    }

    private async void StartRuntimeAfterLoadingFrame()
    {
        if (_loadingScreen is not null)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        StartRuntime();
    }

    private void StartRuntime()
    {
        try
        {
            _options = ParseOptions(OS.GetCmdlineUserArgs());
            if (_options.ContainsKey("vr") && _options.ContainsKey("xr-rig-proof"))
                throw new ArgumentException("Use --vr for a live OpenXR session or --xr-rig-proof for the headless layout gate, not both.");
            if ((_options.ContainsKey("classic-diorama") || _options.ContainsKey("classic-diorama-rig-proof")) &&
                (_options.ContainsKey("vr") || _options.ContainsKey("vr-layout-proof") ||
                    _options.ContainsKey("xr-rig-proof")))
                throw new ArgumentException("Classic Diorama and OpenXR require separate presentation adapters.");
            if (_options.ContainsKey("vr"))
                EnableOpenXr();
            if (_options.ContainsKey("xr-rig-proof"))
            {
                CompleteXrRigProof(_options);
                return;
            }
            if (_options.ContainsKey("classic-diorama-rig-proof"))
            {
                CompleteClassicDioramaRigProof(_options);
                return;
            }
            var hasDataRoot = _options.TryGetValue("data-root", out var dataRoot);
            var hasModel = _options.ContainsKey("model");
            var hasCellScene = _options.ContainsKey("cell-scene");
            var hasActorModel = _options.ContainsKey("actor-model");
            if ((hasDataRoot ? 1 : 0) + (hasModel ? 1 : 0) + (hasCellScene ? 1 : 0) + (hasActorModel ? 1 : 0) > 1)
                throw new ArgumentException(
                    "Use only one of --data-root, --model/--sidecar, --cell-scene, or --actor-model/--actor-sidecar.");
            if (!hasModel && _options.ContainsKey("sidecar"))
                throw new ArgumentException("--sidecar requires --model.");
            if (_options.ContainsKey("material-manifest") != _options.ContainsKey("material-manifest-sha256"))
                throw new ArgumentException("Use --material-manifest together with --material-manifest-sha256.");
            if (!hasModel && _options.ContainsKey("material-manifest"))
                throw new ArgumentException("--material-manifest requires --model.");
            if (!hasActorModel && _options.ContainsKey("actor-sidecar"))
                throw new ArgumentException("--actor-sidecar requires --actor-model.");
            if (hasActorModel && _options.ContainsKey("capture-root"))
                throw new ArgumentException("Actor captures require --cell-scene plus --actor-scene.");
            if (_options.ContainsKey("actor-scene") && _options.ContainsKey("actor-scenes"))
                throw new ArgumentException("Use --actor-scene or --actor-scenes, not both.");
            if (_options.ContainsKey("retail-state-contract") &&
                (!hasCellScene || !_options.ContainsKey("capture-root") ||
                    (!_options.ContainsKey("actor-scene") && !_options.ContainsKey("actor-scenes"))))
                throw new ArgumentException(
                    "--retail-state-contract requires --cell-scene, actor scenes, and --capture-root.");

            if (hasDataRoot)
            {
                SetLoadingStatus("PREPARING PLAYER-OWNED CONTENT");
                var prepared = LegalAssetPreparer.Prepare(dataRoot!, _options);
                LoadPrepared(prepared, _options);
                DismissLoadingScreen();
                return;
            }

            if (hasModel)
            {
                SetLoadingStatus(
                    _options.ContainsKey("classic-diorama")
                        ? "LOADING CLASSIC DIORAMA MODEL"
                        : "VERIFYING HASHED 3D MODEL");
                LoadModel(RequireOption(_options, "model"), RequireOption(_options, "sidecar"), _options);
                DismissLoadingScreen();
                return;
            }

            if (hasCellScene)
            {
                SetLoadingStatus(
                    _options.ContainsKey("classic-diorama")
                        ? "LOADING CLASSIC DIORAMA CELL"
                        : "LOADING VERIFIED 3D CELL");
                LoadCellScene(RequireOption(_options, "cell-scene"), _options);
                DismissLoadingScreen();
                return;
            }

            if (hasActorModel)
            {
                SetLoadingStatus("VERIFYING HASHED ACTOR MODEL");
                LoadActorModel(
                    RequireOption(_options, "actor-model"),
                    RequireOption(_options, "actor-sidecar"),
                    _options);
                DismissLoadingScreen();
                return;
            }

            if (_options.ContainsKey("reuse-cache"))
            {
                SetLoadingStatus("RESTORING VERIFIED OWNED-DATA CACHE");
                if (!LegalAssetPreparer.TryRestore(_options, out var restored, out var restoreError))
                    throw new InvalidOperationException(restoreError ?? "No prepared legal-asset cache exists.");
                LoadPrepared(restored, _options);
                DismissLoadingScreen();
                return;
            }

            if (_options.TryGetValue("report", out var startupReportPath))
                WriteStartupReport(startupReportPath);
            GD.Print("OPENNV_GODOT_EXPERIMENTAL_READY playable=0 playableSandbox=1 openxr=experimental");
            if (DisplayServer.GetName() == "headless")
                GetTree().Quit(0);
            else if (LegalAssetPreparer.TryRestore(_options, out var restored, out var restoreError))
            {
                SetLoadingStatus("RESTORING VERIFIED OWNED-DATA CACHE");
                LoadPrepared(restored, _options);
                DismissLoadingScreen();
            }
            else
            {
                DismissLoadingScreen();
                ShowExperimentalStatus(restoreError);
            }
        }
        catch (Exception exception)
        {
            GD.PushError($"OPENNV_GODOT_RUNTIME_FAIL {exception.Message}");
            if (_loadingScreen is not null && DisplayServer.GetName() != "headless")
                _loadingScreen.ShowError(exception.Message);
            else
                GetTree().Quit(1);
        }
    }

    private void SetLoadingStatus(string status)
    {
        _loadingScreen?.SetStatus(status);
    }

    private void DismissLoadingScreen()
    {
        var loading = _loadingScreen;
        _loadingScreen = null;
        if (loading is null)
            return;
        var elapsedSeconds = (Time.GetTicksMsec() - _loadingStartedMilliseconds) / 1000.0;
        var remainingSeconds = MinimumLoadingScreenSeconds - elapsedSeconds;
        if (remainingSeconds <= 0.0 || _options.ContainsKey("capture-root"))
        {
            loading.QueueFree();
            return;
        }
        var timer = GetTree().CreateTimer(remainingSeconds);
        timer.Timeout += loading.QueueFree;
    }

    private void LoadPrepared(
        LegalAssetPreparer.PreparedContent prepared,
        IReadOnlyDictionary<string, string> options)
    {
        if (prepared.CellScenePath is not null)
        {
            var preparedOptions = options.ToDictionary(
                pair => pair.Key,
                pair => pair.Value,
                StringComparer.OrdinalIgnoreCase);
            if (prepared.ActorScenesPath is not null &&
                !preparedOptions.ContainsKey("actor-scene") &&
                !preparedOptions.ContainsKey("actor-scenes"))
                preparedOptions["actor-scenes"] = prepared.ActorScenesPath;
            LoadCellScene(prepared.CellScenePath, preparedOptions);
        }
        else
            LoadModel(prepared.ModelPath, prepared.SidecarPath, options);
    }

    private void LoadCellScene(string scenePath, IReadOnlyDictionary<string, string> options)
    {
        var runTraversalProof = options.ContainsKey("portal-proof");
        var useXrLayout = options.ContainsKey("vr") || options.ContainsKey("vr-layout-proof");
        var useClassicDiorama = options.ContainsKey("classic-diorama");
        var loaded = CellSceneLoader.Load(
            scenePath,
            this,
            !runTraversalProof && options.ContainsKey("open-proof-door"),
            options.TryGetValue("proof-door", out var proofDoor) ? proofDoor : null,
            options.TryGetValue("save-path", out var savePath) ? savePath : null,
            useXrLayout,
            options.ContainsKey("vr"),
            useClassicDiorama,
            options.TryGetValue("actor-scene", out var actorScene) ? actorScene : null,
            options.TryGetValue("actor-scenes", out var actorScenes) ? actorScenes : null,
            options.ContainsKey("proof-enable-actor"),
            !options.ContainsKey("capture-root"));
        if (options.TryGetValue("capture-root", out var captureRoot))
        {
            _ = EnvironmentCapture.Run(
                this,
                loaded,
                captureRoot,
                scenePath,
                options.TryGetValue("report", out var captureReport) ? captureReport : null,
                options.TryGetValue("retail-state-contract", out var retailState) ? retailState : null);
            return;
        }
        if (options.ContainsKey("gameplay-proof"))
        {
            _ = RunGameplayProof(loaded, scenePath, options);
            return;
        }
        if (options.ContainsKey("gameplay-reload-proof"))
        {
            CompleteGameplayReloadProof(loaded, scenePath, options);
            return;
        }
        if (runTraversalProof)
        {
            _ = RunDoorTraversalProof(loaded, scenePath, options);
            return;
        }
        CompleteCellLoad(loaded, scenePath, options, null);
    }

    private void EnableOpenXr()
    {
        var openXr = XRServer.FindInterface("OpenXR");
        if (openXr is null || !openXr.IsInitialized())
            throw new InvalidOperationException(
                "OpenXR was requested but no initialized runtime is available. " +
                "Launch with --xr-mode on before --, connect the headset, and verify the active OpenXR runtime.");
        GetViewport().UseXR = true;
        Engine.PhysicsTicksPerSecond = 90;
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        GD.Print("OPENNV_OPENXR_READY interface=OpenXR worldScale=1 physicsHz=90");
    }

    private void CompleteXrRigProof(IReadOnlyDictionary<string, string> options)
    {
        var actionMap = ResourceLoader.Load("res://openxr_action_map.tres")
            ?? throw new InvalidOperationException("OpenNV OpenXR action map could not be loaded.");
        var actionSets = actionMap.Get("action_sets").AsGodotArray();
        if (actionSets.Count != 1)
            throw new InvalidOperationException("OpenNV OpenXR action map must expose one gameplay action set.");
        var actionSet = actionSets[0].AsGodotObject() as Resource
            ?? throw new InvalidOperationException("OpenNV OpenXR gameplay action set is invalid.");
        var actions = actionSet.Get("actions").AsGodotArray();
        if (actions.Count != 8)
            throw new InvalidOperationException("OpenNV OpenXR gameplay action set must expose eight bounded actions.");
        var actionNames = actions
            .Select(value => value.AsGodotObject() as Resource
                ?? throw new InvalidOperationException("OpenNV OpenXR action is invalid."))
            .Select(action => action.ResourceName)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        var expectedActions = new[]
        {
            "activate", "aim", "fire", "haptic", "move", "reload", "save", "turn",
        };
        if (!actionNames.SequenceEqual(expectedActions, StringComparer.Ordinal))
            throw new InvalidOperationException("OpenNV OpenXR action names are incomplete.");
        var interactionProfiles = actionMap.Get("interaction_profiles").AsGodotArray()
            .Select(value => value.AsGodotObject() as Resource
                ?? throw new InvalidOperationException("OpenNV OpenXR interaction profile is invalid."))
            .Select(profile => profile.Get("interaction_profile_path").AsString())
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        var expectedProfiles = new[]
        {
            "/interaction_profiles/khr/generic_controller",
            "/interaction_profiles/oculus/touch_controller",
        };
        if (!interactionProfiles.SequenceEqual(expectedProfiles, StringComparer.Ordinal))
            throw new InvalidOperationException("OpenNV OpenXR interaction profile set is incomplete.");

        Engine.PhysicsTicksPerSecond = 90;
        var session = new GameplaySession();
        session.Configure(
            "xr-rig-proof",
            options.TryGetValue("save-path", out var savePath) ? savePath : null,
            true);
        AddChild(session);
        var player = new CellPlayer();
        player.Configure(0.0f, session, true, false);
        AddChild(player);
        session.PrepareXrStartingLoadout(new GameplaySession.StartingWeapon(
            "0000434f",
            "Weap10mmPistol",
            "00004241",
            "Ammo10mm",
            22,
            12,
            12));
        if (!session.Fire(player.RightHand!) || !session.Reload())
            throw new InvalidOperationException("OpenNV OpenXR fire/reload contract failed.");
        var xrHud = player.LeftHand!.FindChild("XrObjectiveInventory", true, false);
        if (!player.UsesXr || player.Camera is not XRCamera3D || player.XrOrigin is null ||
            player.RightHand is null || player.XrRenderModels is not null || xrHud is not Label3D ||
            player.XrOrigin.WorldScale != 1.0f)
            throw new InvalidOperationException("OpenNV OpenXR rig hierarchy is incomplete.");

        var report = new
        {
            schema = "opennv-openxr-rig/v2",
            status = "pass",
            initializedRuntimeRequiredForPlay = true,
            viewportXrEnabledDuringProof = GetViewport().UseXR,
            actionMap = "res://openxr_action_map.tres",
            actionSets = actionSets.Count,
            actions = actions.Count,
            actionNames,
            testedInteractionProfiles = interactionProfiles,
            originType = player.XrOrigin.GetClass().ToString(),
            cameraType = player.Camera.GetClass().ToString(),
            leftControllerType = player.LeftHand.GetClass().ToString(),
            rightControllerType = player.RightHand.GetClass().ToString(),
            controllerRenderModelManagerType = nameof(OpenXRRenderModelManager),
            controllerRenderModelsRequireLiveRuntime = true,
            leftTracker = player.LeftHand.Tracker.ToString(),
            rightTracker = player.RightHand.Tracker.ToString(),
            controllerPose = player.RightHand.Pose.ToString(),
            worldScale = player.XrOrigin.WorldScale,
            desiredEyeHeightMeters = CellPlayer.XrDesiredEyeHeightMeters,
            physicsTicksPerSecond = Engine.PhysicsTicksPerSecond,
            worldSpaceHud = xrHud is Label3D,
            sharedSaveSchema = session.Report(),
        };
        if (options.TryGetValue("report", out var reportPath))
            WriteReport(reportPath, report);
        GD.Print("OPENNV_OPENXR_RIG_PASS profiles=generic,oculus-touch worldScale=1 physicsHz=90");
        GetTree().Quit(0);
    }

    private void CompleteClassicDioramaRigProof(IReadOnlyDictionary<string, string> options)
    {
        var session = new GameplaySession();
        session.Configure(
            "classic-diorama-rig-proof",
            options.TryGetValue("save-path", out var savePath) ? savePath : null,
            false,
            true);
        AddChild(session);
        var player = new CellPlayer();
        player.Configure(0.0f, session, false, false, true);
        AddChild(player);

        if (!player.UsesClassicDiorama || player.UsesXr || player.Camera is XRCamera3D ||
            player.Camera.Projection != Camera3D.ProjectionType.Orthogonal ||
            player.DioramaOrbit is null ||
            !Mathf.IsEqualApprox(player.Camera.Size, CellPlayer.DioramaInitialSizeMeters))
            throw new InvalidOperationException("Classic Diorama camera hierarchy or projection is invalid.");

        var initialYaw = player.DioramaTargetYawRadians;
        var initialSize = player.DioramaTargetSizeMeters;
        player._UnhandledInput(new InputEventKey
        {
            PhysicalKeycode = Key.E,
            Pressed = true,
        });
        player._UnhandledInput(new InputEventMouseButton
        {
            ButtonIndex = MouseButton.WheelUp,
            Pressed = true,
        });
        var expectedYaw = initialYaw - CellPlayer.DioramaYawStepRadians;
        if (!Mathf.IsEqualApprox(player.DioramaTargetYawRadians, expectedYaw) ||
            player.DioramaTargetSizeMeters >= initialSize ||
            player.DioramaTargetSizeMeters < CellPlayer.DioramaMinimumSizeMeters)
            throw new InvalidOperationException("Classic Diorama rotation or zoom input contract failed.");

        var report = new
        {
            schema = "opennv-classic-diorama-rig/v1",
            status = "pass",
            presentation = "classic-diorama",
            simulation = "shared-gameplay-session",
            cameraType = player.Camera.GetType().Name,
            cameraName = player.Camera.Name.ToString(),
            orbitName = player.DioramaOrbit.Name.ToString(),
            projection = "orthogonal",
            initialSizeMeters = CellPlayer.DioramaInitialSizeMeters,
            minimumSizeMeters = CellPlayer.DioramaMinimumSizeMeters,
            maximumSizeMeters = CellPlayer.DioramaMaximumSizeMeters,
            zoomedSizeMeters = player.DioramaTargetSizeMeters,
            yawStepDegrees = Mathf.RadToDeg(CellPlayer.DioramaYawStepRadians),
            targetYawAfterProofDegrees = Mathf.RadToDeg(player.DioramaTargetYawRadians),
            panSpeedMetersPerSecond = CellPlayer.DioramaPanSpeedMetersPerSecond,
            panKeys = new[] { "W", "A", "S", "D" },
            rotationKeys = new[] { "Q", "E" },
            zoomInput = "mouse-wheel",
            resetKey = "Home",
            gameplaySession = session.Report(),
            turnSimulationConnected = false,
            noRetailData = true,
        };
        if (options.TryGetValue("report", out var reportPath))
            WriteReport(reportPath, report);
        GD.Print(
            $"OPENNV_CLASSIC_DIORAMA_RIG_PASS projection=orthogonal " +
            $"size={CellPlayer.DioramaInitialSizeMeters:F1} yawStep=60 panKeys=WASD");
        GetTree().Quit(0);
    }

    private async Task RunGameplayProof(
        CellSceneLoader.LoadedCell loaded,
        string scenePath,
        IReadOnlyDictionary<string, string> options)
    {
        try
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            var revolver = loaded.Pickups.Values.Single(pickup => pickup.ItemFormId == "0008f216");
            loaded.Session.Collect(revolver);
            loaded.Session.Fire(loaded.Player.Camera);
            var aid = loaded.Pickups.Values.First(pickup => pickup.EditorId == "Beer");
            loaded.Session.Collect(aid);
            var container = loaded.Containers.Values.Single(candidate => candidate.EditorId == "SSCrateContainerFull");
            loaded.Session.OpenContainer(container);
            loaded.ProofDoor.SetOpen(true);
            loaded.Session.DoorChanged(loaded.ProofDoor);
            if (!loaded.Session.ObjectiveComplete || loaded.Session.ShotsFired != 1 ||
                loaded.Session.AmmoInMagazine != 5 || !loaded.Session.HasItem("00103b1e") ||
                !loaded.Session.IsContainerEmptied("0010873e") || !File.Exists(loaded.Session.SavePath))
                throw new InvalidOperationException("Playable route did not reach its persisted completion state.");
            WriteGameplayReport("first-run", loaded, scenePath, options);
            GetTree().Quit(0);
        }
        catch (Exception exception)
        {
            GD.PushError($"OPENNV_GODOT_GAMEPLAY_PROOF_FAIL {exception.Message}");
            GetTree().Quit(1);
        }
    }

    private void CompleteGameplayReloadProof(
        CellSceneLoader.LoadedCell loaded,
        string scenePath,
        IReadOnlyDictionary<string, string> options)
    {
        if (!loaded.Session.ObjectiveComplete || loaded.Session.ShotsFired != 1 ||
            loaded.Session.AmmoInMagazine != 5 || !loaded.ProofDoor.IsOpen ||
            !loaded.Session.HasItem("00103b1e") || !loaded.Session.IsContainerEmptied("0010873e") ||
            loaded.Pickups.Values.Any(pickup => pickup.ItemFormId == "0008f216"))
            throw new InvalidOperationException("Cold reload did not restore the completed playable route.");
        WriteGameplayReport("cold-reload", loaded, scenePath, options);
        GetTree().Quit(0);
    }

    private static void WriteGameplayReport(
        string phase,
        CellSceneLoader.LoadedCell loaded,
        string scenePath,
        IReadOnlyDictionary<string, string> options)
    {
        var report = new
        {
            schema = "opennv-godot-playable-route/v1",
            status = "pass",
            phase,
            scene = scenePath,
            cellFormId = loaded.FormId,
            cellEditorId = loaded.EditorId,
            route = new[]
            {
                "pickup-revolver",
                "fire-physical-ray",
                "pickup-aid",
                "open-resolved-container",
                "open-entry-door",
            },
            session = loaded.Session.Report(),
            noHostControl = true,
        };
        if (options.TryGetValue("report", out var reportPath))
            WriteReport(reportPath, report);
        GD.Print($"OPENNV_GODOT_PLAYABLE_ROUTE_PASS phase={phase} save={loaded.Session.SavePath}");
    }

    private async Task RunDoorTraversalProof(
        CellSceneLoader.LoadedCell loaded,
        string scenePath,
        IReadOnlyDictionary<string, string> options)
    {
        try
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            var floor = CellSceneLoader.CastSpawnFloor(GetWorld3D().DirectSpaceState);
            if (!floor.Hit || MathF.Abs(floor.Y) > 0.20f)
                throw new InvalidOperationException(
                    $"XTEL floor contract failed: hit={floor.Hit} y={floor.Y} collider={floor.ColliderPath}");
            var ray = CellSceneLoader.BuildProofRay(loaded.ProofDoor);
            var closed = CellSceneLoader.CastProofRay(GetWorld3D().DirectSpaceState, loaded.ProofDoor, ray);
            loaded.ProofDoor.SetOpen(true);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            var opened = CellSceneLoader.CastProofRay(GetWorld3D().DirectSpaceState, loaded.ProofDoor, ray);
            if (!closed.Hit || !closed.HitProofDoor || opened.Hit)
                throw new InvalidOperationException(
                    $"Door traversal contract failed: closedHit={closed.Hit} " +
                    $"closedHitDoor={closed.HitProofDoor} closedCollider={closed.ColliderPath} " +
                    $"openHit={opened.Hit} openCollider={opened.ColliderPath} " +
                    $"localSize={ray.LocalSize} localNormal={ray.LocalNormal} from={ray.From} to={ray.To}");
            CompleteCellLoad(
                loaded with { ProofDoorOpen = true },
                scenePath,
                options,
                new DoorTraversalProof(
                    floor.Hit,
                    floor.Y,
                    floor.ColliderPath,
                    closed.Hit,
                    closed.HitProofDoor,
                    opened.Hit));
            GetTree().Quit(0);
        }
        catch (Exception exception)
        {
            GD.PushError($"OPENNV_GODOT_DOOR_TRAVERSAL_FAIL {exception.Message}");
            GetTree().Quit(1);
        }
    }

    private void CompleteCellLoad(
        CellSceneLoader.LoadedCell loaded,
        string scenePath,
        IReadOnlyDictionary<string, string> options,
        DoorTraversalProof? traversalProof)
    {
        var report = new
        {
            schema = "opennv-godot-cell/v1",
            status = "pass",
            renderer = "forward_plus",
            scene = scenePath,
            cellFormId = loaded.FormId,
            cellEditorId = loaded.EditorId,
            assets = loaded.Assets,
            textures = loaded.Textures,
            materialBindings = loaded.MaterialBindings,
            references = loaded.References,
            doors = loaded.Doors,
            authoredLights = loaded.AuthoredLights,
            actors = loaded.Actors.Count,
            xrPresentation = !loaded.Player.UsesXr
                ? null
                : new
                {
                    heldWeapon = loaded.Player.HasHeldWeapon,
                    muzzleFeedback = loaded.Player.HasMuzzleFeedback,
                    wristHud = loaded.Session.HasXrHud,
                    wristHudPixelSize = loaded.Session.XrHudPixelSize,
                    startingLoadout = loaded.Session.Report(),
                },
            classicDioramaPresentation = !loaded.Player.UsesClassicDiorama
                ? null
                : new
                {
                    projection = "orthogonal",
                    cameraName = loaded.Player.Camera.Name.ToString(),
                    orbitName = loaded.Player.DioramaOrbit!.Name.ToString(),
                    sizeMeters = loaded.Player.Camera.Size,
                    targetSizeMeters = loaded.Player.DioramaTargetSizeMeters,
                    yawStepDegrees = Mathf.RadToDeg(CellPlayer.DioramaYawStepRadians),
                    panSpeedMetersPerSecond = CellPlayer.DioramaPanSpeedMetersPerSecond,
                    framingBoundsPosition = loaded.Player.DioramaFramingBounds is Aabb bounds
                        ? new[] { bounds.Position.X, bounds.Position.Y, bounds.Position.Z }
                        : null,
                    framingBoundsSize = loaded.Player.DioramaFramingBounds is Aabb framing
                        ? new[] { framing.Size.X, framing.Size.Y, framing.Size.Z }
                        : null,
                    cameraFill = loaded.Player.Camera.FindChild(
                        "ClassicDioramaCameraFill",
                        true,
                        false) is DirectionalLight3D,
                    turnSimulationConnected = false,
                },
            collisionMeshes = loaded.CollisionMeshes,
            surfaces = loaded.Surfaces,
            vertices = loaded.Vertices,
            spawnSource = "XTEL",
            spawnAtFloorOrigin = true,
            proofDoorFormId = loaded.ProofDoorFormId,
            proofDoorOpen = loaded.ProofDoorOpen,
            wholeCellVisible = true,
            doorTraversal = traversalProof is null
                ? null
                : new
                {
                    status = "pass",
                    floorHit = traversalProof.Value.FloorHit,
                    floorY = traversalProof.Value.FloorY,
                    floorCollider = traversalProof.Value.FloorCollider,
                    closedHit = traversalProof.Value.ClosedHit,
                    closedHitDoor = traversalProof.Value.ClosedHitDoor,
                    openHit = traversalProof.Value.OpenHit,
                },
        };
        if (options.TryGetValue("report", out var reportPath))
            WriteReport(reportPath, report);
        GD.Print(
            $"OPENNV_GODOT_CELL_PASS cell={loaded.FormId} assets={loaded.Assets} textures={loaded.Textures} " +
            $"materials={loaded.MaterialBindings} " +
            $"references={loaded.References} doors={loaded.Doors} lights={loaded.AuthoredLights} " +
            $"collision={loaded.CollisionMeshes} " +
            $"surfaces={loaded.Surfaces} vertices={loaded.Vertices} proofDoorOpen={loaded.ProofDoorOpen} " +
            $"presentation={(loaded.Player.UsesClassicDiorama ? "classic-diorama" : loaded.Player.UsesXr ? "openxr" : "flat-first-person")} " +
            $"doorTraversal={(traversalProof is null ? "not-requested" : "pass")}");
        if (options.ContainsKey("quit-after-load"))
            GetTree().Quit(0);
    }

    private void LoadModel(
        string modelPath,
        string sidecarPath,
        IReadOnlyDictionary<string, string> options)
    {
        var loaded = StaticModelSlice.Load(
            modelPath,
            sidecarPath,
            this,
            options.TryGetValue("material-manifest", out var materials) ? materials : null,
            options.TryGetValue("material-manifest-sha256", out var materialsHash) ? materialsHash : null,
            options.ContainsKey("classic-diorama"));
        var report = new
        {
            schema = "opennv-godot-static-model/v1",
            status = "pass",
            renderer = "forward_plus",
            model = modelPath,
            sidecar = sidecarPath,
            sourceSha256 = loaded.SourceSha256,
            meshes = loaded.Meshes,
            surfaces = loaded.Surfaces,
            vertices = loaded.Vertices,
            materialBindings = loaded.MaterialBindings,
            presentation = options.ContainsKey("classic-diorama") ? "classic-diorama" : "reference",
            projection = loaded.Projection,
            boundsPosition = new[] { loaded.Bounds.Position.X, loaded.Bounds.Position.Y, loaded.Bounds.Position.Z },
            boundsSize = new[] { loaded.Bounds.Size.X, loaded.Bounds.Size.Y, loaded.Bounds.Size.Z },
        };
        if (options.TryGetValue("report", out var reportPath))
            WriteReport(reportPath, report);
        GD.Print(
            $"OPENNV_GODOT_STATIC_MODEL_PASS source={loaded.SourceSha256} " +
            $"meshes={loaded.Meshes} surfaces={loaded.Surfaces} vertices={loaded.Vertices} " +
            $"materials={loaded.MaterialBindings} projection={loaded.Projection}");
        if (options.TryGetValue("capture-root", out var captureRoot))
        {
            _ = StaticModelCapture.Run(
                this,
                loaded,
                modelPath,
                captureRoot,
                options.TryGetValue("report", out var captureReport) ? captureReport : null);
            return;
        }
        if (options.ContainsKey("quit-after-load"))
            GetTree().Quit(0);
    }

    private void LoadActorModel(
        string modelPath,
        string sidecarPath,
        IReadOnlyDictionary<string, string> options)
    {
        var loaded = ActorModelSlice.Load(modelPath, sidecarPath, this);
        var report = new
        {
            schema = "opennv-godot-actor/v1",
            status = "pass",
            renderer = "forward_plus",
            model = modelPath,
            sidecar = sidecarPath,
            actorFormId = loaded.FormId,
            actorName = loaded.Name,
            meshes = loaded.Meshes,
            skeletons = loaded.Skeletons,
            animations = loaded.Animations,
            playingAnimation = loaded.PlayingAnimation,
            boundsMinimum = new[] { loaded.Bounds.Position.X, loaded.Bounds.Position.Y, loaded.Bounds.Position.Z },
            boundsSize = new[] { loaded.Bounds.Size.X, loaded.Bounds.Size.Y, loaded.Bounds.Size.Z },
            heightMeters = loaded.Bounds.Size.Y,
            authoredSurfaces = loaded.AuthoredSurfaces,
            authoredTextures = loaded.AuthoredTextures,
        };
        if (options.TryGetValue("report", out var reportPath))
            WriteReport(reportPath, report);
        GD.Print(
            $"OPENNV_GODOT_ACTOR_PASS form={loaded.FormId} meshes={loaded.Meshes} " +
            $"skeletons={loaded.Skeletons} animations={loaded.Animations} playing={loaded.PlayingAnimation}");
        if (options.ContainsKey("quit-after-load"))
            GetTree().Quit(0);
    }

    private void ShowExperimentalStatus(string? restoreError)
    {
        _setupView = new LegalAssetSetupView();
        _setupView.Configure(restoreError, OnDataRootSelected);
        AddChild(_setupView);
    }

    private async void OnDataRootSelected(string dataRoot)
    {
        _setupView!.SetPreparing();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        PrepareSelectedData(dataRoot);
    }

    private void PrepareSelectedData(string dataRoot)
    {
        try
        {
            var prepared = LegalAssetPreparer.Prepare(dataRoot, _options);
            LoadPrepared(prepared, _options);
            _setupView?.QueueFree();
            _setupView = null;
        }
        catch (Exception exception)
        {
            _setupView!.ShowError();
            GD.PushError($"OPENNV_LEGAL_ASSET_SETUP_FAIL {exception.Message}");
        }
    }

    private static void WriteStartupReport(string reportPath)
    {
        WriteReport(reportPath, new
        {
            schema = "opennv-godot-startup/v1",
            status = "experimental",
            playable = false,
            playableSandbox = true,
            openXrLaunchable = true,
            openXrHardwareValidated = false,
            engine = "Godot 4.7.2 Forward+",
        });
    }

    private static void WriteReport(string reportPath, object report)
    {
        var fullReportPath = Path.GetFullPath(reportPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullReportPath)!);
        File.WriteAllText(fullReportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions
        {
            WriteIndented = true,
        }) + System.Environment.NewLine);
    }

    private static Dictionary<string, string> ParseOptions(string[] arguments)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < arguments.Length; index++)
        {
            var argument = arguments[index];
            if (!argument.StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException($"Unexpected runtime argument: {argument}");
            var name = argument[2..];
            var value = index + 1 < arguments.Length && !arguments[index + 1].StartsWith("--", StringComparison.Ordinal)
                ? arguments[++index]
                : "true";
            result.Add(name, value);
        }
        return result;
    }

    private static string RequireOption(IReadOnlyDictionary<string, string> options, string name) =>
        options.TryGetValue(name, out var value)
            ? value
            : throw new ArgumentException($"Missing required --{name} option.");

    private readonly record struct DoorTraversalProof(
        bool FloorHit,
        float FloorY,
        string FloorCollider,
        bool ClosedHit,
        bool ClosedHitDoor,
        bool OpenHit);
}
