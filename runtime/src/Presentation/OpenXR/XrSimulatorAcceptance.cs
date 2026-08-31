using System.Text.Json;
using Godot;


using OpenNV.Runtime.InputSystem;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Presentation.OpenXR;

internal static class XrSimulatorAcceptance
{
    internal static async Task Run(
        RuntimeCoordinator host,
        CellSceneLoader.LoadedCell loaded,
        string scenePath,
        IReadOnlyDictionary<string, string> options,
        RuntimeConfiguration configuration)
    {
        var simulatorDataRoot = System.Environment.GetEnvironmentVariable(
            "OPENXR_SIMULATOR_DATA_DIR");
        if (string.IsNullOrWhiteSpace(simulatorDataRoot))
            throw new InvalidOperationException(
                "OpenXR simulator acceptance requires an isolated OPENXR_SIMULATOR_DATA_DIR.");
        var acceptance = configuration.Xr.SimulatorAcceptance;
        PlayerControlTelemetry.Snapshot control = default;
        var passed = false;
        for (var frame = 0; frame < acceptance.TimeoutFrames; frame++)
        {
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.PhysicsFrame);
            control = loaded.Player.ControlTelemetry;
            passed =
                control.TrackedFrames >= acceptance.MinimumTrackedFrames &&
                control.MaximumLocomotionMeters >= acceptance.MinimumLocomotionMeters &&
                control.MaximumLeftHandTravelMeters >= acceptance.MinimumHandTravelMeters &&
                control.MaximumRightHandTravelMeters >= acceptance.MinimumHandTravelMeters &&
                control.MaximumMoveStickMagnitude >= configuration.Xr.SnapTurnActivationThreshold &&
                control.MaximumTurnStickMagnitude >= configuration.Xr.SnapTurnActivationThreshold &&
                control.FloorObserved &&
                control.MaximumEyeHeightErrorMeters <= acceptance.EyeHeightToleranceMeters &&
                control.SnapTurns >= acceptance.MinimumSnapTurns &&
                control.MaximumSnapPivotErrorMeters <= acceptance.MaximumSnapPivotErrorMeters &&
                control.AcceptedActivations >= acceptance.MinimumAcceptedActivations &&
                control.AcceptedFireActions >= acceptance.MinimumAcceptedFireActions &&
                control.AcceptedReloadActions >= acceptance.MinimumAcceptedReloadActions &&
                control.SaveEdges >= acceptance.MinimumSaveActions &&
                loaded.Session.OpenDoorsCount >= acceptance.MinimumAcceptedActivations &&
                loaded.Player.HasLeftHand && loaded.Player.HasRightHand &&
                loaded.Player.HasHeldWeapon && loaded.Player.HasMuzzleFeedback &&
                loaded.Session.HasXrHud && File.Exists(loaded.Session.SavePath);
            if (passed)
                break;
        }
        if (!passed)
            throw new InvalidOperationException(
                "OpenXR simulator acceptance timed out: " + JsonSerializer.Serialize(control));

        var leftTracker = XRServer.GetTracker("left_hand") as XRPositionalTracker;
        var rightTracker = XRServer.GetTracker("right_hand") as XRPositionalTracker;
        var report = new
        {
            schema = "opennv-openxr-simulator-acceptance/v1",
            status = "pass",
            evidenceLevel = "simulator",
            hardwareHeadsetValidated = false,
            windowsAppControlUsed = false,
            foregroundInputInjected = false,
            simulatorDataRoot = Path.GetFullPath(simulatorDataRoot),
            configurationSchema = RuntimeConfiguration.ExpectedSchema,
            configurationSha256 = configuration.Sha256,
            scene = scenePath,
            leftProfile = leftTracker?.Profile.ToString(),
            rightProfile = rightTracker?.Profile.ToString(),
            leftActive = loaded.Player.LeftGrip!.GetIsActive(),
            leftTracked = loaded.Player.LeftGrip.GetHasTrackingData(),
            rightActive = loaded.Player.RightGrip!.GetIsActive(),
            rightTracked = loaded.Player.RightGrip.GetHasTrackingData(),
            leftGripPose = loaded.Player.LeftGrip.Pose.ToString(),
            rightGripPose = loaded.Player.RightGrip.Pose.ToString(),
            leftAimPose = loaded.Player.LeftAim!.Pose.ToString(),
            rightAimPose = loaded.Player.RightAim!.Pose.ToString(),
            visibleHandProvider = loaded.Player.HandProvider,
            leftHandVisible = loaded.Player.HasLeftHand,
            rightHandVisible = loaded.Player.HasRightHand,
            heldWeapon = loaded.Player.HasHeldWeapon,
            wristHud = loaded.Session.HasXrHud,
            openDoors = loaded.Session.OpenDoorsCount,
            control,
            gameplay = loaded.Session.Report(),
        };
        RuntimeCoordinator.WriteReport(
            RuntimeCoordinator.RequireOption(options, "report"),
            report);
        GD.Print(
            $"OPENNV_XR_SIMULATOR_PASS movement={control.MaximumLocomotionMeters:F3} " +
            $"leftTravel={control.MaximumLeftHandTravelMeters:F3} " +
            $"rightTravel={control.MaximumRightHandTravelMeters:F3} " +
            $"snapTurns={control.SnapTurns} fire={control.AcceptedFireActions} " +
            $"reload={control.AcceptedReloadActions} doors={loaded.Session.OpenDoorsCount}");
    }
}
