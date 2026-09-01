using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Presentation.Ui;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Campaigns.Fallout3;

internal partial class Fo3OpeningFlow
{
    private void StartCg02BirthdayInteraction(
        Fo3Cg02BirthdayParticipant participant,
        Fo3Cg01ToddlerPlayer player,
        Action<string, int?> completed)
    {
        var actor = EnsureCg02BirthdayActor(participant);
        var engineSex = (_selectedSex ?? throw new InvalidOperationException(
            "Fallout 3 CG02 birthday player sex is absent.")).EngineSex;
        var greetingCandidates = participant.GreetingInfoFormIds
            .Select(formId => participant.Nodes[formId])
            .Where(node => (node.EngineSex is null || node.EngineSex == engineSex) &&
                Cg02BirthdayConditionsMatch(node, actor, player)).ToArray();
        var greetingPriority = greetingCandidates.Select(Cg02GreetingStagePriority).Max();
        var greeting = greetingCandidates.Single(node =>
            Cg02GreetingStagePriority(node) == greetingPriority);
        var subtitle = AddVaultDialogueOverlay(
            $"FO3_CG02_BIRTHDAY_{participant.ReferenceFormId}");
        var menu = subtitle.GetParent() as OwnedGamebryoDialogueMenuRuntime ??
            throw new InvalidOperationException(
                "Fallout 3 CG02 birthday DialogueMenu owner is absent.");
        player.SetMenuInputHandler(_ => false);
        PlayNode(greeting);

        void PlayNode(Fo3Cg02BirthdayDialogueNode node)
        {
            var responses = node.ResponseIndexes
                .Select(index => participant.Lines[$"{node.InfoFormId}:{index}"])
                .ToArray();
            GamebryoDialoguePlayback.ValidateOrderedLines(responses.Select(response =>
                new SourceDialogueLine(node.InfoFormId, response.Index,
                    participant.BaseFormId, response.Text,
                    new SourceDialogueAsset(response.Voice.LogicalPath,
                        response.Voice.SourcePath, response.Voice.Sha256),
                    new SourceDialogueAsset(response.Lip.LogicalPath,
                        response.Lip.SourcePath, response.Lip.Sha256))).ToArray());
            PlayLine(0);

            void PlayLine(int index)
            {
                if (index == responses.Length)
                {
                    var targetStage = node.Effects.Where(effect =>
                            effect.Kind == "setStage")
                        .Select(effect => (int?)effect.Stage).SingleOrDefault();
                    ApplyEffects(node.Effects, actor, player);
                    completed(node.InfoFormId, targetStage);
                    if (node.LinkedTopicFormIds.Count == 0)
                    {
                        menu.HideMenu();
                        player.SetMenuInputHandler(null);
                        menu.QueueFree();
                        _vaultPreviewOverlay = null;
                        return;
                    }
                    menu.ShowTopics(participant.DisplayName,
                        node.LinkedTopicFormIds.SelectMany(topicFormId =>
                        {
                            var topic = participant.Topics[topicFormId];
                            return participant.Nodes.Values.Where(value =>
                                value.TopicFormId.Equals(topicFormId,
                                    StringComparison.OrdinalIgnoreCase) &&
                                (value.EngineSex is null || value.EngineSex == engineSex) &&
                                Cg02BirthdayConditionsMatch(value, actor, player))
                                .Select(next => (topic.FormId, topic.Text,
                                    (Action)(() => PlayNode(next))));
                        }).ToArray());
                    return;
                }
                var response = responses[index];
                menu.ShowLine(participant.DisplayName, response.Text, () => { });
                var voice = new AudioStreamPlayer
                {
                    Name = $"Fallout3Cg02BirthdayVoice{node.InfoFormId}_{response.Index}",
                };
                AddChild(voice);
                var dialogue = new GamebryoDialoguePlayback(
                    voice, _runtimeConfiguration.ActorCompiler.FaceGenAnimation.Lip);
                _cg02IntroDialogue.Add(dialogue);
                dialogue.Start(new SourceDialogueLine(node.InfoFormId, response.Index,
                        participant.BaseFormId, response.Text,
                        new SourceDialogueAsset(response.Voice.LogicalPath,
                            response.Voice.SourcePath, response.Voice.Sha256),
                        new SourceDialogueAsset(response.Lip.LogicalPath,
                            response.Lip.SourcePath, response.Lip.Sha256)),
                    new FaceGenMorphController(actor.Actor,
                        _runtimeConfiguration.ActorCompiler.FaceGenAnimation.Lip),
                    () => PlayLine(index + 1));
            }
        }
    }

