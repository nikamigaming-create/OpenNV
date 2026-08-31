using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.World.Actors;

namespace OpenNV.Runtime.Campaigns.Fallout3;

internal sealed record Fo3Cg01Stage12DadResponseCue(
    int Sequence,
    string InfoFormId,
    bool SayOnce,
    Fo3Cg01DadSpeakerIdle SpeakerIdle,
    Fo3OwnedDialogueResponse Response,
    int? TargetStage);

internal sealed record Fo3Cg01Stage14State(
    int SourceStage,
    string ActiveQuestFormId,
    string ActiveQuestEditorId,
    int ActiveStage,
    IReadOnlyList<string> AppliedInfoFormIds,
    int DadTalking,
    bool DadLooksAtPlayer,
    bool DadPackageEvaluated,
    int AccountedCommandCount,
    int AppliedCommandCount,
    Fo3Cg01Stage12Boundary NextBoundary);

internal sealed record Fo3Cg01Stage12DadResponse(
    int SourceStage,
    int TargetStage,
    string TopicFormId,
    string TopicEditorId,
    string DadReferenceFormId,
    IReadOnlyList<Fo3Cg01Stage12DadResponseCue> Cues,
    string NextBoundaryBlocker)
{
    internal const string ExpectedSchema =
        "opennv-fo3-cg01-stage-12-to-14-dad-response/v1";
    internal const string ExpectedSavedStateSchema =
        "opennv-fo3-cg01-stage-12-to-14-dad-response-state/v1";

    private const string ExpectedStatus =
        "source-backed-say-once-dad-response-runtime-unapplied";
    private const string ExpectedLookTarget = "player";
    private const string ExpectedBoundaryBlocker =
        "fo3-cg01-post-stage-14-runtime-not-implemented";
    private const int ExpectedCueCount = 2;
    private const int ExpectedStageCommandCount = 1;
    private const int FirstSourceCommandIndex = 0;
    private const int ExpectedDadTalkingAfterCue = 0;
    private const int ExpectedConditionalStageSource = 75;
    private const int ExpectedConditionalStageTarget = 80;
    private const int GetStageConditionFunction = 58;
    private const int GetIsIdConditionFunction = 72;

    internal static Fo3Cg01Stage12DadResponse Load(
        JsonElement source,
        Fo3Cg01Stage0Transition stage0,
        Fo3Cg01Stage12Transition stage12)
    {
        var sourceStage = RequiredInteger(source, "sourceStage");
        var targetStage = RequiredInteger(source, "targetStage");
        if (RequiredString(source, "schema") != ExpectedSchema ||
            RequiredString(source, "status") != ExpectedStatus ||
            sourceStage != stage12.TargetStage ||
            targetStage <= sourceStage ||
            RequiredFormId(source, "dadReferenceFormId") != stage0.Dad.FormId)
            throw new InvalidOperationException(
                "Fallout 3 CG01 stage-12 Dad response identity differs.");
        _ = RequiredFormId(source, "dadScriptFormId");
        _ = RequiredSha256(source, "dadScriptSourceSha256");

        var completion = RequiredObject(source, "sayToDone");
        if (RequiredInteger(completion, "talking") != ExpectedDadTalkingAfterCue ||
            RequiredString(completion, "lookAt") != ExpectedLookTarget ||
            RequiredInteger(completion, "conditionalStageSource") !=
                ExpectedConditionalStageSource ||
            RequiredInteger(completion, "conditionalStageTarget") !=
                ExpectedConditionalStageTarget)
            throw new InvalidOperationException(
                "Fallout 3 CG01 Dad SayToDone completion differs.");

        var dialogue = RequiredObject(source, "dialogue");
        if (!RequiredBoolean(dialogue, "dialoguePlaybackPrepared") ||
            !RequiredBoolean(dialogue, "dialoguePlaybackImplemented"))
            throw new InvalidOperationException(
                "Fallout 3 CG01 stage-12 Dad response assets are not prepared.");
        var topic = RequiredObject(dialogue, "topic");
        var topicFormId = RequiredFormId(topic, "formId");
        var topicEditorId = RequiredString(topic, "editorId");
        if (topicFormId != RequiredFormId(source, "topicFormId") ||
            topicEditorId != RequiredString(source, "topicEditorId") ||
            RequiredFormId(topic, "questFormId") != stage12.QuestFormId)
            throw new InvalidOperationException(
                "Fallout 3 CG01 stage-12 Dad response topic differs.");
        _ = RequiredSha256(topic, "recordSha256");

        var rows = RequiredArray(dialogue, "branches").EnumerateArray()
            .OrderBy(value => RequiredInteger(value, "sequence"))
            .ToArray();
        if (rows.Length != ExpectedCueCount)
            throw new InvalidOperationException(
                "Fallout 3 CG01 stage-12 Dad response coverage differs.");
        var cues = rows.Select((row, index) => LoadCue(row, index, stage0, stage12, targetStage))
            .ToArray();
        if (cues.Select(value => value.InfoFormId).Distinct(
                StringComparer.OrdinalIgnoreCase).Count() != cues.Length ||
            !Fo3Cg01Stage10Transition.SpeakerIdleEquals(
                cues[0].SpeakerIdle,
                cues[1].SpeakerIdle))
            throw new InvalidOperationException(
                "Fallout 3 CG01 stage-12 Dad response selection differs.");

        var stageResult = RequiredObject(source, "stageResult");
        _ = RequiredSha256(stageResult, "stageSourceSha256");
        var commands = RequiredArray(stageResult, "commands").EnumerateArray().ToArray();
        if (RequiredInteger(stageResult, "accountedCommandCount") !=
                ExpectedStageCommandCount ||
            commands.Length != ExpectedStageCommandCount ||
            RequiredInteger(commands[0], "index") != 0 ||
            RequiredString(commands[0], "kind") != "evaluatePackage" ||
            RequiredFormId(commands[0], "referenceFormId") != stage0.Dad.FormId ||
            RequiredString(commands[0], "referenceEditorId") != stage0.Dad.EditorId)
            throw new InvalidOperationException(
                "Fallout 3 CG01 stage-14 package command differs.");
        var boundary = RequiredObject(source, "nextBoundary");
        if (RequiredBoolean(boundary, "applied") ||
            RequiredString(boundary, "blocker") != ExpectedBoundaryBlocker)
            throw new InvalidOperationException(
                "Fallout 3 CG01 stage-14 boundary differs.");
        return new Fo3Cg01Stage12DadResponse(
            sourceStage,
            targetStage,
            topicFormId,
            topicEditorId,
            stage0.Dad.FormId,
            cues,
            ExpectedBoundaryBlocker);
    }

    internal Fo3Cg01Stage14State Apply(Fo3Cg01Stage12State stage12)
    {
        if (stage12.ActiveStage != SourceStage ||
            stage12.DadDoTalk != 1 ||
            stage12.DadTimerSeconds != 0.0 ||
            stage12.NextBoundary.Applied)
            throw new InvalidOperationException(
                "Fallout 3 CG01 stage-12 Dad response source state differs.");
        var packageEvaluated = false;
        var commands = new[]
        {
            new SourceGamebryoStageCommand<string>(
                FirstSourceCommandIndex,
                GamebryoStageCommandKind.ActorIntent,
                DadReferenceFormId),
        };
        var applied = 0;
        GamebryoStageCommandExecutor.ExecuteAll(commands, command =>
        {
            if (command.Value != DadReferenceFormId)
                return false;
            packageEvaluated = true;
            applied++;
            return packageEvaluated;
        });
        return new Fo3Cg01Stage14State(
            SourceStage,
            stage12.ActiveQuestFormId,
            stage12.ActiveQuestEditorId,
            TargetStage,
            Cues.Select(value => value.InfoFormId).ToArray(),
            ExpectedDadTalkingAfterCue,
            true,
            packageEvaluated,
            commands.Length,
            applied,
            new Fo3Cg01Stage12Boundary(false, NextBoundaryBlocker));
    }

    internal object SavedState(Fo3Cg01Stage14State state) => new
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
        dadTalking = state.DadTalking,
        dadLooksAtPlayer = state.DadLooksAtPlayer,
        dadPackageEvaluated = state.DadPackageEvaluated,
        accountedCommandCount = state.AccountedCommandCount,
        appliedCommandCount = state.AppliedCommandCount,
        nextBoundary = new
        {
            applied = state.NextBoundary.Applied,
            blocker = state.NextBoundary.Blocker,
        },
    };

    internal void ValidateSavedState(JsonElement source, Fo3Cg01Stage14State expected)
    {
        var activeQuest = RequiredObject(source, "activeQuest");
        var boundary = RequiredObject(source, "nextBoundary");
        if (RequiredString(source, "schema") != ExpectedSavedStateSchema ||
            RequiredInteger(source, "sourceStage") != expected.SourceStage ||
            RequiredFormId(activeQuest, "formId") != expected.ActiveQuestFormId ||
            RequiredString(activeQuest, "editorId") != expected.ActiveQuestEditorId ||
            RequiredInteger(activeQuest, "stage") != expected.ActiveStage ||
            !RequiredArray(source, "appliedInfoFormIds").EnumerateArray()
                .Select(value => value.GetString()).SequenceEqual(expected.AppliedInfoFormIds) ||
            RequiredInteger(source, "dadTalking") != expected.DadTalking ||
            RequiredBoolean(source, "dadLooksAtPlayer") != expected.DadLooksAtPlayer ||
            RequiredBoolean(source, "dadPackageEvaluated") != expected.DadPackageEvaluated ||
            RequiredInteger(source, "accountedCommandCount") != expected.AccountedCommandCount ||
            RequiredInteger(source, "appliedCommandCount") != expected.AppliedCommandCount ||
            RequiredBoolean(boundary, "applied") != expected.NextBoundary.Applied ||
            RequiredString(boundary, "blocker") != expected.NextBoundary.Blocker)
            throw new InvalidOperationException(
                "Saved Fallout 3 CG01 stage-14 Dad response differs.");
    }

    private static Fo3Cg01Stage12DadResponseCue LoadCue(
        JsonElement source,
        int expectedSequence,
        Fo3Cg01Stage0Transition stage0,
        Fo3Cg01Stage12Transition stage12,
        int targetStage)
    {
        var sequence = RequiredInteger(source, "sequence");
        var infoFormId = RequiredFormId(source, "infoFormId");
        if (sequence != expectedSequence || !RequiredBoolean(source, "sayOnce"))
            throw new InvalidOperationException(
                "Fallout 3 CG01 stage-12 Dad response order differs.");
        _ = RequiredSha256(source, "recordSha256");
        _ = RequiredSha256(source, "resultSourceSha256");
        var conditions = RequiredArray(source, "conditions").EnumerateArray().ToArray();
        if (conditions.Length != ExpectedCueCount ||
            !conditions.Any(value =>
                RequiredInteger(value, "function") == GetStageConditionFunction &&
                RequiredDouble(value, "comparisonValue") == stage12.TargetStage &&
                RequiredFormId(value, "parameter1") == stage12.QuestFormId) ||
            !conditions.Any(value =>
                RequiredInteger(value, "function") == GetIsIdConditionFunction &&
                RequiredDouble(value, "comparisonValue") == 1.0 &&
                RequiredFormId(value, "parameter1") == stage0.Dad.BaseFormId))
            throw new InvalidOperationException(
                "Fallout 3 CG01 stage-12 Dad response conditions differ.");
        var effects = RequiredArray(source, "effects").EnumerateArray().ToArray();
        int? cueTargetStage = null;
        if (sequence == 0)
        {
            if (effects.Length != 0)
                throw new InvalidOperationException(
                    "Fallout 3 CG01 first stage-12 Dad response has effects.");
        }
        else
        {
            if (effects.Length != 1)
                throw new InvalidOperationException(
                    "Fallout 3 CG01 final Dad response effect differs.");
            var result = GamebryoDialoguePlayback.RequireStageResult(
                RequiredString(effects[0], "kind"),
                RequiredFormId(effects[0], "questFormId"),
                RequiredInteger(effects[0], "stage"));
            if (result.QuestFormId != stage12.QuestFormId || result.Stage != targetStage)
                throw new InvalidOperationException(
                    "Fallout 3 CG01 final Dad response effect differs.");
            cueTargetStage = result.Stage;
        }
        var response = RequiredObject(source, "response");
        var responseIndex = RequiredInteger(response, "index");
        var text = RequiredString(response, "text");
        var actualTextSha256 = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
        if (responseIndex != 1 || actualTextSha256 != RequiredSha256(response, "textSha256"))
            throw new InvalidOperationException(
                "Fallout 3 CG01 stage-12 Dad response text differs.");
        var suffix = $"_{infoFormId}_{responseIndex}";
        return new Fo3Cg01Stage12DadResponseCue(
            sequence,
            infoFormId,
            true,
            Fo3Cg01Stage10Transition.LoadSpeakerIdle(
                RequiredObject(source, "speakerIdle")),
            new Fo3OwnedDialogueResponse(
                responseIndex,
                text,
                Fo3Cg01Stage10Transition.LoadDialogueAsset(
                    RequiredObject(response, "voice"),
                    suffix + ".ogg"),
                Fo3Cg01Stage10Transition.LoadDialogueAsset(
                    RequiredObject(response, "lip"),
                    suffix + ".lip")),
            cueTargetStage);
    }

    private static JsonElement RequiredObject(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object
            ? value
            : throw new InvalidOperationException(
                $"Fallout 3 CG01 Dad response field {name} is absent.");

    private static JsonElement RequiredArray(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? value
            : throw new InvalidOperationException(
                $"Fallout 3 CG01 Dad response field {name} is absent.");

    private static string RequiredString(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!
            : throw new InvalidOperationException(
                $"Fallout 3 CG01 Dad response field {name} is absent.");

    private static int RequiredInteger(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.TryGetInt32(out var result)
            ? result
            : throw new InvalidOperationException(
                $"Fallout 3 CG01 Dad response field {name} is invalid.");

    private static double RequiredDouble(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.TryGetDouble(out var result) &&
        double.IsFinite(result)
            ? result
            : throw new InvalidOperationException(
                $"Fallout 3 CG01 Dad response field {name} is invalid.");

    private static bool RequiredBoolean(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) &&
        value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : throw new InvalidOperationException(
                $"Fallout 3 CG01 Dad response field {name} is invalid.");

    private static string RequiredFormId(JsonElement parent, string name)
    {
        var value = RequiredString(parent, name);
        if (value.Length != Fo3OpeningFlowNumericContracts.FormIdHexCharacters ||
            value.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidOperationException(
                $"Fallout 3 CG01 Dad response FormID {name} is invalid.");
        return value;
    }

    private static string RequiredSha256(JsonElement parent, string name)
    {
        var value = RequiredString(parent, name);
        if (value.Length != Fo3OpeningFlowNumericContracts.Sha256HexCharacters ||
            value.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidOperationException(
                $"Fallout 3 CG01 Dad response hash {name} is invalid.");
        return value;
    }
}
