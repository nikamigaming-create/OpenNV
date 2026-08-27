using System.Security.Cryptography;
using System.Text.Json;
using Godot;

namespace OpenNV.Runtime;

internal static class GalleryCapture
{
    internal static async Task Run(
        Node3D host,
        CellSceneLoader.LoadedCell loaded,
        RuntimeConfiguration configuration,
        string captureRoot,
        string scenePath,
        string? reportPath,
        string galleryShotPath)
    {
        try
        {
            var output = Path.GetFullPath(captureRoot);
            if (Directory.Exists(output) || File.Exists(output))
                throw new InvalidOperationException(
                    $"Refusing to overwrite gallery capture output: {output}");
            Directory.CreateDirectory(output);
            var shot = GalleryShotContract.Load(galleryShotPath, configuration);
            if (FalloutFormId.Normalize(loaded.FormId) !=
                FalloutFormId.Normalize(shot.Scene.CellFormId))
                throw new InvalidOperationException(
                    "Gallery shot and loaded background identify different rendered CELL records.");
            if (loaded.Actors.Count != 1)
                throw new InvalidOperationException(
                    $"Gallery capture requires exactly one loaded actor, found {loaded.Actors.Count}.");
            var actor = loaded.Actors.Single();
            if (FalloutFormId.Normalize(actor.ReferenceFormId) !=
                    FalloutFormId.Normalize(shot.ReferenceFormId) ||
                FalloutFormId.Normalize(actor.BaseFormId) !=
                    FalloutFormId.Normalize(shot.BaseFormId))
                throw new InvalidOperationException(
                    "Gallery shot and loaded actor identify different authored records.");
            var proofEnableExpected =
                shot.EnableStateMode == "proof-enable-initially-disabled";
            if (actor.ProofEnabled != proofEnableExpected ||
                (proofEnableExpected && !actor.InitiallyDisabled))
                throw new InvalidOperationException(
                    "Gallery shot enable-state contract differs from the loaded actor state.");

            RetailEnvironmentRenderer.Application? environmentApplication = null;
            if (shot.LocationClass == "exterior")
            {
                var capturedEnvironment = shot.RetailEvidence.Environment
                    ?? throw new InvalidOperationException(
                        "Exterior gallery shot has no retail environment evidence.");
                if (host.GetChildren().OfType<WorldEnvironment>().Any())
                    throw new InvalidOperationException(
                        "Exterior gallery loaded a provisional cell environment.");
                var resolvedScenePath = VerifiedGltfLoader.ResolvePath(scenePath);
                using var sceneDocument = JsonDocument.Parse(
                    File.ReadAllText(resolvedScenePath));
                var environmentCatalog = RetailExteriorEnvironment.Load(
                    sceneDocument.RootElement,
                    configuration.FalloutEnvironment.ImageSpace);
                environmentApplication = RetailEnvironmentRenderer.Apply(
                    host,
                    capturedEnvironment,
                    loaded.MainContent,
                    environmentCatalog,
                    configuration);
            }

            loaded.Player.ProcessMode = Node.ProcessModeEnum.Disabled;
            var hud = loaded.Session.GetNodeOrNull<CanvasLayer>("GameplayHud");
            if (hud is not null)
                hud.Visible = false;
            var animation = actor.Actor.AnimationPlayer;
            animation.CallbackModeProcess =
                AnimationMixer.AnimationCallbackModeProcess.Manual;
            animation.Play(actor.Actor.PlayingAnimation);
            animation.Advance(0.0);
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            var animationLength = animation.CurrentAnimationLength;
            var animationResource = animation.GetAnimation(actor.Actor.PlayingAnimation)
                ?? throw new InvalidOperationException(
                    "Gallery actor has no loaded authored animation resource.");
            animationResource.LoopMode = Animation.LoopModeEnum.Linear;
            var animationTrackCount = animationResource.GetTrackCount();
            if (animationLength <= 0.0 || animationTrackCount < 1 ||
                actor.Actor.AnimationChannels < 1 ||
                !actor.IdleAnimationPath.Equals(
                    actor.Actor.AnimationLogicalPath,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "Gallery actor authored-animation identity is incomplete.");
            var retailAnimation = ResolvePresentationAnimation(
                shot.RetailEvidence.Presentation,
                actor.IdleAnimationPath);
            var retailAnimationMatchesOwned = NormalizeAnimationPath(
                    retailAnimation.File)
                .Equals(
                    NormalizeAnimationPath(actor.IdleAnimationPath),
                    StringComparison.OrdinalIgnoreCase);
            var presentationAnimationPosition =
                retailAnimation.LastScaledSeconds % (float)animationLength;
            if (presentationAnimationPosition < 0.0f)
                presentationAnimationPosition += (float)animationLength;
            animation.Seek(presentationAnimationPosition, true);
            animation.Advance(0.0);
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            var placementReplay = GalleryActorPlacement.Apply(
                loaded,
                actor,
                shot.RetailEvidence.Presentation,
                configuration);
            var actorBounds = actor.Actor.Bounds;
            var camera = loaded.Player.Camera;
            var groundContact = GalleryGroundContact.Measure(
                camera.GetWorld3D().DirectSpaceState,
                actor,
                actorBounds,
                configuration,
                loaded.Player.CollisionMask);
            var framing = GalleryFraming.Apply(
                camera,
                actor,
                actorBounds,
                configuration,
                loaded.Player.CollisionMask,
                shot.RetailEvidence.Presentation);
            var lodLedger = CellLodLedger.Measure(loaded.MainContent, camera);
            await EnvironmentCapture.WaitForRenderedFrames(
                host,
                configuration.Capture.ActorRenderedFramesBeforeCapture);
            var animationPositionBefore = animation.CurrentAnimationPosition;
            var previousAnimationPosition = animationPositionBefore;
            var animationProgressSeconds = 0.0;
            var progressingFrames = 0;
            var animationStepSeconds =
                1.0 / configuration.Capture.Gallery.FramesPerSecond;
            for (var frameIndex = 0;
                 frameIndex < configuration.Capture.Gallery.FramesPerSubject;
                 frameIndex++)
            {
                animation.Advance(animationStepSeconds);
                await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
                var currentAnimationPosition = animation.CurrentAnimationPosition;
                var delta = currentAnimationPosition - previousAnimationPosition;
                if (delta < 0.0)
                    delta += animationLength;
                if (delta > 0.0)
                    progressingFrames++;
                animationProgressSeconds += Math.Max(delta, 0.0);
                previousAnimationPosition = currentAnimationPosition;
            }
            var animationPositionAfter = animation.CurrentAnimationPosition;
            var minimumAnimationProgressSeconds =
                configuration.Capture.Gallery.DurationSeconds *
                configuration.Capture.Gallery.MinimumMotionProgressFraction;
            var authoredMotionPassed =
                animationProgressSeconds >= minimumAnimationProgressSeconds &&
                progressingFrames > 0;
            animation.Seek(presentationAnimationPosition, true);
            animation.Advance(0.0);
            await EnvironmentCapture.WaitForRenderedFrames(
                host,
                configuration.Capture.ActorRenderedFramesBeforeCapture);
            var frame = EnvironmentCapture.SaveViewportPng(
                host,
                output,
                shot.OutputFile,
                configuration.Capture.ActorMinimumMeanLuminance,
                configuration.Capture);
            var environmentPassed = shot.LocationClass != "exterior" ||
                environmentApplication is { } applied &&
                applied.WeatherRecordApplied &&
                applied.ImageSpaceValidated &&
                applied.RetailRoadDiffuseCoreResolved &&
                applied.RetailLandscapeDiffuseCoreResolved &&
                applied.RetailGrassDiffuseCoreResolved &&
                applied.RetailActorDiffuseCoreResolved;
            var capturePassed =
                authoredMotionPassed &&
                groundContact.Passed &&
                lodLedger.Passed &&
                framing.HeadVisibilityClear &&
                environmentPassed;
            var captureReport = new
            {
                schema = "opennv-godot-gallery-capture/v5",
                status = capturePassed
                    ? "captured-gallery-retail-bound-pending-parity"
                    : !groundContact.Passed
                        ? "failed-ground-contact-gate"
                    : !lodLedger.Passed
                        ? "failed-lod-coverage-gate"
                    : !framing.HeadVisibilityClear
                        ? "failed-camera-visibility-gate"
                    : !environmentPassed
                        ? "failed-retail-environment-gate"
                    : !authoredMotionPassed
                        ? "failed-authored-motion-gate"
                        : "failed-capture-gate",
                parity = false,
                renderer = ProjectSettings.GetSetting(
                    "rendering/renderer/rendering_method").AsString(),
                configurationSchema = RuntimeConfiguration.ExpectedSchema,
                configurationSha256 = configuration.Sha256,
                galleryShot = new
                {
                    path = shot.Path,
                    sha256 = shot.Sha256,
                    shot.Id,
                    shot.Ordinal,
                    shot.Label,
                    shot.Location,
                    shot.LocationId,
                    shot.LocationClass,
                    actorCellFormId = shot.CellFormId,
                    renderedScene = new
                    {
                        cellFormId = shot.Scene.CellFormId,
                        worldspaceFormId = shot.Scene.WorldspaceFormId,
                        interior = shot.Scene.Interior,
                    },
                    shot.RecordType,
                    shot.EnableStateMode,
                    retailEvidence = new
                    {
                        path = shot.RetailEvidence.Evidence.Path,
                        sha256 = shot.RetailEvidence.Evidence.Sha256,
                        reportPath = shot.RetailEvidence.Report.Path,
                        reportSha256 = shot.RetailEvidence.Report.Sha256,
                        oracleJsonlPath = shot.RetailEvidence.OracleJsonl.Path,
                        oracleJsonlSha256 = shot.RetailEvidence.OracleJsonl.Sha256,
                        sourceFrames = shot.RetailEvidence.SourceFrames.Count,
                        shot.RetailEvidence.RuntimePluginStackEventSha256,
                        presentation = new
                        {
                            shotKind = shot.RetailEvidence.Presentation.ShotKind,
                            frame = shot.RetailEvidence.Presentation.Frame,
                            sourceFrame = shot.RetailEvidence.Presentation.SourceFrame.Path,
                            sourceFrameSha256 =
                                shot.RetailEvidence.Presentation.SourceFrame.Sha256,
                            shot.RetailEvidence.Presentation.CameraEventSha256,
                            sourceFrameCameraContractEventSha256 =
                                shot.RetailEvidence.Presentation
                                    .SourceFrameCameraContractEventSha256,
                            shot.RetailEvidence.Presentation.ActorSnapshotEventSha256,
                            shot.RetailEvidence.Presentation.ActorPoseEventSha256,
                            selection = new
                            {
                                policySchema = configuration.Capture.Gallery
                                    .RetailPresentationSelection.Schema,
                                tieBreak = configuration.Capture.Gallery
                                    .RetailPresentationSelection.TieBreak,
                                candidateShotKinds = configuration.Capture.Gallery
                                    .RetailPresentationSelection.CandidateShotKinds,
                                focusKind = shot.RetailEvidence.Presentation
                                    .Selection.FocusKind,
                                focusRuleOrdinal = shot.RetailEvidence.Presentation
                                    .Selection.FocusRuleOrdinal,
                                cameraDirectionDotFocusForward =
                                    shot.RetailEvidence.Presentation.Selection
                                        .CameraDirectionDotFocusForward,
                                surfaceStatus = shot.RetailEvidence.Presentation
                                    .Selection.SurfaceStatus,
                                semanticFocusSurface = shot.RetailEvidence.Presentation
                                    .Selection.SemanticFocusSurface,
                                cameraOutsideActorWorldBound =
                                    shot.RetailEvidence.Presentation.Selection
                                        .CameraOutsideActorWorldBound,
                                cameraCorridorPassed = shot.RetailEvidence.Presentation
                                    .Selection.CameraCorridorPassed,
                                cameraTranslationToleranceGameUnits =
                                    configuration.Capture.Gallery
                                        .RetailPresentationSelection
                                        .CameraTranslationToleranceGameUnits,
                            },
                            animation = retailAnimation.File,
                            ownedAnimationMatched = retailAnimationMatchesOwned,
                            animationPositionSeconds = presentationAnimationPosition,
                        },
                    },
                },
                scene = new
                {
                    path = scenePath,
                    sha256 = FileSha256(VerifiedGltfLoader.ResolvePath(scenePath)),
                    cellFormId = loaded.FormId,
                    loaded.EditorId,
                    originGameUnits = Vector(loaded.OriginGameUnits),
                    lod = CellLodLedger.Document(lodLedger),
                },
                actor = new
                {
                    referenceFormId = actor.ReferenceFormId,
                    baseFormId = actor.BaseFormId,
                    actorFormId = actor.Actor.FormId,
                    actorName = actor.Actor.Name,
                    initiallyDisabled = actor.InitiallyDisabled,
                    proofEnabled = actor.ProofEnabled,
                    placementGodotWorld = Vector(actor.Placement.GlobalPosition),
                    placementScale = Vector(actor.Placement.Scale),
                    matchedRetailPlacement = new
                    {
                        retailWorldTranslationGameUnits =
                            Vector(placementReplay.RetailWorldTranslationGameUnits),
                        cellLocalPositionGameUnits =
                            Vector(placementReplay.CellLocalPositionGameUnits),
                        expectedWorldPositionMeters =
                            Vector(placementReplay.ExpectedWorldPositionMeters),
                        measuredWorldPositionMeters =
                            Vector(placementReplay.MeasuredWorldPositionMeters),
                        placementReplay.RetailWorldScale,
                        placementReplay.PositionErrorMeters,
                        placementReplay.PositionErrorGameUnits,
                        placementReplay.BasisError,
                        placementReplay.Passed,
                        placementReplay.Derivation,
                    },
                    boundsMinimum = Vector(framing.Bounds.Position),
                    boundsSize = Vector(framing.Bounds.Size),
                    heightMeters = framing.Bounds.Size.Y,
                    animation = actor.Actor.PlayingAnimation,
                    idleAnimationPath = actor.IdleAnimationPath,
                    animationSourceLogicalPath = actor.Actor.AnimationLogicalPath,
                    animationSourceSha256 = actor.Actor.AnimationSourceSha256,
                    animationSourceChannels = actor.Actor.AnimationChannels,
                    animationTrackCount,
                    animationLengthSeconds = animationLength,
                    animationPositionBeforeSeconds = animationPositionBefore,
                    animationPositionAfterSeconds = animationPositionAfter,
                    presentationAnimationPositionSeconds =
                        presentationAnimationPosition,
                    animationProgressSeconds,
                    progressingFrames,
                    poseContract = new
                    {
                        skeletonRootNode = actor.Actor.PoseContract.SkeletonRootNode,
                        skeletonRootNodeIndex = actor.Actor.PoseContract.SkeletonRootNodeIndex,
                        facingNode = actor.Actor.PoseContract.FacingNode,
                        facingNodeIndex = actor.Actor.PoseContract.FacingNodeIndex,
                        facingDerivation = actor.Actor.PoseContract.FacingSource,
                        facingRuntimeSource = actor.Actor.PoseContract.FacingRuntimeSource,
                        headNode = actor.Actor.PoseContract.HeadNode,
                        headNodeIndex = actor.Actor.PoseContract.HeadNodeIndex,
                        headDerivation = actor.Actor.PoseContract.HeadSource,
                    },
                    groundContact = new
                    {
                        groundContact.GroundFound,
                        groundContact.Passed,
                        actorRootPosition = Vector(groundContact.ActorRootPosition),
                        groundPosition = Vector(groundContact.GroundPosition),
                        groundContact.DeltaMeters,
                        groundContact.DeltaGameUnits,
                        groundContact.ToleranceGameUnits,
                        groundContact.ToleranceMeters,
                        groundContact.ConfiguredToleranceGameUnits,
                        groundContact.NumericPrecisionToleranceGameUnits,
                        groundContact.GroundContactMaximumUlp,
                        groundContact.VisualBoundsMinimumY,
                        groundContact.VisualBoundsMaximumY,
                        probePosition = Vector(groundContact.ProbePosition),
                        groundContact.ColliderPath,
                        groundContact.RayDirection,
                        groundContact.Derivation,
                        toleranceProvenance = configuration.ActorParity.Provenance,
                    },
                    compilerOmissions = actor.Actor.OmittedSurfaces.Select(surface => new
                    {
                        surface.Role,
                        surface.ModelPath,
                        surface.ModelSha256,
                        surface.Shape,
                        surface.AttachmentNode,
                        surface.AttachmentSource,
                        surface.Disposition,
                        surface.Authority,
                    }).ToArray(),
                },
                camera = new
                {
                    derivation = framing.Derivation,
                    retailShotKind = framing.RetailShotKind,
                    retailFrame = framing.RetailFrame,
                    framing.CameraEventSha256,
                    framing.ActorSnapshotEventSha256,
                    framing.ActorPoseEventSha256,
                    position = Vector(framing.CameraPosition),
                    desiredPosition = Vector(framing.DesiredCameraPosition),
                    poseAdjustedPosition = Vector(
                        framing.PoseAdjustedCameraPosition),
                    target = Vector(framing.Target),
                    front = Vector(framing.Front),
                    right = Vector(framing.Right),
                    distanceMeters = framing.CameraDistanceMeters,
                    desiredDistanceMeters = framing.DesiredCameraDistanceMeters,
                    facingPoseRotationQuaternion = Quaternion(framing.FacingPoseRotation),
                    framing.FacingPoseCorrectionRadians,
                    framing.ProjectedWidthMeters,
                    framing.ProjectedDepthMeters,
                    framing.ViewportAspect,
                    framing.VerticalFovDegrees,
                    framing.MaximumFrameOccupancy,
                    framing.ModelFrontAxis,
                    framing.TargetNodeRole,
                    framing.FacingPoseSource,
                    framing.FacingDerivation,
                    framing.FacingNode,
                    framing.FacingNodeIndex,
                    facingRuntimeSource = actor.Actor.PoseContract.FacingRuntimeSource,
                    framing.HeadDerivation,
                    framing.HeadNode,
                    framing.HeadNodeIndex,
                    framing.OcclusionClearanceSource,
                    framing.OcclusionClearanceMeters,
                    framing.OcclusionResolved,
                    framing.OccludingColliderPath,
                    occlusionHitPosition = Vector(framing.OcclusionHitPosition),
                    projectedHeadPixels = Vector(framing.ProjectedHeadPixels),
                    viewportSizePixels = Vector(framing.ViewportSizePixels),
                    framing.HeadInViewport,
                    framing.HeadVisibilityClear,
                    framing.AimAdjusted,
                    policyProvenance = configuration.Capture.Gallery.Provenance,
                },
                lighting = new
                {
                    mode = shot.LocationClass == "exterior"
                        ? "retail-observed-owned-record-resolved"
                        : "authored-interior-cell",
                    passed = environmentPassed,
                    capturedCurrentWeatherForm =
                        shot.RetailEvidence.Environment?.WeatherForm,
                    capturedDefaultWeatherForm =
                        shot.RetailEvidence.Environment?.DefaultWeatherForm,
                    effectiveWeatherForm = shot.RetailEvidence.EffectiveWeatherForm,
                    capturedGameHour = shot.RetailEvidence.Environment?.GameHour,
                    capturedImageSpaceForm =
                        shot.RetailEvidence.Environment?.ImageSpace.FormId,
                    application = environmentApplication is { } retailEnvironment
                        ? new
                        {
                            weatherFormId = retailEnvironment.Environment.WeatherFormId,
                            retailEnvironment.Environment.WeatherEditorId,
                            retailEnvironment.Environment.GameHour,
                            ambientEncoded = Rgba(
                                retailEnvironment.Environment.AmbientEncoded),
                            sunlightEncoded = Rgba(
                                retailEnvironment.Environment.SunlightEncoded),
                            fogEncoded = Rgba(retailEnvironment.Environment.FogEncoded),
                            retailEnvironment.AtmosphereSourceSha256,
                            retailEnvironment.CloudsSourceSha256,
                            cloudLayers = retailEnvironment.CloudLayers.Count,
                            retailEnvironment.WeatherRecordApplied,
                            retailEnvironment.ImageSpaceValidated,
                            retailEnvironment.AuxiliaryCloudSurfacesResolved,
                            retailEnvironment.CloudUvOffsetResolved,
                            retailEnvironment.DirectionalVectorResolved,
                            retailEnvironment.RetailRoadMaterials,
                            retailEnvironment.RetailRoadDiffuseCoreResolved,
                            retailEnvironment.RetailLandscapeMaterials,
                            retailEnvironment.RetailLandscapeDiffuseCoreResolved,
                            retailEnvironment.RetailGrassMaterials,
                            retailEnvironment.RetailGrassDiffuseCoreResolved,
                            retailEnvironment.RetailActorMaterials,
                            retailEnvironment.RetailActorDiffuseCoreResolved,
                        }
                        : null,
                },
                authoredMotion = new
                {
                    source = "owned-authored-kf",
                    targetFrames = configuration.Capture.Gallery.FramesPerSubject,
                    framesPerSecond = configuration.Capture.Gallery.FramesPerSecond,
                    targetDurationSeconds = configuration.Capture.Gallery.DurationSeconds,
                    minimumProgressFraction =
                        configuration.Capture.Gallery.MinimumMotionProgressFraction,
                    advancement = "configured-fixed-step-manual",
                    playbackCycle = "owned-authored-idle-loop",
                    retailPhaseAnimation = retailAnimation.File,
                    retailPhaseAnimationMatchedOwned = retailAnimationMatchesOwned,
                    runtimeLoopMode = animationResource.LoopMode.ToString(),
                    stepSeconds = animationStepSeconds,
                    minimumProgressSeconds = minimumAnimationProgressSeconds,
                    observedProgressSeconds = animationProgressSeconds,
                    progressingFrames,
                    passed = authoredMotionPassed,
                },
                visualParityGatePassed = frame.Passed,
                windowsAppControlUsed = false,
                foregroundActivationUsed = false,
                foregroundInputInjected = false,
                retailCaptureUsed = false,
                retailEvidenceUsed = true,
                parityClaimed = false,
                files = new[] { frame.Evidence },
            };
            WriteReport(
                Path.Combine(output, "gallery-capture-report.json"),
                captureReport);
            if (reportPath is not null)
                WriteReport(reportPath, captureReport);
            if (capturePassed)
                GD.Print(
                    $"OPENNV_GODOT_GALLERY_CAPTURE_PASS ordinal={shot.Ordinal} " +
                    $"id={shot.Id} actor={actor.Actor.Name} output={output}");
            else
                GD.PushError(
                    $"OPENNV_GODOT_GALLERY_CAPTURE_VISUAL_FAIL ordinal={shot.Ordinal} " +
                    $"id={shot.Id} output={output}");
            host.GetTree().Quit(capturePassed ? 0 : 1);
        }
        catch (Exception exception)
        {
            GD.PushError($"OPENNV_GODOT_GALLERY_CAPTURE_FAIL {exception.Message}");
            host.GetTree().Quit(1);
        }
    }

    private static GalleryRetailEvidence.AnimationSequence ResolvePresentationAnimation(
        GalleryRetailEvidence.PresentationReference presentation,
        string ownedAnimationPath)
    {
        var expected = NormalizeAnimationPath(ownedAnimationPath);
        var matches = presentation.Actor.AnimationSequences
            .Where(sequence => NormalizeAnimationPath(sequence.File).Equals(
                expected,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (matches.Length != 1)
        {
            if (matches.Length > 1)
                throw new InvalidOperationException(
                    $"Retail presentation resolves owned animation {ownedAnimationPath} " +
                    $"to {matches.Length} active sequences.");
            if (presentation.Actor.AnimationSequences.Count < 1)
                throw new InvalidOperationException(
                    "Retail presentation has no active sequence for authored phase replay.");
            return presentation.Actor.AnimationSequences[0];
        }
        return matches[0];
    }

    private static string NormalizeAnimationPath(string value)
    {
        var normalized = value.Replace('/', '\\').TrimStart('\\');
        const string meshesPrefix = "meshes\\";
        return normalized.StartsWith(meshesPrefix, StringComparison.OrdinalIgnoreCase)
            ? normalized[meshesPrefix.Length..]
            : normalized;
    }

    private static float[] Vector(Vector3 value) => new[] { value.X, value.Y, value.Z };

    private static float[] Vector(Vector2 value) => new[] { value.X, value.Y };

    private static float[] Rgba(Color value) =>
        new[] { value.R, value.G, value.B, value.A };

    private static float[] Quaternion(Quaternion value) =>
        new[] { value.X, value.Y, value.Z, value.W };

    private static string FileSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static void WriteReport(string reportPath, object report)
    {
        var fullReportPath = Path.GetFullPath(reportPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullReportPath)!);
        File.WriteAllText(
            fullReportPath,
            JsonSerializer.Serialize(
                report,
                new JsonSerializerOptions { WriteIndented = true }) +
            System.Environment.NewLine);
    }
}
