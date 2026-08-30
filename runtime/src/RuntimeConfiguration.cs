using System.Globalization;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;

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

internal sealed record ConfigurationProvenance(
    string Classification,
    string Status,
    string Source,
    string Evidence)
{
    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Classification) || string.IsNullOrWhiteSpace(Status) ||
            string.IsNullOrWhiteSpace(Source) || string.IsNullOrWhiteSpace(Evidence))
            throw new InvalidOperationException("Every runtime configuration section requires complete provenance.");
    }
}

internal sealed record WorldConfiguration(
    ConfigurationProvenance Provenance,
    float GameUnitsToMeters);

internal sealed record SimulationConfiguration(
    ConfigurationProvenance Provenance,
    int PhysicsTicksPerSecond,
    float GravityMetersPerSecondSquared);

internal sealed record RuntimePerformanceConfiguration(
    ConfigurationProvenance Provenance,
    double SampleIntervalSeconds);

internal sealed record RendererConfiguration(
    ConfigurationProvenance Provenance,
    float[] BackgroundColorRgba,
    string ToneMapper,
    float FogLightEnergy,
    float FogDensity,
    float AmbientEnergyScale,
    float DirectionalEnergyScale,
    float PointLightEnergyScale,
    float MinimumPointLightEnergy,
    bool AuthoredPointLightShadows,
    float[] NeutralNormalColorRgba,
    int[] NeutralNormalTextureSizePixels,
    float DefaultMetallic,
    float EmissionEnergyMultiplier,
    float EnvironmentNormalDecodeScale,
    float EnvironmentNormalDecodeBias,
    float EnvironmentReflectionHomogeneousW,
    float EnvironmentOpaqueAlpha,
    int CubemapFaceCount);

internal sealed record PlayerConfiguration(
    ConfigurationProvenance Provenance,
    float SpawnCenterHeightMeters,
    float CapsuleRadiusMeters,
    float CapsuleHeightMeters,
    float MoveSpeedMetersPerSecond,
    float MouseSensitivityRadiansPerPixel,
    float VerticalLookLimitRadians,
    float ActivationDistanceMeters,
    float FireRayDistanceMeters,
    float[] DesktopCameraOffsetMeters,
    float CameraNearMeters,
    float CameraFarMeters,
    uint CollisionLayer,
    uint CollisionMask,
    DesktopInputConfiguration DesktopInput);

internal sealed record DesktopInputConfiguration(
    ConfigurationProvenance Provenance,
    DesktopKeyBindingConfiguration MoveLeft,
    DesktopKeyBindingConfiguration MoveRight,
    DesktopKeyBindingConfiguration MoveForward,
    DesktopKeyBindingConfiguration MoveBackward,
    DesktopKeyBindingConfiguration Activate,
    DesktopKeyBindingConfiguration Grab,
    DesktopKeyBindingConfiguration Reload,
    DesktopKeyBindingConfiguration Save,
    DesktopKeyBindingConfiguration Cancel,
    DesktopKeyBindingConfiguration PipBoy,
    DesktopMouseBindingConfiguration Fire,
    DesktopMouseBindingConfiguration CaptureMouse,
    DesktopMouseBindingConfiguration PoolPowerUp,
    DesktopMouseBindingConfiguration PoolPowerDown,
    DesktopInputAcceptanceConfiguration Acceptance)
{
    internal IEnumerable<DesktopKeyBindingConfiguration> KeyBindings
    {
        get
        {
            yield return MoveLeft;
            yield return MoveRight;
            yield return MoveForward;
            yield return MoveBackward;
            yield return Activate;
            yield return Grab;
            yield return Reload;
            yield return Save;
            yield return Cancel;
            yield return PipBoy;
        }
    }

    internal IEnumerable<DesktopMouseBindingConfiguration> MouseBindings
    {
        get
        {
            yield return Fire;
            yield return CaptureMouse;
            yield return PoolPowerUp;
            yield return PoolPowerDown;
        }
    }

    internal void Validate()
    {
        var actions = KeyBindings.Select(binding => binding.Action)
            .Concat(MouseBindings.Select(binding => binding.Action))
            .ToArray();
        if (actions.Any(string.IsNullOrWhiteSpace) ||
            actions.Distinct(StringComparer.Ordinal).Count() != actions.Length)
            throw new InvalidOperationException("Desktop input actions must be nonempty and unique.");
        foreach (var binding in KeyBindings)
        {
            if (!Enum.TryParse<Key>(binding.PhysicalKey, true, out var key) || key == Key.None)
                throw new InvalidOperationException(
                    $"Unsupported desktop physical-key binding: {binding.PhysicalKey}");
        }
        foreach (var binding in MouseBindings)
        {
            if (!Enum.TryParse<MouseButton>(binding.Button, true, out var button) ||
                button == MouseButton.None)
                throw new InvalidOperationException(
                    $"Unsupported desktop mouse binding: {binding.Button}");
        }
        if (Acceptance.SettleFrames <= 0 || Acceptance.MovementFrames <= 0 ||
            Acceptance.RenderedFramesBeforeScreenshot <= 0 ||
            !float.IsFinite(Acceptance.MinimumLocomotionMeters) ||
            Acceptance.MinimumLocomotionMeters <= 0.0f ||
            !float.IsFinite(Acceptance.MinimumLookRadians) ||
            Acceptance.MinimumLookRadians <= 0.0f)
            throw new InvalidOperationException("Desktop input acceptance values must be positive.");
    }
}

internal sealed record PickupConfiguration(
    ConfigurationProvenance Provenance,
    float HoldDistanceMeters,
    uint CollisionLayer,
    uint CollisionMask);

internal sealed record DesktopKeyBindingConfiguration(
    string Action,
    string PhysicalKey);

internal sealed record DesktopMouseBindingConfiguration(
    string Action,
    string Button);

internal sealed record DesktopInputAcceptanceConfiguration(
    int SettleFrames,
    int MovementFrames,
    float MinimumLocomotionMeters,
    float MinimumLookRadians,
    int RenderedFramesBeforeScreenshot);

