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
            var hud = loaded.Session.GetNodeOrNull<CanvasLayer>("GameplayHud");
            if (hud is not null)
                hud.Visible = false;
            await WaitForRenderedFrames(host, 3);

            var files = new List<object>();
            var actorShots = new List<object>();
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

            if (loaded.Actors.Count > 0)
            {
                var actor = loaded.Actors[0];
                var forward = -actor.Placement.GlobalBasis.Z;
                forward.Y = 0.0f;
                forward = forward.Normalized();
                var skeleton = Descendants<Skeleton3D>(actor.Actor.Root).Single();
                var headIndex = skeleton.FindBone("Bip01 Head");
                if (headIndex < 0)
                    throw new InvalidOperationException("Retail portrait contract requires Bip01 Head.");
                const float unitsToMeters = 0.0142875f;
                var head = skeleton.ToGlobal(skeleton.GetBoneGlobalPose(headIndex).Origin);
                var faceTarget = head + Vector3.Up * (20.0f * unitsToMeters);
                var faceDistance = 70.0f * unitsToMeters;
                camera.Fov = 75.0f;
                camera.GlobalPosition = faceTarget + forward * faceDistance;
                camera.LookAt(faceTarget, Vector3.Up);
                await WaitForRenderedFrames(host, 5);
                const string faceFileName = "godot-current-front-portrait.png";
                files.Add(SaveViewportPng(host, output, faceFileName, 0.04));
                actorShots.Add(new
                {
                    shotKind = "front-portrait",
                    cameraPosition = Vector(camera.GlobalPosition),
                    target = Vector(faceTarget),
                    distanceMeters = faceDistance,
                    sourceDistanceGameUnits = 70.0,
                    verticalFovDegrees = camera.Fov,
                    file = Path.Combine(output, faceFileName),
                });

                var bodyTarget = actor.Actor.Bounds.GetCenter();
                var bodyDistance = 366.962036f * unitsToMeters;
                camera.Fov = 75.0f;
                camera.GlobalPosition = bodyTarget + forward * bodyDistance;
                camera.LookAt(bodyTarget, Vector3.Up);
                await WaitForRenderedFrames(host, 5);
                const string bodyFileName = "godot-current-front-full-body.png";
                files.Add(SaveViewportPng(host, output, bodyFileName, 0.04));
                actorShots.Add(new
                {
                    shotKind = "front-full-body",
                    cameraPosition = Vector(camera.GlobalPosition),
                    target = Vector(bodyTarget),
                    distanceMeters = bodyDistance,
                    sourceDistanceGameUnits = 366.962036,
                    verticalFovDegrees = camera.Fov,
                    file = Path.Combine(output, bodyFileName),
                });
            }

            var captureReport = new
            {
                schema = "opennv-godot-environment-capture/v1",
                status = "pass",
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
                    outfitFormId = actor.OutfitFormId,
                    headPartFormIds = actor.HeadPartFormIds,
                    animation = actor.Actor.PlayingAnimation,
                    boundsMinimum = Vector(actor.Actor.Bounds.Position),
                    boundsSize = Vector(actor.Actor.Bounds.Size),
                }),
                actorShots,
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

    private static object SaveViewportPng(
        Node host,
        string output,
        string name,
        double minimumMeanLuminance = 0.08)
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
            meanLuminance < minimumMeanLuminance || luminanceDeviation < 0.05 || darkFraction > 0.60)
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

    private static float[] Vector(Vector3 value) => new[] { value.X, value.Y, value.Z };

    private static IEnumerable<T> Descendants<T>(Node node)
        where T : Node
    {
        foreach (var child in node.GetChildren())
        {
            if (child is T match)
                yield return match;
            foreach (var descendant in Descendants<T>(child))
                yield return descendant;
        }
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