    private void ApplyEffects(
        IReadOnlyList<Fo3Cg02BirthdayEffect> effects,
        CellActorLoader.PlacedActor actor,
        Fo3Cg01ToddlerPlayer player)
    {
        foreach (var effect in effects)
        {
            if (effect.Kind == "setTimer")
                player.SetMeta("opennv_cg02_timer", effect.Seconds);
            else if (effect.Kind == "setQuestVariable")
            {
                player.SetMeta($"opennv_cg02_{effect.Variable.ToLowerInvariant()}",
                    effect.Value);
                player.SetMeta($"opennv_cg02_quest_variable_{effect.Variable}",
                    effect.Value);
            }
            else if (effect.Kind == "setActorVariable")
                _cg02IntroActors[effect.ReferenceFormId].Placement.SetMeta(
                    $"opennv_{effect.Variable.ToLowerInvariant()}", effect.Value);
            else if (effect.Kind == "evaluatePackage")
                (_cg02IntroActors.TryGetValue(effect.ReferenceFormId,
                    out var packageActor) ? packageActor.Placement :
                    Cg01WorldReference(effect.ReferenceFormId))
                    .SetMeta("opennv_evaluate_package", 1);
            else if (effect.Kind == "removeItem")
            {
                var key = $"opennv_cg02_item_{effect.FormId}";
                var remaining = player.GetMeta(key, 0).AsInt32() - effect.Count;
                if (remaining < 0)
                    throw new InvalidOperationException(
                        "Fallout 3 CG02 removed item count differs.");
                player.SetMeta(key, remaining);
            }
            else if (effect.Kind == "startCombat")
            {
                actor.Placement.SetMeta("opennv_combat_target", effect.Target);
                (_cg02IntroActors.TryGetValue(effect.ReferenceFormId,
                    out var responder) ? responder.Placement :
                    Cg01WorldReference(effect.ReferenceFormId))
                    .SetMeta("opennv_evaluate_package", 1);
                player.SetMeta("opennv_cg02_combat_runtime_blocker",
                    "fo3-cg02-butch-combat-runtime-not-implemented");
            }
        }
    }

