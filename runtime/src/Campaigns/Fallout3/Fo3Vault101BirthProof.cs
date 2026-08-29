using System.Security.Cryptography;
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

            var profile = Fo3OwnedProfile.Load(profilePath);
            var contract = Fo3Vault101BirthPresentationContract.Load(
                profile.BirthSlice,
                presentationPath);
            var handoff = profile.Section4Transition;
            if (handoff.SourceStage != profile.Appearance.AcceptedStage ||
                !handoff.LocationReferenceFormId.Equals(
                    contract.EntryReferenceFormId,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "Fallout 3 stage-62 package location does not join the owned player marker.");
            var coverage = Fo3Vault101BirthScene.Build(this, contract);
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
            var proofCameraPosition = coverage.Camera.GlobalPosition;
            var proofCameraFov = coverage.Camera.Fov;
            foreach (var child in coverage.CellRoot.GetChildren().OfType<Node3D>()
                         .Where(child => child.Name.ToString().StartsWith(
                             "REFR_", StringComparison.Ordinal)))
                child.Visible = false;
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
            var failure = roomFailure ?? actorFailure;
            var report = new
            {
                schema = "opennv-fo3-vault101-birth-native-render-proof/v4",
                status = failure is null
                    ? "pass-rendered-owned-textured-birth-room-and-doctor-li-no-dialogue-scripts-or-gameplay"
                    : "fail-rendered-owned-birth-room",
                campaign = "Fallout3",
                slice = "Vault101BirthRoom",
                renderer = RenderingServer.GetCurrentRenderingMethod(),
                displayDriver = DisplayServer.GetName(),
                source = new
                {
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
                    cameraPositionGodotMeters = Vector(coverage.Camera.GlobalPosition),
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
                    questCommandsExecuted = false,
                    characterSelectionJoinedToScene = true,
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
                    "Dad, Mom, player body, and all actors except Doctor Li",
                    "CG00 dialogue, packages, scripted animation selection, quest triggers, and stage progression",
                    "CELL lighting, image-space effects, collision, interaction, audio, save, and OpenXR",
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
                    $"actors=1 actorSurfaces={coverage.DoctorActorGeometry.Surfaces} " +
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
