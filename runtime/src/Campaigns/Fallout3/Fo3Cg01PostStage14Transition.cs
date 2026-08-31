using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Presentation.Ui;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Campaigns.Fallout3;

internal sealed record Fo3Cg01PostStage14Package(
    string FormId,
    string EditorId,
    string TargetFormId,
    Fo3Cg01Transform TargetTransform,
    int TargetRadiusGameUnits,
    int? CompletionStage);

internal sealed record Fo3Cg01PostStage14Cue(
    int Sequence,
    string InfoFormId,
    string? EngineSex,
    Fo3OwnedDialogueResponse Response);

internal sealed record Fo3Cg01Stage20State(
    int SourceStage,
    string ActiveQuestFormId,
    string ActiveQuestEditorId,
    int ActiveStage,
    IReadOnlyList<string> AppliedInfoFormIds,
    IReadOnlyList<string> AppliedPackageFormIds,
    string PlaypenGateReferenceFormId,
    bool PlaypenGateOpen,
    string PlayroomDoorReferenceFormId,
    bool PlayroomDoorOpen,
    int PlayroomDoorLockLevel,
    bool PlayerMovementEnabled,
    int DisplayedObjectiveIndex,
    int AccountedCommandCount,
    int AppliedCommandCount,
    IReadOnlyList<int> SpecialValues,
    bool SpecialBookAccepted,
    double TimerRemainingSeconds,
    bool TimerAdvancing,
    double ImageSpaceElapsedSeconds,
    bool Stage90SoundStarted,
    Fo3Cg01Stage12Boundary NextBoundary);

internal sealed record Fo3SpecialActorValue(
    int Index,
    string FormId,
    string EditorId,
    string Label,
    string Description,
    int InitialValue,
    int MinimumValue,
    int MaximumValue);

internal sealed record Fo3SpecialStageResult(
    int Stage,
    IReadOnlyList<SourceGamebryoStageCommand<string>> Commands);

internal sealed record Fo3Cg01DadReturnCue(
    string InfoFormId,
    Fo3OwnedDialogueResponse Response,
    string? TargetQuestFormId,
    int? TargetStage);

internal sealed record Fo3Cg01DadTravelPackage(
    Fo3Cg01PostStage14Package Package,
    IReadOnlyList<SourceGamebryoStageCommand<string>> StageCommands,
    int SourceStage,
    int? CompletionStage,
    IReadOnlyList<SourceGamebryoStageCommand<string>> CompletionCommands);

internal sealed record Fo3Cg01DadLeadTrigger(
    string ReferenceFormId,
    Fo3Cg01Transform SourceTransform,
    Fo3Cg01Vector3 DimensionsGameUnits,
    int SourceStage,
    int TargetStage);

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
    IReadOnlyList<int> Arguments);

internal sealed record Fo3Cg02ReactorGiftRuntime(
    int SourceStage,
    int JonasStage,
    int TargetStage,
    IReadOnlyList<Fo3Cg02BirthdayParticipant> Participants,
    string JonasGreetPackageFormId,
    string DadGreetPackageFormId,
    IReadOnlyDictionary<int, IReadOnlyList<Fo3Cg02ReactorGiftCommand>> StageResults,
    string NextBoundaryBlocker);

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

internal sealed record Fo3Cg01DadLeadSequence(
    Fo3Cg01DadTravelPackage BibleTravel,
    Fo3Cg01DadTravelPackage LeadTravel,
    int SayToDoneStage,
    IReadOnlyList<SourceGamebryoStageCommand<string>> SayToDoneCommands,
    string UnlockedDoorReferenceFormId,
    int DisplayedObjectiveIndex,
    string EscortTargetFormId,
    CellNavigationGraph Navigation,
    string LocomotionLogicalPath,
    string LocomotionSha256,
    float LocomotionSpeedGameUnitsPerSecond,
    Fo3Cg01DadLeadTrigger EndTrigger,
    Fo3Cg01Stage90Completion Completion,
    string NextBoundaryBlocker);

