using System.Text.Json;
using Godot;


using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Presentation.Ui;
using OpenNV.Runtime.Presentation.CharacterCreation;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal sealed record OpeningFlowMenu(
    string Role,
    string Document,
    string MenuName,
    string SourcePath,
    Rect2? Rect,
    IReadOnlyDictionary<string, OpeningFlowSemanticRect> SemanticRects,
    OwnedUiTexture? Background,
    OwnedGamebryoDialogueMenu? DialogueMenu,
    OwnedGamebryoTextEditMenu? TextEditMenu,
    OpeningRaceSexMenuTiles? RaceSexMenuTiles,
    OpeningRaceSexRenderedDevice? RenderedDevice);

internal sealed record OpeningRaceSexRenderedDevice(
    OwnedPhysicalDevice Device,
    string SettingsSourcePath,
    string SettingsSourceSha256,
    IReadOnlyDictionary<string, OpeningRaceSexRenderedSetting> Settings,
    OpeningRaceSexRenderedDeviceFraming Framing,
    OpeningRaceSexPreviewCameraContract PreviewCameraContract)
{
    internal OwnedGamebryoFaceGenDeviceContract FaceGenPreviewDevice => new(
        new Color(
            Float("menuPlayerLightDiffuseRed"),
            Float("menuPlayerLightDiffuseGreen"),
            Float("menuPlayerLightDiffuseBlue")),
        new Color(
            Float("menuPlayerLightAmbientRed"),
            Float("menuPlayerLightAmbientGreen"),
            Float("menuPlayerLightAmbientBlue")),
        Float("nearDistanceGameUnits"),
        Float("farDistanceGameUnits"),
        Float("terminalFov"),
        PreviewCameraContract.Status,
        PreviewCameraContract.CameraContractReady,
        PreviewCameraContract.ParityReady);

    internal float Float(string role) =>
        Settings.TryGetValue(role, out var value) && value.FloatValue is { } result
            ? result
            : throw new InvalidOperationException(
                $"Owned RaceSex rendered-terminal float is absent: {role}");

    internal bool Bool(string role) =>
        Settings.TryGetValue(role, out var value) && value.BoolValue is { } result
            ? result
            : throw new InvalidOperationException(
                $"Owned RaceSex rendered-terminal boolean is absent: {role}");
}

internal sealed record OpeningRaceSexRenderedDeviceFraming(
    string SourcePath,
    string SourceSha256,
    string Status,
    Vector2I ViewportPixels,
    string RetailFrameSha256,
    Rect2I RetailOuterDeviceBoundsPixels,
    Rect2I RetailRightScreenBoundsPixels,
    string CurrentFrameSha256,
    Rect2I CurrentOuterDeviceBoundsPixels,
    Rect2I CurrentRightScreenBoundsPixels,
    double CurrentZoomGameUnits,
    double ProjectionScale,
    double SolvedZoomGameUnits,
    Vector2 ResidualPixels,
    OpeningRaceSexRenderedDeviceAlignment Alignment,
    bool ParityReady);

internal sealed record OpeningRaceSexRenderedDeviceAlignment(
    string BaselineCurrentFrameSha256,
    Rect2 ProjectedCurrentRightScreenBoundsPixels,
    Vector2 DeviceTranslationPixels,
    Vector2I ReferenceCanvasPixels,
    Vector2 DeviceTranslationCanvasUnits,
    Rect2I RetailContentBoundsPixels,
    Rect2I CurrentContentBoundsPixels,
    Vector2 ContentScale,
    Vector2 ContentTranslationWithinScreenPixels);

internal sealed record OpeningRaceSexPreviewCameraContract(
    string SourcePath,
    string SourceSha256,
    string Status,
    bool ParityReady,
    bool CameraContractReady);

internal sealed record OpeningRaceSexRenderedSetting(
    string Section,
    string Key,
    string Type,
    bool? BoolValue,
    float? FloatValue);

internal sealed record OpeningFlowSemanticRect(string Tile, Rect2 Rect);

internal sealed record OpeningRaceSexMenuTiles(
    string Schema,
    string Document,
    string DocumentSha256,
    string MenuName,
    string MenuClassEntity,
    string ActiveListTrait,
    string SliderLeftLabelTrait,
    string SliderRightLabelTrait,
    int FontId,
    OwnedBitmapFont Font,
    OwnedGamebryoRaceSexControls SharedControls,
    OpeningRaceSexBackground Background,
    OpeningRaceSexFaceGrab FaceGrab,
    OpeningRaceSexScroll Scroll,
    OpeningRaceSexNavigation Navigation,
    OpeningRaceSexListTemplate ListItem,
    OpeningRaceSexSliderTemplate Slider);

