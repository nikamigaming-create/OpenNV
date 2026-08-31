using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Diagnostics.Performance;
using OpenNV.Runtime.World.Interactions;
using OpenNV.Runtime.World.Portals;
using OpenNV.Runtime.Campaigns.TTW;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    private async Task RunOpeningAcceptance(
        OpeningQuestRuntime opening,
        CellSceneLoader.LoadedCell loaded,
        string scenePath,
        string mode,
        IReadOnlyDictionary<string, string> options)
    {
        try
        {
            if (!double.TryParse(
                    RequireOption(options, "opening-proof-timeout-seconds"),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var timeoutSeconds) || timeoutSeconds <= 0.0)
                throw new ArgumentException("Opening acceptance timeout is invalid.");
            var initialState = loaded.Session.OpeningState;
            var state = await opening.RunAcceptance(
                mode,
                RequireOption(options, "opening-proof-name"),
                timeoutSeconds,
                options.TryGetValue("capture-root", out var captureRoot)
                    ? captureRoot
                    : null);
            if (!File.Exists(loaded.Session.SavePath))
                throw new InvalidOperationException(
                    "Opening acceptance did not produce the canonical save.");
            var saveSha256 = Convert.ToHexString(
                    SHA256.HashData(File.ReadAllBytes(loaded.Session.SavePath)))
                .ToLowerInvariant();
            WriteReport(
                RequireOption(options, "report"),
                new
                {
                    schema = "opennv-opening-acceptance/v1",
                    status = "pass",
                    mode,
                    inputTransport = "godot-authored-ui-signals-plus-configured-input-map",
                    windowsAppControlUsed = false,
                    foregroundInputInjected = false,
                    configurationSchema = RuntimeConfiguration.ExpectedSchema,
                    configurationSha256 = _configuration.Sha256,
                    scene = Path.GetFullPath(scenePath),
                    openingMenuProof = _acceptedOpeningMenuAction is null
                        ? null
                        : new
                        {
                            action = _acceptedOpeningMenuAction,
                            inputTransport = "godot-owned-button-signal",
                            introSkipTransport = "godot-input-event",
                            windowsAppControlUsed = false,
                            foregroundInputInjected = false,
                        },
                    visualProof = opening.VisualProofReportPath is null
                        ? null
                        : new
                        {
                            report = opening.VisualProofReportPath,
                            sha256 = Convert.ToHexString(
                                    SHA256.HashData(File.ReadAllBytes(
                                        opening.VisualProofReportPath)))
                                .ToLowerInvariant(),
                        },
                    save = new
                    {
                        path = loaded.Session.SavePath,
                        sha256 = saveSha256,
                    },
                    initial = initialState is null
                        ? null
                        : new
                        {
                            stage = initialState.Stage,
                            completed = initialState.Completed,
                        },
                    final = new
                    {
                        schema = state.Schema,
                        questFormId = state.QuestFormId,
                        stage = state.Stage,
                        completed = state.Completed,
                        playerName = state.PlayerName,
                        specialTotal = state.SpecialValues.Values.Sum(),
                        tagSkills = state.TagSkillFormIds.Count,
                        traits = state.TraitFormIds.Count,
                        quests = state.Quests.Count,
                        globals = state.Globals.Count,
                        objectives = state.Objectives.Count,
                        inventory = state.Inventory.Count,
                        equippedItems = state.EquippedItemFormIds.Count,
                        achievements = state.Achievements.Count,
                    },
                    gameplay = loaded.Session.Report(),
                });
            GD.Print(
                $"OPENNV_OPENING_ACCEPTANCE_PASS mode={mode} stage={state.Stage} " +
                $"completed={state.Completed} save={saveSha256}");
            GetTree().Quit(0);
        }
        catch (Exception exception)
        {
            GD.PushError($"OPENNV_OPENING_ACCEPTANCE_FAIL {exception}");
            GetTree().Quit(1);
        }
    }

    private async Task RunOpeningCharacterVideo(
        OpeningQuestRuntime opening,
        CellSceneLoader.LoadedCell loaded)
    {
        try
        {
            var state = await opening.RunAcceptance(
                "creator",
                "COURIER",
                600.0,
                captureRoot: null,
                appearancePresentationHoldFrames: 90);
            if (state.Completed || !File.Exists(loaded.Session.SavePath))
                throw new InvalidOperationException(
                    "New Vegas character video did not return from its authored creator boundary.");
            for (var frame = 0; frame < 45; frame++)
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            GD.Print(
                $"OPENNV_FNV_CHARACTER_VIDEO_COMPLETE name={opening.PlayerName} stage={opening.Stage}");
            GetTree().Quit(0);
        }
        catch (Exception exception)
        {
            GD.PushError($"OPENNV_FNV_CHARACTER_VIDEO_FAIL {exception}");
            GetTree().Quit(1);
        }
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

            objectBall.Freeze = true;
            objectBall.GlobalPosition = new Vector3(
                objectBall.GlobalPosition.X,
                table.GlobalPosition.Y - objectBall.CollisionRadiusMeters,
                objectBall.GlobalPosition.Z);
            objectBall.Freeze = false;
            objectBall.Sleeping = false;
            for (var frame = 0;
                 frame < _configuration.Pool.ProofMaximumPhysicsFrames &&
                    !objectBall.IsPocketed;
                 frame++)
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            if (!objectBall.IsPocketed || objectBall.Visible || !objectBall.Freeze)
                throw new InvalidOperationException(
                    "Pool pocket detection did not retire the authored object ball.");
            loaded.Session.Save();
            GameplaySession? coldSession = null;
            PoolTableInstance.PoolState restoredPoolState;
            try
            {
                coldSession = new GameplaySession();
                coldSession.Configure(
                    loaded.FormId,
                    loaded.EditorId,
                    loaded.ProofDoor.ReferenceFormId,
                    _configuration,
                    loaded.Session.SavePath,
                    loadExistingSave: true,
                    showHud: false);
                if (!coldSession.TryGetLoadedPoolStateForProof(
                        table.ReferenceFormId,
                        out restoredPoolState))
                    throw new InvalidOperationException(
                        "Cold session did not restore the persisted pool table state.");
            }
            finally
            {
                coldSession?.Free();
            }
            var restoredPocketedBall = restoredPoolState.Balls.Single(ball =>
                ball.ReferenceFormId.Equals(
                    objectBall.ReferenceFormId,
                    StringComparison.OrdinalIgnoreCase));
            if (!restoredPocketedBall.Pocketed)
                throw new InvalidOperationException(
                    "Cold-restored pool state lost the pocketed object ball.");

            table.ResetAuthored();
            if (table.Balls.Any(ball => !ball.GlobalPosition.IsEqualApprox(
                    authoredPositions[ball.ReferenceFormId]) ||
                    ball.IsPocketed || !ball.Visible || ball.Freeze))
                throw new InvalidOperationException("Pool reset did not restore authored reference transforms.");
            table.RestoreState(restoredPoolState);
            if (!objectBall.IsPocketed || objectBall.Visible || !objectBall.Freeze)
                throw new InvalidOperationException(
                    "Live pool table did not restore the cold-loaded pocket state.");
            table.ResetAuthored();
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
                pocketDetected = true,
                pocketedBallReferenceFormId = objectBall.ReferenceFormId,
                pocketSaveRestored = true,
                liveStateRestoredFromColdSave = true,
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
        _openXr = XRServer.FindInterface("OpenXR");
        if (_openXr is null || !_openXr.IsInitialized())
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

    private async Task RunXrSimulatorAcceptance(
        CellSceneLoader.LoadedCell loaded,
        string scenePath,
        IReadOnlyDictionary<string, string> options)
    {
        try
        {
            await XrSimulatorAcceptance.Run(this, loaded, scenePath, options, _configuration);
            QuitOpenXr(0);
        }
        catch (Exception exception)
        {
            GD.PushError($"OPENNV_XR_SIMULATOR_FAIL {exception.Message}");
            QuitOpenXr(1);
        }
    }

    private void QuitOpenXr(int exitCode)
    {
        GetViewport().UseXR = false;
        _openXr?.Uninitialize();
        _openXr = null;
        GetTree().Quit(exitCode);
    }

    private async Task RunFlatControlsAcceptance(
        CellSceneLoader.LoadedCell loaded,
        string scenePath,
        IReadOnlyDictionary<string, string> options)
    {
        try
        {
            await FlatControlsAcceptance.Run(this, loaded, scenePath, options, _configuration);
            GetTree().Quit(0);
        }
        catch (Exception exception)
        {
            GD.PushError($"OPENNV_FLAT_CONTROLS_FAIL {exception.Message}");
            GetTree().Quit(1);
        }
    }

    private async Task RunPipBoyVisualAcceptance(
        CellSceneLoader.LoadedCell loaded,
        string scenePath,
        IReadOnlyDictionary<string, string> options)
    {
        try
        {
            await PipBoyVisualAcceptance.Run(this, loaded, scenePath, options, _configuration);
            GetTree().Quit(0);
        }
        catch (Exception exception)
        {
            GD.PushError($"OPENNV_PIPBOY_VISUAL_FAIL {exception.Message}");
            GetTree().Quit(1);
        }
    }

    private async Task RunCellRouteTravelAcceptance(
        CellSceneLoader.LoadedCell loaded,
        string scenePath,
        string mode,
        IReadOnlyDictionary<string, string> options)
    {
        try
        {
            await CellRouteTravelAcceptance.Run(
                this,
                loaded,
                scenePath,
                mode,
                options,
                _configuration);
            GetTree().Quit(0);
        }
        catch (Exception exception)
        {
            GD.PrintErr($"OPENNV_FLAT_ROUTE_TRAVEL_FAIL {exception}");
            GD.PushError($"OPENNV_FLAT_ROUTE_TRAVEL_FAIL {exception}");
            GetTree().Quit(1);
        }
    }

    private void CompleteXrRigProof(IReadOnlyDictionary<string, string> options)
    {
        XrRigLayoutAcceptance.Run(this, options, _configuration);
        GetTree().Quit(0);
    }

    private void CompleteClassicDioramaRigProof(IReadOnlyDictionary<string, string> options)
    {
        var session = new GameplaySession();
        session.Configure(
            "classic-diorama-rig-proof",
            "ClassicDioramaRigProof",
            "classic-diorama-proof-door",
            _configuration,
            options.TryGetValue("save-path", out var savePath) ? savePath : null,
            false,
            false,
            true,
            true,
            "CLASSIC DIORAMA  •  PRESENTATION PROOF");
        AddChild(session);
        var player = new CellPlayer();
        player.Configure(0.0f, session, _configuration, false, true);
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
            var route = _configuration.Proof.GameplayRoute;
            var revolver = loaded.Pickups.Values.Single(
                pickup => pickup.ItemFormId == route.WeaponPickupFormId);
            loaded.Session.Collect(revolver);
            loaded.Session.Fire(loaded.Player.Camera, loaded.Player.CollisionMask);
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
        if (options.TryGetValue("report", out var reportPath) &&
            !options.ContainsKey("fo1-continue-menu-proof") &&
            !options.ContainsKey("fo1-destination-inventory-interaction-proof") &&
            !options.ContainsKey("fo1-destination-inventory-interaction-cold-restore-proof"))
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
            var floorOwnedByLoadedCell = floor.Collider is not null &&
                (loaded.MainContent.Root.IsAncestorOf(floor.Collider) ||
                    loaded.LinkedCells.Any(linked => linked.Content.Root.IsAncestorOf(floor.Collider)));
            var floorWithinProbe = floor.Hit &&
                floor.Y <= _configuration.Proof.SpawnFloorRayStartMeters +
                    _configuration.Proof.SpawnFloorToleranceMeters &&
                floor.Y >= _configuration.Proof.SpawnFloorRayEndMeters -
                    _configuration.Proof.SpawnFloorToleranceMeters;
            var floorWalkable = floor.Hit &&
                floor.Normal.Y >= _configuration.Proof.WalkableSurfaceNormalYMinimum;
            if (!floor.Hit || !floorOwnedByLoadedCell || !floorWithinProbe || !floorWalkable)
                throw new InvalidOperationException(
                    $"XTEL floor contract failed: hit={floor.Hit} y={floor.Y} " +
                    $"normal={floor.Normal} owned={floorOwnedByLoadedCell} " +
                    $"withinProbe={floorWithinProbe} collider={floor.ColliderPath}");
            var portalTraversals = new List<PortalTraversalProof>();
            if (loaded.PortalLinks.Count == 0)
                portalTraversals.Add(await ProvePortalPassage(loaded, loaded.ProofDoor, null));
            else
                foreach (var portal in loaded.PortalLinks)
                    portalTraversals.Add(await ProvePortalPassage(
                        loaded,
                        portal.FromDoor,
                        portal.ToDoor));
            var failedPortal = portalTraversals.FirstOrDefault(value => !value.Passed);
            if (failedPortal != default)
                throw new InvalidOperationException(
                    $"Door traversal contract failed: {failedPortal.FromDoorReferenceFormId} -> " +
                    $"{failedPortal.ToDoorReferenceFormId ?? "standalone"} " +
                    $"closedHit={failedPortal.ClosedHit} " +
                    $"closedHitDoor={failedPortal.ClosedHitDoor} " +
                    $"openBlocked={failedPortal.OpenBlockedByPortalDoor} " +
                    $"openClear={failedPortal.OpenRayPortalClear} " +
                    $"projectileClear={failedPortal.ProjectilePortalClear} " +
                    $"floorHit={failedPortal.FloorHit} floorY={failedPortal.FloorY} " +
                    $"walkForward={failedPortal.CapsuleWalkForward} " +
                    $"walkBackward={failedPortal.CapsuleWalkBackward}");
            CompleteCellLoad(
                loaded with { ProofDoorOpen = true },
                scenePath,
                options,
                new DoorTraversalProof(
                    floor.Hit,
                    floor.Y,
                    floor.Normal,
                    floor.ColliderPath,
                    floorOwnedByLoadedCell,
                    floorWithinProbe,
                    floorWalkable,
                    portalTraversals.All(value => value.ClosedHit),
                    portalTraversals.All(value => value.ClosedHitDoor),
                    portalTraversals.Any(value => !value.OpenRayPortalClear),
                    portalTraversals.Any(value => value.OpenBlockedByPortalDoor),
                    portalTraversals.All(value => value.ProjectilePortalClear),
                    portalTraversals.All(value => value.CapsuleWalkForward),
                    portalTraversals.All(value => value.CapsuleWalkBackward),
                    portalTraversals.All(value => value.CapsuleWalkThrough),
                    loaded.LinkedCells.Count,
                    loaded.PortalLinks.Count == 0
                        ? null
                        : loaded.PortalLinks.Max(portal => portal.AlignmentErrorMeters),
                    portalTraversals));
            GetTree().Quit(0);
        }
        catch (Exception exception)
        {
            GD.PushError($"OPENNV_GODOT_DOOR_TRAVERSAL_FAIL {exception.Message}");
            GetTree().Quit(1);
        }
    }

    private async Task<PortalTraversalProof> ProvePortalPassage(
        CellSceneLoader.LoadedCell loaded,
        DoorInstance fromDoor,
        DoorInstance? toDoor)
    {
        fromDoor.SetOpen(false);
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        var ray = CellSceneLoader.BuildProofRay(fromDoor, _configuration.Proof);
        var closed = CellSceneLoader.CastProofRay(
            GetWorld3D().DirectSpaceState,
            fromDoor,
            ray,
            loaded.Player.CollisionMask);
        var closedHitDoor = closed.HitProofDoor ||
            toDoor is not null && IsColliderUnder(closed.ColliderPath, toDoor);
        fromDoor.SetOpen(true);
        loaded.Session.DoorChanged(fromDoor);
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        var opened = CellSceneLoader.CastProofRay(
            GetWorld3D().DirectSpaceState,
            fromDoor,
            ray,
            loaded.Player.CollisionMask);
        var openBlockedByDoor = opened.HitProofDoor ||
            toDoor is not null && IsColliderUnder(opened.ColliderPath, toDoor);
        var openRayPortalClear = !openBlockedByDoor && RayReachedPortalPlane(opened, ray);
        var portalDirection = (ray.To - ray.From).Normalized();
        var portalCenter = (ray.From + ray.To) / 2.0f;
        var projectileRay = new CellSceneLoader.DoorRay(
            portalCenter - portalDirection * _configuration.Proof.ProjectileRayStartMeters,
            portalCenter + portalDirection * _configuration.Proof.ProjectileRayEndMeters,
            ray.LocalSize,
            ray.LocalNormal);
        var projectile = CellSceneLoader.CastProofRay(
            GetWorld3D().DirectSpaceState,
            fromDoor,
            projectileRay,
            loaded.Player.CollisionMask);
        var projectileBlockedByDoor = projectile.HitProofDoor ||
            toDoor is not null && IsColliderUnder(projectile.ColliderPath, toDoor);
        var projectilePortalClear = !projectileBlockedByDoor &&
            RayReachedPortalPlane(projectile, projectileRay);
        var portalFloor = CellSceneLoader.CastFloorAt(
            GetWorld3D().DirectSpaceState,
            _configuration.Proof,
            loaded.Player.CollisionMask,
            loaded.Player.GetRid(),
            portalCenter);
        var portalFloorWalkable = portalFloor.Hit &&
            portalFloor.Normal.Y >= _configuration.Proof.WalkableSurfaceNormalYMinimum;
        if (portalFloor.Hit)
            portalCenter.Y = portalFloor.Y + _configuration.Proof.PortalCapsuleCenterHeightMeters;
        var portalMotion = portalDirection * _configuration.Proof.PortalCapsuleMotionMeters;
        var forwardCollision = new KinematicCollision3D();
        var walkForwardBlocked = toDoor is not null && loaded.Player.TestMove(
            new Transform3D(Basis.Identity, portalCenter - portalMotion / 2.0f),
            portalMotion,
            forwardCollision);
        var backwardCollision = new KinematicCollision3D();
        var walkBackwardBlocked = toDoor is not null && loaded.Player.TestMove(
            new Transform3D(Basis.Identity, portalCenter + portalMotion / 2.0f),
            -portalMotion,
            backwardCollision);
        return new PortalTraversalProof(
            fromDoor.ReferenceFormId,
            toDoor?.ReferenceFormId,
            closed.Hit,
            closedHitDoor,
            openBlockedByDoor,
            openRayPortalClear,
            projectilePortalClear,
            portalFloor.Hit,
            portalFloorWalkable,
            portalFloor.Y,
            !walkForwardBlocked,
            !walkBackwardBlocked,
            !walkForwardBlocked && !walkBackwardBlocked);
    }

    private static bool IsColliderUnder(string colliderPath, Node node) =>
        colliderPath.StartsWith(node.GetPath().ToString(), StringComparison.Ordinal);

    private static bool RayReachedPortalPlane(
        CellSceneLoader.RayHit hit,
        CellSceneLoader.DoorRay ray)
    {
        if (!hit.Hit)
            return true;
        var direction = (ray.To - ray.From).Normalized();
        var center = (ray.From + ray.To) / 2.0f;
        return (hit.Position - ray.From).Dot(direction) >
            (center - ray.From).Dot(direction);
    }
}