internal sealed record XrConfiguration(
    ConfigurationProvenance Provenance,
    float WorldScale,
    float OriginYOffsetMeters,
    float DesiredEyeHeightMeters,
    int EyeHeightCalibrationTrackedFrames,
    int InputHealthReportFrames,
    float MovementDeadzone,
    float ActionThreshold,
    float SnapTurnActivationThreshold,
    float SnapTurnResetThreshold,
    float SnapTurnDegrees,
    float HandAlignmentPositionToleranceMeters,
    float HandAlignmentRotationToleranceRadians,
    float WeaponFeedbackSeconds,
    float WeaponRecoilMeters,
    float MuzzleFlashVisibleSeconds,
    HapticConfiguration FireHaptic,
    HapticConfiguration ReloadHaptic,
    XrContractConfiguration Contract,
    XrSimulatorAcceptanceConfiguration SimulatorAcceptance,
    XrDiagnosticRigProofConfiguration DiagnosticRigProof,
    DiagnosticMuzzleFlashConfiguration DiagnosticMuzzleFlash);

internal sealed record HapticConfiguration(
    double Frequency,
    double Amplitude,
    double DurationSeconds,
    double DelaySeconds);

internal sealed record PoolConfiguration(
    ConfigurationProvenance Provenance,
    IReadOnlyList<float> FlatStrikeSpeedsMetersPerSecond,
    float[] DesktopCueMountPositionMeters,
    float[] DesktopCueMountRotationDegrees,
    float[] XrCueMountPositionMeters,
    float[] XrCueMountRotationDegrees,
    float XrMinimumTipSpeedMetersPerSecond,
    float XrMaximumTipSpeedMetersPerSecond,
    float XrStrikeCooldownSeconds,
    float XrImpulseScale,
    int MaximumReportedContacts,
    int ProofMaximumPhysicsFrames,
    uint CollisionLayer,
    uint CollisionMask,
    string ResetStatusText,
    HapticConfiguration StrikeHaptic);

internal sealed record XrContractConfiguration(
    ConfigurationProvenance Provenance,
    string ActionMapResourcePath,
    int ExpectedActionSetCount,
    IReadOnlyList<string> ActionNames,
    IReadOnlyList<string> InteractionProfilePaths);

internal sealed record XrSimulatorAcceptanceConfiguration(
    ConfigurationProvenance Provenance,
    int TimeoutFrames,
    int MinimumTrackedFrames,
    float MinimumLocomotionMeters,
    float MinimumHandTravelMeters,
    float EyeHeightToleranceMeters,
    float MaximumSnapPivotErrorMeters,
    float FloorProbeAboveEyeMeters,
    float FloorProbeDistanceMeters,
    int MinimumSnapTurns,
    int MinimumAcceptedActivations,
    int MinimumAcceptedFireActions,
    int MinimumAcceptedReloadActions,
    int MinimumSaveActions);

internal sealed record XrDiagnosticRigProofConfiguration(
    ConfigurationProvenance Provenance,
    string SessionId,
    string WeaponFormId,
    string WeaponEditorId,
    string AmmoFormId,
    string AmmoEditorId,
    int Damage,
    int ClipSize,
    int ReserveRounds,
    int ExpectedShotsFired,
    int ExpectedAmmoInMagazineAfterReload,
    int ExpectedReserveRoundsAfterReload);

internal sealed record DiagnosticMuzzleFlashConfiguration(
    ConfigurationProvenance Provenance,
    float SphereRadiusGameUnits,
    float SphereHeightGameUnits,
    float[] AlbedoColorRgba,
    float[] EmissionColorRgba,
    float EmissionEnergy,
    float[] LightColorRgba,
    float LightEnergy,
    float LightRangeGameUnits);

internal sealed record DoorConfiguration(
    ConfigurationProvenance Provenance,
    float OpenAngleDegrees);

internal sealed record HudConfiguration(
    ConfigurationProvenance Provenance,
    float[] DesktopPanelPositionPixels,
    float[] DesktopPanelSizePixels,
    float[] DesktopPanelColorRgba,
    float[] DesktopLabelsPositionPixels,
    float[] DesktopLabelsSizePixels,
    float[] TextColorRgba,
    int DesktopFontSizePixels,
    float[] CrosshairPositionPixels,
    int CrosshairFontSizePixels,
    float[] XrMountPositionMeters,
    float[] XrMountRotationDegrees,
    int XrFontSizePixels,
    float XrPixelSizeMeters,
    int XrOutlineSizePixels,
    int XrMaximumStatusCharacters,
    float[] PipBoyPanelPositionPixels,
    float[] PipBoyPanelSizePixels,
    string DefaultSavePath,
    HudCopyConfiguration Copy,
    PipBoyCopyConfiguration PipBoy);

internal sealed record HudCopyConfiguration(
    string ObjectiveEquipWeapon,
    string ObjectiveFireWeapon,
    string ObjectiveTakeAid,
    string ObjectiveOpenEntryDoor,
    string ObjectiveComplete,
    string InventoryPrefix,
    string EmptyInventory)
{
    internal IEnumerable<string> Values =>
    [
        ObjectiveEquipWeapon,
        ObjectiveFireWeapon,
        ObjectiveTakeAid,
        ObjectiveOpenEntryDoor,
        ObjectiveComplete,
        InventoryPrefix,
        EmptyInventory,
    ];
}

internal sealed record PipBoyCopyConfiguration(
    string Title,
    string StatusTab,
    string ItemsTab,
    string DataTab,
    string MapTab,
    string ControlsTab,
    string EmptyInventory,
    string EmptyQuests,
    string EmptyMap,
    string CloseHint)
{
    internal IEnumerable<string> Values =>
    [
        Title,
        StatusTab,
        ItemsTab,
        DataTab,
        MapTab,
        ControlsTab,
        EmptyInventory,
        EmptyQuests,
        EmptyMap,
        CloseHint,
    ];
}

