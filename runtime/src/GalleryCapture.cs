using System.Security.Cryptography;
using System.Text.Json;
using Godot;

namespace OpenNV.Runtime;

internal static class GalleryCapture
{
    private const int RetailCycleLoop = 0;
    private const int RetailCycleReverse = 1;
    private const int RetailCycleClamp = 2;

    internal static async Task Run(
        Node3D host,
        CellSceneLoader.LoadedCell loaded,
        RuntimeConfiguration configuration,
        string captureRoot,
        string scenePath,
        string? reportPath,
        string galleryShotPath)
    {
        GD.Print(
            $"OPENNV_GALLERY_STAGE id={Path.GetFileNameWithoutExtension(galleryShotPath)} " +
            "stage=gallery-capture-enter");
        try
        {
            var output = Path.GetFullPath(captureRoot);
            if (Directory.Exists(output) || File.Exists(output))
                throw new InvalidOperationException(
                    $"Refusing to overwrite gallery capture output: {output}");
            Directory.CreateDirectory(output);
            var shot = GalleryShotContract.Load(galleryShotPath, configuration);
            GD.Print($"OPENNV_GALLERY_STAGE id={shot.Id} stage=capture-start");
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
            RetailImageSpaceRenderer.Application? imageSpaceApplication = null;
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
                    shot.RetailEvidence.DirectionalLighting,
                    configuration);
                imageSpaceApplication = environmentApplication.Value.ImageSpace;
            }
            else
            {
                var worldEnvironment = host.GetChildren()
                    .OfType<WorldEnvironment>()
                    .SingleOrDefault()
                    ?? throw new InvalidOperationException(
                        "Interior gallery shot has no authored CELL environment.");
                if (worldEnvironment.Environment is null)
                    throw new InvalidOperationException(
                        "Interior gallery CELL environment has no Environment resource.");
                var capturedImageSpace =
                    RetailImageSpaceComposition.FromCapturedShader(
                        shot.RetailEvidence.ImageSpaceShader,
                        configuration.FalloutEnvironment.ImageSpace);
                imageSpaceApplication = RetailImageSpaceRenderer.Apply(
                    worldEnvironment,
                    capturedImageSpace,
                    configuration.FalloutEnvironment.ImageSpace,
                    configuration.Capture,
                    configuration.ActorCompiler.FaceGenMaterial.RuntimeAlbedoTransfer);
            }
            GD.Print($"OPENNV_GALLERY_STAGE id={shot.Id} stage=environment-complete");

