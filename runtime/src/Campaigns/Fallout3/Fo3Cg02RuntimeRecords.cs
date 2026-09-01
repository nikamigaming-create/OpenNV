using OpenNV.Runtime.Presentation.Ui;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Campaigns.Fallout3;

internal sealed record Fo3Cg02Stage5Actor(
    string ReferenceFormId,
    string EditorId,
    bool Enabled,
    bool LooksAtPlayer,
    bool IgnoresCrime);

internal sealed record Fo3Cg02Stage5Item(
    string FormId,
    string EditorId,
    int AddedCount,
    bool Equipped);

internal sealed record Fo3Cg02IntroParticipant(
    int Phase,
    int SequenceInPhase,
    int Sequence,
    string? EngineSex,
    string ReferenceFormId,
    string ReferenceEditorId,
    string BaseFormId,
    string ActorScenePath,
    string ActorSceneSha256,
    string InfoFormId,
    string? SpeakerIdleFormId,
    string? SpeakerIdleLogicalPath,
    Fo3OwnedDialogueResponse Response,
    IReadOnlyDictionary<string, int> QuestVariableEffects,
    int ResultEffectCount);

internal sealed record Fo3Cg02IntroSound(
    int Phase,
    int Sequence,
    string FormId,
    string EditorId,
    string LogicalPath,
    string SourcePath,
    string Sha256);

internal sealed record Fo3Cg02Stage6Command(
    int Index,
    string Kind,
    string ReferenceFormId,
    string ReferenceEditorId,
    string? Variable,
    int Value);

internal sealed record Fo3Cg02DadSpeechCue(
    int Sequence,
    string? EngineSex,
    string InfoFormId,
    string SpeakerIdleFormId,
    string SpeakerIdleLogicalPath,
    string SpeakerIdleSourceSha256,
    Fo3OwnedDialogueResponse Response,
    int? TargetStage);

internal sealed record Fo3Cg02DadStage7Command(
    int Index,
    string Kind,
    string ReferenceFormId,
    string Variable,
    double Value);

internal sealed record Fo3Cg02DadSpeechRuntime(
    int SourceStage,
    int TargetStage,
    double FailsafeSeconds,
    string DadReferenceFormId,
    IReadOnlyList<Fo3Cg02DadSpeechCue> Cues,
    IReadOnlyList<Fo3Cg02DadStage7Command> Stage7Commands,
    int FinalCommandCount,
    Fo3Cg02OverseerSpeechRuntime? OverseerSpeechRuntime,
    string NextBoundaryBlocker);
internal sealed record Fo3Cg02OverseerCommand(
    int Index,
    string Kind,
    string ReferenceFormId,
    string TargetReferenceFormId,
    string Variable,
    double Value,
    string ItemFormId,
    int Count);

internal sealed record Fo3Cg02OverseerSpeechCue(
    int Sequence,
    string? EngineSex,
    string InfoFormId,
    string? SpeakerIdleLogicalPath,
    string? SpeakerIdleSourceSha256,
    Fo3OwnedDialogueResponse Response,
    IReadOnlyList<Fo3Cg02OverseerCommand> Effects);

internal sealed record Fo3Cg02OverseerSpeechRuntime(
    int SourceStage,
    int TargetStage,
    string OverseerReferenceFormId,
    string OverseerBaseFormId,
    string PlayerReferenceFormId,
    string ActorScenePath,
    string ActorSceneSha256,
    IReadOnlyList<Fo3Cg02OverseerSpeechCue> Cues,
    IReadOnlyDictionary<int, IReadOnlyList<Fo3Cg02OverseerCommand>> StageResults,
    Fo3Cg02DadPartyRuntime? DadPartyRuntime,
    string NextBoundaryBlocker);

internal sealed record Fo3Cg02DadPartyStageCommand(
    string Kind,
    string ReferenceFormId,
    int Value,
    IReadOnlyList<int> Arguments);

internal sealed record Fo3Cg02DadPartyRuntime(
    int SourceStage,
    int TargetStage,
    string DadReferenceFormId,
    string PackageFormId,
    int PackageRadiusGameUnits,
    int PackageResultCommandCount,
    double InitialDistanceGameUnits,
    bool ArrivedAtStart,
    Fo3Cg02DadSpeechCue Cue,
    IReadOnlyList<Fo3Cg02DadPartyStageCommand> StageCommands,
    Fo3Cg02BirthdayInteractionsRuntime? BirthdayInteractionsRuntime,
    string NextBoundaryBlocker);

internal sealed record Fo3Cg02BirthdayEffect(
    string Kind,
    int Stage,
    double Seconds,
    string Variable,
    int Value,
    string ReferenceFormId,
    string FormId,
    int Count,
    string Target,
    string Source);

internal sealed record Fo3Cg02BirthdayDialogueNode(
    string InfoFormId,
    string TopicFormId,
    string? EngineSex,
    IReadOnlyList<int> ResponseIndexes,
    IReadOnlyList<string> LinkedTopicFormIds,
    IReadOnlyList<Fo3Cg02DialogueCondition> Conditions,
    IReadOnlyList<Fo3Cg02BirthdayEffect> Effects);

internal sealed record Fo3Cg02DialogueCondition(
    int OperatorFlags,
    double ComparisonValue,
    int Function,
    int Parameter1,
    int Parameter2,
    int RunOn);

internal sealed record Fo3Cg02BirthdayTopic(
    string FormId,
    string Text);

internal sealed record Fo3Cg02BirthdayParticipant(
    string ReferenceFormId,
    string BaseFormId,
    string DisplayName,
    string? ActorScenePath,
    string? ActorSceneSha256,
    IReadOnlyList<string> GreetingInfoFormIds,
    IReadOnlyDictionary<string, Fo3OwnedDialogueResponse> Lines,
    IReadOnlyDictionary<string, Fo3Cg02BirthdayDialogueNode> Nodes,
    IReadOnlyDictionary<string, Fo3Cg02BirthdayTopic> Topics);

internal sealed record Fo3Cg02BirthdayStageResult(
    int Stage,
    string Kind,
    string FormId,
    int Count,
    int CommandCount,
    int? AggregateStage);

internal sealed record Fo3Cg02BirthdayInteractionsRuntime(
    int SourceStage,
    double FailsafeSeconds,
    IReadOnlyList<Fo3Cg02BirthdayParticipant> Participants,
    IReadOnlyDictionary<int, Fo3Cg02BirthdayStageResult> StageResults,
    int AggregateStage,
    Fo3Cg02CakeRuntime? CakeRuntime,
    Fo3Cg02ButchRuntime? ButchRuntime,
    string NextBoundaryBlocker);

internal sealed record Fo3Cg02ButchStage35Command(
    string Kind,
    string ReferenceFormId,
    string ActorReferenceFormId,
    string Variable,
    int Value);

internal sealed record Fo3Cg01Stage90Completion(
    int SourceStage,
    int TargetStage,
    double TimerInitialSeconds,
    int RunTimerValue,
    Fo3Stage90ImageSpaceModifier ImageSpaceModifier,
    Fo3Stage90Sound Sound,
    int Stage90CommandCount,
    int Stage100CommandCount,
    string DisabledDadReferenceFormId,
    double PlayerScale,
    bool PlayerToddler,
    string NextQuestFormId,
    string NextQuestEditorId,
    int NextQuestStage,
    Fo3Cg02Stage0Transition Cg02Stage0,
    string NextBoundaryBlocker);
