using System.Security.Cryptography;
using System.Text.Json;
using Godot;

namespace OpenNV.Runtime;

internal static class Fo1CampaignPresentationCaptureNumericContracts
{
    // Immutable format, source-art, geometry, and acceptance contracts.
    // Runtime-tunable Fallout 1 behavior remains in the versioned runtime recipe.
    internal const double AcceptanceDouble0Point0722 = 0.0722;
    internal const double AcceptanceDouble0Point2126 = 0.2126;
    internal const double AcceptanceDouble0Point7152 = 0.7152;
}

internal static class Fo1CampaignPresentationCapture
{
    internal static async Task Run(
        Node host,
        Fo1CampaignPresentationCatalog catalog,
        Fo1CampaignPresentationViewer viewer,
        Fo1CampaignMapViewCoverage coverage,
        string captureRoot,
        string? externalReportPath)
    {
        try
        {
            if (DisplayServer.GetName() == "headless")
                throw new InvalidOperationException(
                    "Fallout campaign visual capture requires a rendering display driver.");
            var output = Path.GetFullPath(captureRoot);
            if (Directory.Exists(output) || File.Exists(output))
                throw new InvalidOperationException(
                    $"Refusing to overwrite Fallout campaign visual capture: {output}");
            Directory.CreateDirectory(output);
            var profile = catalog.Viewer.Capture;
            await WaitForFrames(host, profile.WarmupFrames);
            var ui = SaveViewport(host, output, "campaign-entry-ui.png", profile);
            viewer.SetStatusVisible(false);
            await WaitForFrames(host, profile.SettleFrames);
            var entry = SaveViewport(host, output, "campaign-entry-clean.png", profile);
            viewer.SetCaptureSize(catalog.RuntimeProfile.Camera.Tactical.MaximumSizeMeters);
            await WaitForFrames(host, profile.SettleFrames);
            var overview = SaveViewport(host, output, "campaign-overview-clean.png", profile);
            var passed = ui.Passed && entry.Passed && overview.Passed;
            var report = new
            {
                schema = "opennv-fo1-campaign-visual-capture/v1",
                status = passed ? "pass-selected-connected-wall-topology-render" : "fail-visual-gate",
                renderer = "forward_plus",
                campaign = catalog.CampaignPath,
                campaignSha256 = catalog.CampaignSha256,
                selectedMap = coverage,
                files = new[] { ui.Evidence, entry.Evidence, overview.Evidence },
                promotion = new
                {
                    transportedMaps = catalog.Maps.Count,
                    sourceReferencePreparedMaps = catalog.Maps.Count,
                    runtimeValidatedMaps = catalog.Maps.Count,
                    runtimeConstructedMaps = 1,
                    renderedMaps = passed ? 1 : 0,
                    interactiveGameplayMaps = 0,
                    questExecutableMaps = 0,
                    firstPersonReadyMaps = 0,
                    openXrAcceptedMaps = 0,
                },
                windowsAppControlUsed = false,
                foregroundActivationUsed = false,
                foregroundInputInjected = false,
            };
            var reportPath = Path.Combine(output, "campaign-visual-capture-report.json");
            WriteReport(reportPath, report, refuseOverwrite: false);
            if (externalReportPath is not null)
                WriteReport(Path.GetFullPath(externalReportPath), report, refuseOverwrite: true);
            if (passed)
                GD.Print(
                    $"OPENNV_FO1_CAMPAIGN_CAPTURE_PASS map={coverage.MapId} " +
                    $"elevation={coverage.Elevation} files=3 output={output}");
            else
                GD.PushError(
                    $"OPENNV_FO1_CAMPAIGN_CAPTURE_VISUAL_FAIL map={coverage.MapId} " +
                    $"elevation={coverage.Elevation} output={output}");
            host.GetTree().Quit(passed ? 0 : 1);
        }
        catch (Exception exception)
        {
            GD.PushError($"OPENNV_FO1_CAMPAIGN_CAPTURE_FAIL {exception.Message}");
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
        Fo1CampaignCaptureProfile profile)
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
            var value =
                (Fo1CampaignPresentationCaptureNumericContracts.AcceptanceDouble0Point2126 * data[offset] + Fo1CampaignPresentationCaptureNumericContracts.AcceptanceDouble0Point7152 * data[offset + 1] + Fo1CampaignPresentationCaptureNumericContracts.AcceptanceDouble0Point0722 * data[offset + 2]) /
                byte.MaxValue;
            luminance += value;
            luminanceSquared += value * value;
            if (value < profile.DarkPixelLuminance)
                darkPixels++;
        }
        var mean = luminance / pixels;
        var deviation = Math.Sqrt(Math.Max(0.0, luminanceSquared / pixels - mean * mean));
        var darkFraction = (double)darkPixels / pixels;
        var failure = image.GetWidth() != profile.ExpectedWidthPixels ||
            image.GetHeight() != profile.ExpectedHeightPixels
            ? "unexpected-size"
            : mean < profile.MinimumMeanLuminance
                ? "mean-luminance"
                : deviation < profile.MinimumLuminanceDeviation
                    ? "luminance-deviation"
                    : darkFraction > profile.MaximumDarkFraction
                        ? "dark-fraction"
                        : null;
        var error = image.SavePng(path);
        if (error != Error.Ok)
            throw new InvalidOperationException($"Could not save Fallout campaign capture: {error}");
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

    private static void WriteReport(string path, object report, bool refuseOverwrite)
    {
        if (refuseOverwrite && (File.Exists(path) || Directory.Exists(path)))
            throw new InvalidOperationException(
                $"Refusing to overwrite Fallout campaign capture report: {path}");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(
            path,
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) +
                System.Environment.NewLine);
    }

    private readonly record struct CaptureResult(bool Passed, object Evidence);
}