    private void StartCg02DadPartyRuntime(
        Fo3Cg02DadPartyRuntime party,
        Fo3Cg01ToddlerPlayer player,
        IReadOnlyCollection<string> appliedInfoFormIds,
        Action<string, int> completed)
    {
        if (!party.ArrivedAtStart ||
            party.InitialDistanceGameUnits > party.PackageRadiusGameUnits)
            throw new InvalidOperationException(
                "Fallout 3 CG02 Dad party package requires unimplemented travel.");
        if (appliedInfoFormIds.Contains(
                party.Cue.InfoFormId, StringComparer.OrdinalIgnoreCase))
            return;
        var dad = _cg02IntroActors[party.DadReferenceFormId];
        dad.Placement.SetMeta("opennv_active_package_form_id", party.PackageFormId);
        var animation = dad.Actor.LoadedAnimations.Single(value =>
            ActorModelSlice.NormalizeAnimationPath(value.LogicalPath).Equals(
                ActorModelSlice.NormalizeAnimationPath(
                    party.Cue.SpeakerIdleLogicalPath),
                StringComparison.OrdinalIgnoreCase) &&
            value.SourceSha256.Equals(
                party.Cue.SpeakerIdleSourceSha256,
                StringComparison.OrdinalIgnoreCase));
        _cg02IntroAnimations[party.DadReferenceFormId] =
            ActorAnimationPlayback.Start(dad.Actor, animation);
        var voice = new AudioStreamPlayer { Name = "Fallout3Cg02DadPartyVoice" };
        AddChild(voice);
        var dialogue = new GamebryoDialoguePlayback(
            voice, _runtimeConfiguration.ActorCompiler.FaceGenAnimation.Lip);
        _cg02IntroDialogue.Add(dialogue);
        dialogue.Start(
            new SourceDialogueLine(
                party.Cue.InfoFormId, party.Cue.Response.Index,
                party.DadReferenceFormId, party.Cue.Response.Text,
                new SourceDialogueAsset(
                    party.Cue.Response.Voice.LogicalPath,
                    party.Cue.Response.Voice.SourcePath,
                    party.Cue.Response.Voice.Sha256),
                new SourceDialogueAsset(
                    party.Cue.Response.Lip.LogicalPath,
                    party.Cue.Response.Lip.SourcePath,
                    party.Cue.Response.Lip.Sha256)),
            new FaceGenMorphController(
                dad.Actor, _runtimeConfiguration.ActorCompiler.FaceGenAnimation.Lip),
            () =>
            {
                foreach (var command in party.StageCommands)
                {
                    switch (command.Kind)
                    {
                        case "enablePlayerControls":
                            player.SetMeta("opennv_cg02_enabled_controls",
                                JsonSerializer.Serialize(command.Arguments));
                            break;
                        case "autosave":
                            player.SetMeta("opennv_autosave", 1);
                            break;
                        case "setObjectiveDisplayed":
                            player.SetMeta("opennv_displayed_objective", command.Value);
                            break;
                        case "enable":
                            Cg01WorldReference(command.ReferenceFormId)
                                .SetMeta("opennv_enabled", 1);
                            break;
                        case "evaluatePackage":
                            (_cg02IntroActors.TryGetValue(command.ReferenceFormId,
                                out var actor) ? actor.Placement :
                                Cg01WorldReference(command.ReferenceFormId))
                                .SetMeta("opennv_evaluate_package", 1);
                            break;
                        case "setStage":
                            player.SetMeta("opennv_tutorial_stage", command.Value);
                            break;
                        case "forceRadioStationUpdate":
                            player.SetMeta("opennv_force_radio_station_update", 1);
                            break;
                        default:
                            throw new InvalidOperationException(
                                $"Fallout 3 CG02 Dad party command is unsupported: {command.Kind}");
                    }
                }
                player.SetMeta("opennv_cg02_stage", party.TargetStage);
                completed(party.Cue.InfoFormId, party.StageCommands.Count + 1);
            });
    }