internal sealed record CaptureConfiguration(
    ConfigurationProvenance Provenance,
    int RenderedFramesBeforeCapture,
    int ActorRenderedFramesBeforeCapture,
    double MinimumMeanLuminance,
    double ActorMinimumMeanLuminance,
    double DarkPixelLuminance,
    double MinimumLuminanceDeviation,
    double MaximumDarkPixelFraction,
    int ExpectedWidthPixels,
    int ExpectedHeightPixels,
    int RgbaChannelCount,
    double PixelChannelMaximum,
    float[] LuminanceWeightsRgb,
    IReadOnlyList<string> ActorShotKinds,
    GalleryCaptureConfiguration Gallery,
    IReadOnlyList<EnvironmentShotConfiguration> EnvironmentShots);

internal sealed record GalleryCaptureConfiguration(
    ConfigurationProvenance Provenance,
    GalleryPresentationSelectionConfiguration RetailPresentationSelection,
    float VerticalFovDegrees,
    float MaximumFrameOccupancy,
    string TargetNodeRole,
    string FacingPoseSource,
    string OcclusionClearanceSource,
    string ModelFrontAxis,
    string StillImageExtension,
    int FramesPerSubject,
    int FramesPerSecond,
    float MinimumMotionProgressFraction,
    GalleryVideoConfiguration Video)
{
    internal float DurationSeconds => (float)FramesPerSubject / FramesPerSecond;
}

internal sealed record GalleryPresentationSelectionConfiguration(
    string Schema,
    IReadOnlyList<string> CandidateShotKinds,
    IReadOnlyList<GallerySemanticFocusFacingRule> SemanticFocusFacingRules,
    string RequiredSurfaceStatus,
    bool RequireSemanticFocusSurface,
    bool RequireCameraOutsideActorWorldBound,
    bool RequireClearCameraCorridor,
    float CameraTranslationToleranceGameUnits,
    string TieBreak);

internal sealed record GallerySemanticFocusFacingRule(
    string FocusKind,
    IReadOnlyList<string> AllowedShotKinds,
    float MinimumCameraDirectionDotFocusForward,
    float MaximumCameraDirectionDotFocusForward);

internal sealed record GalleryVideoConfiguration(
    ConfigurationProvenance Provenance,
    string SourceContainerExtension,
    string DeliveryContainerExtension,
    string DeliveryFileName,
    string ReportFileName,
    string VideoCodec,
    string PixelFormat,
    int ConstantRateFactor,
    string EncoderPreset,
    int DurationToleranceFrames);

internal sealed record EnvironmentShotConfiguration(
    string Name,
    string OutputFile,
    bool OpenProofDoorBeforeShot,
    float VerticalFovDegrees,
    float[] CameraPositionMeters,
    float[] LookAtMeters);

internal sealed record ProofConfiguration(
    ConfigurationProvenance Provenance,
    float PortalAlignmentToleranceMeters,
    float PortalNormalAgreementMinimum,
    float SpawnFloorToleranceMeters,
    float SpawnFloorRayStartMeters,
    float SpawnFloorRayEndMeters,
    float DoorRayThicknessMultiplier,
    float DoorRayMinimumReachGameUnits,
    float ProjectileRayStartMeters,
    float ProjectileRayEndMeters,
    float PortalCapsuleCenterHeightMeters,
    float PortalCapsuleMotionMeters,
    float WalkableSurfaceNormalYMinimum,
    GameplayRouteProofConfiguration GameplayRoute);

internal sealed record GameplayRouteProofConfiguration(
    ConfigurationProvenance Provenance,
    string WeaponPickupFormId,
    string AidPickupEditorId,
    string ContainerEditorId,
    int ExpectedShotsFired,
    int ExpectedAmmoInMagazine,
    int ExpectedEmptiedContainers,
    int ExpectedOpenDoors,
    string ExpectedInventoryItemFormId,
    string ExpectedContainerReferenceFormId);

internal sealed record DiagnosticPreviewConfiguration(
    ConfigurationProvenance Provenance,
    float[] BackgroundColorRgba,
    float[] AmbientColorRgba,
    float AmbientEnergy,
    float[] LightRotationDegrees,
    float LightEnergy,
    float[] CameraOffsetExtentMultipliers,
    float MinimumNearMeters,
    float NearExtentDivisor,
    float MinimumFarMeters,
    float FarExtentMultiplier,
    float ActorMinimumHeightMeters,
    float ActorMaximumHeightMeters);

internal sealed record ActorReviewConfiguration(
    ConfigurationProvenance Provenance,
    float ProjectionAspectTolerance,
    float CameraBasisTolerance,
    float ProjectedBoneTolerancePixels,
    float SkinPaletteLinearTolerance,
    float SkinPaletteTranslationToleranceGameUnits,
    float[] DirectionalRotationDegrees,
    bool DirectionalShadows);

internal sealed record ExteriorEnvironmentConfiguration(
    ConfigurationProvenance Provenance,
    string Mode,
    float[] AmbientColor,
    float[] DirectionalColor,
    float[] FogColor,
    float FogNearGameUnits,
    float FogFarGameUnits,
    float FogPower,
    float[] DirectionalRotationDegrees,
    float DirectionalFade);

internal sealed record FalloutEnvironmentConfiguration(
    ConfigurationProvenance Provenance,
    float CloudSpeedDivisor,
    int SkyRgbMultiplierImageSpaceTraitIndex,
    int AtmosphereRenderPriority,
    int CloudRenderPriority,
    RetailImageSpaceConfiguration ImageSpace);

