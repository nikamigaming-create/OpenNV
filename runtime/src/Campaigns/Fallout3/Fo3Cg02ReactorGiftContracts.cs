namespace OpenNV.Runtime.Campaigns.Fallout3;

internal sealed record Fo3Cg02PostIntercomPackage(
    string FormId,
    string TargetKind,
    string TargetFormId,
    Fo3Cg01Transform? TargetTransform,
    int RadiusGameUnits);

internal sealed record Fo3Cg02PostIntercomCue(
    string InfoFormId,
    string? EngineSex,
    string SpeakerBaseFormId,
    IReadOnlyList<Fo3OwnedDialogueResponse> Responses,
    int? TargetStage);

internal sealed record Fo3Cg02PostIntercomCommand(
    string Kind,
    string ReferenceFormId,
    string Variable,
    int Value,
    int ObjectiveIndex,
    string QuestFormId,
    int Stage);

internal sealed record Fo3Cg02PostIntercomRuntime(
    int SourceStage,
    int AnswerStage,
    int GoodbyeStage,
    int TargetStage,
    string DadReferenceFormId,
    string DadBaseFormId,
    string JonasReferenceFormId,
    string JonasBaseFormId,
    string JonasActorScenePath,
    string JonasActorSceneSha256,
    string IntercomReferenceFormId,
    Fo3Cg02PostIntercomPackage DadToIntercomPackage,
    Fo3Cg02PostIntercomPackage DadTalkToJonasPackage,
    Fo3Cg02PostIntercomPackage DadToPlayerPackage,
    IReadOnlyList<Fo3Cg02PostIntercomCue> Cues,
    IReadOnlyDictionary<int, IReadOnlyList<Fo3Cg02PostIntercomCommand>> StageResults,
    Fo3Cg02ReactorGiftRuntime? ReactorGiftRuntime,
    string NextBoundaryBlocker);

internal sealed record Fo3Cg02ReactorGiftCommand(
    string Kind,
    string ReferenceFormId,
    string ItemFormId,
    string TargetFormId,
    Fo3Cg01Transform? TargetTransform,
    int Count,
    int Value,
    int ObjectiveIndex,
    IReadOnlyList<int> Arguments,
    string QuestFormId,
    int Stage);

internal sealed record Fo3Cg02ReactorGiftRuntime(
    int SourceStage,
    int JonasStage,
    int TargetStage,
    int RangeStage,
    int HitStage,
    IReadOnlyList<Fo3Cg02BirthdayParticipant> Participants,
    string JonasGreetPackageFormId,
    string DadGreetPackageFormId,
    string DadToRangePackageFormId,
    string DadWaitPackageFormId,
    string JonasWaitPackageFormId,
    IReadOnlyList<string> TargetReferenceFormIds,
    string TargetAnimationGroup,
    int RequiredHitCount,
    int TutorialHitStage,
    string RequiredWeaponFormId,
    IReadOnlyDictionary<int, IReadOnlyList<Fo3Cg02ReactorGiftCommand>> StageResults,
    string NextBoundaryBlocker);