internal sealed record OpeningRaceSexTexture(
    string? LogicalPath,
    string? Atlas,
    string? FileName,
    OwnedUiTexture? Texture,
    OpeningRaceSexTextureAtlas? AtlasContract);

internal sealed record OpeningRaceSexTextureAtlas(
    string IndexLogicalPath,
    string IndexSource,
    long IndexBytes,
    string IndexSha256,
    string IndexSourceArchive,
    string IndexSourceArchiveSha256,
    string TextureLogicalPath,
    string AtlasFileName,
    int AtlasIndex,
    string AtlasType,
    Rect2 UvRect,
    float DepthOffset);

internal sealed record OpeningRaceSexBackground(
    string Tile,
    Rect2 Rect,
    OpeningRaceSexTexture Texture,
    float Brightness,
    float Depth,
    float TopBound,
    float BottomBound);

internal sealed record OpeningRaceSexFaceGrab(
    string Tile,
    string Id,
    Rect2 Rect,
    float Depth);

internal sealed record OpeningRaceSexScroll(
    OpeningRaceSexScrollTarget Up,
    OpeningRaceSexScrollTarget Down);

internal sealed record OpeningRaceSexScrollTarget(
    string Tile,
    string Id,
    float Y,
    float Brightness,
    string AlphaPolicy,
    Rect2? Rect,
    OpeningRaceSexTexture Texture,
    string ClickSound);

internal sealed record OpeningRaceSexNavigation(
    OpeningRaceSexNavigationButton Back,
    OpeningRaceSexNavigationButton Next);

internal sealed record OpeningRaceSexNavigationButton(
    string Tile,
    string Id,
    float X,
    float Y,
    float Font,
    float Brightness,
    float HorizontalBuffer,
    float VerticalBuffer,
    float TextYAdjust,
    float VerticalCenterDivisor,
    float BaseTextYOffset,
    bool BoxVisible,
    bool InheritBrightness,
    string AlphaPolicy,
    string Justify,
    string LabelRole,
    string StringEntity,
    string Label,
    IReadOnlyList<OpeningRaceSexStringSourceDocument> StringSourceDocuments,
    string ClickSound);

internal sealed record OpeningRaceSexStringSourceDocument(
    string Path,
    string Sha256);

internal sealed record OpeningRaceSexListTemplate(
    string Template,
    string Tile,
    string Id,
    Rect2 Rect,
    float Brightness,
    string ActiveListTrait,
    string SelectedTrait,
    OpeningRaceSexSelectionIndicator SelectionIndicator,
    OpeningRaceSexTextTemplate Text,
    string ClickSound,
    string MouseOverSound);

internal sealed record OpeningRaceSexSelectionIndicator(
    string Tile,
    OpeningRaceSexTexture Texture,
    Rect2 Rect);

internal sealed record OpeningRaceSexTextTemplate(
    string Tile,
    float Font,
    float Y,
    float? NotSelectableX,
    float? SelectableX,
    string WidthPolicy,
    string HeightPolicy);

internal sealed record OpeningRaceSexSliderTemplate(
    string Template,
    string Tile,
    string Id,
    Rect2 Rect,
    float Brightness,
    string ActiveListTrait,
    OpeningRaceSexSliderTraits Traits,
    OpeningRaceSexSliderText Label,
    OpeningRaceSexSliderValueText Value,
    OpeningRaceSexSliderBar Bar,
    OpeningRaceSexSliderArrow LeftArrow,
    OpeningRaceSexSliderArrow RightArrow,
    OpeningRaceSexSliderMarker Marker,
    string ClickSound,
    string MouseOverSound);

internal sealed record OpeningRaceSexSliderTraits(
    string Current,
    string Minimum,
    string Maximum,
    string Jump,
    string Display,
    string Increment);

internal sealed record OpeningRaceSexSliderText(
    string Tile,
    float Font,
    float X,
    float Y,
    float? LabelGap,
    string WidthPolicy,
    string HeightPolicy);

internal sealed record OpeningRaceSexSliderValueText(
    string Tile,
    float Font,
    float Y,
    string XPolicy,
    float LabelGap,
    string WidthPolicy,
    string HeightPolicy);

internal sealed record OpeningRaceSexSliderBar(
    string Tile,
    float X,
    float Y,
    float Width,
    string HeightGlobalTrait);

internal sealed record OpeningRaceSexSliderArrow(
    string Tile,
    string TextTile,
    string Id,
    float? X,
    float? XAnchor,
    string? AnchorEdge,
    float Y,
    string WidthPolicy,
    float Height,
    string StringSourceMenuTrait,
    string? Justify,
    string ClickSound);

