using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Presentation.Ui;
using OpenNV.Runtime.Presentation.CharacterCreation;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal sealed partial record OpeningNewGameFlow(
    OpeningCommandContract CommandContract,
    string QuestFormId,
    string QuestEditorId,
    IReadOnlyDictionary<int, string> Objectives,
    IReadOnlyDictionary<string, OpeningOrdinaryQuest> OrdinaryQuests,
    IReadOnlyList<OpeningOrdinaryActor> OrdinaryActors,
    IReadOnlyList<OpeningHitTargetSet> HitTargetSets,
    int CompletionStage,
    int PsychologyStartStage,
    int OutroStartStage,
    string OutroTopicFormId,
    Vector2 ReferenceCanvasSize,
    IReadOnlyDictionary<string, OpeningFlowMenu> Menus,
    IReadOnlyDictionary<string, string> Strings,
    IReadOnlyDictionary<int, OpeningStageProgram> Stages,
    IReadOnlyDictionary<int, OpeningTimerTransition> TimerTransitions,
    IReadOnlyDictionary<int, int> MenuCloseTransitions,
    IReadOnlyDictionary<string, OpeningSceneRole> SceneRoles,
    IReadOnlyList<OpeningInteraction> Interactions,
    IReadOnlyDictionary<string, OpeningDialogueTopic> TopicsByFormId,
    IReadOnlyDictionary<string, OpeningDialogueTopic> TopicsByEditorId,
    OpeningDialogueInfo PsychologyRootInfo,
    OpeningDialogueVoice DialogueVoice,
    OpeningGuideActorAi GuideActorAi,
    OpeningPlayerAnimationGraph PlayerAnimation,
    IReadOnlyDictionary<string, OpeningImageSpaceModifier> ImageSpaceModifiers,
    OpeningCharacterCreation Character)
{
    private const string ExpectedSchema = "opennv-owned-new-game-flow/v7";
    private const string ExpectedCommandContractSchema =
        "opennv-owned-opening-command-contract/v1";
    private const string ExpectedGuideActorAiSchema =
        "opennv-owned-guide-actor-ai/v3";
    private const string ExpectedGuideFurnitureOccupancySchema =
        "opennv-owned-guide-furniture-occupancy/v4";
    private const string ExpectedGuideFurnitureHeadingDeltaEditorId =
        "fFurnitureMarker14HeadingDelta";
    private const string ExpectedGuideFurniturePlacementSemantics =
        "nif-marker-minus-gmst-target-offset-for-actor-placement";
    private const string ExpectedGuideFurniturePlacementXEditorId =
        "fFurnitureMarker14DeltaX";
    private const string ExpectedGuideFurniturePlacementYEditorId =
        "fFurnitureMarker14DeltaY";
    private const string ExpectedGuideFurniturePlacementZEditorId =
        "fFurnitureMarker14DeltaZ";
    private const string ExpectedOwnedGameSettingSourceKind = "owned-master-gmst";
    private const string ExpectedPlayerAppearanceSchema =
        "opennv-owned-player-appearance/v1";
    private const string ExpectedPlayerAppearanceStatus =
        "source-backed-interactive-selection";
    private const string ExpectedPlayerAnimationSchema =
        "opennv-owned-player-animation-graph/v1";
    private const int GuideConditionGreaterOrEqual = 0x60;
    private const int DocInitialChairMarkerId = 14;
    private const int DocInitialChairMarkerOrientation = 3141;
    private const float FurnitureMarkerOrientationUnitsPerRadian = 1000.0f;
    private const int FaceGenSymmetricGeometryCount = 50;
    private const int FaceGenAsymmetricGeometryCount = 30;
    private const int FaceGenSymmetricTextureCount = 50;
    private const int FaceGenAsymmetricTextureCount = 0;
    private const int FaceGenSymmetricGeometryControlCount = 56;
    private const int FaceGenAsymmetricGeometryControlCount = 26;
    private const int FaceGenSymmetricTextureControlCount = 33;
    private const int FaceGenAsymmetricTextureControlCount = 0;
    private const int FaceGenNativeGeometryControlCount = 43;
    private const string ExpectedFaceGenControlSpaceSchema =
        "opennv-owned-facegen-control-space/v1";
    private const string ExpectedFaceGenControlSpaceStatus =
        "source-bound-controls-default-preview-artifact-compiled-all-native-geometry-" +
        "controls-runtime-bound-sibling-gamebryo-slider-semantics-corroborated";
    private const string ExpectedFaceGenControlRuntimeDisposition =
        "control-axes-and-default-preview-egm-targets-compiled-all-native-geometry-" +
        "controls-runtime-bound-sibling-gamebryo-slider-semantics-corroborated";
    private const string ExpectedFaceGenEngineBuild = "1.4.0.525";
    private const string ExpectedPlayerFaceGenPreviewSchema =
        "opennv-owned-player-facegen-preview-set/v5";
    private const string ExpectedPlayerFaceGenPreviewStatus =
        "compiled-playable-race-male-and-female-valid-hair-eye-full-body-live-previews-" +
        "with-ctl-egm-targets-all-native-geometry-controls-runtime-bound";
    private const string ExpectedPlayerFaceGenPreviewRuntimeDisposition =
        "owned-playable-race-male-and-female-valid-hair-eye-identity-preview-hosts-" +
        "and-all-native-geometry-controls-bound-invalid-source-tuples-fail-closed-" +
        "sibling-gamebryo-slider-semantics-corroborated";
    private const string ExpectedPlayerFaceGenPreviewSelectionScope =
        "all-playable-race-sex-valid-hair-eyes-cartesian-product";
    private const string ExpectedPlayerFaceGenUnsupportedSelectionScope =
        "invalid-race-sex-hair-eyes-source-tuple";
    private static readonly string[] ExpectedPlayerFaceGenBodyComponentRoles =
        ["body", "left-hand", "right-hand"];
    private const string ExpectedFaceGenPreviewControlSemantics =
        "sibling-gamebryo-racesexmenu-ui-units-with-ctl-egm-weight-scale";
    private const string ExpectedFaceGenSliderEvidenceClassification =
        "independent-sibling-gamebryo-racesexmenu-static-contract";
    private const string ExpectedFaceGenSliderEvidenceEngineBuild = "1.7.0.4";
    private const string ExpectedFaceGenSliderEvidenceExecutableSha256Prefix =
        "c3f97c2255fa041a851c17cf372d69aa";
    private const string ExpectedFaceGenSliderEvidenceExecutableSha256Suffix =
        "add8694e2dc4230ba556001bbfbd2f3e";
    private const string ExpectedFaceGenSliderLowGlobalAddress = "0x1115438";
    private const string ExpectedFaceGenSliderHighGlobalAddress = "0x1115444";
    private const string ExpectedFaceGenSliderIncrementTrait = "user6";
    private const float ExpectedFaceGenSliderSourceMinimum = -5.0f;
    private const float ExpectedFaceGenSliderSourceMaximum = 5.0f;
    private const float ExpectedFaceGenSliderUiScale = 10.0f;
    private const float ExpectedFaceGenSliderUiMinimum = -50.0f;
    private const float ExpectedFaceGenSliderUiMaximum = 50.0f;
    private const float ExpectedFaceGenSliderOrdinaryIncrement = 1.0f;
    private const float ExpectedFaceGenSliderJump = 25.0f;
    private const float ExpectedFaceGenSliderMorphWeightScale = 0.1f;
    private static readonly HashSet<string> RuntimeCommandKinds = new(
        new[]
        {
            "achievement",
            "actorIntent",
            "actorValueDelta",
            "additem",
            "addScriptPackage",
            "autoDisplayObjectives",
            "autosave",
            "deferredStage",
            "equipitem",
            "imageSpaceModifier",
            "objective",
            "playerControls",
            "playIdle",
            "referenceEnabled",
            "removeitem",
            "removeScriptPackage",
            "sayTo",
            "setDestroyed",
            "setGlobal",
            "setQuestVariable",
            "setStage",
            "setTimer",
            "showMenu",
            "startQuest",
            "stopQuest",
        },
        StringComparer.Ordinal);

    internal static OpeningNewGameFlow Load(
        JsonElement source,
        JsonElement uiFlow,
        IReadOnlyDictionary<string, OwnedUiTexture> textures,
        IReadOnlyDictionary<string, JsonElement> uiDocuments)
    {
        if (source.GetProperty("schema").GetString() != ExpectedSchema)
            throw new InvalidOperationException("Owned New Game flow has an unexpected contract.");

        var menus = uiFlow.GetProperty("menus").EnumerateArray()
            .Select(value => ParseMenu(value, textures, uiDocuments))
            .ToDictionary(value => value.Role, StringComparer.OrdinalIgnoreCase);
        var referenceCanvas = OpeningManifest.ReadVector(
            uiFlow.GetProperty("referenceCanvasSize"));
        if (menus.Values.Any(menu =>
                menu.TextEditMenu is { } textEdit &&
                textEdit.CanvasSize != referenceCanvas))
            throw new InvalidOperationException(
                "Owned TextEditMenu reference canvas differs.");
        var strings = uiFlow.GetProperty("strings").EnumerateObject()
            .ToDictionary(
                value => value.Name,
                value => value.Value.GetString()!,
                StringComparer.OrdinalIgnoreCase);
        var quest = source.GetProperty("quest");
        var stages = quest.GetProperty("stages").EnumerateArray()
            .Select(ParseStage)
            .ToDictionary(value => value.Stage);
        var objectives = quest.GetProperty("objectives").EnumerateArray()
            .ToDictionary(
                value => value.GetProperty("index").GetInt32(),
                value => value.GetProperty("text").GetString()!);
        var ordinaryQuests = source.GetProperty("ordinaryQuests").EnumerateArray()
            .Select(ParseOrdinaryQuest)
            .ToDictionary(value => value.FormId, StringComparer.OrdinalIgnoreCase);
        var ordinaryActors = source.GetProperty("ordinaryActors").EnumerateArray()
            .Select(ParseOrdinaryActor)
            .ToArray();
        var timerTransitions = quest.GetProperty("timerTransitions").EnumerateArray()
            .Select(value => new OpeningTimerTransition(
                value.GetProperty("fromStage").GetInt32(),
                value.GetProperty("toStage").GetInt32()))
            .ToDictionary(value => value.FromStage);
        var menuCloseTransitions = quest.GetProperty("menuCloseTransitions")
            .EnumerateArray()
            .ToDictionary(
                value => value.GetProperty("fromStage").GetInt32(),
                value => value.GetProperty("toStage").GetInt32());
        var sceneRoles = source.GetProperty("sceneRoles").EnumerateArray()
            .Select(value => new OpeningSceneRole(
                value.GetProperty("role").GetString()!,
                value.GetProperty("editorId").GetString()!,
                value.GetProperty("displayName").GetString()!,
                value.GetProperty("recordType").GetString()!,
                value.GetProperty("referenceFormId").GetString()!,
                value.GetProperty("baseFormId").GetString()!))
            .ToDictionary(value => value.Role, StringComparer.OrdinalIgnoreCase);
        var interactions = source.GetProperty("interactions").EnumerateArray()
            .Select(ParseInteraction)
            .ToArray();
        var dialogue = source.GetProperty("dialogue");
        var topics = dialogue.GetProperty("topics").EnumerateArray()
            .Select(ParseTopic)
            .ToArray();
        var topicsByForm = topics.ToDictionary(
            value => value.FormId,
            StringComparer.OrdinalIgnoreCase);
        var topicsByEditor = topics
            .Where(value => !string.IsNullOrWhiteSpace(value.EditorId))
            .ToDictionary(
                value => value.EditorId,
                StringComparer.OrdinalIgnoreCase);
        var character = ParseCharacter(source.GetProperty("character"), textures);
        var guideActorAi = ParseGuideActorAi(source.GetProperty("guideActorAi"));
        var playerAnimation = ParsePlayerAnimation(source.GetProperty("playerAnimation"));
        var imageSpaceModifiers = source.GetProperty("imageSpaceModifiers")
            .EnumerateArray()
            .Select(ParseImageSpaceModifier)
            .ToDictionary(value => value.EditorId, StringComparer.OrdinalIgnoreCase);

        var result = new OpeningNewGameFlow(
            ParseCommandContract(source.GetProperty("commandContract")),
            quest.GetProperty("formId").GetString()!,
            quest.GetProperty("editorId").GetString()!,
            objectives,
            ordinaryQuests,
            ordinaryActors,
            source.GetProperty("hitTargetSets").EnumerateArray()
                .Select(ParseHitTargetSet)
                .ToArray(),
            quest.GetProperty("completionStage").GetInt32(),
            dialogue.GetProperty("psychologyStartStage").GetInt32(),
            dialogue.GetProperty("outroStartStage").GetInt32(),
            dialogue.GetProperty("outroTopicFormId").GetString()!,
            referenceCanvas,
            menus,
            strings,
            stages,
            timerTransitions,
            menuCloseTransitions,
            sceneRoles,
            interactions,
            topicsByForm,
            topicsByEditor,
            ParseInfo(dialogue.GetProperty("psychologyRootInfo")),
            ParseDialogueVoice(dialogue.GetProperty("voice")),
            guideActorAi,
            playerAnimation,
            imageSpaceModifiers,
            character);
        Validate(result);
        return result;
    }

    private static OpeningOrdinaryQuest ParseOrdinaryQuest(JsonElement source)
    {
        var objectives = source.GetProperty("objectives").EnumerateArray()
            .ToDictionary(
                value => value.GetProperty("index").GetInt32(),
                value => value.GetProperty("text").GetString()!);
        return new OpeningOrdinaryQuest(
            source.GetProperty("formId").GetString()!,
            source.GetProperty("editorId").GetString()!,
            source.GetProperty("scriptFormId").GetString()!,
            source.GetProperty("scriptEditorId").GetString()!,
            source.GetProperty("entryStage").GetInt32(),
            source.GetProperty("variables").EnumerateArray()
                .ToDictionary(
                    value => value.GetProperty("index").GetUInt32(),
                    value => value.GetProperty("name").GetString()!),
            objectives,
            source.GetProperty("stages").EnumerateArray()
                .Select(ParseStage)
                .ToDictionary(value => value.Stage),
            ParseCommandContract(source.GetProperty("commandContract")));
    }

    private static OpeningOrdinaryActor ParseOrdinaryActor(JsonElement source)
    {
        var topics = source.GetProperty("topics").EnumerateArray()
            .Select(ParseTopic)
            .ToArray();
        return new OpeningOrdinaryActor(
            source.GetProperty("role").GetString()!,
            source.GetProperty("referenceFormId").GetString()!,
            source.GetProperty("baseFormId").GetString()!,
            source.GetProperty("packagePriority").EnumerateArray()
                .Select(value => value.GetString()!)
                .ToArray(),
            source.GetProperty("packages").EnumerateArray()
                .Select(ParseGuidePackage)
                .ToDictionary(value => value.FormId, StringComparer.OrdinalIgnoreCase),
            source.GetProperty("activationTopicFormId").GetString()!,
            topics.ToDictionary(value => value.FormId, StringComparer.OrdinalIgnoreCase),
            ParseDialogueVoice(source.GetProperty("voice")),
            source.GetProperty("arrivalTransitions").EnumerateArray()
                .Select(value => new OpeningOrdinaryPackageArrival(
                    value.GetProperty("packageFormId").GetString()!,
                    value.GetProperty("scriptFormId").GetString()!,
                    value.GetProperty("scriptEditorId").GetString()!,
                    value.GetProperty("actorReferenceFormId").GetString()!,
                    value.GetProperty("questFormId").GetString()!,
                    value.GetProperty("fromStage").GetInt32(),
                    value.GetProperty("toStage").GetInt32()))
                .ToArray(),
            source.GetProperty("automaticDialogueTriggers").EnumerateArray()
                .Select(value => new OpeningOrdinaryDialogueTrigger(
                    value.GetProperty("scriptFormId").GetString()!,
                    value.GetProperty("scriptEditorId").GetString()!,
                    value.GetProperty("triggerReferenceFormId").GetString()!,
                    value.GetProperty("triggerReferenceEditorId").GetString()!,
                    ReadVector3(value.GetProperty("positionGameUnits")),
                    ReadQuaternion(value.GetProperty("rotationGodotQuaternion")),
                    ReadVector3(value.GetProperty("boundsGameUnits")),
                    value.GetProperty("questFormId").GetString()!,
                    value.GetProperty("objectiveIndex").GetInt32(),
                    value.GetProperty("topicFormId").GetString()!))
                .ToArray(),
            source.GetProperty("automaticPackageDialogues").EnumerateArray()
                .Select(value => new OpeningOrdinaryPackageDialogue(
                    value.GetProperty("packageFormId").GetString()!,
                    value.GetProperty("greetingTopicFormId").GetString()!))
                .ToArray(),
            ParseCommandContract(source.GetProperty("commandContract")));
    }

    private static OpeningHitTargetSet ParseHitTargetSet(JsonElement source) => new(
        source.GetProperty("scriptFormId").GetString()!,
        source.GetProperty("scriptEditorId").GetString()!,
        source.GetProperty("enableParentFormId").GetString()!,
        source.GetProperty("targets").EnumerateArray()
            .Select(value => new OpeningHitTarget(
                value.GetProperty("referenceFormId").GetString()!,
                value.GetProperty("baseFormId").GetString()!))
            .ToArray(),
        source.GetProperty("questFormId").GetString()!,
        source.GetProperty("questVariableIndex").GetInt32(),
        source.GetProperty("questVariableName").GetString()!,
        source.GetProperty("weaponAnimationTypeMinimumExclusive").GetInt32(),
        source.GetProperty("weaponAnimationTypeMaximumExclusive").GetInt32(),
        source.GetProperty("excludedWeaponFormId").GetString()!,
        source.GetProperty("reactionTopicFormId").GetString()!,
        source.GetProperty("speakerReferenceFormId").GetString()!,
        source.GetProperty("tutorialQuestFormId").GetString()!,
        source.GetProperty("tutorialStage").GetInt32(),
        source.GetProperty("threshold").GetInt32(),
        source.GetProperty("objectiveIndex").GetInt32());

    private static OpeningCommandContract ParseCommandContract(JsonElement source) => new(
        source.GetProperty("schema").GetString()!,
        source.GetProperty("commandCount").GetInt32(),
        source.GetProperty("kindCounts").EnumerateObject().ToDictionary(
            value => value.Name,
            value => value.Value.GetInt32(),
            StringComparer.Ordinal),
        source.GetProperty("recordIdentityCounts").EnumerateObject().ToDictionary(
            value => value.Name,
            value => value.Value.GetInt32(),
            StringComparer.Ordinal),
        source.GetProperty("allEmittedKindsRuntimeBlocking").GetBoolean(),
        source.GetProperty("allDeclaredRecordReferencesResolved").GetBoolean());

    private static OpeningFlowMenu ParseMenu(
        JsonElement value,
        IReadOnlyDictionary<string, OwnedUiTexture> textures,
        IReadOnlyDictionary<string, JsonElement> uiDocuments)
    {
        var source = value.GetProperty("source").GetString()!;
        OpeningManifest.VerifyHash(source, value.GetProperty("sha256").GetString()!);
        var document = value.GetProperty("document").GetString()!;
        if (!uiDocuments.TryGetValue(document, out var documentContract))
            throw new InvalidOperationException(
                $"Owned flow menu document contract is absent: {document}");
        if (!documentContract.GetProperty("sha256").GetString()!.Equals(
                value.GetProperty("sha256").GetString(),
                StringComparison.OrdinalIgnoreCase) ||
            !System.IO.Path.GetFullPath(
                documentContract.GetProperty("source").GetString()!).Equals(
                System.IO.Path.GetFullPath(source),
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Owned flow menu document identity differs: {document}");
        var backgroundAssets = documentContract
            .GetProperty("initiallyVisibleAssetReferences")
            .EnumerateArray()
            .Select(asset => asset.GetString()!)
            .Where(asset => asset.Contains(
                "\\background\\",
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (backgroundAssets.Length > 1)
            throw new InvalidOperationException(
                $"Owned flow menu background is ambiguous: {document}");
        OwnedUiTexture? background = null;
        if (backgroundAssets.Length == 1 &&
            !textures.TryGetValue(backgroundAssets[0], out background))
            throw new InvalidOperationException(
                $"Owned flow menu background is unavailable: {backgroundAssets[0]}");
        return new OpeningFlowMenu(
            value.GetProperty("role").GetString()!,
            document,
            value.GetProperty("menuName").GetString()!,
            System.IO.Path.GetFullPath(source),
            value.TryGetProperty("rect", out var rect)
                ? OpeningManifest.ReadRect(rect)
                : null,
            value.TryGetProperty("semanticRects", out var semanticRects)
                ? semanticRects.EnumerateObject().ToDictionary(
                    semantic => semantic.Name,
                    semantic => new OpeningFlowSemanticRect(
                        semantic.Value.GetProperty("tile").GetString()!,
                        OpeningManifest.ReadRect(
                            semantic.Value.GetProperty("rect"))),
                    StringComparer.Ordinal)
                : new Dictionary<string, OpeningFlowSemanticRect>(
                    StringComparer.Ordinal),
            background,
            value.TryGetProperty("dialogueMenuTiles", out var dialogueMenuTiles)
                ? OwnedGamebryoTileRuntime.ParseDialogueMenu(dialogueMenuTiles)
                : null,
            value.TryGetProperty("textEditMenuTiles", out var textEditMenuTiles)
                ? OwnedGamebryoTileRuntime.ParseTextEditMenu(textEditMenuTiles)
                : null,
            value.TryGetProperty("raceSexMenuTiles", out var raceSexMenuTiles)
                ? ParseRaceSexMenuTiles(
                    raceSexMenuTiles,
                    document,
                    value.GetProperty("sha256").GetString()!,
                    textures,
                    uiDocuments)
                : null,
            value.TryGetProperty("renderedDevice", out var renderedDevice)
                ? ParseRaceSexRenderedDevice(renderedDevice)
                : null);
    }

    private static OpeningRaceSexRenderedDevice ParseRaceSexRenderedDevice(
        JsonElement source)
    {
        var surfaceRoles = new HashSet<string>(
            new[]
            {
                "sexButton",
                "raceButton",
                "faceButton",
                "hairButton",
                "sexGlow",
                "raceGlow",
                "faceGlow",
                "hairGlow",
                "deviceShell0",
                "deviceShell1",
                "deviceShell2",
            },
            StringComparer.OrdinalIgnoreCase);
        var device = OpeningManifest.ParseOwnedPhysicalDevice(
            source,
            "opennv-owned-racesex-rendered-device/v1",
            "meshes\\terminals\\nv_reflectron_ui.nif",
            "surfaceRoles",
            surfaceRoles,
            "RaceSex rendered terminal");
        var settingsSource = source.GetProperty("settingsSource");
        var settingsSourcePath = settingsSource.GetProperty("path").GetString()!;
        var settingsSourceSha256 = settingsSource.GetProperty("sha256").GetString()!;
        OpeningManifest.VerifyHash(settingsSourcePath, settingsSourceSha256);
        var settings = source.GetProperty("settings")
            .EnumerateObject()
            .ToDictionary(
                value => value.Name,
                value => ParseRaceSexRenderedSetting(value.Value),
                StringComparer.Ordinal);
        var requiredSettings = new HashSet<string>(
            new[]
            {
                "enabled",
                "terminalFov",
                "terminalZoom",
                "scanlines",
                "scanlineScale",
                "terminalHorizontalPosition",
                "terminalVerticalPosition",
                "screenLightBaseIntensity",
                "screenLightRadius",
                "screenLightColorRed",
                "screenLightColorGreen",
                "screenLightColorBlue",
                "raceSexHorizontalPosition",
                "raceSexVerticalPosition",
                "raceSexZoom",
                "raceSexScale",
                "menuPlayerLightDiffuseRed",
                "menuPlayerLightDiffuseGreen",
                "menuPlayerLightDiffuseBlue",
                "menuPlayerLightAmbientRed",
                "menuPlayerLightAmbientGreen",
                "menuPlayerLightAmbientBlue",
                "defaultFovDegrees",
                "nearDistanceGameUnits",
                "farDistanceGameUnits",
            },
            StringComparer.Ordinal);
        var menuLightingRoles = new HashSet<string>(
            new[]
            {
                "menuPlayerLightDiffuseRed",
                "menuPlayerLightDiffuseGreen",
                "menuPlayerLightDiffuseBlue",
                "menuPlayerLightAmbientRed",
                "menuPlayerLightAmbientGreen",
                "menuPlayerLightAmbientBlue",
            },
            StringComparer.Ordinal);
        var displayRoles = new HashSet<string>(
            new[]
            {
                "defaultFovDegrees",
                "nearDistanceGameUnits",
                "farDistanceGameUnits",
            },
            StringComparer.Ordinal);
        if (!settings.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(requiredSettings) ||
            settings.Any(value => value.Value.Section !=
                (menuLightingRoles.Contains(value.Key)
                    ? "Interface"
                    : displayRoles.Contains(value.Key)
                        ? "Display"
                        : "RenderedTerminal")))
            throw new InvalidOperationException(
                "Owned RaceSex rendered-terminal settings are incomplete.");
        var framing = ParseRaceSexRenderedDeviceFraming(
            source.GetProperty("framingContract"));
        var previewCamera = ParseRaceSexPreviewCameraContract(
            source.GetProperty("previewCameraContract"));
        return new OpeningRaceSexRenderedDevice(
            device,
            settingsSourcePath,
            settingsSourceSha256,
            settings,
            framing,
            previewCamera);
    }

    private static OpeningRaceSexRenderedDeviceFraming ParseRaceSexRenderedDeviceFraming(
        JsonElement source)
    {
        var sourcePath = source.GetProperty("source").GetString()!;
        var sourceSha256 = source.GetProperty("sha256").GetString()!;
        OpeningManifest.VerifyHash(sourcePath, sourceSha256);
        var document = source.GetProperty("document");
        var viewport = IntVector2(document.GetProperty("viewportPixels"));
        var retail = document.GetProperty("retail");
        var current = document.GetProperty("current");
        var solve = document.GetProperty("solve");
        var retailOuterBounds = IntRect(retail.GetProperty("outerDeviceBoundsPixels"));
        var retailScreenBounds = IntRect(retail.GetProperty("rightScreenBoundsPixels"));
        var currentOuterBounds = IntRect(current.GetProperty("outerDeviceBoundsPixels"));
        var currentScreenBounds = IntRect(current.GetProperty("rightScreenBoundsPixels"));
        var currentZoom = current.GetProperty("zoomGameUnits").GetDouble();
        var projectionScale = solve.GetProperty("projectionScale").GetDouble();
        var solvedZoom = solve.GetProperty("zoomGameUnits").GetDouble();
        var residual = DoubleVector2(solve.GetProperty("residualPixels"));
        var alignmentSource = document.GetProperty("alignment");
        var principalPoint = NumberArray(
            alignmentSource.GetProperty("principalPointPixels"),
            2);
        var projectedBounds = NumberArray(
            alignmentSource.GetProperty("projectedCurrentRightScreenBoundsPixels"),
            4);
        var deviceTranslation = NumberArray(
            alignmentSource.GetProperty("deviceTranslationPixels"),
            2);
        var referenceCanvas = IntVector2(
            alignmentSource.GetProperty("referenceCanvasPixels"));
        var deviceTranslationCanvas = NumberArray(
            alignmentSource.GetProperty("deviceTranslationCanvasUnits"),
            2);
        var retailContentBounds = IntRect(
            alignmentSource.GetProperty("retailContentBoundsPixels"));
        var currentContentBounds = IntRect(
            alignmentSource.GetProperty("currentContentBoundsPixels"));
        var contentScale = NumberArray(
            alignmentSource.GetProperty("contentScale"),
            2);
        var contentTranslation = NumberArray(
            alignmentSource.GetProperty("contentTranslationWithinScreenPixels"),
            2);
        var computedScale =
            ((double)currentScreenBounds.Size.X * retailScreenBounds.Size.X +
             (double)currentScreenBounds.Size.Y * retailScreenBounds.Size.Y) /
            ((double)currentScreenBounds.Size.X * currentScreenBounds.Size.X +
             (double)currentScreenBounds.Size.Y * currentScreenBounds.Size.Y);
        var computedZoom = currentZoom / computedScale;
        var computedResidual = new Vector2(
            (float)(currentScreenBounds.Size.X * computedScale -
                retailScreenBounds.Size.X),
            (float)(currentScreenBounds.Size.Y * computedScale -
                retailScreenBounds.Size.Y));
        var retailFrameSha256 = retail.GetProperty("frameSha256").GetString()!;
        var currentFrameSha256 = current.GetProperty("frameSha256").GetString()!;
        var baselineCurrentFrameSha256 = alignmentSource
            .GetProperty("baselineCurrentFrameSha256")
            .GetString()!;
        var expectedPrincipalX = viewport.X / 2.0;
        var expectedPrincipalY = viewport.Y / 2.0;
        var expectedProjectedX = expectedPrincipalX +
            (currentScreenBounds.Position.X - expectedPrincipalX) * computedScale;
        var expectedProjectedY = expectedPrincipalY +
            (currentScreenBounds.Position.Y - expectedPrincipalY) * computedScale;
        var expectedProjectedWidth = currentScreenBounds.Size.X * computedScale;
        var expectedProjectedHeight = currentScreenBounds.Size.Y * computedScale;
        var expectedTranslationX = retailScreenBounds.Position.X +
            retailScreenBounds.Size.X / 2.0 -
            (expectedProjectedX + expectedProjectedWidth / 2.0);
        var expectedTranslationY = retailScreenBounds.Position.Y +
            retailScreenBounds.Size.Y / 2.0 -
            (expectedProjectedY + expectedProjectedHeight / 2.0);
        var canvasScale = Math.Min(
            viewport.X / (double)referenceCanvas.X,
            viewport.Y / (double)referenceCanvas.Y);
        var expectedContentScaleX =
            retailContentBounds.Size.X / (double)currentContentBounds.Size.X;
        var expectedContentScaleY =
            retailContentBounds.Size.Y / (double)currentContentBounds.Size.Y;
        var expectedContentTranslationX =
            retailContentBounds.Position.X - retailScreenBounds.Position.X -
            (currentContentBounds.Position.X - expectedProjectedX) *
                expectedContentScaleX;
        var expectedContentTranslationY =
            retailContentBounds.Position.Y - retailScreenBounds.Position.Y -
            (currentContentBounds.Position.Y - expectedProjectedY) *
                expectedContentScaleY;
        var contentMask = alignmentSource.GetProperty("contentMask");
        if (document.GetProperty("schema").GetString() !=
                "opennv-fnv-racesex-rendered-device-framing/v1" ||
            document.GetProperty("status").GetString() !=
                "wip-hash-bound-retail-current-pixel-framing" ||
            viewport.X <= 0 || viewport.Y <= 0 ||
            retailOuterBounds.Size.X <= 0 || retailOuterBounds.Size.Y <= 0 ||
            retailScreenBounds.Size.X <= 0 || retailScreenBounds.Size.Y <= 0 ||
            currentOuterBounds.Size.X <= 0 || currentOuterBounds.Size.Y <= 0 ||
            currentScreenBounds.Size.X <= 0 || currentScreenBounds.Size.Y <= 0 ||
            !ValidSha256(retailFrameSha256) || !ValidSha256(currentFrameSha256) ||
            solve.GetProperty("model").GetString() !=
                "perspective-pixel-span-inverse-distance-least-squares" ||
            !solve.GetProperty("spanRoles").EnumerateArray()
                .Select(value => value.GetString())
                .SequenceEqual(new[] { "rightScreenWidth", "rightScreenHeight" }) ||
            !double.IsFinite(currentZoom) || currentZoom <= 0.0 ||
            !double.IsFinite(projectionScale) || projectionScale <= 0.0 ||
            !double.IsFinite(solvedZoom) || solvedZoom <= 0.0 ||
            Math.Abs(projectionScale - computedScale) > 1.0e-12 ||
            Math.Abs(solvedZoom - computedZoom) > 1.0e-10 ||
            Math.Abs(residual.X - computedResidual.X) > 1.0e-5 ||
            Math.Abs(residual.Y - computedResidual.Y) > 1.0e-5 ||
            !ValidSha256(baselineCurrentFrameSha256) ||
            Math.Abs(principalPoint[0] - expectedPrincipalX) > 1.0e-10 ||
            Math.Abs(principalPoint[1] - expectedPrincipalY) > 1.0e-10 ||
            Math.Abs(projectedBounds[0] - expectedProjectedX) > 1.0e-10 ||
            Math.Abs(projectedBounds[1] - expectedProjectedY) > 1.0e-10 ||
            Math.Abs(projectedBounds[2] - expectedProjectedWidth) > 1.0e-10 ||
            Math.Abs(projectedBounds[3] - expectedProjectedHeight) > 1.0e-10 ||
            Math.Abs(deviceTranslation[0] - expectedTranslationX) > 1.0e-10 ||
            Math.Abs(deviceTranslation[1] - expectedTranslationY) > 1.0e-10 ||
            referenceCanvas.X <= 0 || referenceCanvas.Y <= 0 ||
            !double.IsFinite(canvasScale) || canvasScale <= 0.0 ||
            Math.Abs(deviceTranslationCanvas[0] -
                expectedTranslationX / canvasScale) > 1.0e-10 ||
            Math.Abs(deviceTranslationCanvas[1] -
                expectedTranslationY / canvasScale) > 1.0e-10 ||
            retailContentBounds.Size.X <= 0 || retailContentBounds.Size.Y <= 0 ||
            currentContentBounds.Size.X <= 0 || currentContentBounds.Size.Y <= 0 ||
            contentMask.GetProperty("model").GetString() !=
                "rgb-green-channel-inequality-inside-right-screen" ||
            contentMask.GetProperty("greenMinimumExclusive").GetInt32() != 70 ||
            contentMask.GetProperty("greenTimes100GreaterThanRedTimes").GetInt32() != 115 ||
            contentMask.GetProperty("greenTimes100GreaterThanBlueTimes").GetInt32() != 105 ||
            Math.Abs(contentScale[0] - expectedContentScaleX) > 1.0e-10 ||
            Math.Abs(contentScale[1] - expectedContentScaleY) > 1.0e-10 ||
            Math.Abs(contentTranslation[0] - expectedContentTranslationX) > 1.0e-10 ||
            Math.Abs(contentTranslation[1] - expectedContentTranslationY) > 1.0e-10 ||
            document.GetProperty("promotion").GetProperty("parityReady").GetBoolean())
            throw new InvalidOperationException(
                "RaceSex rendered-device hash-bound framing contract differs.");
        return new OpeningRaceSexRenderedDeviceFraming(
            sourcePath,
            sourceSha256,
            document.GetProperty("status").GetString()!,
            viewport,
            retailFrameSha256,
            retailOuterBounds,
            retailScreenBounds,
            currentFrameSha256,
            currentOuterBounds,
            currentScreenBounds,
            currentZoom,
            projectionScale,
            solvedZoom,
            residual,
            new OpeningRaceSexRenderedDeviceAlignment(
                baselineCurrentFrameSha256,
                new Rect2(
                    (float)projectedBounds[0],
                    (float)projectedBounds[1],
                    (float)projectedBounds[2],
                    (float)projectedBounds[3]),
                new Vector2(
                    (float)deviceTranslation[0],
                    (float)deviceTranslation[1]),
                referenceCanvas,
                new Vector2(
                    (float)deviceTranslationCanvas[0],
                    (float)deviceTranslationCanvas[1]),
                retailContentBounds,
                currentContentBounds,
                new Vector2((float)contentScale[0], (float)contentScale[1]),
                new Vector2(
                    (float)contentTranslation[0],
                    (float)contentTranslation[1])),
            false);
    }

    private static OpeningRaceSexPreviewCameraContract ParseRaceSexPreviewCameraContract(
        JsonElement source)
    {
        var sourcePath = source.GetProperty("source").GetString()!;
        var sourceSha256 = source.GetProperty("sha256").GetString()!;
        OpeningManifest.VerifyHash(sourcePath, sourceSha256);
        var document = source.GetProperty("document");
        var status = document.GetProperty("status").GetString()!;
        var parityReady = document.GetProperty("parityReady").GetBoolean();
        var cameraContractReady = document.GetProperty("cameraContractReady").GetBoolean();
        var camera = document.GetProperty("camera");
        var unresolvedRoles = new[]
        {
            "projection", "target", "distance", "frustum", "aspectBehavior",
        };
        if (document.GetProperty("schema").GetString() !=
                "opennv-fnv-racesex-preview-camera/v1" ||
            document.GetProperty("engineBuild").GetString() != "1.4.0.525" ||
            status != "blocked-static-evidence-incomplete" ||
            parityReady || cameraContractReady ||
            unresolvedRoles.Any(role =>
                camera.GetProperty(role).GetProperty("status").GetString() != "unresolved" ||
                camera.GetProperty(role).GetProperty("value").ValueKind != JsonValueKind.Null))
            throw new InvalidOperationException(
                "FNV RaceSex preview-camera public contract differs.");
        return new OpeningRaceSexPreviewCameraContract(
            sourcePath,
            sourceSha256,
            status,
            parityReady,
            cameraContractReady);
    }

    private static Vector2I IntVector2(JsonElement source)
    {
        var values = source.EnumerateArray().Select(value => value.GetInt32()).ToArray();
        if (values.Length != 2)
            throw new InvalidOperationException("Expected a two-component integer vector.");
        return new Vector2I(values[0], values[1]);
    }

    private static Rect2I IntRect(JsonElement source)
    {
        var values = source.EnumerateArray().Select(value => value.GetInt32()).ToArray();
        if (values.Length != 4)
            throw new InvalidOperationException("Expected a four-component integer rect.");
        return new Rect2I(values[0], values[1], values[2], values[3]);
    }

    private static Vector2 DoubleVector2(JsonElement source)
    {
        var values = source.EnumerateArray().Select(value => value.GetDouble()).ToArray();
        if (values.Length != 2 || values.Any(value => !double.IsFinite(value)))
            throw new InvalidOperationException("Expected a finite two-component vector.");
        return new Vector2((float)values[0], (float)values[1]);
    }

    private static double[] NumberArray(JsonElement source, int expectedCount)
    {
        var values = source.EnumerateArray().Select(value => value.GetDouble()).ToArray();
        if (values.Length != expectedCount || values.Any(value => !double.IsFinite(value)))
            throw new InvalidOperationException(
                $"Expected a finite {expectedCount}-component numeric array.");
        return values;
    }

    private static OpeningRaceSexRenderedSetting ParseRaceSexRenderedSetting(
        JsonElement source)
    {
        var type = source.GetProperty("type").GetString()!;
        var value = source.GetProperty("value");
        return type switch
        {
            "bool" => new OpeningRaceSexRenderedSetting(
                source.GetProperty("section").GetString()!,
                source.GetProperty("key").GetString()!,
                type,
                value.GetBoolean(),
                null),
            "float" when float.IsFinite(value.GetSingle()) =>
                new OpeningRaceSexRenderedSetting(
                    source.GetProperty("section").GetString()!,
                    source.GetProperty("key").GetString()!,
                    type,
                    null,
                    value.GetSingle()),
            _ => throw new InvalidOperationException(
                "Owned RaceSex rendered-terminal setting type is unsupported."),
        };
    }

    private static OpeningRaceSexMenuTiles ParseRaceSexMenuTiles(
        JsonElement source,
        string document,
        string documentSha256,
        IReadOnlyDictionary<string, OwnedUiTexture> textures,
        IReadOnlyDictionary<string, JsonElement> uiDocuments)
    {
        const string expectedSchema = "opennv-owned-racesex-menu-tiles/v1";
        if (source.GetProperty("schema").GetString() != expectedSchema ||
            !source.GetProperty("document").GetString()!.Equals(
                document,
                StringComparison.OrdinalIgnoreCase) ||
            !source.GetProperty("documentSha256").GetString()!.Equals(
                documentSha256,
                StringComparison.OrdinalIgnoreCase) ||
            source.GetProperty("menuName").GetString() != "RaceSexMenu" ||
            source.GetProperty("activeListTrait").GetString() != "user0")
            throw new InvalidOperationException(
                "Owned RaceSexMenu tile contract identity differs.");
        var background = source.GetProperty("background");
        var faceGrab = source.GetProperty("faceGrab");
        var scroll = source.GetProperty("scroll");
        var navigation = source.GetProperty("navigation");
        var list = source.GetProperty("listItemTemplate");
        var slider = source.GetProperty("sliderTemplate");
        var font = source.GetProperty("font");
        var fontId = source.GetProperty("fontId").GetInt32();
        if (font.GetProperty("fontId").GetInt32() != fontId)
            throw new InvalidOperationException(
                "Owned RaceSexMenu font identity differs.");
        var result = new OpeningRaceSexMenuTiles(
            expectedSchema,
            document,
            documentSha256,
            source.GetProperty("menuName").GetString()!,
            source.GetProperty("menuClassEntity").GetString()!,
            source.GetProperty("activeListTrait").GetString()!,
            source.GetProperty("sliderLeftLabelTrait").GetString()!,
            source.GetProperty("sliderRightLabelTrait").GetString()!,
            fontId,
            OpeningManifest.ParseFont(font),
            OwnedGamebryoTileRuntime.ParseRaceSexControls(source),
            new OpeningRaceSexBackground(
                background.GetProperty("tile").GetString()!,
                OpeningManifest.ReadRect(background.GetProperty("rect")),
                ParseRaceSexTexture(background.GetProperty("texture"), textures),
                background.GetProperty("brightness").GetSingle(),
                background.GetProperty("depth").GetSingle(),
                background.GetProperty("topBound").GetSingle(),
                background.GetProperty("bottomBound").GetSingle()),
            new OpeningRaceSexFaceGrab(
                faceGrab.GetProperty("tile").GetString()!,
                ReadRaceSexIdentity(faceGrab.GetProperty("id")),
                OpeningManifest.ReadRect(faceGrab.GetProperty("rect")),
                faceGrab.GetProperty("depth").GetSingle()),
            new OpeningRaceSexScroll(
                ParseRaceSexScrollTarget(scroll.GetProperty("up"), textures),
                ParseRaceSexScrollTarget(scroll.GetProperty("down"), textures)),
            new OpeningRaceSexNavigation(
                ParseRaceSexNavigationButton(
                    navigation.GetProperty("back"),
                    uiDocuments),
                ParseRaceSexNavigationButton(
                    navigation.GetProperty("next"),
                    uiDocuments)),
            ParseRaceSexListTemplate(list, textures),
            ParseRaceSexSliderTemplate(slider));
        ValidateRaceSexMenuTiles(result);
        return result;
    }

    private static OpeningRaceSexScrollTarget ParseRaceSexScrollTarget(
        JsonElement source,
        IReadOnlyDictionary<string, OwnedUiTexture> textures) => new(
        source.GetProperty("tile").GetString()!,
        ReadRaceSexIdentity(source.GetProperty("id")),
        source.GetProperty("y").GetSingle(),
        source.GetProperty("brightness").GetSingle(),
        source.GetProperty("alphaPolicy").GetString()!,
        source.TryGetProperty("rect", out var rect)
            ? OpeningManifest.ReadRect(rect)
            : null,
        ParseRaceSexTexture(source.GetProperty("texture"), textures),
        source.GetProperty("clickSound").GetString()!);

    private static OpeningRaceSexNavigationButton ParseRaceSexNavigationButton(
        JsonElement source,
        IReadOnlyDictionary<string, JsonElement> uiDocuments)
    {
        var sourceDocuments = source.GetProperty("stringSourceDocuments")
            .EnumerateArray()
            .Select(value => new OpeningRaceSexStringSourceDocument(
                value.GetProperty("path").GetString()!,
                value.GetProperty("sha256").GetString()!))
            .ToArray();
        if (sourceDocuments.Length == 0 || sourceDocuments.Any(value =>
                !uiDocuments.TryGetValue(value.Path, out var document) ||
                !document.GetProperty("sha256").GetString()!.Equals(
                    value.Sha256,
                    StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException(
                "Owned RaceSexMenu navigation string provenance differs.");
        return new OpeningRaceSexNavigationButton(
            source.GetProperty("tile").GetString()!,
            ReadRaceSexIdentity(source.GetProperty("id")),
            source.GetProperty("x").GetSingle(),
            source.GetProperty("y").GetSingle(),
            source.GetProperty("font").GetSingle(),
            source.GetProperty("brightness").GetSingle(),
            source.GetProperty("horizontalBuffer").GetSingle(),
            source.GetProperty("verticalBuffer").GetSingle(),
            source.GetProperty("textYAdjust").GetSingle(),
            source.GetProperty("verticalCenterDivisor").GetSingle(),
            source.GetProperty("baseTextYOffset").GetSingle(),
            source.GetProperty("boxVisible").GetBoolean(),
            source.GetProperty("inheritBrightness").GetBoolean(),
            source.GetProperty("alphaPolicy").GetString()!,
            source.GetProperty("justify").GetString()!,
            source.GetProperty("labelRole").GetString()!,
            source.GetProperty("stringEntity").GetString()!,
            source.GetProperty("label").GetString()!,
            sourceDocuments,
            source.GetProperty("clickSound").GetString()!);
    }

    private static OpeningRaceSexListTemplate ParseRaceSexListTemplate(
        JsonElement source,
        IReadOnlyDictionary<string, OwnedUiTexture> textures)
    {
        var selection = source.GetProperty("selectionIndicator");
        var text = source.GetProperty("text");
        return new OpeningRaceSexListTemplate(
            source.GetProperty("template").GetString()!,
            source.GetProperty("tile").GetString()!,
            ReadRaceSexIdentity(source.GetProperty("id")),
            OpeningManifest.ReadRect(source.GetProperty("rect")),
            source.GetProperty("brightness").GetSingle(),
            source.GetProperty("activeListTrait").GetString()!,
            source.GetProperty("selectedTrait").GetString()!,
            new OpeningRaceSexSelectionIndicator(
                selection.GetProperty("tile").GetString()!,
                ParseRaceSexTexture(selection.GetProperty("texture"), textures),
                OpeningManifest.ReadRect(selection.GetProperty("rect"))),
            new OpeningRaceSexTextTemplate(
                text.GetProperty("tile").GetString()!,
                text.GetProperty("font").GetSingle(),
                text.GetProperty("y").GetSingle(),
                text.GetProperty("notSelectableX").GetSingle(),
                text.GetProperty("selectableX").GetSingle(),
                text.GetProperty("widthPolicy").GetString()!,
                text.GetProperty("heightPolicy").GetString()!),
            source.GetProperty("clickSound").GetString()!,
            source.GetProperty("mouseOverSound").GetString()!);
    }

    private static OpeningRaceSexSliderTemplate ParseRaceSexSliderTemplate(
        JsonElement source)
    {
        var traits = source.GetProperty("valueTraits");
        var label = source.GetProperty("label");
        var value = source.GetProperty("value");
        var bar = source.GetProperty("bar");
        var left = source.GetProperty("leftArrow");
        var right = source.GetProperty("rightArrow");
        var marker = source.GetProperty("marker");
        return new OpeningRaceSexSliderTemplate(
            source.GetProperty("template").GetString()!,
            source.GetProperty("tile").GetString()!,
            ReadRaceSexIdentity(source.GetProperty("id")),
            OpeningManifest.ReadRect(source.GetProperty("rect")),
            source.GetProperty("brightness").GetSingle(),
            source.GetProperty("activeListTrait").GetString()!,
            new OpeningRaceSexSliderTraits(
                traits.GetProperty("current").GetString()!,
                traits.GetProperty("minimum").GetString()!,
                traits.GetProperty("maximum").GetString()!,
                traits.GetProperty("jump").GetString()!,
                traits.GetProperty("display").GetString()!,
                traits.GetProperty("increment").GetString()!),
            new OpeningRaceSexSliderText(
                label.GetProperty("tile").GetString()!,
                label.GetProperty("font").GetSingle(),
                label.GetProperty("x").GetSingle(),
                label.GetProperty("y").GetSingle(),
                null,
                label.GetProperty("widthPolicy").GetString()!,
                label.GetProperty("heightPolicy").GetString()!),
            new OpeningRaceSexSliderValueText(
                value.GetProperty("tile").GetString()!,
                value.GetProperty("font").GetSingle(),
                value.GetProperty("y").GetSingle(),
                value.GetProperty("xPolicy").GetString()!,
                value.GetProperty("labelGap").GetSingle(),
                value.GetProperty("widthPolicy").GetString()!,
                value.GetProperty("heightPolicy").GetString()!),
            new OpeningRaceSexSliderBar(
                bar.GetProperty("tile").GetString()!,
                bar.GetProperty("x").GetSingle(),
                bar.GetProperty("y").GetSingle(),
                bar.GetProperty("width").GetSingle(),
                bar.GetProperty("heightGlobalTrait").GetString()!),
            new OpeningRaceSexSliderArrow(
                left.GetProperty("tile").GetString()!,
                left.GetProperty("textTile").GetString()!,
                ReadRaceSexIdentity(left.GetProperty("id")),
                null,
                left.GetProperty("xAnchor").GetSingle(),
                left.GetProperty("anchorEdge").GetString(),
                left.GetProperty("y").GetSingle(),
                left.GetProperty("widthPolicy").GetString()!,
                left.GetProperty("height").GetSingle(),
                left.GetProperty("stringSource").GetProperty("menuTrait").GetString()!,
                ReadRaceSexIdentity(left.GetProperty("justify")),
                left.GetProperty("clickSound").GetString()!),
            new OpeningRaceSexSliderArrow(
                right.GetProperty("tile").GetString()!,
                right.GetProperty("textTile").GetString()!,
                ReadRaceSexIdentity(right.GetProperty("id")),
                right.GetProperty("x").GetSingle(),
                null,
                null,
                right.GetProperty("y").GetSingle(),
                right.GetProperty("widthPolicy").GetString()!,
                right.GetProperty("height").GetSingle(),
                right.GetProperty("stringSource").GetProperty("menuTrait").GetString()!,
                null,
                right.GetProperty("clickSound").GetString()!),
            new OpeningRaceSexSliderMarker(
                marker.GetProperty("tile").GetString()!,
                marker.GetProperty("textTile").GetString()!,
                ReadRaceSexIdentity(marker.GetProperty("id")),
                marker.GetProperty("barX").GetSingle(),
                marker.GetProperty("barWidth").GetSingle(),
                marker.GetProperty("currentTrait").GetString()!,
                marker.GetProperty("minimumTrait").GetString()!,
                marker.GetProperty("maximumTrait").GetString()!,
                OpeningManifest.ReadVector(marker.GetProperty("clamp")),
                marker.GetProperty("y").GetSingle(),
                marker.GetProperty("width").GetSingle(),
                marker.GetProperty("height").GetSingle(),
                marker.GetProperty("glyph").GetString()!,
                marker.GetProperty("glyphXPolicy").GetString()!,
                marker.GetProperty("glyphXMultiplier").GetSingle(),
                marker.GetProperty("glyphY").GetSingle()),
            source.GetProperty("clickSound").GetString()!,
            source.GetProperty("mouseOverSound").GetString()!);
    }

    private static OpeningRaceSexTexture ParseRaceSexTexture(
        JsonElement source,
        IReadOnlyDictionary<string, OwnedUiTexture> textures)
    {
        var logicalPath = source.TryGetProperty("resolvedLogicalPath", out var resolved)
            ? resolved.GetString()
            : source.TryGetProperty("atlasTextureLogicalPath", out var atlasTexture)
                ? atlasTexture.GetString()
            : source.TryGetProperty("logicalPath", out var logical)
                ? logical.GetString()
                : null;
        OwnedUiTexture? texture = null;
        if (logicalPath is not null && !textures.TryGetValue(logicalPath, out texture))
            throw new InvalidOperationException(
                $"Owned RaceSexMenu texture is unavailable: {logicalPath}");
        var atlas = source.TryGetProperty("atlas", out var atlasValue)
            ? atlasValue.GetString()
            : null;
        var fileName = source.TryGetProperty("fileName", out var fileNameValue)
            ? fileNameValue.GetString()
            : null;
        if (logicalPath is null && (atlas is null || fileName is null))
            throw new InvalidOperationException(
                "Owned RaceSexMenu texture identity is incomplete.");
        OpeningRaceSexTextureAtlas? atlasContract = null;
        if (source.TryGetProperty("atlasIndexLogicalPath", out var atlasIndexPath))
        {
            var atlasIndexSource = source.GetProperty("atlasIndexSource").GetString()!;
            var atlasIndexSha256 = source.GetProperty("atlasIndexSha256").GetString()!;
            OpeningManifest.VerifyHash(atlasIndexSource, atlasIndexSha256);
            atlasContract = new OpeningRaceSexTextureAtlas(
                atlasIndexPath.GetString()!,
                atlasIndexSource,
                source.GetProperty("atlasIndexBytes").GetInt64(),
                atlasIndexSha256,
                source.GetProperty("atlasIndexSourceArchive").GetString()!,
                source.GetProperty("atlasIndexSourceArchiveSha256").GetString()!,
                source.GetProperty("atlasTextureLogicalPath").GetString()!,
                source.GetProperty("atlasFileName").GetString()!,
                source.GetProperty("atlasIndex").GetInt32(),
                source.GetProperty("atlasType").GetString()!,
                OpeningManifest.ReadRect(source.GetProperty("uvRect")),
                source.GetProperty("depthOffset").GetSingle());
            if (atlasContract.IndexBytes <= 0 ||
                string.IsNullOrWhiteSpace(atlasContract.IndexSourceArchive) ||
                string.IsNullOrWhiteSpace(atlasContract.IndexSourceArchiveSha256) ||
                atlasContract.AtlasIndex < 0 ||
                atlasContract.AtlasType != "2D" ||
                atlasContract.UvRect.Position.X < 0.0f ||
                atlasContract.UvRect.Position.Y < 0.0f ||
                atlasContract.UvRect.Size.X <= 0.0f ||
                atlasContract.UvRect.Size.Y <= 0.0f ||
                atlasContract.UvRect.End.X > 1.0f ||
                atlasContract.UvRect.End.Y > 1.0f)
                throw new InvalidOperationException(
                    "Owned RaceSexMenu atlas contract is invalid.");
        }
        return new OpeningRaceSexTexture(
            logicalPath,
            atlas,
            fileName,
            texture,
            atlasContract);
    }

    private static string ReadRaceSexIdentity(JsonElement source) =>
        source.ValueKind switch
        {
            JsonValueKind.Number => source.GetInt32().ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            JsonValueKind.String => source.GetString()!,
            _ => throw new InvalidOperationException(
                "Owned RaceSexMenu tile identity is invalid."),
        };

    private static void ValidateRaceSexMenuTiles(OpeningRaceSexMenuTiles source)
    {
        if (source.MenuClassEntity != "RaceSexMenu" ||
            source.FaceGrab.Id != "1" ||
            source.Scroll.Up.Id != "2" ||
            source.Scroll.Down.Id != "3" ||
            source.Navigation.Back.Id != "4" ||
            source.Navigation.Next.Id != "5" ||
            source.FontId <= 0 ||
            source.ListItem.Text.Font != source.FontId ||
            source.Slider.Label.Font != source.FontId ||
            source.Slider.Value.Font != source.FontId ||
            source.Navigation.Back.Font != source.FontId ||
            source.Navigation.Next.Font != source.FontId ||
            source.Background.Rect.Size.X <= 0.0f ||
            source.Background.Rect.Size.Y <= 0.0f ||
            source.FaceGrab.Rect.Size.X <= 0.0f ||
            source.FaceGrab.Rect.Size.Y <= 0.0f ||
            source.Background.TopBound < 0.0f ||
            source.Background.BottomBound <= source.Background.TopBound ||
            source.Background.BottomBound > source.Background.Rect.Size.Y ||
            source.ListItem.Rect.Size.X <= 0.0f ||
            source.ListItem.Rect.Size.Y <= 0.0f ||
            source.Slider.Rect.Size.X <= 0.0f ||
            source.Slider.Rect.Size.Y <= 0.0f ||
            source.SharedControls.Document != source.Document ||
            source.SharedControls.DocumentSha256 != source.DocumentSha256 ||
            source.SharedControls.BackgroundRect != source.Background.Rect ||
            source.SharedControls.FaceGrabRect != source.FaceGrab.Rect ||
            source.SharedControls.TopBound != source.Background.TopBound ||
            source.SharedControls.BottomBound != source.Background.BottomBound ||
            source.SharedControls.List.Rect != source.ListItem.Rect ||
            source.SharedControls.Slider.Rect != source.Slider.Rect ||
            source.SharedControls.Back.Text.Text != source.Navigation.Back.Label ||
            source.SharedControls.Next.Text.Text != source.Navigation.Next.Label ||
            source.ListItem.ActiveListTrait != source.ActiveListTrait ||
            source.Slider.ActiveListTrait != source.ActiveListTrait ||
            source.Navigation.Back.LabelRole != "back" ||
            source.Navigation.Next.LabelRole != "next" ||
            string.IsNullOrWhiteSpace(source.Navigation.Back.StringEntity) ||
            string.IsNullOrWhiteSpace(source.Navigation.Next.StringEntity) ||
            string.IsNullOrWhiteSpace(source.Navigation.Back.Label) ||
            string.IsNullOrWhiteSpace(source.Navigation.Next.Label) ||
            source.Navigation.Back.StringSourceDocuments.Count == 0 ||
            source.Navigation.Next.StringSourceDocuments.Count == 0 ||
            source.Scroll.Up.Brightness <= 0.0f ||
            source.Scroll.Up.Brightness > 255.0f ||
            source.Scroll.Down.Brightness <= 0.0f ||
            source.Scroll.Down.Brightness > 255.0f ||
            source.Scroll.Up.AlphaPolicy != "hover-only-255" ||
            source.Scroll.Down.AlphaPolicy != "hover-only-255" ||
            source.Navigation.Back.Brightness <= 0.0f ||
            source.Navigation.Back.Brightness > 255.0f ||
            source.Navigation.Next.Brightness <= 0.0f ||
            source.Navigation.Next.Brightness > 255.0f ||
            source.Navigation.Back.HorizontalBuffer < 0.0f ||
            source.Navigation.Back.VerticalBuffer < 0.0f ||
            source.Navigation.Next.HorizontalBuffer < 0.0f ||
            source.Navigation.Next.VerticalBuffer < 0.0f ||
            source.Navigation.Back.VerticalCenterDivisor <= 0.0f ||
            source.Navigation.Next.VerticalCenterDivisor <= 0.0f ||
            source.Navigation.Back.BoxVisible ||
            source.Navigation.Next.BoxVisible ||
            source.Navigation.Back.InheritBrightness ||
            source.Navigation.Next.InheritBrightness ||
            source.Navigation.Back.AlphaPolicy != "hover-only-255" ||
            source.Navigation.Next.AlphaPolicy != "hover-only-255" ||
            source.Navigation.Back.Justify != "left" ||
            source.Navigation.Next.Justify != "right" ||
            source.ListItem.Text.WidthPolicy != "owned-font-content" ||
            source.ListItem.Text.HeightPolicy != "owned-font-line-height" ||
            source.Slider.Label.WidthPolicy != "owned-font-content" ||
            source.Slider.Label.HeightPolicy != "owned-font-line-height" ||
            source.Slider.Value.WidthPolicy != "owned-font-content" ||
            source.Slider.Value.HeightPolicy != "owned-font-line-height" ||
            source.Slider.Value.XPolicy != "after-label" ||
            source.Slider.Bar.HeightGlobalTrait != "_line_thickness" ||
            source.Slider.LeftArrow.AnchorEdge != "right" ||
            source.Slider.LeftArrow.StringSourceMenuTrait !=
                source.SliderLeftLabelTrait ||
            source.Slider.RightArrow.StringSourceMenuTrait !=
                source.SliderRightLabelTrait ||
            source.Slider.LeftArrow.Justify != "right" ||
            source.Slider.LeftArrow.WidthPolicy != "owned-font-content" ||
            source.Slider.RightArrow.WidthPolicy != "owned-font-content" ||
            source.Slider.Marker.Clamp != new Vector2(0.0f, 1.0f) ||
            source.Slider.Marker.GlyphXPolicy !=
                "center-from-owned-text-width" ||
            string.IsNullOrEmpty(source.Slider.Marker.Glyph) ||
            source.Scroll.Up.Rect is null ||
            source.Scroll.Down.Rect is null ||
            source.Scroll.Up.Texture.Texture is null ||
            source.Scroll.Down.Texture.Texture is null ||
            source.Background.Texture.Texture is null ||
            source.ListItem.SelectionIndicator.Texture.Texture is null)
            throw new InvalidOperationException(
                "Owned RaceSexMenu tile contract is incomplete.");
    }

    private static OpeningStageProgram ParseStage(JsonElement value) => new(
        value.GetProperty("stage").GetInt32(),
        value.GetProperty("source").GetString()!,
        value.GetProperty("commands").EnumerateArray().Select(ParseCommand).ToArray());

    private static OpeningInteraction ParseInteraction(JsonElement value) => new(
        value.GetProperty("event").GetString()!,
        value.GetProperty("scriptEditorId").GetString()!,
        value.GetProperty("targetRole").GetString()!,
        value.GetProperty("targetReferenceFormId").GetString()!,
        value.GetProperty("fromStage").GetInt32(),
        value.GetProperty("toStage").GetInt32(),
        value.GetProperty("distancePolicy").GetString()!,
        value.GetProperty("menu").ValueKind == JsonValueKind.Object
            ? ParseCommand(value.GetProperty("menu"))
            : null);

    private static OpeningDialogueTopic ParseTopic(JsonElement value) => new(
        value.GetProperty("formId").GetString()!,
        value.GetProperty("editorId").GetString() ?? "",
        value.GetProperty("prompt").GetString() ?? "",
        value.GetProperty("infos").EnumerateArray().Select(ParseInfo).ToArray());

    private static OpeningDialogueInfo ParseInfo(JsonElement value) => new(
        value.GetProperty("formId").GetString()!,
        value.GetProperty("sourceOrder").GetInt32(),
        value.GetProperty("responses").EnumerateArray()
            .Select(ParseDialogueResponse)
            .ToArray(),
        value.GetProperty("commands").EnumerateArray().Select(ParseCommand).ToArray(),
        value.GetProperty("conditions").EnumerateArray()
            .Select(condition => new OpeningDialogueCondition(
                condition.GetProperty("operatorFlags").GetInt32(),
                condition.GetProperty("comparisonValue").GetSingle(),
                condition.GetProperty("function").GetInt32(),
                condition.GetProperty("parameter1").GetString()!,
                condition.GetProperty("parameter2").GetInt32(),
                condition.GetProperty("runOn").GetInt32(),
                condition.GetProperty("reference").GetString()!))
            .ToArray(),
        value.GetProperty("nextTopicFormIds").EnumerateArray()
            .Select(topic => topic.GetString()!)
            .ToArray(),
        value.GetProperty("responseType").GetInt32(),
        value.GetProperty("flags").GetInt32(),
        value.GetProperty("goodbye").GetBoolean(),
        value.GetProperty("sayOnce").GetBoolean());

    private static OpeningDialogueVoice ParseDialogueVoice(JsonElement value)
    {
        var archiveStack = value.GetProperty("archiveStack");
        var archiveRecipe = archiveStack.GetProperty("recipe");
        return new OpeningDialogueVoice(
            value.GetProperty("speakerRole").GetString()!,
            value.GetProperty("speakerReferenceFormId").GetString()!,
            value.GetProperty("speakerBaseFormId").GetString()!,
            value.GetProperty("voiceTypeFormId").GetString()!,
            value.GetProperty("voiceTypeEditorId").GetString()!,
            value.GetProperty("memberNamespace").GetString()!,
            value.GetProperty("infoCount").GetInt32(),
            value.GetProperty("responseCount").GetInt32(),
            archiveStack.GetProperty("schema").GetString()!,
            archiveRecipe.GetProperty("id").GetString()!,
            archiveRecipe.GetProperty("sha256").GetString()!,
            archiveStack.GetProperty("archives").GetArrayLength());
    }

    private static OpeningDialogueResponse ParseDialogueResponse(JsonElement value) => new(
        value.GetProperty("index").GetInt32(),
        value.GetProperty("text").GetString()!,
        ParseDialogueAsset(value.GetProperty("voice")),
        ParseDialogueAsset(value.GetProperty("lip")));

    private static OpeningDialogueAsset ParseDialogueAsset(JsonElement value)
    {
        var source = System.IO.Path.GetFullPath(value.GetProperty("source").GetString()!);
        var sha256 = value.GetProperty("sha256").GetString()!;
        OpeningManifest.VerifyHash(source, sha256);
        return new OpeningDialogueAsset(
            value.GetProperty("logicalPath").GetString()!,
            source,
            sha256,
            value.GetProperty("sourceArchive").GetString()!,
            value.GetProperty("sourceArchiveSha256").GetString()!);
    }

    private static OpeningFlowCommand ParseCommand(JsonElement value) => new(
        value.GetProperty("kind").GetString()!,
        OptionalString(value, "role"),
        OptionalString(value, "questEditorId"),
        OptionalString(value, "topicEditorId"),
        OptionalString(value, "speakerEditorId"),
        OptionalString(value, "referenceEditorId"),
        OptionalString(value, "resolvedItemEditorId") ?? OptionalString(value, "itemEditorId"),
        OptionalString(value, "packageEditorId"),
        OptionalString(value, "modifierEditorId"),
        OptionalString(value, "operation"),
        OptionalString(value, "targetEditorId"),
        OptionalString(value, "globalEditorId"),
        OptionalString(value, "variable") ?? OptionalString(value, "value"),
        OptionalString(value, "idleEditorId"),
        OptionalString(value, "idleFormId"),
        OptionalString(value, "idleRecordType"),
        OptionalString(value, "animationLogicalPath"),
        OptionalString(value, "state"),
        OptionalInt(value, "stage"),
        OptionalInt(value, "maximumSelected"),
        OptionalInt(value, "totalPoints"),
        OptionalInt(value, "index"),
        OptionalInt(value, "delta"),
        OptionalInt(value, "count"),
        OptionalFloat(value, "seconds"),
        OptionalFloat(value, "value"),
        OptionalBool(value, "enabled"),
        OptionalBool(value, "destroyed"),
        OptionalBool(value, "crossFade"),
        value.TryGetProperty("values", out var controls) &&
        controls.ValueKind == JsonValueKind.Array
            ? controls.EnumerateArray().Select(control => control.GetInt32()).ToArray()
            : Array.Empty<int>(),
        OptionalString(value, "resolvedItemFormId") ?? OptionalString(value, "itemFormId"),
        OptionalString(value, "resolvedItemRecordType") ?? OptionalString(value, "itemRecordType"),
        OptionalString(value, "questFormId"),
        OptionalString(value, "questRecordType"),
        OptionalString(value, "globalFormId"),
        OptionalString(value, "globalRecordType"),
        OptionalString(value, "ownerEditorId"),
        OptionalString(value, "ownerFormId"),
        OptionalString(value, "ownerRecordType"),
        OptionalString(value, "referenceFormId"),
        OptionalString(value, "referenceRecordType"),
        value.TryGetProperty("guard", out var guard)
            ? new OpeningCommandGuard(
                guard.GetProperty("kind").GetString()!,
                OptionalString(guard, "itemFormId"),
                OptionalString(guard, "questFormId"),
                OptionalInt(guard, "stage"))
            : null,
        value.TryGetProperty("weapon", out var weapon)
            ? new OpeningCommandWeapon(
                weapon.GetProperty("ammoFormId").GetString()!,
                weapon.GetProperty("ammoEditorId").GetString()!,
                weapon.GetProperty("damage").GetInt32(),
                weapon.GetProperty("clipSize").GetInt32(),
                weapon.GetProperty("animationType").GetInt32())
            : null,
        value.TryGetProperty("enableParentChildFormIds", out var enableChildren)
            ? enableChildren.EnumerateArray().Select(child => child.GetString()!).ToArray()
            : Array.Empty<string>());

    private static OpeningGuideActorAi ParseGuideActorAi(JsonElement source)
    {
        if (source.GetProperty("schema").GetString() != ExpectedGuideActorAiSchema)
            throw new InvalidOperationException(
                "Owned guide-actor AI has an unexpected contract.");
        var packages = source.GetProperty("packages").EnumerateArray()
            .Select(ParseGuidePackage)
            .ToDictionary(value => value.FormId, StringComparer.OrdinalIgnoreCase);
        var locomotion = source.GetProperty("locomotion");
        return new OpeningGuideActorAi(
            source.GetProperty("role").GetString()!,
            source.GetProperty("referenceFormId").GetString()!,
            source.GetProperty("baseFormId").GetString()!,
            source.GetProperty("questFormId").GetString()!,
            source.GetProperty("packagePriority").EnumerateArray()
                .Select(value => value.GetString()!)
                .ToArray(),
            packages,
            ParseGuideFurnitureOccupancy(
                source.GetProperty("furnitureOccupancy")),
            source.GetProperty("animationObjects").EnumerateArray()
                .Select(ParseGuideAnimationObject)
                .ToArray(),
            new OpeningGuideLocomotion(
                ParseGuideLocomotionClip(locomotion.GetProperty("walk")),
                ParseGuideLocomotionClip(locomotion.GetProperty("run"))));
    }

    private static OpeningGuideFurnitureOccupancy ParseGuideFurnitureOccupancy(
        JsonElement source)
    {
        if (source.GetProperty("schema").GetString() !=
            ExpectedGuideFurnitureOccupancySchema)
            throw new InvalidOperationException(
                "Owned guide furniture occupancy has an unexpected contract.");
        var furniture = source.GetProperty("furniture");
        var patientBed = source.GetProperty("patientBed");
        var marker = furniture.GetProperty("marker");
        var placementOffset = marker.GetProperty(
            "actorPlacementOffsetGameSettings");
        var headingDelta = marker.GetProperty(
            "actorForwardHeadingDeltaGameSetting");
        return new OpeningGuideFurnitureOccupancy(
            source.GetProperty("initialPackageFormId").GetString()!,
            source.GetProperty("referenceFormId").GetString()!,
            source.GetProperty("markerId").GetInt32(),
            source.GetProperty("markerDisposition").GetString()!,
            new OpeningGuideFurnitureSource(
                furniture.GetProperty("referenceFormId").GetString()!,
                furniture.GetProperty("referenceRecordSha256").GetString()!,
                furniture.GetProperty("baseFormId").GetString()!,
                furniture.GetProperty("editorId").GetString()!,
                furniture.GetProperty("recordType").GetString()!,
                furniture.GetProperty("recordSha256").GetString()!,
                furniture.GetProperty("modelLogicalPath").GetString()!,
                furniture.GetProperty("modelBytes").GetInt64(),
                furniture.GetProperty("modelSha256").GetString()!,
                furniture.GetProperty("sourceArchive").GetString()!,
                furniture.GetProperty("sourceArchiveSha256").GetString()!,
                new OpeningGuideFurnitureMarker(
                    marker.GetProperty("extraDataName").GetString()!,
                    marker.GetProperty("index").GetInt32(),
                    marker.GetProperty("positionRef1").GetInt32(),
                    marker.GetProperty("positionRef2").GetInt32(),
                    ReadVector3(marker.GetProperty("offsetNifGameUnits")),
                    ReadVector3(marker.GetProperty("offsetGodotGameUnits")),
                    marker.GetProperty("orientation").GetInt32(),
                    marker.GetProperty("orientationRadians").GetSingle(),
                    marker.GetProperty("heading").GetSingle(),
                    marker.GetProperty("animationType").GetInt32(),
                    ReadQuaternion(marker.GetProperty("rotationGodotQuaternion")),
                    new OpeningGuideFurniturePlacementOffset(
                        placementOffset.GetProperty("semantics").GetString()!,
                        ParseFurniturePlacementGameSetting(
                            placementOffset.GetProperty("x")),
                        ParseFurniturePlacementGameSetting(
                            placementOffset.GetProperty("y")),
                        ParseFurniturePlacementGameSetting(
                            placementOffset.GetProperty("z")),
                        ReadVector3(placementOffset.GetProperty(
                            "offsetNifGameUnits")),
                        ReadVector3(placementOffset.GetProperty(
                            "offsetGodotGameUnits"))),
                    new OpeningGuideFurnitureHeadingDelta(
                        headingDelta.GetProperty("formId").GetString()!,
                        headingDelta.GetProperty("editorId").GetString()!,
                        headingDelta.GetProperty("recordSha256").GetString()!,
                        headingDelta.GetProperty("sourceKind").GetString()!,
                        headingDelta.GetProperty("value").GetSingle(),
                        ReadQuaternion(headingDelta.GetProperty(
                            "rotationGodotQuaternion"))))),
            ParseGuideFurnitureIdentity(patientBed),
            source.GetProperty("releaseStage").GetInt32(),
            source.GetProperty("releasePackageFormId").GetString()!,
            source.GetProperty("animationObjectIdleFormId").GetString()!,
            ParseGuideFurnitureAnimation(source.GetProperty("seatedLoop")),
            ParseGuideFurnitureAnimation(source.GetProperty("exit")));
    }

    private static OpeningGuideFurnitureIdentity ParseGuideFurnitureIdentity(
        JsonElement source) => new(
        source.GetProperty("referenceFormId").GetString()!,
        source.GetProperty("referenceRecordSha256").GetString()!,
        source.GetProperty("baseFormId").GetString()!,
        source.GetProperty("editorId").GetString()!,
        source.GetProperty("recordType").GetString()!,
        source.GetProperty("recordSha256").GetString()!,
        source.GetProperty("modelLogicalPath").GetString()!,
        source.GetProperty("modelBytes").GetInt64(),
        source.GetProperty("modelSha256").GetString()!,
        source.GetProperty("sourceArchive").GetString()!,
        source.GetProperty("sourceArchiveSha256").GetString()!);

    private static OpeningGuideFurniturePlacementGameSetting
        ParseFurniturePlacementGameSetting(JsonElement source) => new(
            source.GetProperty("formId").GetString()!,
            source.GetProperty("editorId").GetString()!,
            source.GetProperty("recordSha256").GetString()!,
            source.GetProperty("sourceKind").GetString()!,
            source.GetProperty("value").GetSingle());

    private static OpeningGuideFurnitureAnimation ParseGuideFurnitureAnimation(
        JsonElement source) => new(
        source.GetProperty("role").GetString()!,
        source.GetProperty("formId").GetString()!,
        source.GetProperty("editorId").GetString()!,
        source.GetProperty("recordType").GetString()!,
        source.GetProperty("recordSha256").GetString()!,
        source.GetProperty("logicalPath").GetString()!,
        source.GetProperty("bytes").GetInt64(),
        source.GetProperty("sha256").GetString()!,
        source.GetProperty("sourceArchive").GetString()!,
        source.GetProperty("sourceArchiveSha256").GetString()!,
            source.GetProperty("sequenceName").GetString()!,
            source.GetProperty("startSeconds").GetSingle(),
            source.GetProperty("stopSeconds").GetSingle(),
            source.GetProperty("cycleType").GetInt32(),
            source.GetProperty("controlledBlocks").GetInt32(),
            source.TryGetProperty("rootMotion", out var rootMotion)
                ? ParseGuideRootMotion(rootMotion)
                : null);

    private static OpeningGuideAnimationObject ParseGuideAnimationObject(
        JsonElement source) => new(
        source.GetProperty("componentRole").GetString()!,
        source.GetProperty("formId").GetString()!,
        source.GetProperty("editorId").GetString()!,
        source.GetProperty("recordType").GetString()!,
        source.GetProperty("recordSha256").GetString()!,
        source.GetProperty("idleAnimationFormId").GetString()!,
        source.GetProperty("idleAnimationEditorId").GetString()!,
        source.GetProperty("idleAnimationLogicalPath").GetString()!,
        source.GetProperty("idleAnimationSha256").GetString()!,
        source.GetProperty("idleAnimationSequenceName").GetString()!,
        source.GetProperty("idleAnimationStartSeconds").GetSingle(),
        source.GetProperty("idleAnimationStopSeconds").GetSingle(),
        source.GetProperty("idleAnimationCycleType").GetInt32(),
        source.GetProperty("idleAnimationTransformPrioritiesByNode")
            .EnumerateObject()
            .ToDictionary(
                value => value.Name,
                value => value.Value.GetInt32(),
                StringComparer.Ordinal),
        source.GetProperty("modelLogicalPath").GetString()!,
        source.GetProperty("bytes").GetInt64(),
        source.GetProperty("sha256").GetString()!,
        source.GetProperty("sourceArchive").GetString()!,
        source.GetProperty("sourceArchiveSha256").GetString()!,
        source.GetProperty("attachmentNode").GetString()!);

    private static OpeningGuidePackage ParseGuidePackage(JsonElement source) => new(
        source.GetProperty("formId").GetString()!,
        source.GetProperty("editorId").GetString()!,
        source.GetProperty("recordSha256").GetString()!,
        source.GetProperty("packageFlags").GetUInt32(),
        source.GetProperty("alwaysRun").GetBoolean(),
        source.GetProperty("packageType").GetInt32(),
        source.GetProperty("packageTypeName").GetString()!,
        source.GetProperty("procedureFlags").GetInt32(),
        source.GetProperty("typeSpecificFlags").GetInt32(),
        source.GetProperty("conditions").EnumerateArray()
            .Select(value => new OpeningGuideCondition(
                value.GetProperty("operatorFlags").GetInt32(),
                value.GetProperty("comparisonValue").GetSingle(),
                value.GetProperty("function").GetInt32(),
                value.GetProperty("functionName").GetString()!,
                value.GetProperty("parameter1").GetString()!,
                value.GetProperty("parameter2").GetUInt32(),
                value.GetProperty("runOn").GetUInt32(),
                value.GetProperty("reference").GetString()!))
            .ToArray(),
        source.GetProperty("location") is { ValueKind: JsonValueKind.Object } location
            ? ParseGuideLocation(location)
            : null,
        source.GetProperty("target") is { ValueKind: JsonValueKind.Object } target
            ? new OpeningGuideTarget(
                target.GetProperty("type").GetInt32(),
                target.GetProperty("typeName").GetString()!,
                target.GetProperty("formId").GetString()!,
                target.GetProperty("count").GetUInt32(),
                target.GetProperty("unknown").GetUInt32())
            : null,
        source.GetProperty("idleAnimationFormIds").EnumerateArray()
            .Select(value => value.GetString()!)
            .ToArray(),
        source.GetProperty("idleAnimationLogicalPaths").EnumerateArray()
            .Select(value => value.GetString()!)
            .ToArray());

    private static OpeningGuideLocation ParseGuideLocation(JsonElement source) => new(
        source.GetProperty("type").GetInt32(),
        source.GetProperty("typeName").GetString()!,
        source.GetProperty("formId").GetString()!,
        source.GetProperty("radiusGameUnits").GetUInt32(),
        source.GetProperty("reference") is { ValueKind: JsonValueKind.Object } reference
            ? new OpeningGuideReference(
                reference.GetProperty("formId").GetString()!,
                OptionalString(reference, "editorId"),
                reference.GetProperty("recordType").GetString()!,
                ReadVector3(reference.GetProperty("positionGameUnits")),
                ReadVector3(reference.GetProperty("rotationRadians")),
                ReadQuaternion(reference.GetProperty("rotationGodotQuaternion")))
            : null);

    private static OpeningGuideLocomotionClip ParseGuideLocomotionClip(
        JsonElement source)
    {
        var rootMotion = source.GetProperty("rootMotion");
        return new OpeningGuideLocomotionClip(
            source.GetProperty("logicalPath").GetString()!,
            source.GetProperty("sha256").GetString()!,
            ParseGuideRootMotion(rootMotion));
    }

    private static OpeningGuideRootMotion ParseGuideRootMotion(JsonElement source) => new(
        source.GetProperty("sequenceName").GetString()!,
        source.GetProperty("targetNode").GetString()!,
        source.GetProperty("startSeconds").GetSingle(),
        source.GetProperty("stopSeconds").GetSingle(),
        source.GetProperty("cycleType").GetInt32(),
        ReadVector3(source.GetProperty("displacementGodotGameUnits")),
        source.GetProperty("speedGameUnitsPerSecond").GetSingle());

    private static OpeningPlayerAnimationGraph ParsePlayerAnimation(JsonElement source)
    {
        if (source.GetProperty("schema").GetString() != ExpectedPlayerAnimationSchema)
            throw new InvalidOperationException(
                "Owned player-animation graph has an unexpected contract.");
        var packages = source.GetProperty("packages").EnumerateArray()
            .Select(value =>
            {
                var selection = value.GetProperty("idleSelection");
                return new OpeningPlayerPackage(
                    value.GetProperty("formId").GetString()!,
                    value.GetProperty("editorId").GetString()!,
                    value.GetProperty("recordSha256").GetString()!,
                    selection.GetProperty("runInSequence").GetBoolean(),
                    selection.GetProperty("doOnce").GetBoolean(),
                    selection.GetProperty("timerSeconds").GetSingle(),
                    value.GetProperty("idleAnimationFormIds").EnumerateArray()
                        .Select(form => form.GetString()!)
                        .ToArray(),
                    value.GetProperty("events").EnumerateObject()
                        .ToDictionary(
                            property => property.Name,
                            property => property.Value.ValueKind == JsonValueKind.Null
                                ? null
                                : property.Value.GetString(),
                            StringComparer.OrdinalIgnoreCase));
            })
            .ToDictionary(value => value.EditorId, StringComparer.OrdinalIgnoreCase);
        var animations = source.GetProperty("animations").EnumerateArray()
            .Select(value =>
            {
                var track = value.GetProperty("track");
                return new OpeningPlayerAnimation(
                    value.GetProperty("formId").GetString()!,
                    value.GetProperty("editorId").GetString()!,
                    value.GetProperty("logicalPath").GetString()!,
                    value.GetProperty("sha256").GetString()!,
                    new OpeningTransformTrack(
                        track.GetProperty("targetNode").GetString()!,
                        track.GetProperty("startSeconds").GetSingle(),
                        track.GetProperty("stopSeconds").GetSingle(),
                        track.GetProperty("cycleType").GetInt32(),
                        track.GetProperty("parentChain").EnumerateArray()
                            .Select(parent => new OpeningTransformParent(
                                parent.GetProperty("nodeName").GetString()!,
                                ReadVector3(parent.GetProperty("translationGodotGameUnits")),
                                ReadQuaternion(parent.GetProperty("rotationQuaternionXyzw")),
                                ReadVector3(parent.GetProperty("scale"))))
                            .ToArray(),
                        track.GetProperty("samples").EnumerateArray()
                            .Select(sample => new OpeningTransformSample(
                                sample.GetProperty("timeSeconds").GetSingle(),
                                ReadVector3(sample.GetProperty("translationGodotGameUnits")),
                                ReadQuaternion(sample.GetProperty("rotationQuaternionXyzw"))))
                            .ToArray()));
            })
            .ToDictionary(value => value.FormId, StringComparer.OrdinalIgnoreCase);
        return new OpeningPlayerAnimationGraph(
            source.GetProperty("cameraNode").GetString()!,
            packages,
            animations);
    }

    private static OpeningImageSpaceModifier ParseImageSpaceModifier(JsonElement source) => new(
        source.GetProperty("formId").GetString()!,
        source.GetProperty("editorId").GetString()!,
        source.GetProperty("duration").GetSingle(),
        source.GetProperty("fade").EnumerateArray()
            .Select(value =>
            {
                var components = value.EnumerateArray()
                    .Select(component => component.GetSingle())
                    .ToArray();
                if (components.Length != OpeningImageSpaceFadeKey.ComponentCount)
                    throw new InvalidOperationException(
                        "Owned image-space fade key has an invalid component count.");
                return new OpeningImageSpaceFadeKey(
                    components[OpeningImageSpaceFadeKey.TimeIndex],
                    new Color(
                        components[OpeningImageSpaceFadeKey.RedIndex],
                        components[OpeningImageSpaceFadeKey.GreenIndex],
                        components[OpeningImageSpaceFadeKey.BlueIndex],
                        components[OpeningImageSpaceFadeKey.AlphaIndex]));
            })
            .ToArray(),
        source.GetProperty("recordSha256").GetString()!);

    private static Vector3 ReadVector3(JsonElement source)
    {
        var values = source.EnumerateArray().Select(value => value.GetSingle()).ToArray();
        if (values.Length != OpeningTransformParent.VectorComponents)
            throw new InvalidOperationException("Owned transform vector has an invalid size.");
        return new Vector3(values[0], values[1], values[2]);
    }

    private static Quaternion ReadQuaternion(JsonElement source)
    {
        var values = source.EnumerateArray().Select(value => value.GetSingle()).ToArray();
        if (values.Length != OpeningTransformParent.QuaternionComponents)
            throw new InvalidOperationException("Owned transform quaternion has an invalid size.");
        return new Quaternion(values[0], values[1], values[2], values[3]);
    }

    private static OpeningCharacterCreation ParseCharacter(
        JsonElement value,
        IReadOnlyDictionary<string, OwnedUiTexture> textures)
    {
        var sex = value.GetProperty("sex");
        var special = value.GetProperty("special");
        var skills = value.GetProperty("tagSkills");
        var traits = value.GetProperty("traits");
        return new OpeningCharacterCreation(
            sex.GetProperty("title").GetString()!,
            sex.GetProperty("choices").EnumerateArray()
                .Select(choice => choice.GetString()!)
                .ToArray(),
            ParsePlayerAppearance(value.GetProperty("appearance"), textures),
            special.GetProperty("minimumValue").GetInt32(),
            special.GetProperty("initialValue").GetInt32(),
            special.GetProperty("maximumValue").GetInt32(),
            special.GetProperty("totalPoints").GetInt32(),
            ParseDocReaction(special.GetProperty("docReaction")),
            ParseCharacterValues(special.GetProperty("values"), textures),
            skills.GetProperty("maximumSelected").GetInt32(),
            ParseCharacterValues(skills.GetProperty("values"), textures),
            traits.GetProperty("maximumSelected").GetInt32(),
            ParseCharacterValues(traits.GetProperty("values"), textures),
            OpeningGameplayVitalsContract.Parse(value.GetProperty("vitals")));
    }

    private static OpeningPlayerAppearance ParsePlayerAppearance(
        JsonElement source,
        IReadOnlyDictionary<string, OwnedUiTexture> textures) => new(
            source.GetProperty("schema").GetString()!,
            source.GetProperty("status").GetString()!,
            source.GetProperty("player").GetProperty("formId").GetString()!,
            source.GetProperty("player").GetProperty("recordSha256").GetString()!,
            source.GetProperty("player").GetProperty("defaultRaceFormId").GetString()!,
            source.GetProperty("player").GetProperty("defaultHairFormId").GetString()!,
            source.GetProperty("player").GetProperty("defaultEyesFormId").GetString()!,
            ParseAppearanceFaceGen(
                source.GetProperty("player").GetProperty("faceGen")),
            source.GetProperty("sexEngineValues").EnumerateArray()
                .Select(value => value.GetString()!)
                .ToArray(),
            source.GetProperty("races").EnumerateArray()
                .Select(value => ParseAppearanceRace(value, textures))
                .ToArray(),
            source.GetProperty("preview").GetString()!);

    private static OpeningAppearanceFaceGen ParseAppearanceFaceGen(
        JsonElement source) => new(
            source.GetProperty("symmetricGeometry").GetProperty("count").GetInt32(),
            source.GetProperty("symmetricGeometry").GetProperty("sha256").GetString()!,
            ParseFloatArray(source.GetProperty("symmetricGeometry").GetProperty("values")),
            source.GetProperty("asymmetricGeometry").GetProperty("count").GetInt32(),
            source.GetProperty("asymmetricGeometry").GetProperty("sha256").GetString()!,
            ParseFloatArray(source.GetProperty("asymmetricGeometry").GetProperty("values")),
            source.GetProperty("symmetricTexture").GetProperty("count").GetInt32(),
            source.GetProperty("symmetricTexture").GetProperty("sha256").GetString()!,
            ParseFloatArray(source.GetProperty("symmetricTexture").GetProperty("values")),
            ParseFaceGenControlSpace(source.GetProperty("controlSpace")),
            ParsePlayerFaceGenPreviewSet(source.GetProperty("previewHead")));

    private static IReadOnlyList<float> ParseFloatArray(JsonElement source) =>
        source.EnumerateArray().Select(value => value.GetSingle()).ToArray();

    private static OpeningFaceGenControlSpace ParseFaceGenControlSpace(
        JsonElement source)
    {
        var format = source.GetProperty("format");
        var basisCounts = format.GetProperty("basisCounts");
        var controlCounts = format.GetProperty("linearControlCounts");
        var exposure = source.GetProperty("nativeGeometryExposure");
        return new OpeningFaceGenControlSpace(
            source.GetProperty("schema").GetString()!,
            source.GetProperty("status").GetString()!,
            source.GetProperty("source").GetProperty("archive").GetString()!,
            source.GetProperty("source").GetProperty("archiveSha256").GetString()!,
            source.GetProperty("source").GetProperty("logicalPath").GetString()!,
            source.GetProperty("source").GetProperty("bytes").GetInt64(),
            source.GetProperty("source").GetProperty("sha256").GetString()!,
            format.GetProperty("formatSignature").GetString()!,
            format.GetProperty("geometryBasisVersion").GetInt32(),
            format.GetProperty("textureBasisVersion").GetInt32(),
            basisCounts.GetProperty("symmetricGeometry").GetInt32(),
            basisCounts.GetProperty("asymmetricGeometry").GetInt32(),
            basisCounts.GetProperty("symmetricTexture").GetInt32(),
            basisCounts.GetProperty("asymmetricTexture").GetInt32(),
            controlCounts.GetProperty("symmetricGeometry").GetInt32(),
            controlCounts.GetProperty("asymmetricGeometry").GetInt32(),
            controlCounts.GetProperty("symmetricTexture").GetInt32(),
            controlCounts.GetProperty("asymmetricTexture").GetInt32(),
            format.GetProperty("controls").GetProperty("symmetricGeometry")
                .EnumerateArray().Select(ParseFaceGenLinearControl).ToArray(),
            exposure.GetProperty("classification").GetString()!,
            exposure.GetProperty("engineBuild").GetString()!,
            exposure.GetProperty("sourceExecutableSha256").GetString()!,
            exposure.GetProperty("controls").EnumerateArray()
                .Select(ParseNativeFaceGenGeometryControl).ToArray(),
            ParseNativeFaceGenAgeControl(source.GetProperty("nativeAgeExposure")),
            ParseFaceGenPreviewControl(source.GetProperty("runtimePreviewControl")),
            source.GetProperty("runtimeDisposition").GetString()!);
    }

    private static OpeningFaceGenLinearControl ParseFaceGenLinearControl(
        JsonElement source) => new(
            source.GetProperty("index").GetInt32(),
            source.GetProperty("sourceLabel").GetString()!,
            source.GetProperty("axisSha256").GetString()!,
            ParseFloatArray(source.GetProperty("axis")));

    private static OpeningNativeFaceGenGeometryControl
        ParseNativeFaceGenGeometryControl(JsonElement source) => new(
            source.GetProperty("controlIndex").GetInt32(),
            source.GetProperty("settingEntity").GetString()!,
            source.GetProperty("sourceLabel").GetString()!,
            source.GetProperty("axisSha256").GetString()!);

    private static OpeningFaceGenPreviewControl ParseFaceGenPreviewControl(
        JsonElement source) => new(
            source.GetProperty("controlIndex").GetInt32(),
            source.GetProperty("settingEntity").GetString()!,
            source.GetProperty("sourceLabel").GetString()!,
            source.GetProperty("axisSha256").GetString()!,
            source.GetProperty("minimum").GetSingle(),
            source.GetProperty("maximum").GetSingle(),
            source.GetProperty("step").GetSingle(),
            source.GetProperty("jump").GetSingle(),
            source.GetProperty("morphWeightScale").GetSingle(),
            source.GetProperty("resetValue").GetSingle(),
            source.GetProperty("acceptanceValue").GetSingle(),
            ParseFaceGenSliderSemanticsEvidence(
                source.GetProperty("sliderSemanticsEvidence")),
            ParseFaceGenPreviewPresentation(source.GetProperty("presentation")),
            source.GetProperty("semantics").GetString()!);

    private static OpeningFaceGenSliderSemanticsEvidence
        ParseFaceGenSliderSemanticsEvidence(JsonElement source) => new(
            source.GetProperty("classification").GetString()!,
            source.GetProperty("engineBuild").GetString()!,
            source.GetProperty("sourceExecutableSha256").GetString()!,
            source.GetProperty("sourceMinimum").GetSingle(),
            source.GetProperty("sourceMaximum").GetSingle(),
            source.GetProperty("uiScale").GetSingle(),
            source.GetProperty("uiMinimum").GetSingle(),
            source.GetProperty("uiMaximum").GetSingle(),
            source.GetProperty("ordinaryIncrement").GetSingle(),
            source.GetProperty("jump").GetSingle(),
            source.GetProperty("morphWeightScale").GetSingle(),
            source.GetProperty("lowGlobalAddress").GetString()!,
            source.GetProperty("highGlobalAddress").GetString()!,
            source.GetProperty("incrementTrait").GetString()!,
            source.GetProperty("incrementDefaultThreshold").GetSingle());

    private static OpeningFaceGenPreviewPresentation ParseFaceGenPreviewPresentation(
        JsonElement source) => new(
            source.TryGetProperty("viewportWidthFraction", out var viewportWidth)
                ? viewportWidth.GetSingle()
                : float.NaN,
            source.TryGetProperty("viewportHeightFraction", out var viewportHeight)
                ? viewportHeight.GetSingle()
                : float.NaN,
            source.TryGetProperty("verticalFovHalfAngleFactor", out var fovFactor)
                ? fovFactor.GetSingle()
                : float.NaN,
            source.TryGetProperty("depthExtentFraction", out var depthExtent)
                ? depthExtent.GetSingle()
                : float.NaN,
            source.GetProperty("fullInVerticalOffsetGameUnits").GetSingle(),
            source.GetProperty("fullInDistanceGameUnits").GetSingle(),
            source.GetProperty("fullInYawRadians").GetSingle(),
            source.GetProperty("fullOutVerticalOffsetGameUnits").GetSingle(),
            source.GetProperty("fullOutDistanceGameUnits").GetSingle(),
            source.GetProperty("fullOutYawRadians").GetSingle(),
            source.GetProperty("startingZoomFraction").GetSingle());

    private static OpeningPlayerFaceGenPreviewSet ParsePlayerFaceGenPreviewSet(
        JsonElement source)
    {
        var schema = source.GetProperty("schema").GetString()!;
        var status = source.GetProperty("status").GetString()!;
        var playerFormId = source.GetProperty("playerFormId").GetString()!;
        var geometryControlNames = source.GetProperty("geometryControlNames")
            .EnumerateArray().Select(value => value.GetString()!).ToArray();
        var geometryControlCount = source.GetProperty("geometryControlCount").GetInt32();
        var textureControlNames = source.GetProperty("textureControlNames")
            .EnumerateArray().Select(value => value.GetString()!).ToArray();
        var textureControlCount = source.GetProperty("textureControlCount").GetInt32();
        var runtimeDisposition = source.GetProperty("runtimeDisposition").GetString()!;
        var selectionScope = source.GetProperty("selectionScope").GetString()!;
        var unsupportedSelectionScope = source.GetProperty("unsupportedSelectionScope")
            .GetString()!;
        var fullBody = source.GetProperty("fullBody").GetBoolean();
        var bodyComponentRoles = source.GetProperty("bodyComponentRoles")
            .EnumerateArray().Select(value => value.GetString()!).ToArray();
        var bodyComponentSourcesBySex = source.GetProperty("bodyComponentSourcesBySex")
            .EnumerateObject()
            .ToDictionary(
                value => value.Name,
                value => (IReadOnlyList<OpeningPlayerBodyComponentSource>)value.Value
                    .EnumerateArray()
                    .Select(ParsePlayerBodyComponentSource)
                    .ToArray(),
                StringComparer.Ordinal);
        var previews = source.GetProperty("previews").EnumerateArray()
            .Select(value =>
            {
                var outputs = value.GetProperty("outputs");
                return new OpeningPlayerFaceGenPreview(
                    schema,
                    status,
                    playerFormId,
                    value.GetProperty("raceFormId").GetString()!,
                    value.GetProperty("sex").GetString()!,
                    value.GetProperty("hairFormId").GetString()!,
                    value.GetProperty("eyesFormId").GetString()!,
                    value.GetProperty("headPartFormIds").EnumerateArray()
                        .Select(part => part.GetString()!).ToArray(),
                    geometryControlNames,
                    geometryControlCount,
                    textureControlNames,
                    textureControlCount,
                    outputs.GetProperty("gltf").GetString()!,
                    outputs.GetProperty("gltfSha256").GetString()!,
                    outputs.GetProperty("sidecar").GetString()!,
                    outputs.GetProperty("sidecarSha256").GetString()!,
                    outputs.GetProperty("bufferSha256").GetString()!,
                    outputs.GetProperty("egt").GetString()!,
                    outputs.GetProperty("egtSha256").GetString()!,
                    ParseFloatArray(value.GetProperty("symmetricTexture")),
                    value.GetProperty("textureControls").EnumerateArray()
                        .Select(control => new OpeningNativeFaceGenTextureControl(
                            control.GetProperty("controlIndex").GetInt32(),
                            control.GetProperty("settingEntity").GetString()!,
                            control.GetProperty("sourceLabel").GetString()!,
                            control.GetProperty("axisSha256").GetString()!,
                            ParseFloatArray(control.GetProperty("axis"))))
                        .ToArray(),
                    runtimeDisposition,
                    fullBody,
                    bodyComponentRoles,
                    bodyComponentSourcesBySex,
                    ParseNativeFaceGenAgeControl(value.GetProperty("ageControl")));
            })
            .ToArray();
        return new OpeningPlayerFaceGenPreviewSet(
            schema,
            status,
            playerFormId,
            geometryControlNames,
            geometryControlCount,
            textureControlNames,
            textureControlCount,
            runtimeDisposition,
            selectionScope,
            unsupportedSelectionScope,
            fullBody,
            bodyComponentRoles,
            bodyComponentSourcesBySex,
            previews);
    }

    private static OpeningPlayerBodyComponentSource ParsePlayerBodyComponentSource(
        JsonElement source) => new(
            source.GetProperty("role").GetString()!,
            source.GetProperty("modelLogicalPath").GetString()!,
            source.GetProperty("modelSha256").GetString()!,
            source.GetProperty("sourceSurfaceCount").GetInt32(),
            source.GetProperty("retainedSurfaceCount").GetInt32(),
            source.GetProperty("retainedSurfaceNames").EnumerateArray()
                .Select(value => value.GetString()!).ToArray(),
            source.GetProperty("omittedDismemberCapSurfaceCount").GetInt32(),
            source.GetProperty("diffuseLogicalPath").GetString()!,
            source.GetProperty("diffuseSha256").GetString()!,
            source.GetProperty("normalLogicalPath").GetString()!,
            source.GetProperty("normalSha256").GetString()!,
            source.GetProperty("shapeTransformDisposition").GetString()!);

    private static OpeningAppearanceRace ParseAppearanceRace(
        JsonElement source,
        IReadOnlyDictionary<string, OwnedUiTexture> textures)
    {
        var sexes = source.GetProperty("sex").EnumerateObject()
            .ToDictionary(
                value => value.Name,
                value => ParseAppearanceSex(value.Value, textures),
                StringComparer.Ordinal);
        return new OpeningAppearanceRace(
            source.GetProperty("formId").GetString()!,
            source.GetProperty("editorId").GetString()!,
            source.GetProperty("label").GetString()!,
            source.GetProperty("recordSha256").GetString()!,
            sexes);
    }

    private static OpeningAppearanceSex ParseAppearanceSex(
        JsonElement source,
        IReadOnlyDictionary<string, OwnedUiTexture> textures) => new(
            source.GetProperty("defaultHairFormId").GetString()!,
            source.GetProperty("defaultEyesFormId").GetString()!,
            source.GetProperty("hairOptions").EnumerateArray()
                .Select(value => ParseAppearanceOption(value, textures))
                .ToArray(),
            source.GetProperty("eyeOptions").EnumerateArray()
                .Select(value => ParseAppearanceOption(value, textures))
                .ToArray());

    private static OpeningAppearanceOption ParseAppearanceOption(
        JsonElement source,
        IReadOnlyDictionary<string, OwnedUiTexture> textures)
    {
        var texturePath = source.GetProperty("textureLogicalPath").GetString()!;
        if (!textures.TryGetValue(texturePath, out var texture))
            throw new InvalidOperationException(
                $"Owned appearance preview texture is absent: {texturePath}");
        return new OpeningAppearanceOption(
            source.GetProperty("formId").GetString()!,
            source.GetProperty("recordType").GetString()!,
            source.GetProperty("editorId").GetString()!,
            source.GetProperty("label").GetString()!,
            source.GetProperty("recordSha256").GetString()!,
            source.GetProperty("modelLogicalPath").ValueKind == JsonValueKind.String
                ? source.GetProperty("modelLogicalPath").GetString()
                : null,
            texture);
    }

    private static OpeningDocReaction ParseDocReaction(JsonElement value) => new(
        value.GetProperty("averageValue").GetSingle(),
        value.GetProperty("highDeviationThreshold").GetSingle(),
        value.GetProperty("lowDeviationThreshold").GetSingle(),
        value.GetProperty("defaultReaction").GetInt32(),
        value.GetProperty("values").EnumerateArray()
            .Select(row => new OpeningDocReactionValue(
                row.GetProperty("formId").GetString()!,
                row.GetProperty("evaluationOrder").GetInt32(),
                row.GetProperty("lowReaction").GetInt32(),
                row.GetProperty("highReaction").GetInt32()))
            .OrderBy(row => row.EvaluationOrder)
            .ToArray());

    private static IReadOnlyList<OpeningCharacterValue> ParseCharacterValues(
        JsonElement values,
        IReadOnlyDictionary<string, OwnedUiTexture> textures) =>
        values.EnumerateArray().Select(value =>
        {
            var logicalPath = OptionalString(value, "iconLogicalPath");
            string? iconPath = null;
            if (logicalPath is not null)
            {
                if (!textures.TryGetValue(logicalPath, out var texture))
                    throw new FileNotFoundException(
                        "Owned character-creation icon was not prepared.",
                        logicalPath);
                iconPath = texture.Path;
            }
            return new OpeningCharacterValue(
                value.GetProperty("formId").GetString()!,
                value.GetProperty("editorId").GetString()!,
                value.GetProperty("sourceName").GetString()!,
                value.GetProperty("name").GetString()!,
                value.GetProperty("description").GetString()!,
                iconPath);
        }).ToArray();

    private static string? OptionalString(JsonElement source, string property) =>
        source.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? OptionalInt(JsonElement source, string property) =>
        source.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : null;

    private static float? OptionalFloat(JsonElement source, string property) =>
        source.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetSingle()
            : null;

    private static bool? OptionalBool(JsonElement source, string property) =>
        source.TryGetProperty(property, out var value) &&
        value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;


}
