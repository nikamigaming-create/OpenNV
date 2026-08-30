using Godot;

namespace OpenNV.Runtime.Campaigns.Fallout2.Temple;

public sealed partial class Fo2ArroyoCavesInteractiveHost : Node3D
{
    public override void _Ready()
    {
        try
        {
            var options = Fo2ArroyoCavesProofOptions.Parse(OS.GetCmdlineUserArgs());
            var temple = Fo2TemplePresentationCatalog.Load(
                Fo2ArroyoCavesProofOptions.Require(options, "fo2-temple-cache"));
            var transition = Fo2TempleTransitionCatalog.Load(
                Fo2ArroyoCavesProofOptions.Require(options, "fo2-temple-transitions"),
                temple);
            var catalog = Fo2ArroyoCavesPresentationCatalog.Load(
                Fo2ArroyoCavesProofOptions.Require(options, "fo2-arroyo-cache"),
                transition);
            var playerPresentation = Fo2ArroyoPlayerPresentationCatalog.Load(
                Fo2ArroyoCavesProofOptions.Require(options, "fo2-player-cache"),
                catalog.SourceProfileId);
            var scene = Fo2ArroyoCavesScene.Build(catalog, this);
            var runtime = Fo2ArroyoCavesPlayerRuntime.Build(
                catalog,
                scene,
                playerPresentation);
            GD.Print(
                $"OPENNV_FO2_ARROYO_INTERACTIVE_READY map={scene.MapIndex} " +
                $"elevation={scene.Elevation} tile={runtime.Player.ArrivalTile} " +
                $"fid={Fo2ArroyoPlayerPresentationCatalog.ExpectedFid} " +
                $"mapLights={scene.Molded3D.SourceMapLightRecords}/" +
                    $"{scene.Molded3D.SourceMapLights} " +
                $"torchMotivatedMapLights=" +
                    $"{scene.Molded3D.SourceTorchMotivatedMapLights} " +
                "controls=WASD exit=Escape");
            if (options.TryGetValue("fo2-arroyo-arrival-capture", out var capturePath))
                _ = CaptureArrival(capturePath, scene, runtime);
        }
        catch (Exception exception)
        {
            GD.PushError($"OPENNV_FO2_ARROYO_INTERACTIVE_FAIL {exception}");
            GetTree().Quit(1);
        }
    }

    public override void _UnhandledKeyInput(InputEvent inputEvent)
    {
        if (inputEvent is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
            GetTree().Quit();
    }

    private async Task CaptureArrival(
        string configuredPath,
        Fo2ArroyoCavesSceneCoverage scene,
        Fo2ArroyoCavesPlayerRuntimeCoverage runtime)
    {
        try
        {
            var path = Path.GetFullPath(configuredPath);
            if (File.Exists(path) || Directory.Exists(path))
                throw new InvalidOperationException(
                    $"Refusing to overwrite Fallout 2 r8 arrival evidence: {path}");
            var parent = Path.GetDirectoryName(path) ??
                throw new InvalidOperationException(
                    "Fallout 2 r8 arrival evidence has no parent directory.");
            Directory.CreateDirectory(parent);
            for (var frame = 0; frame < 4; frame++)
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            var image = GetViewport().GetTexture().GetImage();
            if (image.IsEmpty())
                throw new InvalidOperationException(
                    "Fallout 2 r8 arrival framebuffer is empty.");
            var error = image.SavePng(path);
            if (error != Error.Ok)
                throw new IOException(
                    $"Fallout 2 r8 arrival PNG write failed: {error}.");
            var sha256 = Fo2TemplePresentationCatalog.Sha256(File.ReadAllBytes(path));
            GD.Print(
                $"OPENNV_FO2_ARROYO_R8_ARRIVAL_CAPTURE path={path} " +
                $"sha256={sha256} size={image.GetWidth()}x{image.GetHeight()} " +
                $"cameraMode={runtime.Profile.CameraCompositionMode} " +
                $"cameraSizeMeters={runtime.Player.CameraSizeMeters} " +
                $"sourcePixelScale={runtime.Player.CameraSourcePixelScale} " +
                $"mapLights={scene.Molded3D.SourceMapLightRecords}/" +
                    $"{scene.Molded3D.SourceMapLights} " +
                $"torchMotivatedMapLights=" +
                    $"{scene.Molded3D.SourceTorchMotivatedMapLights}");
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            GD.PushError($"OPENNV_FO2_ARROYO_R8_ARRIVAL_CAPTURE_FAIL {exception}");
            GetTree().Quit(1);
        }
    }
}