internal sealed record RetailImageSpaceConfiguration(
    ConfigurationProvenance Provenance,
    string Schema,
    IReadOnlyList<ImageSpaceModifierChannelConfiguration> ModifierChannels,
    ImageSpaceTraitIndexConfiguration TraitIndices,
    float[] LuminanceWeightsRgb,
    string ShaderPath,
    int ShaderByteCount,
    string ShaderFnv1a32,
    int ShaderRegisterComponents,
    int HdrParametersRegister,
    int CinematicRegister,
    int TintRegister,
    int FadeRegister,
    float ShaderConstantTolerance,
    RetailHdrBlendConfiguration HdrBlend,
    int CanvasLayer)
{
    internal const string ExpectedSchema = "opennv-retail-image-space-composition/v2";
    private const int D3D9FloatRegisterComponents = 4;

    internal void Validate()
    {
        if (Schema != ExpectedSchema)
            throw new InvalidOperationException("Unexpected Fallout image-space configuration schema.");
        if (ModifierChannels.Count < 1 ||
            ModifierChannels.Any(channel =>
                string.IsNullOrWhiteSpace(channel.Name) || channel.TraitIndex < 0) ||
            ModifierChannels.Select(channel => channel.Name)
                .Distinct(StringComparer.Ordinal).Count() != ModifierChannels.Count ||
            ModifierChannels.Select(channel => channel.TraitIndex).Distinct().Count() !=
                ModifierChannels.Count)
            throw new InvalidOperationException(
                "Fallout image-space modifier channels must be unique and nonempty.");
        TraitIndices.Validate();
        if (LuminanceWeightsRgb.Length != 3 ||
            LuminanceWeightsRgb.Any(value => !float.IsFinite(value) || value < 0.0f) ||
            MathF.Abs(LuminanceWeightsRgb.Sum() - 1.0f) > ShaderConstantTolerance)
            throw new InvalidOperationException(
                "Fallout cinematic luminance weights must be normalized and nonnegative.");
        if (string.IsNullOrWhiteSpace(ShaderPath) || ShaderByteCount <= 0 ||
            string.IsNullOrWhiteSpace(ShaderFnv1a32) ||
            ShaderRegisterComponents != D3D9FloatRegisterComponents ||
            HdrParametersRegister < 0 || CinematicRegister < 0 || TintRegister < 0 ||
            FadeRegister < 0 || ShaderConstantTolerance <= 0.0f)
            throw new InvalidOperationException(
                "Fallout retail image-space shader evidence configuration is incomplete.");
        var registers = new[]
        {
            HdrParametersRegister,
            CinematicRegister,
            TintRegister,
            FadeRegister,
        };
        if (registers.Distinct().Count() != registers.Length)
            throw new InvalidOperationException(
                "Fallout retail image-space shader registers must be unique.");
        HdrBlend.Validate();
    }
}

