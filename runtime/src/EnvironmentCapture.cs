using System.Security.Cryptography;
using System.Text.Json;
using Godot;

namespace OpenNV.Runtime;

internal static class EnvironmentCapture
{
    internal static async Task Run(
        Node3D host,
        CellSceneLoader.LoadedCell loaded,
        string captureRoot,
        string scenePath,
        string? reportPath)
    {
        try
        {
            var output = Path.GetFullPath(captureRoot);
            if (Directory.Exists(output) || File.Exists(output))
                throw new InvalidOperationException($"Refusing to overwrite capture output: {output}");
            Directory.CreateDirectory(output);
            loaded.Player.ProcessMode = Node.ProcessModeEnum.Disabled;
            var camera = loaded.Player.Camera;
            await WaitForRenderedFrames(host, 3);

            var files = new List<object>();
            camera.Fov = 58.0f;
            camera.GlobalPosition = new Vector3(0.0f, 1.62f, -1.6f);
            camera.LookAt(new Vector3(-0.5f, 1.45f, -8.0f), Vector3.Up);
            await WaitForRenderedFrames(host, 3);
            files.Add(SaveViewportPng(host, output, "saloon-entry-textured.png"));

            loaded.ProofDoor.SetOpen(true);
            camera.GlobalPosition = new Vector3(0.25f, 1.58f, -6.5f);
            camera.LookAt(new Vector3(0.0f, 1.42f, -13.0f), Vector3.Up);
            await WaitForRenderedFrames(host, 3);
            files.Add(SaveViewportPng(host, output, "saloon-room-wide.png"));

            var captureReport = new
            {
                schema = "opennv-godot-environment-capture/v1",
                status = "pass",
                renderer = "forward_plus",
                scene = scenePath,
                sceneSha256 = FileSha256(VerifiedGltfLoader.ResolvePath(scenePath)),
                cellFormId = loaded.FormId,
                cellEditorId = loaded.EditorId,
                actorCount = 0,
                textures = loaded.Textures,
                materialBindings = loaded.MaterialBindings,
                authoredLights = loaded.AuthoredLights,
                proofDoorFormId = loaded.ProofDoorFormId,
                proofDoorOpen = true,
                windowsAppControlUsed = false,
                foregroundActivationUsed = false,
                foregroundInputInjected = false,
                files,
            };
            WriteReport(Path.Combine(output, "environment-capture-report.json"), captureReport);
            if (reportPath is not null)
                WriteReport(reportPath, captureReport);
            GD.Print($"OPENNV_GODOT_ENVIRONMENT_CAPTURE_PASS output={output} files={files.Count}");
            host.GetTree().Quit(0);
        }
        catch (Exception exception)
        {
            GD.PushError($"OPENNV_GODOT_ENVIRONMENT_CAPTURE_FAIL {exception.Message}");
            host.GetTree().Quit(1);
        }
    }

    private static async Task WaitForRenderedFrames(Node host, int count)
    {
        for (var index = 0; index < count; index++)
            await host.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
    }

    private static object SaveViewportPng(Node host, string output, string name)
    {
        var path = Path.Combine(output, name);
        if (File.Exists(path))
            throw new InvalidOperationException($"Refusing to overwrite capture frame: {path}");
        var image = host.GetViewport().GetTexture().GetImage();
        image.Convert(Image.Format.Rgba8);
        var data = image.GetData();
        var pixels = image.GetWidth() * image.GetHeight();
        double luminanceSum = 0.0;
        double luminanceSquaredSum = 0.0;
        var darkPixels = 0;
        for (var index = 0; index < data.Length; index += 4)
        {
            var luminance = (0.2126 * data[index] + 0.7152 * data[index + 1] + 0.0722 * data[index + 2]) / 255.0;
            luminanceSum += luminance;
            luminanceSquaredSum += luminance * luminance;
            if (luminance < 0.03)
                darkPixels++;
        }
        var meanLuminance = luminanceSum / pixels;
        var variance = Math.Max(0.0, luminanceSquaredSum / pixels - meanLuminance * meanLuminance);
        var luminanceDeviation = Math.Sqrt(variance);
        var darkFraction = (double)darkPixels / pixels;
        if (image.GetWidth() != 1280 || image.GetHeight() != 720 ||
            meanLuminance < 0.08 || luminanceDeviation < 0.05 || darkFraction > 0.60)
            throw new InvalidOperationException(
                $"Capture frame failed visual metrics: name={name} size={image.GetWidth()}x{image.GetHeight()} " +
                $"mean={meanLuminance:F4} deviation={luminanceDeviation:F4} darkFraction={darkFraction:F4}");
        var error = image.SavePng(path);
        if (error != Error.Ok)
            throw new InvalidOperationException($"Godot could not save capture frame ({error}): {path}");
        using var stream = File.OpenRead(path);
        return new
        {
            path,
            bytes = stream.Length,
            width = image.GetWidth(),
            height = image.GetHeight(),
            meanLuminance,
            luminanceDeviation,
            darkFraction,
            sha256 = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(),
        };
    }

    private static string FileSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
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
}
