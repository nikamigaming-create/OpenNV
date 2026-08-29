using System.Text.Json;
using Godot;
using OpenNV.Runtime.Presentation.Ui;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal sealed record OpeningNewGameFlow(
    OpeningCommandContract CommandContract,
    string QuestFormId,
    string QuestEditorId,
    IReadOnlyDictionary<int, string> Objectives,
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
    private const string ExpectedSchema = "opennv-owned-new-game-flow/v6";
    private const string ExpectedCommandContractSchema =
        "opennv-owned-opening-command-contract/v1";
    private const string ExpectedGuideActorAiSchema =
        "opennv-owned-guide-actor-ai/v1";
    private const string ExpectedPlayerAnimationSchema =
        "opennv-owned-player-animation-graph/v1";
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
            background);
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
        OptionalString(value, "itemEditorId"),
        OptionalString(value, "packageEditorId"),
        OptionalString(value, "modifierEditorId"),
        OptionalString(value, "operation"),
        OptionalString(value, "targetEditorId"),
        OptionalString(value, "globalEditorId"),
        OptionalString(value, "variable") ?? OptionalString(value, "value"),
        OptionalString(value, "idleEditorId"),
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
        OptionalString(value, "itemFormId"),
        OptionalString(value, "itemRecordType"),
        OptionalString(value, "questFormId"),
        OptionalString(value, "questRecordType"),
        OptionalString(value, "globalFormId"),
        OptionalString(value, "globalRecordType"),
        OptionalString(value, "ownerEditorId"),
        OptionalString(value, "ownerFormId"),
        OptionalString(value, "ownerRecordType"),
        OptionalString(value, "referenceFormId"),
        OptionalString(value, "referenceRecordType"));

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
            new OpeningGuideLocomotion(
                ParseGuideLocomotionClip(locomotion.GetProperty("walk")),
                ParseGuideLocomotionClip(locomotion.GetProperty("run"))));
    }

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
            new OpeningGuideRootMotion(
                rootMotion.GetProperty("sequenceName").GetString()!,
                rootMotion.GetProperty("targetNode").GetString()!,
                rootMotion.GetProperty("startSeconds").GetSingle(),
                rootMotion.GetProperty("stopSeconds").GetSingle(),
                rootMotion.GetProperty("cycleType").GetInt32(),
                ReadVector3(rootMotion.GetProperty("displacementGodotGameUnits")),
                rootMotion.GetProperty("speedGameUnitsPerSecond").GetSingle()));
    }

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

    private static void Validate(OpeningNewGameFlow flow)
    {
        if (string.IsNullOrWhiteSpace(flow.QuestFormId) ||
            string.IsNullOrWhiteSpace(flow.QuestEditorId) ||
            flow.ReferenceCanvasSize.X <= 0.0f ||
            flow.ReferenceCanvasSize.Y <= 0.0f ||
            !flow.Stages.ContainsKey(flow.CompletionStage) ||
            !flow.Stages.ContainsKey(flow.PsychologyStartStage) ||
            !flow.Stages.ContainsKey(flow.OutroStartStage) ||
            !flow.TopicsByFormId.ContainsKey(flow.OutroTopicFormId) ||
            flow.Menus.Count == 0 ||
            flow.Strings.Count == 0 ||
            flow.SceneRoles.Count == 0 ||
            flow.Interactions.Count == 0 ||
            !flow.SceneRoles.TryGetValue(
                flow.DialogueVoice.SpeakerRole,
                out var dialogueSpeaker) ||
            !dialogueSpeaker.ReferenceFormId.Equals(
                flow.DialogueVoice.SpeakerReferenceFormId,
                StringComparison.OrdinalIgnoreCase) ||
            !dialogueSpeaker.BaseFormId.Equals(
                flow.DialogueVoice.SpeakerBaseFormId,
                StringComparison.OrdinalIgnoreCase) ||
            !flow.SceneRoles.TryGetValue(flow.GuideActorAi.Role, out var guideRole) ||
            !guideRole.ReferenceFormId.Equals(
                flow.GuideActorAi.ReferenceFormId,
                StringComparison.OrdinalIgnoreCase) ||
            !guideRole.BaseFormId.Equals(
                flow.GuideActorAi.BaseFormId,
                StringComparison.OrdinalIgnoreCase) ||
            !flow.GuideActorAi.QuestFormId.Equals(
                flow.QuestFormId,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Owned New Game flow is incomplete.");
        if (flow.Stages.Values
            .SelectMany(value => value.Commands)
            .Where(value => value.Kind == "objective" &&
                value.QuestEditorId?.Equals(
                    flow.QuestEditorId,
                    StringComparison.OrdinalIgnoreCase) == true)
            .Any(value => value.Index is null || !flow.Objectives.ContainsKey(value.Index.Value)))
            throw new InvalidOperationException("Owned New Game objective text is incomplete.");
        if (flow.TimerTransitions.Values.Any(value =>
                !flow.Stages.ContainsKey(value.FromStage) ||
                !flow.Stages.ContainsKey(value.ToStage)) ||
            flow.MenuCloseTransitions.Any(value =>
                !flow.Stages.ContainsKey(value.Key) ||
                !flow.Stages.ContainsKey(value.Value)) ||
            flow.Interactions.Any(value =>
                !flow.SceneRoles.ContainsKey(value.TargetRole) ||
                !flow.Stages.ContainsKey(value.FromStage) ||
                !flow.Stages.ContainsKey(value.ToStage)))
            throw new InvalidOperationException("Owned New Game transitions do not join authored stages.");
        var commands = flow.Stages.Values
            .SelectMany(value => value.Commands)
            .Concat(flow.TopicsByFormId.Values.SelectMany(topic =>
                topic.Infos.SelectMany(info => info.Commands)))
            .Concat(flow.PsychologyRootInfo.Commands)
            .ToArray();
        ValidateCommandContract(flow.CommandContract, commands);
        var dialogueInfos = flow.TopicsByFormId.Values
            .SelectMany(topic => topic.Infos)
            .Append(flow.PsychologyRootInfo)
            .ToArray();
        var uniqueDialogueInfos = dialogueInfos
            .GroupBy(info => info.FormId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
        if (string.IsNullOrWhiteSpace(flow.DialogueVoice.VoiceTypeFormId) ||
            string.IsNullOrWhiteSpace(flow.DialogueVoice.VoiceTypeEditorId) ||
            string.IsNullOrWhiteSpace(flow.DialogueVoice.MemberNamespace) ||
            string.IsNullOrWhiteSpace(flow.DialogueVoice.ArchiveSchema) ||
            string.IsNullOrWhiteSpace(flow.DialogueVoice.ArchiveRecipeId) ||
            string.IsNullOrWhiteSpace(flow.DialogueVoice.ArchiveRecipeSha256) ||
            flow.DialogueVoice.ArchiveCount == 0 ||
            flow.DialogueVoice.InfoCount != uniqueDialogueInfos.Length ||
            flow.DialogueVoice.ResponseCount !=
                uniqueDialogueInfos.Sum(info => info.Responses.Count) ||
            dialogueInfos.Any(info =>
                info.Responses.Count == 0 ||
                info.Responses.Where((response, index) => response.Index != index + 1).Any() ||
                info.Responses.Any(response =>
                    string.IsNullOrWhiteSpace(response.Text) ||
                    !ValidDialogueAsset(response.Voice) ||
                    !ValidDialogueAsset(response.Lip))))
            throw new InvalidOperationException(
                "Owned dialogue response, voice, or lip graph is incomplete.");
        var guide = flow.GuideActorAi;
        if (guide.PackagePriority.Count == 0 ||
            guide.PackagePriority.Count != guide.Packages.Count ||
            guide.PackagePriority.Distinct(StringComparer.OrdinalIgnoreCase).Count() !=
                guide.PackagePriority.Count ||
            guide.PackagePriority.Any(form => !guide.Packages.ContainsKey(form)) ||
            guide.Packages.Values.Any(package =>
                string.IsNullOrWhiteSpace(package.FormId) ||
                string.IsNullOrWhiteSpace(package.EditorId) ||
                string.IsNullOrWhiteSpace(package.RecordSha256) ||
                string.IsNullOrWhiteSpace(package.PackageTypeName) ||
                package.Conditions.Any(condition =>
                    string.IsNullOrWhiteSpace(condition.FunctionName) ||
                    !float.IsFinite(condition.ComparisonValue)) ||
                package.Location is { TypeName: "nearReference", Reference: null } ||
                package.Location?.Reference is { } destination &&
                    (!destination.PositionGameUnits.IsFinite() ||
                        !destination.RotationGodot.IsNormalized()) ||
                package.IdleAnimationFormIds.Count !=
                    package.IdleAnimationLogicalPaths.Count) ||
            !ValidGuideLocomotionClip(guide.Locomotion.Walk) ||
            !ValidGuideLocomotionClip(guide.Locomotion.Run))
            throw new InvalidOperationException("Owned guide-actor AI graph is incomplete.");
        if (flow.PlayerAnimation.Packages.Count == 0 ||
            flow.PlayerAnimation.Animations.Count == 0 ||
            flow.PlayerAnimation.Packages.Values.Any(package =>
                package.IdleTimerSeconds < 0.0f ||
                package.IdleAnimationFormIds.Any(form =>
                    !flow.PlayerAnimation.Animations.ContainsKey(form)) ||
                package.EventAnimationFormIds.Values.Any(form =>
                    form is not null && !flow.PlayerAnimation.Animations.ContainsKey(form))) ||
            flow.PlayerAnimation.Animations.Values.Any(animation =>
                animation.Track.TargetNode != flow.PlayerAnimation.CameraNode ||
                animation.Track.StopSeconds <= animation.Track.StartSeconds ||
                animation.Track.ParentChain.Count == 0 ||
                animation.Track.Samples.Count < 2 ||
                animation.Track.Samples[0].TimeSeconds != animation.Track.StartSeconds ||
                animation.Track.Samples[^1].TimeSeconds != animation.Track.StopSeconds ||
                animation.Track.Samples.Zip(
                    animation.Track.Samples.Skip(1),
                    (first, second) => second.TimeSeconds > first.TimeSeconds)
                    .Any(increasing => !increasing)) ||
            commands.Any(command =>
                command.Kind == "addScriptPackage" &&
                (command.PackageEditorId is null ||
                    !flow.PlayerAnimation.Packages.ContainsKey(command.PackageEditorId))) ||
            commands.Any(command =>
                command.Kind == "imageSpaceModifier" &&
                (command.ModifierEditorId is null ||
                    !flow.ImageSpaceModifiers.ContainsKey(command.ModifierEditorId))))
            throw new InvalidOperationException(
                "Owned player animation or image-space command graph is incomplete.");
        var character = flow.Character;
        if (character.SexChoices.Count == 0 ||
            character.SpecialValues.Count == 0 ||
            character.SkillValues.Count == 0 ||
            character.TraitValues.Count == 0 ||
            character.SpecialMinimum > character.SpecialInitial ||
            character.SpecialInitial > character.SpecialMaximum ||
            character.SpecialTotalPoints <
                character.SpecialInitial * character.SpecialValues.Count ||
            character.DocReaction.Values.Count != character.SpecialValues.Count ||
            character.TagSkillMaximumSelected <= 0 ||
            character.TagSkillMaximumSelected > character.SkillValues.Count ||
            character.TraitMaximumSelected <= 0 ||
            character.TraitMaximumSelected > character.TraitValues.Count)
            throw new InvalidOperationException("Owned character-creation contract is invalid.");
    }

    private static void ValidateCommandContract(
        OpeningCommandContract contract,
        IReadOnlyList<OpeningFlowCommand> commands)
    {
        var kindCounts = commands
            .GroupBy(command => command.Kind, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var identityCounts = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["itemEditorId"] = commands.Count(command => command.ItemEditorId is not null),
            ["questEditorId"] = commands.Count(command => command.QuestEditorId is not null),
            ["globalEditorId"] = commands.Count(command => command.GlobalEditorId is not null),
            ["ownerEditorId"] = commands.Count(command => command.OwnerEditorId is not null),
            ["referenceEditorId"] = commands.Count(command => command.ReferenceEditorId is not null),
        };
        foreach (var empty in identityCounts.Where(value => value.Value == 0).ToArray())
            identityCounts.Remove(empty.Key);
        if (contract.Schema != ExpectedCommandContractSchema ||
            !contract.AllEmittedKindsRuntimeBlocking ||
            !contract.AllDeclaredRecordReferencesResolved ||
            contract.CommandCount != commands.Count ||
            !DictionaryMatches(contract.KindCounts, kindCounts) ||
            !DictionaryMatches(contract.RecordIdentityCounts, identityCounts) ||
            commands.Any(command => !RuntimeCommandKinds.Contains(command.Kind)) ||
            commands.Any(command =>
                !ValidIdentity(command.ItemEditorId, command.ItemFormId, command.ItemRecordType) ||
                !ValidIdentity(
                    command.QuestEditorId,
                    command.QuestFormId,
                    command.QuestRecordType,
                    "QUST") ||
                !ValidIdentity(
                    command.GlobalEditorId,
                    command.GlobalFormId,
                    command.GlobalRecordType,
                    "GLOB") ||
                !ValidIdentity(
                    command.OwnerEditorId,
                    command.OwnerFormId,
                    command.OwnerRecordType,
                    "QUST") ||
                !ValidReferenceIdentity(command)))
            throw new InvalidOperationException(
                "Owned opening command execution contract is incomplete.");
    }

    private static bool DictionaryMatches(
        IReadOnlyDictionary<string, int> expected,
        IReadOnlyDictionary<string, int> actual) =>
        expected.Count == actual.Count &&
        expected.All(value => actual.GetValueOrDefault(value.Key) == value.Value);

    private static bool ValidIdentity(
        string? editorId,
        string? formId,
        string? recordType,
        string? expectedRecordType = null)
    {
        if (editorId is null)
            return formId is null && recordType is null;
        if (string.IsNullOrWhiteSpace(formId) || string.IsNullOrWhiteSpace(recordType) ||
            expectedRecordType is not null && recordType != expectedRecordType)
            return false;
        try
        {
            return FalloutFormId.Normalize(formId) == formId;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static bool ValidReferenceIdentity(OpeningFlowCommand command) =>
        ValidIdentity(
            command.ReferenceEditorId,
            command.ReferenceFormId,
            command.ReferenceRecordType) &&
        (command.ReferenceRecordType is null or "REFR" or "ACHR" or "ACRE");

    private static bool ValidGuideLocomotionClip(OpeningGuideLocomotionClip clip) =>
        !string.IsNullOrWhiteSpace(clip.LogicalPath) &&
        !string.IsNullOrWhiteSpace(clip.Sha256) &&
        !string.IsNullOrWhiteSpace(clip.RootMotion.SequenceName) &&
        !string.IsNullOrWhiteSpace(clip.RootMotion.TargetNode) &&
        float.IsFinite(clip.RootMotion.StartSeconds) &&
        float.IsFinite(clip.RootMotion.StopSeconds) &&
        float.IsFinite(clip.RootMotion.SpeedGameUnitsPerSecond) &&
        clip.RootMotion.StopSeconds > clip.RootMotion.StartSeconds &&
        clip.RootMotion.SpeedGameUnitsPerSecond > 0.0f &&
        clip.RootMotion.DisplacementGodotGameUnits.IsFinite();

    private static bool ValidDialogueAsset(OpeningDialogueAsset asset) =>
        !string.IsNullOrWhiteSpace(asset.LogicalPath) &&
        !string.IsNullOrWhiteSpace(asset.SourcePath) &&
        !string.IsNullOrWhiteSpace(asset.Sha256) &&
        !string.IsNullOrWhiteSpace(asset.SourceArchive) &&
        !string.IsNullOrWhiteSpace(asset.SourceArchiveSha256);
}

internal sealed record OpeningFlowMenu(
    string Role,
    string Document,
    string MenuName,
    string SourcePath,
    Rect2? Rect,
    OwnedUiTexture? Background);

internal sealed record OpeningStageProgram(
    int Stage,
    string Source,
    IReadOnlyList<OpeningFlowCommand> Commands);

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
    string? ReferenceRecordType);

internal sealed record OpeningGuideActorAi(
    string Role,
    string ReferenceFormId,
    string BaseFormId,
    string QuestFormId,
    IReadOnlyList<string> PackagePriority,
    IReadOnlyDictionary<string, OpeningGuidePackage> Packages,
    OpeningGuideLocomotion Locomotion);

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