internal sealed record RetailHdrBlendConfiguration(
    int BlurredAdaptationStage,
    int HdrSceneStage,
    uint D3D9ResourceType,
    uint D3D9SurfaceType,
    uint D3D9Usage,
    uint D3D9Pool,
    uint D3D9MultiSampleType,
    uint D3D9MultiSampleQuality,
    int LevelCount,
    uint D3D9TextureFormat,
    string D3D9TextureFormatName,
    int ComponentCount,
    int ComponentBytes,
    int WorkGroupSidePixels,
    int ReadbackTimeoutSeconds,
    RetailHdrTargetConfiguration Targets,
    float AdaptationDeltaSeconds,
    float AdaptationRetentionBase,
    float MinimumAdaptationMagnitude,
    float BrightThreshold,
    float BrightScale,
    IReadOnlyList<float> BlurWeights,
    float BloomNormalizationScale,
    bool SamplerSrgbEnabled,
    bool RenderTargetSrgbWriteEnabled,
    string OutputTransfer,
    string SamplerFilter)
{
    internal void Validate()
    {
        if (BlurredAdaptationStage < 0 || HdrSceneStage < 0 ||
            BlurredAdaptationStage == HdrSceneStage || D3D9ResourceType == 0 ||
            D3D9SurfaceType == 0 || LevelCount <= 0 || D3D9TextureFormat == 0 ||
            string.IsNullOrWhiteSpace(D3D9TextureFormatName) ||
            ComponentCount <= 0 || ComponentBytes <= 0 ||
            WorkGroupSidePixels <= 0 || ReadbackTimeoutSeconds <= 0 ||
            !float.IsFinite(AdaptationDeltaSeconds) || AdaptationDeltaSeconds <= 0.0f ||
            !float.IsFinite(AdaptationRetentionBase) ||
            AdaptationRetentionBase <= 0.0f || AdaptationRetentionBase > 1.0f ||
            !float.IsFinite(MinimumAdaptationMagnitude) ||
            MinimumAdaptationMagnitude <= 0.0f ||
            !float.IsFinite(BrightThreshold) || BrightThreshold <= 0.0f ||
            !float.IsFinite(BrightScale) || BrightScale <= 0.0f ||
            BlurWeights.Count < 1 || BlurWeights.Count % 2 == 0 ||
            BlurWeights.Any(value => !float.IsFinite(value) || value <= 0.0f) ||
            !float.IsFinite(BloomNormalizationScale) || BloomNormalizationScale <= 0.0f ||
            !OutputTransfer.Equals("linear", StringComparison.Ordinal) ||
            !SamplerFilter.Equals("linear", StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Fallout HDR blend configuration is incomplete.");
        Targets.Validate();
    }

    internal IReadOnlyList<int> InputStages =>
        [BlurredAdaptationStage, HdrSceneStage];
}

internal sealed record RetailHdrTargetConfiguration(
    int[] HalfPixels,
    int[] SourcePixels,
    IReadOnlyList<int[]> DownsamplePixels,
    int[] AdaptationPixels,
    int[] BrightPixels,
    int[] BloomPixels)
{
    internal void Validate()
    {
        var targets = new[]
        {
            HalfPixels,
            SourcePixels,
            AdaptationPixels,
            BrightPixels,
            BloomPixels,
        }.Concat(DownsamplePixels);
        if (DownsamplePixels.Count < 1 || targets.Any(target =>
                target.Length != 2 || target.Any(value => value <= 0)))
            throw new InvalidOperationException(
                "Fallout HDR target dimensions must contain positive pixel pairs.");
    }
}

internal sealed record ImageSpaceModifierChannelConfiguration(string Name, int TraitIndex);

internal sealed record ImageSpaceTraitIndexConfiguration(
    int TargetLuminance,
    int SunlightDimmer,
    int SkinDimmer,
    int CinematicSaturation,
    int CinematicContrastAverageLuminance,
    int CinematicContrast,
    int CinematicBrightness,
    int CinematicTintRed,
    int CinematicTintGreen,
    int CinematicTintBlue,
    int CinematicTintStrength)
{
    internal IEnumerable<int> Values() =>
    [
        TargetLuminance,
        SunlightDimmer,
        SkinDimmer,
        CinematicSaturation,
        CinematicContrastAverageLuminance,
        CinematicContrast,
        CinematicBrightness,
        CinematicTintRed,
        CinematicTintGreen,
        CinematicTintBlue,
        CinematicTintStrength,
    ];

    internal void Validate()
    {
        var indices = Values().ToArray();
        if (indices.Any(index => index < 0) || indices.Distinct().Count() != indices.Length)
            throw new InvalidOperationException(
                "Fallout image-space trait indices must be unique and nonnegative.");
    }
}

internal sealed record RetailActorStateConfiguration(
    ConfigurationProvenance Provenance,
    IReadOnlyList<string> RequiredShotKinds,
    float FullSequenceWeight,
    float SequenceWeightTolerance,
    float MinimumContextSequenceWeight,
    int MinimumContextActors,
    int MinimumPoseBones,
    int MinimumArmBones,
    IReadOnlyList<string> ExcludedPoseNodes);

internal sealed record ActorParityConfiguration(
    ConfigurationProvenance Provenance,
    float PoseTranslationToleranceMeters,
    float PoseRotationToleranceRadians,
    int MaximumReportedWorstBones,
    float PlacementToleranceGameUnits,
    int GroundContactMaximumUlp,
    float YawToleranceRadians,
    float CameraPositionToleranceGameUnits,
    float CameraAimToleranceGameUnits,
    float CameraDistanceToleranceMeters,
    float VerticalFovToleranceDegrees,
    float AnimationPhaseToleranceSeconds,
    int ChangedPixelChannelTolerance,
    float MaximumMeanAbsoluteError,
    float MaximumChangedPixelFraction,
    float MaximumMeanLuminanceDelta,
    ActorParityContactSheetConfiguration ContactSheet);

internal sealed record ActorParityContactSheetConfiguration(
    int HeaderPixels,
    int[] BackgroundRgb,
    int TitleFontPixels,
    int DetailFontPixels,
    int TextMarginXPixels,
    int TitleYPixels,
    int DetailYPixels,
    int[] RetailTitleRgb,
    int[] GodotTitleRgb,
    int[] DetailRgb);

internal sealed record SetupViewConfiguration(
    ConfigurationProvenance Provenance,
    float[] BackgroundColorRgba,
    float[] ContentPositionPixels,
    float[] ContentSizePixels,
    int TitleFontSizePixels,
    int BodyFontSizePixels,
    float ButtonMinimumHeightPixels,
    float StatusMinimumHeightPixels,
    float[] StatusColorRgba,
    int StatusFontSizePixels,
    float DialogCenteredRatio,
    SetupViewCopyConfiguration Copy);

internal sealed record SetupViewCopyConfiguration(
    string Title,
    string Body,
    string SelectButton,
    string WaitingStatus,
    string RebuildStatusPrefix,
    string DialogTitle)
{
    internal IEnumerable<string> Values =>
    [Title, Body, SelectButton, WaitingStatus, RebuildStatusPrefix, DialogTitle];
}

internal sealed record DesktopLauncherConfiguration(
    ConfigurationProvenance Provenance,
    int MainWindowWidthPixels,
    int MainWindowHeightPixels,
    int MainWindowMinimumWidthPixels,
    int MainWindowMinimumHeightPixels,
    int ToastVisibilityMilliseconds);

internal sealed record LegalAssetsConfiguration(
    ConfigurationProvenance Provenance,
    string DefaultOpeningRecipe,
    string DefaultCellRecipe,
    string LinkedWorldProofCellRecipe,
    string DefaultCacheRoot,
    string PackagedCompilerName,
    SourceContentToolConfiguration SourceContentTool,
    string SmokeModelLogicalPath,
    OpeningVideoImportConfiguration VideoImport,
    LegalOwnedDataConfiguration OwnedData);

internal sealed record SourceContentToolConfiguration(
    string Executable,
    string Script,
    string CompilerName);

internal sealed record OpeningVideoImportConfiguration(
    string TranscoderExecutable,
    string OutputExtension,
    string ContainerFormat,
    string VideoCodec,
    string AudioCodec,
    int VideoQuality,
    int AudioQuality,
    int Threads,
    string PixelFormat,
    string LogLevel)
{
    internal void Validate()
    {
        foreach (var value in new[]
        {
            TranscoderExecutable,
            OutputExtension,
            ContainerFormat,
            VideoCodec,
            AudioCodec,
            PixelFormat,
            LogLevel,
        })
            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException("Opening video-import strings must be nonempty.");
        if (VideoQuality <= 0 || AudioQuality <= 0 || Threads <= 0)
            throw new InvalidOperationException("Opening video-import quality values must be positive.");
    }
}

internal sealed record LegalOwnedDataConfiguration(
    string MasterFile,
    string DefaultIniFile,
    string MeshesArchiveFile,
    string UiArchiveFile,
    IReadOnlyList<string> TextureArchiveFiles,
    string DataDirectoryName,
    string VideoDirectoryName);

internal sealed record ToolingConfiguration(
    ConfigurationProvenance Provenance,
    IReadOnlyDictionary<string, string> RecipeFiles)
{
    internal void Validate()
    {
        if (RecipeFiles.Count < 1 ||
            RecipeFiles.Any(row =>
                string.IsNullOrWhiteSpace(row.Key) ||
                string.IsNullOrWhiteSpace(row.Value) ||
                Path.GetFileName(row.Value) != row.Value))
            throw new InvalidOperationException(
                "Tooling recipe registry must contain nonempty file names.");
    }
}

internal sealed record ContentCompilerConfiguration(
    ConfigurationProvenance Provenance,
    int AssetIdHexCharacters,
    int StableIdHexCharacters,
    int PngCompressionLevel,
    float AnimationSamplesPerSecond,
    float ZeroSpecularEpsilon,
    float MinimumMaterialRoughness,
    float DefaultMaterialGlossiness,
    float ExteriorCellSizeGameUnits,
    int LandscapeQuadrantPixels,
    int LandscapeTilesPerQuadrant,
    int LandscapeTileRepeatsPerCell,
    SpeedTreeCompilerConfiguration SpeedTree,
    RetailGrassCompilerConfiguration RetailGrass,
    IReadOnlyList<string> NonPresentationBaseFormIds);

internal sealed record SpeedTreeCompilerConfiguration(
    ConfigurationProvenance Provenance,
    string BillboardTexture,
    float BillboardAlphaCutoff)
{
    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(BillboardTexture))
            throw new InvalidOperationException("SpeedTree billboard texture must not be empty.");
        if (!float.IsFinite(BillboardAlphaCutoff) ||
            BillboardAlphaCutoff <= 0.0f || BillboardAlphaCutoff > 1.0f)
            throw new InvalidOperationException(
                "SpeedTree billboard alpha cutoff must be in (0, 1].");
    }
}

