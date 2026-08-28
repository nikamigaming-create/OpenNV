using Godot;

namespace OpenNV.Runtime;

internal static class FlatControlsAcceptance
{
    internal static async Task Run(
        RuntimeCoordinator host,
        CellSceneLoader.LoadedCell loaded,
        string scenePath,
        IReadOnlyDictionary<string, string> options,
        RuntimeConfiguration configuration)
    {
        var input = configuration.Player.DesktopInput;
        try
        {
            if (loaded.Player.UsesXr)
                throw new InvalidOperationException("Flat control acceptance loaded an XR player.");
            await WaitPhysicsFrames(host, input.Acceptance.SettleFrames);
            var initialPosition = loaded.Player.GlobalPosition;
            var initialYaw = loaded.Player.Rotation.Y;
            var initialPitch = loaded.Player.Camera.Rotation.X;

            await PulseMouseBinding(host, input.CaptureMouse, input.Acceptance.SettleFrames);
            if (Input.MouseMode != Input.MouseModeEnum.Captured)
                throw new InvalidOperationException("Configured desktop mouse capture was not accepted.");

            var doorRay = CellSceneLoader.BuildProofRay(loaded.ProofDoor, configuration.Proof);
            var doorCenter = (doorRay.From + doorRay.To) / 2.0f;
            ApplyMouseLook(loaded.Player, doorCenter, configuration.Player);
            await WaitPhysicsFrames(host, input.Acceptance.SettleFrames);
            var lookRadians = MathF.Sqrt(
                MathF.Pow(Mathf.AngleDifference(initialYaw, loaded.Player.Rotation.Y), 2.0f) +
                MathF.Pow(loaded.Player.Camera.Rotation.X - initialPitch, 2.0f));
            if (lookRadians < input.Acceptance.MinimumLookRadians)
                throw new InvalidOperationException(
                    $"Configured desktop mouse look did not rotate far enough: {lookRadians:F4}");

            await PulseKeyBinding(host, input.Activate, input.Acceptance.SettleFrames);
            if (!loaded.ProofDoor.IsOpen || loaded.Session.OpenDoorsCount < 1)
                throw new InvalidOperationException("Configured desktop activate input did not open the proof door.");

            ApplyMouseRotation(
                Mathf.AngleDifference(loaded.Player.Rotation.Y, initialYaw),
                initialPitch - loaded.Player.Camera.Rotation.X,
                configuration.Player.MouseSensitivityRadiansPerPixel);
            await WaitPhysicsFrames(host, input.Acceptance.SettleFrames);

            Input.ParseInputEvent(DesktopInputMap.CreateEvent(input.MoveForward, true));
            await WaitPhysicsFrames(host, input.Acceptance.MovementFrames);
            Input.ParseInputEvent(DesktopInputMap.CreateEvent(input.MoveForward, false));
            await WaitPhysicsFrames(host, input.Acceptance.SettleFrames);
            var movement = loaded.Player.GlobalPosition - initialPosition;
            movement.Y = 0.0f;
            if (movement.Length() < input.Acceptance.MinimumLocomotionMeters)
                throw new InvalidOperationException(
                    $"Configured desktop movement did not clear acceptance: {movement.Length():F4}");

            var shotsBefore = loaded.Session.ShotsFired;
            var ammoBefore = loaded.Session.AmmoInMagazine;
            var reserveBefore = loaded.Session.ReserveAmmo;
            await PulseMouseBinding(host, input.Fire, input.Acceptance.SettleFrames);
            if (loaded.Session.ShotsFired != shotsBefore + 1 ||
                loaded.Session.AmmoInMagazine != ammoBefore - 1)
                throw new InvalidOperationException("Configured desktop fire input was not accepted.");

            var ammoAfterFire = loaded.Session.AmmoInMagazine;
            var expectedReloadedRounds = Math.Min(
                loaded.Session.WeaponClipSize - ammoAfterFire,
                reserveBefore);
            if (expectedReloadedRounds <= 0)
                throw new InvalidOperationException(
                    "Flat controls acceptance has no reloadable reserve rounds.");
            await PulseKeyBinding(host, input.Reload, input.Acceptance.SettleFrames);
            if (loaded.Session.AmmoInMagazine != ammoAfterFire + expectedReloadedRounds ||
                loaded.Session.ReserveAmmo != reserveBefore - expectedReloadedRounds)
                throw new InvalidOperationException("Configured desktop reload input was not accepted.");

            await PulseKeyBinding(host, input.Save, input.Acceptance.SettleFrames);
            if (!File.Exists(loaded.Session.SavePath))
                throw new InvalidOperationException("Configured desktop save input did not write the save file.");
            var pipBoyOpened = false;
            await PulseKeyBinding(host, input.PipBoy, input.Acceptance.SettleFrames);
            if (!loaded.Session.HasPipBoy || !loaded.Session.IsPipBoyOpen)
                throw new InvalidOperationException("Configured Pip-Boy input did not open the UI.");
            pipBoyOpened = true;
            await PulseKeyBinding(host, input.Cancel, input.Acceptance.SettleFrames);
            if (loaded.Session.IsPipBoyOpen)
                throw new InvalidOperationException("Configured cancel input did not close the Pip-Boy.");
            if (!loaded.Player.HasLeftHand || !loaded.Player.HasRightHand ||
                !loaded.Player.HasHeldWeapon || !loaded.Player.HasMuzzleFeedback ||
                !loaded.Session.HasDesktopHud)
                throw new InvalidOperationException(
                    "Flat first-person hands, weapon, feedback, or desktop HUD is missing.");

            string? screenshotPath = null;
            if (options.TryGetValue("screenshot", out var requestedScreenshot))
            {
                screenshotPath = Path.GetFullPath(requestedScreenshot);
                Directory.CreateDirectory(Path.GetDirectoryName(screenshotPath)!);
                for (var frame = 0;
                     frame < input.Acceptance.RenderedFramesBeforeScreenshot;
                     frame++)
                    await host.ToSignal(
                        RenderingServer.Singleton,
                        RenderingServer.SignalName.FramePostDraw);
                var image = host.GetViewport().GetTexture().GetImage();
                if (image.IsEmpty() || image.SavePng(screenshotPath) != Error.Ok)
                    throw new InvalidOperationException(
                        $"Could not save the flat acceptance screenshot: {screenshotPath}");
            }

            var report = new
            {
                schema = "opennv-flat-controls-acceptance/v1",
                status = "pass",
                inputTransport = "godot-input-map-plus-parse-input-event",
                windowsAppControlUsed = false,
                foregroundInputInjected = false,
                configurationSchema = RuntimeConfiguration.ExpectedSchema,
                configurationSha256 = configuration.Sha256,
                scene = scenePath,
                proofDoorReferenceFormId = loaded.ProofDoorFormId,
                mouseCaptured = Input.MouseMode == Input.MouseModeEnum.Captured,
                lookRadians,
                locomotionMeters = movement.Length(),
                leftHandVisible = loaded.Player.HasLeftHand,
                rightHandVisible = loaded.Player.HasRightHand,
                visibleHandProvider = loaded.Player.HandProvider,
                heldWeapon = loaded.Player.HasHeldWeapon,
                desktopHud = loaded.Session.HasDesktopHud,
                pipBoy = new
                {
                    available = loaded.Session.HasPipBoy,
                    opened = pipBoyOpened,
                    closed = !loaded.Session.IsPipBoyOpen,
                },
                openDoors = loaded.Session.OpenDoorsCount,
                screenshot = screenshotPath,
                keyBindings = input.KeyBindings.Select(binding => new
                {
                    binding.Action,
                    binding.PhysicalKey,
                }),
                mouseBindings = input.MouseBindings.Select(binding => new
                {
                    binding.Action,
                    binding.Button,
                }),
                gameplay = loaded.Session.Report(),
            };
            RuntimeCoordinator.WriteReport(
                RuntimeCoordinator.RequireOption(options, "report"),
                report);
            GD.Print(
                $"OPENNV_FLAT_CONTROLS_PASS movement={movement.Length():F3} " +
                $"look={lookRadians:F3} fire={loaded.Session.ShotsFired - shotsBefore} " +
                $"doors={loaded.Session.OpenDoorsCount}");
        }
        finally
        {
            foreach (var binding in input.KeyBindings)
                Input.ParseInputEvent(DesktopInputMap.CreateEvent(binding, false));
            foreach (var binding in input.MouseBindings)
                Input.ParseInputEvent(DesktopInputMap.CreateEvent(binding, false));
            Input.MouseMode = Input.MouseModeEnum.Visible;
        }
    }

