using System.Text.Json;
using Godot;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator : Node3D
{
    private Dictionary<string, string> _options = new(StringComparer.OrdinalIgnoreCase);
    private RuntimeConfiguration _configuration = null!;
    private LegalAssetSetupView? _setupView;

    public override void _Ready()
    {
        Callable.From(StartRuntime).CallDeferred();
    }

    private void StartRuntime()
    {
        try
        {
            _configuration = RuntimeConfiguration.Load();
            GetWindow().Size = new Vector2I(
                _configuration.Capture.ExpectedWidthPixels,
                _configuration.Capture.ExpectedHeightPixels);
            RenderingServer.SetDefaultClearColor(_configuration.Renderer.BackgroundColorRgba.Color());
            Engine.PhysicsTicksPerSecond = _configuration.Simulation.PhysicsTicksPerSecond;
            _options = ParseOptions(OS.GetCmdlineUserArgs());
            if (_options.ContainsKey("vr") && _options.ContainsKey("xr-rig-proof"))
                throw new ArgumentException("Use --vr for a live OpenXR session or --xr-rig-proof for the headless layout gate, not both.");
            if (_options.ContainsKey("vr"))
                EnableOpenXr();
            if (_options.ContainsKey("xr-rig-proof"))
            {
                CompleteXrRigProof(_options);
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
                var prepared = LegalAssetPreparer.Prepare(dataRoot!, _options, _configuration);
                LoadPrepared(prepared, _options);
                return;
            }

            if (hasModel)
            {
                LoadModel(RequireOption(_options, "model"), RequireOption(_options, "sidecar"), _options);
                return;
            }

            if (hasCellScene)
            {
                LoadCellScene(RequireOption(_options, "cell-scene"), _options);
                return;
            }

            if (hasActorModel)
            {
                LoadActorModel(
                    RequireOption(_options, "actor-model"),
                    RequireOption(_options, "actor-sidecar"),
                    _options);
                return;
            }

            if (_options.ContainsKey("reuse-cache"))
            {
                if (!LegalAssetPreparer.TryRestore(
                        _options,
                        _configuration,
                        out var restored,
                        out var restoreError))
                    throw new InvalidOperationException(restoreError ?? "No prepared legal-asset cache exists.");
                LoadPrepared(restored, _options);
                return;
            }

            if (_options.TryGetValue("report", out var startupReportPath))
                WriteStartupReport(startupReportPath);
            GD.Print("OPENNV_GODOT_EXPERIMENTAL_READY playable=0 playableSandbox=1 openxr=experimental");
            if (DisplayServer.GetName() == "headless")
                GetTree().Quit(0);
            else if (LegalAssetPreparer.TryRestore(
                         _options,
                         _configuration,
                         out var restored,
                         out var restoreError))
                LoadPrepared(restored, _options);
            else
                ShowExperimentalStatus(restoreError);
        }
        catch (Exception exception)
        {
            GD.PushError($"OPENNV_GODOT_RUNTIME_FAIL {exception.Message}");
            GetTree().Quit(1);
        }
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
        var loaded = CellSceneLoader.Load(
            scenePath,
            this,
            _configuration,
            !runTraversalProof && options.ContainsKey("open-proof-door"),
            options.TryGetValue("proof-door", out var proofDoor) ? proofDoor : null,
            options.TryGetValue("save-path", out var savePath) ? savePath : null,
            useXrLayout,
            options.ContainsKey("vr"),
            options.TryGetValue("actor-scene", out var actorScene) ? actorScene : null,
            options.TryGetValue("actor-scenes", out var actorScenes) ? actorScenes : null,
            options.ContainsKey("proof-enable-actor"),
            !options.ContainsKey("capture-root"));
        if (options.TryGetValue("capture-root", out var captureRoot))
        {
            _ = EnvironmentCapture.Run(
                this,
                loaded,
                _configuration,
                captureRoot,
                scenePath,
                options.TryGetValue("report", out var captureReport) ? captureReport : null,
                options.TryGetValue("retail-state-contract", out var retailState) ? retailState : null);
            return;
        }
        if (options.ContainsKey("pool-proof"))
        {
            _ = RunPoolProof(loaded, scenePath, options);
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

    private async Task RunPoolProof(
        CellSceneLoader.LoadedCell loaded,
        string scenePath,
        IReadOnlyDictionary<string, string> options)
    {
        try
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            if (loaded.Pools.Count != 1)
                throw new InvalidOperationException("Pool proof requires one configured table.");
            var table = loaded.Pools.Values.Single();
            if (table.BallCount < 1 || table.Balls.Any(ball =>
                    ball.Mass <= 0.0f || ball.CollisionRadiusMeters <= 0.0f))
                throw new InvalidOperationException("Pool proof found an incomplete authored ball body.");
            var authoredPositions = table.Balls.ToDictionary(
                ball => ball.ReferenceFormId,
                ball => ball.AuthoredTransform.Origin,
                StringComparer.OrdinalIgnoreCase);
            loaded.Player.EnterPoolForProof(table);
            if (!loaded.Player.HasHeldPoolCue)
                throw new InvalidOperationException("Pool proof did not mount the authored cue.");
            var cueMounted = loaded.Player.HasHeldPoolCue;

            var objectBall = table.Balls
                .Where(ball => ball.Role == "object-ball")
                .OrderBy(ball => new Vector2(
                    ball.GlobalPosition.X - table.CueBall.GlobalPosition.X,
                    ball.GlobalPosition.Z - table.CueBall.GlobalPosition.Z).LengthSquared())
                .First();
            var direction = objectBall.GlobalPosition - table.CueBall.GlobalPosition;
            direction.Y = 0.0f;
            if (direction.IsZeroApprox())
                throw new InvalidOperationException("Authored pool-ball placement has no strike direction.");
            direction = direction.Normalized();
            table.CueBall.ClearBallCollisionEvidence();
            var xrLayout = loaded.Player.UsesXr;
            bool struck;
            if (xrLayout)
            {
                var radius = table.CueBall.CollisionRadiusMeters;
                var timestep = 1.0 / _configuration.Simulation.PhysicsTicksPerSecond;
                table.UpdateTrackedCue(
                    table.CueBall.GlobalPosition - direction * radius * 2.0f,
                    true,
                    timestep);
                struck = table.UpdateTrackedCue(
                    table.CueBall.GlobalPosition + direction * radius,
                    true,
                    timestep);
            }
            else
            {
                table.SelectMaximumFlatPowerForProof();
                struck = table.StrikeFlat(direction);
            }
            if (!struck)
                throw new InvalidOperationException("Pool input adapter did not produce a shared strike.");
            for (var frame = 0;
                 frame < _configuration.Pool.ProofMaximumPhysicsFrames &&
                    table.CueBall.BallCollisionCount == 0;
                 frame++)
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            if (table.CueBall.BallCollisionCount == 0)
                throw new InvalidOperationException(
                    "Pool cue ball did not collide with an authored object ball: " +
                    $"cuePosition={table.CueBall.GlobalPosition} " +
                    $"cueVelocity={table.CueBall.LinearVelocity} " +
                    $"cuePocketed={table.CueBall.IsPocketed} " +
                    $"targetPosition={objectBall.GlobalPosition} " +
                    $"targetPocketed={objectBall.IsPocketed} " +
                    $"travelFromAuthored={table.CueBall.GlobalPosition.DistanceTo(authoredPositions[table.CueBall.ReferenceFormId]):F4}");
            var cueBallBallCollisions = table.CueBall.BallCollisionCount;
            var travelled = table.CueBall.GlobalPosition.DistanceTo(
                authoredPositions[table.CueBall.ReferenceFormId]);
            if (Mathf.IsZeroApprox(travelled))
                throw new InvalidOperationException("Pool cue ball did not move after the accepted strike.");

            table.ResetAuthored();
            if (table.Balls.Any(ball => !ball.GlobalPosition.IsEqualApprox(
                    authoredPositions[ball.ReferenceFormId])))
                throw new InvalidOperationException("Pool reset did not restore authored reference transforms.");
            loaded.Player.ExitPoolForProof();
            loaded.Session.Save();
            if (!File.Exists(loaded.Session.SavePath))
                throw new InvalidOperationException("Pool state was not persisted by the shared session.");

            var report = new
            {
                schema = "opennv-pool-practice/v1",
                status = "pass",
                configurationSchema = RuntimeConfiguration.ExpectedSchema,
                configurationSha256 = _configuration.Sha256,
                scene = scenePath,
                cellFormId = loaded.FormId,
                tableReferenceFormId = table.ReferenceFormId,
                presentationModelPath = table.PresentationModelPath,
                gameplayCollisionSource = table.GameplayCollisionSource,
                authoredBalls = table.BallCount,
                dynamicConvexBodies = table.Balls.Count,
                massKilograms = table.Balls.Select(ball => ball.Mass).ToArray(),
                collisionRadiusMeters = table.Balls.Select(ball => ball.CollisionRadiusMeters).ToArray(),
                inputAdapter = xrLayout ? "openxr-tracked-cue-layout" : "desktop-look-and-power",
                configuredDesktopStrikeMetersPerSecond = table.SelectedFlatPowerMetersPerSecond,
                sharedSimulation = true,
                cueMounted,
                strikeAccepted = struck,
                cueBallBallCollisions,
                cueBallTravelMeters = travelled,
                authoredReset = true,
                savePath = loaded.Session.SavePath,
                hardwareValidated = false,
            };
            if (options.TryGetValue("report", out var reportPath))
                WriteReport(reportPath, report);
            GD.Print(
                $"OPENNV_POOL_PRACTICE_PASS adapter={report.inputAdapter} " +
                $"balls={table.BallCount} travel={travelled:F4}");
            GetTree().Quit(0);
        }
        catch (Exception exception)
        {
            GD.PushError($"OPENNV_POOL_PRACTICE_FAIL {exception.Message}");
            GetTree().Quit(1);
        }
    }

    private void EnableOpenXr()
    {
        var openXr = XRServer.FindInterface("OpenXR");
        if (openXr is null || !openXr.IsInitialized())
            throw new InvalidOperationException(
                "OpenXR was requested but no initialized runtime is available. " +
                "Launch with --xr-mode on before --, connect the headset, and verify the active OpenXR runtime.");
        GetViewport().UseXR = true;
        Engine.PhysicsTicksPerSecond = _configuration.Simulation.PhysicsTicksPerSecond;
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        GD.Print(
            $"OPENNV_OPENXR_READY interface=OpenXR worldScale={_configuration.Xr.WorldScale} " +
            $"physicsHz={_configuration.Simulation.PhysicsTicksPerSecond}");
    }

    private void CompleteXrRigProof(IReadOnlyDictionary<string, string> options)
    {
        var contract = _configuration.Xr.Contract;
        var proof = _configuration.Xr.DiagnosticRigProof;
        var actionMap = ResourceLoader.Load(contract.ActionMapResourcePath)
            ?? throw new InvalidOperationException("OpenNV OpenXR action map could not be loaded.");
        var actionSets = actionMap.Get("action_sets").AsGodotArray();
        if (actionSets.Count != contract.ExpectedActionSetCount)
            throw new InvalidOperationException("OpenNV OpenXR action-map set count disagrees with configuration.");
        var actionSet = actionSets[0].AsGodotObject() as Resource
            ?? throw new InvalidOperationException("OpenNV OpenXR gameplay action set is invalid.");
        var actions = actionSet.Get("actions").AsGodotArray();
        if (actions.Count != contract.ActionNames.Count)
            throw new InvalidOperationException("OpenNV OpenXR action count disagrees with configuration.");
        var actionNames = actions
            .Select(value => value.AsGodotObject() as Resource
                ?? throw new InvalidOperationException("OpenNV OpenXR action is invalid."))
            .Select(action => action.ResourceName)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        var expectedActions = contract.ActionNames.OrderBy(value => value, StringComparer.Ordinal).ToArray();
        if (!actionNames.SequenceEqual(expectedActions, StringComparer.Ordinal))
            throw new InvalidOperationException("OpenNV OpenXR action names are incomplete.");
        var interactionProfiles = actionMap.Get("interaction_profiles").AsGodotArray()
            .Select(value => value.AsGodotObject() as Resource
                ?? throw new InvalidOperationException("OpenNV OpenXR interaction profile is invalid."))
            .Select(profile => profile.Get("interaction_profile_path").AsString())
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        var expectedProfiles = contract.InteractionProfilePaths
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        if (!interactionProfiles.SequenceEqual(expectedProfiles, StringComparer.Ordinal))
            throw new InvalidOperationException("OpenNV OpenXR interaction profile set is incomplete.");

        Engine.PhysicsTicksPerSecond = _configuration.Simulation.PhysicsTicksPerSecond;
        var session = new GameplaySession();
        session.Configure(
            proof.SessionId,
            proof.SessionId,
            _configuration,
            options.TryGetValue("save-path", out var savePath) ? savePath : null,
            true);
        AddChild(session);
        var player = new CellPlayer();
        player.Configure(0.0f, session, _configuration, true, false);
        AddChild(player);
        session.PrepareXrStartingLoadout(new GameplaySession.StartingWeapon(
            proof.WeaponFormId,
            proof.WeaponEditorId,
            proof.AmmoFormId,
            proof.AmmoEditorId,
            proof.Damage,
            proof.ClipSize,
            proof.ReserveRounds));
        if (!session.Fire(player.RightHand!) || !session.Reload())
            throw new InvalidOperationException("OpenNV OpenXR fire/reload contract failed.");
        if (session.ShotsFired != proof.ExpectedShotsFired ||
            session.AmmoInMagazine != proof.ExpectedAmmoInMagazineAfterReload ||
            session.ReserveAmmo != proof.ExpectedReserveRoundsAfterReload)
            throw new InvalidOperationException("OpenNV OpenXR ammunition outcome disagrees with configuration.");
        var xrHud = player.LeftHand!.FindChild("XrObjectiveInventory", true, false);
        if (!player.UsesXr || player.Camera is not XRCamera3D || player.XrOrigin is null ||
            player.RightHand is null || player.XrRenderModels is not null || xrHud is not Label3D ||
            !Mathf.IsEqualApprox(player.XrOrigin.WorldScale, _configuration.Xr.WorldScale))
            throw new InvalidOperationException("OpenNV OpenXR rig hierarchy is incomplete.");

        var report = new
        {
            schema = "opennv-openxr-rig/v2",
            status = "pass",
            configurationSchema = RuntimeConfiguration.ExpectedSchema,
            configurationSha256 = _configuration.Sha256,
            initializedRuntimeRequiredForPlay = true,
            viewportXrEnabledDuringProof = GetViewport().UseXR,
            actionMap = contract.ActionMapResourcePath,
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
            desiredEyeHeightMeters = player.DesiredEyeHeightMeters,
            physicsTicksPerSecond = Engine.PhysicsTicksPerSecond,
            worldSpaceHud = xrHud is Label3D,
            sharedSaveSchema = session.Report(),
        };
        if (options.TryGetValue("report", out var reportPath))
            WriteReport(reportPath, report);
        GD.Print(
            $"OPENNV_OPENXR_RIG_PASS profiles=generic,oculus-touch " +
            $"worldScale={_configuration.Xr.WorldScale} " +
            $"physicsHz={_configuration.Simulation.PhysicsTicksPerSecond}");
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
            var route = _configuration.Proof.GameplayRoute;
            var revolver = loaded.Pickups.Values.Single(
                pickup => pickup.ItemFormId == route.WeaponPickupFormId);
            loaded.Session.Collect(revolver);
            loaded.Session.Fire(loaded.Player.Camera);
            var aid = loaded.Pickups.Values.First(
                pickup => pickup.EditorId == route.AidPickupEditorId);
            loaded.Session.Collect(aid);
            var container = loaded.Containers.Values.Single(
                candidate => candidate.EditorId == route.ContainerEditorId);
            loaded.Session.OpenContainer(container);
            loaded.ProofDoor.SetOpen(true);
            loaded.Session.DoorChanged(loaded.ProofDoor);
            if (!loaded.Session.ObjectiveComplete ||
                loaded.Session.ShotsFired != route.ExpectedShotsFired ||
                loaded.Session.AmmoInMagazine != route.ExpectedAmmoInMagazine ||
                loaded.Session.EmptiedContainersCount != route.ExpectedEmptiedContainers ||
                loaded.Session.OpenDoorsCount != route.ExpectedOpenDoors ||
                !loaded.Session.HasItem(route.ExpectedInventoryItemFormId) ||
                !loaded.Session.IsContainerEmptied(route.ExpectedContainerReferenceFormId) ||
                !File.Exists(loaded.Session.SavePath))
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
        var route = _configuration.Proof.GameplayRoute;
        if (!loaded.Session.ObjectiveComplete ||
            loaded.Session.ShotsFired != route.ExpectedShotsFired ||
            loaded.Session.AmmoInMagazine != route.ExpectedAmmoInMagazine || !loaded.ProofDoor.IsOpen ||
            loaded.Session.EmptiedContainersCount != route.ExpectedEmptiedContainers ||
            loaded.Session.OpenDoorsCount != route.ExpectedOpenDoors ||
            !loaded.Session.HasItem(route.ExpectedInventoryItemFormId) ||
            !loaded.Session.IsContainerEmptied(route.ExpectedContainerReferenceFormId) ||
            loaded.Pickups.Values.Any(pickup => pickup.ItemFormId == route.WeaponPickupFormId))
            throw new InvalidOperationException("Cold reload did not restore the completed playable route.");
        WriteGameplayReport("cold-reload", loaded, scenePath, options);
        GetTree().Quit(0);
    }

    private void WriteGameplayReport(
        string phase,
        CellSceneLoader.LoadedCell loaded,
        string scenePath,
        IReadOnlyDictionary<string, string> options)
    {
        var report = new
        {
            schema = "opennv-godot-playable-route/v1",
            status = "pass",
            configurationSchema = RuntimeConfiguration.ExpectedSchema,
            configurationSha256 = _configuration.Sha256,
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
            var floor = CellSceneLoader.CastSpawnFloor(
                GetWorld3D().DirectSpaceState,
                _configuration.Proof,
                loaded.Player.CollisionMask,
                loaded.Player.GetRid());
            if (!floor.Hit || MathF.Abs(floor.Y) > _configuration.Proof.SpawnFloorToleranceMeters)
                throw new InvalidOperationException(
                    $"XTEL floor contract failed: hit={floor.Hit} y={floor.Y} collider={floor.ColliderPath}");
            var ray = CellSceneLoader.BuildProofRay(loaded.ProofDoor, _configuration.Proof);
            var closed = CellSceneLoader.CastProofRay(
                GetWorld3D().DirectSpaceState,
                loaded.ProofDoor,
                ray,
                _configuration.Player.CollisionMask);
            loaded.ProofDoor.SetOpen(true);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            var opened = CellSceneLoader.CastProofRay(GetWorld3D().DirectSpaceState, loaded.ProofDoor, ray);
            var portalDirection = (ray.To - ray.From).Normalized();
            var portalCenter = (ray.From + ray.To) / 2.0f;
            var projectileRay = new CellSceneLoader.DoorRay(
                portalCenter - portalDirection * _configuration.Proof.ProjectileRayStartMeters,
                portalCenter + portalDirection * _configuration.Proof.ProjectileRayEndMeters,
                ray.LocalSize,
                ray.LocalNormal);
            var projectileHit = CellSceneLoader.CastProofRay(
                GetWorld3D().DirectSpaceState,
                loaded.ProofDoor,
                projectileRay);
            var projectileBlockedByDoor = projectileHit.HitProofDoor || loaded.PortalLinks.Any(portal =>
                projectileHit.ColliderPath.StartsWith(portal.ToDoor.GetPath().ToString(), StringComparison.Ordinal));
            portalCenter.Y = _configuration.Proof.PortalCapsuleCenterHeightMeters;
            var portalMotion = portalDirection * _configuration.Proof.PortalCapsuleMotionMeters;
            var forwardCollision = new KinematicCollision3D();
            var walkForwardBlocked = loaded.PortalLinks.Count > 0 && loaded.Player.TestMove(
                new Transform3D(Basis.Identity, portalCenter - portalMotion / 2.0f),
                portalMotion,
                forwardCollision);
            var backwardCollision = new KinematicCollision3D();
            var walkBackwardBlocked = loaded.PortalLinks.Count > 0 && loaded.Player.TestMove(
                new Transform3D(Basis.Identity, portalCenter + portalMotion / 2.0f),
                -portalMotion,
                backwardCollision);
            var forwardCollider = walkForwardBlocked
                ? (forwardCollision.GetCollider() as Node)?.GetPath().ToString() ?? "unknown"
                : "";
            var backwardCollider = walkBackwardBlocked
                ? (backwardCollision.GetCollider() as Node)?.GetPath().ToString() ?? "unknown"
                : "";
            var forwardNormal = walkForwardBlocked ? forwardCollision.GetNormal() : Vector3.Zero;
            var backwardNormal = walkBackwardBlocked ? backwardCollision.GetNormal() : Vector3.Zero;
            var linkedDoorBlocked = loaded.PortalLinks.Any(portal =>
                opened.ColliderPath.StartsWith(portal.ToDoor.GetPath().ToString(), StringComparison.Ordinal));
            var requiresEmptyOpenRay = loaded.PortalLinks.Count == 0;
            if (!closed.Hit || !closed.HitProofDoor || opened.HitProofDoor || linkedDoorBlocked ||
                (requiresEmptyOpenRay && opened.Hit) ||
                projectileBlockedByDoor ||
                walkForwardBlocked ||
                (walkBackwardBlocked &&
                    backwardNormal.Y < _configuration.Proof.WalkableSurfaceNormalYMinimum) ||
                loaded.PortalLinks.Any(portal => !portal.FromDoor.IsOpen || !portal.ToDoor.IsOpen ||
                    portal.AlignmentErrorMeters > _configuration.Proof.PortalAlignmentToleranceMeters))
                throw new InvalidOperationException(
                    $"Door traversal contract failed: closedHit={closed.Hit} " +
                    $"closedHitDoor={closed.HitProofDoor} closedCollider={closed.ColliderPath} " +
                    $"openHit={opened.Hit} openCollider={opened.ColliderPath} " +
                    $"projectileHit={projectileHit.Hit} projectileCollider={projectileHit.ColliderPath} " +
                    $"projectileBlockedByDoor={projectileBlockedByDoor} " +
                    $"walkForwardBlocked={walkForwardBlocked} forwardCollider={forwardCollider} " +
                    $"forwardNormal={forwardNormal} walkBackwardBlocked={walkBackwardBlocked} " +
                    $"backwardCollider={backwardCollider} backwardNormal={backwardNormal} " +
                    $"linkedCells={loaded.LinkedCells.Count} portals={loaded.PortalLinks.Count} " +
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
                    opened.Hit,
                    opened.HitProofDoor || linkedDoorBlocked,
                    !projectileBlockedByDoor,
                    !walkForwardBlocked,
                    !walkBackwardBlocked ||
                        backwardNormal.Y >= _configuration.Proof.WalkableSurfaceNormalYMinimum,
                    !walkForwardBlocked &&
                        (!walkBackwardBlocked ||
                            backwardNormal.Y >= _configuration.Proof.WalkableSurfaceNormalYMinimum),
                    loaded.LinkedCells.Count,
                    loaded.PortalLinks.Count == 0
                        ? null
                        : loaded.PortalLinks.Max(portal => portal.AlignmentErrorMeters)));
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
            configurationSchema = RuntimeConfiguration.ExpectedSchema,
            configurationSha256 = _configuration.Sha256,
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
            poolTables = loaded.Pools.Values.Select(table => new
            {
                referenceFormId = table.ReferenceFormId,
                presentationModelPath = table.PresentationModelPath,
                gameplayCollisionSource = table.GameplayCollisionSource,
                authoredBalls = table.BallCount,
                pocketedBalls = table.PocketedBallCount,
            }).ToArray(),
            linkedCells = loaded.LinkedCells.Select(linked => new
            {
                cellFormId = linked.Content.FormId,
                cellEditorId = linked.Content.EditorId,
                sourceCellFormIds = linked.Content.SourceCellFormIds.OrderBy(value => value).ToArray(),
                assets = linked.Content.Assets,
                references = linked.Content.References,
                actors = linked.Content.Actors.Count,
                collisionMeshes = linked.Content.CollisionMeshes,
            }).ToArray(),
            portals = loaded.PortalLinks.Select(portal => new
            {
                fromDoorReferenceFormId = portal.FromDoor.ReferenceFormId,
                toDoorReferenceFormId = portal.ToDoor.ReferenceFormId,
                reciprocal = portal.FromDoor.DestinationReferenceFormId == portal.ToDoor.ReferenceFormId &&
                    portal.ToDoor.DestinationReferenceFormId == portal.FromDoor.ReferenceFormId,
                alignmentErrorMeters = portal.AlignmentErrorMeters,
                normalAgreement = portal.NormalAgreement,
                bothOpen = portal.FromDoor.IsOpen && portal.ToDoor.IsOpen,
            }).ToArray(),
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
            collisionMeshes = loaded.CollisionMeshes,
            surfaces = loaded.Surfaces,
            vertices = loaded.Vertices,
            spawnSource = "XTEL",
            spawnAtFloorOrigin = true,
            proofDoorFormId = loaded.ProofDoorFormId,
            proofDoorOpen = loaded.ProofDoorOpen,
            wholeCellVisible = true,
            connectedAuthoredSpaces = loaded.LinkedCells.Count > 0,
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
                    openBlockedByPortalDoor = traversalProof.Value.OpenBlockedByPortalDoor,
                    projectilePortalClear = traversalProof.Value.ProjectilePortalClear,
                    capsuleWalkForward = traversalProof.Value.CapsuleWalkForward,
                    capsuleWalkBackward = traversalProof.Value.CapsuleWalkBackward,
                    capsuleWalkThrough = traversalProof.Value.CapsuleWalkThrough,
                    linkedCells = traversalProof.Value.LinkedCells,
                    maximumPortalAlignmentErrorMeters = traversalProof.Value.MaximumPortalAlignmentErrorMeters,
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
            $"linkedCells={loaded.LinkedCells.Count} portals={loaded.PortalLinks.Count} " +
            $"doorTraversal={(traversalProof is null ? "not-requested" : "pass")}");
        if (options.ContainsKey("quit-after-load"))
            GetTree().Quit(0);
    }

    private void LoadModel(
        string modelPath,
        string sidecarPath,
        IReadOnlyDictionary<string, string> options)
    {
        var loaded = StaticModelSlice.Load(modelPath, sidecarPath, this, _configuration);
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
        };
        if (options.TryGetValue("report", out var reportPath))
            WriteReport(reportPath, report);
        GD.Print(
            $"OPENNV_GODOT_STATIC_MODEL_PASS source={loaded.SourceSha256} " +
            $"meshes={loaded.Meshes} surfaces={loaded.Surfaces} vertices={loaded.Vertices}");
        if (options.ContainsKey("quit-after-load"))
            GetTree().Quit(0);
    }

    private void LoadActorModel(
        string modelPath,
        string sidecarPath,
        IReadOnlyDictionary<string, string> options)
    {
        var loaded = ActorModelSlice.Load(modelPath, sidecarPath, this, _configuration);
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
        _setupView.Configure(restoreError, OnDataRootSelected, _configuration.SetupView);
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
            var prepared = LegalAssetPreparer.Prepare(dataRoot, _options, _configuration);
            LoadPrepared(prepared, _options);
            _setupView?.QueueFree();
            _setupView = null;
        }
        catch (Exception exception)
        {
            _setupView!.ShowError(exception.Message);
            GD.PushError($"OPENNV_LEGAL_ASSET_SETUP_FAIL {exception.Message}");
        }
    }

    private void WriteStartupReport(string reportPath)
    {
        WriteReport(reportPath, new
        {
            schema = "opennv-godot-startup/v1",
            status = "experimental",
            playable = false,
            playableSandbox = true,
            openXrLaunchable = true,
            openXrHardwareValidated = false,
            engine = Engine.GetVersionInfo()["string"].AsString(),
            renderer = "forward_plus",
            configurationSchema = RuntimeConfiguration.ExpectedSchema,
            configurationSha256 = _configuration.Sha256,
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
        bool OpenHit,
        bool OpenBlockedByPortalDoor,
        bool ProjectilePortalClear,
        bool CapsuleWalkForward,
        bool CapsuleWalkBackward,
        bool CapsuleWalkThrough,
        int LinkedCells,
        float? MaximumPortalAlignmentErrorMeters);
}
