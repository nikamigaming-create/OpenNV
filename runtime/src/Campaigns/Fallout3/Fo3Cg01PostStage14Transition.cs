using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Presentation.Ui;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Campaigns.Fallout3;

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
            [],
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
            [],
            0.0,
            false,
            0.0,
            false,
            false,
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
        cg02TargetHitFormIds = state.Cg02TargetHitFormIds,
        combatHealthByReferenceFormId = state.CombatHealthByReferenceFormId,
        deadCombatReferenceFormIds = state.DeadCombatReferenceFormIds,
        imageSpaceElapsedSeconds = state.ImageSpaceElapsedSeconds,
        stage90SoundStarted = state.Stage90SoundStarted,
        cg02PictureImageSpaceElapsedSeconds =
            state.Cg02PictureImageSpaceElapsedSeconds,
        cg02PictureSoundStarted = state.Cg02PictureSoundStarted,
        cg02SkillBookTransferred = state.Cg02SkillBookTransferred,
        cg02AdultVaultSuitEquipped = state.Cg02AdultVaultSuitEquipped,
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
        var cg02Completion = cg02Butch?.PostIntercomRuntime?.ReactorGiftRuntime?
            .PictureRuntime.CompletionRuntime;
        var cg03 = cg02Completion?.NextQuestRuntime;
        var savedInfoFormIds = RequiredArray(source, "appliedInfoFormIds")
            .EnumerateArray().Select(value => value.GetString() ?? "").ToArray();
        var baselineInfoCount = baseline.AppliedInfoFormIds.Count;
        var savedCg02InfoFormIds = savedInfoFormIds.Skip(baselineInfoCount).ToArray();
        var savedPackageFormIds = RequiredArray(source, "appliedPackageFormIds")
            .EnumerateArray().Select(value => value.GetString() ?? "").ToArray();
        var savedTargetHitFormIds = RequiredArray(source, "cg02TargetHitFormIds")
            .EnumerateArray().Select(value => value.GetString() ?? "").ToArray();
        var savedCombatHealth = RequiredObject(source,
                "combatHealthByReferenceFormId").EnumerateObject()
            .ToDictionary(value => value.Name, value => value.Value.GetInt32(),
                StringComparer.OrdinalIgnoreCase);
        var savedDeadCombatReferences = RequiredArray(source,
                "deadCombatReferenceFormIds").EnumerateArray()
            .Select(value => value.GetString() ?? "").ToArray();
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
        if (cg03 is not null)
            birthdayInfoFormIds.UnionWith(
                cg03.Cues.Select(value => value.InfoFormId));
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
                    (stage == gift.JonasStage || stage == gift.TargetStage ||
                     stage == gift.RangeStage || stage == gift.HitStage ||
                     stage == gift.CombatStage || stage == gift.DeathStage ||
                     stage == gift.CompletionStage ||
                     stage == gift.PictureRuntime.PictureStage ||
                     stage == gift.PictureRuntime.TimerStage ||
                     stage == gift.PictureRuntime.CompletionRuntime.FlashStage)));
        var cg02DadComplete = cg02DadSpeech is not null &&
            savedCg02InfoFormIds.Length >= 2;
        var reachedCg03 = cg02Completion is not null &&
            RequiredFormId(active, "formId") == cg02Completion.NextQuestFormId &&
            RequiredString(active, "editorId") == cg02Completion.NextQuestEditorId &&
            (stage == cg02Completion.NextQuestTargetStage ||
             cg03 is not null && stage == cg03.SpeechStage);
        var reachedNextQuest = reachedCg03 ||
            RequiredFormId(active, "formId") == completion.NextQuestFormId &&
            RequiredString(active, "editorId") == completion.NextQuestEditorId &&
            (stage == completion.Cg02Stage0.TargetStage || cg02IntroComplete);
        var commandStage = reachedCg03 ? cg02Completion!.CompletionStage : stage;
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
            ? cg02Butch?.PostIntercomRuntime?.ReactorGiftRuntime is { } objectiveGift &&
                commandStage >= objectiveGift.PictureRuntime.SourceStage
                ? objectiveGift.PictureRuntime.ObjectiveIndex
                : cg02Butch?.PostIntercomRuntime?.ReactorGiftRuntime is { } objectiveGiftRange &&
                commandStage >= objectiveGiftRange.RangeStage
                ? objectiveGiftRange.StageResults
                    .Where(value => value.Key <= commandStage)
                    .SelectMany(value => value.Value)
                    .Where(value => value.Kind == "setObjectiveDisplayed" &&
                        value.Value != 0)
                    .Select(value => value.ObjectiveIndex).Last()
                : cg02Butch?.PostIntercomRuntime is { } objectivePost &&
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
                        if (commandStage >= commandGift.JonasStage)
                            interactionCommandCount += commandGift.StageResults[
                                commandGift.JonasStage].Count;
                        if (commandStage >= commandGift.TargetStage)
                            interactionCommandCount += commandGift.StageResults[
                                commandGift.TargetStage].Count;
                        if (commandStage >= commandGift.RangeStage)
                            interactionCommandCount += commandGift.StageResults[
                                commandGift.RangeStage].Count;
                        if (commandStage >= commandGift.HitStage)
                            interactionCommandCount += commandGift.StageResults[
                                commandGift.HitStage].Count;
                        if (commandStage >= commandGift.CombatStage)
                            interactionCommandCount += commandGift.StageResults[
                                commandGift.CombatStage].Count;
                        if (commandStage >= commandGift.DeathStage)
                            interactionCommandCount += commandGift.StageResults[
                                commandGift.DeathStage].Count + 1;
                        var commandPicture = commandGift.PictureRuntime;
                        if (commandStage >= commandPicture.SourceStage)
                            interactionCommandCount +=
                                commandPicture.SourceStageCommandCount;
                        if (commandStage >= commandPicture.PictureStage)
                            interactionCommandCount +=
                                commandPicture.PictureStageCommandCount;
                        if (savedCg02InfoFormIds.Contains(
                                commandPicture.JonasInfoFormId,
                                StringComparer.OrdinalIgnoreCase))
                            interactionCommandCount += 1 + commandPicture
                                .CompletionRuntime.Stage95CommandCount;
                        if (commandStage >= commandPicture.CompletionRuntime.FlashStage)
                            interactionCommandCount += commandPicture
                                .CompletionRuntime.Stage98CommandCount;
                        if (reachedCg03)
                            interactionCommandCount += commandPicture.CompletionRuntime
                                .Stage100CommandCount + commandPicture.CompletionRuntime
                                .NextQuestStage0CommandCount;
                        if (reachedCg03 && cg03 is not null &&
                            savedPackageFormIds.Contains(
                                cg03.DadHoldPackageFormId,
                                StringComparer.OrdinalIgnoreCase))
                            interactionCommandCount += cg03.Stage5CommandCount;
                        if (reachedCg03 && cg03 is not null &&
                            stage == cg03.SpeechStage)
                            interactionCommandCount += cg03.Stage6CommandCount + 1;
                        interactionCommandCount += commandPicture.Packages
                            .Where(value => savedPackageFormIds.Contains(
                                value.FormId, StringComparer.OrdinalIgnoreCase))
                            .Sum(value => value.CompletionCommandCount);
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
                commandStage >= expectedPost.SourceStage)
                expectedPackages = expectedPackages.Append(
                    expectedPost.DadToIntercomPackage.FormId);
            if (cg02Butch.PostIntercomRuntime is { } talkPost &&
                commandStage >= talkPost.AnswerStage)
                expectedPackages = expectedPackages.Append(
                    talkPost.DadTalkToJonasPackage.FormId);
            if (cg02Butch.PostIntercomRuntime is { } playerPost &&
                commandStage >= playerPost.GoodbyeStage)
                expectedPackages = expectedPackages.Append(
                    playerPost.DadToPlayerPackage.FormId);
            if (cg02Butch.PostIntercomRuntime?.ReactorGiftRuntime is { } expectedGift &&
                commandStage >= expectedGift.JonasStage)
                expectedPackages = expectedPackages.Append(
                    expectedGift.JonasGreetPackageFormId);
            if (cg02Butch.PostIntercomRuntime?.ReactorGiftRuntime is { } targetGift &&
                commandStage >= targetGift.TargetStage)
                expectedPackages = expectedPackages.Append(
                    targetGift.DadGreetPackageFormId);
            if (cg02Butch.PostIntercomRuntime?.ReactorGiftRuntime is { } rangeGift &&
                commandStage >= rangeGift.TargetStage)
                expectedPackages = expectedPackages
                    .Append(rangeGift.DadToRangePackageFormId)
                    .Append(rangeGift.JonasWaitPackageFormId);
            if (cg02Butch.PostIntercomRuntime?.ReactorGiftRuntime is { } waitGift &&
                commandStage >= waitGift.RangeStage)
                expectedPackages = expectedPackages.Append(
                    waitGift.DadWaitPackageFormId);
            if (cg02Butch.PostIntercomRuntime?.ReactorGiftRuntime is { } combatGift &&
                commandStage >= combatGift.CombatStage)
                expectedPackages = expectedPackages.Append(
                    combatGift.Combatant.PackageFormId);
            if (cg02Butch.PostIntercomRuntime?.ReactorGiftRuntime?.PictureRuntime
                    is { } expectedPicture)
                foreach (var package in expectedPicture.Packages.Where(value =>
                    savedPackageFormIds.Contains(value.FormId,
                        StringComparer.OrdinalIgnoreCase)))
                {
                    if (savedPackageFormIds.Count(saved => saved.Equals(
                            package.FormId, StringComparison.OrdinalIgnoreCase)) != 1)
                        throw new InvalidOperationException(
                            "Saved Fallout 3 CG02 picture package count differs.");
                    expectedPackages = expectedPackages.Append(package.FormId);
                }
            if (reachedCg03 && cg03 is not null)
            {
                expectedPackages = expectedPackages.Append(
                    cg03.DadHoldPackageFormId);
                if (stage == cg03.SpeechStage)
                    expectedPackages = expectedPackages.Append(
                        cg03.DadTalkPackageFormId);
            }
        }
        var expectedPackageArray = expectedPackages.ToArray();
        var timerRemaining = source.GetProperty("timerRemainingSeconds").GetDouble();
        var timerAdvancing = RequiredBoolean(source, "timerAdvancing");
        var imageSpaceElapsed = source.GetProperty("imageSpaceElapsedSeconds").GetDouble();
        var soundStarted = RequiredBoolean(source, "stage90SoundStarted");
        var pictureImageSpaceElapsed = source.GetProperty(
            "cg02PictureImageSpaceElapsedSeconds").GetDouble();
        var pictureSoundStarted = RequiredBoolean(
            source, "cg02PictureSoundStarted");
        var skillBookTransferred = RequiredBoolean(
            source, "cg02SkillBookTransferred");
        var adultVaultSuitEquipped = RequiredBoolean(
            source, "cg02AdultVaultSuitEquipped");
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
            reachedNextQuest && !reachedCg03 && (cg02Butch is null ||
                stage != cg02Butch.AggregateStage &&
                stage != cg02Butch.SceneDoneStage &&
                stage != cg02Butch.IntercomStage &&
                (cg02Butch.PostIntercomRuntime is not { } timerPost ||
                 stage != timerPost.AnswerStage && stage != timerPost.GoodbyeStage &&
                 stage != timerPost.TargetStage &&
                 (timerPost.ReactorGiftRuntime is not { } timerGift ||
                  stage != timerGift.JonasStage && stage != timerGift.TargetStage &&
                  stage != timerGift.RangeStage && stage != timerGift.HitStage &&
                  stage != timerGift.CombatStage && stage != timerGift.DeathStage &&
                  stage != timerGift.CompletionStage &&
                  stage != timerGift.PictureRuntime.TimerStage &&
                  stage != timerGift.PictureRuntime.CompletionRuntime.FlashStage))) &&
                (completion.Cg02Stage0.IntroRuntime is null
                ? timerAdvancing || timerRemaining != 0.0
                : timerRemaining > completion.Cg02Stage0.IntroRuntime.InitialSeconds ||
                  timerAdvancing != (timerRemaining > 0.0)) ||
            reachedCg03 && cg03 is not null &&
                (stage == cg03.SourceStage
                    ? timerRemaining > cg03.TimerSeconds ||
                      timerAdvancing != (timerRemaining > 0.0)
                    : timerAdvancing || timerRemaining != 0.0) ||
            reachedNextQuest &&
                (!double.IsFinite(imageSpaceElapsed) ||
                 imageSpaceElapsed < completion.TimerInitialSeconds ||
                 imageSpaceElapsed > completion.ImageSpaceModifier.DurationSeconds ||
                 !soundStarted) ||
            !reachedNextQuest && (imageSpaceElapsed != 0.0 || soundStarted))
            throw new InvalidOperationException("Saved Fallout 3 CG01 timer state differs.");
        if (!double.IsFinite(pictureImageSpaceElapsed) ||
            pictureImageSpaceElapsed < 0.0 ||
            cg02Butch?.PostIntercomRuntime?.ReactorGiftRuntime?.PictureRuntime
                is { } timerPicture &&
            (reachedCg03
                ? (cg03 is null || stage != cg03.SourceStage) &&
                    (timerAdvancing || timerRemaining != 0.0) ||
                  pictureImageSpaceElapsed != timerPicture.CompletionRuntime
                    .ImageSpaceModifier.DurationSeconds ||
                  !pictureSoundStarted || !adultVaultSuitEquipped
                : stage == timerPicture.TimerStage &&
                (!timerAdvancing || timerRemaining > timerPicture
                    .CompletionRuntime.Stage95TimerSeconds ||
                 pictureImageSpaceElapsed != 0.0 || pictureSoundStarted) ||
             stage == timerPicture.CompletionRuntime.FlashStage &&
                (!timerAdvancing || timerRemaining > timerPicture
                    .CompletionRuntime.Stage98TimerSeconds ||
                 pictureImageSpaceElapsed > timerPicture.CompletionRuntime
                    .ImageSpaceModifier.DurationSeconds || !pictureSoundStarted) ||
             stage < timerPicture.TimerStage &&
                (pictureImageSpaceElapsed != 0.0 || pictureSoundStarted) ||
             !reachedCg03 &&
                (skillBookTransferred || adultVaultSuitEquipped)))
            throw new InvalidOperationException(
                "Saved Fallout 3 CG02 picture timer state differs.");
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
            cg02Butch?.PostIntercomRuntime?.ReactorGiftRuntime is { } hitGift &&
                (savedTargetHitFormIds.Length > hitGift.RequiredHitCount ||
                 savedTargetHitFormIds.Any(value =>
                     !hitGift.TargetReferenceFormIds.Contains(value,
                         StringComparer.OrdinalIgnoreCase)) ||
                 commandStage < hitGift.RangeStage && savedTargetHitFormIds.Length != 0 ||
                 commandStage == hitGift.RangeStage &&
                    savedTargetHitFormIds.Length >= hitGift.RequiredHitCount ||
                 commandStage >= hitGift.HitStage &&
                    savedTargetHitFormIds.Length != hitGift.RequiredHitCount) ||
            cg02Butch?.PostIntercomRuntime?.ReactorGiftRuntime is { } combatState &&
                (commandStage < combatState.CombatStage &&
                    (savedCombatHealth.Count != 0 ||
                     savedDeadCombatReferences.Length != 0) ||
                 commandStage == combatState.CombatStage &&
                    (savedCombatHealth.Count != 1 ||
                     !savedCombatHealth.TryGetValue(
                         combatState.Combatant.ReferenceFormId, out var health) ||
                     health <= 0 || health > combatState.Combatant.MaximumHealth ||
                     savedDeadCombatReferences.Length != 0) ||
                 commandStage >= combatState.DeathStage &&
                    (savedCombatHealth.Count != 1 ||
                     !savedCombatHealth.TryGetValue(
                         combatState.Combatant.ReferenceFormId, out health) ||
                     health != 0 ||
                     !savedDeadCombatReferences.SequenceEqual(
                         [combatState.Combatant.ReferenceFormId],
                         StringComparer.OrdinalIgnoreCase))) ||
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
            RequiredBoolean(source, "playerMovementEnabled") !=
                (!reachedCg03 &&
                 (cg02Butch?.PostIntercomRuntime?.ReactorGiftRuntime?.PictureRuntime
                    is not { } movementPicture || commandStage < movementPicture.PictureStage)) ||
            objective != expectedObjective ||
            RequiredInteger(source, "accountedCommandCount") != expectedCommandCount ||
            RequiredInteger(source, "appliedCommandCount") != expectedCommandCount ||
            RequiredBoolean(boundary, "applied") ||
            RequiredString(boundary, "blocker") !=
                (cg02Butch?.PostIntercomRuntime is { } boundaryPost &&
                 commandStage >= boundaryPost.TargetStage
                    ? boundaryPost.ReactorGiftRuntime is { } boundaryGift &&
                        commandStage >= boundaryGift.PictureRuntime.SourceStage
                        ? boundaryGift.PictureRuntime.NextBoundaryBlocker
                        : boundaryPost.ReactorGiftRuntime is { } completedGift &&
                            stage == completedGift.CompletionStage
                            ? completedGift.NextBoundaryBlocker
                            : boundaryPost.NextBoundaryBlocker
                    : baseline.NextBoundary.Blocker))
            throw new InvalidOperationException(
                "Saved Fallout 3 CG01 stage-20 state differs.");
        return baseline with
        {
            ActiveQuestFormId = reachedCg03
                ? cg02Completion!.NextQuestFormId
                : reachedNextQuest ? completion.NextQuestFormId : baseline.ActiveQuestFormId,
            ActiveQuestEditorId = reachedCg03
                ? cg02Completion!.NextQuestEditorId
                : reachedNextQuest ? completion.NextQuestEditorId : baseline.ActiveQuestEditorId,
            ActiveStage = stage,
            PlayerMovementEnabled = cg02Butch?.PostIntercomRuntime?
                .ReactorGiftRuntime?.PictureRuntime is not { } restoredPicture ||
                stage < restoredPicture.PictureStage,
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
            Cg02TargetHitFormIds = savedTargetHitFormIds,
            CombatHealthByReferenceFormId = savedCombatHealth,
            DeadCombatReferenceFormIds = savedDeadCombatReferences,
            ImageSpaceElapsedSeconds = imageSpaceElapsed,
            Stage90SoundStarted = soundStarted,
            Cg02PictureImageSpaceElapsedSeconds = pictureImageSpaceElapsed,
            Cg02PictureSoundStarted = pictureSoundStarted,
            Cg02SkillBookTransferred = skillBookTransferred,
            Cg02AdultVaultSuitEquipped = adultVaultSuitEquipped,
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