    private static async Task PulseKeyBinding(
        RuntimeCoordinator host,
        DesktopKeyBindingConfiguration binding,
        int settleFrames)
    {
        Input.ParseInputEvent(DesktopInputMap.CreateEvent(binding, true));
        await WaitPhysicsFrames(host, settleFrames);
        Input.ParseInputEvent(DesktopInputMap.CreateEvent(binding, false));
        await WaitPhysicsFrames(host, settleFrames);
    }

    private static async Task PulseMouseBinding(
        RuntimeCoordinator host,
        DesktopMouseBindingConfiguration binding,
        int settleFrames)
    {
        Input.ParseInputEvent(DesktopInputMap.CreateEvent(binding, true));
        await WaitPhysicsFrames(host, settleFrames);
        Input.ParseInputEvent(DesktopInputMap.CreateEvent(binding, false));
        await WaitPhysicsFrames(host, settleFrames);
    }

    private static void ApplyMouseLook(
        CellPlayer player,
        Vector3 target,
        PlayerConfiguration configuration)
    {
        var direction = (target - player.Camera.GlobalPosition).Normalized();
        var desiredYaw = MathF.Atan2(-direction.X, -direction.Z);
        var desiredPitch = MathF.Asin(direction.Y);
        ApplyMouseRotation(
            Mathf.AngleDifference(player.Rotation.Y, desiredYaw),
            desiredPitch - player.Camera.Rotation.X,
            configuration.MouseSensitivityRadiansPerPixel);
    }

    private static void ApplyMouseRotation(
        float yawDelta,
        float pitchDelta,
        float sensitivity)
    {
        Input.ParseInputEvent(new InputEventMouseMotion
        {
            Relative = new Vector2(-yawDelta / sensitivity, -pitchDelta / sensitivity),
        });
    }

    private static async Task WaitPhysicsFrames(RuntimeCoordinator host, int frameCount)
    {
        for (var frame = 0; frame < frameCount; frame++)
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.PhysicsFrame);
    }
}
