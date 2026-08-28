using System.Security.Cryptography;
using System.Text.Json;
using Godot;

namespace OpenNV.Runtime;

internal static class StaticModelCaptureNumericContracts
{
    // Immutable format, source-art, geometry, and acceptance contracts.
    // Runtime-tunable Fallout 1 behavior remains in the versioned runtime recipe.
    internal const double AcceptanceDouble0Point03 = 0.03;
    internal const double AcceptanceDouble0Point04 = 0.04;
    internal const double AcceptanceDouble0Point05 = 0.05;
    internal const double AcceptanceDouble0Point0722 = 0.0722;
    internal const double AcceptanceDouble0Point2126 = 0.2126;
    internal const double AcceptanceDouble0Point65 = 0.65;
    internal const double AcceptanceDouble0Point7152 = 0.7152;
    internal const int AcceptanceInt1280 = 1280;
    internal const double AcceptanceDouble255Point0 = 255.0;
    internal const int AcceptanceInt5 = 5;
    internal const int AcceptanceInt720 = 720;
}

internal static class StaticModelCapture
{
    internal static async Task Run(
        Node host,
        StaticModelSlice.LoadedStaticModel loaded,
        string modelPath,
        string captureRoot,
        string? reportPath)
    {
        try
        {
            var output = Path.GetFullPath(captureRoot);
            if (Directory.Exists(output) || File.Exists(output))
                throw new InvalidOperationException($"Refusing to overwrite static capture: {output}");
            Directory.CreateDirectory(output);
            for (var index = 0; index < StaticModelCaptureNumericContracts.AcceptanceInt5; index++)
                await host.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            var framePath = Path.Combine(output, "static-model.png");
            var image = host.GetViewport().GetTexture().GetImage();
            image.Convert(Image.Format.Rgba8);
            var data = image.GetData();
            var pixels = image.GetWidth() * image.GetHeight();
            double luminance = 0.0;
            double luminanceSquared = 0.0;
            var darkPixels = 0;
            for (var offset = 0; offset < data.Length; offset += 4)
            {
                var value = (StaticModelCaptureNumericContracts.AcceptanceDouble0Point2126 * data[offset] + StaticModelCaptureNumericContracts.AcceptanceDouble0Point7152 * data[offset + 1] + StaticModelCaptureNumericContracts.AcceptanceDouble0Point0722 * data[offset + 2]) / StaticModelCaptureNumericContracts.AcceptanceDouble255Point0;
                luminance += value;
                luminanceSquared += value * value;
                if (value < StaticModelCaptureNumericContracts.AcceptanceDouble0Point03)
                    darkPixels++;
            }
            var mean = luminance / pixels;
            var deviation = Math.Sqrt(Math.Max(0.0, luminanceSquared / pixels - mean * mean));
            var darkFraction = (double)darkPixels / pixels;
            var failure = image.GetWidth() != StaticModelCaptureNumericContracts.AcceptanceInt1280 || image.GetHeight() != StaticModelCaptureNumericContracts.AcceptanceInt720
                ? "unexpected-size"
                : mean < StaticModelCaptureNumericContracts.AcceptanceDouble0Point04
                    ? "mean-luminance"
                    : deviation < StaticModelCaptureNumericContracts.AcceptanceDouble0Point05
                        ? "luminance-deviation"
                        : darkFraction > StaticModelCaptureNumericContracts.AcceptanceDouble0Point65
                            ? "dark-fraction"
                            : null;
            var error = image.SavePng(framePath);
            if (error != Error.Ok)
                throw new InvalidOperationException($"Could not save static model capture: {error}");
            using var stream = File.OpenRead(framePath);
            var report = new
            {
                schema = "opennv-static-model-capture/v1",
                status = failure is null ? "pass" : "fail",
                renderer = "forward_plus",
                model = modelPath,
                sourceSha256 = loaded.SourceSha256,
                projection = loaded.Projection,
                meshes = loaded.Meshes,
                surfaces = loaded.Surfaces,
                vertices = loaded.Vertices,
                materialBindings = loaded.MaterialBindings,
                boundsPosition = new[] { loaded.Bounds.Position.X, loaded.Bounds.Position.Y, loaded.Bounds.Position.Z },
                boundsSize = new[] { loaded.Bounds.Size.X, loaded.Bounds.Size.Y, loaded.Bounds.Size.Z },
                windowsAppControlUsed = false,
                foregroundActivationUsed = false,
                foregroundInputInjected = false,
                frame = new
                {
                    path = framePath,
                    bytes = stream.Length,
                    width = image.GetWidth(),
                    height = image.GetHeight(),
                    meanLuminance = mean,
                    luminanceDeviation = deviation,
                    darkFraction,
                    visualGatePassed = failure is null,
                    visualGateFailure = failure,
                    sha256 = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(),
                },
            };
            WriteReport(Path.Combine(output, "static-model-capture-report.json"), report);
            if (reportPath is not null)
                WriteReport(reportPath, report);
            if (failure is null)
                GD.Print($"OPENNV_STATIC_MODEL_CAPTURE_PASS output={output}");
            else
                GD.PushError($"OPENNV_STATIC_MODEL_CAPTURE_VISUAL_FAIL output={output} failure={failure}");
            host.GetTree().Quit(failure is null ? 0 : 1);
        }
        catch (Exception exception)
        {
            GD.PushError($"OPENNV_STATIC_MODEL_CAPTURE_FAIL {exception.Message}");
            host.GetTree().Quit(1);
        }
    }

    private static void WriteReport(string path, object report)
    {
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(
            fullPath,
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) +
                System.Environment.NewLine);
    }
}
