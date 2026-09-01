using System.Text.Json;

namespace OpenNV.Runtime.Campaigns.Fallout3;

internal sealed record Fo3Cg03DadSpeechCue(
    string InfoFormId,
    string EngineSex,
    string SpeakerIdleFormId,
    string SpeakerIdleLogicalPath,
    string SpeakerIdleSourceSha256,
    Fo3OwnedDialogueResponse Response);

internal sealed record Fo3Cg03Stage5Runtime(
    string QuestFormId,
    string QuestEditorId,
    int SourceStage,
    int SpeechStage,
    int Stage5CommandCount,
    int Stage6CommandCount,
    string DadReferenceFormId,
    string DadBaseFormId,
    string DadActorScenePath,
    string DadActorSceneSha256,
    string DadHoldPackageFormId,
    string DadTalkPackageFormId,
    double TimerSeconds,
    Fo3Cg01OwnedMovie Movie,
    string RadioReferenceFormId,
    string Cg02HiddenPlaneReferenceFormId,
    string Cg03HiddenPlaneReferenceFormId,
    string VaultSuitFormId,
    string PipBoyFormId,
    IReadOnlyList<Fo3Cg03DadSpeechCue> Cues,
    string NextBoundaryBlocker);

internal static class Fo3Cg03Stage5Contract
{
    internal static Fo3Cg03Stage5Runtime Load(JsonElement source)
    {
        if (source.GetProperty("schema").GetString() !=
                "opennv-fo3-cg03-stage-5-dad-speech-runtime/v1")
            throw new InvalidOperationException(
                "Fallout 3 CG03 stage-5 runtime identity differs.");
        var dialogue = source.GetProperty("dialogue");
        if (!dialogue.GetProperty("dialoguePlaybackPrepared").GetBoolean() ||
            !dialogue.GetProperty("dialoguePlaybackImplemented").GetBoolean())
            throw new InvalidOperationException(
                "Fallout 3 CG03 Dad dialogue assets are absent.");
        var cues = dialogue.GetProperty("branches").EnumerateArray().Select(row =>
        {
            var idle = row.GetProperty("speakerIdle");
            var response = row.GetProperty("response");
            return new Fo3Cg03DadSpeechCue(
                row.GetProperty("infoFormId").GetString()!,
                row.GetProperty("engineSex").GetString()!,
                idle.GetProperty("formId").GetString()!,
                idle.GetProperty("modelPath").GetString()!,
                idle.GetProperty("sourceSha256").GetString()!,
                new Fo3OwnedDialogueResponse(
                    response.GetProperty("index").GetInt32(),
                    response.GetProperty("text").GetString()!,
                    Fo3Cg01Stage10Transition.LoadDialogueAsset(
                        response.GetProperty("voice"),
                        $"_{row.GetProperty("infoFormId").GetString()}_" +
                        $"{response.GetProperty("index").GetInt32()}.ogg"),
                    Fo3Cg01Stage10Transition.LoadDialogueAsset(
                        response.GetProperty("lip"),
                        $"_{row.GetProperty("infoFormId").GetString()}_" +
                        $"{response.GetProperty("index").GetInt32()}.lip")));
        }).ToArray();
        if (cues.Select(value => value.EngineSex).ToHashSet(
                StringComparer.OrdinalIgnoreCase).Count != cues.Length)
            throw new InvalidOperationException(
                "Fallout 3 CG03 Dad sex dialogue coverage differs.");
        var actor = source.GetProperty("dadActorScene");
        var movie = source.GetProperty("movie");
        return new Fo3Cg03Stage5Runtime(
            source.GetProperty("questFormId").GetString()!,
            source.GetProperty("questEditorId").GetString()!,
            source.GetProperty("sourceStage").GetInt32(),
            source.GetProperty("speechStage").GetInt32(),
            source.GetProperty("stage5CommandCount").GetInt32(),
            source.GetProperty("stage6CommandCount").GetInt32(),
            source.GetProperty("dadReferenceFormId").GetString()!,
            source.GetProperty("dadBaseFormId").GetString()!,
            actor.GetProperty("scene").GetString()!,
            actor.GetProperty("sha256").GetString()!,
            source.GetProperty("dadHoldPackageFormId").GetString()!,
            source.GetProperty("dadTalkPackageFormId").GetString()!,
            source.GetProperty("timerSeconds").GetDouble(),
            Fo3Cg01Stage0Transition.LoadOwnedMovie(
                movie.GetProperty("video"),
                movie.GetProperty("logicalPath").GetString()!,
                movie.GetProperty("arguments").EnumerateArray()
                    .Select(value => value.GetInt32()).ToArray()),
            source.GetProperty("radioReferenceFormId").GetString()!,
            source.GetProperty("cg02HiddenPlaneReferenceFormId").GetString()!,
            source.GetProperty("cg03HiddenPlaneReferenceFormId").GetString()!,
            source.GetProperty("vaultSuitFormId").GetString()!,
            source.GetProperty("pipBoyFormId").GetString()!,
            cues,
            source.GetProperty("nextBoundary").GetProperty("blocker").GetString()!);
    }
}