internal sealed record OpeningRaceSexSliderMarker(
    string Tile,
    string TextTile,
    string Id,
    float BarX,
    float BarWidth,
    string CurrentTrait,
    string MinimumTrait,
    string MaximumTrait,
    Vector2 Clamp,
    float Y,
    float Width,
    float Height,
    string Glyph,
    string GlyphXPolicy,
    float GlyphXMultiplier,
    float GlyphY);

internal sealed record OpeningStageProgram(
    int Stage,
    string Source,
    IReadOnlyList<OpeningFlowCommand> Commands);

internal sealed record OpeningOrdinaryQuest(
    string FormId,
    string EditorId,
    string ScriptFormId,
    string ScriptEditorId,
    int EntryStage,
    IReadOnlyDictionary<uint, string> Variables,
    IReadOnlyDictionary<int, string> Objectives,
    IReadOnlyDictionary<int, OpeningStageProgram> Stages,
    OpeningCommandContract CommandContract);

internal sealed record OpeningOrdinaryActor(
    string Role,
    string ReferenceFormId,
    string BaseFormId,
    IReadOnlyList<string> PackagePriority,
    IReadOnlyDictionary<string, OpeningGuidePackage> Packages,
    string ActivationTopicFormId,
    IReadOnlyDictionary<string, OpeningDialogueTopic> Topics,
    OpeningDialogueVoice Voice,
    IReadOnlyList<OpeningOrdinaryPackageArrival> ArrivalTransitions,
    IReadOnlyList<OpeningOrdinaryDialogueTrigger> AutomaticDialogueTriggers,
    IReadOnlyList<OpeningOrdinaryPackageDialogue> AutomaticPackageDialogues,
    OpeningCommandContract CommandContract);

internal sealed record OpeningOrdinaryPackageDialogue(
    string PackageFormId,
    string GreetingTopicFormId);

internal sealed record OpeningOrdinaryPackageArrival(
    string PackageFormId,
    string ScriptFormId,
    string ScriptEditorId,
    string ActorReferenceFormId,
    string QuestFormId,
    int FromStage,
    int ToStage);

internal sealed record OpeningOrdinaryDialogueTrigger(
    string ScriptFormId,
    string ScriptEditorId,
    string TriggerReferenceFormId,
    string TriggerReferenceEditorId,
    Vector3 PositionGameUnits,
    Quaternion RotationGodot,
    Vector3 BoundsGameUnits,
    string QuestFormId,
    int ObjectiveIndex,
    string TopicFormId);

internal sealed record OpeningHitTargetSet(
    string ScriptFormId,
    string ScriptEditorId,
    string EnableParentFormId,
    IReadOnlyList<OpeningHitTarget> Targets,
    string QuestFormId,
    int QuestVariableIndex,
    string QuestVariableName,
    int WeaponAnimationTypeMinimumExclusive,
    int WeaponAnimationTypeMaximumExclusive,
    string ExcludedWeaponFormId,
    string ReactionTopicFormId,
    string SpeakerReferenceFormId,
    string TutorialQuestFormId,
    int TutorialStage,
    int Threshold,
    int ObjectiveIndex);

internal sealed record OpeningHitTarget(string ReferenceFormId, string BaseFormId);

internal sealed record OpeningTimerTransition(int FromStage, int ToStage);

internal sealed record OpeningCommandContract(
    string Schema,
    int CommandCount,
    IReadOnlyDictionary<string, int> KindCounts,
    IReadOnlyDictionary<string, int> RecordIdentityCounts,
    bool AllEmittedKindsRuntimeBlocking,
    bool AllDeclaredRecordReferencesResolved);

internal sealed record OpeningSceneRole(
    string Role,
    string EditorId,
    string DisplayName,
    string RecordType,
    string ReferenceFormId,
    string BaseFormId);

internal sealed record OpeningInteraction(
    string Event,
    string ScriptEditorId,
    string TargetRole,
    string TargetReferenceFormId,
    int FromStage,
    int ToStage,
    string DistancePolicy,
    OpeningFlowCommand? Menu);

internal sealed record OpeningDialogueTopic(
    string FormId,
    string EditorId,
    string Prompt,
    IReadOnlyList<OpeningDialogueInfo> Infos);

internal sealed record OpeningDialogueInfo(
    string FormId,
    int SourceOrder,
    IReadOnlyList<OpeningDialogueResponse> Responses,
    IReadOnlyList<OpeningFlowCommand> Commands,
    IReadOnlyList<OpeningDialogueCondition> Conditions,
    IReadOnlyList<string> NextTopicFormIds,
    int ResponseType,
    int Flags,
    bool Goodbye,
    bool SayOnce);