internal sealed record RetailGrassCompilerConfiguration(
    ConfigurationProvenance Provenance,
    string Schema,
    string MaterialSchema,
    string MaterialModel,
    RetailGrassMaterialConfiguration Material,
    RetailGrassTextureConfiguration Texture,
    RetailGrassShaderConfiguration Shader,
    RetailGrassDrawConfiguration Draw,
    RetailGrassCaptureConfiguration Capture,
    RetailGrassReconstructionConfiguration Reconstruction,
    IReadOnlyList<RetailGrassMeshConfiguration> Meshes)
{
    internal const string ExpectedSchema = "opennv-retail-grass-compiler-contract/v1";

    internal void Validate()
    {
        if (Schema != ExpectedSchema || string.IsNullOrWhiteSpace(MaterialSchema) ||
            string.IsNullOrWhiteSpace(MaterialModel) || Meshes.Count < 1 ||
            Meshes.Select(mesh => mesh.Suffix).Distinct(StringComparer.Ordinal).Count() !=
                Meshes.Count ||
            Meshes.Any(mesh =>
                string.IsNullOrWhiteSpace(mesh.Suffix) || string.IsNullOrWhiteSpace(mesh.Path) ||
                string.IsNullOrWhiteSpace(mesh.Sha256) || mesh.SourceVertices <= 0 ||
                mesh.StripLength <= 0))
            throw new InvalidOperationException(
                "Retail grass compiler contract is incomplete.");
        Texture.Validate();
        Material.Validate();
        Shader.Validate();
        Draw.Validate();
        Capture.Validate();
        Reconstruction.Validate();
    }
}

internal sealed record RetailGrassMaterialConfiguration(
    string AlphaMode,
    string DiffuseDomain,
    string Sampler,
    string VertexLightingBake,
    string WindBake,
    int TextureClampMode,
    bool DoubleSided,
    bool Unshaded)
{
    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(AlphaMode) ||
            string.IsNullOrWhiteSpace(DiffuseDomain) ||
            string.IsNullOrWhiteSpace(Sampler) ||
            string.IsNullOrWhiteSpace(VertexLightingBake) ||
            string.IsNullOrWhiteSpace(WindBake) || TextureClampMode < 0)
            throw new InvalidOperationException(
                "Retail grass material contract is incomplete.");
    }
}

internal sealed record RetailGrassTextureConfiguration(
    string Path,
    string Fnv1a32,
    string TopLevelFnv1a32,
    int WidthPixels,
    int HeightPixels,
    int LevelCount,
    uint D3d9Format)
{
    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Path) || !RetailGrassHash.TryParse(Fnv1a32, out _) ||
            !RetailGrassHash.TryParse(TopLevelFnv1a32, out _) || WidthPixels <= 0 ||
            HeightPixels <= 0 || LevelCount <= 0 || D3d9Format == 0u)
            throw new InvalidOperationException("Retail grass texture contract is incomplete.");
    }
}

internal sealed record RetailGrassShaderConfiguration(
    string VertexFnv1a32,
    string PixelFnv1a32,
    int InstanceFirstRegister,
    int InstanceCapacity,
    int VertexConstantRegisterCount,
    int PixelConstantRegisterCount,
    float InstanceRegisterCeiling,
    float FloatTolerance,
    RetailGrassRegisterConfiguration Registers)
{
    internal void Validate()
    {
        if (!RetailGrassHash.TryParse(VertexFnv1a32, out _) ||
            !RetailGrassHash.TryParse(PixelFnv1a32, out _) ||
            InstanceFirstRegister < 0 || InstanceCapacity <= 0 ||
            VertexConstantRegisterCount <= 0 || PixelConstantRegisterCount <= 0 ||
            !float.IsFinite(InstanceRegisterCeiling) || InstanceRegisterCeiling <= 0.0f ||
            !float.IsFinite(FloatTolerance) || FloatTolerance <= 0.0f)
            throw new InvalidOperationException("Retail grass shader contract is incomplete.");
    }
}

internal sealed record RetailGrassRegisterConfiguration(
    int ScaleMask,
    int InstanceCeiling,
    int InstanceCeilingComponent,
    int DiffuseDirection,
    int DiffuseColor,
    int Wind,
    int Fade,
    int AmbientColor,
    int DirectionalScale,
    int FogColor,
    int Fog,
    int AlphaCutoff);

internal sealed record RetailGrassDrawConfiguration(
    int PrimitiveType,
    int VertexStrideBytes,
    IReadOnlyList<int[]> Declaration,
    RetailGrassSamplerConfiguration Sampler,
    RetailGrassRenderStateConfiguration RenderState,
    int RenderFrameLead,
    int StripBridgeIndices,
    int PrimitiveCountBias,
    int FullBatchTrailingBridgeIndices)
{
    internal void Validate()
    {
        if (PrimitiveType <= 0 || VertexStrideBytes <= 0 || Declaration.Count < 1 ||
            Declaration.Any(row => row.Length < 1) || RenderFrameLead < 0 ||
            StripBridgeIndices < 0 || PrimitiveCountBias < 0 ||
            FullBatchTrailingBridgeIndices < 0)
            throw new InvalidOperationException("Retail grass draw contract is incomplete.");
    }
}

