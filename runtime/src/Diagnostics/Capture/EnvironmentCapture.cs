using System.Security.Cryptography;
using System.Text.Json;
using Godot;

using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Diagnostics.Capture;

internal static class EnvironmentCaptureNumericContracts
{
    // Immutable format, source-art, geometry, and acceptance contracts.
    // Runtime-tunable Fallout 1 behavior remains in the versioned runtime recipe.
    internal const double AcceptanceDouble0Point035 = 0.035;
}

internal static class EnvironmentCapture
{
    internal static async Task Run(
        Node3D host,
        CellSceneLoader.LoadedCell loaded,
        RuntimeConfiguration configuration,
        string captureRoot,
        string scenePath,
        string? reportPath,
        string? galleryShotPath)
    {
        if (galleryShotPath is not null)
            GD.Print(
                $"OPENNV_GALLERY_STAGE id={Path.GetFileNameWithoutExtension(galleryShotPath)} " +
                "stage=environment-capture-enter");
        if (galleryShotPath is not null)
        {
            await GalleryCapture.Run(
                host,
                loaded,
                configuration,
                captureRoot,
                scenePath,
                reportPath,
                galleryShotPath);
            return;
        }
        try
        {
            var output = Path.GetFullPath(captureRoot);
            if (Directory.Exists(output) || File.Exists(output))
                throw new InvalidOperationException($"Refusing to overwrite capture output: {output}");
            Directory.CreateDirectory(output);
            loaded.Player.ProcessMode = Node.ProcessModeEnum.Disabled;
            var camera = loaded.Player.Camera;
            var hud = loaded.Session.GetNodeOrNull<CanvasLayer>("GameplayHud");
            if (loaded.Player.UsesClassicDiorama)
            {
                await CaptureClassicDiorama(
                    host,
                    loaded,
                    configuration,
                    output,
                    scenePath,
                    reportPath,
                    hud);
                return;
            }
            if (hud is not null)
                hud.Visible = false;
            await WaitForRenderedFrames(host, configuration.Capture.RenderedFramesBeforeCapture);

            var files = new List<object>();
            var visualGates = new List<bool>();
            foreach (var shot in configuration.Capture.EnvironmentShots)
            {
                if (shot.OpenProofDoorBeforeShot)
                    loaded.ProofDoor.SetOpen(true);
                camera.Fov = shot.VerticalFovDegrees;
                camera.GlobalPosition = shot.CameraPositionMeters.Vector3();
                camera.LookAt(shot.LookAtMeters.Vector3(), Vector3.Up);
                await WaitForRenderedFrames(host, configuration.Capture.RenderedFramesBeforeCapture);
                var frame = SaveViewportPng(
                    host,
                    output,
                    shot.OutputFile,
                    configuration.Capture.MinimumMeanLuminance,
                    configuration.Capture);
                files.Add(frame.Evidence);
                visualGates.Add(frame.Passed);
            }

            var visualQualityPassed = visualGates.All(passed => passed);
            var captureReport = new
            {
                schema = "opennv-godot-environment-capture/v1",
                status = visualQualityPassed ? "pass" : "fail",
                renderer = "forward_plus",
                scene = scenePath,
                sceneSha256 = FileSha256(VerifiedGltfLoader.ResolvePath(scenePath)),
                cellFormId = loaded.FormId,
                cellEditorId = loaded.EditorId,
                actorCount = loaded.Actors.Count,
                actorReferences = loaded.Actors.Select(actor => new
                {
                    formId = actor.ReferenceFormId,
                    baseFormId = actor.BaseFormId,
                    initiallyDisabled = actor.InitiallyDisabled,
                    proofEnabled = actor.ProofEnabled,
                    actorFormId = actor.Actor.FormId,
                    actorName = actor.Actor.Name,
                    raceFormId = actor.RaceFormId,
                    hairFormId = actor.HairFormId,
                    eyesFormId = actor.EyesFormId,
                    outfitFormIds = actor.OutfitFormIds,
                    headPartFormIds = actor.HeadPartFormIds,
                    animation = actor.Actor.PlayingAnimation,
                    idleAnimationPath = actor.IdleAnimationPath,
                    boundsMinimum = Vector(actor.Actor.Bounds.Position),
                    boundsSize = Vector(actor.Actor.Bounds.Size),
                }),
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
            if (visualQualityPassed)
                GD.Print($"OPENNV_GODOT_ENVIRONMENT_CAPTURE_PASS output={output} files={files.Count}");
            else
                GD.PushError($"OPENNV_GODOT_ENVIRONMENT_CAPTURE_VISUAL_FAIL output={output} files={files.Count}");
            host.GetTree().Quit(visualQualityPassed ? 0 : 1);
        }
        catch (Exception exception)
        {
            GD.PushError($"OPENNV_GODOT_ENVIRONMENT_CAPTURE_FAIL {exception.Message}");
            host.GetTree().Quit(1);
        }
    }

    private static async Task CaptureClassicDiorama(
        Node3D host,
        CellSceneLoader.LoadedCell loaded,
        RuntimeConfiguration configuration,
        string output,
        string scenePath,
        string? reportPath,
        CanvasLayer? hud)
    {
        await WaitForRenderedFrames(host, configuration.Capture.RenderedFramesBeforeCapture);
        var withHud = SaveViewportPng(
            host,
            output,
            "classic-diorama-ui.png",
            EnvironmentCaptureNumericContracts.AcceptanceDouble0Point035,
            configuration.Capture);
        if (hud is not null)
            hud.Visible = false;
        await WaitForRenderedFrames(host, configuration.Capture.RenderedFramesBeforeCapture);
        var environment = SaveViewportPng(
            host,
            output,
            "classic-diorama-environment.png",
            EnvironmentCaptureNumericContracts.AcceptanceDouble0Point035,
            configuration.Capture);
        var visualQualityPassed = withHud.Passed && environment.Passed;
        var camera = loaded.Player.Camera;
        var captureReport = new
        {
            schema = "opennv-classic-diorama-capture/v1",
            status = visualQualityPassed ? "pass" : "fail",
            renderer = "forward_plus",
            scene = scenePath,
            sceneSha256 = FileSha256(VerifiedGltfLoader.ResolvePath(scenePath)),
            cellFormId = loaded.FormId,
            cellEditorId = loaded.EditorId,
            presentation = "classic-diorama",
            projection = "orthogonal",
            cameraName = camera.Name.ToString(),
            cameraPosition = Vector(camera.GlobalPosition),
            cameraRotationDegrees = Vector(camera.GlobalRotationDegrees),
            orthographicSizeMeters = camera.Size,
            framingBoundsPosition = loaded.Player.DioramaFramingBounds is Aabb bounds
                ? Vector(bounds.Position)
                : null,
            framingBoundsSize = loaded.Player.DioramaFramingBounds is Aabb framing
                ? Vector(framing.Size)
                : null,
            cameraFill = camera.FindChild(
                "ClassicDioramaCameraFill",
                true,
                false) is DirectionalLight3D,
            assets = loaded.Assets,
            textures = loaded.Textures,
            materialBindings = loaded.MaterialBindings,
            references = loaded.References,
            authoredLights = loaded.AuthoredLights,
            collisionMeshes = loaded.CollisionMeshes,
            windowsAppControlUsed = false,
            foregroundActivationUsed = false,
            foregroundInputInjected = false,
            turnSimulationConnected = false,
            visualTarget = "manual concept reference; not a retail acceptance oracle",
            files = new[] { withHud.Evidence, environment.Evidence },
        };
        WriteReport(Path.Combine(output, "classic-diorama-capture-report.json"), captureReport);
        if (reportPath is not null)
            WriteReport(reportPath, captureReport);
        if (visualQualityPassed)
            GD.Print($"OPENNV_CLASSIC_DIORAMA_CAPTURE_PASS output={output} files=2");
        else
            GD.PushError(
                $"OPENNV_CLASSIC_DIORAMA_CAPTURE_VISUAL_FAIL output={output} files=2");
        host.GetTree().Quit(visualQualityPassed ? 0 : 1);
    }

    internal static async Task WaitForRenderedFrames(Node host, int count)
    {
        for (var index = 0; index < count; index++)
            await host.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
    }

    internal static (object Evidence, bool Passed) SaveViewportPng(
        Node host,
        string output,
        string name,
        double minimumMeanLuminance,
        CaptureConfiguration configuration,
        Vector2I? expectedSize = null)
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
        var weights = configuration.LuminanceWeightsRgb;
        for (var index = 0; index < data.Length; index += configuration.RgbaChannelCount)
        {
            var luminance = (
                weights[0] * data[index] +
                weights[1] * data[index + 1] +
                weights[2] * data[index + 2]) / configuration.PixelChannelMaximum;
            luminanceSum += luminance;
            luminanceSquaredSum += luminance * luminance;
            if (luminance < configuration.DarkPixelLuminance)
                darkPixels++;
        }
        var meanLuminance = luminanceSum / pixels;
        var variance = Math.Max(0.0, luminanceSquaredSum / pixels - meanLuminance * meanLuminance);
        var luminanceDeviation = Math.Sqrt(variance);
        var darkFraction = (double)darkPixels / pixels;
        var error = image.SavePng(path);
        if (error != Error.Ok)
            throw new InvalidOperationException($"Godot could not save capture frame ({error}): {path}");
        var requiredSize = expectedSize ?? new Vector2I(
            configuration.ExpectedWidthPixels,
            configuration.ExpectedHeightPixels);
        var gateFailure = image.GetWidth() != requiredSize.X ||
            image.GetHeight() != requiredSize.Y
            ? "unexpected-size"
            : meanLuminance < minimumMeanLuminance
                ? "mean-luminance"
                : luminanceDeviation < configuration.MinimumLuminanceDeviation
                    ? "luminance-deviation"
                    : darkFraction > configuration.MaximumDarkPixelFraction
                        ? "dark-fraction"
                        : null;
        using var stream = File.OpenRead(path);
        return (new
        {
            path,
            bytes = stream.Length,
            width = image.GetWidth(),
            height = image.GetHeight(),
            meanLuminance,
            luminanceDeviation,
            darkFraction,
            visualGatePassed = gateFailure is null,
            visualGateFailure = gateFailure,
            sha256 = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(),
        }, gateFailure is null);
    }

    private static string FileSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static float[] Vector(Vector3 value) => new[] { value.X, value.Y, value.Z };

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