internal sealed record OpeningDialogueVoice(
    string SpeakerRole,
    string SpeakerReferenceFormId,
    string SpeakerBaseFormId,
    string VoiceTypeFormId,
    string VoiceTypeEditorId,
    string MemberNamespace,
    int InfoCount,
    int ResponseCount,
    string ArchiveSchema,
    string ArchiveRecipeId,
    string ArchiveRecipeSha256,
    int ArchiveCount);

internal sealed record OpeningDialogueResponse(
    int Index,
    string Text,
    OpeningDialogueAsset Voice,
    OpeningDialogueAsset Lip);

internal sealed record OpeningDialogueAsset(
    string LogicalPath,
    string SourcePath,
    string Sha256,
    string SourceArchive,
    string SourceArchiveSha256);

internal sealed record OpeningDialogueCondition(
    int OperatorFlags,
    float ComparisonValue,
    int Function,
    string Parameter1,
    int Parameter2,
    int RunOn,
    string Reference);

internal sealed record OpeningFlowCommand(
    string Kind,
    string? Role,
    string? QuestEditorId,
    string? TopicEditorId,
    string? SpeakerEditorId,
    string? ReferenceEditorId,
    string? ItemEditorId,
    string? PackageEditorId,
    string? ModifierEditorId,
    string? Operation,
    string? TargetEditorId,
    string? GlobalEditorId,
    string? ValueName,
    string? IdleEditorId,
    string? IdleFormId,
    string? IdleRecordType,
    string? AnimationLogicalPath,
    string? State,
    int? Stage,
    int? MaximumSelected,
    int? TotalPoints,
    int? Index,
    int? Delta,
    int? Count,
    float? Seconds,
    float? NumericValue,
    bool? Enabled,
    bool? Destroyed,
    bool? CrossFade,
    IReadOnlyList<int> ControlValues,
    string? ItemFormId,
    string? ItemRecordType,
    string? QuestFormId,
    string? QuestRecordType,
    string? GlobalFormId,
    string? GlobalRecordType,
    string? OwnerEditorId,
    string? OwnerFormId,
    string? OwnerRecordType,
    string? ReferenceFormId,
    string? ReferenceRecordType,
    OpeningCommandGuard? Guard,
    OpeningCommandWeapon? Weapon,
    IReadOnlyList<string> EnableParentChildFormIds);

internal sealed record OpeningCommandGuard(
    string Kind,
    string? ItemFormId,
    string? QuestFormId,
    int? Stage);

internal sealed record OpeningCommandWeapon(
    string AmmoFormId,
    string AmmoEditorId,
    int Damage,
    int ClipSize,
    int AnimationType);

internal sealed record OpeningGuideActorAi(
    string Role,
    string ReferenceFormId,
    string BaseFormId,
    string QuestFormId,
    IReadOnlyList<string> PackagePriority,
    IReadOnlyDictionary<string, OpeningGuidePackage> Packages,
    OpeningGuideFurnitureOccupancy FurnitureOccupancy,
    IReadOnlyList<OpeningGuideAnimationObject> AnimationObjects,
    OpeningGuideLocomotion Locomotion);

internal sealed record OpeningGuideFurnitureOccupancy(
    string InitialPackageFormId,
    string ReferenceFormId,
    int MarkerId,
    string MarkerDisposition,
    OpeningGuideFurnitureSource Furniture,
    OpeningGuideFurnitureIdentity PatientBed,
    int ReleaseStage,
    string ReleasePackageFormId,
    string AnimationObjectIdleFormId,
    OpeningGuideFurnitureAnimation SeatedLoop,
    OpeningGuideFurnitureAnimation Exit);

internal sealed record OpeningGuideFurnitureSource(
    string ReferenceFormId,
    string ReferenceRecordSha256,
    string BaseFormId,
    string EditorId,
    string RecordType,
    string RecordSha256,
    string ModelLogicalPath,
    long ModelBytes,
    string ModelSha256,
    string SourceArchive,
    string SourceArchiveSha256,
    OpeningGuideFurnitureMarker Marker);

internal sealed record OpeningGuideFurnitureIdentity(
    string ReferenceFormId,
    string ReferenceRecordSha256,
    string BaseFormId,
    string EditorId,
    string RecordType,
    string RecordSha256,
    string ModelLogicalPath,
    long ModelBytes,
    string ModelSha256,
    string SourceArchive,
    string SourceArchiveSha256);

internal sealed record OpeningGuideFurnitureMarker(
    string ExtraDataName,
    int Index,
    int PositionRef1,
    int PositionRef2,
    Vector3 OffsetNifGameUnits,
    Vector3 OffsetGodotGameUnits,
    int Orientation,
    float OrientationRadians,
    float Heading,
    int AnimationType,
    Quaternion RotationGodot,
    OpeningGuideFurniturePlacementOffset ActorPlacementOffset,
    OpeningGuideFurnitureHeadingDelta ActorForwardHeadingDelta);

