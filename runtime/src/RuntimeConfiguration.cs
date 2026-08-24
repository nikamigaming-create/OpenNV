using System.Security.Cryptography;
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
    PoolConfiguration Pool,
    DoorConfiguration Door,
    HudConfiguration Hud,
    CaptureConfiguration Capture,
    ProofConfiguration Proof,
    DiagnosticPreviewConfiguration DiagnosticPreview,
    ActorReviewConfiguration ActorReview,
    ExteriorEnvironmentConfiguration ExteriorEnvironment,
    RetailActorStateConfiguration RetailActorState,
    ActorParityConfiguration ActorParity,
    SetupViewConfiguration SetupView,
    DesktopLauncherConfiguration DesktopLauncher,
    LegalAssetsConfiguration LegalAssets,
    ContentCompilerConfiguration ContentCompiler,
    ActorCompilerConfiguration ActorCompiler)
{
    internal const string ExpectedSchema = "opennv-runtime-configuration/v1";
    internal const string ResourcePath = "res://config/open-nv-runtime-v1.json";
    private const float PerspectiveMaximumDegrees = 180.0f;

    internal string Sha256 { get; private set; } = "";

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
        return configuration;
    }

    internal void VerifyCompiledConfiguration(JsonElement source)
    {
        var compiled = source.GetProperty("configuration");
        if (compiled.GetProperty("schema").GetString() != ExpectedSchema ||
            !compiled.GetProperty("sha256").GetString()!.Equals(Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Prepared content was compiled with another runtime configuration.");
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
            Pool.Provenance,
            Door.Provenance,
            Hud.Provenance,
            Capture.Provenance,
            Proof.Provenance,
            Proof.GameplayRoute.Provenance,
            DiagnosticPreview.Provenance,
            ActorReview.Provenance,
            ExteriorEnvironment.Provenance,
            RetailActorState.Provenance,
            ActorParity.Provenance,
            SetupView.Provenance,
            DesktopLauncher.Provenance,
            LegalAssets.Provenance,
            ContentCompiler.Provenance,
            ActorCompiler.Provenance,
        })
            provenance.Validate();

        RequirePositive(World.GameUnitsToMeters, nameof(World.GameUnitsToMeters));
        RequirePositive(Simulation.PhysicsTicksPerSecond, nameof(Simulation.PhysicsTicksPerSecond));
        RequirePositive(Simulation.GravityMetersPerSecondSquared, nameof(Simulation.GravityMetersPerSecondSquared));
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
        RequireUnitInterval((float)Pool.StrikeHaptic.Amplitude, nameof(Pool.StrikeHaptic.Amplitude));
        RequirePositive(Pool.StrikeHaptic.DurationSeconds, nameof(Pool.StrikeHaptic.DurationSeconds));
        RequirePositive(Door.FallbackOpenAngleDegrees, nameof(Door.FallbackOpenAngleDegrees));
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
        RequireColor(Renderer.BackgroundColorRgba, nameof(Renderer.BackgroundColorRgba));
        RequireColor(Renderer.NeutralNormalColorRgba, nameof(Renderer.NeutralNormalColorRgba));
        if (Renderer.NeutralNormalTextureSizePixels.Length != 2 ||
            Renderer.NeutralNormalTextureSizePixels.Any(value => value <= 0))
            throw new InvalidOperationException("Neutral normal texture dimensions must be two positive values.");
        RequirePositive(Renderer.CubemapFaceCount, nameof(Renderer.CubemapFaceCount));
        RequireColor(Hud.DesktopPanelColorRgba, nameof(Hud.DesktopPanelColorRgba));
        RequireColor(Hud.TextColorRgba, nameof(Hud.TextColorRgba));
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
        if (Capture.EnvironmentShots.Count < 1)
            throw new InvalidOperationException("Capture configuration must declare at least one environment shot.");
        RequirePositive(Capture.RgbaChannelCount, nameof(Capture.RgbaChannelCount));
        RequirePositive(Capture.PixelChannelMaximum, nameof(Capture.PixelChannelMaximum));
        RequireVector(Capture.LuminanceWeightsRgb, 3, nameof(Capture.LuminanceWeightsRgb));
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
        RequirePositive(ContentCompiler.LandscapeQuadrantPixels, nameof(ContentCompiler.LandscapeQuadrantPixels));
        RequirePositive(
            ContentCompiler.LandscapeTilesPerQuadrant,
            nameof(ContentCompiler.LandscapeTilesPerQuadrant));
        RequirePositive(
            ContentCompiler.LandscapeTileRepeatsPerCell,
            nameof(ContentCompiler.LandscapeTileRepeatsPerCell));
        if (ActorCompiler.States.Count < 1 ||
            ActorCompiler.States.Select(state => state.ReferenceFormId)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count() != ActorCompiler.States.Count)
            throw new InvalidOperationException("Actor compiler states must be nonempty and uniquely keyed.");
    }

    private static void RequirePositive(float value, string name)
    {
        if (!float.IsFinite(value) || value <= 0.0f)
            throw new InvalidOperationException($"Runtime configuration {name} must be positive.");
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
    DesktopKeyBindingConfiguration Reload,
    DesktopKeyBindingConfiguration Save,
    DesktopKeyBindingConfiguration Cancel,
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
            yield return Reload;
            yield return Save;
            yield return Cancel;
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
    float FallbackOpenAngleDegrees);

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
    string DefaultSavePath);

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
    IReadOnlyList<EnvironmentShotConfiguration> EnvironmentShots);

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
    float DialogCenteredRatio);

internal sealed record DesktopLauncherConfiguration(
    ConfigurationProvenance Provenance,
    int MainWindowWidthPixels,
    int MainWindowHeightPixels,
    int MainWindowMinimumWidthPixels,
    int MainWindowMinimumHeightPixels,
    int ToastVisibilityMilliseconds);

internal sealed record LegalAssetsConfiguration(
    ConfigurationProvenance Provenance,
    string DefaultCellRecipe,
    string DefaultCacheRoot,
    string PackagedCompilerName);

internal sealed record ContentCompilerConfiguration(
    ConfigurationProvenance Provenance,
    int AssetIdHexCharacters,
    int StableIdHexCharacters,
    int PngCompressionLevel,
    float AnimationSamplesPerSecond,
    float ZeroSpecularEpsilon,
    float MinimumMaterialRoughness,
    float DefaultMaterialGlossiness,
    int LandscapeQuadrantPixels,
    int LandscapeTilesPerQuadrant,
    int LandscapeTileRepeatsPerCell);

internal sealed record ActorCompilerConfiguration(
    ConfigurationProvenance Provenance,
    IReadOnlyList<ActorCompilerStateConfiguration> States);

internal sealed record ActorCompilerStateConfiguration(
    string ReferenceFormId,
    string IdleAnimation,
    int[] SkinToneRgba,
    string SkinToneSource,
    IReadOnlyList<string> BodyTextureSourceAliases);

internal static class RuntimeConfigurationConversions
{
    internal static Vector2 Vector2(this float[] values) => new(values[0], values[1]);

    internal static Vector3 Vector3(this float[] values) => new(values[0], values[1], values[2]);

    internal static Color Color(this float[] values) => new(values[0], values[1], values[2], values[3]);
}
