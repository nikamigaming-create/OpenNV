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
    int CombatStage,
    int DeathStage,
    int CompletionStage,
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
    Fo3Cg02Combatant Combatant,
    IReadOnlyDictionary<int, IReadOnlyList<Fo3Cg02ReactorGiftCommand>> StageResults,
    string NextBoundaryBlocker);

internal sealed record Fo3Cg02Combatant(
    string ReferenceFormId,
    string PlayerReferenceFormId,
    string BaseFormId,
    string ScriptFormId,
    string PackageFormId,
    string PackageTargetFormId,
    int PackageRadiusGameUnits,
    int MaximumHealth,
    string WeaponFormId,
    string AmmunitionFormId,
    int WeaponDamage,
    int ClipSize,
    int DeathStage);

internal sealed record Fo3Cg02ButchRuntime(
    int SourceStage,
    int RequiredCakeStage,
    int SceneDoneStage,
    int AggregateStage,
    int IntercomStage,
    string ReferenceFormId,
    string BaseFormId,
    string SweetrollFormId,
    string FindPlayerPackageFormId,
    int FindPlayerRadiusGameUnits,
    int FindPlayerResultCommandCount,
    double AggregateTimerSeconds,
    IReadOnlyList<Fo3Cg02ButchStage35Command> Stage35Commands,
    Fo3Cg02PostIntercomRuntime? PostIntercomRuntime,
    string NextBoundaryBlocker);

internal sealed record Fo3Cg02CakeCue(
    int Sequence,
    string SpeakerBaseFormId,
    string InfoFormId,
    Fo3OwnedDialogueResponse Response,
    IReadOnlyList<string> Effects);

internal sealed record Fo3Cg02CakeRuntime(
    int SourceStage,
    int TriggerStage,
    int TargetStage,
    double FailsafeSeconds,
    string TriggerReferenceFormId,
    Fo3Cg01Transform TriggerTransform,
    Fo3Cg01Vector3 TriggerDimensionsGameUnits,
    string AndyReferenceFormId,
    string AndyBaseFormId,
    string AndyActorScenePath,
    string AndyActorSceneSha256,
    string PackageFormId,
    string PackageTargetMarkerFormId,
    Fo3Cg01Transform PackageTargetTransform,
    int PackageRadiusGameUnits,
    string PackageLocomotionLogicalPath,
    string PackageLocomotionSha256,
    float PackageLocomotionSpeedGameUnitsPerSecond,
    string PackageIdleLogicalPath,
    string CakeReferenceFormId,
    IReadOnlyList<Fo3Cg02CakeCue> Cues,
    int PackageResultCommandCount,
    int Stage15CommandCount,
    int Stage16CommandCount,
    string NextBoundaryBlocker);

internal sealed record Fo3Cg02IntroRuntime(
    int SourceStage,
    int TargetStage,
    double FailsafeSeconds,
    double InitialSeconds,
    IReadOnlyList<Fo3Cg02IntroParticipant> Participants,
    IReadOnlyList<Fo3Cg02IntroSound> Sounds,
    IReadOnlyList<Fo3Cg02Stage6Command> Stage6Commands,
    int FinalCommandCount,
    Fo3Cg02DadSpeechRuntime? DadSpeechRuntime,
    string NextBoundaryBlocker);

internal sealed record Fo3Cg02Stage0Transition(
    int SourceStage,
    int TargetStage,
    int Stage0CommandCount,
    int Stage5CommandCount,
    Fo3Cg01Transform PlayerMoveTransform,
    string PlayerMoveReferenceFormId,
    IReadOnlyList<int> DisabledPlayerControls,
    IReadOnlyDictionary<string, double> GameTime,
    bool PlayerYoung,
    int AgeRaceYears,
    IReadOnlyList<Fo3Cg02Stage5Item> Inventory,
    IReadOnlyList<Fo3Cg02Stage5Actor> Actors,
    double TimerInitialSeconds,
    int RunTimerValue,
    int IntroValue,
    Fo3Cg01OwnedMovie TransitionMovie,
    Fo3Cg02IntroRuntime? IntroRuntime,
    string NextBoundaryBlocker);