internal sealed record Fo3Cg01Stage50Timer(
    int SourceStage,
    int TargetStage,
    double InitialSeconds,
    IReadOnlyList<SourceGamebryoStageCommand<string>> TargetCommands,
    Fo3Cg01PostStage14Package DadReturnPackage,
    int CompletionStage,
    IReadOnlyList<SourceGamebryoStageCommand<string>> CompletionCommands,
    double DialogueDelaySeconds,
    IReadOnlyList<Fo3Cg01DadReturnCue> DialogueCues,
    int DialogueTargetStage,
    Fo3Cg01DadLeadSequence DadLead,
    string MainDoorReferenceFormId,
    int MainDoorLockLevel,
    bool MainDoorOpen,
    string NextBoundaryBlocker)
{
    private const int ExpectedCg02IntroParticipantCount = 15;
    private const int ExpectedCg02IntroEffectCarrierCount = 5;
    private const int ExpectedCg02OverseerCueCount = 5;

    internal static Fo3Cg01Stage50Timer Load(JsonElement source, int expectedSourceStage)
    {
        if (source.GetProperty("schema").GetString() !=
                "opennv-fo3-cg01-stage-50-timer-runtime/v1" ||
            source.GetProperty("sourceStage").GetInt32() != expectedSourceStage ||
            source.GetProperty("decrementSource").GetString() != "GetSecondsPassed")
            throw new InvalidOperationException("Fallout 3 CG01 stage-50 timer identity differs.");
        var targetStage = source.GetProperty("targetStage").GetInt32();
        var timer = source.GetProperty("timerVariable");
        var run = source.GetProperty("runVariable");
        if (timer.GetProperty("name").GetString() != "timer" ||
            run.GetProperty("name").GetString() != "runTimer" ||
            run.GetProperty("requiredValue").GetInt32() != 1 || targetStage <= expectedSourceStage)
            throw new InvalidOperationException("Fallout 3 CG01 stage-50 timer variables differ.");
        var commands = source.GetProperty("targetResult").GetProperty("commands")
            .EnumerateArray().Select((row, index) =>
            {
                if (row.GetProperty("index").GetInt32() != index)
                    throw new InvalidOperationException("Fallout 3 CG01 stage-70 command order differs.");
                var kind = row.GetProperty("kind").GetString() switch
                {
                    "setQuestVariable" => GamebryoStageCommandKind.SetQuestVariable,
                    "evaluatePackage" => GamebryoStageCommandKind.ActorIntent,
                    _ => throw new InvalidOperationException("Fallout 3 CG01 stage-70 command differs."),
                };
                return new SourceGamebryoStageCommand<string>(index, kind,
                    row.GetProperty("kind").GetString()!);
            }).ToArray();
        var dadReturn = source.GetProperty("dadReturn");
        var package = dadReturn.GetProperty("package");
        var target = package.GetProperty("target");
        var transform = Fo3Cg01Stage12Transition.LoadTransform(target.GetProperty("sourceTransform"));
        var completionStage = package.GetProperty("completionStage").GetInt32();
        var completionCommands = ReadCommands(dadReturn.GetProperty("completionResult"),
            new Dictionary<string, GamebryoStageCommandKind>(StringComparer.Ordinal)
            {
                ["setScriptVariable"] = GamebryoStageCommandKind.SetScriptVariable,
                ["unlock"] = GamebryoStageCommandKind.ActorIntent,
                ["setOpenState"] = GamebryoStageCommandKind.ActorIntent,
                ["lock"] = GamebryoStageCommandKind.ActorIntent,
            });
        var completionRows = dadReturn.GetProperty("completionResult").GetProperty("commands")
            .EnumerateArray().ToArray();
        var mainDoorRows = completionRows.Where(row =>
            row.TryGetProperty("referenceEditorId", out var editor) &&
            editor.GetString() == "CG01MainDoor").ToArray();
        var mainDoorLock = mainDoorRows.Single(row => row.GetProperty("kind").GetString() == "lock");
        var mainDoorOpen = mainDoorRows.Single(row => row.GetProperty("kind").GetString() == "setOpenState");
        var dialogue = dadReturn.GetProperty("dialogue");
        if (!dialogue.GetProperty("dialoguePlaybackPrepared").GetBoolean() ||
            !dialogue.GetProperty("dialoguePlaybackImplemented").GetBoolean())
            throw new InvalidOperationException("Fallout 3 CG01 Dad-return dialogue is not prepared.");
        var cues = dialogue.GetProperty("branches").EnumerateArray().Select((row, index) =>
        {
            if (row.GetProperty("sequence").GetInt32() != index)
                throw new InvalidOperationException("Fallout 3 CG01 Dad-return cue order differs.");
            var info = row.GetProperty("infoFormId").GetString()!;
            var response = row.GetProperty("response");
            return new Fo3Cg01DadReturnCue(info,
                new Fo3OwnedDialogueResponse(1, response.GetProperty("text").GetString()!,
                    Fo3Cg01Stage10Transition.LoadDialogueAsset(response.GetProperty("voice"), $"_{info}_1.ogg"),
                    Fo3Cg01Stage10Transition.LoadDialogueAsset(response.GetProperty("lip"), $"_{info}_1.lip")),
                row.GetProperty("targetQuestFormId").ValueKind == JsonValueKind.Null
                    ? null : row.GetProperty("targetQuestFormId").GetString(),
                row.GetProperty("targetStage").ValueKind == JsonValueKind.Null
                    ? null : row.GetProperty("targetStage").GetInt32());
        }).ToArray();
        var boundary = dadReturn.GetProperty("nextBoundary");
        if (!boundary.GetProperty("applied").GetBoolean() ||
            boundary.GetProperty("blocker").ValueKind != JsonValueKind.Null)
            throw new InvalidOperationException("Fallout 3 CG01 Dad lead boundary differs.");
        var bible = dadReturn.GetProperty("bibleTravel");
        var lead = dadReturn.GetProperty("dadLead");
        var biblePackage = LoadTravelPackage(
            bible,
            completionStage: bible.GetProperty("completionStage").GetInt32(),
            sourceStage: (int)bible.GetProperty("condition").GetProperty("comparisonValue").GetDouble());
        var leadStage = dadReturn.GetProperty("targetStage").GetInt32();
        var leadPackage = LoadTravelPackage(lead, completionStage: null, leadStage);
        var navigationSource = lead.GetProperty("navigation");
        var navigationCells = navigationSource.GetProperty("navmeshes").EnumerateArray()
            .Select(row => row.GetProperty("cellFormId").GetString()!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (navigationCells.Count != 1)
            throw new InvalidOperationException("Fallout 3 CG01 Dad lead navigation differs.");
        var locomotion = lead.GetProperty("locomotion");
        var rootMotion = locomotion.GetProperty("rootMotion");
        var speed = rootMotion.GetProperty("speedGameUnitsPerSecond").GetSingle();
        if (!float.IsFinite(speed) || speed <= 0.0f)
            throw new InvalidOperationException("Fallout 3 CG01 Dad lead locomotion differs.");
        var endTrigger = lead.GetProperty("endTrigger");
        var leadResultRows = lead.GetProperty("stageResult").GetProperty("commands")
            .EnumerateArray().ToArray();
        var unlockDoor = leadResultRows.Single(row =>
            row.GetProperty("kind").GetString() == "unlock");
        var sayToDoneRows = lead.GetProperty("sayToDoneResult").GetProperty("commands")
            .EnumerateArray().ToArray();
        var displayedObjective = sayToDoneRows.Single(row =>
            row.GetProperty("kind").GetString() == "setObjectiveDisplayed");
        var dimensions = endTrigger.GetProperty("dimensionsGameUnits").EnumerateArray()
            .Select(value => value.GetDouble()).ToArray();
        if (dimensions.Length != 3 || dimensions.Any(value => !double.IsFinite(value) || value <= 0.0))
            throw new InvalidOperationException("Fallout 3 CG01 end trigger dimensions differ.");
        var stage90Completion = LoadStage90Completion(
            lead.GetProperty("completion"),
            endTrigger.GetProperty("targetStage").GetInt32(),
            lead.GetProperty("nextBoundary"));
        var dadLead = new Fo3Cg01DadLeadSequence(
            biblePackage,
            leadPackage,
            lead.GetProperty("sayToDoneStage").GetInt32(),
            ReadCommands(lead.GetProperty("sayToDoneResult"), new Dictionary<string, GamebryoStageCommandKind>(StringComparer.Ordinal)
            {
                ["setObjectiveDisplayed"] = GamebryoStageCommandKind.Objective,
                ["evaluatePackage"] = GamebryoStageCommandKind.ActorIntent,
            }),
            unlockDoor.GetProperty("referenceFormId").GetString()!,
            displayedObjective.GetProperty("objectiveIndex").GetInt32(),
            lead.GetProperty("escortTarget").GetProperty("formId").GetString()!,
            CellNavigationGraph.Load(navigationSource, navigationCells),
            locomotion.GetProperty("logicalPath").GetString()!,
            locomotion.GetProperty("sha256").GetString()!,
            speed,
            new Fo3Cg01DadLeadTrigger(
                endTrigger.GetProperty("referenceFormId").GetString()!,
                Fo3Cg01Stage12Transition.LoadTransform(endTrigger.GetProperty("sourceTransform")),
                new Fo3Cg01Vector3(dimensions[0], dimensions[1], dimensions[2]),
                endTrigger.GetProperty("sourceStage").GetInt32(),
                endTrigger.GetProperty("targetStage").GetInt32()),
            stage90Completion,
            stage90Completion.NextBoundaryBlocker);
        return new Fo3Cg01Stage50Timer(expectedSourceStage, targetStage,
            timer.GetProperty("initialSeconds").GetDouble(), commands,
            new Fo3Cg01PostStage14Package(package.GetProperty("formId").GetString()!,
                package.GetProperty("editorId").GetString()!, target.GetProperty("formId").GetString()!,
                transform, target.GetProperty("radiusGameUnits").GetInt32(), completionStage),
            completionStage, completionCommands, dadReturn.GetProperty("dialogueDelaySeconds").GetDouble(),
            cues, dadReturn.GetProperty("targetStage").GetInt32(), dadLead,
            mainDoorLock.GetProperty("referenceFormId").GetString()!,
            mainDoorLock.GetProperty("value").GetInt32(),
            mainDoorOpen.GetProperty("value").GetInt32() != 0,
            dadLead.NextBoundaryBlocker);
    }

    private static Fo3Cg01Stage90Completion LoadStage90Completion(
        JsonElement source,
        int expectedSourceStage,
        JsonElement completedBoundary)
    {
        if (source.GetProperty("schema").GetString() !=
                "opennv-fo3-cg01-stage-90-to-cg02-runtime/v1" ||
            source.GetProperty("sourceStage").GetInt32() != expectedSourceStage ||
            !completedBoundary.GetProperty("applied").GetBoolean() ||
            completedBoundary.GetProperty("blocker").ValueKind != JsonValueKind.Null)
            throw new InvalidOperationException("Fallout 3 CG01 stage-90 completion differs.");
        var timer = source.GetProperty("timer");
        if (timer.GetProperty("decrementSource").GetString() != "GetSecondsPassed")
            throw new InvalidOperationException("Fallout 3 CG01 stage-90 timer source differs.");
        _ = timer.GetProperty("scriptFormId").GetString();
        _ = timer.GetProperty("scriptEditorId").GetString();
        _ = timer.GetProperty("scriptSourceSha256").GetString();
        var targetStage = timer.GetProperty("targetStage").GetInt32();
        if (targetStage <= expectedSourceStage)
            throw new InvalidOperationException("Fallout 3 CG01 stage-90 timer target differs.");

        var stage90 = source.GetProperty("stage90Result").GetProperty("commands")
            .EnumerateArray().ToArray();
        var stage90Kinds = new[]
        {
            "setQuestVariable", "setQuestVariable", "completeAllObjectives",
            "autoDisplayObjectives", "killQuestUpdates", "applyImageSpaceModifier",
            "playSound",
        };
        if (stage90.Length != stage90Kinds.Length || stage90.Where((row, index) =>
                row.GetProperty("index").GetInt32() != index ||
                row.GetProperty("kind").GetString() != stage90Kinds[index]).Any())
            throw new InvalidOperationException("Fallout 3 CG01 stage-90 command order differs.");
        var timerVariable = stage90[0];
        var runVariable = stage90[1];
        var initialSeconds = timerVariable.GetProperty("value").GetDouble();
        var runValue = runVariable.GetProperty("value").GetInt32();
        if (timerVariable.GetProperty("variable").GetString() != "timer" ||
            timerVariable.GetProperty("variableType").GetString() != "float" ||
            runVariable.GetProperty("variable").GetString() != "runTimer" ||
            runVariable.GetProperty("variableType").GetString() != "short" ||
            !double.IsFinite(initialSeconds) || initialSeconds <= 0.0 || runValue != 1 ||
            stage90[3].GetProperty("value").GetInt32() != 0)
            throw new InvalidOperationException("Fallout 3 CG01 stage-90 result differs.");
        var modifierSource = stage90.Single(row =>
            row.GetProperty("kind").GetString() == "applyImageSpaceModifier")
            .GetProperty("modifier");
        var soundSource = stage90.Single(row =>
            row.GetProperty("kind").GetString() == "playSound")
            .GetProperty("sound");
        var modifier = Fo3Stage90Transition.LoadModifier(
            modifierSource, modifierSource.GetProperty("editorId").GetString()!);
        var sound = Fo3Stage90Transition.LoadSound(
            soundSource, soundSource.GetProperty("editorId").GetString()!);

        var stage100 = source.GetProperty("stage100Result").GetProperty("commands")
            .EnumerateArray().ToArray();
        var stage100Kinds = new[]
        {
            "stopQuest", "disable", "setPlayerScale", "setPlayerToddler",
            "clearNoActivationSound", "setStage",
        };
        if (stage100.Length != stage100Kinds.Length || stage100.Where((row, index) =>
                row.GetProperty("index").GetInt32() != index ||
                row.GetProperty("kind").GetString() != stage100Kinds[index]).Any())
            throw new InvalidOperationException("Fallout 3 CG01 stage-100 command order differs.");
        var dadDisable = stage100.Single(row =>
            row.GetProperty("kind").GetString() == "disable");
        var scale = stage100.Single(row =>
            row.GetProperty("kind").GetString() == "setPlayerScale")
            .GetProperty("value").GetDouble();
        var toddler = stage100.Single(row =>
            row.GetProperty("kind").GetString() == "setPlayerToddler")
            .GetProperty("value").GetInt32();
        var nextStage = stage100.Single(row =>
            row.GetProperty("kind").GetString() == "setStage");
        var next = source.GetProperty("nextBoundary");
        if (!next.GetProperty("applied").GetBoolean() ||
            nextStage.GetProperty("questFormId").GetString() != next.GetProperty("questFormId").GetString() ||
            nextStage.GetProperty("questEditorId").GetString() != next.GetProperty("questEditorId").GetString() ||
            nextStage.GetProperty("stage").GetInt32() != next.GetProperty("stage").GetInt32() ||
            !double.IsFinite(scale) || scale <= 0.0 || toddler is not 0)
            throw new InvalidOperationException("Fallout 3 CG01 stage-100 completion differs.");
        var cg02 = LoadCg02Stage0(
            source.GetProperty("cg02Stage0"),
            next.GetProperty("questFormId").GetString()!,
            next.GetProperty("stage").GetInt32());
        return new Fo3Cg01Stage90Completion(
            expectedSourceStage,
            targetStage,
            initialSeconds,
            runValue,
            modifier,
            sound,
            stage90.Length,
            stage100.Length,
            dadDisable.GetProperty("referenceFormId").GetString()!,
            scale,
            false,
            next.GetProperty("questFormId").GetString()!,
            next.GetProperty("questEditorId").GetString()!,
            next.GetProperty("stage").GetInt32(),
            cg02,
            cg02.NextBoundaryBlocker);
    }

    private static Fo3Cg02Stage0Transition LoadCg02Stage0(
        JsonElement source,
        string expectedQuestFormId,
        int expectedSourceStage)
    {
        if (source.GetProperty("schema").GetString() !=
                "opennv-fo3-cg02-stage-0-to-5-runtime/v1" ||
            source.GetProperty("questFormId").GetString() != expectedQuestFormId ||
            source.GetProperty("sourceStage").GetInt32() != expectedSourceStage)
            throw new InvalidOperationException("Fallout 3 CG02 stage-0 identity differs.");
        var targetStage = source.GetProperty("targetStage").GetInt32();
        if (targetStage <= expectedSourceStage)
            throw new InvalidOperationException("Fallout 3 CG02 stage-5 target differs.");
        var commands = source.GetProperty("stage5Commands").EnumerateArray().ToArray();
        var supportedKinds = new HashSet<string>(StringComparer.Ordinal)
        {
            "setLocationSpecificLoadScreensOnly", "setInCharGen", "setGameTime",
            "disablePlayerControls", "setPlayerYoung", "ageRace", "removeAllItems",
            "addItem", "equipItem", "enable", "setQuestVariable", "playBink",
            "lookAt", "ignoreCrime",
        };
        if (commands.Length == 0 || commands.Where((row, index) =>
                row.GetProperty("index").GetInt32() != index).Any())
            throw new InvalidOperationException("Fallout 3 CG02 stage-5 command order differs.");
        if (commands.Any(row => !supportedKinds.Contains(
                row.GetProperty("kind").GetString()!)))
            throw new InvalidOperationException("Fallout 3 CG02 stage-5 command differs.");
        var controls = commands.Single(row =>
            row.GetProperty("kind").GetString() == "disablePlayerControls")
            .GetProperty("arguments").EnumerateArray().Select(value => value.GetInt32()).ToArray();
        var gameTime = commands.Where(row => row.GetProperty("kind").GetString() == "setGameTime")
            .ToDictionary(
                row => row.GetProperty("variable").GetString()!,
                row => row.GetProperty("value").GetDouble(),
                StringComparer.OrdinalIgnoreCase);
        if (!gameTime.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(
                ["gameyear", "gamemonth", "gameday", "gamehour"]))
            throw new InvalidOperationException("Fallout 3 CG02 stage-5 game time differs.");
        var itemRows = commands.Where(row => row.GetProperty("kind").GetString() is "addItem" or "equipItem")
            .GroupBy(row => row.GetProperty("itemFormId").GetString()!, StringComparer.OrdinalIgnoreCase)
            .Select(group => new Fo3Cg02Stage5Item(
                group.Key,
                group.First().GetProperty("itemEditorId").GetString()!,
                group.Where(row => row.GetProperty("kind").GetString() == "addItem")
                    .Sum(row => row.GetProperty("count").GetInt32()),
                group.Any(row => row.GetProperty("kind").GetString() == "equipItem")))
            .ToArray();
        var actors = commands.Where(row => row.GetProperty("kind").GetString() is "enable" or "lookAt" or "ignoreCrime")
            .GroupBy(row => row.GetProperty("referenceFormId").GetString()!, StringComparer.OrdinalIgnoreCase)
            .Select(group => new Fo3Cg02Stage5Actor(
                group.Key,
                group.First().GetProperty("subject").GetString()!,
                group.Any(row => row.GetProperty("kind").GetString() == "enable"),
                group.Any(row => row.GetProperty("kind").GetString() == "lookAt"),
                group.Any(row => row.GetProperty("kind").GetString() == "ignoreCrime")))
            .ToArray();
        var variables = commands.Where(row => row.GetProperty("kind").GetString() == "setQuestVariable")
            .ToDictionary(row => row.GetProperty("variable").GetString()!, StringComparer.OrdinalIgnoreCase);
        if (!variables.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(
                ["timer", "runTimer", "intro"]))
            throw new InvalidOperationException("Fallout 3 CG02 stage-5 variables differ.");
        var movieCommand = commands.Single(row => row.GetProperty("kind").GetString() == "playBink");
        var movie = Fo3Cg01Stage0Transition.LoadOwnedMovie(
            movieCommand.GetProperty("video"),
            movieCommand.GetProperty("logicalPath").GetString()!,
            movieCommand.GetProperty("arguments").EnumerateArray().Select(value => value.GetInt32()).ToArray());
        var move = source.GetProperty("playerMove");
        var boundary = source.GetProperty("nextBoundary");
        var stage0CommandCount = source.GetProperty("stage0CommandCount").GetInt32();
        if (stage0CommandCount <= 0 ||
            move.GetProperty("index").GetInt32() != stage0CommandCount - 1 ||
            move.GetProperty("kind").GetString() != "moveToReference")
            throw new InvalidOperationException("Fallout 3 CG02 stage-0 move boundary differs.");
        var boundaryApplied = boundary.GetProperty("applied").GetBoolean();
        var intro = boundaryApplied
            ? LoadCg02Intro(source.GetProperty("introRuntime"), targetStage)
            : null;
        var blocker = boundaryApplied
            ? intro!.NextBoundaryBlocker
            : boundary.GetProperty("blocker").GetString()!;
        return new Fo3Cg02Stage0Transition(
            expectedSourceStage,
            targetStage,
            stage0CommandCount,
            commands.Length,
            Fo3Cg01Stage12Transition.LoadTransform(move.GetProperty("sourceTransform")),
            move.GetProperty("referenceFormId").GetString()!,
            controls,
            gameTime,
            commands.Single(row => row.GetProperty("kind").GetString() == "setPlayerYoung")
                .GetProperty("value").GetInt32() != 0,
            commands.Single(row => row.GetProperty("kind").GetString() == "ageRace")
                .GetProperty("value").GetInt32(),
            itemRows,
            actors,
            variables["timer"].GetProperty("value").GetDouble(),
            variables["runTimer"].GetProperty("value").GetInt32(),
            variables["intro"].GetProperty("value").GetInt32(),
            movie,
            intro,
            blocker);
    }

    private static Fo3Cg02IntroRuntime LoadCg02Intro(JsonElement source, int sourceStage)
    {
        if (source.GetProperty("schema").GetString() !=
                "opennv-fo3-cg02-stage-5-intro-runtime/v1" ||
            source.GetProperty("sourceStage").GetInt32() != sourceStage ||
            !source.GetProperty("assetsPrepared").GetBoolean())
            throw new InvalidOperationException("Fallout 3 CG02 intro identity differs.");
        var timer = source.GetProperty("timer");
        if (timer.GetProperty("decrementSource").GetString() != "GetSecondsPassed" ||
            timer.GetProperty("initialVariable").GetString() != "timer" ||
            timer.GetProperty("runVariable").GetString() != "runTimer" ||
            timer.GetProperty("requiredIntro").GetInt32() != 1)
            throw new InvalidOperationException("Fallout 3 CG02 intro timer differs.");
        var participants = source.GetProperty("participants").EnumerateArray()
            .Select((row, index) =>
            {
                if (row.GetProperty("sequence").GetInt32() != index)
                    throw new InvalidOperationException("Fallout 3 CG02 intro order differs.");
                var actor = row.GetProperty("actorScene");
                var idle = row.GetProperty("speakerIdle");
                var response = row.GetProperty("response");
                var effectRows = row.GetProperty("effects").EnumerateArray().ToArray();
                var effects = effectRows.Where(value =>
                        value.GetProperty("kind").GetString() == "setQuestVariable")
                    .ToDictionary(
                    value => value.GetProperty("variable").GetString()!,
                    value => value.GetProperty("value").GetInt32(),
                    StringComparer.OrdinalIgnoreCase);
                var voice = response.GetProperty("voice");
                var lip = response.GetProperty("lip");
                return new Fo3Cg02IntroParticipant(
                    row.GetProperty("phase").GetInt32(),
                    row.GetProperty("sequenceInPhase").GetInt32(),
                    index,
                    row.TryGetProperty("engineSex", out var sex) &&
                        sex.ValueKind != JsonValueKind.Null ? sex.GetString() : null,
                    row.GetProperty("referenceFormId").GetString()!,
                    row.GetProperty("referenceEditorId").GetString()!,
                    row.GetProperty("baseFormId").GetString()!,
                    actor.GetProperty("scene").GetString()!,
                    actor.GetProperty("sha256").GetString()!,
                    row.GetProperty("infoFormId").GetString()!,
                    idle.ValueKind == JsonValueKind.Null
                        ? null : idle.GetProperty("formId").GetString()!,
                    idle.ValueKind == JsonValueKind.Null
                        ? null : idle.GetProperty("modelPath").GetString()!,
                    new Fo3OwnedDialogueResponse(
                        response.GetProperty("index").GetInt32(),
                        response.GetProperty("text").GetString()!,
                        new Fo3OwnedDialogueAsset(
                            voice.GetProperty("logicalPath").GetString()!,
                            voice.GetProperty("source").GetString()!,
                            voice.GetProperty("bytes").GetInt64(),
                            voice.GetProperty("sha256").GetString()!),
                        new Fo3OwnedDialogueAsset(
                            lip.GetProperty("logicalPath").GetString()!,
                            lip.GetProperty("source").GetString()!,
                            lip.GetProperty("bytes").GetInt64(),
                            lip.GetProperty("sha256").GetString()!)),
                    effects,
                    effectRows.Length);
            }).ToArray();
        if (participants.Length != ExpectedCg02IntroParticipantCount || participants.Count(value =>
                value.QuestVariableEffects.Count != 0) != ExpectedCg02IntroEffectCarrierCount ||
            participants.Where(value => value.EngineSex is null)
                .GroupBy(value => value.Phase).Any(group =>
                !group.OrderBy(value => value.SequenceInPhase)
                    .Select(value => value.SequenceInPhase)
                    .SequenceEqual(Enumerable.Range(0, group.Count()))))
            throw new InvalidOperationException("Fallout 3 CG02 intro participants differ.");
        var sounds = source.GetProperty("sounds").EnumerateArray()
            .Select((row, index) =>
            {
                var asset = row.GetProperty("asset");
                return new Fo3Cg02IntroSound(
                    row.GetProperty("phase").GetInt32(),
                    row.GetProperty("sequence").GetInt32(),
                    row.GetProperty("formId").GetString()!,
                    row.GetProperty("editorId").GetString()!,
                    asset.GetProperty("logicalPath").GetString()!,
                    asset.GetProperty("source").GetString()!,
                    asset.GetProperty("sha256").GetString()!);
            }).ToArray();
        if (sounds.Length != 3 || sounds.GroupBy(value => value.Phase).Any(group =>
                !group.OrderBy(value => value.Sequence).Select(value => value.Sequence)
                    .SequenceEqual(Enumerable.Range(0, group.Count()))))
            throw new InvalidOperationException("Fallout 3 CG02 intro sounds differ.");
        var finalCommands = source.GetProperty("finalCommands").EnumerateArray().ToArray();
        if (finalCommands.Length != 3 ||
            finalCommands[0].GetProperty("kind").GetString() != "setStage" ||
            finalCommands[0].GetProperty("stage").GetInt32() !=
                source.GetProperty("targetStage").GetInt32() ||
            finalCommands.Skip(1).Any(value =>
                value.GetProperty("kind").GetString() != "setQuestVariable"))
            throw new InvalidOperationException("Fallout 3 CG02 intro completion differs.");
        var stage6Commands = source.GetProperty("stage6Commands").EnumerateArray()
            .Select((row, index) =>
            {
                if (row.GetProperty("index").GetInt32() != index)
                    throw new InvalidOperationException("Fallout 3 CG02 stage-6 order differs.");
                var kind = row.GetProperty("kind").GetString()!;
                if (kind is not ("setActorVariable" or "setOpenState" or "lookAt"))
                    throw new InvalidOperationException("Fallout 3 CG02 stage-6 command differs.");
                return new Fo3Cg02Stage6Command(
                    index,
                    kind,
                    row.GetProperty("referenceFormId").GetString()!,
                    row.GetProperty("referenceEditorId").GetString()!,
                    row.TryGetProperty("variable", out var variable)
                        ? variable.GetString() : null,
                    row.TryGetProperty("value", out var value) ? value.GetInt32() : 0);
            }).ToArray();
        if (stage6Commands.Length == 0)
            throw new InvalidOperationException("Fallout 3 CG02 stage-6 result is absent.");
        var boundary = source.GetProperty("nextBoundary");
        var boundaryApplied = boundary.GetProperty("applied").GetBoolean();
        var dadSpeech = boundaryApplied
            ? LoadCg02DadSpeech(source.GetProperty("dadSpeechRuntime"),
                source.GetProperty("targetStage").GetInt32())
            : null;
        return new Fo3Cg02IntroRuntime(
            sourceStage,
            source.GetProperty("targetStage").GetInt32(),
            source.GetProperty("failsafeSeconds").GetDouble(),
            timer.GetProperty("initialSeconds").GetDouble(),
            participants,
            sounds,
            stage6Commands,
            finalCommands.Length + stage6Commands.Length,
            dadSpeech,
            boundaryApplied
                ? dadSpeech!.NextBoundaryBlocker
                : boundary.GetProperty("blocker").GetString()!);
    }

    private static Fo3Cg02DadSpeechRuntime LoadCg02DadSpeech(
        JsonElement source,
        int expectedSourceStage)
    {
        if (source.GetProperty("schema").GetString() !=
                "opennv-fo3-cg02-stage-6-dad-speech-runtime/v1" ||
            source.GetProperty("sourceStage").GetInt32() != expectedSourceStage)
            throw new InvalidOperationException("Fallout 3 CG02 Dad speech identity differs.");
        var scriptSha256 = source.GetProperty("dadScriptSourceSha256").GetString()!;
        if (scriptSha256.Length != Fo3OpeningFlowNumericContracts.Sha256HexCharacters ||
            !scriptSha256.All(Uri.IsHexDigit))
            throw new InvalidOperationException(
                "Fallout 3 CG02 Dad script hash differs.");
        var targetStage = source.GetProperty("targetStage").GetInt32();
        var dialogue = source.GetProperty("dialogue");
        if (!dialogue.GetProperty("dialoguePlaybackPrepared").GetBoolean() ||
            !dialogue.GetProperty("dialoguePlaybackImplemented").GetBoolean())
            throw new InvalidOperationException("Fallout 3 CG02 Dad speech assets differ.");
        var cues = dialogue.GetProperty("branches").EnumerateArray()
            .Select(row =>
            {
                var sequence = row.GetProperty("sequence").GetInt32();
                var sex = row.TryGetProperty("engineSex", out var rawSex) &&
                    rawSex.ValueKind != JsonValueKind.Null ? rawSex.GetString() : null;
                var response = row.GetProperty("response");
                var responseIndex = response.GetProperty("index").GetInt32();
                var infoFormId = row.GetProperty("infoFormId").GetString()!;
                var text = response.GetProperty("text").GetString()!;
                if (!row.GetProperty("sayOnce").GetBoolean() ||
                    Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))
                        .ToLowerInvariant() != response.GetProperty("textSha256").GetString())
                    throw new InvalidOperationException(
                        "Fallout 3 CG02 Dad speech response differs.");
                var idle = row.GetProperty("speakerIdle");
                var effects = row.GetProperty("effects").EnumerateArray().ToArray();
                int? effectStage = effects.Length == 0 ? null :
                    effects.Single().GetProperty("stage").GetInt32();
                return new Fo3Cg02DadSpeechCue(
                    sequence,
                    sex,
                    infoFormId,
                    idle.GetProperty("formId").GetString()!,
                    idle.GetProperty("modelPath").GetString()!,
                    idle.GetProperty("sourceSha256").GetString()!,
                    new Fo3OwnedDialogueResponse(
                        responseIndex,
                        text,
                        Fo3Cg01Stage10Transition.LoadDialogueAsset(
                            response.GetProperty("voice"),
                            $"_{infoFormId}_{responseIndex}.ogg"),
                        Fo3Cg01Stage10Transition.LoadDialogueAsset(
                            response.GetProperty("lip"),
                            $"_{infoFormId}_{responseIndex}.lip")),
                    effectStage);
            }).ToArray();
        if (cues.Length != 3 ||
            !cues.Select(value => value.Sequence).SequenceEqual([0, 0, 1]) ||
            !cues.Take(2).Select(value => value.EngineSex)
                .ToHashSet(StringComparer.Ordinal).SetEquals(["male", "female"]) ||
            cues[2].EngineSex is not null || cues[2].TargetStage != targetStage)
            throw new InvalidOperationException("Fallout 3 CG02 Dad speech order differs.");
        var commands = source.GetProperty("stageResult").GetProperty("commands")
            .EnumerateArray().Select((row, index) =>
            {
                if (row.GetProperty("index").GetInt32() != index)
                    throw new InvalidOperationException(
                        "Fallout 3 CG02 stage-7 command order differs.");
                return new Fo3Cg02DadStage7Command(
                    index,
                    row.GetProperty("kind").GetString()!,
                    row.GetProperty("referenceFormId").GetString()!,
                    row.TryGetProperty("variable", out var variable)
                        ? variable.GetString()! : "",
                    row.TryGetProperty("value", out var value)
                        ? value.GetDouble() : 0.0);
            }).ToArray();
        if (!commands.Select(value => value.Kind).SequenceEqual([
                "setActorVariable", "evaluatePackage",
                "setActorVariable", "setActorVariable"]))
            throw new InvalidOperationException("Fallout 3 CG02 stage-7 result differs.");
        var boundary = source.GetProperty("nextBoundary");
        var boundaryApplied = boundary.GetProperty("applied").GetBoolean();
        var overseer = boundaryApplied
            ? LoadCg02OverseerSpeech(
                source.GetProperty("overseerSpeechRuntime"), targetStage)
            : null;
        return new Fo3Cg02DadSpeechRuntime(
            expectedSourceStage,
            targetStage,
            source.GetProperty("failsafeSeconds").GetDouble(),
            source.GetProperty("dadReferenceFormId").GetString()!,
            cues,
            commands,
            commands.Length + 1,
            overseer,
            boundaryApplied
                ? overseer!.NextBoundaryBlocker
                : boundary.GetProperty("blocker").GetString()!);
    }

    private static Fo3Cg02OverseerSpeechRuntime LoadCg02OverseerSpeech(
        JsonElement source,
        int expectedSourceStage)
    {
        if (source.GetProperty("schema").GetString() !=
                "opennv-fo3-cg02-stage-7-overseer-speech-runtime/v1" ||
            source.GetProperty("sourceStage").GetInt32() != expectedSourceStage)
            throw new InvalidOperationException(
                "Fallout 3 CG02 Overseer speech identity differs.");
        var dialogue = source.GetProperty("dialogue");
        if (!dialogue.GetProperty("dialoguePlaybackPrepared").GetBoolean() ||
            !dialogue.GetProperty("dialoguePlaybackImplemented").GetBoolean())
            throw new InvalidOperationException(
                "Fallout 3 CG02 Overseer speech assets differ.");
        Fo3Cg02OverseerCommand LoadCommand(JsonElement row, int index)
        {
            if (row.GetProperty("index").GetInt32() != index)
                throw new InvalidOperationException(
                    "Fallout 3 CG02 Overseer command order differs.");
            return new Fo3Cg02OverseerCommand(
                index,
                row.GetProperty("kind").GetString()!,
                row.TryGetProperty("referenceFormId", out var reference)
                    ? reference.GetString()! : "",
                row.TryGetProperty("targetReferenceFormId", out var target)
                    ? target.GetString()! : "",
                row.TryGetProperty("variable", out var variable)
                    ? variable.GetString()! : "",
                row.TryGetProperty("value", out var value)
                    ? value.GetDouble() : 0.0,
                row.TryGetProperty("itemFormId", out var item)
                    ? item.GetString()! : "",
                row.TryGetProperty("count", out var count)
                    ? count.GetInt32() : 0);
        }
        var cues = dialogue.GetProperty("branches").EnumerateArray().Select(row =>
        {
            var response = row.GetProperty("response");
            var infoFormId = row.GetProperty("infoFormId").GetString()!;
            var responseIndex = response.GetProperty("index").GetInt32();
            var idle = row.GetProperty("speakerIdle");
            return new Fo3Cg02OverseerSpeechCue(
                row.GetProperty("sequence").GetInt32(),
                row.TryGetProperty("engineSex", out var sex) &&
                    sex.ValueKind != JsonValueKind.Null ? sex.GetString() : null,
                infoFormId,
                idle.ValueKind == JsonValueKind.Null ? null :
                    idle.GetProperty("modelPath").GetString()!,
                idle.ValueKind == JsonValueKind.Null ? null :
                    idle.GetProperty("sourceSha256").GetString()!,
                new Fo3OwnedDialogueResponse(
                    responseIndex,
                    response.GetProperty("text").GetString()!,
                    Fo3Cg01Stage10Transition.LoadDialogueAsset(
                        response.GetProperty("voice"),
                        $"_{infoFormId}_{responseIndex}.ogg"),
                    Fo3Cg01Stage10Transition.LoadDialogueAsset(
                        response.GetProperty("lip"),
                        $"_{infoFormId}_{responseIndex}.lip")),
                row.GetProperty("effects").EnumerateArray()
                    .Select(LoadCommand).ToArray());
        }).ToArray();
        if (cues.Length != ExpectedCg02OverseerCueCount ||
            !cues.Select(value => value.Sequence).SequenceEqual([0, 0, 1, 2, 3]))
            throw new InvalidOperationException(
                "Fallout 3 CG02 Overseer cue order differs.");
        var stageResults = source.GetProperty("stageResults").EnumerateObject()
            .ToDictionary(
                property => int.Parse(property.Name,
                    System.Globalization.CultureInfo.InvariantCulture),
                property => (IReadOnlyList<Fo3Cg02OverseerCommand>)property.Value
                    .GetProperty("commands").EnumerateArray()
                    .Select(LoadCommand).ToArray());
        var actor = source.GetProperty("actorScene");
        var boundary = source.GetProperty("nextBoundary");
        var boundaryApplied = boundary.GetProperty("applied").GetBoolean();
        var party = boundaryApplied
            ? LoadCg02DadParty(source.GetProperty("dadPartyRuntime"),
                source.GetProperty("targetStage").GetInt32())
            : null;
        return new Fo3Cg02OverseerSpeechRuntime(
            expectedSourceStage,
            source.GetProperty("targetStage").GetInt32(),
            source.GetProperty("overseerReferenceFormId").GetString()!,
            source.GetProperty("overseerBaseFormId").GetString()!,
            source.GetProperty("playerReferenceFormId").GetString()!,
            actor.GetProperty("scene").GetString()!,
            actor.GetProperty("sha256").GetString()!,
            cues,
            stageResults,
            party,
            boundaryApplied ? party!.NextBoundaryBlocker :
                boundary.GetProperty("blocker").GetString()!);
    }

    private static Fo3Cg02DadPartyRuntime LoadCg02DadParty(
        JsonElement source,
        int expectedSourceStage)
    {
        if (source.GetProperty("schema").GetString() !=
                "opennv-fo3-cg02-stage-10-dad-party-runtime/v1" ||
            source.GetProperty("sourceStage").GetInt32() != expectedSourceStage)
            throw new InvalidOperationException("Fallout 3 CG02 Dad party identity differs.");
        var dialogue = source.GetProperty("dialogue");
        if (!dialogue.GetProperty("dialoguePlaybackPrepared").GetBoolean())
            throw new InvalidOperationException("Fallout 3 CG02 Dad party assets differ.");
        var row = dialogue.GetProperty("branches").EnumerateArray().Single();
        var response = row.GetProperty("response");
        var idle = row.GetProperty("speakerIdle");
        var infoFormId = row.GetProperty("infoFormId").GetString()!;
        var cue = new Fo3Cg02DadSpeechCue(
            row.GetProperty("sequence").GetInt32(), null, infoFormId,
            idle.GetProperty("formId").GetString()!,
            idle.GetProperty("modelPath").GetString()!,
            idle.GetProperty("sourceSha256").GetString()!,
            new Fo3OwnedDialogueResponse(
                response.GetProperty("index").GetInt32(),
                response.GetProperty("text").GetString()!,
                Fo3Cg01Stage10Transition.LoadDialogueAsset(
                    response.GetProperty("voice"), $"_{infoFormId}_1.ogg"),
                Fo3Cg01Stage10Transition.LoadDialogueAsset(
                    response.GetProperty("lip"), $"_{infoFormId}_1.lip")),
            source.GetProperty("targetStage").GetInt32());
        var commands = source.GetProperty("stageResult").GetProperty("commands")
            .EnumerateArray().Select(row => new Fo3Cg02DadPartyStageCommand(
                row.GetProperty("kind").GetString()!,
                row.TryGetProperty("referenceFormId", out var reference)
                    ? reference.GetString()! : "",
                row.TryGetProperty("stage", out var stage) ? stage.GetInt32() :
                    row.TryGetProperty("objectiveIndex", out var objective)
                        ? objective.GetInt32() : 0,
                row.TryGetProperty("arguments", out var arguments)
                    ? arguments.EnumerateArray().Select(value => value.GetInt32()).ToArray()
                    : [])).ToArray();
        var package = source.GetProperty("package");
        var boundary = source.GetProperty("nextBoundary");
        var boundaryApplied = boundary.GetProperty("applied").GetBoolean();
        var birthday = boundaryApplied
            ? LoadCg02BirthdayInteractions(
                source.GetProperty("birthdayInteractionsRuntime"),
                source.GetProperty("targetStage").GetInt32())
            : null;
        return new Fo3Cg02DadPartyRuntime(
            expectedSourceStage, source.GetProperty("targetStage").GetInt32(),
            source.GetProperty("dadReferenceFormId").GetString()!,
            package.GetProperty("formId").GetString()!,
            package.GetProperty("radiusGameUnits").GetInt32(),
            package.GetProperty("resultCommands").GetArrayLength(),
            package.GetProperty("initialDistanceGameUnits").GetDouble(),
            package.GetProperty("arrivedAtStart").GetBoolean(), cue, commands,
            birthday,
            boundaryApplied ? birthday!.NextBoundaryBlocker :
                boundary.GetProperty("blocker").GetString()!);
    }

    private static Fo3Cg02BirthdayInteractionsRuntime LoadCg02BirthdayInteractions(
        JsonElement source,
        int expectedSourceStage)
    {
        if (source.GetProperty("schema").GetString() !=
                "opennv-fo3-cg02-stage-12-birthday-interactions-runtime/v1" ||
            source.GetProperty("sourceStage").GetInt32() != expectedSourceStage)
            throw new InvalidOperationException(
                "Fallout 3 CG02 birthday interaction identity differs.");
        var participants = source.GetProperty("participants").EnumerateArray()
            .Select(participant =>
            {
                var dialogue = participant.GetProperty("dialogue");
                if (!dialogue.GetProperty("dialoguePlaybackPrepared").GetBoolean() ||
                    !dialogue.GetProperty("dialoguePlaybackImplemented").GetBoolean())
                    throw new InvalidOperationException(
                        "Fallout 3 CG02 birthday dialogue assets differ.");
                var lines = dialogue.GetProperty("branches").EnumerateArray()
                    .ToDictionary(
                        row => $"{row.GetProperty("infoFormId").GetString()!}:" +
                            row.GetProperty("response").GetProperty("index").GetInt32(),
                        row =>
                        {
                            var response = row.GetProperty("response");
                            var infoFormId = row.GetProperty("infoFormId").GetString()!;
                            var index = response.GetProperty("index").GetInt32();
                            return new Fo3OwnedDialogueResponse(
                                index,
                                response.GetProperty("text").GetString()!,
                                Fo3Cg01Stage10Transition.LoadDialogueAsset(
                                    response.GetProperty("voice"),
                                    $"_{infoFormId}_{index}.ogg"),
                                Fo3Cg01Stage10Transition.LoadDialogueAsset(
                                    response.GetProperty("lip"),
                                    $"_{infoFormId}_{index}.lip"));
                        }, StringComparer.OrdinalIgnoreCase);
                var nodes = dialogue.GetProperty("nodes").EnumerateArray()
                    .Select(row => new Fo3Cg02BirthdayDialogueNode(
                        row.GetProperty("infoFormId").GetString()!,
                        row.GetProperty("topicFormId").GetString()!,
                        row.TryGetProperty("engineSex", out var sex) &&
                            sex.ValueKind != JsonValueKind.Null ? sex.GetString() : null,
                        row.GetProperty("responseIndexes").EnumerateArray()
                            .Select(value => value.GetInt32()).ToArray(),
                        row.GetProperty("linkedTopicFormIds").EnumerateArray()
                            .Select(value => value.GetString()!).ToArray(),
                        row.GetProperty("conditions").EnumerateArray()
                            .Select(condition => new Fo3Cg02DialogueCondition(
                                condition.GetProperty("operatorFlags").GetInt32(),
                                condition.GetProperty("comparisonValue").GetDouble(),
                                condition.GetProperty("function").GetInt32(),
                                condition.GetProperty("parameter1").GetInt32(),
                                condition.GetProperty("parameter2").GetInt32(),
                                condition.GetProperty("runOn").GetInt32()))
                            .ToArray(),
                        row.GetProperty("effects").EnumerateArray()
                            .Select(effect => new Fo3Cg02BirthdayEffect(
                                effect.GetProperty("kind").GetString()!,
                                effect.TryGetProperty("stage", out var stage)
                                    ? stage.GetInt32() : 0,
                                effect.TryGetProperty("seconds", out var seconds)
                                    ? seconds.GetDouble() : 0.0,
                                effect.TryGetProperty("variable", out var variable)
                                    ? variable.GetString()! : "",
                                effect.TryGetProperty("value", out var value)
                                    ? value.GetInt32() : 0,
                                effect.TryGetProperty("referenceFormId", out var reference)
                                    ? reference.GetString()! : "",
                                effect.TryGetProperty("formId", out var form)
                                    ? form.GetString()! : "",
                                effect.TryGetProperty("count", out var count)
                                    ? count.GetInt32() : 0,
                                effect.TryGetProperty("target", out var target)
                                    ? target.GetString()! : "",
                                effect.TryGetProperty("source", out var effectSource)
                                    ? effectSource.GetString()! : ""))
                            .ToArray()))
                    .ToDictionary(row => row.InfoFormId,
                        StringComparer.OrdinalIgnoreCase);
                var topics = dialogue.GetProperty("topics").EnumerateArray()
                    .Select(row => new Fo3Cg02BirthdayTopic(
                        row.GetProperty("formId").GetString()!,
                        row.GetProperty("text").GetString()!))
                    .ToDictionary(row => row.FormId,
                        StringComparer.OrdinalIgnoreCase);
                return new Fo3Cg02BirthdayParticipant(
                    participant.GetProperty("referenceFormId").GetString()!,
                    participant.GetProperty("baseFormId").GetString()!,
                    participant.GetProperty("displayName").GetString()!,
                    participant.TryGetProperty("actorScene", out var participantScene)
                        ? participantScene.GetProperty("scene").GetString() : null,
                    participant.TryGetProperty("actorScene", out participantScene)
                        ? participantScene.GetProperty("sha256").GetString() : null,
                    participant.GetProperty("greetingInfoFormIds").EnumerateArray()
                        .Select(value => value.GetString()!).ToArray(),
                    lines, nodes, topics);
            }).ToArray();
        var stageResults = source.GetProperty("stageResults").EnumerateObject()
            .ToDictionary(
                property => int.Parse(property.Name,
                    System.Globalization.CultureInfo.InvariantCulture),
                property => new Fo3Cg02BirthdayStageResult(
                    int.Parse(property.Name,
                        System.Globalization.CultureInfo.InvariantCulture),
                    property.Value.GetProperty("kind").GetString()!,
                    property.Value.GetProperty("formId").GetString()!,
                    property.Value.GetProperty("count").GetInt32(),
                    property.Value.GetProperty("commandCount").GetInt32(),
                    property.Value.TryGetProperty("aggregateStage", out var aggregate)
                        ? aggregate.GetInt32() : null));
        if (participants.Length == 0 || stageResults.Count == 0)
            throw new InvalidOperationException(
                "Fallout 3 CG02 birthday interaction graph is empty.");
        var boundary = source.GetProperty("nextBoundary");
        var boundaryApplied = boundary.GetProperty("applied").GetBoolean();
        var cake = boundaryApplied
            ? LoadCg02Cake(source.GetProperty("cakeRuntime"), expectedSourceStage)
            : null;
        var butch = source.TryGetProperty("butchRuntime", out var butchSource)
            ? LoadCg02Butch(butchSource)
            : null;
        return new Fo3Cg02BirthdayInteractionsRuntime(
            expectedSourceStage,
            source.GetProperty("failsafeTimer").GetProperty("seconds").GetDouble(),
            participants,
            stageResults,
            source.GetProperty("aggregateStage").GetInt32(),
            cake,
            butch,
            butch?.NextBoundaryBlocker ?? (boundaryApplied ? cake!.NextBoundaryBlocker :
                boundary.GetProperty("blocker").GetString()!));
    }

    private static Fo3Cg02ButchRuntime LoadCg02Butch(JsonElement source)
    {
        if (source.GetProperty("schema").GetString() !=
                "opennv-fo3-cg02-stage-20-butch-runtime/v1")
            throw new InvalidOperationException(
                "Fallout 3 CG02 Butch runtime identity differs.");
        var package = source.GetProperty("findPlayerPackage");
        if (package.GetProperty("target").GetString() != "player")
            throw new InvalidOperationException(
                "Fallout 3 CG02 Butch package target differs.");
        var stage35 = source.GetProperty("stage35").GetProperty("commands")
            .EnumerateArray().Select(row => new Fo3Cg02ButchStage35Command(
                row.GetProperty("kind").GetString()!,
                row.TryGetProperty("referenceFormId", out var reference)
                    ? reference.GetString()! : "",
                row.TryGetProperty("actorReferenceFormId", out var actor)
                    ? actor.GetString()! : "",
                row.TryGetProperty("variable", out var variable)
                    ? variable.GetString()! : "",
                row.TryGetProperty("value", out var value)
                    ? value.GetInt32() : 0)).ToArray();
        return new Fo3Cg02ButchRuntime(
            source.GetProperty("sourceStage").GetInt32(),
            source.GetProperty("requiredCakeStage").GetInt32(),
            source.GetProperty("sceneDoneStage").GetInt32(),
            source.GetProperty("aggregateStage").GetInt32(),
            source.GetProperty("intercomStage").GetInt32(),
            source.GetProperty("referenceFormId").GetString()!,
            source.GetProperty("baseFormId").GetString()!,
            source.GetProperty("sweetrollFormId").GetString()!,
            package.GetProperty("formId").GetString()!,
            package.GetProperty("radiusGameUnits").GetInt32(),
            package.GetProperty("resultCommands").GetArrayLength(),
            source.GetProperty("stage34").GetProperty("timerSeconds").GetDouble(),
            stage35,
            source.TryGetProperty("postIntercomRuntime", out var postIntercom)
                ? LoadCg02PostIntercom(postIntercom)
                : null,
            source.GetProperty("nextBoundary").GetProperty("blocker").GetString()!);
    }

    private static Fo3Cg02PostIntercomRuntime LoadCg02PostIntercom(JsonElement source)
    {
        if (source.GetProperty("schema").GetString() !=
                "opennv-fo3-cg02-stage-35-post-intercom-runtime/v1")
            throw new InvalidOperationException(
                "Fallout 3 CG02 post-intercom runtime identity differs.");
        var dialogue = source.GetProperty("dialogue");
        if (!dialogue.GetProperty("dialoguePlaybackPrepared").GetBoolean() ||
            !dialogue.GetProperty("dialoguePlaybackImplemented").GetBoolean())
            throw new InvalidOperationException(
                "Fallout 3 CG02 post-intercom dialogue is not prepared.");
        var cues = dialogue.GetProperty("cues").EnumerateArray().Select(row =>
        {
            var info = row.GetProperty("infoFormId").GetString()!;
            return new Fo3Cg02PostIntercomCue(
                info,
                row.GetProperty("engineSex").ValueKind == JsonValueKind.Null
                    ? null : row.GetProperty("engineSex").GetString(),
                row.GetProperty("speakerBaseFormId").GetString()!,
                row.GetProperty("responses").EnumerateArray().Select(response =>
                {
                    var index = response.GetProperty("index").GetInt32();
                    return new Fo3OwnedDialogueResponse(
                        index, response.GetProperty("text").GetString()!,
                        Fo3Cg01Stage10Transition.LoadDialogueAsset(
                            response.GetProperty("voice"), $"_{info}_{index}.ogg"),
                        Fo3Cg01Stage10Transition.LoadDialogueAsset(
                            response.GetProperty("lip"), $"_{info}_{index}.lip"));
                }).ToArray(),
                row.GetProperty("targetStage").ValueKind == JsonValueKind.Null
                    ? null : row.GetProperty("targetStage").GetInt32());
        }).ToArray();
        var packages = source.GetProperty("packages");
        Fo3Cg02PostIntercomPackage LoadPackage(JsonElement row) => new(
            row.GetProperty("formId").GetString()!,
            row.GetProperty("targetKind").GetString()!,
            row.GetProperty("targetFormId").GetString()!,
            row.TryGetProperty("targetTransform", out var transform)
                ? Fo3Cg01Stage12Transition.LoadTransform(transform) : null,
            row.GetProperty("radiusGameUnits").GetInt32());
        var stageResults = source.GetProperty("stageResults").EnumerateObject()
            .ToDictionary(property => int.Parse(property.Name,
                    System.Globalization.CultureInfo.InvariantCulture),
                property => (IReadOnlyList<Fo3Cg02PostIntercomCommand>)property.Value
                    .GetProperty("commands").EnumerateArray().Select(row =>
                        new Fo3Cg02PostIntercomCommand(
                            row.GetProperty("kind").GetString()!,
                            row.TryGetProperty("referenceFormId", out var reference)
                                ? reference.GetString()! : "",
                            row.TryGetProperty("variable", out var variable)
                                ? variable.GetString()! : "",
                            row.TryGetProperty("value", out var value)
                                ? value.GetInt32() : 0,
                            row.TryGetProperty("objectiveIndex", out var objective)
                                ? objective.GetInt32() : 0,
                            row.TryGetProperty("questFormId", out var quest)
                                ? quest.GetString()! : "",
                            row.TryGetProperty("stage", out var stage)
                                ? stage.GetInt32() : 0)).ToArray());
        var actorScene = source.GetProperty("jonasActorScene");
        return new Fo3Cg02PostIntercomRuntime(
            source.GetProperty("sourceStage").GetInt32(),
            source.GetProperty("answerStage").GetInt32(),
            source.GetProperty("goodbyeStage").GetInt32(),
            source.GetProperty("targetStage").GetInt32(),
            source.GetProperty("dadReferenceFormId").GetString()!,
            source.GetProperty("dadBaseFormId").GetString()!,
            source.GetProperty("jonasReferenceFormId").GetString()!,
            source.GetProperty("jonasBaseFormId").GetString()!,
            actorScene.GetProperty("scene").GetString()!,
            actorScene.GetProperty("sha256").GetString()!,
            source.GetProperty("intercomReferenceFormId").GetString()!,
            LoadPackage(packages.GetProperty("toIntercom")),
            LoadPackage(packages.GetProperty("talkToJonas")),
            LoadPackage(packages.GetProperty("toPlayer")),
            cues, stageResults,
            source.TryGetProperty("reactorGiftRuntime", out var reactorGift)
                ? LoadCg02ReactorGift(reactorGift)
                : null,
            source.GetProperty("nextBoundary").GetProperty("blocker").GetString()!);
    }

    private static Fo3Cg02ReactorGiftRuntime LoadCg02ReactorGift(JsonElement source)
    {
        if (source.GetProperty("schema").GetString() !=
                "opennv-fo3-cg02-stage-40-reactor-gift-runtime/v1")
            throw new InvalidOperationException(
                "Fallout 3 CG02 reactor-gift runtime identity differs.");
        var participants = source.GetProperty("participants").EnumerateArray()
            .Select(LoadCg02ReactorGiftParticipant).ToArray();
        var stageResults = source.GetProperty("stageResults").EnumerateObject()
            .ToDictionary(property => int.Parse(property.Name,
                    System.Globalization.CultureInfo.InvariantCulture),
                property => (IReadOnlyList<Fo3Cg02ReactorGiftCommand>)property.Value
                    .GetProperty("commands").EnumerateArray().Select(row =>
                        new Fo3Cg02ReactorGiftCommand(
                            row.GetProperty("kind").GetString()!,
                            row.TryGetProperty("referenceFormId", out var reference)
                                ? reference.GetString()! : "",
                            row.TryGetProperty("itemFormId", out var item)
                                ? item.GetString()! : "",
                            row.TryGetProperty("targetFormId", out var target)
                                ? target.GetString()! : "",
                            row.TryGetProperty("targetTransform", out var transform)
                                ? Fo3Cg01Stage12Transition.LoadTransform(transform) : null,
                            row.TryGetProperty("count", out var count)
                                ? count.GetInt32() : 0,
                            row.TryGetProperty("value", out var value)
                                ? value.GetInt32() : 0,
                            row.TryGetProperty("objectiveIndex", out var objective)
                                ? objective.GetInt32() : 0,
                            row.TryGetProperty("arguments", out var arguments)
                                ? arguments.EnumerateArray().Select(value =>
                                    value.GetInt32()).ToArray()
                                : [])).ToArray());
        return new Fo3Cg02ReactorGiftRuntime(
            source.GetProperty("sourceStage").GetInt32(),
            source.GetProperty("jonasStage").GetInt32(),
            source.GetProperty("targetStage").GetInt32(),
            participants,
            source.GetProperty("packages").GetProperty("jonasGreet")
                .GetProperty("formId").GetString()!,
            source.GetProperty("packages").GetProperty("dadGreet")
                .GetProperty("formId").GetString()!,
            stageResults,
            source.GetProperty("nextBoundary").GetProperty("blocker").GetString()!);
    }

    private static Fo3Cg02BirthdayParticipant LoadCg02ReactorGiftParticipant(
        JsonElement participant)
    {
        var dialogue = participant.GetProperty("dialogue");
        if (!dialogue.GetProperty("dialoguePlaybackPrepared").GetBoolean() ||
            !dialogue.GetProperty("dialoguePlaybackImplemented").GetBoolean())
            throw new InvalidOperationException(
                "Fallout 3 CG02 reactor-gift dialogue assets differ.");
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
                        0.0, "", 0, "", "", 0, "", "")).ToArray()))
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

    private static Fo3Cg02CakeRuntime LoadCg02Cake(
        JsonElement source,
        int expectedSourceStage)
    {
        if (source.GetProperty("schema").GetString() !=
                "opennv-fo3-cg02-stage-12-cake-runtime/v1" ||
            source.GetProperty("sourceStage").GetInt32() != expectedSourceStage)
            throw new InvalidOperationException(
                "Fallout 3 CG02 cake runtime identity differs.");
        var dialogue = source.GetProperty("dialogue");
        if (!dialogue.GetProperty("dialoguePlaybackPrepared").GetBoolean() ||
            !dialogue.GetProperty("dialoguePlaybackImplemented").GetBoolean())
            throw new InvalidOperationException(
                "Fallout 3 CG02 cake dialogue assets differ.");
        var cues = dialogue.GetProperty("cues").EnumerateArray().Select(row =>
        {
            var response = row.GetProperty("response");
            var infoFormId = row.GetProperty("infoFormId").GetString()!;
            var index = response.GetProperty("index").GetInt32();
            return new Fo3Cg02CakeCue(
                row.GetProperty("sequence").GetInt32(),
                row.GetProperty("speakerBaseFormId").GetString()!,
                infoFormId,
                new Fo3OwnedDialogueResponse(
                    index,
                    response.GetProperty("text").GetString()!,
                    Fo3Cg01Stage10Transition.LoadDialogueAsset(
                        response.GetProperty("voice"), $"_{infoFormId}_{index}.ogg"),
                    Fo3Cg01Stage10Transition.LoadDialogueAsset(
                        response.GetProperty("lip"), $"_{infoFormId}_{index}.lip")),
                row.GetProperty("effects").EnumerateArray()
                    .Select(value => value.GetString()!).ToArray());
        }).ToArray();
        var trigger = source.GetProperty("trigger");
        var andy = source.GetProperty("andy");
        var actorScene = andy.GetProperty("actorScene");
        var package = source.GetProperty("package");
        return new Fo3Cg02CakeRuntime(
            expectedSourceStage,
            source.GetProperty("triggerStage").GetInt32(),
            source.GetProperty("targetStage").GetInt32(),
            source.GetProperty("failsafeSeconds").GetDouble(),
            trigger.GetProperty("referenceFormId").GetString()!,
            Fo3Cg01Stage12Transition.LoadTransform(
                trigger.GetProperty("sourceTransform")),
            LoadCg02CakeVector(trigger.GetProperty("dimensionsGameUnits")),
            andy.GetProperty("referenceFormId").GetString()!,
            andy.GetProperty("baseFormId").GetString()!,
            actorScene.GetProperty("scene").GetString()!,
            actorScene.GetProperty("sha256").GetString()!,
            package.GetProperty("formId").GetString()!,
            package.GetProperty("targetMarkerFormId").GetString()!,
            Fo3Cg01Stage12Transition.LoadTransform(
                package.GetProperty("targetTransform")),
            package.GetProperty("radiusGameUnits").GetInt32(),
            package.GetProperty("locomotion").GetProperty("logicalPath").GetString()!,
            package.GetProperty("locomotion").GetProperty("sha256").GetString()!,
            package.GetProperty("locomotion").GetProperty("rootMotion")
                .GetProperty("speedGameUnitsPerSecond").GetSingle(),
            package.GetProperty("idle").GetProperty("modelPath").GetString()!,
            source.GetProperty("cakeReferenceFormId").GetString()!,
            cues,
            package.GetProperty("resultCommands").GetArrayLength(),
            source.GetProperty("stage15Commands").GetArrayLength(),
            source.GetProperty("stage16Commands").GetArrayLength(),
            source.GetProperty("nextBoundary").GetProperty("blocker").GetString()!);
    }

    private static Fo3Cg01Vector3 LoadCg02CakeVector(JsonElement source)
    {
        var values = source.EnumerateArray().Select(value => value.GetDouble()).ToArray();
        if (values.Length != 3 || values.Any(value => !double.IsFinite(value) || value <= 0.0))
            throw new InvalidOperationException(
                "Fallout 3 CG02 cake trigger dimensions differ.");
        return new Fo3Cg01Vector3(values[0], values[1], values[2]);
    }

    private static Fo3Cg01DadTravelPackage LoadTravelPackage(
        JsonElement source,
        int? completionStage,
        int? sourceStage = null)
    {
        var target = source.GetProperty("target");
        var package = new Fo3Cg01PostStage14Package(
            source.GetProperty("formId").GetString()!,
            source.GetProperty("editorId").GetString()!,
            target.GetProperty("formId").GetString()!,
            Fo3Cg01Stage12Transition.LoadTransform(target.GetProperty("sourceTransform")),
            target.GetProperty("radiusGameUnits").GetInt32(),
            completionStage);
        var stageCommands = sourceStage is null
            ? Array.Empty<SourceGamebryoStageCommand<string>>()
            : ReadCommands(source.GetProperty("stageResult"), new Dictionary<string, GamebryoStageCommandKind>(StringComparer.Ordinal)
            {
                ["setScriptVariable"] = GamebryoStageCommandKind.SetScriptVariable,
                ["unlock"] = GamebryoStageCommandKind.ActorIntent,
                ["evaluatePackage"] = GamebryoStageCommandKind.ActorIntent,
            });
        var completionCommands = completionStage is null
            ? Array.Empty<SourceGamebryoStageCommand<string>>()
            : source.GetProperty("completionCommands").EnumerateArray().Select((row, index) =>
            {
                if (row.GetProperty("index").GetInt32() != index || row.GetProperty("kind").GetString() != "setScriptVariable")
                    throw new InvalidOperationException("Fallout 3 CG01 Bible completion differs.");
                return new SourceGamebryoStageCommand<string>(index, GamebryoStageCommandKind.SetScriptVariable, "setScriptVariable");
            }).ToArray();
        return new Fo3Cg01DadTravelPackage(package, stageCommands,
            sourceStage ?? (int)source.GetProperty("condition").GetProperty("comparisonValue").GetDouble(),
            completionStage, completionCommands);
    }

    private static IReadOnlyList<SourceGamebryoStageCommand<string>> ReadCommands(
        JsonElement result,
        IReadOnlyDictionary<string, GamebryoStageCommandKind> kinds) =>
        result.GetProperty("commands").EnumerateArray().Select((row, index) =>
        {
            var kind = row.GetProperty("kind").GetString()!;
            if (row.GetProperty("index").GetInt32() != index || !kinds.TryGetValue(kind, out var mapped))
                throw new InvalidOperationException("Fallout 3 CG01 Dad-return command differs.");
            return new SourceGamebryoStageCommand<string>(index, mapped, kind);
        }).ToArray();

    internal int ExecuteTargetResult()
    {
        var applied = 0;
        GamebryoStageCommandExecutor.ExecuteAll(TargetCommands, command =>
        {
            applied++;
            return applied == command.SourceIndex + 1;
        });
        return applied;
    }

    internal int ExecuteCompletionResult()
    {
        var applied = 0;
        GamebryoStageCommandExecutor.ExecuteAll(CompletionCommands, command =>
        {
            applied++;
            return applied == command.SourceIndex + 1;
        });
        return applied;
    }
}

