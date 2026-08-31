using System.Text.Json;
using Godot;


namespace OpenNV.Runtime.Campaigns.Fallout1;

internal static class Fo1XrSimulatorPreviewNumericContracts
{
    // Immutable format, source-art, geometry, and acceptance contracts.
    // Runtime-tunable Fallout 1 behavior remains in the versioned runtime recipe.
    internal const float AcceptanceFloat0Point020f = 0.020f;
    internal const float AcceptanceFloat0Point035f = 0.035f;
    internal const float AcceptanceFloat0Point58f = 0.58f;
    internal const float AcceptanceFloat0Point68f = 0.68f;
    internal const float AcceptanceFloat0Point70f = 0.70f;
    internal const float AcceptanceFloat0Point72f = 0.72f;
    internal const float AcceptanceFloat0Point84f = 0.84f;
    internal const float AcceptanceFloat0Point92f = 0.92f;
    internal const float AcceptanceFloat1Point05f = 1.05f;
    internal const float AcceptanceFloat1Point15f = 1.15f;
    internal const float AcceptanceFloat1Point18f = 1.18f;
    internal const float AcceptanceFloat1Point24f = 1.24f;
    internal const float AcceptanceFloat11Point0f = 11.0f;
    internal const float AcceptanceFloat180Point0f = 180.0f;
    internal const float AcceptanceFloat2Point35f = 2.35f;
    internal const float AcceptanceFloat3Point2f = 3.2f;
    internal const float AcceptanceFloat48Point0f = 48.0f;
    internal const float AcceptanceFloat9Point0f = 9.0f;
}

/// <summary>
/// Bounded simulator-only OpenXR adapter for the authoritative Fallout 1
/// V13ENT session. It publishes tracked input into the same movement, combat,
/// reload, and save owners used by flat first person. The current Fallout 1
/// cache has no supported first-person hand, weapon-mount, or wrist-UI contract,
/// so those presentation claims remain explicitly false.
/// </summary>
internal static class Fo1XrSimulatorPreview
{
    private const int ReadyFrames = 12;
    private const int TimeoutFrames = 60 * 60 * 4;