internal sealed record RetailGrassCaptureConfiguration(
    string Schema,
    string Event,
    int TextureStageCount,
    int MaximumCandidates,
    int MaximumRecords,
    int MaximumShaderBytes,
    int MaximumVertexBufferBytes,
    int MinimumMatchingRecords,
    int RequiredMatchedResourceCount,
    bool RequireEveryObservedMesh)
{
    internal const string ExpectedSchema = "opennv-retail-grass-capture-contract/v1";
    internal const string ExpectedEvent = "texture-sampler-contract";

    internal void Validate()
    {
        if (Schema != ExpectedSchema || Event != ExpectedEvent ||
            TextureStageCount <= 0 || MaximumCandidates <= 0 || MaximumRecords <= 0 ||
            MaximumShaderBytes <= 0 || MaximumVertexBufferBytes <= 0 ||
            MinimumMatchingRecords <= 0 || RequiredMatchedResourceCount <= 0 ||
            MinimumMatchingRecords >= MaximumRecords || !RequireEveryObservedMesh)
            throw new InvalidOperationException(
                "Retail grass capture contract is incomplete.");
    }
}

internal sealed record RetailGrassSamplerConfiguration(
    int AddressU,
    int AddressV,
    int MagFilter,
    int MinFilter,
    int MipFilter,
    int SrgbTexture,
    int SrgbWrite);

internal sealed record RetailGrassRenderStateConfiguration(
    int CullMode,
    int ZEnable,
    int ZWriteEnable,
    int ZFunction,
    int AlphaTestEnable,
    int AlphaReference,
    int AlphaFunction,
    int AlphaBlendEnable,
    int SourceBlend,
    int DestinationBlend,
    int BlendOperation,
    int SeparateAlphaBlendEnable,
    int SourceBlendAlpha,
    int DestinationBlendAlpha,
    int BlendOperationAlpha,
    int ColorWriteEnable,
    int FogEnable)
{
    internal IReadOnlyDictionary<string, int> Values =>
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["cullMode"] = CullMode,
            ["zEnable"] = ZEnable,
            ["zWriteEnable"] = ZWriteEnable,
            ["zFunction"] = ZFunction,
            ["alphaTestEnable"] = AlphaTestEnable,
            ["alphaReference"] = AlphaReference,
            ["alphaFunction"] = AlphaFunction,
            ["alphaBlendEnable"] = AlphaBlendEnable,
            ["sourceBlend"] = SourceBlend,
            ["destinationBlend"] = DestinationBlend,
            ["blendOperation"] = BlendOperation,
            ["separateAlphaBlendEnable"] = SeparateAlphaBlendEnable,
            ["sourceBlendAlpha"] = SourceBlendAlpha,
            ["destinationBlendAlpha"] = DestinationBlendAlpha,
            ["blendOperationAlpha"] = BlendOperationAlpha,
            ["colorWriteEnable"] = ColorWriteEnable,
            ["fogEnable"] = FogEnable,
        };
}

internal sealed record RetailGrassReconstructionConfiguration(
    float ZeroLengthEpsilon,
    float ScaleBase,
    float ScalePerInstance,
    float ShadeBase,
    float ShadeFraction,
    float PhaseSpatialScale,
    float PhaseRadiansScale,
    float PhaseOffset,
    float Tau,
    float Pi)
{
    internal void Validate()
    {
        var values = new[]
        {
            ZeroLengthEpsilon,
            ScaleBase,
            ScalePerInstance,
            ShadeBase,
            ShadeFraction,
            PhaseSpatialScale,
            PhaseRadiansScale,
            PhaseOffset,
            Tau,
            Pi,
        };
        if (values.Any(value => !float.IsFinite(value) || value <= 0.0f))
            throw new InvalidOperationException(
                "Retail grass reconstruction contract must be finite and positive.");
    }
}

internal sealed record RetailGrassMeshConfiguration(
    string Suffix,
    string Path,
    string Sha256,
    int SourceVertices,
    int StripLength);

internal static class RetailGrassHash
{
    private const string Prefix = "0x";
    private const int CanonicalCharacters = 10;

    internal static bool TryParse(string value, out uint result)
    {
        result = default;
        return value.Length == CanonicalCharacters &&
            value.StartsWith(Prefix, StringComparison.Ordinal) &&
            uint.TryParse(
                value.AsSpan(Prefix.Length),
                System.Globalization.NumberStyles.AllowHexSpecifier,
                System.Globalization.CultureInfo.InvariantCulture,
                out result);
    }
}

internal sealed record ActorCompilerConfiguration(
    ConfigurationProvenance Provenance,
    FaceGenMaterialConfiguration FaceGenMaterial,
    FaceGenAnimationConfiguration FaceGenAnimation,
    ActorAnimationProfilesConfiguration AnimationProfiles,
    ActorRigidAttachmentConfiguration RigidAttachment);

internal sealed record FaceGenAnimationConfiguration(
    string Schema,
    ConfigurationProvenance Provenance,
    FaceGenLipConfiguration Lip,
    FaceGenTriConfiguration Tri)
{
    internal const string ExpectedSchema = "opennv-retail-facegen-animation/v1";

    internal void Validate()
    {
        if (Schema != ExpectedSchema)
            throw new InvalidOperationException("Actor FaceGen animation schema is invalid.");
        Lip.Validate();
        Tri.Validate();
    }
}

