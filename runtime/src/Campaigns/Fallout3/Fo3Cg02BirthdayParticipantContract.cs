using System.Text.Json;

namespace OpenNV.Runtime.Campaigns.Fallout3;

internal static class Fo3Cg02BirthdayParticipantContract
{
    internal static Fo3Cg02BirthdayParticipant Load(JsonElement participant)
    {
        var dialogue = participant.GetProperty("dialogue");
        if (!dialogue.GetProperty("dialoguePlaybackPrepared").GetBoolean() ||
            !dialogue.GetProperty("dialoguePlaybackImplemented").GetBoolean())
            throw new InvalidOperationException(
                "Fallout 3 CG02 birthday dialogue assets differ.");
        var lines = dialogue.GetProperty("branches").EnumerateArray().ToDictionary(
            row => $"{row.GetProperty("infoFormId").GetString()!}:" +
                row.GetProperty("response").GetProperty("index").GetInt32(),
            row =>
            {
                var response = row.GetProperty("response");
                var info = row.GetProperty("infoFormId").GetString()!;
                var index = response.GetProperty("index").GetInt32();
                return new Fo3OwnedDialogueResponse(
                    index, response.GetProperty("text").GetString()!,
                    Fo3Cg01Stage10Transition.LoadDialogueAsset(
                        response.GetProperty("voice"), $"_{info}_{index}.ogg"),
                    Fo3Cg01Stage10Transition.LoadDialogueAsset(
                        response.GetProperty("lip"), $"_{info}_{index}.lip"));
            }, StringComparer.OrdinalIgnoreCase);
        var nodes = dialogue.GetProperty("nodes").EnumerateArray().Select(row =>
            new Fo3Cg02BirthdayDialogueNode(
                row.GetProperty("infoFormId").GetString()!,
                row.GetProperty("topicFormId").GetString()!,
                row.GetProperty("engineSex").ValueKind == JsonValueKind.Null
                    ? null : row.GetProperty("engineSex").GetString(),
                row.GetProperty("responseIndexes").EnumerateArray()
                    .Select(value => value.GetInt32()).ToArray(),
                row.GetProperty("linkedTopicFormIds").EnumerateArray()
                    .Select(value => value.GetString()!).ToArray(),
                row.GetProperty("conditions").EnumerateArray().Select(condition =>
                    new Fo3Cg02DialogueCondition(
                        condition.GetProperty("operatorFlags").GetInt32(),
                        condition.GetProperty("comparisonValue").GetDouble(),
                        condition.GetProperty("function").GetInt32(),
                        condition.GetProperty("parameter1").GetInt32(),
                        condition.GetProperty("parameter2").GetInt32(),
                        condition.GetProperty("runOn").GetInt32())).ToArray(),
                row.GetProperty("effects").EnumerateArray().Select(effect =>
                    new Fo3Cg02BirthdayEffect(
                        effect.GetProperty("kind").GetString()!,
                        effect.TryGetProperty("stage", out var stage)
                            ? stage.GetInt32() : 0,
                        0.0, "", 0,
                        effect.TryGetProperty("referenceFormId", out var reference)
                            ? reference.GetString()! : "",
                        "", 0, "", "")).ToArray()))
            .ToDictionary(value => value.InfoFormId,
                StringComparer.OrdinalIgnoreCase);
        var topics = dialogue.GetProperty("topics").EnumerateArray().Select(row =>
            new Fo3Cg02BirthdayTopic(
                row.GetProperty("formId").GetString()!,
                row.GetProperty("text").GetString()!)).ToDictionary(
                    value => value.FormId, StringComparer.OrdinalIgnoreCase);
        return new Fo3Cg02BirthdayParticipant(
            participant.GetProperty("referenceFormId").GetString()!,
            participant.GetProperty("baseFormId").GetString()!,
            participant.GetProperty("displayName").GetString()!,
            participant.TryGetProperty("actorScene", out var scene)
                ? scene.GetProperty("scene").GetString() : null,
            participant.TryGetProperty("actorScene", out scene)
                ? scene.GetProperty("sha256").GetString() : null,
            participant.GetProperty("greetingInfoFormIds").EnumerateArray()
                .Select(value => value.GetString()!).ToArray(),
            lines, nodes, topics);
    }
}
