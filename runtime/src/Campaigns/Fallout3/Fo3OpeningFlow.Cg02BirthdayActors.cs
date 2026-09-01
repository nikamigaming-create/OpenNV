using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Campaigns.Fallout3;

internal partial class Fo3OpeningFlow
{
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
