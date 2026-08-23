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
            if (loaded.Player.UsesClassicDiorama)
            {
                await CaptureClassicDiorama(host, loaded, output, scenePath, reportPath, hud);
                return;
            }
            if (hud is not null)
                hud.Visible = false;
            await WaitForRenderedFrames(host, 3);

            var files = new List<object>();
            var visualGates = new List<bool>();
            var actorShots = new List<object>();
            RetailActorStateContract.Contract? retailState = null;
            CellActorLoader.PlacedActor? targetActor = null;
            if (loaded.Actors.Count > 0)
            {
                var proofActors = loaded.Actors.Where(actor => actor.ProofEnabled).ToArray();
                if (proofActors.Length != 1 || retailStateContractPath is null)
                    throw new InvalidOperationException(
                        "Actor capture requires one proof-enabled target and one --retail-state-contract.");
                targetActor = proofActors[0];
                retailState = RetailActorStateContract.Load(
                    retailStateContractPath,
                    targetActor.Value.ReferenceFormId,
                    targetActor.Value.BaseFormId);
                var contextReferences = retailState.Shots.Values.First().ContextActors
                    .Select(actor => NormalizeForm(actor.ReferenceFormId))
                    .ToHashSet(StringComparer.Ordinal);
                var loadedContexts = loaded.Actors
                    .Where(actor => !actor.ProofEnabled)
                    .Select(actor => NormalizeForm(actor.ReferenceFormId))
                    .ToHashSet(StringComparer.Ordinal);
                if (!loadedContexts.SetEquals(contextReferences))
                    throw new InvalidOperationException(
                        "Loaded actor scenes do not exactly match the retail context actors.");
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
                var actor = targetActor!.Value;
                foreach (var kind in new[] { "front-portrait", "front-full-body" })
                {
                    var state = retailState!.Shots[kind];
                    ApplyActorState(
                        loaded,
                        actor,
                        state.ReferencePositionGameUnits,
                        state.ReferenceYawRadians,
                        state.AnimationFile,
                        state.AnimationPhaseSeconds,
                        state.PoseBones);
                    var contextActors = state.ContextActors.Select(context =>
                    {
                        var placed = loaded.Actors.Single(candidate =>
                            NormalizeForm(candidate.ReferenceFormId) ==
                            NormalizeForm(context.ReferenceFormId));
                        ApplyActorState(
                            loaded,
                            placed,
                            context.PositionGameUnits,
                            context.YawRadians,
                            context.AnimationFile,
                            context.AnimationPhaseSeconds,
                            context.PoseBones);
                        return new
                        {
                            referenceFormId = placed.ReferenceFormId,
                            baseFormId = placed.BaseFormId,
                            positionGameUnits = Vector(context.PositionGameUnits),
                            positionGodotWorld = Vector(placed.Placement.GlobalPosition),
                            yawRadians = context.YawRadians,
                            godotYawRadians = -context.YawRadians,
                            context.ActorSitSleepState,
                            context.FurnitureReferenceFormId,
                            context.FurnitureBaseFormId,
                            animationFile = context.AnimationFile,
                            requestedAnimationPhaseSeconds = context.AnimationPhaseSeconds,
                            appliedAnimationPhaseSeconds =
                                placed.Actor.AnimationPlayer.CurrentAnimationPosition,
                            poseBones = CaptureBonePoses(
                                placed,
                                context.PoseBones.Select(bone => bone.Name).ToArray()),
                            faceVertexHash = context.FaceVertexHash,
                        };
                    }).ToArray();
                    camera.Fov = state.VerticalFovDegrees;
                    camera.GlobalPosition = loaded.GameToWorld(state.CameraPositionGameUnits);
                    var cameraTarget = loaded.GameToWorld(state.CameraAimGameUnits);
                    camera.LookAt(cameraTarget, Vector3.Up);
                    await WaitForRenderedFrames(host, 5);
                    var fileName = $"godot-current-{kind}.png";
                    var actorFrame = SaveViewportPng(host, output, fileName, 0.04);
                    files.Add(actorFrame.Evidence);
                    visualGates.Add(actorFrame.Passed);
                    actorShots.Add(new
                    {
                        shotKind = kind,
                        retailStateApplied = true,
                        cellOriginGameUnits = Vector(loaded.OriginGameUnits),
                        unitsToMeters = loaded.UnitsToMeters,
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
                        poseBones = CaptureBonePoses(
                            actor,
                            state.PoseBones.Select(bone => bone.Name).ToArray()),
                        contextActors,
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
                    idleAnimationPath = actor.IdleAnimationPath,
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

    private static async Task CaptureClassicDiorama(
        Node3D host,
        CellSceneLoader.LoadedCell loaded,
        string output,
        string scenePath,
        string? reportPath,
        CanvasLayer? hud)
    {
        await WaitForRenderedFrames(host, 5);
        var withHud = SaveViewportPng(host, output, "classic-diorama-ui.png", 0.035, 0.05);
        if (hud is not null)
            hud.Visible = false;
        await WaitForRenderedFrames(host, 3);
        var environment = SaveViewportPng(host, output, "classic-diorama-environment.png", 0.035, 0.035);
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
            cameraFill = camera.FindChild("ClassicDioramaCameraFill", true, false) is DirectionalLight3D,
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
            visualThresholds = new
            {
                minimumMeanLuminance = 0.035,
                uiMinimumLuminanceDeviation = 0.05,
                environmentMinimumLuminanceDeviation = 0.035,
                maximumDarkFraction = 0.60,
            },
            files = new[] { withHud.Evidence, environment.Evidence },
        };
        WriteReport(Path.Combine(output, "classic-diorama-capture-report.json"), captureReport);
        if (reportPath is not null)
            WriteReport(reportPath, captureReport);
        if (visualQualityPassed)
            GD.Print($"OPENNV_CLASSIC_DIORAMA_CAPTURE_PASS output={output} files=2");
        else
            GD.PushError($"OPENNV_CLASSIC_DIORAMA_CAPTURE_VISUAL_FAIL output={output} files=2");
        host.GetTree().Quit(visualQualityPassed ? 0 : 1);
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
        double minimumMeanLuminance = 0.08,
        double minimumLuminanceDeviation = 0.05,
        double maximumDarkFraction = 0.60)
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
                : luminanceDeviation < minimumLuminanceDeviation
                    ? "luminance-deviation"
                    : darkFraction > maximumDarkFraction
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

    private static void ApplyActorState(
        CellSceneLoader.LoadedCell loaded,
        CellActorLoader.PlacedActor actor,
        Vector3 positionGameUnits,
        float yawRadians,
        string animationFile,
        double animationPhaseSeconds,
        IReadOnlyList<RetailActorStateContract.PoseBone> poseBones)
    {
        if (!NormalizeMeshPath(actor.IdleAnimationPath).Equals(
                NormalizeMeshPath(animationFile),
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Actor animation does not match retail: actor={actor.ReferenceFormId} " +
                $"cache={actor.IdleAnimationPath} retail={animationFile}");
        actor.Placement.Position = loaded.GameToCellUnits(positionGameUnits);
        actor.Placement.Rotation = new Vector3(0.0f, -yawRadians, 0.0f);
        actor.Actor.AnimationPlayer.Play(actor.Actor.PlayingAnimation);
        actor.Actor.AnimationPlayer.Seek(animationPhaseSeconds, true);
        actor.Actor.AnimationPlayer.Pause();
        var skeleton = Descendants<Skeleton3D>(actor.Actor.Root).Single();
        foreach (var bone in poseBones)
        {
            var index = skeleton.FindBone(bone.Name);
            if (index < 0)
                throw new InvalidOperationException(
                    $"Godot actor {actor.ReferenceFormId} is missing retail pose bone: {bone.Name}");
            var worldPosition = loaded.GameToWorld(bone.WorldPositionGameUnits);
            var skeletonPose = new Transform3D(
                skeleton.GlobalBasis.Orthonormalized().Inverse() *
                    bone.WorldBasis.Orthonormalized(),
                skeleton.ToLocal(worldPosition));
            skeleton.SetBoneGlobalPose(index, skeletonPose);
        }
    }

    private static object[] CaptureBonePoses(
        CellActorLoader.PlacedActor actor,
        IReadOnlyList<string> names)
    {
        var skeleton = Descendants<Skeleton3D>(actor.Actor.Root).Single();
        return names.Select(name =>
        {
            var index = skeleton.FindBone(name);
            if (index < 0)
                throw new InvalidOperationException(
                    $"Godot actor {actor.ReferenceFormId} is missing retail pose bone: {name}");
            var pose = skeleton.GetBoneGlobalPose(index);
            var parentIndex = skeleton.GetBoneParent(index);
            var localPose = parentIndex < 0
                ? pose
                : skeleton.GetBoneGlobalPose(parentIndex).AffineInverse() * pose;
            var localRotation = localPose.Basis.GetRotationQuaternion();
            var rotation = (skeleton.GlobalBasis * pose.Basis).GetRotationQuaternion();
            return (object)new
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
    }

    private static string NormalizeForm(string value) =>
        value.Replace("0x", "", StringComparison.OrdinalIgnoreCase)
            .PadLeft(8, '0')
            .ToLowerInvariant();

    private static string NormalizeMeshPath(string value)
    {
        var path = value.Replace('/', '\\').TrimStart('\\');
        return path.StartsWith("meshes\\", StringComparison.OrdinalIgnoreCase)
            ? path["meshes\\".Length..]
            : path;
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
