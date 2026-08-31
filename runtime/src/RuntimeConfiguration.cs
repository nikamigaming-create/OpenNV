using System.Globalization;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;


using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime;

internal sealed record RuntimeConfiguration(
    string Schema,
    WorldConfiguration World,
    SimulationConfiguration Simulation,
    RendererConfiguration Renderer,
    PlayerConfiguration Player,
    XrConfiguration Xr,
    PickupConfiguration Pickup,
    PoolConfiguration Pool,
    DoorConfiguration Door,
    HudConfiguration Hud,
    CaptureConfiguration Capture,
    ProofConfiguration Proof,
    RuntimePerformanceConfiguration Performance,
    DiagnosticPreviewConfiguration DiagnosticPreview,
    ActorReviewConfiguration ActorReview,
    ExteriorEnvironmentConfiguration ExteriorEnvironment,
    FalloutEnvironmentConfiguration FalloutEnvironment,
    RetailActorStateConfiguration RetailActorState,
    ActorParityConfiguration ActorParity,
    SetupViewConfiguration SetupView,
    DesktopLauncherConfiguration DesktopLauncher,
    LegalAssetsConfiguration LegalAssets,
    ToolingConfiguration Tooling,
    ContentCompilerConfiguration ContentCompiler,
    ActorCompilerConfiguration ActorCompiler)
{
    internal const string ExpectedSchema = "opennv-runtime-configuration/v1";
    internal const string ActorArtifactExpectedSchema =
        "opennv-actor-artifact-runtime-configuration/v1";
    internal const string ResourcePath = "res://config/open-nv-runtime-v1.json";
    private const float PerspectiveMaximumDegrees = 180.0f;
    private const int RgbaChannelCount = 4;

    internal string Sha256 { get; private set; } = "";
    internal string ActorArtifactConfigurationSha256 { get; private set; } = "";

    private static readonly string[] ActorArtifactContentCompilerFields =
    [
        "animationSamplesPerSecond",
        "assetIdHexCharacters",
        "defaultMaterialGlossiness",
        "minimumMaterialRoughness",
        "pngCompressionLevel",
        "zeroSpecularEpsilon",
    ];

    internal static RuntimeConfiguration Load()
    {
        var payload = Godot.FileAccess.GetFileAsBytes(ResourcePath);
        if (payload.Length == 0)
            throw new InvalidOperationException(
                $"OpenNV runtime configuration is missing or empty: {ResourcePath}");
        var configuration = JsonSerializer.Deserialize<RuntimeConfiguration>(
            payload,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            })
            ?? throw new InvalidOperationException(
                $"OpenNV runtime configuration is empty: {ResourcePath}");
        if (configuration.Schema != ExpectedSchema)
            throw new InvalidOperationException(
                $"Unexpected OpenNV runtime configuration: {ResourcePath}");
        configuration.Validate();
        configuration.Sha256 = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
        using var source = JsonDocument.Parse(payload);
        configuration.ActorArtifactConfigurationSha256 =
            ActorArtifactConfigurationHash(source.RootElement);
        return configuration;
    }

    internal void VerifyCompiledConfiguration(JsonElement source)
    {
        VerifyCompiledConfigurationDescriptor(source.GetProperty("configuration"));
    }

    internal void VerifyCompiledConfigurationDescriptor(JsonElement compiled)
    {
        if (compiled.GetProperty("schema").GetString() != ExpectedSchema ||
            !compiled.GetProperty("sha256").GetString()!.Equals(Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Prepared content was compiled with another runtime configuration.");
    }

    internal void VerifyCompiledActorConfiguration(JsonElement source)
    {
        var compiled = source.GetProperty("configuration");
        var schema = compiled.GetProperty("schema").GetString();
        if (schema == ExpectedSchema)
        {
            VerifyCompiledConfigurationDescriptor(compiled);
            return;
        }
        if (schema != ActorArtifactExpectedSchema ||
            compiled.EnumerateObject().Count() != 3 ||
            !compiled.GetProperty("sha256").GetString()!.Equals(
                ActorArtifactConfigurationSha256,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Prepared actor content was compiled with another actor configuration.");
        var sections = compiled.GetProperty("sections");
        if (sections.EnumerateObject().Count() != 2 ||
            sections.GetProperty("actorCompiler").GetString() != "all" ||
            !sections.GetProperty("contentCompiler").EnumerateArray()
                .Select(value => value.GetString()!)
                .SequenceEqual(ActorArtifactContentCompilerFields, StringComparer.Ordinal))
            throw new InvalidOperationException(
                "Prepared actor content has another configuration scope.");
    }

    private static string ActorArtifactConfigurationHash(JsonElement root)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(
            stream,
            new JsonWriterOptions
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            }))
        {
            writer.WriteStartObject();
            writer.WritePropertyName("actorCompiler");
            WriteCanonicalJson(writer, root.GetProperty("actorCompiler"));
            writer.WritePropertyName("contentCompiler");
            writer.WriteStartObject();
            var contentCompiler = root.GetProperty("contentCompiler");
            foreach (var field in ActorArtifactContentCompilerFields)
            {
                writer.WritePropertyName(field);
                WriteCanonicalJson(writer, contentCompiler.GetProperty(field));
            }
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
        return Convert.ToHexString(SHA256.HashData(stream.ToArray())).ToLowerInvariant();
    }

    private static void WriteCanonicalJson(Utf8JsonWriter writer, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in value.EnumerateObject()
                    .OrderBy(row => row.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonicalJson(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var row in value.EnumerateArray())
                    WriteCanonicalJson(writer, row);
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(value.GetString());
                break;
            case JsonValueKind.Number:
                writer.WriteStringValue(
                    value.TryGetInt64(out var integer)
                        ? integer.ToString(CultureInfo.InvariantCulture)
                        : value.GetDouble().ToString("G17", CultureInfo.InvariantCulture)
                            .ToLowerInvariant());
                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            case JsonValueKind.Null:
                writer.WriteNullValue();
                break;
            default:
                throw new InvalidOperationException(
                    "Runtime configuration contains unsupported JSON.");
        }
    }

    private void Validate()
    {
        foreach (var provenance in new[]
        {
            World.Provenance,
            Simulation.Provenance,
            Renderer.Provenance,
            Player.Provenance,
            Player.DesktopInput.Provenance,
            Xr.Provenance,
            Xr.Contract.Provenance,
            Xr.SimulatorAcceptance.Provenance,
            Xr.DiagnosticRigProof.Provenance,
            Xr.DiagnosticMuzzleFlash.Provenance,
            Pickup.Provenance,
            Pool.Provenance,
            Door.Provenance,
            Hud.Provenance,
            Capture.Provenance,
            Capture.Gallery.Provenance,
            Capture.Gallery.Video.Provenance,
            Proof.Provenance,
            Proof.GameplayRoute.Provenance,
            Performance.Provenance,
            DiagnosticPreview.Provenance,
            ActorReview.Provenance,
            ExteriorEnvironment.Provenance,
            FalloutEnvironment.Provenance,
            FalloutEnvironment.ImageSpace.Provenance,
            RetailActorState.Provenance,
            ActorParity.Provenance,
            SetupView.Provenance,
            DesktopLauncher.Provenance,
            LegalAssets.Provenance,
            Tooling.Provenance,
            ContentCompiler.Provenance,
            ContentCompiler.SpeedTree.Provenance,
            ContentCompiler.RetailGrass.Provenance,
            ActorCompiler.Provenance,
            ActorCompiler.FaceGenAnimation.Provenance,
            ActorCompiler.RigidAttachment.Provenance,
        })
            provenance.Validate();

        RequirePositive(World.GameUnitsToMeters, nameof(World.GameUnitsToMeters));
        RequirePositive(Simulation.PhysicsTicksPerSecond, nameof(Simulation.PhysicsTicksPerSecond));
        RequirePositive(Simulation.GravityMetersPerSecondSquared, nameof(Simulation.GravityMetersPerSecondSquared));
        RequirePositive(Performance.SampleIntervalSeconds, nameof(Performance.SampleIntervalSeconds));
        RequirePositive(Player.CapsuleRadiusMeters, nameof(Player.CapsuleRadiusMeters));
        RequirePositive(Player.CapsuleHeightMeters, nameof(Player.CapsuleHeightMeters));
        RequirePositive(Player.MoveSpeedMetersPerSecond, nameof(Player.MoveSpeedMetersPerSecond));
        RequirePositive(Player.ActivationDistanceMeters, nameof(Player.ActivationDistanceMeters));
        RequirePositive(Player.FireRayDistanceMeters, nameof(Player.FireRayDistanceMeters));
        RequirePositive(Player.CameraNearMeters, nameof(Player.CameraNearMeters));
        Player.DesktopInput.Validate();
        if (Player.CameraFarMeters <= Player.CameraNearMeters)
            throw new InvalidOperationException("Player camera far plane must exceed its near plane.");
        RequirePositive(Xr.WorldScale, nameof(Xr.WorldScale));
        RequirePositive(Xr.DesiredEyeHeightMeters, nameof(Xr.DesiredEyeHeightMeters));
        RequirePositive(Xr.EyeHeightCalibrationTrackedFrames, nameof(Xr.EyeHeightCalibrationTrackedFrames));
        RequirePositive(Xr.InputHealthReportFrames, nameof(Xr.InputHealthReportFrames));
        RequirePositive(Xr.Contract.ExpectedActionSetCount, nameof(Xr.Contract.ExpectedActionSetCount));
        if (Xr.Contract.ActionNames.Count < 1 || Xr.Contract.InteractionProfilePaths.Count < 1)
            throw new InvalidOperationException("XR contract actions and profiles must not be empty.");
        RequirePositive(Xr.DiagnosticRigProof.Damage, nameof(Xr.DiagnosticRigProof.Damage));
        RequirePositive(Xr.DiagnosticRigProof.ClipSize, nameof(Xr.DiagnosticRigProof.ClipSize));
        RequirePositive(Xr.DiagnosticRigProof.ReserveRounds, nameof(Xr.DiagnosticRigProof.ReserveRounds));
        RequirePositive(
            Xr.DiagnosticRigProof.ExpectedShotsFired,
            nameof(Xr.DiagnosticRigProof.ExpectedShotsFired));
        RequirePositive(
            Xr.DiagnosticRigProof.ExpectedAmmoInMagazineAfterReload,
            nameof(Xr.DiagnosticRigProof.ExpectedAmmoInMagazineAfterReload));
        RequirePositive(
            Xr.DiagnosticRigProof.ExpectedReserveRoundsAfterReload,
            nameof(Xr.DiagnosticRigProof.ExpectedReserveRoundsAfterReload));
        RequireUnitInterval(Xr.ActionThreshold, nameof(Xr.ActionThreshold));
        RequireUnitInterval(Xr.SnapTurnActivationThreshold, nameof(Xr.SnapTurnActivationThreshold));
        RequireUnitInterval(Xr.SnapTurnResetThreshold, nameof(Xr.SnapTurnResetThreshold));
        if (Xr.SnapTurnResetThreshold >= Xr.SnapTurnActivationThreshold)
            throw new InvalidOperationException("XR snap-turn reset threshold must be lower than activation.");
        RequirePositive(Pickup.HoldDistanceMeters, nameof(Pickup.HoldDistanceMeters));
        if (Pickup.CollisionLayer == 0 || Pickup.CollisionMask == 0)
            throw new InvalidOperationException(
                "Pickup collision layer and mask must be nonzero.");
        if (Pool.FlatStrikeSpeedsMetersPerSecond.Count < 1 ||
            Pool.FlatStrikeSpeedsMetersPerSecond.Any(value => !float.IsFinite(value) || value <= 0.0f))
            throw new InvalidOperationException("Pool flat strike speeds must be nonempty and positive.");
        RequireVector(Pool.DesktopCueMountPositionMeters, 3, nameof(Pool.DesktopCueMountPositionMeters));
        RequireVector(Pool.DesktopCueMountRotationDegrees, 3, nameof(Pool.DesktopCueMountRotationDegrees));
        RequireVector(Pool.XrCueMountPositionMeters, 3, nameof(Pool.XrCueMountPositionMeters));
        RequireVector(Pool.XrCueMountRotationDegrees, 3, nameof(Pool.XrCueMountRotationDegrees));
        RequirePositive(Pool.XrMinimumTipSpeedMetersPerSecond, nameof(Pool.XrMinimumTipSpeedMetersPerSecond));
        if (Pool.XrMaximumTipSpeedMetersPerSecond <= Pool.XrMinimumTipSpeedMetersPerSecond)
            throw new InvalidOperationException("Pool XR maximum tip speed must exceed its minimum.");
        RequirePositive(Pool.XrStrikeCooldownSeconds, nameof(Pool.XrStrikeCooldownSeconds));
        RequirePositive(Pool.XrImpulseScale, nameof(Pool.XrImpulseScale));
        RequirePositive(Pool.MaximumReportedContacts, nameof(Pool.MaximumReportedContacts));
        RequirePositive(Pool.ProofMaximumPhysicsFrames, nameof(Pool.ProofMaximumPhysicsFrames));
        if (Pool.CollisionLayer == 0 || Pool.CollisionMask == 0)
            throw new InvalidOperationException("Pool collision layer and mask must be nonzero.");
        RequireText(Pool.ResetStatusText, nameof(Pool.ResetStatusText));
        RequireUnitInterval((float)Pool.StrikeHaptic.Amplitude, nameof(Pool.StrikeHaptic.Amplitude));
        RequirePositive(Pool.StrikeHaptic.DurationSeconds, nameof(Pool.StrikeHaptic.DurationSeconds));
        RequirePositive(Door.OpenAngleDegrees, nameof(Door.OpenAngleDegrees));
        RequirePositive(Proof.SpawnFloorRayStartMeters, nameof(Proof.SpawnFloorRayStartMeters));
        if (Proof.SpawnFloorRayEndMeters >= 0.0f)
            throw new InvalidOperationException("Spawn floor proof ray end must be below the origin.");
        RequirePositive(Proof.DoorRayThicknessMultiplier, nameof(Proof.DoorRayThicknessMultiplier));
        RequirePositive(Proof.DoorRayMinimumReachGameUnits, nameof(Proof.DoorRayMinimumReachGameUnits));
        RequirePositive(Proof.GameplayRoute.ExpectedShotsFired, nameof(Proof.GameplayRoute.ExpectedShotsFired));
        RequirePositive(
            Proof.GameplayRoute.ExpectedEmptiedContainers,
            nameof(Proof.GameplayRoute.ExpectedEmptiedContainers));
        RequirePositive(
            Proof.GameplayRoute.ExpectedOpenDoors,
            nameof(Proof.GameplayRoute.ExpectedOpenDoors));
        RequireVector(Player.DesktopCameraOffsetMeters, 3, nameof(Player.DesktopCameraOffsetMeters));
        RequirePositive(
            Xr.HandAlignmentPositionToleranceMeters,
            nameof(Xr.HandAlignmentPositionToleranceMeters));
        RequirePositive(
            Xr.HandAlignmentRotationToleranceRadians,
            nameof(Xr.HandAlignmentRotationToleranceRadians));
        RequirePositive(Xr.SimulatorAcceptance.TimeoutFrames, nameof(Xr.SimulatorAcceptance.TimeoutFrames));
        RequirePositive(
            Xr.SimulatorAcceptance.MinimumTrackedFrames,
            nameof(Xr.SimulatorAcceptance.MinimumTrackedFrames));
        RequirePositive(
            Xr.SimulatorAcceptance.MinimumLocomotionMeters,
            nameof(Xr.SimulatorAcceptance.MinimumLocomotionMeters));
        RequirePositive(
            Xr.SimulatorAcceptance.MinimumHandTravelMeters,
            nameof(Xr.SimulatorAcceptance.MinimumHandTravelMeters));
        RequirePositive(
            Xr.SimulatorAcceptance.EyeHeightToleranceMeters,
            nameof(Xr.SimulatorAcceptance.EyeHeightToleranceMeters));
        RequirePositive(
            Xr.SimulatorAcceptance.MaximumSnapPivotErrorMeters,
            nameof(Xr.SimulatorAcceptance.MaximumSnapPivotErrorMeters));
        RequirePositive(
            Xr.SimulatorAcceptance.FloorProbeAboveEyeMeters,
            nameof(Xr.SimulatorAcceptance.FloorProbeAboveEyeMeters));
        RequirePositive(
            Xr.SimulatorAcceptance.FloorProbeDistanceMeters,
            nameof(Xr.SimulatorAcceptance.FloorProbeDistanceMeters));
        RequirePositive(
            Xr.SimulatorAcceptance.MinimumSnapTurns,
            nameof(Xr.SimulatorAcceptance.MinimumSnapTurns));
        RequirePositive(
            Xr.SimulatorAcceptance.MinimumAcceptedActivations,
            nameof(Xr.SimulatorAcceptance.MinimumAcceptedActivations));
        RequirePositive(
            Xr.SimulatorAcceptance.MinimumAcceptedFireActions,
            nameof(Xr.SimulatorAcceptance.MinimumAcceptedFireActions));
        RequirePositive(
            Xr.SimulatorAcceptance.MinimumAcceptedReloadActions,
            nameof(Xr.SimulatorAcceptance.MinimumAcceptedReloadActions));
        RequirePositive(
            Xr.SimulatorAcceptance.MinimumSaveActions,
            nameof(Xr.SimulatorAcceptance.MinimumSaveActions));
        RequireVector(Hud.DesktopPanelPositionPixels, 2, nameof(Hud.DesktopPanelPositionPixels));
        RequireVector(Hud.DesktopPanelSizePixels, 2, nameof(Hud.DesktopPanelSizePixels));
        RequireVector(Hud.DesktopLabelsPositionPixels, 2, nameof(Hud.DesktopLabelsPositionPixels));
        RequireVector(Hud.DesktopLabelsSizePixels, 2, nameof(Hud.DesktopLabelsSizePixels));
        RequireVector(Hud.CrosshairPositionPixels, 2, nameof(Hud.CrosshairPositionPixels));
        RequireVector(Hud.XrMountPositionMeters, 3, nameof(Hud.XrMountPositionMeters));
        RequireVector(Hud.XrMountRotationDegrees, 3, nameof(Hud.XrMountRotationDegrees));
        RequireVector(Hud.PipBoyPanelPositionPixels, 2, nameof(Hud.PipBoyPanelPositionPixels));
        RequireVector(Hud.PipBoyPanelSizePixels, 2, nameof(Hud.PipBoyPanelSizePixels));
        if (Hud.PipBoyPanelSizePixels.Any(value => value <= 0.0f))
            throw new InvalidOperationException("Pip-Boy panel size must be positive.");
        RequireColor(Renderer.BackgroundColorRgba, nameof(Renderer.BackgroundColorRgba));
        RequireColor(Renderer.NeutralNormalColorRgba, nameof(Renderer.NeutralNormalColorRgba));
        if (Renderer.NeutralNormalTextureSizePixels.Length != 2 ||
            Renderer.NeutralNormalTextureSizePixels.Any(value => value <= 0))
            throw new InvalidOperationException("Neutral normal texture dimensions must be two positive values.");
        if (ContentCompiler.NonPresentationBaseFormIds.Count < 1 ||
            ContentCompiler.NonPresentationBaseFormIds
                .Select(FalloutFormId.Normalize)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() != ContentCompiler.NonPresentationBaseFormIds.Count)
            throw new InvalidOperationException(
                "Content compiler non-presentation base FormIDs must be nonempty and unique.");
        RequirePositive(Renderer.CubemapFaceCount, nameof(Renderer.CubemapFaceCount));
        RequireColor(Hud.DesktopPanelColorRgba, nameof(Hud.DesktopPanelColorRgba));
        RequireColor(Hud.TextColorRgba, nameof(Hud.TextColorRgba));
        foreach (var text in Hud.Copy.Values)
            RequireText(text, nameof(Hud.Copy));
        foreach (var text in Hud.PipBoy.Values)
            RequireText(text, nameof(Hud.PipBoy));
        RequireColor(DiagnosticPreview.BackgroundColorRgba, nameof(DiagnosticPreview.BackgroundColorRgba));
        RequireColor(DiagnosticPreview.AmbientColorRgba, nameof(DiagnosticPreview.AmbientColorRgba));
        RequirePositive(
            DiagnosticPreview.ActorMinimumHeightMeters,
            nameof(DiagnosticPreview.ActorMinimumHeightMeters));
        if (DiagnosticPreview.ActorMaximumHeightMeters <= DiagnosticPreview.ActorMinimumHeightMeters)
            throw new InvalidOperationException("Diagnostic actor maximum height must exceed its minimum.");
        RequirePositive(
            ActorReview.ProjectionAspectTolerance,
            nameof(ActorReview.ProjectionAspectTolerance));
        RequirePositive(
            ActorReview.CameraBasisTolerance,
            nameof(ActorReview.CameraBasisTolerance));
        RequirePositive(
            ActorReview.ProjectedBoneTolerancePixels,
            nameof(ActorReview.ProjectedBoneTolerancePixels));
        RequirePositive(
            ActorReview.SkinPaletteLinearTolerance,
            nameof(ActorReview.SkinPaletteLinearTolerance));
        RequirePositive(
            ActorReview.SkinPaletteTranslationToleranceGameUnits,
            nameof(ActorReview.SkinPaletteTranslationToleranceGameUnits));
        RequireVector(
            ActorReview.DirectionalRotationDegrees,
            3,
            nameof(ActorReview.DirectionalRotationDegrees));
        RequireVector(ExteriorEnvironment.AmbientColor, 3, nameof(ExteriorEnvironment.AmbientColor));
        RequireVector(ExteriorEnvironment.DirectionalColor, 3, nameof(ExteriorEnvironment.DirectionalColor));
        RequireVector(ExteriorEnvironment.FogColor, 3, nameof(ExteriorEnvironment.FogColor));
        RequireVector(
            ExteriorEnvironment.DirectionalRotationDegrees,
            2,
            nameof(ExteriorEnvironment.DirectionalRotationDegrees));
        RequirePositive(ExteriorEnvironment.FogFarGameUnits, nameof(ExteriorEnvironment.FogFarGameUnits));
        if (ExteriorEnvironment.FogNearGameUnits < 0.0f ||
            ExteriorEnvironment.FogNearGameUnits >= ExteriorEnvironment.FogFarGameUnits)
            throw new InvalidOperationException("Exterior fog near must be nonnegative and below fog far.");
        RequirePositive(
            FalloutEnvironment.CloudSpeedDivisor,
            nameof(FalloutEnvironment.CloudSpeedDivisor));
        if (FalloutEnvironment.SkyRgbMultiplierImageSpaceTraitIndex < 0)
            throw new InvalidOperationException(
                "Fallout sky RGB multiplier trait index cannot be negative.");
        if (FalloutEnvironment.AtmosphereRenderPriority >=
            FalloutEnvironment.CloudRenderPriority)
            throw new InvalidOperationException(
                "Fallout atmosphere must render before its cloud layers.");
        FalloutEnvironment.ImageSpace.Validate();
        if (Capture.EnvironmentShots.Count < 1)
            throw new InvalidOperationException("Capture configuration must declare at least one environment shot.");
        RequirePositive(Capture.RgbaChannelCount, nameof(Capture.RgbaChannelCount));
        RequirePositive(Capture.PixelChannelMaximum, nameof(Capture.PixelChannelMaximum));
        RequireVector(Capture.LuminanceWeightsRgb, 3, nameof(Capture.LuminanceWeightsRgb));
        RequirePositive(
            Capture.Gallery.VerticalFovDegrees,
            nameof(Capture.Gallery.VerticalFovDegrees));
        if (Capture.Gallery.VerticalFovDegrees >= PerspectiveMaximumDegrees)
            throw new InvalidOperationException(
                "Gallery vertical FOV must be below the perspective limit.");
        RequireUnitInterval(
            Capture.Gallery.MaximumFrameOccupancy,
            nameof(Capture.Gallery.MaximumFrameOccupancy));
        if (Capture.Gallery.MaximumFrameOccupancy <= 0.0f)
            throw new InvalidOperationException(
                "Gallery frame occupancy must be greater than zero.");
        if (Capture.Gallery.TargetNodeRole != "sidecar-biped-head")
            throw new InvalidOperationException(
                "Gallery target node role must use the owned sidecar biped head.");
        if (Capture.Gallery.FacingPoseSource != "full-body-owned-animation-root")
            throw new InvalidOperationException(
                "Gallery facing must use the full-body owned animation root.");
        if (Capture.Gallery.OcclusionClearanceSource != "camera-near-plane")
            throw new InvalidOperationException(
                "Gallery occlusion clearance must use the configured camera near plane.");
        if (Capture.Gallery.ModelFrontAxis is not ("positive-z" or "negative-z"))
            throw new InvalidOperationException("Gallery model front axis is unsupported.");
        var presentation = Capture.Gallery.RetailPresentationSelection;
        if (presentation.Schema != "opennv-gallery-presentation-selection/v1" ||
            presentation.CandidateShotKinds.Count < 1 ||
            presentation.CandidateShotKinds.Distinct(StringComparer.Ordinal).Count() !=
                presentation.CandidateShotKinds.Count ||
            presentation.CandidateShotKinds.Any(kind =>
                !Capture.ActorShotKinds.Contains(kind, StringComparer.Ordinal)))
            throw new InvalidOperationException(
                "Gallery retail presentation candidates must be unique configured actor shots.");
        if (presentation.RequiredSurfaceStatus !=
                "visible-final-eye-semantic-focus-draw" ||
            !presentation.RequireSemanticFocusSurface ||
            !presentation.RequireCameraOutsideActorWorldBound ||
            !presentation.RequireClearCameraCorridor ||
            presentation.CameraTranslationToleranceGameUnits <= 0.0f ||
            presentation.TieBreak != "candidate-order-then-lowest-source-frame" ||
            presentation.SemanticFocusFacingRules.Count < 1 ||
            presentation.SemanticFocusFacingRules
                .Select(rule => rule.FocusKind)
                .Distinct(StringComparer.Ordinal).Count() !=
                    presentation.SemanticFocusFacingRules.Count)
            throw new InvalidOperationException(
                "Gallery retail presentation selection policy is incomplete.");
        foreach (var rule in presentation.SemanticFocusFacingRules)
        {
            if (string.IsNullOrWhiteSpace(rule.FocusKind) ||
                rule.AllowedShotKinds.Count < 1 ||
                rule.AllowedShotKinds.Distinct(StringComparer.Ordinal).Count() !=
                    rule.AllowedShotKinds.Count ||
                rule.AllowedShotKinds.Any(kind =>
                    !presentation.CandidateShotKinds.Contains(kind, StringComparer.Ordinal)) ||
                rule.MinimumCameraDirectionDotFocusForward < -1.0f ||
                rule.MaximumCameraDirectionDotFocusForward > 1.0f ||
                rule.MinimumCameraDirectionDotFocusForward >
                    rule.MaximumCameraDirectionDotFocusForward)
                throw new InvalidOperationException(
                    "Gallery semantic-focus facing rule is invalid.");
        }
        if (string.IsNullOrWhiteSpace(Capture.Gallery.StillImageExtension) ||
            !Capture.Gallery.StillImageExtension.StartsWith(".", StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Gallery still-image extension must be explicit.");
        RequirePositive(
            Capture.Gallery.FramesPerSubject,
            nameof(Capture.Gallery.FramesPerSubject));
        RequirePositive(
            Capture.Gallery.FramesPerSecond,
            nameof(Capture.Gallery.FramesPerSecond));
        RequireUnitInterval(
            Capture.Gallery.MinimumMotionProgressFraction,
            nameof(Capture.Gallery.MinimumMotionProgressFraction));
        if (Capture.Gallery.MinimumMotionProgressFraction <= 0.0f)
            throw new InvalidOperationException(
                "Gallery minimum motion progress fraction must be greater than zero.");
        foreach (var extension in new[]
                 {
                     Capture.Gallery.Video.SourceContainerExtension,
                     Capture.Gallery.Video.DeliveryContainerExtension,
                 })
            if (string.IsNullOrWhiteSpace(extension) ||
                !extension.StartsWith(".", StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "Gallery video container extensions must be explicit extensions.");
        if (Path.GetFileName(Capture.Gallery.Video.DeliveryFileName) !=
                Capture.Gallery.Video.DeliveryFileName ||
            !Capture.Gallery.Video.DeliveryFileName.EndsWith(
                Capture.Gallery.Video.DeliveryContainerExtension,
                StringComparison.OrdinalIgnoreCase) ||
            Path.GetFileName(Capture.Gallery.Video.ReportFileName) !=
                Capture.Gallery.Video.ReportFileName)
            throw new InvalidOperationException(
                "Gallery video delivery artifact names must be explicit file names.");
        foreach (var value in new[]
                 {
                     Capture.Gallery.Video.VideoCodec,
                     Capture.Gallery.Video.PixelFormat,
                     Capture.Gallery.Video.EncoderPreset,
                 })
            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException(
                    "Gallery video encoder policy must be explicit.");
        if (Capture.Gallery.Video.ConstantRateFactor < 0 ||
            Capture.Gallery.Video.DurationToleranceFrames < 0)
            throw new InvalidOperationException(
                "Gallery video numeric policy must be nonnegative.");
        if (Capture.ActorShotKinds.Count < 1 ||
            Capture.ActorShotKinds.Any(string.IsNullOrWhiteSpace) ||
            Capture.ActorShotKinds.Distinct(StringComparer.Ordinal).Count() != Capture.ActorShotKinds.Count)
            throw new InvalidOperationException("Capture actor shot kinds must be unique and nonempty.");
        foreach (var shot in Capture.EnvironmentShots)
        {
            if (string.IsNullOrWhiteSpace(shot.Name) || string.IsNullOrWhiteSpace(shot.OutputFile))
                throw new InvalidOperationException("Capture shot names and output files must not be empty.");
            RequirePositive(shot.VerticalFovDegrees, $"{shot.Name} FOV");
            RequireVector(shot.CameraPositionMeters, 3, $"{shot.Name} camera position");
            RequireVector(shot.LookAtMeters, 3, $"{shot.Name} look target");
        }
        if (RetailActorState.RequiredShotKinds.Count < 1 ||
            !RetailActorState.RequiredShotKinds.SequenceEqual(Capture.ActorShotKinds, StringComparer.Ordinal))
            throw new InvalidOperationException("Retail actor-state and capture shot kinds must be identical.");
        RequirePositive(RetailActorState.MinimumContextActors, nameof(RetailActorState.MinimumContextActors));
        RequirePositive(RetailActorState.MinimumPoseBones, nameof(RetailActorState.MinimumPoseBones));
        RequirePositive(RetailActorState.MinimumArmBones, nameof(RetailActorState.MinimumArmBones));
        RequireUnitInterval(RetailActorState.FullSequenceWeight, nameof(RetailActorState.FullSequenceWeight));
        RequireUnitInterval(
            RetailActorState.MinimumContextSequenceWeight,
            nameof(RetailActorState.MinimumContextSequenceWeight));
        RequirePositive(
            RetailActorState.SequenceWeightTolerance,
            nameof(RetailActorState.SequenceWeightTolerance));
        RequirePositive(
            ActorParity.PoseTranslationToleranceMeters,
            nameof(ActorParity.PoseTranslationToleranceMeters));
        RequirePositive(
            ActorParity.PoseRotationToleranceRadians,
            nameof(ActorParity.PoseRotationToleranceRadians));
        RequirePositive(
            ActorParity.MaximumReportedWorstBones,
            nameof(ActorParity.MaximumReportedWorstBones));
        RequirePositive(
            ActorParity.GroundContactMaximumUlp,
            nameof(ActorParity.GroundContactMaximumUlp));
        RequirePositive(
            ActorParity.ChangedPixelChannelTolerance,
            nameof(ActorParity.ChangedPixelChannelTolerance));
        RequireUnitInterval(
            ActorParity.MaximumChangedPixelFraction,
            nameof(ActorParity.MaximumChangedPixelFraction));
        RequireColor(SetupView.BackgroundColorRgba, nameof(SetupView.BackgroundColorRgba));
        RequireColor(SetupView.StatusColorRgba, nameof(SetupView.StatusColorRgba));
        RequireVector(SetupView.ContentPositionPixels, 2, nameof(SetupView.ContentPositionPixels));
        RequireVector(SetupView.ContentSizePixels, 2, nameof(SetupView.ContentSizePixels));
        RequireUnitInterval(SetupView.DialogCenteredRatio, nameof(SetupView.DialogCenteredRatio));
        foreach (var text in SetupView.Copy.Values)
            RequireText(text, nameof(SetupView.Copy));
        RequirePositive(
            DesktopLauncher.MainWindowWidthPixels,
            nameof(DesktopLauncher.MainWindowWidthPixels));
        RequirePositive(
            DesktopLauncher.MainWindowHeightPixels,
            nameof(DesktopLauncher.MainWindowHeightPixels));
        RequirePositive(
            DesktopLauncher.MainWindowMinimumWidthPixels,
            nameof(DesktopLauncher.MainWindowMinimumWidthPixels));
        RequirePositive(
            DesktopLauncher.MainWindowMinimumHeightPixels,
            nameof(DesktopLauncher.MainWindowMinimumHeightPixels));
        RequirePositive(
            DesktopLauncher.ToastVisibilityMilliseconds,
            nameof(DesktopLauncher.ToastVisibilityMilliseconds));
        if (DesktopLauncher.MainWindowMinimumWidthPixels > DesktopLauncher.MainWindowWidthPixels ||
            DesktopLauncher.MainWindowMinimumHeightPixels > DesktopLauncher.MainWindowHeightPixels)
            throw new InvalidOperationException("Desktop launcher minimum dimensions exceed startup dimensions.");
        RequireText(LegalAssets.DefaultOpeningRecipe, nameof(LegalAssets.DefaultOpeningRecipe));
        RequireText(LegalAssets.DefaultCellRecipe, nameof(LegalAssets.DefaultCellRecipe));
        RequireText(
            LegalAssets.LinkedWorldProofCellRecipe,
            nameof(LegalAssets.LinkedWorldProofCellRecipe));
        RequireText(LegalAssets.DefaultCacheRoot, nameof(LegalAssets.DefaultCacheRoot));
        RequireText(LegalAssets.PackagedCompilerName, nameof(LegalAssets.PackagedCompilerName));
        RequireText(
            LegalAssets.SourceContentTool.Executable,
            nameof(LegalAssets.SourceContentTool.Executable));
        RequireText(
            LegalAssets.SourceContentTool.Script,
            nameof(LegalAssets.SourceContentTool.Script));
        RequireText(
            LegalAssets.SourceContentTool.CompilerName,
            nameof(LegalAssets.SourceContentTool.CompilerName));
        RequireText(LegalAssets.SmokeModelLogicalPath, nameof(LegalAssets.SmokeModelLogicalPath));
        LegalAssets.VideoImport.Validate();
        RequireText(LegalAssets.OwnedData.MasterFile, nameof(LegalAssets.OwnedData.MasterFile));
        RequireText(
            LegalAssets.OwnedData.DefaultIniFile,
            nameof(LegalAssets.OwnedData.DefaultIniFile));
        RequireText(
            LegalAssets.OwnedData.MeshesArchiveFile,
            nameof(LegalAssets.OwnedData.MeshesArchiveFile));
        RequireText(
            LegalAssets.OwnedData.UiArchiveFile,
            nameof(LegalAssets.OwnedData.UiArchiveFile));
        RequireText(
            LegalAssets.OwnedData.DataDirectoryName,
            nameof(LegalAssets.OwnedData.DataDirectoryName));
        RequireText(
            LegalAssets.OwnedData.VideoDirectoryName,
            nameof(LegalAssets.OwnedData.VideoDirectoryName));
        if (LegalAssets.OwnedData.TextureArchiveFiles.Count < 1 ||
            LegalAssets.OwnedData.TextureArchiveFiles.Any(string.IsNullOrWhiteSpace))
            throw new InvalidOperationException("Legal owned-data texture archives must be nonempty.");
        Tooling.Validate();
        RequirePositive(ContentCompiler.AssetIdHexCharacters, nameof(ContentCompiler.AssetIdHexCharacters));
        RequirePositive(ContentCompiler.StableIdHexCharacters, nameof(ContentCompiler.StableIdHexCharacters));
        RequirePositive(ContentCompiler.PngCompressionLevel, nameof(ContentCompiler.PngCompressionLevel));
        RequirePositive(
            ContentCompiler.AnimationSamplesPerSecond,
            nameof(ContentCompiler.AnimationSamplesPerSecond));
        RequirePositive(ContentCompiler.ZeroSpecularEpsilon, nameof(ContentCompiler.ZeroSpecularEpsilon));
        RequireUnitInterval(
            ContentCompiler.MinimumMaterialRoughness,
            nameof(ContentCompiler.MinimumMaterialRoughness));
        RequirePositive(
            ContentCompiler.DefaultMaterialGlossiness,
            nameof(ContentCompiler.DefaultMaterialGlossiness));
        RequirePositive(
            ContentCompiler.ExteriorCellSizeGameUnits,
            nameof(ContentCompiler.ExteriorCellSizeGameUnits));
        RequirePositive(ContentCompiler.LandscapeQuadrantPixels, nameof(ContentCompiler.LandscapeQuadrantPixels));
        RequirePositive(
            ContentCompiler.LandscapeTilesPerQuadrant,
            nameof(ContentCompiler.LandscapeTilesPerQuadrant));
        RequirePositive(
            ContentCompiler.LandscapeTileRepeatsPerCell,
            nameof(ContentCompiler.LandscapeTileRepeatsPerCell));
        ContentCompiler.SpeedTree.Validate();
        ContentCompiler.RetailGrass.Validate();
        var faceGenMaterial = ActorCompiler.FaceGenMaterial;
        if (faceGenMaterial.Schema != FaceGenMaterialConfiguration.ExpectedSchema)
            throw new InvalidOperationException("Actor FaceGen material schema is invalid.");
        if (faceGenMaterial.SourceSamplerSrgbTexture ||
            faceGenMaterial.SourceRenderTargetSrgbWrite)
            throw new InvalidOperationException(
                "Actor FaceGen material declares unsupported retail color-space state.");
        RequireUnitInterval(
            faceGenMaterial.SignedDetailNeutral,
            nameof(faceGenMaterial.SignedDetailNeutral));
        RequirePositive(
            faceGenMaterial.SignedDetailScale,
            nameof(faceGenMaterial.SignedDetailScale));
        RequirePositive(faceGenMaterial.ToneScale, nameof(faceGenMaterial.ToneScale));
        if (faceGenMaterial.ToneMapRgba.Length != RgbaChannelCount ||
            faceGenMaterial.ToneMapRgba.Any(channel =>
                channel < byte.MinValue || channel > byte.MaxValue))
            throw new InvalidOperationException(
                "Actor FaceGen tone map must contain four byte-range RGBA values.");
        if (string.IsNullOrWhiteSpace(faceGenMaterial.Source))
            throw new InvalidOperationException("Actor FaceGen material source must not be empty.");
        var transfer = faceGenMaterial.RuntimeAlbedoTransfer;
        if (transfer.Schema != ColorTransferConfiguration.ExpectedSchema)
            throw new InvalidOperationException("Actor FaceGen albedo transfer schema is invalid.");
        RequirePositive(transfer.EncodedCutoff, nameof(transfer.EncodedCutoff));
        RequirePositive(transfer.LinearScale, nameof(transfer.LinearScale));
        RequirePositive(transfer.Offset, nameof(transfer.Offset));
        RequirePositive(transfer.Normalization, nameof(transfer.Normalization));
        RequirePositive(transfer.Exponent, nameof(transfer.Exponent));
        if (string.IsNullOrWhiteSpace(transfer.Source))
            throw new InvalidOperationException("Actor FaceGen albedo transfer source must not be empty.");
        ActorCompiler.FaceGenAnimation.Validate();
        ActorCompiler.AnimationProfiles.Validate();
        ActorCompiler.RigidAttachment.Validate();
    }

    private static void RequirePositive(float value, string name)
    {
        if (!float.IsFinite(value) || value <= 0.0f)
            throw new InvalidOperationException($"Runtime configuration {name} must be positive.");
    }

    private static void RequireText(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"Runtime configuration {name} must not be empty.");
    }

    private static void RequirePositive(double value, string name)
    {
        if (!double.IsFinite(value) || value <= 0.0)
            throw new InvalidOperationException($"Runtime configuration {name} must be positive.");
    }

    private static void RequirePositive(int value, string name)
    {
        if (value <= 0)
            throw new InvalidOperationException($"Runtime configuration {name} must be positive.");
    }

    private static void RequireUnitInterval(float value, string name)
    {
        if (!float.IsFinite(value) || value < 0.0f || value > 1.0f)
            throw new InvalidOperationException($"Runtime configuration {name} must be in [0, 1].");
    }

    private static void RequireVector(IReadOnlyCollection<float> values, int count, string name)
    {
        if (values.Count != count || values.Any(value => !float.IsFinite(value)))
            throw new InvalidOperationException($"Runtime configuration {name} must contain {count} finite values.");
    }

    private static void RequireColor(IReadOnlyCollection<float> values, string name)
    {
        RequireVector(values, 4, name);
        if (values.Any(value => value < 0.0f || value > 1.0f))
            throw new InvalidOperationException($"Runtime configuration {name} must contain normalized RGBA values.");
    }
}