    internal static async Task Run(
        RuntimeCoordinator host,
        Fo1HexSceneLoader.LoadedFo1HexScene loaded,
        IReadOnlyDictionary<string, string> options,
        RuntimeConfiguration configuration)
    {
        var simulatorDataRoot = System.Environment.GetEnvironmentVariable(
            "OPENXR_SIMULATOR_DATA_DIR");
        if (string.IsNullOrWhiteSpace(simulatorDataRoot))
            throw new InvalidOperationException(
                "Fallout 1 XR preview requires an isolated OPENXR_SIMULATOR_DATA_DIR.");
        if (!string.Equals(
                System.Environment.GetEnvironmentVariable("OPENXR_SIMULATOR_HEADLESS"),
                "1",
                StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Fallout 1 XR preview is restricted to the headless OpenXR simulator.");

        simulatorDataRoot = Path.GetFullPath(simulatorDataRoot);
        Directory.CreateDirectory(simulatorDataRoot);
        var stopPath = Path.Combine(simulatorDataRoot, "preview_stop.json");
        if (File.Exists(stopPath))
            File.Delete(stopPath);

        PrepareWorld(loaded);
        var entry = Fo1HexMath.Center(loaded.EntryTile);
        var door = Fo1HexMath.Center(loaded.DoorTile);
        var caveForward = entry - door;
        caveForward.Y = 0.0f;
        caveForward = caveForward.Normalized();
        var yaw = MathF.Atan2(-caveForward.X, -caveForward.Z);

        var origin = new XROrigin3D
        {
            Name = "Fo1V13EntryXrOrigin",
            Position = entry,
            Rotation = new Vector3(0.0f, yaw, 0.0f),
            WorldScale = configuration.Xr.WorldScale,
            Current = true,
        };
        var camera = new XRCamera3D
        {
            Name = "Fo1V13TrackedHead",
            Near = configuration.Player.CameraNearMeters,
            Far = configuration.Player.CameraFarMeters,
            Current = true,
        };
        origin.AddChild(camera);
        var leftGrip = Controller("Fo1LeftGrip", "left_hand", "grip");
        var rightGrip = Controller("Fo1RightGrip", "right_hand", "grip");
        var leftAim = Controller("Fo1LeftAim", "left_hand", "aim");
        var rightAim = Controller("Fo1RightAim", "right_hand", "aim");
        origin.AddChild(leftGrip);
        origin.AddChild(rightGrip);
        origin.AddChild(leftAim);
        origin.AddChild(rightAim);
        camera.AddChild(new SpotLight3D
        {
            Name = "Fo1V13TrackedHeadFill",
            LightColor = new Color(Fo1XrSimulatorPreviewNumericContracts.AcceptanceFloat0Point92f, Fo1XrSimulatorPreviewNumericContracts.AcceptanceFloat0Point84f, Fo1XrSimulatorPreviewNumericContracts.AcceptanceFloat0Point70f),
            LightEnergy = Fo1XrSimulatorPreviewNumericContracts.AcceptanceFloat1Point18f,
            SpotRange = Fo1XrSimulatorPreviewNumericContracts.AcceptanceFloat11Point0f,
            SpotAngle = Fo1XrSimulatorPreviewNumericContracts.AcceptanceFloat48Point0f,
            ShadowEnabled = false,
        });
        loaded.Root.AddChild(origin);
        loaded.Root.AddChild(new OmniLight3D
        {
            Name = "Fo1V13EntryVrFill",
            Position = entry + caveForward * Fo1XrSimulatorPreviewNumericContracts.AcceptanceFloat3Point2f + Vector3.Up * Fo1XrSimulatorPreviewNumericContracts.AcceptanceFloat2Point35f,
            LightColor = new Color(Fo1XrSimulatorPreviewNumericContracts.AcceptanceFloat0Point72f, Fo1XrSimulatorPreviewNumericContracts.AcceptanceFloat0Point68f, Fo1XrSimulatorPreviewNumericContracts.AcceptanceFloat0Point58f),
            LightEnergy = Fo1XrSimulatorPreviewNumericContracts.AcceptanceFloat1Point15f,
            OmniRange = Fo1XrSimulatorPreviewNumericContracts.AcceptanceFloat9Point0f,
            ShadowEnabled = false,
        });

        for (var frame = 0; frame < ReadyFrames; frame++)
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);

        GD.Print(
            $"OPENNV_FO1_XR_SIMULATOR_PREVIEW_READY entry={loaded.EntryTile} " +
            $"door={loaded.DoorTile} yaw={yaw:F6} worldScale={origin.WorldScale:F2}");

        var observedFrames = 0;
        var stopObserved = false;
        var initialPlayerPosition = loaded.Session.PlayerToken.Position;
        Vector3? leftHome = null;
        Vector3? rightHome = null;
        var trackedFrames = 0;
        var consecutiveTrackedFrames = 0;
        var maximumLeftHandTravelMeters = 0.0f;
        var maximumRightHandTravelMeters = 0.0f;
        var maximumLocomotionMeters = 0.0f;
        var maximumMoveStickMagnitude = 0.0f;
        var maximumTurnStickMagnitude = 0.0f;
        var maximumSnapPivotErrorMeters = 0.0f;
        var maximumEyeHeightErrorMeters = 0.0f;
        var eyeHeightCalibrated = false;
        var movementAcceptedFrames = 0;
        var snapTurns = 0;
        var fireEdges = 0;
        var reloadEdges = 0;
        var saveEdges = 0;
        var activateEdgesObserved = 0;
        var snapReady = true;
        var activatePressed = false;
        var firePressed = false;
        var reloadPressed = false;
        var savePressed = false;
        for (; observedFrames < TimeoutFrames; observedFrames++)
        {
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.PhysicsFrame);
            var tracked = leftGrip.GetHasTrackingData() && rightGrip.GetHasTrackingData();
            if (tracked)
            {
                trackedFrames++;
                consecutiveTrackedFrames++;
                leftHome ??= leftGrip.GlobalPosition;
                rightHome ??= rightGrip.GlobalPosition;
                maximumLeftHandTravelMeters = MathF.Max(
                    maximumLeftHandTravelMeters,
                    leftGrip.GlobalPosition.DistanceTo(leftHome.Value));
                maximumRightHandTravelMeters = MathF.Max(
                    maximumRightHandTravelMeters,
                    rightGrip.GlobalPosition.DistanceTo(rightHome.Value));
            }
            else
                consecutiveTrackedFrames = 0;

            if (!eyeHeightCalibrated &&
                consecutiveTrackedFrames >= configuration.Xr.EyeHeightCalibrationTrackedFrames)
            {
                origin.Position += Vector3.Up *
                    (configuration.Xr.DesiredEyeHeightMeters - camera.GlobalPosition.Y);
                eyeHeightCalibrated = true;
            }
            if (eyeHeightCalibrated)
                maximumEyeHeightErrorMeters = MathF.Max(
                    maximumEyeHeightErrorMeters,
                    MathF.Abs(camera.GlobalPosition.Y -
                        Fo1HexMath.Center(loaded.Session.PlayerTile).Y -
                        configuration.Xr.DesiredEyeHeightMeters));

            var move = leftGrip.GetVector2("move");
            var turn = rightGrip.GetVector2("turn");
            maximumMoveStickMagnitude = MathF.Max(maximumMoveStickMagnitude, move.Length());
            maximumTurnStickMagnitude = MathF.Max(maximumTurnStickMagnitude, turn.Length());
            if (move.Length() >= configuration.Xr.MovementDeadzone)
            {
                var forward = -camera.GlobalBasis.Z;
                var right = camera.GlobalBasis.X;
                forward.Y = 0.0f;
                right.Y = 0.0f;
                var direction = right.Normalized() * move.X + forward.Normalized() * move.Y;
                var before = loaded.Session.PlayerToken.Position;
                var accepted = loaded.Session.TryMoveFirstPerson(
                    direction,
                    loaded.RuntimeProfile.Camera.FirstPerson.MoveSpeedMetersPerSecond *
                    (float)host.GetPhysicsProcessDeltaTime());
                var playerDelta = loaded.Session.PlayerToken.Position - before;
                origin.GlobalPosition += new Vector3(playerDelta.X, 0.0f, playerDelta.Z);
                if (accepted)
                    movementAcceptedFrames++;
            }
            else
                loaded.Session.SetFirstPersonMoving(false);
            var playerOffset = loaded.Session.PlayerToken.Position - initialPlayerPosition;
            playerOffset.Y = 0.0f;
            maximumLocomotionMeters = MathF.Max(
                maximumLocomotionMeters,
                playerOffset.Length());

            if (MathF.Abs(turn.X) >= configuration.Xr.SnapTurnActivationThreshold && snapReady)
            {
                maximumSnapPivotErrorMeters = MathF.Max(
                    maximumSnapPivotErrorMeters,
                    SnapTurn(
                        origin,
                        camera,
                        -MathF.Sign(turn.X) * Mathf.DegToRad(configuration.Xr.SnapTurnDegrees)));
                snapTurns++;
                snapReady = false;
            }
            else if (MathF.Abs(turn.X) < configuration.Xr.SnapTurnResetThreshold)
                snapReady = true;

            var activate = rightGrip.GetFloat("activate") >= configuration.Xr.ActionThreshold;
            if (activate && !activatePressed)
            {
                activateEdgesObserved++;
                GD.Print("OPENNV_FO1_XR_ACTION action=activate accepted=False reason=no-v13ent-xr-activation-contract");
            }
            activatePressed = activate;

            var fire = rightGrip.GetFloat("fire") >= configuration.Xr.ActionThreshold;
            if (fire && !firePressed)
            {
                var shotsBefore = loaded.Session.FpsShots;
                loaded.Session.FireFirstPerson(
                    rightAim.GlobalPosition,
                    -rightAim.GlobalBasis.Z);
                if (loaded.Session.FpsShots > shotsBefore)
                {
                    fireEdges++;
                    GD.Print("OPENNV_FO1_XR_ACTION action=fire accepted=True");
                }
            }
            firePressed = fire;

            var reload = rightGrip.IsButtonPressed("reload");
            if (reload && !reloadPressed && loaded.Session.Reload())
            {
                reloadEdges++;
                GD.Print("OPENNV_FO1_XR_ACTION action=reload accepted=True");
            }
            reloadPressed = reload;

            var save = leftGrip.IsButtonPressed("save");
            if (save && !savePressed)
            {
                loaded.Session.SaveAndNotify();
                saveEdges++;
                GD.Print("OPENNV_FO1_XR_ACTION action=save accepted=True");
            }
            savePressed = save;

            if (!File.Exists(stopPath))
                continue;
            stopObserved = true;
            break;
        }

        var acceptance = configuration.Xr.SimulatorAcceptance;
        var controlsRequested = options.ContainsKey("fo1-xr-controls-proof");
        var trackingPassed =
            trackedFrames >= acceptance.MinimumTrackedFrames &&
            maximumLeftHandTravelMeters >= acceptance.MinimumHandTravelMeters &&
            maximumRightHandTravelMeters >= acceptance.MinimumHandTravelMeters;
        var locomotionPassed =
            trackingPassed &&
            maximumLocomotionMeters >= acceptance.MinimumLocomotionMeters;
        var snapTurnPassed =
            snapTurns >= acceptance.MinimumSnapTurns &&
            maximumSnapPivotErrorMeters <= acceptance.MaximumSnapPivotErrorMeters;
        var firePassed = fireEdges >= acceptance.MinimumAcceptedFireActions;
        var reloadPassed = reloadEdges >= acceptance.MinimumAcceptedReloadActions;
        var savePassed =
            saveEdges >= acceptance.MinimumSaveActions &&
            File.Exists(loaded.Session.SavePath);
        var controlsPassed =
            locomotionPassed && snapTurnPassed && firePassed && reloadPassed && savePassed;
        var passed = stopObserved && (!controlsRequested || controlsPassed);
        var leftTracker = XRServer.GetTracker("left_hand") as XRPositionalTracker;
        var rightTracker = XRServer.GetTracker("right_hand") as XRPositionalTracker;

        var report = new
        {
            schema = "opennv-fo1-xr-simulator-preview/v2",
            status = passed ? "pass" : stopObserved ? "fail" : "timeout",
            evidenceLevel = "simulator",
            hardwareHeadsetValidated = false,
            windowsAppControlUsed = false,
            foregroundInputInjected = false,
            inputTransport = "repo-local-openxr-runtime-file-ipc",
            simulatorDataRoot,
            scene = loaded.ScenePath,
            sceneSha256 = loaded.SceneSha256,
            entryTile = loaded.EntryTile,
            doorTile = loaded.DoorTile,
            doorOpen = loaded.Door.Controller.IsOpen,
            originMeters = Vector(origin.Position),
            originYawRadians = yaw,
            worldScale = origin.WorldScale,
            cameraType = camera.GetType().Name,
            nearMeters = camera.Near,
            farMeters = camera.Far,
            playerBodySuppressedForFirstPerson = !loaded.Session.PlayerToken.Visible,
            desktopHudSuppressed = !loaded.Session.Hud.Visible,
            tacticalCameraSuppressed = !loaded.Camera.Camera.Current,
            cutawayDisabled = true,
            controllerInputValidated = controlsPassed,
            controlsProofRequested = controlsRequested,
            controllerTrackingValidated = trackingPassed,
            locomotionValidated = locomotionPassed,
            snapTurnValidated = snapTurnPassed,
            combatFireValidated = firePassed,
            reloadValidated = reloadPassed,
            saveValidated = savePassed,
            combatReloadSaveValidated = firePassed && reloadPassed && savePassed,
            doorActivationValidated = false,
            activateEdgesObserved,
            handsValidated = false,
            visibleHandProvider = "none-no-supported-fo1-first-person-hand-contract",
            heldWeaponValidated = false,
            wristHudValidated = false,
            physicalHeadsetValidated = false,
            observedFrames,
            control = new
            {
                leftProfile = leftTracker?.Profile.ToString(),
                rightProfile = rightTracker?.Profile.ToString(),
                leftActive = leftGrip.GetIsActive(),
                leftTracked = leftGrip.GetHasTrackingData(),
                rightActive = rightGrip.GetIsActive(),
                rightTracked = rightGrip.GetHasTrackingData(),
                leftGripPose = leftGrip.Pose.ToString(),
                rightGripPose = rightGrip.Pose.ToString(),
                leftAimPose = leftAim.Pose.ToString(),
                rightAimPose = rightAim.Pose.ToString(),
                trackedFrames,
                movementAcceptedFrames,
                maximumLocomotionMeters,
                maximumLeftHandTravelMeters,
                maximumRightHandTravelMeters,
                maximumMoveStickMagnitude,
                maximumTurnStickMagnitude,
                snapTurns,
                maximumSnapPivotErrorMeters,
                eyeHeightCalibrated,
                maximumEyeHeightErrorMeters,
                fireEdges,
                reloadEdges,
                saveEdges,
                saveExists = File.Exists(loaded.Session.SavePath),
            },
            gameplay = loaded.Session.Report(),
        };
        if (options.TryGetValue("report", out var reportPath))
            WriteReport(reportPath, report);
        GD.Print(
            $"OPENNV_FO1_XR_SIMULATOR_PREVIEW_{(passed ? "PASS" : stopObserved ? "FAIL" : "TIMEOUT")} " +
            $"frames={observedFrames} locomotion={maximumLocomotionMeters:F3} " +
            $"snaps={snapTurns} fire={fireEdges} reload={reloadEdges} save={saveEdges}");
        host.GetTree().Quit(passed ? 0 : 1);
    }