internal sealed record OpeningGuideFurniturePlacementOffset(
    string Semantics,
    OpeningGuideFurniturePlacementGameSetting X,
    OpeningGuideFurniturePlacementGameSetting Y,
    OpeningGuideFurniturePlacementGameSetting Z,
    Vector3 OffsetNifGameUnits,
    Vector3 OffsetGodotGameUnits);

internal sealed record OpeningGuideFurniturePlacementGameSetting(
    string FormId,
    string EditorId,
    string RecordSha256,
    string SourceKind,
    float ValueGameUnits);

internal sealed record OpeningGuideFurnitureHeadingDelta(
    string FormId,
    string EditorId,
    string RecordSha256,
    string SourceKind,
    float ValueRadians,
    Quaternion RotationGodot);

internal sealed record OpeningGuideFurnitureAnimation(
    string Role,
    string FormId,
    string EditorId,
    string RecordType,
    string RecordSha256,
    string LogicalPath,
    long Bytes,
    string Sha256,
    string SourceArchive,
    string SourceArchiveSha256,
    string SequenceName,
    float StartSeconds,
    float StopSeconds,
    int CycleType,
    int ControlledBlocks,
    OpeningGuideRootMotion? RootMotion);

internal sealed record OpeningGuideAnimationObject(
    string ComponentRole,
    string FormId,
    string EditorId,
    string RecordType,
    string RecordSha256,
    string IdleAnimationFormId,
    string IdleAnimationEditorId,
    string IdleAnimationLogicalPath,
    string IdleAnimationSha256,
    string IdleAnimationSequenceName,
    float IdleAnimationStartSeconds,
    float IdleAnimationStopSeconds,
    int IdleAnimationCycleType,
    IReadOnlyDictionary<string, int> IdleAnimationTransformPrioritiesByNode,
    string ModelLogicalPath,
    long Bytes,
    string Sha256,
    string SourceArchive,
    string SourceArchiveSha256,
    string AttachmentNode);

internal sealed record OpeningGuidePackage(
    string FormId,
    string EditorId,
    string RecordSha256,
    uint PackageFlags,
    bool AlwaysRun,
    int PackageType,
    string PackageTypeName,
    int ProcedureFlags,
    int TypeSpecificFlags,
    IReadOnlyList<OpeningGuideCondition> Conditions,
    OpeningGuideLocation? Location,
    OpeningGuideTarget? Target,
    IReadOnlyList<string> IdleAnimationFormIds,
    IReadOnlyList<string> IdleAnimationLogicalPaths);

internal sealed record OpeningGuideCondition(
    int OperatorFlags,
    float ComparisonValue,
    int Function,
    string FunctionName,
    string Parameter1,
    uint Parameter2,
    uint RunOn,
    string Reference);

internal sealed record OpeningGuideLocation(
    int Type,
    string TypeName,
    string FormId,
    uint RadiusGameUnits,
    OpeningGuideReference? Reference);

internal sealed record OpeningGuideTarget(
    int Type,
    string TypeName,
    string FormId,
    uint Count,
    uint Unknown);

internal sealed record OpeningGuideReference(
    string FormId,
    string? EditorId,
    string RecordType,
    Vector3 PositionGameUnits,
    Vector3 RotationRadians,
    Quaternion RotationGodot);

internal sealed record OpeningGuideLocomotion(
    OpeningGuideLocomotionClip Walk,
    OpeningGuideLocomotionClip Run);

internal sealed record OpeningGuideLocomotionClip(
    string LogicalPath,
    string Sha256,
    OpeningGuideRootMotion RootMotion);

internal sealed record OpeningGuideRootMotion(
    string SequenceName,
    string TargetNode,
    float StartSeconds,
    float StopSeconds,
    int CycleType,
    Vector3 DisplacementGodotGameUnits,
    float SpeedGameUnitsPerSecond);

internal sealed record OpeningPlayerAnimationGraph(
    string CameraNode,
    IReadOnlyDictionary<string, OpeningPlayerPackage> Packages,
    IReadOnlyDictionary<string, OpeningPlayerAnimation> Animations);

internal sealed record OpeningPlayerPackage(
    string FormId,
    string EditorId,
    string RecordSha256,
    bool RunInSequence,
    bool DoOnce,
    float IdleTimerSeconds,
    IReadOnlyList<string> IdleAnimationFormIds,
    IReadOnlyDictionary<string, string?> EventAnimationFormIds);