    private CellActorLoader.PlacedActor EnsureCg02BirthdayActor(
        Fo3Cg02BirthdayParticipant participant)
    {
        if (_cg02IntroActors.TryGetValue(participant.ReferenceFormId, out var existing))
            return existing;
        if (_vaultBirthCoverage?.Cg01DadActor is { } dad &&
            dad.ReferenceFormId.Equals(
                participant.ReferenceFormId, StringComparison.OrdinalIgnoreCase) &&
            dad.BaseFormId.Equals(
                participant.BaseFormId, StringComparison.OrdinalIgnoreCase))
            return dad;
        if (participant.ActorScenePath is null || participant.ActorSceneSha256 is null)
            throw new InvalidOperationException(
                "Fallout 3 CG02 birthday actor scene is absent.");
        using var stream = File.OpenRead(participant.ActorScenePath);
        var actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        if (!actual.Equals(participant.ActorSceneSha256,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Fallout 3 CG02 birthday actor scene hash differs.");
        var coverage = _vaultBirthCoverage ?? throw new InvalidOperationException(
            "Fallout 3 CG02 birthday world is absent.");
        var actor = CellActorLoader.Load(
                participant.ActorScenePath,
                new HashSet<string>([coverage.Contract.CellFormId],
                    StringComparer.OrdinalIgnoreCase),
                coverage.CellRoot,
                coverage.Contract.EntryPositionGameUnits,
                _runtimeConfiguration,
                proofEnableInitiallyDisabled: false,
                materializeInitiallyDisabled: true)
            ?? throw new InvalidOperationException(
                "Fallout 3 CG02 birthday actor is absent.");
        if (actor.ReferenceFormId != participant.ReferenceFormId ||
            actor.BaseFormId != participant.BaseFormId)
            throw new InvalidOperationException(
                "Fallout 3 CG02 birthday actor identity differs.");
        _cg02IntroActors.Add(actor.ReferenceFormId, actor);
        return actor;
    }

    private static bool Cg02BirthdayConditionsMatch(
        Fo3Cg02BirthdayDialogueNode node,
        CellActorLoader.PlacedActor actor,
        Fo3Cg01ToddlerPlayer player)
    {
        foreach (var condition in node.Conditions)
        {
            double actual = condition.Function switch
            {
                Fo3OpeningFlowNumericContracts.DialogueConditionGetDistance =>
                    actor.Placement.GlobalPosition.DistanceTo(player.GlobalPosition),
                Fo3OpeningFlowNumericContracts.DialogueConditionGetItemCount =>
                    player.GetMeta($"opennv_cg02_item_{condition.Parameter1:x8}", 0)
                        .AsInt32(),
                Fo3OpeningFlowNumericContracts.DialogueConditionGetQuestVariable =>
                    player.GetMeta(
                        $"opennv_cg02_quest_variable_{condition.Parameter2}", 0)
                        .AsInt32(),
                Fo3OpeningFlowNumericContracts.DialogueConditionGetStage =>
                    player.GetMeta(
                        $"opennv_quest_stage_{condition.Parameter1:x8}")
                        .AsInt32(),
                Fo3OpeningFlowNumericContracts.DialogueConditionGetIsId =>
                    actor.BaseFormId.Equals($"{condition.Parameter1:x8}",
                        StringComparison.OrdinalIgnoreCase) ? 1.0 : 0.0,
                Fo3OpeningFlowNumericContracts.DialogueConditionGetIsCurrentPackage =>
                    actor.Placement.GetMeta("opennv_active_package_form_id", "")
                        .AsString().Equals($"{condition.Parameter1:x8}",
                            StringComparison.OrdinalIgnoreCase) ? 1.0 : 0.0,
                _ => throw new InvalidOperationException(
                    $"Fallout 3 CG02 dialogue condition is unsupported: " +
                    condition.Function),
            };
            var matched = condition.OperatorFlags switch
            {
                Fo3OpeningFlowNumericContracts.DialogueConditionEqual =>
                    actual == condition.ComparisonValue,
                Fo3OpeningFlowNumericContracts.DialogueConditionGreaterThan =>
                    actual > condition.ComparisonValue,
                Fo3OpeningFlowNumericContracts.DialogueConditionGreaterThanOrEqual =>
                    actual >= condition.ComparisonValue,
                Fo3OpeningFlowNumericContracts.DialogueConditionLessThan =>
                    actual < condition.ComparisonValue,
                Fo3OpeningFlowNumericContracts.DialogueConditionLessThanOrEqual =>
                    actual <= condition.ComparisonValue,
                _ => throw new InvalidOperationException(
                    "Fallout 3 CG02 dialogue condition comparison is unsupported."),
            };
            if (!matched)
                return false;
        }
        return true;
    }

    private static int Cg02GreetingStagePriority(
        Fo3Cg02BirthdayDialogueNode node) => node.Conditions
        .Where(condition =>
            condition.Function == Fo3OpeningFlowNumericContracts.DialogueConditionGetStage &&
            condition.OperatorFlags is
                Fo3OpeningFlowNumericContracts.DialogueConditionGreaterThan or
                Fo3OpeningFlowNumericContracts.DialogueConditionGreaterThanOrEqual)
        .Select(condition => (int)condition.ComparisonValue)
        .DefaultIfEmpty(int.MinValue)
        .Max();
}
