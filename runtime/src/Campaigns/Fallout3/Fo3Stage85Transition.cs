using System.Text.Json;

namespace OpenNV.Runtime.Campaigns.Fallout3;

internal sealed record Fo3Stage85State(
    int Stage,
    string AppliedInfoFormId,
    int AppliedCommandCount,
    string NextBoundary);

internal sealed record Fo3Stage85Transition(
    int SourceStage,
    int Stage,
    string InfoFormId,
    int AccountedCommandCount,
    string NextBoundary)
{
    internal const string ExpectedSchema = "opennv-fo3-cg00-stage-85-transition/v1";
    private const string ExpectedDialogueSchema =
        "opennv-fo3-cg00-post-stage-80-dialogue/v1";
    private const string ExpectedDialogueStatus = "source-backed-info-result-trigger";
    private const string ExpectedStatus = "source-backed-empty-stage-result-application";
    private const int GetStageFunction = 58;
    private const int GetIsVoiceTypeFunction = 427;

    internal static Fo3Stage85Transition Load(
        JsonElement dialogue,
        JsonElement transition,
        int expectedSourceStage,
        string questFormId)
    {
        if (RequiredString(dialogue, "schema") != ExpectedDialogueSchema ||
            RequiredString(dialogue, "status") != ExpectedDialogueStatus ||
            RequiredInteger(dialogue, "sourceStage") != expectedSourceStage ||
            RequiredBoolean(dialogue, "dialoguePlaybackImplemented"))
            throw new InvalidOperationException(
                "Fallout 3 post-stage-80 INFO trigger contract is unsupported.");
        var stage = RequiredInteger(dialogue, "targetStage");
        if (stage <= expectedSourceStage)
            throw new InvalidOperationException("Fallout 3 stage-85 INFO result is not forward-moving.");
        var topic = RequiredObject(dialogue, "topic");
        _ = RequiredFormId(topic, "formId");
        _ = RequiredString(topic, "editorId");
        _ = RequiredSha256(topic, "recordSha256");
        if (RequiredFormId(topic, "questFormId") != questFormId)
            throw new InvalidOperationException("Fallout 3 stage-85 INFO topic quest differs.");
        var voice = RequiredObject(dialogue, "voiceType");
        var voiceFormId = RequiredFormId(voice, "formId");
        _ = RequiredString(voice, "editorId");
        _ = RequiredSha256(voice, "recordSha256");
        var info = RequiredObject(dialogue, "info");
        var infoFormId = RequiredFormId(info, "formId");
        _ = RequiredSha256(info, "recordSha256");
        _ = RequiredSha256(info, "resultSourceSha256");
        var conditions = RequiredArray(info, "conditions").EnumerateArray()
            .ToDictionary(value => RequiredInteger(value, "function"));
        if (!conditions.Keys.ToHashSet().SetEquals(new[]
            {
                GetStageFunction,
                GetIsVoiceTypeFunction,
            }))
            throw new InvalidOperationException("Fallout 3 stage-85 INFO conditions differ.");
        ValidateCondition(
            conditions[GetIsVoiceTypeFunction],
            0,
            1.0,
            voiceFormId);
        ValidateCondition(
            conditions[GetStageFunction],
            0x60,
            expectedSourceStage,
            questFormId);

        var stageResult = RequiredObject(dialogue, "stageResult");
        var stageSourceSha256 = RequiredSha256(stageResult, "stageSourceSha256");
        if (!RequiredBoolean(stageResult, "runtimeReady") ||
            RequiredString(stageResult, "contractSchema") != ExpectedSchema ||
            RequiredArray(stageResult, "commands").GetArrayLength() != 0)
            throw new InvalidOperationException("Fallout 3 stage-85 result differs.");

        if (RequiredString(transition, "schema") != ExpectedSchema ||
            RequiredString(transition, "status") != ExpectedStatus ||
            RequiredInteger(transition, "sourceStage") != expectedSourceStage ||
            RequiredInteger(transition, "stage") != stage ||
            RequiredString(transition, "dialogueTriggerSchema") != ExpectedDialogueSchema ||
            RequiredSha256(transition, "stageSourceSha256") != stageSourceSha256 ||
            RequiredInteger(transition, "accountedCommandCount") != 0 ||
            RequiredArray(transition, "commands").GetArrayLength() != 0)
            throw new InvalidOperationException("Fallout 3 stage-85 transition differs.");
        return new Fo3Stage85Transition(
            expectedSourceStage,
            stage,
            infoFormId,
            0,
            RequiredString(transition, "nextBoundary"));
    }

    internal Fo3Stage85State Apply(Fo3Stage80State stage80)
    {
        if (stage80.Stage != SourceStage)
            throw new InvalidOperationException("Fallout 3 stage-85 source state differs.");
        return new Fo3Stage85State(Stage, InfoFormId, AccountedCommandCount, NextBoundary);
    }

    internal void ValidateSavedState(JsonElement source, Fo3Stage85State expected)
    {
        if (RequiredString(source, "schema") != ExpectedSchema ||
            RequiredInteger(source, "stage") != expected.Stage ||
            RequiredFormId(source, "appliedInfoFormId") != expected.AppliedInfoFormId ||
            RequiredInteger(source, "appliedCommandCount") != expected.AppliedCommandCount ||
            RequiredString(source, "nextBoundary") != expected.NextBoundary)
            throw new InvalidOperationException("Saved Fallout 3 stage-85 state differs.");
    }

    private static void ValidateCondition(
        JsonElement source,
        int operatorFlags,
        double comparisonValue,
        string parameter1)
    {
        if (RequiredInteger(source, "operatorFlags") != operatorFlags ||
            RequiredDouble(source, "comparisonValue") != comparisonValue ||
            RequiredFormId(source, "parameter1") != parameter1 ||
            RequiredInteger(source, "parameter2") != 0 ||
            RequiredInteger(source, "runOn") != 0 ||
            RequiredFormId(source, "reference") != "00000000")
            throw new InvalidOperationException("Fallout 3 stage-85 INFO condition differs.");
    }

    private static JsonElement RequiredObject(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException($"Fallout 3 stage-85 field {name} is absent.");
        return value;
    }

    private static JsonElement RequiredArray(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException($"Fallout 3 stage-85 field {name} is absent.");
        return value;
    }

    private static string RequiredString(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
            throw new InvalidOperationException($"Fallout 3 stage-85 field {name} is absent.");
        return value.GetString()!;
    }

    private static int RequiredInteger(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || !value.TryGetInt32(out var result))
            throw new InvalidOperationException($"Fallout 3 stage-85 field {name} is invalid.");
        return result;
    }

    private static double RequiredDouble(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || !value.TryGetDouble(out var result) ||
            !double.IsFinite(result))
            throw new InvalidOperationException($"Fallout 3 stage-85 field {name} is invalid.");
        return result;
    }

    private static bool RequiredBoolean(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) ||
            value.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
            throw new InvalidOperationException($"Fallout 3 stage-85 field {name} is invalid.");
        return value.GetBoolean();
    }

    private static string RequiredFormId(JsonElement parent, string name)
    {
        var value = RequiredString(parent, name);
        if (value.Length != Fo3OpeningFlowNumericContracts.FormIdHexCharacters ||
            value.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidOperationException($"Fallout 3 stage-85 FormID {name} is invalid.");
        return value;
    }

    private static string RequiredSha256(JsonElement parent, string name)
    {
        var value = RequiredString(parent, name);
        if (value.Length != Fo3OpeningFlowNumericContracts.Sha256HexCharacters ||
            value.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidOperationException($"Fallout 3 stage-85 hash {name} is invalid.");
        return value;
    }
}