internal sealed record OpeningPlayerAnimation(
    string FormId,
    string EditorId,
    string LogicalPath,
    string Sha256,
    OpeningTransformTrack Track);

internal sealed record OpeningTransformTrack(
    string TargetNode,
    float StartSeconds,
    float StopSeconds,
    int CycleType,
    IReadOnlyList<OpeningTransformParent> ParentChain,
    IReadOnlyList<OpeningTransformSample> Samples);

internal sealed record OpeningTransformParent(
    string NodeName,
    Vector3 TranslationGodotGameUnits,
    Quaternion Rotation,
    Vector3 Scale)
{
    internal const int VectorComponents = 3;
    internal const int QuaternionComponents = 4;
}

internal sealed record OpeningTransformSample(
    float TimeSeconds,
    Vector3 TranslationGodotGameUnits,
    Quaternion Rotation);

internal sealed record OpeningImageSpaceModifier(
    string FormId,
    string EditorId,
    float DurationSeconds,
    IReadOnlyList<OpeningImageSpaceFadeKey> Fade,
    string RecordSha256);

internal sealed record OpeningImageSpaceFadeKey(float Time, Color Color)
{
    internal const int ComponentCount = 5;
    internal const int TimeIndex = 0;
    internal const int RedIndex = 1;
    internal const int GreenIndex = 2;
    internal const int BlueIndex = 3;
    internal const int AlphaIndex = 4;
}

internal sealed record OpeningCharacterCreation(
    string SexTitle,
    IReadOnlyList<string> SexChoices,
    OpeningPlayerAppearance Appearance,
    int SpecialMinimum,
    int SpecialInitial,
    int SpecialMaximum,
    int SpecialTotalPoints,
    OpeningDocReaction DocReaction,
    IReadOnlyList<OpeningCharacterValue> SpecialValues,
    int TagSkillMaximumSelected,
    IReadOnlyList<OpeningCharacterValue> SkillValues,
    int TraitMaximumSelected,
    IReadOnlyList<OpeningCharacterValue> TraitValues,
    OpeningGameplayVitalsContract Vitals);

internal sealed record OpeningPlayerAppearance(
    string Schema,
    string Status,
    string PlayerFormId,
    string PlayerRecordSha256,
    string DefaultRaceFormId,
    string DefaultHairFormId,
    string DefaultEyesFormId,
    OpeningAppearanceFaceGen FaceGen,
    IReadOnlyList<string> SexEngineValues,
    IReadOnlyList<OpeningAppearanceRace> Races,
    string PreviewDisposition);

internal sealed record OpeningAppearanceFaceGen(
    int SymmetricGeometryCount,
    string SymmetricGeometrySha256,
    IReadOnlyList<float> SymmetricGeometryValues,
    int AsymmetricGeometryCount,
    string AsymmetricGeometrySha256,
    IReadOnlyList<float> AsymmetricGeometryValues,
    int SymmetricTextureCount,
    string SymmetricTextureSha256,
    IReadOnlyList<float> SymmetricTextureValues,
    OpeningFaceGenControlSpace ControlSpace,
    OpeningPlayerFaceGenPreviewSet PreviewHead);

internal sealed record OpeningFaceGenControlSpace(
    string Schema,
    string Status,
    string SourceArchive,
    string SourceArchiveSha256,
    string SourceLogicalPath,
    long SourceBytes,
    string SourceSha256,
    string FormatSignature,
    int GeometryBasisVersion,
    int TextureBasisVersion,
    int SymmetricGeometryBasisCount,
    int AsymmetricGeometryBasisCount,
    int SymmetricTextureBasisCount,
    int AsymmetricTextureBasisCount,
    int SymmetricGeometryControlCount,
    int AsymmetricGeometryControlCount,
    int SymmetricTextureControlCount,
    int AsymmetricTextureControlCount,
    IReadOnlyList<OpeningFaceGenLinearControl> SymmetricGeometryControls,
    string ExposureClassification,
    string EngineBuild,
    string SourceExecutableSha256,
    IReadOnlyList<OpeningNativeFaceGenGeometryControl> NativeGeometryControls,
    OpeningNativeFaceGenAgeControl NativeAgeControl,
    OpeningFaceGenPreviewControl PreviewControl,
    string RuntimeDisposition);

internal sealed record OpeningFaceGenLinearControl(
    int Index,
    string SourceLabel,
    string AxisSha256,
    IReadOnlyList<float> Axis);

internal sealed record OpeningAppearanceRace(
    string FormId,
    string EditorId,
    string Label,
    string RecordSha256,
    IReadOnlyDictionary<string, OpeningAppearanceSex> Sex);