            loaded.Player.ProcessMode = Node.ProcessModeEnum.Disabled;
            var hud = loaded.Session.GetNodeOrNull<CanvasLayer>("GameplayHud");
            if (hud is not null)
                hud.Visible = false;
            var retailAnimations = ResolvePresentationAnimations(
                shot.RetailEvidence.Presentation,
                actor.Actor.LoadedAnimations);
            var retailAnimation = retailAnimations[^1];
            var animation = retailAnimation.Owned.Player;
            if (retailAnimations.Any(value => value.Owned.Player != animation))
                throw new InvalidOperationException(
                    "Retail animation stack was imported into multiple animation players.");
            animation.CallbackModeProcess =
                AnimationMixer.AnimationCallbackModeProcess.Manual;
            var animationLengths = new List<double>(retailAnimations.Count);
            var animationResources = retailAnimations.Select(value =>
            {
                var resource = animation.GetAnimation(value.Owned.RuntimeName)
                    ?? throw new InvalidOperationException(
                        "Gallery actor has no loaded authored animation resource.");
                resource.LoopMode = ResolveLoopMode(value.Source.Cycle);
                animation.Play(value.Owned.RuntimeName);
                animation.Advance(0.0);
                var length = animation.CurrentAnimationLength;
                if (length <= 0.0 || resource.GetTrackCount() < 1 ||
                    value.Owned.Channels < 1)
                    throw new InvalidOperationException(
                        $"Gallery actor animation {value.Source.File} is incomplete.");
                if (value.Source.Weight != 1.0f)
                    throw new InvalidOperationException(
                        "Retail animation-stack replay requires a full-weight sequence; " +
                        $"{value.Source.File} has weight {value.Source.Weight}.");
                animationLengths.Add(length);
                return resource;
            }).ToArray();
            var animationResource = animationResources[^1];
            var animationLength = animationLengths[^1];
            var animationTrackCount = animationResource.GetTrackCount();
            var retailAnimationMatchesOwned = true;
            var presentationAnimationPosition =
                ResolveAnimationPosition(retailAnimation.Source, animationLength);
            ApplyAnimationStack(
                animation,
                retailAnimations,
                animationLengths,
                0.0);
            GD.Print(
                $"OPENNV_GALLERY_STAGE id={shot.Id} stage=animation-stack-applied " +
                $"sequences={retailAnimations.Count}");
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            var placementReplay = GalleryActorPlacement.Apply(
                loaded,
                actor,
                shot.RetailEvidence.Presentation,
                configuration);
            var presentationFacingCorrection =
                GalleryActorPlacement.ApplyPresentationFacingCorrection(
                    actor,
                    configuration.Capture.Gallery.ModelFrontAxis);
            var camera = loaded.Player.Camera;
            // The scene loader publishes authored collision through the physics
            // server.  Wait for that publication boundary before asking the
            // direct space state for floor support; render frames alone do not
            // guarantee that a newly loaded static body is queryable.
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.PhysicsFrame);
            var alignmentBounds = ActorModelSlice.PosedWorldBounds(
                actor.Actor,
                includeWeapons: false);
            var groundAlignment = GalleryGroundContact.Align(
                camera.GetWorld3D().DirectSpaceState,
                actor,
                alignmentBounds,
                configuration,
                loaded.Player.CollisionMask,
                loaded.Root.GlobalPosition);
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            var actorBounds = ActorModelSlice.PosedWorldBounds(
                actor.Actor,
                includeWeapons: true);
            var contactBounds = ActorModelSlice.PosedWorldBounds(
                actor.Actor,
                includeWeapons: false);
            var groundContact = GalleryGroundContact.Measure(
                camera.GetWorld3D().DirectSpaceState,
                actor,
                contactBounds,
                configuration,
                loaded.Player.CollisionMask,
                groundAlignment.Support);
            var framing = GalleryFraming.Apply(
                camera,
                actor,
                actorBounds,
                configuration,
                loaded.Player.CollisionMask,
                shot.RetailEvidence.Presentation,
                placementReplay.MeasuredWorldPositionMeters);
            GD.Print($"OPENNV_GALLERY_STAGE id={shot.Id} stage=ground-and-framing-complete");
            var lodLedger = CellLodLedger.Measure(loaded.MainContent, camera);
            var rigidSurfaceDiagnostics = actor.Actor.Surfaces
                .Where(surface => !surface.Skinned)
                .Select(surface =>
                {
                    var bounds = ActorModelSlice.PosedWorldBounds(
                        actor.Actor,
                        surface);
                    var parent = surface.Mesh.GetParent();
                    return new
                    {
                        surface.Role,
                        surface.Shape,
                        surface.RuntimeNodeName,
                        surface.AttachmentNode,
                        surface.RetailGeometryName,
                        surface.RetailVisualNodePath,
                        parentName = parent?.Name.ToString(),
                        parentType = parent?.GetClass(),
                        globalTransform = Transform(surface.Mesh.GlobalTransform),
                        boundsMinimum = Vector(bounds.Position),
                        boundsSize = Vector(bounds.Size),
                        boundsCenter = Vector(bounds.GetCenter()),
                    };
                })
                .ToArray();
            await EnvironmentCapture.WaitForRenderedFrames(
                host,
                configuration.Capture.ActorRenderedFramesBeforeCapture);
            GD.Print($"OPENNV_GALLERY_STAGE id={shot.Id} stage=render-settled");
            var appliedImageSpace = imageSpaceApplication
                ?? throw new InvalidOperationException(
                    "Gallery capture has no applied retail image-space compositor.");
            var imageSpaceStem = Path.GetFileNameWithoutExtension(shot.OutputFile);
            var preHdrBytes = appliedImageSpace.Effect.CapturePreHdrSceneColor();
            var preHdrPath = Path.Combine(
                output,
                $"{imageSpaceStem}-pre-hdr-rgba16f.bin");
            File.WriteAllBytes(preHdrPath, preHdrBytes);
            var preHdrEvidence = new
            {
                path = preHdrPath,
                bytes = preHdrBytes.Length,
                sha256 = Convert.ToHexString(SHA256.HashData(preHdrBytes))
                    .ToLowerInvariant(),
                width = configuration.Capture.ExpectedWidthPixels,
                height = configuration.Capture.ExpectedHeightPixels,
                format = "R16G16B16A16_SFLOAT-little-endian",
                boundary = "retail-stage-1-hdr-scene",
            };
            var postHdrBytes = appliedImageSpace.Effect.CapturePostHdrSceneColor();
            var postHdrPath = Path.Combine(
                output,
                $"{imageSpaceStem}-post-hdr-rgba16f.bin");
            File.WriteAllBytes(postHdrPath, postHdrBytes);
            var postHdrEvidence = new
            {
                path = postHdrPath,
                bytes = postHdrBytes.Length,
                sha256 = Convert.ToHexString(SHA256.HashData(postHdrBytes))
                    .ToLowerInvariant(),
                width = configuration.Capture.ExpectedWidthPixels,
                height = configuration.Capture.ExpectedHeightPixels,
                format = "R16G16B16A16_SFLOAT-little-endian",
                boundary = "final-image-space-compositor-output",
            };
            var frame = EnvironmentCapture.SaveViewportPng(
                host,
                output,
                shot.OutputFile,
                configuration.Capture.ActorMinimumMeanLuminance,
                configuration.Capture);
            GD.Print($"OPENNV_GALLERY_STAGE id={shot.Id} stage=still-captured");
            var animationPositionBefore = animation.CurrentAnimationPosition;
            var previousAnimationPosition = animationPositionBefore;
            var animationProgressSeconds = 0.0;
            var progressingFrames = 0;
            var animationStepSeconds =
                1.0 / configuration.Capture.Gallery.FramesPerSecond;
            var groundContactSamples = 1;
            var groundContactFailures = groundContact.Passed ? 0 : 1;
            var alignedRootPosition = actor.Placement.GlobalPosition;
            var maximumRootDriftGameUnits = 0.0f;
            var maximumGroundContactErrorGameUnits = MathF.Abs(
                groundContact.DeltaGameUnits ?? float.PositiveInfinity);
            for (var frameIndex = 0;
                 frameIndex < configuration.Capture.Gallery.FramesPerSubject;
                 frameIndex++)
            {
                ApplyAnimationStack(
                    animation,
                    retailAnimations,
                    animationLengths,
                    (frameIndex + 1) * animationStepSeconds);
                await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
                var motionBounds = ActorModelSlice.PosedWorldBounds(
                    actor.Actor,
                    includeWeapons: false);
                var motionGroundContact = GalleryGroundContact.Measure(
                    camera.GetWorld3D().DirectSpaceState,
                    actor,
                    motionBounds,
                    configuration,
                    loaded.Player.CollisionMask,
                    groundAlignment.Support);
                groundContactSamples++;
                var rootDriftGameUnits = actor.Placement.GlobalPosition.DistanceTo(
                        alignedRootPosition) /
                    configuration.World.GameUnitsToMeters;
                maximumRootDriftGameUnits = MathF.Max(
                    maximumRootDriftGameUnits,
                    rootDriftGameUnits);
                if (rootDriftGameUnits >
                    configuration.ActorParity.PlacementToleranceGameUnits)
                    groundContactFailures++;
                maximumGroundContactErrorGameUnits = MathF.Max(
                    maximumGroundContactErrorGameUnits,
                    MathF.Abs(
                        motionGroundContact.DeltaGameUnits ??
                        float.PositiveInfinity));
                var currentAnimationPosition = animation.CurrentAnimationPosition;
                var delta = currentAnimationPosition - previousAnimationPosition;
                if (retailAnimation.Source.Cycle == RetailCycleLoop && delta < 0.0)
                    delta += animationLength;
                else if (retailAnimation.Source.Cycle == RetailCycleReverse)
                    delta = Math.Abs(delta);
                if (delta > 0.0)
                    progressingFrames++;
                animationProgressSeconds += Math.Max(delta, 0.0);
                previousAnimationPosition = currentAnimationPosition;
            }
            var animationPositionAfter = animation.CurrentAnimationPosition;
            GD.Print($"OPENNV_GALLERY_STAGE id={shot.Id} stage=motion-complete");
            var minimumAnimationProgressSeconds =
                MinimumAnimationProgress(
                    retailAnimation.Source,
                    animationLength,
                    presentationAnimationPosition,
                    configuration.Capture.Gallery.DurationSeconds,
                    configuration.Capture.Gallery.MinimumMotionProgressFraction);
            var authoredMotionPassed =
                animationProgressSeconds >= minimumAnimationProgressSeconds &&
                progressingFrames > 0;
            var groundContactPassed = groundContactFailures == 0;
            var environmentPassed =
                imageSpaceApplication is { } imageSpace &&
                imageSpace.FinalCinematicStageResolved &&
                imageSpace.HdrAdaptationBrightPassBloomResolved &&
                (shot.LocationClass != "exterior" ||
                    environmentApplication is { } applied &&
                    applied.WeatherRecordApplied &&
                    applied.ImageSpaceValidated &&
                    applied.RetailRoadDiffuseCoreResolved &&
                    applied.RetailLandscapeDiffuseCoreResolved &&
                    applied.RetailGrassDiffuseCoreResolved &&
                    applied.RetailActorDiffuseCoreResolved);
            var capturePassed =
                authoredMotionPassed &&
                groundContactPassed &&
                lodLedger.Passed &&
                framing.HeadVisibilityClear &&
                environmentPassed;
            var captureReport = new
            {
                schema = "opennv-godot-gallery-capture/v5",
                status = capturePassed
                    ? "captured-gallery-retail-bound-pending-parity"
                    : !groundContactPassed
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
                            animation = retailAnimation.Source.File,
                            ownedAnimationMatched = retailAnimationMatchesOwned,
                            animationPositionSeconds = presentationAnimationPosition,
                            animationSelection =
                                "ordered-full-weight-retail-active-sequence-stack",
                            animationStack = shot.RetailEvidence.Presentation.Actor
                                .AnimationSequences.Select(sequence => new
                                {
                                    sequence.File,
                                    sequence.State,
                                    sequence.Cycle,
                                    sequence.Weight,
                                    sequence.Frequency,
                                    sequence.LastScaledSeconds,
                                    sequence.Group,
                                }).ToArray(),
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
                    presentationFacingCorrection = new
                    {
                        facingPoseRotationQuaternion = Quaternion(
                            presentationFacingCorrection.FacingPoseRotation),
                        retailRootWorldFront = Vector(
                            presentationFacingCorrection.RetailRootWorldFront),
                        posedWorldFrontBeforeCorrection = Vector(
                            presentationFacingCorrection
                                .PosedWorldFrontBeforeCorrection),
                        posedWorldFrontAfterCorrection = Vector(
                            presentationFacingCorrection
                                .PosedWorldFrontAfterCorrection),
                        presentationFacingCorrection.PosedYawRadians,
                        presentationFacingCorrection.AppliedYawRadians,
                        presentationFacingCorrection.ResidualYawRadians,
                        visualRootBefore = Transform(
                            presentationFacingCorrection.VisualRootBefore),
                        visualRootAfter = Transform(
                            presentationFacingCorrection.VisualRootAfter),
                        presentationFacingCorrection.Derivation,
                    },
                    groundAlignment = new
                    {
                        rootBefore = Vector(groundAlignment.RootBefore),
                        rootAfter = Vector(groundAlignment.RootAfter),
                        groundAlignment.CorrectionMeters,
                        groundAlignment.CorrectionGameUnits,
                        groundPosition = Vector(groundAlignment.GroundPosition),
                        groundAlignment.ColliderPath,
                        groundAlignment.Derivation,
                    },
                    boundsMinimum = Vector(framing.Bounds.Position),
                    boundsSize = Vector(framing.Bounds.Size),
                    heightMeters = framing.Bounds.Size.Y,
                    rigidSurfaces = rigidSurfaceDiagnostics,
                    animation = retailAnimation.Owned.RuntimeName.ToString(),
                    idleAnimationPath = actor.IdleAnimationPath,
                    animationSourceLogicalPath = retailAnimation.Owned.LogicalPath,
                    animationSourceSha256 = retailAnimation.Owned.SourceSha256,
                    animationSourceChannels = retailAnimation.Owned.Channels,
                    loadedAnimations = actor.Actor.LoadedAnimations.Select(value => new
                    {
                        value.LogicalPath,
                        value.SourceSha256,
                        value.Channels,
                        runtimeName = value.RuntimeName.ToString(),
                    }).ToArray(),
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
                        allFramesPassed = groundContactPassed,
                        sampledFrames = groundContactSamples,
                        failedFrames = groundContactFailures,
                        maximumAbsoluteDeltaGameUnits =
                            maximumGroundContactErrorGameUnits,
                        maximumRootDriftGameUnits,
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
                        : "authored-interior-cell-plus-retail-captured-image-space",
                    passed = environmentPassed,
                    capturedCurrentWeatherForm =
                        shot.RetailEvidence.Environment?.WeatherForm,
                    capturedDefaultWeatherForm =
                        shot.RetailEvidence.Environment?.DefaultWeatherForm,
                    effectiveWeatherForm = shot.RetailEvidence.EffectiveWeatherForm,
                    capturedGameHour = shot.RetailEvidence.Environment?.GameHour,
                    capturedImageSpaceForm =
                        shot.RetailEvidence.Environment?.ImageSpace.FormId,
                    capturedImageSpaceShaderEventSha256 =
                        shot.RetailEvidence.ImageSpaceShader.EventSha256,
                    imageSpace = imageSpaceApplication is { } retailImageSpace
                        ? new
                        {
                            retailImageSpace.Schema,
                            cinematic = Vector4(retailImageSpace.Cinematic),
                            tint = Vector4(retailImageSpace.Tint),
                            fade = Vector4(retailImageSpace.Fade),
                            retailImageSpace.MatchedAdaptationSum,
                            retailImageSpace.MatchedAdaptationSourceSha256,
                            retailImageSpace.FinalCinematicStageResolved,
                            retailImageSpace.HdrAdaptationBrightPassBloomResolved,
                            buffers = new
                            {
                                preHdrSceneColor = preHdrEvidence,
                                postHdrSceneColor = postHdrEvidence,
                            },
                        }
                        : null,
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
                            retailEnvironment.DirectionalShadowsEnabled,
                            directionalLighting =
                                retailEnvironment.DirectionalLighting is { } lighting
                                    ? new
                                    {
                                        lighting.Source,
                                        lighting.SourceFrame,
                                        lighting.RenderFrame,
                                        lighting.RecordCount,
                                        vertexShaderFnv1a32 =
                                            $"0x{lighting.VertexShaderFnv1a32:X8}",
                                        pixelShaderFnv1a32 =
                                            $"0x{lighting.PixelShaderFnv1a32:X8}",
                                        diffuseDirectionGamebryo = Vector(
                                            lighting.DiffuseDirectionGamebryo),
                                        surfaceToLightGodot = Vector(
                                            lighting.SurfaceToLightGodot),
                                        diffuseColorEncoded = Rgba(
                                            lighting.DiffuseColorEncoded),
                                        ambientColorEncoded = Rgba(
                                            lighting.AmbientColorEncoded),
                                        lighting.DirectionalScale,
                                        rotationDegrees = Vector(
                                            retailEnvironment.DirectionalRotationDegrees),
                                    }
                                    : null,
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
                    playbackCycle = animationResource.LoopMode.ToString(),
                    retailCycle = retailAnimation.Source.Cycle,
                    retailFrequency = retailAnimation.Source.Frequency,
                    retailPhaseAnimation = retailAnimation.Source.File,
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

    private static IReadOnlyList<ResolvedAnimation> ResolvePresentationAnimations(
        GalleryRetailEvidence.PresentationReference presentation,
        IReadOnlyList<ActorModelSlice.LoadedAnimation> ownedAnimations)
    {
        if (presentation.Actor.AnimationSequences.Count < 1)
            throw new InvalidOperationException(
                "Retail presentation has no active sequence for authored phase replay.");
        var resolved = presentation.Actor.AnimationSequences.Select(sequence =>
        {
            var expected = ActorModelSlice.NormalizeAnimationPath(sequence.File);
            var matches = ownedAnimations.Where(animation =>
                ActorModelSlice.NormalizeAnimationPath(animation.LogicalPath).Equals(
                    expected,
                    StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length != 1)
                throw new InvalidOperationException(
                    $"Retail animation {sequence.File} maps to {matches.Length} " +
                    "owned runtime animations.");
            return new ResolvedAnimation(sequence, matches[0]);
        }).ToArray();
        if (resolved.Length != ownedAnimations.Count)
            throw new InvalidOperationException(
                "Owned gallery actor animations differ from the retail active stack.");
        return resolved;
    }

    private static void ApplyAnimationStack(
        AnimationPlayer player,
        IReadOnlyList<ResolvedAnimation> stack,
        IReadOnlyList<double> animationLengths,
        double elapsedSeconds)
    {
        if (stack.Count != animationLengths.Count)
            throw new InvalidOperationException(
                "Retail animation stack and length ledger differ.");
        for (var index = 0; index < stack.Count; index++)
        {
            var sequence = stack[index];
            player.Play(sequence.Owned.RuntimeName);
            player.Seek(
                ResolveAnimationPosition(
                    sequence.Source,
                    animationLengths[index],
                    elapsedSeconds),
                true);
            player.Advance(0.0);
        }
    }

    private static Animation.LoopModeEnum ResolveLoopMode(int retailCycle)
    {
        return retailCycle switch
        {
            RetailCycleLoop => Animation.LoopModeEnum.Linear,
            RetailCycleReverse => Animation.LoopModeEnum.Pingpong,
            RetailCycleClamp => Animation.LoopModeEnum.None,
            _ => throw new InvalidOperationException(
                $"Retail animation cycle {retailCycle} is unsupported."),
        };
    }

    private static float ResolveAnimationPosition(
        GalleryRetailEvidence.AnimationSequence sequence,
        double animationLength,
        double elapsedSeconds = 0.0)
    {
        var scaledSeconds = sequence.LastScaledSeconds +
            elapsedSeconds * sequence.Frequency;
        if (sequence.Cycle == RetailCycleClamp)
            return (float)Math.Clamp(scaledSeconds, 0.0, animationLength);
        if (sequence.Cycle == RetailCycleReverse)
        {
            var period = animationLength * 2.0;
            var position = scaledSeconds % period;
            if (position < 0.0)
                position += period;
            return (float)(position <= animationLength
                ? position
                : period - position);
        }
        var loopPosition = scaledSeconds % animationLength;
        return (float)(loopPosition < 0.0
            ? loopPosition + animationLength
            : loopPosition);
    }

    private static double MinimumAnimationProgress(
        GalleryRetailEvidence.AnimationSequence sequence,
        double animationLength,
        float startPosition,
        double durationSeconds,
        double minimumProgressFraction)
    {
        var requested = durationSeconds * sequence.Frequency *
            minimumProgressFraction;
        return sequence.Cycle == RetailCycleClamp
            ? Math.Min(requested, Math.Max(animationLength - startPosition, 0.0))
            : requested;
    }

    private readonly record struct ResolvedAnimation(
        GalleryRetailEvidence.AnimationSequence Source,
        ActorModelSlice.LoadedAnimation Owned);

    private static float[] Vector(Vector3 value) => new[] { value.X, value.Y, value.Z };

    private static float[] Vector(Vector2 value) => new[] { value.X, value.Y };

    private static float[] Rgba(Color value) =>
        new[] { value.R, value.G, value.B, value.A };

    private static float[] Vector4(Vector4 value) =>
        new[] { value.X, value.Y, value.Z, value.W };

    private static float[] Quaternion(Quaternion value) =>
        new[] { value.X, value.Y, value.Z, value.W };

    private static object Transform(Transform3D value) => new
    {
        origin = Vector(value.Origin),
        basisX = Vector(value.Basis.X),
        basisY = Vector(value.Basis.Y),
        basisZ = Vector(value.Basis.Z),
    };

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
