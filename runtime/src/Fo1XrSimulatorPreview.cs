using System.Text.Json;
using Godot;

namespace OpenNV.Runtime;

/// <summary>
/// Bounded, simulator-only first-person presentation of the Fallout 1 V13ENT
/// entry.  This adapter deliberately does not claim controller interaction or
/// physical-headset acceptance; it exists to capture native OpenXR projection
/// frames from the exact owned-data scene.
/// </summary>
internal static class Fo1XrSimulatorPreview
{
    private const int ReadyFrames = 12;
    private const int TimeoutFrames = 60 * 60 * 4;

    internal static async Task Run(
        RuntimeCoordinator host,
        Fo1HexSceneLoader.LoadedFo1HexScene loaded,
        IReadOnlyDictionary<string, string> options)
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
            WorldScale = 1.0f,
            Current = true,
        };
        loaded.Root.AddChild(origin);
        var camera = new XRCamera3D
        {
            Name = "Fo1V13TrackedHead",
            Near = 0.035f,
            Far = 180.0f,
            Current = true,
        };
        origin.AddChild(camera);
        camera.AddChild(new SpotLight3D
        {
            Name = "Fo1V13TrackedHeadFill",
            LightColor = new Color(0.92f, 0.84f, 0.70f),
            LightEnergy = 1.18f,
            SpotRange = 11.0f,
            SpotAngle = 48.0f,
            ShadowEnabled = false,
        });
        loaded.Root.AddChild(new OmniLight3D
        {
            Name = "Fo1V13EntryVrFill",
            Position = entry + caveForward * 3.2f + Vector3.Up * 2.35f,
            LightColor = new Color(0.72f, 0.68f, 0.58f),
            LightEnergy = 1.15f,
            OmniRange = 9.0f,
            ShadowEnabled = false,
        });

        for (var frame = 0; frame < ReadyFrames; frame++)
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);

        GD.Print(
            $"OPENNV_FO1_XR_SIMULATOR_PREVIEW_READY entry={loaded.EntryTile} " +
            $"door={loaded.DoorTile} yaw={yaw:F6} worldScale={origin.WorldScale:F2}");

        var observedFrames = 0;
        var stopObserved = false;
        for (; observedFrames < TimeoutFrames; observedFrames++)
        {
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            if (!File.Exists(stopPath))
                continue;
            stopObserved = true;
            break;
        }

        var report = new
        {
            schema = "opennv-fo1-xr-simulator-preview/v1",
            status = stopObserved ? "pass" : "timeout",
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
            interactionValidated = false,
            handsValidated = false,
            physicalHeadsetValidated = false,
            observedFrames,
        };
        if (options.TryGetValue("report", out var reportPath))
            WriteReport(reportPath, report);
        GD.Print(
            $"OPENNV_FO1_XR_SIMULATOR_PREVIEW_{(stopObserved ? "PASS" : "TIMEOUT")} " +
            $"frames={observedFrames}");
        host.GetTree().Quit(stopObserved ? 0 : 1);
    }

    private static void PrepareWorld(Fo1HexSceneLoader.LoadedFo1HexScene loaded)
    {
        loaded.Camera.ProcessMode = Node.ProcessModeEnum.Disabled;
        loaded.Camera.Camera.Current = false;
        loaded.Session.Hud.Visible = false;
        loaded.Session.SetWorldGuidesVisible(false);
        loaded.Session.PlayerToken.Visible = false;
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
            environment.AmbientLightEnergy = 1.05f;
            environment.TonemapExposure = 1.24f;
            environment.FogDensity = 0.020f;
        }
    }

    private static float[] Vector(Vector3 value) => [value.X, value.Y, value.Z];

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