    private static void PrepareWorld(Fo1HexSceneLoader.LoadedFo1HexScene loaded)
    {
        loaded.Camera.ProcessMode = Node.ProcessModeEnum.Disabled;
        loaded.Camera.Camera.Current = false;
        loaded.Session.Hud.Visible = false;
        loaded.Session.SetWorldGuidesVisible(false);
        loaded.Session.PlayerToken.Visible = false;
        loaded.Session.SetFirstPersonModeActive(true);
        foreach (var mob in loaded.Session.Mobs)
            mob.SetReadabilityMarkersVisible(false);
        loaded.CaveCutaway.SetMeltEnabled(false);
        loaded.CaveCutaway.ProcessMode = Node.ProcessModeEnum.Disabled;
        loaded.Door.Controller.SetOpenAmount(1.0f);

        if (loaded.Root.FindChild(
                "V13ENT_200X200_HEX_GRID",
                recursive: true,
                owned: false) is GeometryInstance3D grid)
            grid.Visible = false;
        if (loaded.Root.FindChild(
                "FO1_SOURCE_STATIC_SPRITE_OVERLAY",
                recursive: true,
                owned: false) is Node3D sourceOverlay)
            sourceOverlay.Visible = false;
        foreach (var name in new[] { "ExactV13Secr3Frame", "VaultDoorHexIdentity" })
        {
            if (loaded.Root.FindChild(name, recursive: true, owned: false) is Node3D guide)
                guide.Visible = false;
        }

        if (loaded.Root.FindChild(
                "WorldEnvironment",
                recursive: true,
                owned: false) is WorldEnvironment world && world.Environment is { } environment)
        {
            environment.AmbientLightEnergy = Fo1XrSimulatorPreviewNumericContracts.AcceptanceFloat1Point05f;
            environment.TonemapExposure = Fo1XrSimulatorPreviewNumericContracts.AcceptanceFloat1Point24f;
            environment.FogDensity = Fo1XrSimulatorPreviewNumericContracts.AcceptanceFloat0Point020f;
        }
    }

    private static float[] Vector(Vector3 value) => [value.X, value.Y, value.Z];

    private static XRController3D Controller(string name, string tracker, string pose) => new()
    {
        Name = name,
        Tracker = tracker,
        Pose = pose,
    };

    private static float SnapTurn(XROrigin3D origin, XRCamera3D camera, float radians)
    {
        var headBefore = camera.GlobalPosition;
        var headOffset = camera.GlobalPosition - origin.GlobalPosition;
        headOffset.Y = 0.0f;
        origin.RotateY(radians);
        origin.GlobalPosition += headOffset - headOffset.Rotated(Vector3.Up, radians);
        var pivotError = camera.GlobalPosition - headBefore;
        pivotError.Y = 0.0f;
        GD.Print(
            $"OPENNV_FO1_XR_ACTION action=snap-turn accepted=True pivotError={pivotError.Length():F6}");
        return pivotError.Length();
    }

    private static void WriteReport(string path, object report)
    {
        var resolved = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(resolved)!);
        File.WriteAllText(
            resolved,
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) +
            System.Environment.NewLine);
    }
}
