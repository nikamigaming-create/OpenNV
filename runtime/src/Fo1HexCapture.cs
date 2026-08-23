using System.Security.Cryptography;
using System.Text.Json;
using Godot;

namespace OpenNV.Runtime;

internal static class Fo1HexCapture
{
    internal static async Task Run(
        Node host,
        Fo1HexSceneLoader.LoadedFo1HexScene loaded,
        string captureRoot,
        object runtimeReport)
    {
        try
        {
            var output = Path.GetFullPath(captureRoot);
            if (Directory.Exists(output) || File.Exists(output))
                throw new InvalidOperationException($"Refusing to overwrite Fallout hex capture: {output}");
            Directory.CreateDirectory(output);
            await WaitForFrames(host, 5);
            var ui = SaveViewport(host, output, "v13ent-hex-tactical-ui.png", 0.025, 0.035);
            loaded.Session.Hud.Visible = false;
            await WaitForFrames(host, 3);
            var environment = SaveViewport(host, output, "v13ent-hex-map.png", 0.025, 0.025);
            loaded.Session.Hud.Visible = true;
            var status = ui.Passed && environment.Passed ? "pass" : "fail";
            var report = new
            {
                schema = "opennv-fo1-hex-capture/v1",
                status,
                renderer = "forward_plus",
                scene = loaded.ScenePath,
                sceneSha256 = loaded.SceneSha256,
                entryTile = loaded.EntryTile,
                doorTile = loaded.DoorTile,
                floorEntries = loaded.FloorEntries,
                floorTextures = loaded.FloorTextures,
                renderedFloorTiles = loaded.RenderedFloorTiles,
                provisionalWalkableHexes = loaded.WalkableHexes,
                spriteArtifacts = loaded.SpriteArtifacts,
                spritePlacements = loaded.SpritePlacements,
                camera = new
                {
                    projection = "orthogonal",
                    targetSizeMeters = loaded.Camera.TargetSizeMeters,
                    targetYawDegrees = Mathf.RadToDeg(loaded.Camera.TargetYawRadians),
                    targetPitchDegrees = Mathf.RadToDeg(loaded.Camera.TargetPitchRadians),
                    middleMouseOrbit = true,
                    rightMousePan = true,
                    wheelZoomTowardCursor = true,
                    edgePan = true,
                },
                tactical = loaded.Session.Report(),
                runtime = runtimeReport,
                files = new[] { ui.Evidence, environment.Evidence },
                windowsAppControlUsed = false,
                foregroundActivationUsed = false,
                foregroundInputInjected = false,
            };
            WriteReport(Path.Combine(output, "fo1-hex-capture-report.json"), report);
            if (status == "pass")
                GD.Print($"OPENNV_FO1_HEX_CAPTURE_PASS output={output} files=2");
            else
                GD.PushError($"OPENNV_FO1_HEX_CAPTURE_VISUAL_FAIL output={output}");
            host.GetTree().Quit(status == "pass" ? 0 : 1);
        }
        catch (Exception exception)
        {
            GD.PushError($"OPENNV_FO1_HEX_CAPTURE_FAIL {exception.Message}");
            host.GetTree().Quit(1);
        }
    }

    private static async Task WaitForFrames(Node host, int count)
    {
        for (var index = 0; index < count; index++)
            await host.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
    }

    private static CaptureResult SaveViewport(
        Node host,
        string output,
        string filename,
        double minimumMean,
        double minimumDeviation)
    {
        var path = Path.Combine(output, filename);
        var image = host.GetViewport().GetTexture().GetImage();
        image.Convert(Image.Format.Rgba8);
        var data = image.GetData();
        var pixels = image.GetWidth() * image.GetHeight();
        double luminance = 0.0;
        double luminanceSquared = 0.0;
        var darkPixels = 0;
        for (var offset = 0; offset < data.Length; offset += 4)
        {
            var value = (0.2126 * data[offset] + 0.7152 * data[offset + 1] + 0.0722 * data[offset + 2]) / 255.0;
            luminance += value;
            luminanceSquared += value * value;
            if (value < 0.02)
                darkPixels++;
        }
        var mean = luminance / pixels;
        var deviation = Math.Sqrt(Math.Max(0.0, luminanceSquared / pixels - mean * mean));
        var darkFraction = (double)darkPixels / pixels;
        var failure = image.GetWidth() != 1280 || image.GetHeight() != 720
            ? "unexpected-size"
            : mean < minimumMean
                ? "mean-luminance"
                : deviation < minimumDeviation
                    ? "luminance-deviation"
                    : darkFraction > 0.75
                        ? "dark-fraction"
                        : null;
        var error = image.SavePng(path);
        if (error != Error.Ok)
            throw new InvalidOperationException($"Could not save Fallout hex capture: {error}");
        using var stream = File.OpenRead(path);
        var evidence = new
        {
            path,
            bytes = stream.Length,
            width = image.GetWidth(),
            height = image.GetHeight(),
            meanLuminance = mean,
            luminanceDeviation = deviation,
            darkFraction,
            visualGatePassed = failure is null,
            visualGateFailure = failure,
            sha256 = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(),
        };
        return new CaptureResult(failure is null, evidence);
    }

    private static void WriteReport(string path, object report)
    {
        File.WriteAllText(
            path,
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) +
                System.Environment.NewLine);
    }

    private readonly record struct CaptureResult(bool Passed, object Evidence);
}
