using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Godot;


namespace OpenNV.Runtime;

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
    DesktopKeyBindingConfiguration Jump,
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
            yield return Jump;
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

internal sealed record PersistenceConfiguration(
    ConfigurationProvenance Provenance,
    int AtomicReplaceAttempts,
    int AtomicReplaceRetryMilliseconds);

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

internal sealed record DesktopLauncherConfiguration(
    ConfigurationProvenance Provenance,
    int MainWindowWidthPixels,
    int MainWindowHeightPixels,
    int MainWindowMinimumWidthPixels,
    int MainWindowMinimumHeightPixels,
    int ToastVisibilityMilliseconds);

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
            NPC_.Roles is not null ||
            CREA.Mode != "skeleton-directory" ||
            CREA.Path is not null ||
            string.IsNullOrWhiteSpace(CREA.FileName) ||
            CREA.Roles is null ||
            CREA.Roles.Count != 3 ||
            !CREA.Roles.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(
                new[] { "locomotion", "melee", "hit" }) ||
            CREA.Roles.Values.Any(candidates =>
                candidates.Count == 0 ||
                candidates.Any(string.IsNullOrWhiteSpace) ||
                candidates.Distinct(StringComparer.OrdinalIgnoreCase).Count() !=
                    candidates.Count))
            throw new InvalidOperationException(
                "Actor animation profiles do not declare complete owned-member resolvers.");
    }
}

internal sealed record ActorAnimationProfileConfiguration(
    string Mode,
    string? Path,
    string? FileName,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? Roles);

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
