using System.Text.Json;
using Godot;

namespace OpenNV.Runtime;

internal sealed record OpeningNewGameFlow(
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
    OpeningCharacterCreation Character)
{
    private const string ExpectedSchema = "opennv-owned-new-game-flow/v1";

    internal static OpeningNewGameFlow Load(
        JsonElement source,
        JsonElement uiFlow,
        IReadOnlyDictionary<string, OpeningTexture> textures)
    {
        if (source.GetProperty("schema").GetString() != ExpectedSchema)
            throw new InvalidOperationException("Owned New Game flow has an unexpected contract.");

        var menus = uiFlow.GetProperty("menus").EnumerateArray()
            .Select(ParseMenu)
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

        var result = new OpeningNewGameFlow(
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
            character);
        Validate(result);
        return result;
    }

    private static OpeningFlowMenu ParseMenu(JsonElement value)
    {
        var source = value.GetProperty("source").GetString()!;
        OpeningManifest.VerifyHash(source, value.GetProperty("sha256").GetString()!);
        return new OpeningFlowMenu(
            value.GetProperty("role").GetString()!,
            value.GetProperty("document").GetString()!,
            value.GetProperty("menuName").GetString()!,
            System.IO.Path.GetFullPath(source),
            value.TryGetProperty("rect", out var rect)
                ? OpeningManifest.ReadRect(rect)
                : null);
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
        value.GetProperty("lines").EnumerateArray()
            .Select(line => line.GetString()!)
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

    private static OpeningFlowCommand ParseCommand(JsonElement value) => new(
        value.GetProperty("kind").GetString()!,
        OptionalString(value, "role"),
        OptionalString(value, "questEditorId"),
        OptionalString(value, "topicEditorId"),
        OptionalString(value, "speakerEditorId"),
        OptionalString(value, "referenceEditorId"),
        OptionalString(value, "itemEditorId"),
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
        value.TryGetProperty("values", out var controls) &&
        controls.ValueKind == JsonValueKind.Array
            ? controls.EnumerateArray().Select(control => control.GetInt32()).ToArray()
            : Array.Empty<int>());

    private static OpeningCharacterCreation ParseCharacter(
        JsonElement value,
        IReadOnlyDictionary<string, OpeningTexture> textures)
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
            ParseCharacterValues(traits.GetProperty("values"), textures));
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
        IReadOnlyDictionary<string, OpeningTexture> textures) =>
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
            flow.Interactions.Count == 0)
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
}

internal sealed record OpeningFlowMenu(
    string Role,
    string Document,
    string MenuName,
    string SourcePath,
    Rect2? Rect);

internal sealed record OpeningStageProgram(
    int Stage,
    string Source,
    IReadOnlyList<OpeningFlowCommand> Commands);

internal sealed record OpeningTimerTransition(int FromStage, int ToStage);

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
    IReadOnlyList<string> Lines,
    IReadOnlyList<OpeningFlowCommand> Commands,
    IReadOnlyList<OpeningDialogueCondition> Conditions,
    IReadOnlyList<string> NextTopicFormIds,
    int ResponseType,
    int Flags,
    bool Goodbye,
    bool SayOnce);

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
    IReadOnlyList<int> ControlValues);

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
    IReadOnlyList<OpeningCharacterValue> TraitValues);

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
