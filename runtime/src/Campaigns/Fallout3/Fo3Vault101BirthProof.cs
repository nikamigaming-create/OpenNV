using System.Security.Cryptography;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using Godot;

namespace OpenNV.Runtime.Campaigns.Fallout3;

internal partial class Fo3Vault101BirthProof : Node3D
{
    private const int WarmupFrames = 8;
    private const int ActorProofWarmupFrames = 4;
    private const double MinimumLuminanceDeviation = 0.005;
    private const int MinimumNonBackgroundPixels = 1000;
    private const float BackgroundPixelDeltaSquared = 16.0f;

    public override async void _Ready()
    {
        try
        {
            var profilePath = RequiredOption("--fo3-profile");
            var presentationPath = RequiredOption("--fo3-birth-presentation");
            var output = Path.GetFullPath(RequiredOption("--fo3-birth-capture"));
            if (Directory.Exists(output) || File.Exists(output))
                throw new InvalidOperationException(
                    $"Refusing to overwrite Fallout 3 Vault 101 proof: {output}");
            Directory.CreateDirectory(output);
            if (DisplayServer.GetName() == "headless")
                throw new InvalidOperationException(
                    "Fallout 3 Vault 101 render proof requires a rendering display driver.");
            var runtimeAssemblyPath = Path.GetFullPath(
                RequiredOption("--fo3-runtime-assembly"));
            var expectedRuntimeAssemblySha256 =
                RequiredOption("--fo3-runtime-assembly-sha256");
            using var runtimeAssemblyStream = File.OpenRead(runtimeAssemblyPath);
            var runtimeAssemblySha256 = Convert.ToHexString(
                SHA256.HashData(runtimeAssemblyStream)).ToLowerInvariant();
            if (!runtimeAssemblySha256.Equals(
                    expectedRuntimeAssemblySha256,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "Fallout 3 proof runtime assembly hash differs from its launch receipt.");
            using var runtimePeStream = File.OpenRead(runtimeAssemblyPath);
            using var runtimePe = new PEReader(runtimePeStream);
            var metadata = runtimePe.GetMetadataReader();
            var declaredMvid = metadata.GetGuid(metadata.GetModuleDefinition().Mvid);
            var loadedMvid = typeof(Fo3Vault101BirthProof).Assembly.ManifestModule.ModuleVersionId;
            if (declaredMvid != loadedMvid)
                throw new InvalidOperationException(
                    "Fallout 3 proof loaded assembly differs from the current-source build.");

            var profile = Fo3OwnedProfile.Load(profilePath);
            var contract = Fo3Vault101BirthPresentationContract.Load(
                profile.BirthSlice,
                profile.Cg01Stage0Transition,
                profile.Stage65Appearance,
                profile.Cg01Stage10Transition,
                presentationPath);
            var handoff = profile.Section4Transition;
            if (handoff.SourceStage != profile.Appearance.AcceptedStage ||
                !handoff.LocationReferenceFormId.Equals(
                    contract.EntryReferenceFormId,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "Fallout 3 stage-62 package location does not join the owned player marker.");
            var proofSex = profile.SexChoices.Single(value => value.EngineSex == "male");
            var proofSelection = profile.Appearance.DefaultSelection(proofSex.EngineSex);
            var proofStage65 = profile.Stage65Appearance.Apply(
                proofSex.EngineSex,
                proofSelection.Race.FormId,
                proofSelection.Sex.FaceGen);
            var coverage = Fo3Vault101BirthScene.Build(
                this,
                contract,
                contract.Cg01DadActorFor(
                    proofSelection.Race.FormId,
                    proofSex.EngineSex,
                    proofStage65));
            for (var frame = 0; frame < WarmupFrames; frame++)
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

            var framePath = Path.Combine(output, "vault101-birth-entry.png");
            var image = GetViewport().GetTexture().GetImage();
            image.Convert(Image.Format.Rgba8);
            var metrics = Analyze(image, contract.ProofBackgroundColor);
            var saveError = image.SavePng(framePath);
            if (saveError != Error.Ok)
                throw new InvalidOperationException(
                    $"Could not save Fallout 3 Vault 101 render frame: {saveError}");
            using var frameStream = File.OpenRead(framePath);
            var frameSha256 = Convert.ToHexString(SHA256.HashData(frameStream)).ToLowerInvariant();
            var roomFailure = metrics.LuminanceDeviation < MinimumLuminanceDeviation
                ? "luminance-deviation"
                : metrics.NonBackgroundPixels < MinimumNonBackgroundPixels
                    ? "owned-geometry-not-visible"
                    : null;
            var dialogue = profile.Stage80Transition.DialogueFor("male");
            var dialogueStream = AudioStreamOggVorbis.LoadFromFile(
                    dialogue.Response.Voice.SourcePath)
                ?? throw new InvalidOperationException(
                    "Fallout 3 owned Dad dialogue voice could not be decoded.");
            var dialogueDurationSeconds = dialogueStream.GetLength();
            if (!double.IsFinite(dialogueDurationSeconds) || dialogueDurationSeconds <= 0.0)
                throw new InvalidOperationException(
                    "Fallout 3 owned Dad dialogue voice has no duration.");
            var dialoguePlayer = new AudioStreamPlayer
            {
                Name = "FO3_CG00_OWNED_DAD_DIALOGUE_PROOF",
                Stream = dialogueStream,
            };
            AddChild(dialoguePlayer);
            var dialogueOverlay = BuildDialogueOverlay(dialogue.Response.Text);
            AddChild(dialogueOverlay);
            dialoguePlayer.Play();
            for (var dialogueFrame = 0; dialogueFrame < ActorProofWarmupFrames; dialogueFrame++)
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            var dialoguePlaybackStarted = dialoguePlayer.Playing;
            var dialogueFramePath = Path.Combine(output, "stage65-owned-dad-cue.png");
            var dialogueImage = GetViewport().GetTexture().GetImage();
            dialogueImage.Convert(Image.Format.Rgba8);
            var dialogueSaveError = dialogueImage.SavePng(dialogueFramePath);
            if (dialogueSaveError != Error.Ok)
                throw new InvalidOperationException(
                    $"Could not save Fallout 3 Dad dialogue frame: {dialogueSaveError}");
            using var dialogueFrameStream = File.OpenRead(dialogueFramePath);
            var dialogueFrameSha256 = Convert.ToHexString(
                SHA256.HashData(dialogueFrameStream)).ToLowerInvariant();
            dialoguePlayer.Stop();
            dialoguePlayer.QueueFree();
            dialogueOverlay.QueueFree();
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            var proofCameraPosition = coverage.Camera.GlobalPosition;
            var proofCameraFov = coverage.Camera.Fov;
            foreach (var child in coverage.CellRoot.GetChildren().OfType<Node3D>()
                         .Where(child => child.Name.ToString().StartsWith(
                             "REFR_", StringComparison.Ordinal)))
                child.Visible = false;
            coverage.DadActor.Actor.Root.Visible = false;
            var actorTarget = ActorModelSlice.PosedSemanticCenter(
                    coverage.DoctorActor.Actor,
                    "head",
                    "eye-left",
                    "eye-right",
                    "hair")
                ?? throw new InvalidOperationException(
                    "Fallout 3 Doctor Li actor has no owned head target.");
            coverage.Camera.GlobalPosition = actorTarget + new Vector3(0.0f, 0.0f, -1.5f);
            coverage.Camera.LookAt(actorTarget, Vector3.Up);
            coverage.Camera.Fov = 45.0f;
            coverage.Camera.AddChild(new DirectionalLight3D
            {
                Name = "DOCTOR_LI_PROOF_CAMERA_FILL",
                LightColor = Colors.White,
                LightEnergy = 1.5f,
                ShadowEnabled = false,
            });
            for (var actorFrame = 0; actorFrame < ActorProofWarmupFrames; actorFrame++)
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            var actorProofGeometry = CellReferenceLedger.MeasureGeometry(
                coverage.DoctorActor.Actor.Root,
                coverage.Camera,
                coverage.DoctorGrounding.GroundedBounds.GetCenter());
            var actorFramePath = Path.Combine(output, "doctor-li-owned-actor.png");
            var actorImage = GetViewport().GetTexture().GetImage();
            actorImage.Convert(Image.Format.Rgba8);
            var actorMetrics = Analyze(actorImage, contract.ProofBackgroundColor);
            var actorSaveError = actorImage.SavePng(actorFramePath);
            if (actorSaveError != Error.Ok)
                throw new InvalidOperationException(
                    $"Could not save Fallout 3 Doctor Li render frame: {actorSaveError}");
            using var actorFrameStream = File.OpenRead(actorFramePath);
            var actorFrameSha256 = Convert.ToHexString(
                SHA256.HashData(actorFrameStream)).ToLowerInvariant();
            var doctorProofCameraPosition = coverage.Camera.GlobalPosition;
            var actorFailure = !actorProofGeometry.RenderLayerVisible ||
                !actorProofGeometry.AabbValid ||
                !actorProofGeometry.FrustumIntersection ||
                actorProofGeometry.ProjectedScreenBounds is not Vector4 projectedBounds ||
                !CellReferenceLedger.ProjectedBoundsIntersectsViewport(
                    projectedBounds,
                    coverage.Camera)
                    ? "doctor-li-not-visible-in-actor-proof"
                    : actorMetrics.LuminanceDeviation < MinimumLuminanceDeviation
                        ? "doctor-li-luminance-deviation"
                        : actorMetrics.NonBackgroundPixels < MinimumNonBackgroundPixels
                            ? "doctor-li-pixels-not-visible"
                            : null;
            coverage.DoctorActor.Actor.Root.Visible = false;
            coverage.DadActor.Actor.Root.Visible = true;
            var dadTarget = ActorModelSlice.PosedSemanticCenter(
                    coverage.DadActor.Actor,
                    "head",
                    "eye-left",
                    "eye-right",
                    "hair")
                ?? throw new InvalidOperationException(
                    "Fallout 3 CG00 Dad actor has no owned head target.");
            coverage.Camera.GlobalPosition = dadTarget + new Vector3(0.0f, 0.0f, -1.5f);
            coverage.Camera.LookAt(dadTarget, Vector3.Up);
            for (var dadFrame = 0; dadFrame < ActorProofWarmupFrames; dadFrame++)
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            var dadProofGeometry = CellReferenceLedger.MeasureGeometry(
                coverage.DadActor.Actor.Root,
                coverage.Camera,
                coverage.DadGrounding.GroundedBounds.GetCenter());
            var dadFramePath = Path.Combine(output, "cg00-dad-owned-actor.png");
            var dadImage = GetViewport().GetTexture().GetImage();
            dadImage.Convert(Image.Format.Rgba8);
            var dadMetrics = Analyze(dadImage, contract.ProofBackgroundColor);
            var dadSaveError = dadImage.SavePng(dadFramePath);
            if (dadSaveError != Error.Ok)
                throw new InvalidOperationException(
                    $"Could not save Fallout 3 CG00 Dad render frame: {dadSaveError}");
            using var dadFrameStream = File.OpenRead(dadFramePath);
            var dadFrameSha256 = Convert.ToHexString(
                SHA256.HashData(dadFrameStream)).ToLowerInvariant();
            var dadProofCameraPosition = coverage.Camera.GlobalPosition;
            var dadFailure = !dadProofGeometry.RenderLayerVisible ||
                !dadProofGeometry.AabbValid ||
                !dadProofGeometry.FrustumIntersection ||
                dadProofGeometry.ProjectedScreenBounds is not Vector4 dadProjectedBounds ||
                !CellReferenceLedger.ProjectedBoundsIntersectsViewport(
                    dadProjectedBounds,
                    coverage.Camera)
                    ? "cg00-dad-not-visible-in-actor-proof"
                    : dadMetrics.LuminanceDeviation < MinimumLuminanceDeviation
                        ? "cg00-dad-luminance-deviation"
                        : dadMetrics.NonBackgroundPixels < MinimumNonBackgroundPixels
                            ? "cg00-dad-pixels-not-visible"
                            : null;
            var failure = roomFailure ?? actorFailure ?? dadFailure;
            var report = new
            {
                schema = "opennv-fo3-vault101-birth-native-render-proof/v6",
                status = failure is null
                    ? "pass-rendered-owned-birth-room-doctor-li-cg00-dad-and-dialogue-cue"
                    : "fail-rendered-owned-birth-room",
                campaign = "Fallout3",
                slice = "Vault101BirthRoom",
                renderer = RenderingServer.GetCurrentRenderingMethod(),
                displayDriver = DisplayServer.GetName(),
                source = new
                {
                    runtimeAssembly = new
                    {
                        path = runtimeAssemblyPath,
                        bytes = runtimeAssemblyStream.Length,
                        sha256 = runtimeAssemblySha256,
                        moduleVersionId = loadedMvid,
                    },
                    profileId = profile.ProfileId,
                    profileSha256 = profile.Sha256,
                    birthSlice = profile.BirthSlice.Path,
                    birthSliceSha256 = profile.BirthSlice.Sha256,
                    presentationManifest = contract.ManifestPath,
                    presentationManifestSha256 = contract.ManifestSha256,
                    recipeId = contract.RecipeId,
                    recipeSha256 = contract.RecipeSha256,
                    doctorActorScene = contract.DoctorActor.ScenePath,
                    doctorActorSceneSha256 = contract.DoctorActor.SceneSha256,
                    dadActorScene = contract.DadActor.ScenePath,
                    dadActorSceneSha256 = contract.DadActor.SceneSha256,
                },
                cell = new
                {
                    formId = contract.CellFormId,
                    editorId = contract.CellEditorId,
                    sourceReferences = profile.BirthSlice.ReferenceCount,
                    loadedStaticReferences = coverage.PlacedReferences,
                    loadedUniqueModels = coverage.LoadedAssets,
                },
                entry = new
                {
                    authority = "exact owned CG00PlayerStartMarker transform",
                    referenceFormId = contract.EntryReferenceFormId,
                    positionGameUnits = Vector(contract.EntryPositionGameUnits),
                    rotationRadians = Vector(contract.EntryRotationRadians),
                    positionGodotMeters = Vector(
                        coverage.CellRoot.ToGlobal(Vector3.Zero)),
                },
                proofCamera = new
                {
                    authority = contract.ProofCameraAuthority,
                    supportReferenceFormId = contract.ProofCameraSupportReferenceFormId,
                    supportBaseEditorId = contract.ProofCameraSupportBaseEditorId,
                    supportAssetId = contract.ProofCameraSupportAssetId,
                    supportSurfaceGodotGameUnits =
                        contract.ProofCameraSupportSurfaceGodotGameUnits,
                    surfaceClearanceGameUnits =
                        contract.ProofCameraSurfaceClearanceGameUnits,
                    nearGameUnits = contract.ProofCameraNearGameUnits,
                    positionGameUnits = Vector(contract.ProofCameraPositionGameUnits),
                    positionGodotGameUnits = Vector(
                        contract.ProofCameraPositionGodotGameUnits),
                    positionGodotMeters = Vector(proofCameraPosition),
                    rotationAuthority = "exact owned entry-marker rotation",
                    projection = "recipe-proof-only-not-retail-parity",
                    verticalFovDegrees = proofCameraFov,
                },
                characterSelectionHandoff = new
                {
                    authority =
                        "owned stage-62 command/package location joined to owned player marker",
                    sourceStage = handoff.SourceStage,
                    acceptedStageCommand = handoff.Command,
                    packageFormId = handoff.PackageFormId,
                    packageEditorId = handoff.PackageEditorId,
                    packageLocationReferenceFormId = handoff.LocationReferenceFormId,
                    entryReferenceFormId = contract.EntryReferenceFormId,
                    boundedPresentationOnly = true,
                    packageExecuted = false,
                    playerIdleExecuted = false,
                    dialoguePlayback = false,
                    retailTimingApplied = false,
                },
                boundedDialogueCue = new
                {
                    authority =
                        "owned stage-65 INFO response text and exact owned voice/LIP members",
                    sourceStage = profile.Stage80Transition.SourceStage,
                    targetStage = profile.Stage80Transition.Stage,
                    engineSex = dialogue.EngineSex,
                    infoFormId = dialogue.InfoFormId,
                    responseIndex = dialogue.Response.Index,
                    subtitleSha256 = Convert.ToHexString(SHA256.HashData(
                        System.Text.Encoding.UTF8.GetBytes(dialogue.Response.Text)))
                        .ToLowerInvariant(),
                    voiceLogicalPath = dialogue.Response.Voice.LogicalPath,
                    voiceSha256 = dialogue.Response.Voice.Sha256,
                    lipLogicalPath = dialogue.Response.Lip.LogicalPath,
                    lipSha256 = dialogue.Response.Lip.Sha256,
                    durationSeconds = dialogueDurationSeconds,
                    explicitAdvanceRequired = true,
                    audioPlaybackStarted = dialoguePlaybackStarted,
                    subtitleRendered = true,
                    lipPlayback = false,
                    dadRendered = true,
                    retailTimingApplied = false,
                    stage80Applied = false,
                    frame = new
                    {
                        path = dialogueFramePath,
                        bytes = dialogueFrameStream.Length,
                        sha256 = dialogueFrameSha256,
                    },
                },
                geometry = new
                {
                    meshInstances = coverage.MeshInstances,
                    surfaces = coverage.Surfaces,
                    vertices = coverage.Vertices,
                    triangles = coverage.Triangles,
                    materialAuthority =
                        "owned NIF surface identities plus exact owned DDS bindings",
                    collisionConsumed = false,
                },
                materials = new
                {
                    authoredTextureBindingRequests =
                        contract.AuthoredTextureBindingRequests,
                    resolvedUniqueTextures = coverage.LoadedTextures,
                    materialBindings = coverage.MaterialBindings,
                    proofLitRetailMaterials = coverage.ProofLitRetailMaterials,
                    authoredDdsTextures = coverage.AuthoredDdsTextures,
                    authoredDdsMipChainTextures = coverage.AuthoredDdsMipChainTextures,
                    decodedAuthoredBc1AlphaMipChainTextures =
                        coverage.DecodedAuthoredBc1AlphaMipChainTextures,
                    runtimeGeneratedMipTextures = coverage.RuntimeGeneratedMipTextures,
                    unresolvedUniqueTextures = 0,
                    texturesBound = coverage.LoadedTextures == contract.ResolvedUniqueTextures &&
                        coverage.MaterialBindings > 0,
                    lightingAuthority = "recipe proof only; retail CELL lighting remains absent",
                },
                doctorActor = new
                {
                    authority =
                        "owned ACHR/NPC_/template/appearance closure; authored X/Z/yaw/scale " +
                        "with owned posed-foot grounding",
                    referenceFormId = coverage.DoctorActor.ReferenceFormId,
                    baseFormId = coverage.DoctorActor.BaseFormId,
                    name = coverage.DoctorActor.Actor.Name,
                    raceFormId = coverage.DoctorActor.RaceFormId,
                    hairFormId = coverage.DoctorActor.HairFormId,
                    eyesFormId = coverage.DoctorActor.EyesFormId,
                    headPartFormIds = coverage.DoctorActor.HeadPartFormIds,
                    outfitFormIds = coverage.DoctorActor.OutfitFormIds,
                    positionGameUnits = Vector(contract.DoctorActor.PositionGameUnits),
                    authoredPositionGodotGameUnits = Vector(
                        coverage.DoctorGrounding.AuthoredPlacementGodotGameUnits),
                    presentationPositionGodotGameUnits = Vector(
                        coverage.DoctorGrounding.PresentationPlacementGodotGameUnits),
                    grounding = new
                    {
                        authority =
                            "owned utility-room mesh minimum joined to owned posed actor foot bound",
                        supportReferenceFormId =
                            coverage.DoctorGrounding.SupportReferenceFormId,
                        supportBaseEditorId = coverage.DoctorGrounding.SupportBaseEditorId,
                        supportAssetLogicalPath =
                            coverage.DoctorGrounding.SupportAssetLogicalPath,
                        supportGodotGameUnits = coverage.DoctorGrounding.SupportGodotGameUnits,
                        supportGodotMeters = coverage.DoctorGrounding.SupportGodotMeters,
                        ungroundedFootMinimumGodotMeters =
                            coverage.DoctorGrounding.UngroundedFootMinimumGodotMeters,
                        verticalCorrectionGodotGameUnits =
                            coverage.DoctorGrounding.VerticalCorrectionGodotGameUnits,
                        verticalCorrectionGodotMeters =
                            coverage.DoctorGrounding.VerticalCorrectionGodotMeters,
                        groundedFootMinimumGodotMeters =
                            coverage.DoctorGrounding.GroundedBounds.Position.Y,
                        preservedAuthoredHorizontalTransform = true,
                    },
                    idleAnimation = coverage.DoctorActor.Actor.AnimationLogicalPath,
                    idleAuthority =
                        "owned mtidle compiler input only; not CG00 package/script selection",
                    meshes = coverage.DoctorActor.Actor.Meshes,
                    skeletons = coverage.DoctorActor.Actor.Skeletons,
                    animations = coverage.DoctorActor.Actor.Animations,
                    animationChannels = coverage.DoctorActor.Actor.AnimationChannels,
                    authoredComponents = contract.DoctorActor.Components,
                    authoredSkins = contract.DoctorActor.Skins,
                    authoredSurfaces = contract.DoctorActor.Surfaces,
                    authoredTextures = contract.DoctorActor.Textures,
                    faceGenMorphTargets = contract.DoctorActor.FaceGenMorphTargets,
                    proofLitMaterials = coverage.ProofLitActorMaterials,
                    runtimeSurfaces = coverage.DoctorActorGeometry.Surfaces,
                    runtimeVertices = coverage.DoctorActorGeometry.Vertices,
                    runtimeTriangles = coverage.DoctorActorGeometry.Triangles,
                    renderLayerVisible = coverage.DoctorActorGeometry.RenderLayerVisible,
                    aabbValid = coverage.DoctorActorGeometry.AabbValid,
                    frustumIntersection = coverage.DoctorActorGeometry.FrustumIntersection,
                    globalBounds = coverage.DoctorActorGeometry.GlobalAabb is Aabb actorBounds
                        ? new
                        {
                            position = Vector(actorBounds.Position),
                            size = Vector(actorBounds.Size),
                        }
                        : null,
                },
                dadActor = new
                {
                    authority =
                        "direct owned CG00Dad ACHR/NPC_/race/FaceGen with authored stage-0 " +
                        "MoveTo marker and owned posed-foot grounding",
                    referenceFormId = coverage.DadActor.ReferenceFormId,
                    baseFormId = coverage.DadActor.BaseFormId,
                    name = coverage.DadActor.Actor.Name,
                    raceFormId = coverage.DadActor.RaceFormId,
                    hairFormId = coverage.DadActor.HairFormId,
                    eyesFormId = coverage.DadActor.EyesFormId,
                    headPartFormIds = coverage.DadActor.HeadPartFormIds,
                    outfitFormIds = coverage.DadActor.OutfitFormIds,
                    authoredPositionGameUnits = Vector(
                        contract.DadActor.AuthoredPositionGameUnits),
                    stage0MarkerReferenceFormId =
                        contract.DadActor.StartMarkerReferenceFormId,
                    stage0MarkerPositionGameUnits = Vector(
                        contract.DadActor.StartMarkerPositionGameUnits),
                    presentationPositionGodotGameUnits = Vector(
                        coverage.DadGrounding.PresentationPlacementGodotGameUnits),
                    bodySurfaceTextureSource = contract.DadActor.BodySurfaceTextureSource,
                    bodyModSynthesized = false,
                    grounding = new
                    {
                        authority =
                            "owned utility-room mesh minimum joined to owned posed actor foot bound",
                        supportReferenceFormId = coverage.DadGrounding.SupportReferenceFormId,
                        supportBaseEditorId = coverage.DadGrounding.SupportBaseEditorId,
                        supportAssetLogicalPath = coverage.DadGrounding.SupportAssetLogicalPath,
                        supportGodotGameUnits = coverage.DadGrounding.SupportGodotGameUnits,
                        supportGodotMeters = coverage.DadGrounding.SupportGodotMeters,
                        ungroundedFootMinimumGodotMeters =
                            coverage.DadGrounding.UngroundedFootMinimumGodotMeters,
                        verticalCorrectionGodotGameUnits =
                            coverage.DadGrounding.VerticalCorrectionGodotGameUnits,
                        verticalCorrectionGodotMeters =
                            coverage.DadGrounding.VerticalCorrectionGodotMeters,
                        groundedFootMinimumGodotMeters =
                            coverage.DadGrounding.GroundedBounds.Position.Y,
                        preservedStage0MarkerHorizontalTransform = true,
                    },
                    idleAnimation = coverage.DadActor.Actor.AnimationLogicalPath,
                    idleAuthority =
                        "owned mtidle compiler input only; not CG00 package/script selection",
                    authoredComponents = contract.DadActor.Components,
                    authoredSkins = contract.DadActor.Skins,
                    authoredSurfaces = contract.DadActor.Surfaces,
                    authoredTextures = contract.DadActor.Textures,
                    faceGenMorphTargets = contract.DadActor.FaceGenMorphTargets,
                    proofLitMaterials = coverage.ProofLitDadActorMaterials,
                    runtimeSurfaces = coverage.DadActorGeometry.Surfaces,
                    runtimeVertices = coverage.DadActorGeometry.Vertices,
                    runtimeTriangles = coverage.DadActorGeometry.Triangles,
                    renderLayerVisible = coverage.DadActorGeometry.RenderLayerVisible,
                    aabbValid = coverage.DadActorGeometry.AabbValid,
                    frustumIntersection = coverage.DadActorGeometry.FrustumIntersection,
                },
                frame = new
                {
                    path = framePath,
                    bytes = frameStream.Length,
                    sha256 = frameSha256,
                    width = metrics.Width,
                    height = metrics.Height,
                    meanLuminance = metrics.MeanLuminance,
                    luminanceDeviation = metrics.LuminanceDeviation,
                    nonBackgroundPixels = metrics.NonBackgroundPixels,
                    visualGatePassed = roomFailure is null,
                    visualGateFailure = roomFailure,
                },
                doctorActorFrame = new
                {
                    path = actorFramePath,
                    bytes = actorFrameStream.Length,
                    sha256 = actorFrameSha256,
                    width = actorMetrics.Width,
                    height = actorMetrics.Height,
                    meanLuminance = actorMetrics.MeanLuminance,
                    luminanceDeviation = actorMetrics.LuminanceDeviation,
                    nonBackgroundPixels = actorMetrics.NonBackgroundPixels,
                    sourceTransformPreserved = true,
                    staticGeometryHiddenForIsolation = true,
                    cameraAuthority = "proof-only framing, not retail or CG00 camera",
                    cameraPositionGodotMeters = Vector(doctorProofCameraPosition),
                    targetGodotMeters = Vector(actorTarget),
                    runtimeSurfaces = actorProofGeometry.Surfaces,
                    runtimeVertices = actorProofGeometry.Vertices,
                    runtimeTriangles = actorProofGeometry.Triangles,
                    renderLayerVisible = actorProofGeometry.RenderLayerVisible,
                    aabbValid = actorProofGeometry.AabbValid,
                    frustumIntersection = actorProofGeometry.FrustumIntersection,
                    projectedScreenBounds = actorProofGeometry.ProjectedScreenBounds is Vector4 bounds
                        ? new[] { bounds.X, bounds.Y, bounds.Z, bounds.W }
                        : null,
                    visualGatePassed = actorFailure is null,
                    visualGateFailure = actorFailure,
                },
                dadActorFrame = new
                {
                    path = dadFramePath,
                    bytes = dadFrameStream.Length,
                    sha256 = dadFrameSha256,
                    width = dadMetrics.Width,
                    height = dadMetrics.Height,
                    meanLuminance = dadMetrics.MeanLuminance,
                    luminanceDeviation = dadMetrics.LuminanceDeviation,
                    nonBackgroundPixels = dadMetrics.NonBackgroundPixels,
                    stage0MarkerTransformPreserved = true,
                    staticGeometryHiddenForIsolation = true,
                    cameraAuthority = "proof-only framing, not retail or CG00 camera",
                    cameraPositionGodotMeters = Vector(dadProofCameraPosition),
                    targetGodotMeters = Vector(dadTarget),
                    runtimeSurfaces = dadProofGeometry.Surfaces,
                    runtimeVertices = dadProofGeometry.Vertices,
                    runtimeTriangles = dadProofGeometry.Triangles,
                    renderLayerVisible = dadProofGeometry.RenderLayerVisible,
                    aabbValid = dadProofGeometry.AabbValid,
                    frustumIntersection = dadProofGeometry.FrustumIntersection,
                    projectedScreenBounds = dadProofGeometry.ProjectedScreenBounds is Vector4 dadBounds
                        ? new[] { dadBounds.X, dadBounds.Y, dadBounds.Z, dadBounds.W }
                        : null,
                    visualGatePassed = dadFailure is null,
                    visualGateFailure = dadFailure,
                },
                promotion = new
                {
                    transported = true,
                    runtimeManifestValidated = true,
                    runtimeSceneConstructed = true,
                    rendered = failure is null,
                    interactive = false,
                    actorsRendered = failure is null,
                    doctorLiRendered = failure is null &&
                        actorFailure is null,
                    cg00DadRendered = failure is null && dadFailure is null,
                    questCommandsExecuted = false,
                    characterSelectionJoinedToScene = true,
                    sourceBoundDialogueCue = failure is null && dialoguePlaybackStarted,
                    collisionConsumed = false,
                    texturesBound = failure is null &&
                        coverage.LoadedTextures == contract.ResolvedUniqueTextures &&
                        coverage.MaterialBindings > 0,
                    retailParityReviewed = false,
                    headsetAccepted = false,
                    launcherPlayable = false,
                },
                unsupported = new[]
                {
                    "Mom, player body, and all actors except Doctor Li and CG00 Dad",
                    "automatic CG00 dialogue timing, package execution, scripted animation selection, and quest triggers",
                    "Dad lip animation",
                    "CELL lighting, image-space effects, collision, world interaction, and OpenXR",
                    "retail camera, material, lighting, animation, and pixel parity",
                },
                windowsAppControlUsed = false,
                foregroundActivationUsed = false,
                foregroundInputInjected = false,
            };
            File.WriteAllText(
                Path.Combine(output, "vault101-birth-native-render-proof.json"),
                JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) +
                    System.Environment.NewLine);
            if (failure is null)
                GD.Print(
                    $"OPENNV_FO3_VAULT101_RENDER_PASS cell={contract.CellFormId} " +
                    $"entry={contract.EntryReferenceFormId} references={coverage.PlacedReferences} " +
                    $"models={coverage.LoadedAssets} surfaces={coverage.Surfaces} " +
                    $"textures={coverage.LoadedTextures} materials={coverage.MaterialBindings} " +
                    $"actors=2 actorSurfaces=" +
                    $"{coverage.DoctorActorGeometry.Surfaces + coverage.DadActorGeometry.Surfaces} " +
                    $"interactive=0 output={output}");
            else
                GD.PushError(
                    $"OPENNV_FO3_VAULT101_RENDER_VISUAL_FAIL failure={failure} output={output}");
            GetTree().Quit(failure is null ? 0 : 1);
        }
        catch (Exception exception)
        {
            GD.PushError($"OPENNV_FO3_VAULT101_RENDER_FAIL {exception.Message}");
            GetTree().Quit(1);
        }
    }

    private static CanvasLayer BuildDialogueOverlay(string subtitle)
    {
        var layer = new CanvasLayer { Name = "FO3_CG00_DAD_DIALOGUE_OVERLAY" };
        var panel = new PanelContainer
        {
            OffsetLeft = 150.0f,
            OffsetTop = 540.0f,
            OffsetRight = 1130.0f,
            OffsetBottom = 700.0f,
        };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.0f, 0.0f, 0.0f, 0.88f),
            BorderColor = new Color(0.25f, 0.85f, 0.35f),
            BorderWidthLeft = 2,
            BorderWidthTop = 2,
            BorderWidthRight = 2,
            BorderWidthBottom = 2,
        });
        layer.AddChild(panel);
        var label = new Label
        {
            Text = $"DAD\n{subtitle}",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        label.AddThemeFontSizeOverride("font_size", 24);
        panel.AddChild(label);
        return layer;
    }

    private static FrameMetrics Analyze(Image image, Color backgroundColor)
    {
        var data = image.GetData();
        var pixels = image.GetWidth() * image.GetHeight();
        if (pixels <= 0 || data.Length != pixels * 4)
            throw new InvalidOperationException("Fallout 3 Vault 101 viewport is empty.");
        var background = new Vector3(
            backgroundColor.R * byte.MaxValue,
            backgroundColor.G * byte.MaxValue,
            backgroundColor.B * byte.MaxValue);
        double luminance = 0.0;
        double luminanceSquared = 0.0;
        var nonBackgroundPixels = 0;
        for (var offset = 0; offset < data.Length; offset += 4)
        {
            var value = (0.2126 * data[offset] + 0.7152 * data[offset + 1] +
                0.0722 * data[offset + 2]) / byte.MaxValue;
            luminance += value;
            luminanceSquared += value * value;
            var delta = new Vector3(data[offset], data[offset + 1], data[offset + 2]) -
                background;
            if (delta.LengthSquared() > BackgroundPixelDeltaSquared)
                nonBackgroundPixels++;
        }
        var mean = luminance / pixels;
        return new FrameMetrics(
            image.GetWidth(),
            image.GetHeight(),
            mean,
            Math.Sqrt(Math.Max(0.0, luminanceSquared / pixels - mean * mean)),
            nonBackgroundPixels);
    }

    private static string RequiredOption(string option)
    {
        var arguments = OS.GetCmdlineUserArgs();
        var matches = Enumerable.Range(0, arguments.Length - 1)
            .Where(index => arguments[index] == option)
            .Select(index => arguments[index + 1])
            .ToArray();
        if (matches.Length != 1 || string.IsNullOrWhiteSpace(matches[0]))
            throw new InvalidOperationException($"Required Fallout 3 option is absent: {option}");
        return matches[0];
    }

    private static float[] Vector(Vector3 value) => [value.X, value.Y, value.Z];

    private readonly record struct FrameMetrics(
        int Width,
        int Height,
        double MeanLuminance,
        double LuminanceDeviation,
        int NonBackgroundPixels);
}
