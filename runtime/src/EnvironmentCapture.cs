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
        string? reportPath,
        string? retailStateContractPath)
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
            var visualGates = new List<bool>();
            var actorShots = new List<object>();
            RetailActorStateContract.Contract? retailState = null;
            if (loaded.Actors.Count > 0)
            {
                if (loaded.Actors.Count != 1 || retailStateContractPath is null)
                    throw new InvalidOperationException(
                        "Actor capture requires one actor and one --retail-state-contract.");
                retailState = RetailActorStateContract.Load(
                    retailStateContractPath,
                    loaded.Actors[0].ReferenceFormId,
                    loaded.Actors[0].BaseFormId);
            }
            camera.Fov = 58.0f;
            camera.GlobalPosition = new Vector3(0.0f, 1.62f, -1.6f);
            camera.LookAt(new Vector3(-0.5f, 1.45f, -8.0f), Vector3.Up);
            await WaitForRenderedFrames(host, 3);
            var entryFrame = SaveViewportPng(host, output, "saloon-entry-textured.png");
            files.Add(entryFrame.Evidence);
            visualGates.Add(entryFrame.Passed);

            loaded.ProofDoor.SetOpen(true);
            camera.GlobalPosition = new Vector3(0.25f, 1.58f, -6.5f);
            camera.LookAt(new Vector3(0.0f, 1.42f, -13.0f), Vector3.Up);
            await WaitForRenderedFrames(host, 3);
            var roomFrame = SaveViewportPng(host, output, "saloon-room-wide.png");
            files.Add(roomFrame.Evidence);
            visualGates.Add(roomFrame.Passed);

            if (loaded.Actors.Count > 0)
            {
                var actor = loaded.Actors[0];
                var skeleton = Descendants<Skeleton3D>(actor.Actor.Root).Single();
                foreach (var kind in new[] { "front-portrait", "front-full-body" })
                {
                    var state = retailState!.Shots[kind];
                    actor.Placement.Position = loaded.GameToCellUnits(state.ReferencePositionGameUnits);
                    actor.Placement.Rotation = new Vector3(0.0f, -state.ReferenceYawRadians, 0.0f);
                    actor.Actor.AnimationPlayer.Play(actor.Actor.PlayingAnimation);
                    actor.Actor.AnimationPlayer.Seek(state.AnimationPhaseSeconds, true);
                    actor.Actor.AnimationPlayer.Pause();
                    camera.Fov = state.VerticalFovDegrees;
                    camera.GlobalPosition = loaded.GameToWorld(state.CameraPositionGameUnits);
                    var cameraTarget = loaded.GameToWorld(state.CameraAimGameUnits);
                    camera.LookAt(cameraTarget, Vector3.Up);
                    await WaitForRenderedFrames(host, 5);
                    var fileName = $"godot-current-{kind}.png";
                    var actorFrame = SaveViewportPng(host, output, fileName, 0.04);
                    files.Add(actorFrame.Evidence);
                    visualGates.Add(actorFrame.Passed);
                    var armBones = state.ArmBones.Select(name =>
                    {
                        var index = skeleton.FindBone(name);
                        if (index < 0)
                            throw new InvalidOperationException($"Godot actor is missing retail arm bone: {name}");
                        var pose = skeleton.GetBoneGlobalPose(index);
                        var parentIndex = skeleton.GetBoneParent(index);
                        var localPose = parentIndex < 0
                            ? pose
                            : skeleton.GetBoneGlobalPose(parentIndex).AffineInverse() * pose;
                        var localRotation = localPose.Basis.GetRotationQuaternion();
                        var rotation = (skeleton.GlobalBasis * pose.Basis).GetRotationQuaternion();
                        return new
                        {
                            name,
                            localTranslation = Vector(localPose.Origin),
                            localRotationQuaternion = new[]
                            {
                                localRotation.X,
                                localRotation.Y,
                                localRotation.Z,
                                localRotation.W,
                            },
                            worldPosition = Vector(skeleton.ToGlobal(pose.Origin)),
                            worldRotationQuaternion = new[] { rotation.X, rotation.Y, rotation.Z, rotation.W },
                        };
                    }).ToArray();
                    actorShots.Add(new
                    {
                        shotKind = kind,
                        retailStateApplied = true,
                        referencePositionGameUnits = Vector(state.ReferencePositionGameUnits),
                        referencePositionGodotWorld = Vector(actor.Placement.GlobalPosition),
                        referenceYawRadians = state.ReferenceYawRadians,
                        referenceGodotYawRadians = -state.ReferenceYawRadians,
                        cameraPositionGameUnits = Vector(state.CameraPositionGameUnits),
                        cameraAimGameUnits = Vector(state.CameraAimGameUnits),
                        cameraPosition = Vector(camera.GlobalPosition),
                        target = Vector(cameraTarget),
                        distanceMeters = state.CameraDistanceGameUnits * loaded.UnitsToMeters,
                        sourceDistanceGameUnits = state.CameraDistanceGameUnits,
                        verticalFovDegrees = camera.Fov,
                        projectionExact = state.ExactProjection,
                        projectionStatus = state.ProjectionStatus,
                        projectionSource = state.ProjectionSource,
                        projectionConfidence = state.ProjectionConfidence,
                        animationFile = state.AnimationFile,
                        requestedAnimationPhaseSeconds = state.AnimationPhaseSeconds,
                        appliedAnimationPhaseSeconds = actor.Actor.AnimationPlayer.CurrentAnimationPosition,
                        armBones,
                        faceVertexHash = state.FaceVertexHash,
                        hairVertexHash = state.HairVertexHash,
                        file = Path.Combine(output, fileName),
                    });
                }
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
                retailActorStateContract = retailState is null
                    ? null
                    : new
                    {
                        path = retailState.Path,
                        sha256 = retailState.Sha256,
                        exactProjectionResolved = retailState.ExactProjectionResolved,
                        shots = retailState.Shots.Count,
                    },
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

    private static async Task WaitForRenderedFrames(Node host, int count)
    {
        for (var index = 0; index < count; index++)
            await host.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
    }

    private static (object Evidence, bool Passed) SaveViewportPng(
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
        var error = image.SavePng(path);
        if (error != Error.Ok)
            throw new InvalidOperationException($"Godot could not save capture frame ({error}): {path}");
        var gateFailure = image.GetWidth() != 1280 || image.GetHeight() != 720
            ? "unexpected-size"
            : meanLuminance < minimumMeanLuminance
                ? "mean-luminance"
                : luminanceDeviation < 0.05
                    ? "luminance-deviation"
                    : darkFraction > 0.60
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