internal sealed record OpeningAppearanceSex(
    string DefaultHairFormId,
    string DefaultEyesFormId,
    IReadOnlyList<OpeningAppearanceOption> HairOptions,
    IReadOnlyList<OpeningAppearanceOption> EyeOptions);

internal sealed record OpeningAppearanceOption(
    string FormId,
    string RecordType,
    string EditorId,
    string Label,
    string RecordSha256,
    string? ModelLogicalPath,
    OwnedUiTexture Texture);

internal sealed record OpeningGameplayVitalsContract(
    string Schema,
    OpeningVitalsPlayerBase PlayerBase,
    IReadOnlyDictionary<string, OpeningVitalsActorValue> ActorValues,
    IReadOnlyDictionary<string, OpeningVitalsGameSetting> GameSettings,
    int InitialExperiencePoints,
    IReadOnlyDictionary<string, string> Derivations)
{
    private const string ExpectedSchema = "opennv-owned-gameplay-vitals/v1";
    private const string ExactEngineBuild = "1.4.0.525";
    private const string XpBaseEvidenceId = "fnv-1.4.0.525-gmst-ixpbase-v1";
    private const string HitPointFormula =
        "baseHealth + endurance * fAVDHealthEnduranceMult + " +
        "(level - 1) * fAVDHealthLevelMult";
    private const string ActionPointFormula =
        "fAVDActionPointsBase + agility * fAVDActionPointsMult";
    private const string ExperienceFormula =
        "(targetLevel - 1) * (((targetLevel - 2) * iXPBumpBase) / 2 + iXPBase)";
    private static readonly string[] RequiredActorValues =
        ["AVHealth", "AVActionPoints", "AVXP", "AVEndurance", "AVAgility"];
    private static readonly string[] RequiredGameSettings =
    [
        "fAVDHealthEnduranceMult",
        "fAVDHealthLevelMult",
        "fAVDActionPointsBase",
        "fAVDActionPointsMult",
        "iXPBumpBase",
        "iXPBase",
    ];

    internal static OpeningGameplayVitalsContract Parse(JsonElement source)
    {
        var result = new OpeningGameplayVitalsContract(
            source.GetProperty("schema").GetString()!,
            ParsePlayerBase(source.GetProperty("playerBase")),
            source.GetProperty("actorValues").EnumerateArray()
                .Select(value => new OpeningVitalsActorValue(
                    value.GetProperty("editorId").GetString()!,
                    value.GetProperty("formId").GetString()!,
                    value.GetProperty("recordSha256").GetString()!))
                .ToDictionary(value => value.EditorId, StringComparer.OrdinalIgnoreCase),
            source.GetProperty("gameSettings").EnumerateArray()
                .Select(ParseGameSetting)
                .ToDictionary(value => value.EditorId, StringComparer.OrdinalIgnoreCase),
            source.GetProperty("initialExperiencePoints").GetInt32(),
            source.GetProperty("derivations").EnumerateObject().ToDictionary(
                value => value.Name,
                value => value.Value.GetString()!,
                StringComparer.Ordinal));
        result.Validate();
        return result;
    }

    internal GameplayVitals CreateInitial(OpeningCampaignState opening)
    {
        opening.Validate();
        var endurance = ReadSpecial(opening, "AVEndurance");
        var agility = ReadSpecial(opening, "AVAgility");
        var maximumHitPoints = ExactInt(
            PlayerBase.BaseHealth +
            endurance * Setting("fAVDHealthEnduranceMult") +
            (PlayerBase.InitialLevel - 1) * Setting("fAVDHealthLevelMult"),
            "maximum hit points");
        var maximumActionPoints = ExactInt(
            Setting("fAVDActionPointsBase") +
            agility * Setting("fAVDActionPointsMult"),
            "maximum action points");
        var targetLevel = PlayerBase.InitialLevel + 1;
        var nextLevelExperiencePoints = ExactInt(
            (targetLevel - 1) *
            (((targetLevel - 2) * Setting("iXPBumpBase")) / 2.0 +
             Setting("iXPBase")),
            "next-level experience threshold");
        var result = new GameplayVitals(
            PlayerBase.InitialLevel,
            maximumHitPoints,
            maximumHitPoints,
            maximumActionPoints,
            maximumActionPoints,
            InitialExperiencePoints,
            nextLevelExperiencePoints);
        result.Validate();
        return result;
    }

    private static OpeningVitalsPlayerBase ParsePlayerBase(JsonElement source) => new(
        source.GetProperty("editorId").GetString()!,
        source.GetProperty("formId").GetString()!,
        source.GetProperty("recordSha256").GetString()!,
        source.GetProperty("initialLevel").GetInt32(),
        source.GetProperty("baseHealth").GetInt32());

    private static OpeningVitalsGameSetting ParseGameSetting(JsonElement source) => new(
        source.GetProperty("editorId").GetString()!,
        source.GetProperty("formId").ValueKind == JsonValueKind.String
            ? source.GetProperty("formId").GetString()
            : null,
        source.GetProperty("recordSha256").ValueKind == JsonValueKind.String
            ? source.GetProperty("recordSha256").GetString()
            : null,
        source.GetProperty("sourceKind").GetString()!,
        source.TryGetProperty("engineBuild", out var build) &&
            build.ValueKind == JsonValueKind.String
                ? build.GetString()
                : null,
        source.TryGetProperty("evidenceId", out var evidence) &&
            evidence.ValueKind == JsonValueKind.String
                ? evidence.GetString()
                : null,
        source.GetProperty("value").GetDouble());

    private void Validate()
    {
        if (Schema != ExpectedSchema || PlayerBase.EditorId != "Player" ||
            FalloutFormId.Normalize(PlayerBase.FormId) != PlayerBase.FormId ||
            PlayerBase.RecordSha256.Length != 64 || PlayerBase.InitialLevel <= 0 ||
            PlayerBase.BaseHealth <= 0 || InitialExperiencePoints < 0 ||
            ActorValues.Count != RequiredActorValues.Length ||
            RequiredActorValues.Any(value => !ActorValues.ContainsKey(value)) ||
            ActorValues.Values.Any(value =>
                FalloutFormId.Normalize(value.FormId) != value.FormId ||
                value.RecordSha256.Length != 64) ||
            GameSettings.Count != RequiredGameSettings.Length ||
            RequiredGameSettings.Any(value => !GameSettings.ContainsKey(value)) ||
            GameSettings.Values.Any(value => !double.IsFinite(value.Value)) ||
            GameSettings.Where(value => value.Key != "iXPBase").Any(value =>
                value.Value.SourceKind != "owned-master-gmst" ||
                value.Value.FormId is null || value.Value.RecordSha256?.Length != 64 ||
                FalloutFormId.Normalize(value.Value.FormId) != value.Value.FormId) ||
            GameSettings["iXPBase"] is not
            {
                SourceKind: "falloutnv-exact-build-engine-default",
                FormId: null,
                RecordSha256: null,
                EngineBuild: ExactEngineBuild,
                EvidenceId: XpBaseEvidenceId,
                Value: 200d,
            } ||
            Derivations.Count != 3 ||
            Derivations.GetValueOrDefault("maximumHitPoints") != HitPointFormula ||
            Derivations.GetValueOrDefault("maximumActionPoints") != ActionPointFormula ||
            Derivations.GetValueOrDefault("experienceThreshold") != ExperienceFormula)
            throw new InvalidOperationException("Owned gameplay-vitals contract is invalid.");
    }

    private int ReadSpecial(OpeningCampaignState opening, string editorId)
    {
        var actorValue = ActorValues[editorId];
        if (!opening.SpecialValues.TryGetValue(actorValue.FormId, out var value) || value <= 0)
            throw new InvalidOperationException(
                $"Opening character state has no positive {editorId} value.");
        return value;
    }

    private double Setting(string editorId) => GameSettings[editorId].Value;

    private static int ExactInt(double value, string name)
    {
        var rounded = Math.Round(value);
        if (!double.IsFinite(value) || Math.Abs(value - rounded) > 0.000001 ||
            rounded <= 0 || rounded > int.MaxValue)
            throw new InvalidOperationException(
                $"Owned gameplay-vitals derivation did not produce an exact positive {name}.");
        return checked((int)rounded);
    }
}

internal sealed record OpeningVitalsPlayerBase(
    string EditorId,
    string FormId,
    string RecordSha256,
    int InitialLevel,
    int BaseHealth);

internal sealed record OpeningVitalsActorValue(
    string EditorId,
    string FormId,
    string RecordSha256);

internal sealed record OpeningVitalsGameSetting(
    string EditorId,
    string? FormId,
    string? RecordSha256,
    string SourceKind,
    string? EngineBuild,
    string? EvidenceId,
    double Value);

internal sealed record OpeningCharacterValue(
    string FormId,
    string EditorId,
    string SourceName,
    string Name,
    string Description,
    string? IconPath);

internal sealed record OpeningDocReaction(
    float AverageValue,
    float HighDeviationThreshold,
    float LowDeviationThreshold,
    int DefaultReaction,
    IReadOnlyList<OpeningDocReactionValue> Values);

internal sealed record OpeningDocReactionValue(
    string FormId,
    int EvaluationOrder,
    int LowReaction,
    int HighReaction);