internal sealed record Fo3Cg01Stage20Interaction(
    int SourceStage,
    int GateStage,
    int ExitStage,
    int BookStage,
    string GateReferenceFormId,
    string ExitTriggerReferenceFormId,
    Fo3Cg01Transform ExitTriggerTransform,
    Fo3Cg01Vector3 ExitTriggerDimensionsGameUnits,
    string BookReferenceFormId,
    string BookDisplayName,
    int MenuPoints,
    string MenuDocument,
    IReadOnlyList<Fo3SpecialActorValue> ActorValues,
    OwnedGamebryoSpecialBookMenu Tiles,
    IReadOnlyList<Fo3SpecialStageResult> StageResults,
    Fo3Cg01Stage50Timer TimerTransition,
    string NextBoundaryBlocker)
{
    internal const string ExpectedSchema = "opennv-fo3-cg01-stage-20-special-runtime/v1";

    internal static Fo3Cg01Stage20Interaction Load(JsonElement source, int expectedSourceStage)
    {
        if (RequiredString(source, "schema") != ExpectedSchema ||
            RequiredString(source, "status") != "source-backed-physical-interaction-runtime-ready" ||
            RequiredInteger(source, "sourceStage") != expectedSourceStage)
            throw new InvalidOperationException("Fallout 3 CG01 stage-20 interaction identity differs.");
        var gate = RequiredObject(source, "gate");
        var exit = RequiredObject(source, "exitTrigger");
        var book = RequiredObject(source, "specialBook");
        var gateStage = RequiredInteger(gate, "targetStage");
        var exitStage = RequiredInteger(exit, "targetStage");
        var bookStage = RequiredInteger(book, "targetStage");
        if (!(expectedSourceStage < gateStage && gateStage < exitStage && exitStage < bookStage) ||
            RequiredInteger(book, "menuPoints") <= 0 ||
            RequiredInteger(exit, "primitiveType") != 1)
            throw new InvalidOperationException("Fallout 3 CG01 stage-20 interaction sequence differs.");
        var dimensions = RequiredArray(exit, "dimensionsGameUnits").EnumerateArray()
            .Select(value => value.GetDouble()).ToArray();
        if (dimensions.Length != 3 || dimensions.Any(value => !double.IsFinite(value) || value <= 0))
            throw new InvalidOperationException("Fallout 3 CG01 crib-exit dimensions differ.");
        var transformSource = RequiredObject(exit, "sourceTransform");
        var position = RequiredArray(transformSource, "positionGameUnits").EnumerateArray().Select(v => v.GetDouble()).ToArray();
        var rotation = RequiredArray(transformSource, "rotationRadians").EnumerateArray().Select(v => v.GetDouble()).ToArray();
        if (position.Length != 3 || rotation.Length != 3)
            throw new InvalidOperationException("Fallout 3 CG01 crib-exit transform differs.");
        var boundary = RequiredObject(source, "nextBoundary");
        if (!RequiredBoolean(boundary, "applied") ||
            boundary.GetProperty("blocker").ValueKind != JsonValueKind.Null)
            throw new InvalidOperationException("Fallout 3 CG01 stage-50 boundary differs.");
        var actorValues = RequiredArray(book, "actorValues").EnumerateArray()
            .Select((row, index) =>
            {
                if (RequiredInteger(row, "index") != index)
                    throw new InvalidOperationException("Fallout 3 SPECIAL actor-value order differs.");
                _ = RequiredString(row, "recordSha256");
                return new Fo3SpecialActorValue(index, RequiredFormId(row, "formId"),
                    RequiredString(row, "editorId"), RequiredString(row, "label"),
                    RequiredString(row, "description"), RequiredInteger(row, "initialValue"),
                    RequiredInteger(row, "minimumValue"), RequiredInteger(row, "maximumValue"));
            }).ToArray();
        if (actorValues.Length == 0 || actorValues.Any(value =>
                value.MinimumValue > value.InitialValue ||
                value.InitialValue > value.MaximumValue) ||
            actorValues.Sum(value => value.InitialValue) > RequiredInteger(book, "menuPoints"))
            throw new InvalidOperationException("Fallout 3 SPECIAL actor-value allocation differs.");
        var stageResults = RequiredArray(source, "stageResults").EnumerateArray()
            .Select(row =>
            {
                var stage = RequiredInteger(row, "stage");
                var commands = RequiredArray(row, "commands").EnumerateArray()
                    .Select((command, index) =>
                    {
                        if (RequiredInteger(command, "index") != index)
                            throw new InvalidOperationException(
                                "Fallout 3 SPECIAL stage-command order differs.");
                        var kind = RequiredString(command, "kind") switch
                        {
                            "setObjectiveCompleted" or "setObjectiveDisplayed" =>
                                GamebryoStageCommandKind.Objective,
                            "setOpenState" or "lock" => GamebryoStageCommandKind.ActorIntent,
                            "setQuestVariable" => GamebryoStageCommandKind.SetQuestVariable,
                            _ => throw new InvalidOperationException(
                                "Fallout 3 SPECIAL stage-command kind differs."),
                        };
                        return new SourceGamebryoStageCommand<string>(
                            index, kind, RequiredString(command, "kind"));
                    }).ToArray();
                return new Fo3SpecialStageResult(stage, commands);
            }).ToArray();
        if (!stageResults.Select(value => value.Stage).SequenceEqual(
                new[] { gateStage, exitStage, bookStage }))
            throw new InvalidOperationException("Fallout 3 SPECIAL stage-result coverage differs.");
        return new Fo3Cg01Stage20Interaction(
            expectedSourceStage, gateStage, exitStage, bookStage,
            RequiredFormId(gate, "referenceFormId"), RequiredFormId(exit, "referenceFormId"),
            new Fo3Cg01Transform(new Fo3Cg01Vector3(position[0], position[1], position[2]),
                new Fo3Cg01Vector3(rotation[0], rotation[1], rotation[2]),
                RequiredDouble(transformSource, "scale")),
            new Fo3Cg01Vector3(dimensions[0], dimensions[1], dimensions[2]),
            RequiredFormId(book, "referenceFormId"), RequiredString(book, "displayName"),
            RequiredInteger(book, "menuPoints"), RequiredString(book, "menuDocument"),
            actorValues,
            OwnedGamebryoTileRuntime.ParseSpecialBookMenu(RequiredObject(book, "tiles")),
            stageResults,
            Fo3Cg01Stage50Timer.Load(RequiredObject(source, "timerTransition"), bookStage),
            RequiredString(RequiredObject(RequiredObject(source, "timerTransition"), "nextBoundary"), "blocker"));
    }

    internal int ExecuteStageResult(int stage)
    {
        var result = StageResults.Single(value => value.Stage == stage);
        var applied = 0;
        GamebryoStageCommandExecutor.ExecuteAll(result.Commands, command =>
        {
            applied++;
            return applied == command.SourceIndex + 1;
        });
        return applied;
    }

    private static double RequiredDouble(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.TryGetDouble(out var result) && double.IsFinite(result)
            ? result : throw new InvalidOperationException($"Fallout 3 CG01 interaction field {name} differs.");
    private static int RequiredInteger(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.TryGetInt32(out var result) ? result : throw new InvalidOperationException($"Fallout 3 CG01 interaction field {name} differs.");
    private static bool RequiredBoolean(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : throw new InvalidOperationException($"Fallout 3 CG01 interaction field {name} differs.");
    private static string RequiredString(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()) ? value.GetString()! : throw new InvalidOperationException($"Fallout 3 CG01 interaction field {name} differs.");
    private static string RequiredFormId(JsonElement parent, string name)
    {
        var value = RequiredString(parent, name);
        return value.Length == sizeof(uint) * 2 && value.All(Uri.IsHexDigit) ? value : throw new InvalidOperationException($"Fallout 3 CG01 interaction FormID {name} differs.");
    }
    private static JsonElement RequiredObject(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object ? value : throw new InvalidOperationException($"Fallout 3 CG01 interaction field {name} differs.");
    private static JsonElement RequiredArray(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array ? value : throw new InvalidOperationException($"Fallout 3 CG01 interaction field {name} differs.");
}

internal sealed record Fo3Cg01PostStage14Transition(
    int SourceStage,
    int Stage16,
    int Stage18,
    int TargetStage,
    string DadReferenceFormId,
    Fo3Cg01PostStage14Package CloseGatePackage,
    Fo3Cg01PostStage14Package CloseDoorPackage,
    Fo3Cg01PostStage14Package LeaveRoomPackage,
    string PlaypenGateReferenceFormId,
    string PlayroomDoorReferenceFormId,
    int PlayroomDoorLockLevel,
    IReadOnlyList<int> EnabledPlayerControls,
    int ObjectiveIndex,
    IReadOnlyList<Fo3Cg01PostStage14Cue> Cues,
    int AccountedCommandCount,
    Fo3Cg01Stage20Interaction Stage20Interaction,
    string NextBoundaryBlocker)
{
    internal const string ExpectedSchema = "opennv-fo3-cg01-stage-14-to-20-runtime/v1";
    internal const string ExpectedSavedStateSchema =
        "opennv-fo3-cg01-stage-14-to-cg02-stage-5-runtime-state/v5";

    private const string ExpectedStatus = "source-backed-package-dialogue-runtime-ready";
    private const int GetPcIsSexFunction = 131;
    private const int GetIsIdFunction = 72;
    private const int ExpectedCueRows = 3;
    private const int ExpectedAppliedCues = 2;
    private const int ExpectedPackageCount = 3;
    private const int FormIdRadix = 16;
    private const uint MaleSexValue = 0;
    private const uint FemaleSexValue = 1;

    internal static Fo3Cg01PostStage14Transition Load(
        JsonElement source,
        Fo3Cg01Stage0Transition stage0,
        Fo3Cg01Stage12DadResponse stage14)
    {
        var sourceStage = RequiredInteger(source, "sourceStage");
        var stage16 = RequiredInteger(source, "stage16");
        var stage18 = RequiredInteger(source, "stage18");
        var targetStage = RequiredInteger(source, "targetStage");
        if (RequiredString(source, "schema") != ExpectedSchema ||
            RequiredString(source, "status") != ExpectedStatus ||
            sourceStage != stage14.TargetStage ||
            !(sourceStage < stage16 && stage16 < stage18 && stage18 < targetStage) ||
            RequiredFormId(source, "dadReferenceFormId") != stage0.Dad.FormId)
            throw new InvalidOperationException(
                "Fallout 3 CG01 post-stage-14 identity differs.");

        var packages = RequiredObject(source, "packages");
        var closeGate = LoadPackage(
            RequiredObject(packages, "closeGate"), stage16);
        var closeDoor = LoadPackage(
            RequiredObject(packages, "closeDoor"), stage18);
        var leaveRoom = LoadPackage(
            RequiredObject(packages, "leaveRoom"), null);
        if (new[] { closeGate.FormId, closeDoor.FormId, leaveRoom.FormId }
                .Distinct(StringComparer.OrdinalIgnoreCase).Count() != ExpectedPackageCount)
            throw new InvalidOperationException(
                "Fallout 3 CG01 post-stage-14 package identities differ.");

        var stage16Commands = LoadCommands(RequiredObject(source, "stage16Result"));
        var stage18Commands = LoadCommands(RequiredObject(source, "stage18Result"));
        var stage20Commands = LoadCommands(RequiredObject(source, "stage20Result"));
        var allCommands = stage16Commands.Concat(stage18Commands).Concat(stage20Commands).ToArray();
        var playpen = allCommands.Where(value => value.Kind == "setOpenState")
            .GroupBy(value => value.ReferenceFormId, StringComparer.OrdinalIgnoreCase)
            .Single(value => value.Count() == 2).Key;
        var playroom = allCommands.Single(value => value.Kind == "lock").ReferenceFormId;
        var lockLevel = allCommands.Single(value => value.Kind == "lock").Value;
        var controls = stage20Commands.Single(value => value.Kind == "enablePlayerControls")
            .Arguments;
        var objective = stage20Commands.Single(value => value.Kind == "setObjectiveDisplayed")
            .Value;
        if (!stage16Commands.Any(value => value.Kind == "setScriptVariable") ||
            !stage18Commands.Any(value => value.Kind == "setStage" && value.Value == targetStage) ||
            !stage20Commands.Any(value => value.Kind == "evaluatePackage" &&
                value.ReferenceFormId == stage0.Dad.FormId) ||
            controls.Count == 0)
            throw new InvalidOperationException(
                "Fallout 3 CG01 post-stage-14 commands differ.");

        var dialogue = RequiredObject(source, "dialogue");
        if (!RequiredBoolean(dialogue, "dialoguePlaybackPrepared") ||
            !RequiredBoolean(dialogue, "dialoguePlaybackImplemented") ||
            RequiredFormId(dialogue, "topicFormId") != stage14.TopicFormId ||
            RequiredString(dialogue, "topicEditorId") != stage14.TopicEditorId)
            throw new InvalidOperationException(
                "Fallout 3 CG01 stage-16 Dad dialogue is not prepared.");
        var rows = RequiredArray(dialogue, "branches").EnumerateArray()
            .OrderBy(value => RequiredInteger(value, "sequence")).ToArray();
        if (rows.Length != ExpectedCueRows)
            throw new InvalidOperationException(
                "Fallout 3 CG01 stage-16 Dad dialogue coverage differs.");
        var cues = rows.Select((row, index) => LoadCue(row, index, stage0)).ToArray();
        if (cues.Count(value => value.EngineSex is null) != 1 ||
            cues.Count(value => value.EngineSex == "male") != 1 ||
            cues.Count(value => value.EngineSex == "female") != 1)
            throw new InvalidOperationException(
                "Fallout 3 CG01 stage-16 Dad sex selection differs.");

        var boundary = RequiredObject(source, "nextBoundary");
        if (!RequiredBoolean(boundary, "applied") ||
            boundary.GetProperty("blocker").ValueKind != JsonValueKind.Null)
            throw new InvalidOperationException(
                "Fallout 3 CG01 post-stage-20 boundary differs.");
        var interaction = Fo3Cg01Stage20Interaction.Load(
            RequiredObject(source, "stage20Interaction"), targetStage);
        return new Fo3Cg01PostStage14Transition(
            sourceStage,
            stage16,
            stage18,
            targetStage,
            stage0.Dad.FormId,
            closeGate,
            closeDoor,
            leaveRoom,
            playpen,
            playroom,
            lockLevel,
            controls,
            objective,
            cues,
            allCommands.Length + ExpectedPackageCount,
            interaction,
            interaction.NextBoundaryBlocker);
    }

    internal IReadOnlyList<Fo3Cg01PostStage14Cue> SelectCues(string engineSex)
    {
        if (engineSex is not ("male" or "female"))
            throw new InvalidOperationException("Fallout 3 CG01 Dad response sex differs.");
        var selected = Cues.Where(value => value.EngineSex is null || value.EngineSex == engineSex)
            .OrderBy(value => value.Sequence).ToArray();
        if (selected.Length != ExpectedAppliedCues)
            throw new InvalidOperationException(
                "Fallout 3 CG01 Dad response selection is incomplete.");
        return selected;
    }

    internal Fo3Cg01Stage20State Apply(Fo3Cg01Stage14State stage14, string engineSex)
    {
        if (stage14.ActiveStage != SourceStage || !stage14.DadPackageEvaluated ||
            stage14.NextBoundary.Applied)
            throw new InvalidOperationException(
                "Fallout 3 CG01 post-stage-14 source state differs.");
        var cues = SelectCues(engineSex);
        return new Fo3Cg01Stage20State(
            SourceStage,
            stage14.ActiveQuestFormId,
            stage14.ActiveQuestEditorId,
            TargetStage,
            cues.Select(value => value.InfoFormId).ToArray(),
            [CloseGatePackage.FormId, CloseDoorPackage.FormId, LeaveRoomPackage.FormId],
            PlaypenGateReferenceFormId,
            false,
            PlayroomDoorReferenceFormId,
            false,
            PlayroomDoorLockLevel,
            true,
            ObjectiveIndex,
            AccountedCommandCount,
            AccountedCommandCount,
            Stage20Interaction.ActorValues.Select(value => value.InitialValue).ToArray(),
            false,
            Stage20Interaction.TimerTransition.InitialSeconds,
            false,
            0.0,
            false,
            new Fo3Cg01Stage12Boundary(false, NextBoundaryBlocker));
    }

    internal object SavedState(Fo3Cg01Stage20State state) => new
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
        appliedPackageFormIds = state.AppliedPackageFormIds,
        playpenGate = new { referenceFormId = state.PlaypenGateReferenceFormId, open = state.PlaypenGateOpen },
        playroomDoor = new
        {
            referenceFormId = state.PlayroomDoorReferenceFormId,
            open = state.PlayroomDoorOpen,
            lockLevel = state.PlayroomDoorLockLevel,
        },
        playerMovementEnabled = state.PlayerMovementEnabled,
        displayedObjectiveIndex = state.DisplayedObjectiveIndex,
        accountedCommandCount = state.AccountedCommandCount,
        appliedCommandCount = state.AppliedCommandCount,
        specialValues = state.SpecialValues,
        specialBookAccepted = state.SpecialBookAccepted,
        timerRemainingSeconds = state.TimerRemainingSeconds,
        timerAdvancing = state.TimerAdvancing,
        imageSpaceElapsedSeconds = state.ImageSpaceElapsedSeconds,
        stage90SoundStarted = state.Stage90SoundStarted,
        nextBoundary = new { applied = false, blocker = state.NextBoundary.Blocker },
    };

    internal Fo3Cg01Stage20State LoadSavedState(
        JsonElement source,
        Fo3Cg01Stage20State baseline)
    {
        var active = RequiredObject(source, "activeQuest");
        var gate = RequiredObject(source, "playpenGate");
        var door = RequiredObject(source, "playroomDoor");
        var boundary = RequiredObject(source, "nextBoundary");
        var stage = RequiredInteger(active, "stage");
        var dadLead = Stage20Interaction.TimerTransition.DadLead;
        var completion = dadLead.Completion;
        var cg02Intro = completion.Cg02Stage0.IntroRuntime;
        var cg02DadSpeech = cg02Intro?.DadSpeechRuntime;
        var cg02Overseer = cg02DadSpeech?.OverseerSpeechRuntime;
        var cg02Party = cg02Overseer?.DadPartyRuntime;
        var cg02Birthday = cg02Party?.BirthdayInteractionsRuntime;
        var cg02Butch = cg02Birthday?.ButchRuntime;
        var savedInfoFormIds = RequiredArray(source, "appliedInfoFormIds")
            .EnumerateArray().Select(value => value.GetString() ?? "").ToArray();
        var baselineInfoCount = baseline.AppliedInfoFormIds.Count;
        var savedCg02InfoFormIds = savedInfoFormIds.Skip(baselineInfoCount).ToArray();
        var savedPackageFormIds = RequiredArray(source, "appliedPackageFormIds")
            .EnumerateArray().Select(value => value.GetString() ?? "").ToArray();
        var validCg02Sequences = cg02DadSpeech is null
            ? Array.Empty<string[]>()
            : cg02DadSpeech.Cues.Where(value => value.EngineSex is not null)
                .Select(dadSexCue => new[]
                {
                    dadSexCue.InfoFormId,
                    cg02DadSpeech.Cues.Single(value => value.EngineSex is null).InfoFormId,
                }.Concat(cg02Overseer is null ? [] :
                    cg02Overseer.Cues.Where(value => value.EngineSex is null ||
                            value.EngineSex == dadSexCue.EngineSex)
                        .OrderBy(value => value.Sequence)
                        .Select(value => value.InfoFormId))
                    .Concat(cg02Party is null ? [] : [cg02Party.Cue.InfoFormId])
                    .ToArray())
                .ToArray();
        var birthdayParticipantInfoFormIds = cg02Birthday?.Participants
            .SelectMany(value => value.Nodes.Keys)
            .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
        var birthdayInfoFormIds = new HashSet<string>(
            birthdayParticipantInfoFormIds, StringComparer.OrdinalIgnoreCase);
        if (cg02Birthday?.CakeRuntime is { } cakeRuntime)
            birthdayInfoFormIds.UnionWith(
                cakeRuntime.Cues.Select(value => value.InfoFormId));
        if (cg02Butch?.PostIntercomRuntime is { } postIntercom)
            birthdayInfoFormIds.UnionWith(
                postIntercom.Cues.Select(value => value.InfoFormId));
        if (cg02Butch?.PostIntercomRuntime?.ReactorGiftRuntime is { } reactorGift)
            birthdayInfoFormIds.UnionWith(reactorGift.Participants.SelectMany(
                value => value.Nodes.Keys));
        var matchingCg02Sequence = validCg02Sequences.SingleOrDefault(sequence =>
            sequence.Take(Math.Min(savedCg02InfoFormIds.Length, sequence.Length))
                .SequenceEqual(savedCg02InfoFormIds.Take(sequence.Length),
                    StringComparer.OrdinalIgnoreCase) &&
            (savedCg02InfoFormIds.Length <= sequence.Length ||
             savedCg02InfoFormIds.Skip(sequence.Length).All(birthdayInfoFormIds.Contains)));
        var cg02IntroComplete = cg02Intro is not null &&
            (stage == cg02Intro.TargetStage || stage == cg02DadSpeech?.TargetStage ||
             cg02Overseer is not null &&
                (stage == cg02Overseer.TargetStage ||
                 cg02Overseer.StageResults.ContainsKey(stage)));
        cg02IntroComplete = cg02IntroComplete || stage == cg02Party?.TargetStage;
        cg02IntroComplete = cg02IntroComplete ||
            cg02Birthday?.StageResults.ContainsKey(stage) == true;
        cg02IntroComplete = cg02IntroComplete ||
            cg02Birthday?.CakeRuntime is { } cake &&
                (stage == cake.TriggerStage || stage == cake.TargetStage);
        cg02IntroComplete = cg02IntroComplete || cg02Butch is not null &&
            (stage == cg02Butch.SceneDoneStage ||
             stage == cg02Butch.AggregateStage ||
             stage == cg02Butch.IntercomStage ||
             cg02Butch.PostIntercomRuntime is { } post &&
                (stage == post.AnswerStage || stage == post.GoodbyeStage ||
                 stage == post.TargetStage ||
                 post.ReactorGiftRuntime is { } gift &&
                    (stage == gift.JonasStage || stage == gift.TargetStage)));
        var cg02DadComplete = cg02DadSpeech is not null &&
            savedCg02InfoFormIds.Length >= 2;
        var reachedNextQuest = RequiredFormId(active, "formId") == completion.NextQuestFormId &&
            RequiredString(active, "editorId") == completion.NextQuestEditorId &&
            (stage == completion.Cg02Stage0.TargetStage || cg02IntroComplete);
        var progressStage = reachedNextQuest ? completion.TargetStage : stage;
        var values = RequiredArray(source, "specialValues").EnumerateArray()
            .Select(value => value.GetInt32()).ToArray();
        var accepted = RequiredBoolean(source, "specialBookAccepted");
        var supportedStages = new HashSet<int>
        {
                TargetStage,
                Stage20Interaction.GateStage,
                Stage20Interaction.ExitStage,
                Stage20Interaction.BookStage,
                Stage20Interaction.TimerTransition.TargetStage,
                Stage20Interaction.TimerTransition.CompletionStage,
                Stage20Interaction.TimerTransition.DialogueTargetStage,
                Stage20Interaction.TimerTransition.DadLead.SayToDoneStage,
                Stage20Interaction.TimerTransition.DadLead.EndTrigger.TargetStage,
        };
        supportedStages.UnionWith(Stage20Interaction.TimerTransition.DialogueCues
            .Where(cue => cue.TargetStage is not null).Select(cue => cue.TargetStage!.Value));
        if (!reachedNextQuest && !supportedStages.Contains(stage) ||
            values.Length != Stage20Interaction.ActorValues.Count ||
            values.Select((value, index) =>
                value < Stage20Interaction.ActorValues[index].MinimumValue ||
                value > Stage20Interaction.ActorValues[index].MaximumValue).Any(value => value) ||
            values.Sum() > Stage20Interaction.MenuPoints ||
            accepted && (stage < Stage20Interaction.BookStage ||
                values.Sum() != Stage20Interaction.MenuPoints))
            throw new InvalidOperationException(
                "Saved Fallout 3 SPECIAL allocation differs.");
        var gateOpen = RequiredBoolean(gate, "open");
        var expectedGateOpen = progressStage != TargetStage;
        var objective = RequiredInteger(source, "displayedObjectiveIndex");
        var expectedObjective = reachedNextQuest
            ? cg02Butch?.PostIntercomRuntime is { } objectivePost &&
                stage >= objectivePost.TargetStage
                ? objectivePost.StageResults[objectivePost.TargetStage]
                    .Where(value => value.Kind == "setObjectiveDisplayed" && value.Value != 0)
                    .Select(value => value.ObjectiveIndex).Single()
                : dadLead.DisplayedObjectiveIndex
            : stage switch
            {
                var value when value == TargetStage => TargetStage,
                var value when value == Stage20Interaction.GateStage => Stage20Interaction.GateStage,
                var value when value >= Stage20Interaction.TimerTransition.DadLead.SayToDoneStage =>
                    Stage20Interaction.TimerTransition.DadLead.DisplayedObjectiveIndex,
                _ => Stage20Interaction.ExitStage,
            };
        var interactionCommandCount = Stage20Interaction.StageResults
            .Where(result => result.Stage <= progressStage)
            .Sum(result => result.Commands.Count);
        if (progressStage == Stage20Interaction.TimerTransition.TargetStage)
            interactionCommandCount += Stage20Interaction.TimerTransition.TargetCommands.Count;
        else if (progressStage >= Stage20Interaction.TimerTransition.CompletionStage)
            interactionCommandCount += Stage20Interaction.TimerTransition.TargetCommands.Count +
                Stage20Interaction.TimerTransition.CompletionCommands.Count;
        if (progressStage >= dadLead.BibleTravel.CompletionStage!.Value)
            interactionCommandCount += dadLead.BibleTravel.StageCommands.Count +
                dadLead.BibleTravel.CompletionCommands.Count;
        if (progressStage >= dadLead.SayToDoneStage)
            interactionCommandCount += dadLead.LeadTravel.StageCommands.Count +
                dadLead.SayToDoneCommands.Count;
        if (progressStage >= dadLead.EndTrigger.TargetStage)
            interactionCommandCount++;
        if (reachedNextQuest)
            interactionCommandCount += completion.Stage90CommandCount +
                completion.Stage100CommandCount +
                completion.Cg02Stage0.Stage5CommandCount +
                completion.Cg02Stage0.Stage0CommandCount;
        if (cg02IntroComplete)
            interactionCommandCount += cg02Intro!.FinalCommandCount;
        if (cg02DadComplete)
            interactionCommandCount += cg02DadSpeech!.FinalCommandCount;
        if (cg02Overseer is not null && savedCg02InfoFormIds.Length > 2)
        {
            foreach (var infoFormId in savedCg02InfoFormIds.Skip(2).Where(infoFormId =>
                cg02Overseer.Cues.Any(value => value.InfoFormId.Equals(
                    infoFormId, StringComparison.OrdinalIgnoreCase))))
            {
                var cue = cg02Overseer.Cues.Single(value =>
                    value.InfoFormId.Equals(infoFormId,
                        StringComparison.OrdinalIgnoreCase));
                interactionCommandCount += cue.Effects.Count;
                foreach (var stageCommand in cue.Effects.Where(value =>
                    value.Kind == "setStage"))
                    interactionCommandCount += cg02Overseer.StageResults[
                        (int)stageCommand.Value].Count;
            }
        }
        if (cg02Party is not null && savedCg02InfoFormIds.Contains(
                cg02Party.Cue.InfoFormId, StringComparer.OrdinalIgnoreCase))
            interactionCommandCount += cg02Party.StageCommands.Count + 1;
        if (cg02Birthday is not null)
        {
            foreach (var infoFormId in savedCg02InfoFormIds.Where(
                birthdayParticipantInfoFormIds.Contains))
            {
                var node = cg02Birthday.Participants.SelectMany(value => value.Nodes.Values)
                    .Single(value => value.InfoFormId.Equals(
                        infoFormId, StringComparison.OrdinalIgnoreCase));
                interactionCommandCount += node.Effects.Count(value =>
                    value.Kind != "sourceConditional");
                foreach (var stageEffect in node.Effects.Where(value =>
                    value.Kind == "setStage" &&
                    cg02Birthday.StageResults.ContainsKey(value.Stage)))
                {
                    var result = cg02Birthday.StageResults[stageEffect.Stage];
                    interactionCommandCount += result.CommandCount;
                    if (result.AggregateStage is not null)
                        interactionCommandCount++;
                }
            }
            if (cg02Birthday.CakeRuntime is { } commandCake)
            {
                var cakeCompleted = savedPackageFormIds.Contains(
                    commandCake.PackageFormId, StringComparer.OrdinalIgnoreCase);
                if (stage == commandCake.TriggerStage || cakeCompleted)
                    interactionCommandCount += commandCake.Stage15CommandCount + 1;
                if (cakeCompleted)
                    interactionCommandCount += commandCake.PackageResultCommandCount +
                        commandCake.Stage16CommandCount;
                interactionCommandCount += commandCake.Cues
                    .Where(cue => savedCg02InfoFormIds.Contains(
                        cue.InfoFormId, StringComparer.OrdinalIgnoreCase))
                    .Sum(cue => cue.Effects.Count);
            }
            if (cg02Butch is not null)
            {
                var butchPackageOccurrences = savedPackageFormIds.Count(value =>
                    value.Equals(cg02Butch.FindPlayerPackageFormId,
                        StringComparison.OrdinalIgnoreCase));
                if (butchPackageOccurrences > 1)
                    interactionCommandCount += cg02Butch.FindPlayerResultCommandCount;
                if (stage >= cg02Butch.IntercomStage)
                    interactionCommandCount += cg02Butch.Stage35Commands.Count;
                if (cg02Butch.PostIntercomRuntime is { } commandPost)
                {
                    foreach (var cue in commandPost.Cues.Where(cue =>
                        savedCg02InfoFormIds.Contains(cue.InfoFormId,
                            StringComparer.OrdinalIgnoreCase)))
                    {
                        if (cue.TargetStage is { } cueStage)
                            interactionCommandCount += 1 +
                                commandPost.StageResults[cueStage].Count;
                    }
                    if (commandPost.ReactorGiftRuntime is { } commandGift)
                    {
                        foreach (var node in commandGift.Participants
                            .SelectMany(value => value.Nodes.Values).Where(node =>
                                savedCg02InfoFormIds.Contains(node.InfoFormId,
                                    StringComparer.OrdinalIgnoreCase)))
                            interactionCommandCount += node.Effects.Count;
                        if (stage >= commandGift.JonasStage)
                            interactionCommandCount += commandGift.StageResults[
                                commandGift.JonasStage].Count;
                        if (stage >= commandGift.TargetStage)
                            interactionCommandCount += commandGift.StageResults[
                                commandGift.TargetStage].Count;
                    }
                }
            }
        }
        var expectedCommandCount = baseline.AccountedCommandCount + interactionCommandCount;
        var expectedPackages = baseline.AppliedPackageFormIds.AsEnumerable();
        if (progressStage >= Stage20Interaction.TimerTransition.CompletionStage)
            expectedPackages = expectedPackages.Append(
                Stage20Interaction.TimerTransition.DadReturnPackage.FormId);
        if (progressStage >= dadLead.BibleTravel.CompletionStage!.Value)
            expectedPackages = expectedPackages.Append(dadLead.BibleTravel.Package.FormId);
        if (progressStage >= dadLead.SayToDoneStage)
            expectedPackages = expectedPackages.Append(dadLead.LeadTravel.Package.FormId);
        if (cg02Birthday?.CakeRuntime is { } expectedCake &&
            savedPackageFormIds.Contains(expectedCake.PackageFormId,
                StringComparer.OrdinalIgnoreCase))
            expectedPackages = expectedPackages.Append(expectedCake.PackageFormId);
        if (cg02Butch is not null)
        {
            var butchPackageOccurrences = savedPackageFormIds.Count(value =>
                value.Equals(cg02Butch.FindPlayerPackageFormId,
                    StringComparison.OrdinalIgnoreCase));
            if (butchPackageOccurrences > 2)
                throw new InvalidOperationException(
                    "Saved Fallout 3 CG02 Butch package count differs.");
            for (var index = 0; index < butchPackageOccurrences; index++)
                expectedPackages = expectedPackages.Append(
                    cg02Butch.FindPlayerPackageFormId);
            if (cg02Butch.PostIntercomRuntime is { } expectedPost &&
                stage >= expectedPost.SourceStage)
                expectedPackages = expectedPackages.Append(
                    expectedPost.DadToIntercomPackage.FormId);
            if (cg02Butch.PostIntercomRuntime is { } talkPost &&
                stage >= talkPost.AnswerStage)
                expectedPackages = expectedPackages.Append(
                    talkPost.DadTalkToJonasPackage.FormId);
            if (cg02Butch.PostIntercomRuntime is { } playerPost &&
                stage >= playerPost.GoodbyeStage)
                expectedPackages = expectedPackages.Append(
                    playerPost.DadToPlayerPackage.FormId);
            if (cg02Butch.PostIntercomRuntime?.ReactorGiftRuntime is { } expectedGift &&
                stage >= expectedGift.JonasStage)
                expectedPackages = expectedPackages.Append(
                    expectedGift.JonasGreetPackageFormId);
            if (cg02Butch.PostIntercomRuntime?.ReactorGiftRuntime is { } targetGift &&
                stage >= targetGift.TargetStage)
                expectedPackages = expectedPackages.Append(
                    targetGift.DadGreetPackageFormId);
        }
        var expectedPackageArray = expectedPackages.ToArray();
        var timerRemaining = source.GetProperty("timerRemainingSeconds").GetDouble();
        var timerAdvancing = RequiredBoolean(source, "timerAdvancing");
        var imageSpaceElapsed = source.GetProperty("imageSpaceElapsedSeconds").GetDouble();
        var soundStarted = RequiredBoolean(source, "stage90SoundStarted");
        if (!double.IsFinite(timerRemaining) || timerRemaining < 0.0 ||
            !reachedNextQuest && timerAdvancing &&
                (!accepted || stage != Stage20Interaction.BookStage) ||
            !reachedNextQuest && progressStage >= Stage20Interaction.TimerTransition.TargetStage &&
                (timerAdvancing || timerRemaining != 0.0) ||
            reachedNextQuest && cg02Butch is not null &&
                (stage == cg02Butch.AggregateStage ||
                 stage == cg02Butch.SceneDoneStage) &&
                (!timerAdvancing || timerRemaining > cg02Butch.AggregateTimerSeconds) ||
            reachedNextQuest && cg02Butch is not null &&
                stage == cg02Butch.IntercomStage &&
                (timerAdvancing || timerRemaining != 0.0) ||
            reachedNextQuest && (cg02Butch is null ||
                stage != cg02Butch.AggregateStage &&
                stage != cg02Butch.SceneDoneStage &&
                stage != cg02Butch.IntercomStage &&
                (cg02Butch.PostIntercomRuntime is not { } timerPost ||
                 stage != timerPost.AnswerStage && stage != timerPost.GoodbyeStage &&
                 stage != timerPost.TargetStage &&
                 (timerPost.ReactorGiftRuntime is not { } timerGift ||
                  stage != timerGift.JonasStage && stage != timerGift.TargetStage))) &&
                (completion.Cg02Stage0.IntroRuntime is null
                ? timerAdvancing || timerRemaining != 0.0
                : timerRemaining > completion.Cg02Stage0.IntroRuntime.InitialSeconds ||
                  timerAdvancing != (timerRemaining > 0.0)) ||
            reachedNextQuest &&
                (!double.IsFinite(imageSpaceElapsed) ||
                 imageSpaceElapsed < completion.TimerInitialSeconds ||
                 imageSpaceElapsed > completion.ImageSpaceModifier.DurationSeconds ||
                 !soundStarted) ||
            !reachedNextQuest && (imageSpaceElapsed != 0.0 || soundStarted))
            throw new InvalidOperationException("Saved Fallout 3 CG01 timer state differs.");
        var dadInfoStateValid =
            savedInfoFormIds.Take(baselineInfoCount)
                .SequenceEqual(baseline.AppliedInfoFormIds) &&
            matchingCg02Sequence is not null &&
            (stage != cg02DadSpeech?.TargetStage ||
                savedCg02InfoFormIds.Length is >= 2 and <= 3) &&
            (cg02Overseer is null || stage != cg02Overseer.TargetStage ||
                savedCg02InfoFormIds.Length == matchingCg02Sequence.Length - 1) &&
            (cg02Party is null || stage != cg02Party.TargetStage ||
                savedCg02InfoFormIds.Length >= matchingCg02Sequence.Length) &&
            (cg02Birthday is null || !cg02Birthday.StageResults.ContainsKey(stage) ||
                savedCg02InfoFormIds.Skip(matchingCg02Sequence.Length)
                    .Any(birthdayInfoFormIds.Contains));
        if (RequiredString(source, "schema") != ExpectedSavedStateSchema ||
            (!reachedNextQuest && RequiredFormId(active, "formId") != baseline.ActiveQuestFormId) ||
            (!reachedNextQuest && RequiredString(active, "editorId") != baseline.ActiveQuestEditorId) ||
            !dadInfoStateValid ||
            !savedPackageFormIds.SequenceEqual(expectedPackageArray,
                StringComparer.OrdinalIgnoreCase) ||
            RequiredFormId(gate, "referenceFormId") != baseline.PlaypenGateReferenceFormId ||
            gateOpen != expectedGateOpen ||
            RequiredFormId(door, "referenceFormId") != baseline.PlayroomDoorReferenceFormId ||
            RequiredBoolean(door, "open") !=
                (progressStage >= Stage20Interaction.TimerTransition.CompletionStage) ||
            RequiredInteger(door, "lockLevel") !=
                (progressStage >= Stage20Interaction.TimerTransition.CompletionStage
                    ? 0
                    : baseline.PlayroomDoorLockLevel) ||
            !RequiredBoolean(source, "playerMovementEnabled") ||
            objective != expectedObjective ||
            RequiredInteger(source, "accountedCommandCount") != expectedCommandCount ||
            RequiredInteger(source, "appliedCommandCount") != expectedCommandCount ||
            RequiredBoolean(boundary, "applied") ||
            RequiredString(boundary, "blocker") !=
                (cg02Butch?.PostIntercomRuntime is { } boundaryPost &&
                 stage >= boundaryPost.TargetStage
                    ? boundaryPost.ReactorGiftRuntime is { } boundaryGift &&
                        stage == boundaryGift.TargetStage
                        ? boundaryGift.NextBoundaryBlocker
                        : boundaryPost.NextBoundaryBlocker
                    : baseline.NextBoundary.Blocker))
            throw new InvalidOperationException(
                "Saved Fallout 3 CG01 stage-20 state differs.");
        return baseline with
        {
            ActiveQuestFormId = reachedNextQuest ? completion.NextQuestFormId : baseline.ActiveQuestFormId,
            ActiveQuestEditorId = reachedNextQuest ? completion.NextQuestEditorId : baseline.ActiveQuestEditorId,
            ActiveStage = stage,
            PlaypenGateOpen = gateOpen,
            DisplayedObjectiveIndex = objective,
            AccountedCommandCount = expectedCommandCount,
            AppliedCommandCount = expectedCommandCount,
            AppliedInfoFormIds = savedInfoFormIds,
            AppliedPackageFormIds = expectedPackageArray,
            PlayroomDoorOpen = progressStage >= Stage20Interaction.TimerTransition.CompletionStage,
            PlayroomDoorLockLevel = progressStage >= Stage20Interaction.TimerTransition.CompletionStage
                ? 0
                : baseline.PlayroomDoorLockLevel,
            SpecialValues = values,
            SpecialBookAccepted = accepted,
            TimerRemainingSeconds = timerRemaining,
            TimerAdvancing = timerAdvancing,
            ImageSpaceElapsedSeconds = imageSpaceElapsed,
            Stage90SoundStarted = soundStarted,
            NextBoundary = new Fo3Cg01Stage12Boundary(
                false, RequiredString(boundary, "blocker")),
        };
    }

    private static Fo3Cg01PostStage14Package LoadPackage(
        JsonElement source,
        int? completionStage)
    {
        _ = RequiredSha256(source, "recordSha256");
        var target = RequiredObject(source, "target");
        if (RequiredString(target, "kind") != "referenceMarker")
            throw new InvalidOperationException(
                "Fallout 3 CG01 package target kind differs.");
        _ = RequiredSha256(target, "recordSha256");
        var actualCompletion = source.TryGetProperty("completionStage", out var completion)
            ? completion.GetInt32()
            : (int?)null;
        if (actualCompletion != completionStage)
            throw new InvalidOperationException(
                "Fallout 3 CG01 package completion stage differs.");
        var radius = RequiredInteger(target, "radiusGameUnits");
        if (radius < 0)
            throw new InvalidOperationException(
                "Fallout 3 CG01 package target radius differs.");
        return new Fo3Cg01PostStage14Package(
            RequiredFormId(source, "formId"),
            RequiredString(source, "editorId"),
            RequiredFormId(target, "formId"),
            Fo3Cg01Stage12Transition.LoadTransform(RequiredObject(target, "sourceTransform")),
            radius,
            completionStage);
    }

    private static Fo3Cg01PostStage14Cue LoadCue(
        JsonElement source,
        int sequence,
        Fo3Cg01Stage0Transition stage0)
    {
        if (RequiredInteger(source, "sequence") != sequence ||
            !RequiredBoolean(source, "sayOnce"))
            throw new InvalidOperationException("Fallout 3 CG01 stage-16 cue order differs.");
        var infoFormId = RequiredFormId(source, "infoFormId");
        _ = RequiredSha256(source, "recordSha256");
        string? engineSex = null;
        var conditions = RequiredArray(source, "conditions").EnumerateArray().ToArray();
        foreach (var condition in conditions)
        {
            var function = RequiredInteger(condition, "function");
            if (function == GetPcIsSexFunction)
                engineSex = Convert.ToUInt32(
                    RequiredFormId(condition, "parameter1"),
                    FormIdRadix) switch
                {
                    MaleSexValue => "male",
                    FemaleSexValue => "female",
                    _ => throw new InvalidOperationException(
                        "Fallout 3 CG01 stage-16 cue sex differs."),
                };
        }
        if (!conditions.Any(condition =>
                RequiredInteger(condition, "function") == GetIsIdFunction &&
                RequiredFormId(condition, "parameter1") == stage0.Dad.BaseFormId))
            throw new InvalidOperationException(
                "Fallout 3 CG01 stage-16 Dad cue identity differs.");
        var effects = RequiredArray(source, "effects").EnumerateArray().ToArray();
        if (sequence == 0)
        {
            if (effects.Length != 0 || engineSex is not null)
                throw new InvalidOperationException(
                    "Fallout 3 CG01 stage-16 opening cue differs.");
        }
        else if (effects.Length != 1 || engineSex is null ||
            RequiredString(effects[0], "kind") != "setScriptVariable" ||
            RequiredFormId(effects[0], "referenceFormId") != stage0.Dad.FormId ||
            RequiredString(effects[0], "variable") != "doTalk" ||
            RequiredInteger(effects[0], "value") != 0)
            throw new InvalidOperationException(
                "Fallout 3 CG01 stage-16 closing cue effect differs.");
        var response = RequiredObject(source, "response");
        var responseIndex = RequiredInteger(response, "index");
        var text = RequiredString(response, "text");
        var actualHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
        if (responseIndex != 1 || actualHash != RequiredSha256(response, "textSha256"))
            throw new InvalidOperationException(
                "Fallout 3 CG01 stage-16 response text differs.");
        var suffix = $"_{infoFormId}_{responseIndex}";
        return new Fo3Cg01PostStage14Cue(
            sequence,
            infoFormId,
            engineSex,
            new Fo3OwnedDialogueResponse(
                responseIndex,
                text,
                Fo3Cg01Stage10Transition.LoadDialogueAsset(
                    RequiredObject(response, "voice"), suffix + ".ogg"),
                Fo3Cg01Stage10Transition.LoadDialogueAsset(
                    RequiredObject(response, "lip"), suffix + ".lip")));
    }

    private static IReadOnlyList<CompiledCommand> LoadCommands(JsonElement source)
    {
        _ = RequiredSha256(source, "sourceSha256");
        return RequiredArray(source, "commands").EnumerateArray().Select((row, index) =>
        {
            if (RequiredInteger(row, "index") != index)
                throw new InvalidOperationException(
                    "Fallout 3 CG01 post-stage-14 command order differs.");
            var kind = RequiredString(row, "kind");
            var reference = row.TryGetProperty("referenceFormId", out var referenceValue)
                ? referenceValue.GetString() ?? ""
                : "";
            var value = row.TryGetProperty("stage", out var stageValue)
                ? stageValue.GetInt32()
                : row.TryGetProperty("objectiveIndex", out var objectiveValue)
                    ? objectiveValue.GetInt32()
                    : row.TryGetProperty("value", out var rawValue) && rawValue.TryGetInt32(out var integer)
                        ? integer
                        : 0;
            var arguments = row.TryGetProperty("arguments", out var rawArguments)
                ? rawArguments.EnumerateArray().Select(item => item.GetInt32()).ToArray()
                : Array.Empty<int>();
            return new CompiledCommand(kind, reference, value, arguments);
        }).ToArray();
    }

    private sealed record CompiledCommand(
        string Kind,
        string ReferenceFormId,
        int Value,
        IReadOnlyList<int> Arguments);

    private static JsonElement RequiredObject(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object
            ? value
            : throw new InvalidOperationException($"Fallout 3 CG01 field {name} is absent.");
    private static JsonElement RequiredArray(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? value
            : throw new InvalidOperationException($"Fallout 3 CG01 field {name} is absent.");
    private static string RequiredString(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!
            : throw new InvalidOperationException($"Fallout 3 CG01 field {name} is absent.");
    private static int RequiredInteger(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.TryGetInt32(out var result)
            ? result
            : throw new InvalidOperationException($"Fallout 3 CG01 field {name} is invalid.");
    private static bool RequiredBoolean(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) &&
        value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : throw new InvalidOperationException($"Fallout 3 CG01 field {name} is invalid.");
    private static string RequiredFormId(JsonElement parent, string name)
    {
        var value = RequiredString(parent, name);
        if (value.Length != Fo3OpeningFlowNumericContracts.FormIdHexCharacters ||
            !value.All(Uri.IsHexDigit))
            throw new InvalidOperationException($"Fallout 3 CG01 field {name} is invalid.");
        return value.ToLowerInvariant();
    }
    private static string RequiredSha256(JsonElement parent, string name)
    {
        var value = RequiredString(parent, name);
        if (value.Length != Fo3OpeningFlowNumericContracts.Sha256HexCharacters ||
            !value.All(Uri.IsHexDigit))
            throw new InvalidOperationException($"Fallout 3 CG01 field {name} is invalid.");
        return value.ToLowerInvariant();
    }
}
