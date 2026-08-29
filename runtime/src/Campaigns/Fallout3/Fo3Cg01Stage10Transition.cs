using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OpenNV.Runtime.Campaigns.Fallout3;

internal sealed record Fo3Cg01DadSpeechCue(
    int Sequence,
    string EngineSex,
    string InfoFormId,
    double DadTimerAfterSeconds,
    Fo3OwnedDialogueResponse Response);

internal sealed record Fo3Cg01Stage10Boundary(bool Applied, string Blocker);

internal sealed record Fo3Cg01Stage10State(
    int SourceStage,
    string ActiveQuestFormId,
    string ActiveQuestEditorId,
    int ActiveStage,
    IReadOnlyList<string> AppliedInfoFormIds,
    int AccountedCommandCount,
    int AppliedCommandCount,
    IReadOnlyList<string> AppliedExecutionTrace,
    double DadTimerSeconds,
    int DisplayedObjectiveIndex,
    IReadOnlyList<int> EnabledPlayerControls,
    string TutorialQuestFormId,
    string TutorialQuestEditorId,
    int TutorialQuestStage,
    int AutosaveRequestCount,
    Fo3Cg01Stage10Boundary NextBoundary);

internal sealed record Fo3Cg01Stage10Transition(
    int SourceStage,
    int TargetStage,
    IReadOnlyDictionary<string, IReadOnlyList<Fo3Cg01DadSpeechCue>> DialogueBySex,
    double FinalDadTimerSeconds,
    int ObjectiveIndex,
    IReadOnlyList<int> EnabledPlayerControls,
    string TutorialQuestFormId,
    string TutorialQuestEditorId,
    int TutorialQuestStage,
    int AutosaveRequestCount,
    string NextBoundaryBlocker)
{
    internal const string ExpectedSchema =
        "opennv-fo3-cg01-stage-5-to-10-transition/v1";
    internal const string ExpectedSavedStateSchema =
        "opennv-fo3-cg01-stage-5-to-10-state/v1";

    private const string ExpectedStatus =
        "source-backed-dad-dialogue-and-stage-result-runtime-unapplied";
    private const string ExpectedTopicFormId = "0001f3d8";
    private const string ExpectedTopicEditorId = "CG01DadSpeech";
    private const string ExpectedVoiceFormId = "00019fdf";
    private const string ExpectedVoiceEditorId = "MaleUniqueDad";
    private const string ExpectedTutorialQuestFormId = "00059c85";
    private const string ExpectedTutorialQuestEditorId = "CGTutorial";
    private const string ExpectedBoundaryBlocker =
        "fo3-cg01-post-stage-10-toddler-world-interaction-not-implemented";
    private const int ExpectedSourceStage = 5;
    private const int ExpectedTargetStage = 10;
    private const int ExpectedTutorialStage = 2;
    private const int ExpectedObjectiveIndex = 10;
    private const int ExpectedStageCommandCount = 4;
    private const int ExpectedDialogueEffectCount = 3;
    private const int ExpectedAccountedCommandCount =
        ExpectedDialogueEffectCount + ExpectedStageCommandCount;
    private const int GetPcIsSexFunction = 131;
    private const int GetIsIdFunction = 72;

    private static readonly int[] ExpectedEnabledPlayerControls = [1, 0, 0, 0, 1, 1, 0];

    internal static Fo3Cg01Stage10Transition Load(
        JsonElement source,
        Fo3Cg01Stage0Transition stage0)
    {
        if (RequiredString(source, "schema") != ExpectedSchema ||
            RequiredString(source, "status") != ExpectedStatus ||
            RequiredInteger(source, "sourceStage") != ExpectedSourceStage ||
            RequiredInteger(source, "targetStage") != ExpectedTargetStage ||
            stage0.ResultingStage != ExpectedSourceStage)
            throw new InvalidOperationException("Fallout 3 CG01 stage-10 transition identity differs.");
        var questFormId = stage0.QuestFormId;
        var questEditorId = stage0.QuestEditorId;
        var dadVariable = stage0.DadVariables.Single(value => value.Variable == "doTalk");

        var dadScript = RequiredObject(source, "dadScript");
        if (RequiredFormId(dadScript, "formId") != dadVariable.ScriptFormId ||
            RequiredString(dadScript, "editorId") != dadVariable.ScriptEditorId ||
            RequiredSha256(dadScript, "recordSha256") !=
                dadVariable.ScriptRecordSha256 ||
            RequiredSha256(dadScript, "sourceSha256") !=
                dadVariable.ScriptSourceSha256 ||
            RequiredString(dadScript, "decrementFunction") != "GetSecondsPassed")
            throw new InvalidOperationException("Fallout 3 CG01 Dad script identity differs.");
        var requiredVariables = RequiredArray(dadScript, "requiredVariables")
            .EnumerateArray().ToArray();
        if (requiredVariables.Length != 2 ||
            !VariableEquals(requiredVariables[0], "doTalk", "short", 1) ||
            !VariableEquals(requiredVariables[1], "talking", "short", 0))
            throw new InvalidOperationException("Fallout 3 CG01 Dad dialogue gate differs.");
        var timerVariable = RequiredObject(dadScript, "timerVariable");
        if (RequiredString(timerVariable, "name") != "timer" ||
            RequiredString(timerVariable, "type") != "float")
            throw new InvalidOperationException("Fallout 3 CG01 Dad timer identity differs.");

        var dialogue = RequiredObject(source, "dialogue");
        if (!RequiredBoolean(dialogue, "dialoguePlaybackPrepared") ||
            !RequiredBoolean(dialogue, "dialoguePlaybackImplemented"))
            throw new InvalidOperationException("Fallout 3 CG01 Dad dialogue is not prepared.");
        var topic = RequiredObject(dialogue, "topic");
        if (RequiredFormId(topic, "formId") != ExpectedTopicFormId ||
            RequiredString(topic, "editorId") != ExpectedTopicEditorId ||
            RequiredFormId(topic, "questFormId") != questFormId)
            throw new InvalidOperationException("Fallout 3 CG01 Dad topic differs.");
        _ = RequiredSha256(topic, "recordSha256");
        var voice = RequiredObject(dialogue, "voiceType");
        if (RequiredFormId(voice, "formId") != ExpectedVoiceFormId ||
            RequiredString(voice, "editorId") != ExpectedVoiceEditorId ||
            !RequiredString(voice, "memberNamespace").Equals(
                "sound\\voice\\fallout3.esm\\maleuniquedad",
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Fallout 3 CG01 Dad voice differs.");
        _ = RequiredSha256(voice, "recordSha256");

        var cues = RequiredArray(dialogue, "branches").EnumerateArray()
            .Select(value => LoadCue(value, stage0, questFormId, questEditorId))
            .OrderBy(value => value.Sequence)
            .ThenBy(value => value.EngineSex, StringComparer.Ordinal)
            .ToArray();
        var expectedCueKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            "0:female", "0:male", "1:female", "1:male",
        };
        if (cues.Length != 4 ||
            !cues.Select(value => $"{value.Sequence}:{value.EngineSex}").ToHashSet(
                StringComparer.Ordinal).SetEquals(expectedCueKeys))
            throw new InvalidOperationException("Fallout 3 CG01 Dad dialogue branches differ.");
        var dialogueBySex = cues.GroupBy(value => value.EngineSex, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<Fo3Cg01DadSpeechCue>)group.OrderBy(
                    value => value.Sequence).ToArray(),
                StringComparer.Ordinal);

        var stageResult = RequiredObject(source, "stageResult");
        _ = RequiredSha256(stageResult, "stageSourceSha256");
        if (RequiredInteger(stageResult, "accountedCommandCount") !=
            ExpectedStageCommandCount)
            throw new InvalidOperationException("Fallout 3 CG01 stage-10 command count differs.");
        var commands = RequiredArray(stageResult, "commands").EnumerateArray().ToArray();
        if (commands.Length != ExpectedStageCommandCount)
            throw new InvalidOperationException("Fallout 3 CG01 stage-10 commands are incomplete.");
        RequireCommand(commands[0], 0, "setObjectiveDisplayed");
        if (RequiredFormId(commands[0], "questFormId") != questFormId ||
            RequiredString(commands[0], "questEditorId") != questEditorId ||
            RequiredInteger(commands[0], "objectiveIndex") != ExpectedObjectiveIndex ||
            !RequiredBoolean(commands[0], "displayed"))
            throw new InvalidOperationException("Fallout 3 CG01 stage-10 objective differs.");
        RequireCommand(commands[1], 1, "setScriptVariable");
        if (RequiredFormId(commands[1], "referenceFormId") != stage0.Dad.FormId ||
            RequiredString(commands[1], "referenceEditorId") != stage0.Dad.EditorId ||
            RequiredString(commands[1], "variable") != "timer" ||
            RequiredString(commands[1], "variableType") != "float" ||
            RequiredDouble(commands[1], "value") != 5.0)
            throw new InvalidOperationException("Fallout 3 CG01 stage-10 Dad timer differs.");
        RequireCommand(commands[2], 2, "enablePlayerControls");
        var controls = RequiredIntegerArray(commands[2], "arguments");
        if (!controls.SequenceEqual(ExpectedEnabledPlayerControls))
            throw new InvalidOperationException("Fallout 3 CG01 stage-10 controls differ.");
        RequireCommand(commands[3], 3, "autosave");
        var autosaveCount = RequiredInteger(commands[3], "requestCount");
        if (autosaveCount != 1)
            throw new InvalidOperationException("Fallout 3 CG01 stage-10 autosave differs.");

        var boundary = RequiredObject(source, "nextBoundary");
        if (RequiredBoolean(boundary, "applied") ||
            RequiredString(boundary, "blocker") != ExpectedBoundaryBlocker)
            throw new InvalidOperationException("Fallout 3 CG01 stage-10 boundary differs.");
        return new Fo3Cg01Stage10Transition(
            ExpectedSourceStage,
            ExpectedTargetStage,
            dialogueBySex,
            5.0,
            ExpectedObjectiveIndex,
            controls,
            ExpectedTutorialQuestFormId,
            ExpectedTutorialQuestEditorId,
            ExpectedTutorialStage,
            autosaveCount,
            ExpectedBoundaryBlocker);
    }

    internal IReadOnlyList<Fo3Cg01DadSpeechCue> DialogueFor(string engineSex) =>
        DialogueBySex.TryGetValue(engineSex, out var cues)
            ? cues
            : throw new InvalidOperationException(
                $"Fallout 3 CG01 Dad dialogue has no branch for {engineSex}.");

    internal Fo3Cg01Stage10State Apply(Fo3Cg01Stage0State stage5, string engineSex)
    {
        if (stage5.ActiveStage != SourceStage ||
            stage5.ActiveQuestFormId != "00014e83" ||
            stage5.ActiveQuestEditorId != "CG01" ||
            stage5.NextBoundary.Applied ||
            stage5.NextBoundary.Blocker != Fo3Cg01Stage0Transition.NextBoundaryBlocker ||
            stage5.Dad.ScriptVariables.Single(value => value.Variable == "doTalk").Value != 1 ||
            stage5.Dad.ScriptVariables.Single(value => value.Variable == "talking").Value != 0)
            throw new InvalidOperationException("Fallout 3 CG01 stage-10 source state differs.");
        var cues = DialogueFor(engineSex);
        if (cues.Count != 2 || cues[0].Sequence != 0 || cues[1].Sequence != 1 ||
            cues[0].DadTimerAfterSeconds != 1.0 || cues[1].DadTimerAfterSeconds != 5.0)
            throw new InvalidOperationException("Fallout 3 CG01 Dad dialogue sequence differs.");
        var trace = new[]
        {
            "d0:0:setScriptVariable",
            "d1:0:setStage",
            "d1:1:setStage",
            "s10:0:setObjectiveDisplayed",
            "s10:1:setScriptVariable",
            "s10:2:enablePlayerControls",
            "s10:3:autosave",
        };
        return new Fo3Cg01Stage10State(
            SourceStage,
            stage5.ActiveQuestFormId,
            stage5.ActiveQuestEditorId,
            TargetStage,
            cues.Select(value => value.InfoFormId).ToArray(),
            ExpectedAccountedCommandCount,
            ExpectedAccountedCommandCount,
            trace,
            FinalDadTimerSeconds,
            ObjectiveIndex,
            EnabledPlayerControls,
            TutorialQuestFormId,
            TutorialQuestEditorId,
            TutorialQuestStage,
            AutosaveRequestCount,
            new Fo3Cg01Stage10Boundary(false, NextBoundaryBlocker));
    }

    internal object SavedState(Fo3Cg01Stage10State state) => new
    {
        schema = ExpectedSavedStateSchema,
        sourceStage = state.SourceStage,
        activeQuest = new
        {
            formId = state.ActiveQuestFormId,
            editorId = state.ActiveQuestEditorId,
            stage = state.ActiveStage,
        },
        appliedInfoFormIds = state.AppliedInfoFormIds,
        accountedCommandCount = state.AccountedCommandCount,
        appliedCommandCount = state.AppliedCommandCount,
        appliedExecutionTrace = state.AppliedExecutionTrace,
        dadTimerSeconds = state.DadTimerSeconds,
        displayedObjectiveIndex = state.DisplayedObjectiveIndex,
        enabledPlayerControls = state.EnabledPlayerControls,
        tutorialQuest = new
        {
            formId = state.TutorialQuestFormId,
            editorId = state.TutorialQuestEditorId,
            stage = state.TutorialQuestStage,
        },
        autosaveRequestCount = state.AutosaveRequestCount,
        nextBoundary = new
        {
            applied = state.NextBoundary.Applied,
            blocker = state.NextBoundary.Blocker,
        },
    };

    internal void ValidateSavedState(JsonElement source, Fo3Cg01Stage10State expected)
    {
        var activeQuest = RequiredObject(source, "activeQuest");
        var tutorial = RequiredObject(source, "tutorialQuest");
        var boundary = RequiredObject(source, "nextBoundary");
        if (RequiredString(source, "schema") != ExpectedSavedStateSchema ||
            RequiredInteger(source, "sourceStage") != expected.SourceStage ||
            RequiredFormId(activeQuest, "formId") != expected.ActiveQuestFormId ||
            RequiredString(activeQuest, "editorId") != expected.ActiveQuestEditorId ||
            RequiredInteger(activeQuest, "stage") != expected.ActiveStage ||
            !RequiredFormIdArray(source, "appliedInfoFormIds").SequenceEqual(
                expected.AppliedInfoFormIds) ||
            RequiredInteger(source, "accountedCommandCount") != expected.AccountedCommandCount ||
            RequiredInteger(source, "appliedCommandCount") != expected.AppliedCommandCount ||
            !RequiredStringArray(source, "appliedExecutionTrace").SequenceEqual(
                expected.AppliedExecutionTrace) ||
            RequiredDouble(source, "dadTimerSeconds") != expected.DadTimerSeconds ||
            RequiredInteger(source, "displayedObjectiveIndex") !=
                expected.DisplayedObjectiveIndex ||
            !RequiredIntegerArray(source, "enabledPlayerControls").SequenceEqual(
                expected.EnabledPlayerControls) ||
            RequiredFormId(tutorial, "formId") != expected.TutorialQuestFormId ||
            RequiredString(tutorial, "editorId") != expected.TutorialQuestEditorId ||
            RequiredInteger(tutorial, "stage") != expected.TutorialQuestStage ||
            RequiredInteger(source, "autosaveRequestCount") != expected.AutosaveRequestCount ||
            RequiredBoolean(boundary, "applied") != expected.NextBoundary.Applied ||
            RequiredString(boundary, "blocker") != expected.NextBoundary.Blocker)
            throw new InvalidOperationException("Saved Fallout 3 CG01 stage-10 state differs.");
    }

    private static Fo3Cg01DadSpeechCue LoadCue(
        JsonElement source,
        Fo3Cg01Stage0Transition stage0,
        string questFormId,
        string questEditorId)
    {
        var sequence = RequiredInteger(source, "sequence");
        var engineSex = RequiredString(source, "engineSex");
        if (sequence is < 0 or > 1 || engineSex is not "male" and not "female")
            throw new InvalidOperationException("Fallout 3 CG01 Dad cue identity differs.");
        var expectedInfoFormId = (sequence, engineSex) switch
        {
            (0, "female") => "0001f3e8",
            (0, "male") => "0001f3e9",
            (1, "female") => "0001f3e6",
            (1, "male") => "0001f3e7",
            _ => throw new InvalidOperationException("Fallout 3 CG01 Dad cue differs."),
        };
        var infoFormId = RequiredFormId(source, "infoFormId");
        if (infoFormId != expectedInfoFormId)
            throw new InvalidOperationException("Fallout 3 CG01 Dad INFO differs.");
        _ = RequiredSha256(source, "recordSha256");
        _ = RequiredSha256(source, "resultSourceSha256");
        var conditions = RequiredArray(source, "conditions").EnumerateArray()
            .ToDictionary(value => RequiredInteger(value, "function"));
        if (!conditions.Keys.ToHashSet().SetEquals(new[] { GetPcIsSexFunction, GetIsIdFunction }))
            throw new InvalidOperationException("Fallout 3 CG01 Dad INFO conditions differ.");
        ValidateCondition(
            conditions[GetPcIsSexFunction],
            engineSex == "female" ? "00000001" : "00000000");
        ValidateCondition(conditions[GetIsIdFunction], stage0.Dad.BaseFormId);

        var effects = RequiredArray(source, "effects").EnumerateArray().ToArray();
        double timerAfter;
        if (sequence == 0)
        {
            if (effects.Length != 1 || RequiredString(effects[0], "kind") != "setScriptVariable" ||
                RequiredFormId(effects[0], "referenceFormId") != stage0.Dad.FormId ||
                RequiredString(effects[0], "referenceEditorId") != stage0.Dad.EditorId ||
                RequiredString(effects[0], "variable") != "timer" ||
                RequiredString(effects[0], "variableType") != "float" ||
                RequiredDouble(effects[0], "value") != 1.0)
                throw new InvalidOperationException("Fallout 3 CG01 Dad prelude effect differs.");
            timerAfter = 1.0;
        }
        else
        {
            if (effects.Length != 2 ||
                !StageEffectEquals(effects[0], questFormId, questEditorId, ExpectedTargetStage) ||
                !StageEffectEquals(
                    effects[1],
                    ExpectedTutorialQuestFormId,
                    ExpectedTutorialQuestEditorId,
                    ExpectedTutorialStage))
                throw new InvalidOperationException("Fallout 3 CG01 Dad stage effects differ.");
            timerAfter = 5.0;
        }

        var response = RequiredObject(source, "response");
        var responseIndex = RequiredInteger(response, "index");
        if (responseIndex != 1)
            throw new InvalidOperationException("Fallout 3 CG01 Dad response index differs.");
        var text = RequiredString(response, "text");
        var expectedTextSha256 = RequiredSha256(response, "textSha256");
        var actualTextSha256 = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
        if (actualTextSha256 != expectedTextSha256)
            throw new InvalidOperationException("Fallout 3 CG01 Dad response text differs.");
        var suffix = $"_{infoFormId}_{responseIndex}";
        return new Fo3Cg01DadSpeechCue(
            sequence,
            engineSex,
            infoFormId,
            timerAfter,
            new Fo3OwnedDialogueResponse(
                responseIndex,
                text,
                LoadDialogueAsset(RequiredObject(response, "voice"), suffix + ".ogg"),
                LoadDialogueAsset(RequiredObject(response, "lip"), suffix + ".lip")));
    }

    private static Fo3OwnedDialogueAsset LoadDialogueAsset(
        JsonElement source,
        string expectedSuffix)
    {
        var logicalPath = RequiredString(source, "logicalPath").Replace('/', '\\');
        if (!logicalPath.StartsWith(
                "sound\\voice\\fallout3.esm\\maleuniquedad\\",
                StringComparison.OrdinalIgnoreCase) ||
            !logicalPath.EndsWith(expectedSuffix, StringComparison.OrdinalIgnoreCase) ||
            RequiredString(source, "sourceArchive") != "Fallout - Voices.bsa")
            throw new InvalidOperationException("Fallout 3 CG01 Dad dialogue asset differs.");
        _ = RequiredSha256(source, "sourceArchiveSha256");
        var path = Path.GetFullPath(RequiredString(source, "source"));
        var bytes = RequiredLong(source, "bytes");
        var sha256 = RequiredSha256(source, "sha256");
        var info = new FileInfo(path);
        if (!info.Exists || info.Length != bytes)
            throw new InvalidOperationException("Fallout 3 CG01 Dad dialogue asset is absent.");
        using var stream = File.OpenRead(path);
        var actualSha256 = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        if (!actualSha256.Equals(sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Fallout 3 CG01 Dad dialogue asset changed.");
        return new Fo3OwnedDialogueAsset(logicalPath, path, bytes, sha256);
    }

    private static bool VariableEquals(JsonElement source, string name, string type, int value) =>
        RequiredString(source, "name") == name &&
        RequiredString(source, "type") == type &&
        RequiredInteger(source, "value") == value;

    private static bool StageEffectEquals(
        JsonElement source,
        string questFormId,
        string questEditorId,
        int stage) =>
        RequiredString(source, "kind") == "setStage" &&
        RequiredFormId(source, "questFormId") == questFormId &&
        RequiredString(source, "questEditorId") == questEditorId &&
        RequiredInteger(source, "stage") == stage;

    private static void ValidateCondition(JsonElement source, string parameter1)
    {
        if (RequiredInteger(source, "operatorFlags") != 0 ||
            RequiredDouble(source, "comparisonValue") != 1.0 ||
            RequiredFormId(source, "parameter1") != parameter1 ||
            RequiredInteger(source, "parameter2") != 0 ||
            RequiredInteger(source, "runOn") != 0 ||
            RequiredFormId(source, "reference") != "00000000")
            throw new InvalidOperationException("Fallout 3 CG01 Dad INFO condition differs.");
    }

    private static void RequireCommand(JsonElement source, int index, string kind)
    {
        if (RequiredInteger(source, "index") != index || RequiredString(source, "kind") != kind)
            throw new InvalidOperationException("Fallout 3 CG01 stage-10 command order differs.");
    }

    private static JsonElement RequiredObject(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException($"Fallout 3 CG01 stage-10 field {name} is absent.");
        return value;
    }

    private static JsonElement RequiredArray(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException($"Fallout 3 CG01 stage-10 field {name} is absent.");
        return value;
    }

    private static string RequiredString(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
            throw new InvalidOperationException($"Fallout 3 CG01 stage-10 field {name} is absent.");
        return value.GetString()!;
    }

    private static int RequiredInteger(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || !value.TryGetInt32(out var result))
            throw new InvalidOperationException($"Fallout 3 CG01 stage-10 field {name} is invalid.");
        return result;
    }

    private static double RequiredDouble(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || !value.TryGetDouble(out var result) ||
            !double.IsFinite(result))
            throw new InvalidOperationException($"Fallout 3 CG01 stage-10 field {name} is invalid.");
        return result;
    }

    private static long RequiredLong(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || !value.TryGetInt64(out var result) ||
            result < 1)
            throw new InvalidOperationException($"Fallout 3 CG01 stage-10 field {name} is invalid.");
        return result;
    }

    private static bool RequiredBoolean(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) ||
            value.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
            throw new InvalidOperationException($"Fallout 3 CG01 stage-10 field {name} is invalid.");
        return value.GetBoolean();
    }

    private static string RequiredFormId(JsonElement parent, string name)
    {
        var value = RequiredString(parent, name);
        if (value.Length != Fo3OpeningFlowNumericContracts.FormIdHexCharacters ||
            value.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidOperationException($"Fallout 3 CG01 stage-10 FormID {name} is invalid.");
        return value;
    }

    private static string RequiredSha256(JsonElement parent, string name)
    {
        var value = RequiredString(parent, name);
        if (value.Length != Fo3OpeningFlowNumericContracts.Sha256HexCharacters ||
            value.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidOperationException($"Fallout 3 CG01 stage-10 hash {name} is invalid.");
        return value;
    }

    private static int[] RequiredIntegerArray(JsonElement parent, string name) =>
        RequiredArray(parent, name).EnumerateArray().Select(value =>
            value.TryGetInt32(out var result)
                ? result
                : throw new InvalidOperationException(
                    $"Fallout 3 CG01 stage-10 field {name} contains an invalid value.")).ToArray();

    private static string[] RequiredStringArray(JsonElement parent, string name) =>
        RequiredArray(parent, name).EnumerateArray().Select(value =>
            value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
                ? value.GetString()!
                : throw new InvalidOperationException(
                    $"Fallout 3 CG01 stage-10 field {name} contains an invalid value.")).ToArray();

    private static string[] RequiredFormIdArray(JsonElement parent, string name) =>
        RequiredArray(parent, name).EnumerateArray().Select(value =>
        {
            if (value.ValueKind != JsonValueKind.String || value.GetString() is not { } formId ||
                formId.Length != Fo3OpeningFlowNumericContracts.FormIdHexCharacters ||
                formId.Any(character => !Uri.IsHexDigit(character)))
                throw new InvalidOperationException(
                    $"Fallout 3 CG01 stage-10 field {name} contains an invalid FormID.");
            return formId;
        }).ToArray();
}
