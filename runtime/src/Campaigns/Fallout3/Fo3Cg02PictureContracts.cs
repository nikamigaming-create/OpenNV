using System.Text.Json;

namespace OpenNV.Runtime.Campaigns.Fallout3;

internal sealed record Fo3Cg02PicturePackage(
    string FormId,
    string ActorReferenceFormId,
    string TargetMarkerFormId,
    Fo3Cg01Transform TargetTransform,
    int RadiusGameUnits,
    int CompletionCommandCount);

internal sealed record Fo3Cg02PictureTrigger(
    string ReferenceFormId,
    Fo3Cg01Transform SourceTransform,
    Fo3Cg01Vector3 DimensionsGameUnits);

internal sealed record Fo3Cg02PictureRuntime(
    int SourceStage,
    int PictureStage,
    int TimerStage,
    string DadInfoFormId,
    string JonasInfoFormId,
    IReadOnlyList<Fo3Cg02PicturePackage> Packages,
    IReadOnlyList<Fo3Cg02PictureTrigger> Triggers,
    int MinimumHeadingDegrees,
    int MaximumHeadingDegrees,
    int DadReadyValue,
    int JonasReadyValue,
    int PlayerReadyValue,
    int DadTalkValue,
    double DadTimerSeconds,
    int ObjectiveIndex,
    int PictureDadTalkValue,
    int SourceStageCommandCount,
    int PictureStageCommandCount,
    Fo3Cg02CompletionRuntime CompletionRuntime,
    string NextBoundaryBlocker);

internal sealed record Fo3Cg02CompletionRuntime(
    int TimerStage,
    int FlashStage,
    int CompletionStage,
    double Stage95TimerSeconds,
    double Stage98TimerSeconds,
    int Stage95CommandCount,
    int Stage98CommandCount,
    int Stage100CommandCount,
    string UnequipItemFormId,
    int UnequipPrevent,
    int UnequipSilent,
    Fo3Stage90ImageSpaceModifier ImageSpaceModifier,
    Fo3Stage90Sound Sound,
    string DadReferenceFormId,
    string BeatriceReferenceFormId,
    string SkillBookFormId,
    string NextDresserReferenceFormId,
    string AdultVaultSuitFormId,
    string NextQuestFormId,
    string NextQuestEditorId,
    int NextQuestEntryStage,
    int NextQuestTargetStage,
    int NextQuestStage0CommandCount,
    string NextQuestStartMarkerFormId,
    Fo3Cg01Transform NextQuestStartTransform,
    string NextBoundaryBlocker);

internal static class Fo3Cg02PictureContract
{
    internal static Fo3Cg02PictureRuntime Load(JsonElement source)
    {
        if (source.GetProperty("schema").GetString() !=
                "opennv-fo3-cg02-stage-80-picture-runtime/v1")
            throw new InvalidOperationException(
                "Fallout 3 CG02 picture runtime identity differs.");
        var packages = source.GetProperty("packages").EnumerateArray().Select(row =>
            new Fo3Cg02PicturePackage(
                row.GetProperty("formId").GetString()!,
                row.GetProperty("actorReferenceFormId").GetString()!,
                row.GetProperty("targetMarkerFormId").GetString()!,
                Fo3Cg01Stage12Transition.LoadTransform(
                    row.GetProperty("targetTransform")),
                row.GetProperty("radiusGameUnits").GetInt32(),
                row.GetProperty("completionCommandCount").GetInt32())).ToArray();
        var triggers = source.GetProperty("triggers").EnumerateArray().Select(row =>
            new Fo3Cg02PictureTrigger(
                row.GetProperty("referenceFormId").GetString()!,
                Fo3Cg01Stage12Transition.LoadTransform(
                    row.GetProperty("sourceTransform")),
                LoadVector3(row.GetProperty("dimensionsGameUnits")))).ToArray();
        if (packages.Length != 2 || triggers.Length == 0)
            throw new InvalidOperationException(
                "Fallout 3 CG02 picture source inventory differs.");
        return new Fo3Cg02PictureRuntime(
            source.GetProperty("sourceStage").GetInt32(),
            source.GetProperty("pictureStage").GetInt32(),
            source.GetProperty("timerStage").GetInt32(),
            source.GetProperty("dadInfoFormId").GetString()!,
            source.GetProperty("jonasInfoFormId").GetString()!,
            packages, triggers,
            source.GetProperty("minimumHeadingDegrees").GetInt32(),
            source.GetProperty("maximumHeadingDegrees").GetInt32(),
            source.GetProperty("dadReadyValue").GetInt32(),
            source.GetProperty("jonasReadyValue").GetInt32(),
            source.GetProperty("playerReadyValue").GetInt32(),
            source.GetProperty("dadTalkValue").GetInt32(),
            source.GetProperty("dadTimerSeconds").GetDouble(),
            source.GetProperty("objectiveIndex").GetInt32(),
            source.GetProperty("pictureDadTalkValue").GetInt32(),
            source.GetProperty("sourceStageCommandCount").GetInt32(),
            source.GetProperty("pictureStageCommandCount").GetInt32(),
            LoadCompletion(source.GetProperty("completionRuntime")),
            source.GetProperty("nextBoundary").GetProperty("blocker").GetString()!);
    }

    private static Fo3Cg02CompletionRuntime LoadCompletion(JsonElement source)
    {
        if (source.GetProperty("schema").GetString() !=
                "opennv-fo3-cg02-stage-95-completion-runtime/v1")
            throw new InvalidOperationException(
                "Fallout 3 CG02 completion runtime identity differs.");
        return new Fo3Cg02CompletionRuntime(
            source.GetProperty("timerStage").GetInt32(),
            source.GetProperty("flashStage").GetInt32(),
            source.GetProperty("completionStage").GetInt32(),
            source.GetProperty("stage95TimerSeconds").GetDouble(),
            source.GetProperty("stage98TimerSeconds").GetDouble(),
            source.GetProperty("stage95CommandCount").GetInt32(),
            source.GetProperty("stage98CommandCount").GetInt32(),
            source.GetProperty("stage100CommandCount").GetInt32(),
            source.GetProperty("unequipItemFormId").GetString()!,
            source.GetProperty("unequipPrevent").GetInt32(),
            source.GetProperty("unequipSilent").GetInt32(),
            Fo3Stage90Transition.LoadModifier(
                source.GetProperty("imageSpaceModifier")),
            Fo3Stage90Transition.LoadSound(source.GetProperty("sound")),
            source.GetProperty("dadReferenceFormId").GetString()!,
            source.GetProperty("beatriceReferenceFormId").GetString()!,
            source.GetProperty("skillBookFormId").GetString()!,
            source.GetProperty("nextDresserReferenceFormId").GetString()!,
            source.GetProperty("adultVaultSuitFormId").GetString()!,
            source.GetProperty("nextQuestFormId").GetString()!,
            source.GetProperty("nextQuestEditorId").GetString()!,
            source.GetProperty("nextQuestEntryStage").GetInt32(),
            source.GetProperty("nextQuestTargetStage").GetInt32(),
            source.GetProperty("nextQuestStage0CommandCount").GetInt32(),
            source.GetProperty("nextQuestStartMarkerFormId").GetString()!,
            Fo3Cg01Stage12Transition.LoadTransform(
                source.GetProperty("nextQuestStartTransform")),
            source.GetProperty("nextBoundary").GetProperty("blocker").GetString()!);
    }

    private static Fo3Cg01Vector3 LoadVector3(JsonElement source) => new(
        source.GetProperty("x").GetDouble(),
        source.GetProperty("y").GetDouble(),
        source.GetProperty("z").GetDouble());
}