internal sealed record FaceGenLipConfiguration(
    string ByteOrder,
    int Version,
    string[] FileHeaderFields,
    string[] DecodedHeaderFields,
    int IntegerBytes,
    int ValueBytes,
    int RunMarker,
    int RunLengthBytes,
    int StoredSizeBiasBytes,
    int ImplicitTrailingZeroBytes,
    int CompressedFlag,
    int BigEndianFlag,
    int UncompressedMarker,
    double SampleRateHz,
    string Interpolation,
    bool ZeroOutsideAuthoredRange,
    int MaximumDecodedBytes,
    int MaximumFrames,
    float MaximumAbsoluteWeight,
    string[] TargetNames,
    string?[] MorphTargetNames)
{
    internal void Validate()
    {
        var positiveValues = new double[]
        {
            Version,
            IntegerBytes,
            ValueBytes,
            RunLengthBytes,
            StoredSizeBiasBytes,
            CompressedFlag,
            BigEndianFlag,
            UncompressedMarker,
            SampleRateHz,
            MaximumDecodedBytes,
            MaximumFrames,
            MaximumAbsoluteWeight,
        };
        if (positiveValues.Any(value => !double.IsFinite(value) || value <= 0.0) ||
            ByteOrder != "little" ||
            Interpolation != "linear" ||
            !ZeroOutsideAuthoredRange ||
            ImplicitTrailingZeroBytes < 0 ||
            RunMarker < byte.MinValue || RunMarker > byte.MaxValue ||
            UncompressedMarker < byte.MinValue || UncompressedMarker > byte.MaxValue ||
            (CompressedFlag & BigEndianFlag) != 0 ||
            !UniqueNames(FileHeaderFields) ||
            !UniqueNames(DecodedHeaderFields) ||
            !UniqueNames(TargetNames) ||
            MorphTargetNames is null ||
            MorphTargetNames.Length != TargetNames.Length ||
            !MorphTargetNames.Any(value => value is not null) ||
            MorphTargetNames.Any(value => value is not null && string.IsNullOrWhiteSpace(value)) ||
            MorphTargetNames.Where(value => value is not null)
                .Distinct(StringComparer.Ordinal).Count() !=
                MorphTargetNames.Count(value => value is not null))
            throw new InvalidOperationException("Actor FaceGen LIP contract is invalid.");
    }

    private static bool UniqueNames(string[]? values) =>
        values is { Length: > 0 } &&
        values.All(value => !string.IsNullOrWhiteSpace(value)) &&
        values.Distinct(StringComparer.Ordinal).Count() == values.Length;
}

internal sealed record FaceGenTriConfiguration(
    string Signature,
    string ByteOrder,
    string[] HeaderFields,
    int IntegerBytes,
    int ScalarBytes,
    int DeltaComponentBytes,
    int ReservedBytes,
    int LabelledVertexPrefixBytes,
    int LabelledSurfacePrefixBytes,
    int UvExtensionFlag,
    int PositionComponents,
    int UvComponents,
    int TriangleIndices,
    int QuadIndices,
    string[] ExportMorphKinds,
    string TargetNameCollisionPolicy,
    string NormalTargetPolicy)
{
    internal void Validate()
    {
        var positiveValues = new[]
        {
            IntegerBytes,
            ScalarBytes,
            DeltaComponentBytes,
            UvExtensionFlag,
            PositionComponents,
            UvComponents,
            TriangleIndices,
            QuadIndices,
        };
        var expectedKinds = new HashSet<string>(
            new[] { "differential", "static" },
            StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(Signature) ||
            ByteOrder != "little" ||
            positiveValues.Any(value => value <= 0) ||
            ReservedBytes < 0 ||
            LabelledVertexPrefixBytes < 0 ||
            LabelledSurfacePrefixBytes < 0 ||
            HeaderFields is not { Length: > 0 } ||
            HeaderFields.Any(string.IsNullOrWhiteSpace) ||
            HeaderFields.Distinct(StringComparer.Ordinal).Count() != HeaderFields.Length ||
            ExportMorphKinds is null ||
            !expectedKinds.SetEquals(ExportMorphKinds) ||
            TargetNameCollisionPolicy != "reject" ||
            NormalTargetPolicy != "recompute-from-authored-topology")
            throw new InvalidOperationException("Actor FaceGen TRI contract is invalid.");
    }
}

internal sealed record ActorRigidAttachmentConfiguration(
    ConfigurationProvenance Provenance,
    string BipedHeadNode,
    ActorRigidAttachmentProfilesConfiguration Profiles)
{
    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(BipedHeadNode))
            throw new InvalidOperationException("Actor biped-head node must not be empty.");
        Profiles.Validate();
    }
}

internal sealed record ActorRigidAttachmentProfilesConfiguration(
    ActorRigidAttachmentProfileConfiguration NPC_,
    ActorRigidAttachmentProfileConfiguration CREA)
{
    internal void Validate()
    {
        foreach (var profile in new[] { NPC_, CREA })
        {
            if (string.IsNullOrWhiteSpace(profile.SkeletonRootNode) ||
                string.IsNullOrWhiteSpace(profile.UnparentedRigidNode))
                throw new InvalidOperationException(
                    "Actor rigid-attachment profiles must declare both node identities.");
        }
    }
}

internal sealed record ActorRigidAttachmentProfileConfiguration(
    string SkeletonRootNode,
    string UnparentedRigidNode);

internal sealed record ActorAnimationProfilesConfiguration(
    ActorAnimationProfileConfiguration NPC_,
    ActorAnimationProfileConfiguration CREA)
{
    internal void Validate()
    {
        if (NPC_.Mode != "exact-owned-member" ||
            string.IsNullOrWhiteSpace(NPC_.Path) ||
            NPC_.FileName is not null ||
            CREA.Mode != "skeleton-directory" ||
            CREA.Path is not null ||
            string.IsNullOrWhiteSpace(CREA.FileName))
            throw new InvalidOperationException(
                "Actor animation profiles do not declare complete owned-member resolvers.");
    }
}

internal sealed record ActorAnimationProfileConfiguration(
    string Mode,
    string? Path,
    string? FileName);

internal sealed record FaceGenMaterialConfiguration(
    string Schema,
    bool SourceSamplerSrgbTexture,
    bool SourceRenderTargetSrgbWrite,
    float SignedDetailNeutral,
    float SignedDetailScale,
    int[] ToneMapRgba,
    float ToneScale,
    ColorTransferConfiguration RuntimeAlbedoTransfer,
    string Source)
{
    internal const string ExpectedSchema = "opennv-retail-facegen-material/v2";
}

internal sealed record ColorTransferConfiguration(
    string Schema,
    float EncodedCutoff,
    float LinearScale,
    float Offset,
    float Normalization,
    float Exponent,
    string Source)
{
    internal const string ExpectedSchema = "opennv-srgb-transfer/v1";
}

internal static class RuntimeConfigurationConversions
{
    internal static Vector2 Vector2(this float[] values) => new(values[0], values[1]);

    internal static Vector3 Vector3(this float[] values) => new(values[0], values[1], values[2]);

    internal static Color Color(this float[] values) => new(values[0], values[1], values[2], values[3]);
}
