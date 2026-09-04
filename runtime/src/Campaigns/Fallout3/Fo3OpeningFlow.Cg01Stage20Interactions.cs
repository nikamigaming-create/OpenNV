using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Campaigns.NewVegas.Opening;
using OpenNV.Runtime.Presentation.CharacterCreation;
using OpenNV.Runtime.Presentation.Ui;


using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;
using OpenNV.Runtime.World.Interactions;

namespace OpenNV.Runtime.Campaigns.Fallout3;


internal partial class Fo3OpeningFlow
{
    private void InstallCg01Stage20Interactions(
        Fo3Cg01RuntimeContext context,
        Fo3Cg01Stage0State stage5,
        Fo3Cg01Stage10State stage10,
        Fo3Cg01Stage12State stage12,
        Fo3Cg01ToddlerWorldState toddlerWorld,
        Fo3Cg01Stage14State stage14,
        Fo3Cg01Stage20State initial)
    {
        var current = initial;
        var interaction = _profile.Cg01PostStage14Transition.Stage20Interaction;
        void Persist() => PersistCg01Stage20Transition(
            context, stage5, stage10, stage12,
            (_cg01ToddlerWorld ?? throw new InvalidOperationException(
                "Fallout 3 CG01 interaction world is absent.")).State(triggerEntered: true),
            stage14, current);
        void InstallBirthday(
            Fo3Cg02BirthdayInteractionsRuntime birthday,
            Fo3Cg01ToddlerPlayer player)
        {
            var cake = birthday.CakeRuntime ?? throw new InvalidOperationException(
                "Fallout 3 CG02 cake runtime is absent.");
            var butch = birthday.ButchRuntime ?? throw new InvalidOperationException(
                "Fallout 3 CG02 Butch runtime is absent.");
            var postIntercom = butch.PostIntercomRuntime ??
                throw new InvalidOperationException(
                    "Fallout 3 CG02 post-intercom runtime is absent.");
            var reactorGift = postIntercom.ReactorGiftRuntime ??
                throw new InvalidOperationException(
                    "Fallout 3 CG02 reactor-gift runtime is absent.");
            var picture = reactorGift.PictureRuntime;
            var pictureCompletion = picture.CompletionRuntime;
            var cg03 = pictureCompletion.NextQuestRuntime ??
                throw new InvalidOperationException(
                    "Fallout 3 CG03 stage-5 runtime is absent.");
            var jonasGift = reactorGift.Participants.Single(value =>
                value.ReferenceFormId.Equals(postIntercom.JonasReferenceFormId,
                    StringComparison.OrdinalIgnoreCase));
            var dadGift = reactorGift.Participants.Single(value =>
                value.ReferenceFormId.Equals(postIntercom.DadReferenceFormId,
                    StringComparison.OrdinalIgnoreCase));
            if (current.ActiveQuestFormId.Equals(
                    pictureCompletion.NextQuestFormId,
                    StringComparison.OrdinalIgnoreCase))
            {
                if (current.ActiveStage != pictureCompletion.NextQuestTargetStage &&
                    current.ActiveStage != cg03.SpeechStage ||
                    !current.Cg02AdultVaultSuitEquipped)
                    throw new InvalidOperationException(
                        "Fallout 3 CG03 completion handoff state differs.");
                _vaultBirthCoverage!.Cg01DadActor.Placement.Visible = false;
                _vaultBirthCoverage.Cg01DadActor.Placement.ProcessMode =
                    ProcessModeEnum.Disabled;
                var restoredBeatrice = Cg01WorldReference(
                    pictureCompletion.BeatriceReferenceFormId);
                restoredBeatrice.Visible = false;
                restoredBeatrice.ProcessMode = ProcessModeEnum.Disabled;
                player.SetMeta("opennv_pipboy_radio_on", false);
                player.SetMeta("opennv_inventory_cleared", 1);
                player.SetMeta(
                    $"opennv_cg03_item_{pictureCompletion.AdultVaultSuitFormId}", 1);
                player.SetMeta("opennv_equipped_item_form_id",
                    pictureCompletion.AdultVaultSuitFormId);
                player.SetMeta("opennv_age_race_delta", 1);
                if (current.Cg02SkillBookTransferred)
                    player.SetMeta(
                        $"opennv_reference_item_{pictureCompletion.NextDresserReferenceFormId}_" +
                        pictureCompletion.SkillBookFormId, 1);
                StartCg03Stage5Runtime(
                    cg03,
                    player,
                    current.ActiveStage,
                    current.TimerRemainingSeconds,
                    current.AppliedPackageFormIds.Contains(
                        cg03.DadHoldPackageFormId,
                        StringComparer.OrdinalIgnoreCase),
                    ApplyCg03Progress);
                return;
            }
            player.SetMeta($"opennv_quest_stage_{current.ActiveQuestFormId}",
                current.ActiveStage);
            bool InfoAppliedAtStage(int stage) => birthday.Participants
                .SelectMany(value => value.Nodes.Values)
                .Where(node => current.AppliedInfoFormIds.Contains(
                    node.InfoFormId, StringComparer.OrdinalIgnoreCase))
                .SelectMany(node => node.Effects)
                .Any(effect => effect.Kind == "setStage" && effect.Stage == stage);
            bool InfoRemovedItem(string formId) => birthday.Participants
                .SelectMany(value => value.Nodes.Values)
                .Where(node => current.AppliedInfoFormIds.Contains(
                    node.InfoFormId, StringComparer.OrdinalIgnoreCase))
                .SelectMany(node => node.Effects)
                .Any(effect => effect.Kind == "removeItem" &&
                    effect.FormId.Equals(formId, StringComparison.OrdinalIgnoreCase));
            var sweetrollCount = InfoAppliedAtStage(butch.SourceStage) &&
                !InfoRemovedItem(butch.SweetrollFormId) ? 1 : 0;
            player.SetMeta($"opennv_cg02_item_{butch.SweetrollFormId}", sweetrollCount);

            void StartDadToIntercomTravel()
            {
                var target = postIntercom.DadToIntercomPackage.TargetTransform ??
                    throw new InvalidOperationException(
                        "Fallout 3 CG02 intercom package target is absent.");
                var coverage = _vaultBirthCoverage ?? throw new InvalidOperationException(
                    "Fallout 3 CG02 intercom travel world is absent.");
                var local = coverage.Cg01DadActor.Placement.Transform.Origin -
                    Vector3.Up * coverage.Cg01DadGrounding.VerticalCorrectionGodotGameUnits;
                var sourceStart = coverage.Contract.EntryPositionGameUnits +
                    new Vector3(local.X, -local.Z, local.Y);
                var start = target with
                {
                    PositionGameUnits = new Fo3Cg01Vector3(
                        sourceStart.X, sourceStart.Y, sourceStart.Z),
                };
                var package = new Fo3Cg01DadTravelPackage(
                    new Fo3Cg01PostStage14Package(
                        postIntercom.DadToIntercomPackage.FormId,
                        postIntercom.DadToIntercomPackage.FormId,
                        postIntercom.DadToIntercomPackage.TargetFormId,
                        target,
                        postIntercom.DadToIntercomPackage.RadiusGameUnits,
                        null),
                    [], postIntercom.SourceStage, null, []);
                StartCg01DadSourceTravel(
                    interaction.TimerTransition.DadLead, package, start, stage5,
                    () => coverage.Cg01DadActor.Placement.SetMeta(
                        "opennv_active_package_form_id",
                        postIntercom.DadTalkToJonasPackage.FormId));
            }

            CellActorLoader.PlacedActor EnsureJonas()
            {
                if (_cg02IntroActors.TryGetValue(
                        postIntercom.JonasReferenceFormId, out var existing))
                    return existing;
                using var stream = File.OpenRead(postIntercom.JonasActorScenePath);
                var hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
                if (!hash.Equals(postIntercom.JonasActorSceneSha256,
                        StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(
                        "Fallout 3 CG02 Jonas actor scene hash differs.");
                var coverage = _vaultBirthCoverage ?? throw new InvalidOperationException(
                    "Fallout 3 CG02 Jonas world is absent.");
                var actor = CellActorLoader.Load(
                        postIntercom.JonasActorScenePath,
                        new HashSet<string>([coverage.Contract.CellFormId],
                            StringComparer.OrdinalIgnoreCase), coverage.CellRoot,
                        coverage.Contract.EntryPositionGameUnits,
                        _runtimeConfiguration, proofEnableInitiallyDisabled: false,
                        materializeInitiallyDisabled: true)
                    ?? throw new InvalidOperationException(
                        "Fallout 3 CG02 Jonas actor is absent.");
                if (actor.ReferenceFormId != postIntercom.JonasReferenceFormId ||
                    actor.BaseFormId != postIntercom.JonasBaseFormId)
                    throw new InvalidOperationException(
                        "Fallout 3 CG02 Jonas actor identity differs.");
                _cg02IntroActors.Add(actor.ReferenceFormId, actor);
                return actor;
            }

            void ApplyPostIntercomStage(int stage)
            {
                var commands = postIntercom.StageResults[stage];
                foreach (var command in commands)
                {
                    var target = string.IsNullOrEmpty(command.ReferenceFormId)
                        ? null
                        : _cg02IntroActors.TryGetValue(command.ReferenceFormId,
                            out var actor) ? actor.Placement
                        : Cg01WorldReference(command.ReferenceFormId);
                    switch (command.Kind)
                    {
                        case "setQuestVariable":
                            player.SetMeta($"opennv_cg02_{command.Variable.ToLowerInvariant()}",
                                command.Value);
                            break;
                        case "evaluatePackage":
                            target!.SetMeta("opennv_evaluate_package", 1);
                            break;
                        case "clearTalkingActivatorActor":
                            target!.SetMeta("opennv_talking_activator_actor", "");
                            break;
                        case "enable":
                            target!.Visible = true;
                            target.ProcessMode = ProcessModeEnum.Inherit;
                            target.SetMeta("opennv_enabled", 1);
                            break;
                        case "ignoreCrime":
                            target!.SetMeta("opennv_ignore_crime", command.Value);
                            break;
                        case "setObjectiveDisplayed":
                            player.SetMeta("opennv_cg02_objective_displayed",
                                command.ObjectiveIndex);
                            break;
                        case "setObjectiveCompleted":
                            player.SetMeta("opennv_cg02_objective_completed",
                                command.ObjectiveIndex);
                            break;
                        default:
                            throw new InvalidOperationException(
                                $"Fallout 3 CG02 post-intercom command is unsupported: {command.Kind}");
                    }
                }
                current = current with
                {
                    ActiveStage = stage,
                    DisplayedObjectiveIndex = commands
                        .Where(value => value.Kind == "setObjectiveDisplayed" &&
                            value.Value != 0)
                        .Select(value => value.ObjectiveIndex)
                        .DefaultIfEmpty(current.DisplayedObjectiveIndex).Last(),
                    AccountedCommandCount = current.AccountedCommandCount + commands.Count,
                    AppliedCommandCount = current.AppliedCommandCount + commands.Count,
                    NextBoundary = new Fo3Cg01Stage12Boundary(false,
                        stage == postIntercom.TargetStage
                            ? postIntercom.NextBoundaryBlocker
                            : birthday.NextBoundaryBlocker),
                };
                player.SetMeta("opennv_cg02_stage", stage);
                player.SetMeta($"opennv_quest_stage_{current.ActiveQuestFormId}", stage);
                Persist();
            }

            void PlayPostIntercomCue(Fo3Cg02PostIntercomCue cue, Action completed)
            {
                if (current.AppliedInfoFormIds.Contains(
                        cue.InfoFormId, StringComparer.OrdinalIgnoreCase))
                {
                    completed();
                    return;
                }
                var speaker = cue.SpeakerBaseFormId.Equals(
                        postIntercom.JonasBaseFormId, StringComparison.OrdinalIgnoreCase)
                    ? EnsureJonas()
                    : _vaultBirthCoverage!.Cg01DadActor;
                GamebryoDialoguePlayback.ValidateOrderedLines(cue.Responses.Select(
                    response => new SourceDialogueLine(cue.InfoFormId, response.Index,
                        cue.SpeakerBaseFormId, response.Text,
                        new SourceDialogueAsset(response.Voice.LogicalPath,
                            response.Voice.SourcePath, response.Voice.Sha256),
                        new SourceDialogueAsset(response.Lip.LogicalPath,
                            response.Lip.SourcePath, response.Lip.Sha256))).ToArray());
                PlayLine(0);
                void PlayLine(int index)
                {
                    if (index == cue.Responses.Count)
                    {
                        current = current with
                        {
                            AppliedInfoFormIds =
                                current.AppliedInfoFormIds.Append(cue.InfoFormId).ToArray(),
                            AccountedCommandCount = current.AccountedCommandCount +
                                (cue.TargetStage is null ? 0 : 1),
                            AppliedCommandCount = current.AppliedCommandCount +
                                (cue.TargetStage is null ? 0 : 1),
                        };
                        if (cue.TargetStage is { } targetStage)
                            ApplyPostIntercomStage(targetStage);
                        else
                            Persist();
                        completed();
                        return;
                    }
                    var response = cue.Responses[index];
                    var voice = new AudioStreamPlayer
                    {
                        Name = $"Fallout3Cg02PostIntercomVoice{cue.InfoFormId}_{response.Index}",
                    };
                    AddChild(voice);
                    var dialogue = new GamebryoDialoguePlayback(
                        voice, _runtimeConfiguration.ActorCompiler.FaceGenAnimation.Lip);
                    _cg02IntroDialogue.Add(dialogue);
                    dialogue.Start(new SourceDialogueLine(cue.InfoFormId, response.Index,
                            cue.SpeakerBaseFormId, response.Text,
                            new SourceDialogueAsset(response.Voice.LogicalPath,
                                response.Voice.SourcePath, response.Voice.Sha256),
                            new SourceDialogueAsset(response.Lip.LogicalPath,
                                response.Lip.SourcePath, response.Lip.Sha256)),
                        new FaceGenMorphController(speaker.Actor,
                            _runtimeConfiguration.ActorCompiler.FaceGenAnimation.Lip),
                        () => PlayLine(index + 1));
                }
            }

            void ActivateIntercom()
            {
                if (current.ActiveStage != postIntercom.SourceStage)
                    return;
                var sex = (_selectedSex ?? throw new InvalidOperationException(
                    "Fallout 3 CG02 post-intercom player sex is absent.")).EngineSex;
                current = current with
                {
                    AppliedPackageFormIds =
                    current.AppliedPackageFormIds.Contains(
                        postIntercom.DadTalkToJonasPackage.FormId,
                        StringComparer.OrdinalIgnoreCase)
                        ? current.AppliedPackageFormIds
                        : current.AppliedPackageFormIds.Append(
                            postIntercom.DadTalkToJonasPackage.FormId).ToArray()
                };
                var dadCall = postIntercom.Cues.Single(value =>
                    value.TargetStage == postIntercom.AnswerStage);
                var jonasReply = postIntercom.Cues.Single(value =>
                    value.SpeakerBaseFormId.Equals(postIntercom.JonasBaseFormId,
                        StringComparison.OrdinalIgnoreCase));
                var goodbye = postIntercom.Cues.Single(value => value.EngineSex == sex);
                PlayPostIntercomCue(dadCall, () => PlayPostIntercomCue(jonasReply,
                    () => PlayPostIntercomCue(goodbye, () =>
                    {
                        var dad = _vaultBirthCoverage!.Cg01DadActor.Placement;
                        dad.SetMeta("opennv_active_package_form_id",
                            postIntercom.DadToPlayerPackage.FormId);
                        current = current with
                        {
                            AppliedPackageFormIds =
                            current.AppliedPackageFormIds.Append(
                                postIntercom.DadToPlayerPackage.FormId).ToArray()
                        };
                        Persist();
                    })));
            }

            void ActivateDadPostIntercom()
            {
                if (current.ActiveStage != postIntercom.GoodbyeStage)
                    return;
                var greeting = postIntercom.Cues.Single(value =>
                    value.TargetStage == postIntercom.TargetStage);
                PlayPostIntercomCue(greeting, () => { });
            }

            void ExecuteReactorGiftStageCommands(int stage)
            {
                var commands = reactorGift.StageResults[stage];
                foreach (var command in commands)
                {
                    switch (command.Kind)
                    {
                        case "removeItem":
                            (_cg02IntroActors.TryGetValue(command.ReferenceFormId,
                                out var removeActor) ? removeActor.Placement :
                                _vaultBirthCoverage!.Cg01DadActor.Placement).SetMeta(
                                    $"opennv_item_{command.ItemFormId}", 0);
                            break;
                        case "moveToReference":
                            {
                                var source = command.TargetTransform ??
                                    throw new InvalidOperationException(
                                        "Fallout 3 CG02 reactor-gift move target is absent.");
                                var package = new Fo3Cg01PostStage14Package(
                                    command.TargetFormId, command.TargetFormId,
                                    command.TargetFormId, source, 0, null);
                                var coverage = _vaultBirthCoverage!;
                                var placement = Cg01DadPackagePlacement(
                                    package, stage5, coverage);
                                GamebryoPackageTravel.ArriveAtSourceTarget(
                                    command.TargetFormId, placement,
                                    coverage.Cg01DadActor.Placement.Transform,
                                    GamebryoPackageTravel.ExactArrivalToleranceCellUnits)
                                    .Publish(coverage.Cg01DadActor.Placement);
                                break;
                            }
                        case "setOpenState":
                            SetCg01WorldReferenceOpen(
                                command.ReferenceFormId, command.Value != 0);
                            break;
                        case "lock":
                            SetCg01WorldReferenceLock(
                                command.ReferenceFormId, command.Value);
                            break;
                        case "addItem":
                            player.SetMeta($"opennv_cg02_item_{command.ItemFormId}",
                                player.GetMeta(
                                    $"opennv_cg02_item_{command.ItemFormId}", 0).AsInt32() +
                                command.Count);
                            break;
                        case "equipItem":
                            player.SetMeta("opennv_equipped_item_form_id",
                                command.ItemFormId);
                            break;
                        case "unlock":
                            SetCg01WorldReferenceLock(command.ReferenceFormId, 0);
                            break;
                        case "enablePlayerControls":
                            player.SetMeta("opennv_enabled_player_controls",
                                string.Join(',', command.Arguments));
                            break;
                        case "setObjectiveCompleted":
                            player.SetMeta("opennv_cg02_objective_completed",
                                command.ObjectiveIndex);
                            break;
                        case "setObjectiveDisplayed":
                            player.SetMeta("opennv_cg02_objective_displayed",
                                command.ObjectiveIndex);
                            break;
                        case "setStage":
                            player.SetMeta("opennv_tutorial_quest_form_id",
                                command.QuestFormId);
                            player.SetMeta("opennv_tutorial_stage", command.Stage);
                            break;
                        case "enable":
                            {
                                var enabled = Cg01WorldReference(command.ReferenceFormId);
                                enabled.Visible = true;
                                enabled.ProcessMode = ProcessModeEnum.Inherit;
                                enabled.SetMeta("opennv_enabled", 1);
                                break;
                            }
                        case "evaluatePackage":
                            (_cg02IntroActors.TryGetValue(command.ReferenceFormId,
                                out var packageActor) ? packageActor.Placement :
                                Cg01WorldReference(command.ReferenceFormId)).SetMeta(
                                    "opennv_evaluate_package", 1);
                            break;
                        case "setQuestObject":
                            player.SetMeta(
                                $"opennv_quest_object_{command.ItemFormId}",
                                command.Value);
                            break;
                        default:
                            throw new InvalidOperationException(
                                $"Fallout 3 CG02 reactor-gift command is unsupported: {command.Kind}");
                    }
                }
            }

            void ApplyReactorGiftStage(int stage)
            {
                var commands = reactorGift.StageResults.TryGetValue(stage,
                    out var preparedCommands) ? preparedCommands : [];
                if (commands.Count != 0)
                    ExecuteReactorGiftStageCommands(stage);
                IReadOnlyList<string> packages = stage switch
                {
                    var value when value == reactorGift.JonasStage =>
                        [reactorGift.JonasGreetPackageFormId],
                    var value when value == reactorGift.TargetStage =>
                        [reactorGift.DadGreetPackageFormId,
                         reactorGift.DadToRangePackageFormId,
                         reactorGift.JonasWaitPackageFormId],
                    var value when value == reactorGift.RangeStage =>
                        [reactorGift.DadWaitPackageFormId],
                    var value when value == reactorGift.HitStage => [],
                    var value when value == reactorGift.CombatStage =>
                        [reactorGift.Combatant.PackageFormId],
                    var value when value == reactorGift.DeathStage => [],
                    var value when value == reactorGift.CompletionStage => [],
                    _ => throw new InvalidOperationException(
                        "Fallout 3 CG02 reactor-gift stage differs."),
                };
                current = current with
                {
                    ActiveStage = stage,
                    AppliedPackageFormIds = current.AppliedPackageFormIds
                        .Concat(packages).ToArray(),
                    DisplayedObjectiveIndex = commands
                        .Where(value => value.Kind == "setObjectiveDisplayed" &&
                            value.Value != 0)
                        .Select(value => value.ObjectiveIndex)
                        .DefaultIfEmpty(current.DisplayedObjectiveIndex).Last(),
                    AccountedCommandCount = current.AccountedCommandCount +
                        commands.Count + 1 + (stage == reactorGift.CompletionStage
                            ? picture.SourceStageCommandCount : 0),
                    AppliedCommandCount = current.AppliedCommandCount +
                        commands.Count + 1 + (stage == reactorGift.CompletionStage
                            ? picture.SourceStageCommandCount : 0),
                    NextBoundary = new Fo3Cg01Stage12Boundary(false,
                        stage == reactorGift.CompletionStage
                            ? reactorGift.NextBoundaryBlocker
                            : postIntercom.NextBoundaryBlocker),
                };
                player.SetMeta("opennv_cg02_stage", stage);
                if (stage == reactorGift.CompletionStage)
                {
                    player.SetMeta("opennv_cg02_objective_displayed",
                        picture.ObjectiveIndex);
                    player.SetMeta($"opennv_quest_stage_{current.ActiveQuestFormId}",
                        stage);
                }
                if (stage == reactorGift.CombatStage &&
                    !current.CombatHealthByReferenceFormId.ContainsKey(
                        reactorGift.Combatant.ReferenceFormId))
                {
                    current = current with
                    {
                        CombatHealthByReferenceFormId =
                            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                            {
                                [reactorGift.Combatant.ReferenceFormId] =
                                    reactorGift.Combatant.MaximumHealth,
                            },
                    };
                    var combatant = Cg01WorldReference(
                        reactorGift.Combatant.ReferenceFormId);
                    combatant.SetMeta("opennv_active_package_form_id",
                        reactorGift.Combatant.PackageFormId);
                    combatant.SetMeta("opennv_package_target_form_id",
                        reactorGift.Combatant.PackageTargetFormId);
                    combatant.SetMeta("opennv_package_radius_game_units",
                        reactorGift.Combatant.PackageRadiusGameUnits);
                    combatant.SetMeta("opennv_current_health",
                        reactorGift.Combatant.MaximumHealth);
                }
                Persist();
                if (stage == reactorGift.HitStage)
                    StartReactorGiftParticipant(dadGift);
            }

            void StartReactorGiftParticipant(Fo3Cg02BirthdayParticipant participant)
            {
                StartCg02BirthdayInteraction(
                    participant, player, (infoFormId, targetStage) =>
                    {
                        if (current.AppliedInfoFormIds.Contains(
                                infoFormId, StringComparer.OrdinalIgnoreCase))
                            throw new InvalidOperationException(
                                "Fallout 3 CG02 reactor-gift INFO replay differs.");
                        current = current with
                        {
                            AppliedInfoFormIds = current.AppliedInfoFormIds
                                .Append(infoFormId).ToArray(),
                            AccountedCommandCount = current.AccountedCommandCount +
                                participant.Nodes[infoFormId].Effects.Count(value =>
                                    value.Kind != "setStage"),
                            AppliedCommandCount = current.AppliedCommandCount +
                                participant.Nodes[infoFormId].Effects.Count(value =>
                                    value.Kind != "setStage"),
                        };
                        if (targetStage is { } stage)
                            ApplyReactorGiftStage(stage);
                        else
                            Persist();
                    });
            }

            void CompletePictureSequence()
            {
                var transferredBook = player.GetMeta(
                    $"opennv_cg02_item_{pictureCompletion.SkillBookFormId}", 0)
                    .AsInt32() > 0;
                _vaultBirthCoverage!.Cg01DadActor.Placement.Visible = false;
                _vaultBirthCoverage.Cg01DadActor.Placement.ProcessMode =
                    ProcessModeEnum.Disabled;
                var beatrice = Cg01WorldReference(
                    pictureCompletion.BeatriceReferenceFormId);
                beatrice.Visible = false;
                beatrice.ProcessMode = ProcessModeEnum.Disabled;
                player.ConfigureSourceFormActivations(null);
                player.ClearSourceHitscan();
                player.SetMeta("opennv_pipboy_radio_on", false);
                player.SetMeta("opennv_inventory_cleared", 1);
                player.SetMeta(
                    $"opennv_cg02_item_{pictureCompletion.SkillBookFormId}", 0);
                if (transferredBook)
                    player.SetMeta(
                        $"opennv_reference_item_{pictureCompletion.NextDresserReferenceFormId}_" +
                        pictureCompletion.SkillBookFormId, 1);
                player.SetMeta(
                    $"opennv_cg03_item_{pictureCompletion.AdultVaultSuitFormId}", 1);
                player.SetMeta("opennv_equipped_item_form_id",
                    pictureCompletion.AdultVaultSuitFormId);
                player.SetMeta("opennv_age_race_delta", 1);
                player.MoveToSourceTransform(
                    pictureCompletion.NextQuestStartTransform,
                    _vaultBirthCoverage.Contract);
                current = current with
                {
                    ActiveQuestFormId = pictureCompletion.NextQuestFormId,
                    ActiveQuestEditorId = pictureCompletion.NextQuestEditorId,
                    ActiveStage = pictureCompletion.NextQuestTargetStage,
                    TimerRemainingSeconds = 0.0,
                    TimerAdvancing = false,
                    Cg02PictureImageSpaceElapsedSeconds =
                        pictureCompletion.ImageSpaceModifier.DurationSeconds,
                    Cg02PictureSoundStarted = true,
                    PlayerMovementEnabled = false,
                    Cg02SkillBookTransferred = transferredBook,
                    Cg02AdultVaultSuitEquipped = true,
                    AccountedCommandCount = current.AccountedCommandCount +
                        pictureCompletion.Stage100CommandCount +
                        pictureCompletion.NextQuestStage0CommandCount,
                    AppliedCommandCount = current.AppliedCommandCount +
                        pictureCompletion.Stage100CommandCount +
                        pictureCompletion.NextQuestStage0CommandCount,
                    NextBoundary = new Fo3Cg01Stage12Boundary(
                        false, pictureCompletion.NextBoundaryBlocker),
                };
                player.SetMeta("opennv_active_quest_form_id",
                    pictureCompletion.NextQuestFormId);
                player.SetMeta("opennv_cg03_stage",
                    pictureCompletion.NextQuestTargetStage);
                player.SetMeta(
                    $"opennv_quest_stage_{pictureCompletion.NextQuestFormId}",
                    pictureCompletion.NextQuestTargetStage);
                Persist();
                StartCg03Stage5Runtime(
                    cg03,
                    player,
                    current.ActiveStage,
                    current.TimerRemainingSeconds,
                    current.AppliedPackageFormIds.Contains(
                        cg03.DadHoldPackageFormId,
                        StringComparer.OrdinalIgnoreCase),
                    ApplyCg03Progress);
            }

            void ApplyCg03Progress(Fo3Cg03Stage5Progress progress)
            {
                current = current with
                {
                    ActiveStage = progress.Stage,
                    TimerRemainingSeconds = progress.TimerRemainingSeconds,
                    TimerAdvancing = progress.TimerAdvancing,
                    AppliedInfoFormIds = progress.AppliedInfoFormId is null ||
                        current.AppliedInfoFormIds.Contains(
                            progress.AppliedInfoFormId,
                            StringComparer.OrdinalIgnoreCase)
                        ? current.AppliedInfoFormIds
                        : current.AppliedInfoFormIds.Append(
                            progress.AppliedInfoFormId).ToArray(),
                    AppliedPackageFormIds = current.AppliedPackageFormIds.Contains(
                            progress.AppliedPackageFormId,
                            StringComparer.OrdinalIgnoreCase)
                        ? current.AppliedPackageFormIds
                        : current.AppliedPackageFormIds.Append(
                            progress.AppliedPackageFormId).ToArray(),
                    AccountedCommandCount = current.AccountedCommandCount +
                        progress.AppliedCommandCount,
                    AppliedCommandCount = current.AppliedCommandCount +
                        progress.AppliedCommandCount,
                    NextBoundary = new Fo3Cg01Stage12Boundary(
                        false, progress.NextBoundaryBlocker),
                };
                Persist();
            }

            void CompletionProgress(Fo3Cg02CompletionProgress progress)
            {
                var stageChanged = current.ActiveStage != progress.Stage;
                current = current with
                {
                    ActiveStage = progress.Stage,
                    TimerRemainingSeconds = progress.TimerRemainingSeconds,
                    TimerAdvancing = progress.TimerAdvancing,
                    Cg02PictureImageSpaceElapsedSeconds =
                        progress.ImageSpaceElapsedSeconds,
                    Cg02PictureSoundStarted = progress.SoundStarted,
                    AccountedCommandCount = current.AccountedCommandCount +
                        (stageChanged ? pictureCompletion.Stage98CommandCount : 0),
                    AppliedCommandCount = current.AppliedCommandCount +
                        (stageChanged ? pictureCompletion.Stage98CommandCount : 0),
                };
                player.SetMeta("opennv_cg02_stage", progress.Stage);
                player.SetMeta("opennv_cg02_timer", progress.TimerRemainingSeconds);
                player.SetMeta("opennv_cg02_run_timer",
                    progress.TimerAdvancing ? 1 : 0);
                Persist();
            }

            void StartPictureCompletion()
            {
                StartCg02CompletionTimer(
                    pictureCompletion, current.ActiveStage,
                    current.TimerRemainingSeconds,
                    current.Cg02PictureImageSpaceElapsedSeconds,
                    current.Cg02PictureSoundStarted,
                    CompletionProgress, CompletePictureSequence);
            }

            void StartPictureJonas()
            {
                if (current.AppliedInfoFormIds.Contains(
                        picture.JonasInfoFormId, StringComparer.OrdinalIgnoreCase))
                    return;
                StartCg02BirthdayInteraction(jonasGift, player,
                    (infoFormId, targetStage) =>
                    {
                        if (targetStage is not null || !infoFormId.Equals(
                                picture.JonasInfoFormId,
                                StringComparison.OrdinalIgnoreCase))
                            throw new InvalidOperationException(
                                "Fallout 3 CG02 picture Jonas result differs.");
                        current = current with
                        {
                            ActiveStage = picture.TimerStage,
                            AppliedInfoFormIds = current.AppliedInfoFormIds
                                .Append(infoFormId).ToArray(),
                            TimerRemainingSeconds =
                                pictureCompletion.Stage95TimerSeconds,
                            TimerAdvancing = true,
                            AccountedCommandCount = current.AccountedCommandCount + 1 +
                                pictureCompletion.Stage95CommandCount,
                            AppliedCommandCount = current.AppliedCommandCount + 1 +
                                pictureCompletion.Stage95CommandCount,
                            NextBoundary = new Fo3Cg01Stage12Boundary(
                                false, picture.NextBoundaryBlocker),
                        };
                        player.SetMeta("opennv_cg02_stage", picture.TimerStage);
                        player.SetMeta(
                            $"opennv_quest_stage_{current.ActiveQuestFormId}",
                            picture.TimerStage);
                        player.SetMeta("opennv_objectives_completed", true);
                        player.SetMeta("opennv_equipped_item_form_id", "");
                        player.SetMeta("opennv_cg02_timer",
                            pictureCompletion.Stage95TimerSeconds);
                        player.SetMeta("opennv_cg02_run_timer", 1);
                        Persist();
                        StartPictureCompletion();
                    });
            }

            void ApplyPictureStage()
            {
                if (current.ActiveStage != picture.SourceStage)
                    return;
                player.StopAtAuthoredTrigger();
                _vaultBirthCoverage!.Cg01DadActor.Placement.SetMeta(
                    "opennv_dotalk", picture.PictureDadTalkValue);
                current = current with
                {
                    ActiveStage = picture.PictureStage,
                    PlayerMovementEnabled = false,
                    DisplayedObjectiveIndex = picture.ObjectiveIndex,
                    AccountedCommandCount = current.AccountedCommandCount +
                        picture.PictureStageCommandCount,
                    AppliedCommandCount = current.AppliedCommandCount +
                        picture.PictureStageCommandCount,
                    NextBoundary = new Fo3Cg01Stage12Boundary(
                        false, picture.NextBoundaryBlocker),
                };
                player.SetMeta("opennv_cg02_objective_completed",
                    picture.ObjectiveIndex);
                player.SetMeta("opennv_cg02_stage", picture.PictureStage);
                player.SetMeta($"opennv_quest_stage_{current.ActiveQuestFormId}",
                    picture.PictureStage);
                Persist();
                StartPictureJonas();
            }

            void PicturePackageCompleted(string packageFormId)
            {
                if (current.AppliedPackageFormIds.Contains(
                        packageFormId, StringComparer.OrdinalIgnoreCase))
                    return;
                var package = picture.Packages.Single(value =>
                    value.FormId.Equals(packageFormId,
                        StringComparison.OrdinalIgnoreCase));
                var actor = package.ActorReferenceFormId.Equals(
                        postIntercom.DadReferenceFormId,
                        StringComparison.OrdinalIgnoreCase)
                    ? _vaultBirthCoverage!.Cg01DadActor.Placement
                    : EnsureJonas().Placement;
                actor.SetMeta("opennv_picture_ready", 1);
                if (package.ActorReferenceFormId.Equals(
                        postIntercom.DadReferenceFormId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    actor.SetMeta("opennv_dotalk", picture.DadTalkValue);
                    actor.SetMeta("opennv_timer", picture.DadTimerSeconds);
                }
                current = current with
                {
                    AppliedPackageFormIds = current.AppliedPackageFormIds
                        .Append(package.FormId).ToArray(),
                    AccountedCommandCount = current.AccountedCommandCount +
                        package.CompletionCommandCount,
                    AppliedCommandCount = current.AppliedCommandCount +
                        package.CompletionCommandCount,
                };
                Persist();
            }

            void StartPicturePositioning()
            {
                var dad = _vaultBirthCoverage!.Cg01DadActor;
                var jonas = EnsureJonas();
                StartCg02PicturePositioning(
                    picture, interaction.TimerTransition.DadLead, player,
                    dad, jonas, () => current.AppliedPackageFormIds,
                    PicturePackageCompleted, ApplyPictureStage);
                if (!current.AppliedInfoFormIds.Contains(
                        picture.DadInfoFormId, StringComparer.OrdinalIgnoreCase))
                    StartReactorGiftParticipant(dadGift);
            }

            void ApplyTargetHit(string targetReferenceFormId)
            {
                if (current.ActiveStage != reactorGift.RangeStage ||
                    current.Cg02TargetHitFormIds.Count >= reactorGift.RequiredHitCount)
                    return;
                var target = Cg01WorldReference(targetReferenceFormId);
                target.SetMeta("opennv_animation_group",
                    reactorGift.TargetAnimationGroup);
                current = current with
                {
                    Cg02TargetHitFormIds = current.Cg02TargetHitFormIds
                        .Append(targetReferenceFormId).ToArray(),
                };
                player.SetMeta("opennv_cg02_target_count",
                    current.Cg02TargetHitFormIds.Count);
                player.SetMeta("opennv_tutorial_stage",
                    reactorGift.TutorialHitStage);
                if (current.Cg02TargetHitFormIds.Count == reactorGift.RequiredHitCount)
                    ApplyReactorGiftStage(reactorGift.HitStage);
                else
                    Persist();
            }

            void ApplyCombatHit()
            {
                if (current.ActiveStage != reactorGift.CombatStage ||
                    !current.CombatHealthByReferenceFormId.TryGetValue(
                        reactorGift.Combatant.ReferenceFormId, out var health))
                    return;
                var outcome = GamebryoRangedCombat.ApplyHit(
                    new GamebryoRangedAttack(
                        reactorGift.Combatant.WeaponFormId,
                        reactorGift.Combatant.AmmunitionFormId,
                        reactorGift.Combatant.WeaponDamage),
                    player.GetMeta("opennv_equipped_item_form_id", "").AsString(),
                    new GamebryoCombatantState(
                        reactorGift.Combatant.ReferenceFormId,
                        reactorGift.Combatant.MaximumHealth,
                        health,
                        current.DeadCombatReferenceFormIds.Contains(
                            reactorGift.Combatant.ReferenceFormId,
                            StringComparer.OrdinalIgnoreCase)));
                current = current with
                {
                    CombatHealthByReferenceFormId =
                        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                        {
                            [outcome.Target.ReferenceFormId] =
                                outcome.Target.CurrentHealth,
                        },
                    DeadCombatReferenceFormIds = outcome.Target.Dead
                        ? [outcome.Target.ReferenceFormId]
                        : current.DeadCombatReferenceFormIds,
                };
                var combatant = Cg01WorldReference(outcome.Target.ReferenceFormId);
                combatant.SetMeta("opennv_combat_target_form_id",
                    reactorGift.Combatant.PlayerReferenceFormId);
                combatant.SetMeta("opennv_current_health", outcome.Target.CurrentHealth);
                combatant.SetMeta("opennv_dead", outcome.Target.Dead ? 1 : 0);
                if (outcome.Died)
                    ApplyReactorGiftStage(reactorGift.DeathStage);
                else
                    Persist();
            }

            void ApplyStage35()
            {
                foreach (var command in butch.Stage35Commands)
                {
                    if (command.Kind == "evaluatePackage")
                        (_cg02IntroActors.TryGetValue(command.ReferenceFormId,
                            out var packageActor) ? packageActor.Placement :
                            Cg01WorldReference(command.ReferenceFormId))
                            .SetMeta("opennv_evaluate_package", 1);
                    else if (command.Kind == "setTalkingActivatorActor")
                        Cg01WorldReference(command.ReferenceFormId).SetMeta(
                            "opennv_talking_activator_actor",
                            command.ActorReferenceFormId);
                    else if (command.Kind == "setQuestVariable")
                        player.SetMeta(
                            $"opennv_cg02_{command.Variable.ToLowerInvariant()}",
                            command.Value);
                    else
                        throw new InvalidOperationException(
                            $"Fallout 3 CG02 stage-35 command is unsupported: " +
                            command.Kind);
                }
                current = current with
                {
                    ActiveStage = butch.IntercomStage,
                    TimerRemainingSeconds = 0.0,
                    TimerAdvancing = false,
                    AccountedCommandCount = current.AccountedCommandCount +
                        butch.Stage35Commands.Count,
                    AppliedCommandCount = current.AppliedCommandCount +
                        butch.Stage35Commands.Count,
                    AppliedPackageFormIds = current.AppliedPackageFormIds.Append(
                        postIntercom.DadToIntercomPackage.FormId).ToArray(),
                };
                player.SetMeta("opennv_cg02_stage", butch.IntercomStage);
                _cg02ButchTimerTick = null;
                Persist();
                EnsureJonas();
                var dad = _vaultBirthCoverage!.Cg01DadActor.Placement;
                dad.SetMeta("opennv_active_package_form_id",
                    postIntercom.DadToIntercomPackage.FormId);
                StartDadToIntercomTravel();
            }
            void StartIntercomTimer(double remainingSeconds)
            {
                if (_cg02ButchTimerTick is not null)
                    return;
                _cg02ButchTimerTick = delta =>
                {
                    var remaining = Math.Max(
                        0.0, current.TimerRemainingSeconds - delta);
                    current = current with { TimerRemainingSeconds = remaining };
                    if (remaining > 0.0)
                    {
                        Persist();
                        return;
                    }
                    ApplyStage35();
                };
                current = current with
                {
                    TimerRemainingSeconds = remainingSeconds,
                    TimerAdvancing = true,
                };
                player.SetMeta("opennv_cg02_timer", remainingSeconds);
                Persist();
            }
            void CakeStageChanged(int stage, string? packageFormId)
            {
                if (current.ActiveStage == stage)
                    return;
                var commandCount = stage == cake.TriggerStage
                    ? cake.Stage15CommandCount + 1
                    : cake.PackageResultCommandCount + cake.Stage16CommandCount;
                current = current with
                {
                    ActiveStage = stage,
                    AppliedPackageFormIds = packageFormId is null
                        ? current.AppliedPackageFormIds
                        : current.AppliedPackageFormIds.Append(packageFormId).ToArray(),
                    AccountedCommandCount = current.AccountedCommandCount + commandCount,
                    AppliedCommandCount = current.AppliedCommandCount + commandCount,
                    NextBoundary = new Fo3Cg01Stage12Boundary(
                        false, cake.NextBoundaryBlocker),
                };
                player.SetMeta("opennv_cg02_stage", stage);
                Persist();
            }
            void CakeCueCompleted(Fo3Cg02CakeCue cue)
            {
                if (current.AppliedInfoFormIds.Contains(
                        cue.InfoFormId, StringComparer.OrdinalIgnoreCase))
                    return;
                current = current with
                {
                    AppliedInfoFormIds = current.AppliedInfoFormIds
                        .Append(cue.InfoFormId).ToArray(),
                    AccountedCommandCount = current.AccountedCommandCount +
                        cue.Effects.Count,
                    AppliedCommandCount = current.AppliedCommandCount +
                        cue.Effects.Count,
                };
                Persist();
            }
            void StartCake() => StartCg02CakeRuntime(
                cake, player, CakeStageChanged, CakeCueCompleted,
                current.AppliedInfoFormIds,
                current.AppliedPackageFormIds.Contains(
                    cake.PackageFormId, StringComparer.OrdinalIgnoreCase));
            var coverage = _vaultBirthCoverage ?? throw new InvalidOperationException(
                "Fallout 3 CG02 cake trigger world is absent.");
            var triggerName = $"SOURCE_TRIGGER_{cake.TriggerReferenceFormId}";
            if (!coverage.CellRoot.HasNode(triggerName))
            {
                var source = cake.TriggerTransform;
                var trigger = new Area3D
                {
                    Name = triggerName,
                    Position = GamebryoCoordinate.ConvertVector(
                        new Vector3((float)source.PositionGameUnits.X,
                            (float)source.PositionGameUnits.Y,
                            (float)source.PositionGameUnits.Z) -
                        coverage.Contract.EntryPositionGameUnits),
                    Rotation = new Vector3(0.0f, -(float)source.RotationRadians.Z, 0.0f),
                    Scale = Vector3.One * (float)source.Scale,
                    CollisionLayer = 0,
                    CollisionMask = player.SourceBodyCollisionLayer,
                    Monitoring = true,
                };
                trigger.SetMeta("opennv_source_form_id", cake.TriggerReferenceFormId);
                trigger.AddChild(new CollisionShape3D
                {
                    Shape = new BoxShape3D
                    {
                        Size = new Vector3(
                            (float)cake.TriggerDimensionsGameUnits.X,
                            (float)cake.TriggerDimensionsGameUnits.Z,
                            (float)cake.TriggerDimensionsGameUnits.Y),
                    },
                });
                trigger.BodyEntered += body =>
                {
                    if (body == player && _cg02CakePackageTick is null &&
                        !current.AppliedPackageFormIds.Contains(
                            cake.PackageFormId, StringComparer.OrdinalIgnoreCase))
                        StartCake();
                };
                coverage.CellRoot.AddChild(trigger);
            }
            foreach (var participant in birthday.Participants)
            {
                var actor = EnsureCg02BirthdayActor(participant);
                var bodyName = $"SOURCE_ACTIVATION_{participant.ReferenceFormId}";
                if (actor.Placement.HasNode(bodyName))
                    continue;
                var bounds = actor.Actor.Bounds;
                var body = new StaticBody3D
                {
                    Name = bodyName,
                    Position = bounds.GetCenter(),
                    CollisionLayer = player.SourceActivationCollisionLayer,
                    CollisionMask = 0,
                };
                body.SetMeta("opennv_source_form_id", participant.ReferenceFormId);
                body.AddChild(new CollisionShape3D
                {
                    Shape = new BoxShape3D { Size = bounds.Size },
                });
                actor.Placement.AddChild(body);
            }
            foreach (var effect in birthday.Participants
                .SelectMany(value => value.Nodes.Values)
                .Where(node => current.AppliedInfoFormIds.Contains(
                    node.InfoFormId, StringComparer.OrdinalIgnoreCase))
                .SelectMany(node => node.Effects))
            {
                if (effect.Kind == "setQuestVariable")
                    player.SetMeta(
                        $"opennv_cg02_{effect.Variable.ToLowerInvariant()}",
                        effect.Value);
                else if (effect.Kind == "setActorVariable")
                    _cg02IntroActors[effect.ReferenceFormId].Placement.SetMeta(
                        $"opennv_{effect.Variable.ToLowerInvariant()}", effect.Value);
                else if (effect.Kind == "evaluatePackage")
                    (_cg02IntroActors.TryGetValue(effect.ReferenceFormId,
                        out var packageActor) ? packageActor.Placement :
                        Cg01WorldReference(effect.ReferenceFormId))
                        .SetMeta("opennv_evaluate_package", 1);
                else if (effect.Kind == "startCombat")
                {
                    EnsureCg02BirthdayActor(birthday.Participants.Single(value =>
                        value.ReferenceFormId.Equals(butch.ReferenceFormId,
                            StringComparison.OrdinalIgnoreCase))).Placement.SetMeta(
                        "opennv_combat_target", effect.Target);
                    (_cg02IntroActors.TryGetValue(effect.ReferenceFormId,
                        out var responder) ? responder.Placement :
                        Cg01WorldReference(effect.ReferenceFormId))
                        .SetMeta("opennv_evaluate_package", 1);
                    player.SetMeta("opennv_cg02_combat_runtime_blocker",
                        butch.NextBoundaryBlocker);
                }
            }
            void StartButchPackageIfEligible()
            {
                var butchActor = EnsureCg02BirthdayActor(
                    birthday.Participants.Single(value =>
                        value.ReferenceFormId.Equals(butch.ReferenceFormId,
                            StringComparison.OrdinalIgnoreCase)));
                var eligible = current.AppliedPackageFormIds.Contains(
                        cake.PackageFormId, StringComparer.OrdinalIgnoreCase) &&
                    InfoAppliedAtStage(butch.SourceStage) &&
                    current.ActiveStage != butch.SceneDoneStage &&
                    current.ActiveStage != butch.IntercomStage;
                if (!eligible)
                    return;
                butchActor.Placement.SetMeta(
                    "opennv_active_package_form_id", butch.FindPlayerPackageFormId);
                if (!current.AppliedPackageFormIds.Contains(
                        butch.FindPlayerPackageFormId,
                        StringComparer.OrdinalIgnoreCase))
                {
                    current = current with
                    {
                        AppliedPackageFormIds = current.AppliedPackageFormIds
                            .Append(butch.FindPlayerPackageFormId).ToArray(),
                    };
                    Persist();
                }
                if (current.AppliedPackageFormIds.Count(value => value.Equals(
                        butch.FindPlayerPackageFormId,
                        StringComparison.OrdinalIgnoreCase)) > 1)
                    return;
                _cg02ButchPackageTick ??= _ =>
                {
                    if (butchActor.Placement.GlobalPosition.DistanceTo(
                            player.GlobalPosition) >
                        butch.FindPlayerRadiusGameUnits *
                            _runtimeConfiguration.World.GameUnitsToMeters)
                        return;
                    _cg02ButchPackageTick = null;
                    var paul = birthday.Participants.Single(value =>
                        value.DisplayName.Equals("Paul Hannon",
                            StringComparison.OrdinalIgnoreCase));
                    EnsureCg02BirthdayActor(paul).Placement.SetMeta(
                        "opennv_evaluate_package", 1);
                    current = current with
                    {
                        AppliedPackageFormIds = current.AppliedPackageFormIds
                            .Append(butch.FindPlayerPackageFormId).ToArray(),
                        AccountedCommandCount = current.AccountedCommandCount +
                            butch.FindPlayerResultCommandCount,
                        AppliedCommandCount = current.AppliedCommandCount +
                            butch.FindPlayerResultCommandCount,
                    };
                    Persist();
                };
            }
            var activations = birthday.Participants.ToDictionary(
                participant => participant.ReferenceFormId,
                participant => (Action)(() => StartCg02BirthdayInteraction(
                    participant,
                    player,
                    (infoFormId, targetStage) =>
                    {
                        if (current.AppliedInfoFormIds.Contains(
                                infoFormId, StringComparer.OrdinalIgnoreCase))
                            throw new InvalidOperationException(
                                "Fallout 3 CG02 birthday INFO replay differs.");
                        var completedNode = participant.Nodes[infoFormId];
                        var appliedCommands = completedNode.Effects.Count(effect =>
                            effect.Kind != "sourceConditional");
                        int? effectiveStage = targetStage;
                        if (targetStage is not null)
                        {
                            if (targetStage == cake.TriggerStage)
                                StartCake();
                            else if (birthday.StageResults.TryGetValue(
                                targetStage.Value, out var result))
                            {
                                player.SetMeta(
                                    $"opennv_cg02_{result.Kind.ToLowerInvariant()}_{result.FormId}",
                                    result.Count);
                                if (result.Kind == "addItem")
                                    player.SetMeta(
                                        $"opennv_cg02_item_{result.FormId}",
                                        result.Count);
                                player.SetMeta("opennv_cg02_stage", targetStage.Value);
                                appliedCommands += result.CommandCount;
                                if (result.AggregateStage is not null)
                                {
                                    if (result.AggregateStage != butch.AggregateStage)
                                        throw new InvalidOperationException(
                                            "Fallout 3 CG02 aggregate stage differs.");
                                    appliedCommands++;
                                    effectiveStage = result.AggregateStage;
                                    StartIntercomTimer(butch.AggregateTimerSeconds);
                                }
                            }
                            else if (targetStage == butch.SceneDoneStage)
                                appliedCommands = 1;
                            else
                                throw new InvalidOperationException(
                                    "Fallout 3 CG02 birthday stage is unsupported.");
                        }
                        current = current with
                        {
                            ActiveStage = targetStage == cake.TriggerStage
                                ? current.ActiveStage
                                : effectiveStage ?? current.ActiveStage,
                            AppliedInfoFormIds = current.AppliedInfoFormIds
                                .Append(infoFormId).ToArray(),
                            AccountedCommandCount = current.AccountedCommandCount +
                                appliedCommands,
                            AppliedCommandCount = current.AppliedCommandCount +
                                appliedCommands,
                            NextBoundary = new Fo3Cg01Stage12Boundary(
                                false, birthday.NextBoundaryBlocker),
                        };
                        Persist();
                        StartButchPackageIfEligible();
                    })),
                StringComparer.OrdinalIgnoreCase);
            activations[postIntercom.IntercomReferenceFormId] = ActivateIntercom;
            activations[postIntercom.JonasReferenceFormId] = () =>
            {
                if (current.ActiveStage == reactorGift.SourceStage)
                    StartReactorGiftParticipant(jonasGift);
            };
            activations[postIntercom.DadReferenceFormId] = () =>
            {
                if (current.ActiveStage == postIntercom.GoodbyeStage)
                    ActivateDadPostIntercom();
                else if (current.ActiveStage == reactorGift.JonasStage ||
                         current.ActiveStage == reactorGift.TargetStage ||
                         current.ActiveStage == reactorGift.HitStage ||
                         current.ActiveStage == reactorGift.DeathStage)
                    StartReactorGiftParticipant(dadGift);
            };
            player.ConfigureSourceFormActivations(activations);
            var sourceHits = reactorGift.TargetReferenceFormIds.ToDictionary(
                formId => formId,
                formId => (Action)(() => ApplyTargetHit(formId)),
                StringComparer.OrdinalIgnoreCase);
            sourceHits.Add(reactorGift.Combatant.ReferenceFormId, ApplyCombatHit);
            player.ConfigureSourceHitscan(
                _runtimeConfiguration.Player.DesktopInput.Fire.Action,
                _runtimeConfiguration.Player.FireRayDistanceMeters,
                reactorGift.RequiredWeaponFormId,
                sourceHits);
            if (current.ActiveStage >= postIntercom.SourceStage)
            {
                EnsureJonas();
                var activePackage = current.ActiveStage >= postIntercom.GoodbyeStage
                    ? postIntercom.DadToPlayerPackage.FormId
                    : current.ActiveStage >= postIntercom.AnswerStage
                        ? postIntercom.DadTalkToJonasPackage.FormId
                        : postIntercom.DadToIntercomPackage.FormId;
                _vaultBirthCoverage!.Cg01DadActor.Placement.SetMeta(
                    "opennv_active_package_form_id", activePackage);
                if (current.ActiveStage == postIntercom.SourceStage &&
                    _cg01DadPackageTravelTick is null)
                    StartDadToIntercomTravel();
            }
            if (current.ActiveStage >= reactorGift.JonasStage)
                ExecuteReactorGiftStageCommands(reactorGift.JonasStage);
            if (current.ActiveStage >= reactorGift.TargetStage)
                ExecuteReactorGiftStageCommands(reactorGift.TargetStage);
            if (current.ActiveStage >= reactorGift.RangeStage)
                ExecuteReactorGiftStageCommands(reactorGift.RangeStage);
            if (current.ActiveStage >= reactorGift.HitStage)
                ExecuteReactorGiftStageCommands(reactorGift.HitStage);
            if (current.ActiveStage >= reactorGift.CombatStage)
                ExecuteReactorGiftStageCommands(reactorGift.CombatStage);
            if (current.ActiveStage >= reactorGift.DeathStage)
                ExecuteReactorGiftStageCommands(reactorGift.DeathStage);
            if (current.ActiveStage == picture.SourceStage)
                StartPicturePositioning();
            else if (current.ActiveStage == picture.PictureStage)
            {
                StartPicturePositioning();
                StartPictureJonas();
            }
            else if (current.ActiveStage == pictureCompletion.TimerStage ||
                     current.ActiveStage == pictureCompletion.FlashStage)
                StartPictureCompletion();
            foreach (var targetReferenceFormId in current.Cg02TargetHitFormIds.Distinct(
                StringComparer.OrdinalIgnoreCase))
                Cg01WorldReference(targetReferenceFormId).SetMeta(
                    "opennv_animation_group", reactorGift.TargetAnimationGroup);
            player.SetMeta("opennv_cg02_target_count",
                current.Cg02TargetHitFormIds.Count);
            if (current.CombatHealthByReferenceFormId.TryGetValue(
                    reactorGift.Combatant.ReferenceFormId, out var restoredHealth))
            {
                var restoredCombatant = Cg01WorldReference(
                    reactorGift.Combatant.ReferenceFormId);
                restoredCombatant.SetMeta("opennv_current_health", restoredHealth);
                restoredCombatant.SetMeta("opennv_dead",
                    current.DeadCombatReferenceFormIds.Contains(
                        reactorGift.Combatant.ReferenceFormId,
                        StringComparer.OrdinalIgnoreCase) ? 1 : 0);
                restoredCombatant.SetMeta("opennv_active_package_form_id",
                    reactorGift.Combatant.PackageFormId);
                restoredCombatant.SetMeta("opennv_package_target_form_id",
                    reactorGift.Combatant.PackageTargetFormId);
                restoredCombatant.SetMeta("opennv_package_radius_game_units",
                    reactorGift.Combatant.PackageRadiusGameUnits);
                if (restoredHealth < reactorGift.Combatant.MaximumHealth)
                    restoredCombatant.SetMeta("opennv_combat_target_form_id",
                        reactorGift.Combatant.PlayerReferenceFormId);
            }
            StartButchPackageIfEligible();
            if ((current.ActiveStage == butch.AggregateStage ||
                 current.ActiveStage == butch.SceneDoneStage) &&
                current.TimerAdvancing)
                StartIntercomTimer(current.TimerRemainingSeconds);
            if (current.ActiveStage == cake.TriggerStage &&
                !current.AppliedPackageFormIds.Contains(
                    cake.PackageFormId, StringComparer.OrdinalIgnoreCase))
                StartCake();
            else if (current.ActiveStage == cake.TargetStage &&
                cake.Cues.Any(cue => !current.AppliedInfoFormIds.Contains(
                    cue.InfoFormId, StringComparer.OrdinalIgnoreCase)))
                StartCake();
        }
        void StartDadParty(
            Fo3Cg02DadPartyRuntime party,
            Fo3Cg01ToddlerPlayer player)
        {
            StartCg02DadPartyRuntime(
                party, player, current.AppliedInfoFormIds,
                (infoFormId, appliedCommands) =>
                {
                    current = current with
                    {
                        ActiveStage = party.TargetStage,
                        AppliedInfoFormIds = current.AppliedInfoFormIds
                            .Append(infoFormId).ToArray(),
                        AccountedCommandCount = current.AccountedCommandCount +
                            appliedCommands,
                        AppliedCommandCount = current.AppliedCommandCount +
                            appliedCommands,
                        NextBoundary = new Fo3Cg01Stage12Boundary(
                            false, party.NextBoundaryBlocker),
                    };
                    Persist();
                    InstallBirthday(
                        party.BirthdayInteractionsRuntime ??
                            throw new InvalidOperationException(
                                "Fallout 3 CG02 birthday interactions are absent."),
                        player);
                });
        }
        void StartOverseer(
            Fo3Cg02OverseerSpeechRuntime speech,
            Fo3Cg01ToddlerPlayer player)
        {
            StartCg02OverseerSpeechRuntime(
                speech,
                player,
                current.AppliedInfoFormIds,
                (infoFormId, appliedCommands, activeStage) =>
                {
                    current = current with
                    {
                        ActiveStage = activeStage ?? current.ActiveStage,
                        AppliedInfoFormIds = current.AppliedInfoFormIds
                            .Append(infoFormId).ToArray(),
                        AccountedCommandCount = current.AccountedCommandCount +
                            appliedCommands,
                        AppliedCommandCount = current.AppliedCommandCount +
                            appliedCommands,
                    };
                    Persist();
                },
                () =>
                {
                    if (current.ActiveStage != speech.TargetStage)
                        throw new InvalidOperationException(
                            "Fallout 3 CG02 Overseer completion stage differs.");
                    current = current with
                    {
                        NextBoundary = new Fo3Cg01Stage12Boundary(
                            false, speech.NextBoundaryBlocker),
                    };
                    Persist();
                    StartDadParty(
                        speech.DadPartyRuntime ?? throw new InvalidOperationException(
                            "Fallout 3 CG02 Dad party contract is absent."),
                        player);
                });
        }
        void StartStage50Timer()
        {
            if (!current.TimerAdvancing ||
                current.ActiveStage != interaction.TimerTransition.SourceStage ||
                _cg01Stage50TimerTick is not null)
                throw new InvalidOperationException(
                    "Fallout 3 CG01 stage-50 timer start differs.");
            _cg01Stage50TimerTick = delta =>
            {
                var remaining = Math.Max(0.0, current.TimerRemainingSeconds - delta);
                current = current with { TimerRemainingSeconds = remaining };
                if (remaining > 0.0)
                {
                    Persist();
                    return;
                }
                var applied = interaction.TimerTransition.ExecuteTargetResult();
                current = current with
                {
                    ActiveStage = interaction.TimerTransition.TargetStage,
                    TimerAdvancing = false,
                    AccountedCommandCount = current.AccountedCommandCount + applied,
                    AppliedCommandCount = current.AppliedCommandCount + applied
                };
                (_vaultBirthCoverage ?? throw new InvalidOperationException(
                    "Fallout 3 CG01 stage-70 Dad world is absent."))
                    .Cg01DadActor.Placement.SetMeta("opennv_package_evaluated", true);
                _cg01Stage50TimerTick = null;
                ApplyCg01DadPackage(interaction.TimerTransition.DadReturnPackage, stage5);
                var completionApplied = interaction.TimerTransition.ExecuteCompletionResult();
                SetCg01WorldReferenceLock(current.PlayroomDoorReferenceFormId, 0);
                SetCg01WorldReferenceOpen(current.PlayroomDoorReferenceFormId, true);
                SetCg01WorldReferenceLock(
                    interaction.TimerTransition.MainDoorReferenceFormId,
                    interaction.TimerTransition.MainDoorLockLevel);
                SetCg01WorldReferenceOpen(
                    interaction.TimerTransition.MainDoorReferenceFormId,
                    interaction.TimerTransition.MainDoorOpen);
                current = current with
                {
                    ActiveStage = interaction.TimerTransition.CompletionStage,
                    AppliedPackageFormIds = current.AppliedPackageFormIds
                        .Append(interaction.TimerTransition.DadReturnPackage.FormId).ToArray(),
                    PlayroomDoorOpen = true,
                    PlayroomDoorLockLevel = 0,
                    AccountedCommandCount = current.AccountedCommandCount + completionApplied,
                    AppliedCommandCount = current.AppliedCommandCount + completionApplied
                };
                var dialogueDelay = GetTree().CreateTimer(
                    interaction.TimerTransition.DialogueDelaySeconds);
                dialogueDelay.Timeout += () => PlayCg01DadReturnCue(
                    interaction.TimerTransition.DialogueCues,
                    0,
                    targetStage =>
                    {
                        if (targetStage is null)
                            return true;
                        var sequence = interaction.TimerTransition.DadLead;
                        current = current with { ActiveStage = targetStage.Value };
                        if (targetStage == sequence.BibleTravel.SourceStage)
                        {
                            var applied = ExecuteSourceCommands(
                                sequence.BibleTravel.StageCommands);
                            current = current with
                            {
                                AccountedCommandCount = current.AccountedCommandCount + applied,
                                AppliedCommandCount = current.AppliedCommandCount + applied,
                            };
                            StartCg01DadSourceTravel(
                                sequence,
                                sequence.BibleTravel,
                                interaction.TimerTransition.DadReturnPackage.TargetTransform,
                                stage5,
                                () =>
                                {
                                    var completionApplied = ExecuteSourceCommands(
                                        sequence.BibleTravel.CompletionCommands);
                                    current = current with
                                    {
                                        ActiveStage = sequence.BibleTravel.CompletionStage!.Value,
                                        AppliedPackageFormIds = current.AppliedPackageFormIds
                                            .Append(sequence.BibleTravel.Package.FormId).ToArray(),
                                        AccountedCommandCount = current.AccountedCommandCount + completionApplied,
                                        AppliedCommandCount = current.AppliedCommandCount + completionApplied,
                                    };
                                    PlayCg01DadReturnCue(
                                        interaction.TimerTransition.DialogueCues,
                                        1,
                                        HandleDadReturnStage);
                                });
                            return false;
                        }
                        return HandleDadReturnStage(targetStage);

                        bool HandleDadReturnStage(int? stage)
                        {
                            if (stage is null)
                                return true;
                            current = current with { ActiveStage = stage.Value };
                            if (stage != interaction.TimerTransition.DialogueTargetStage)
                                return true;
                            var leadApplied = ExecuteSourceCommands(sequence.LeadTravel.StageCommands);
                            SetCg01WorldReferenceLock(
                                sequence.UnlockedDoorReferenceFormId, 0);
                            current = current with
                            {
                                AccountedCommandCount = current.AccountedCommandCount + leadApplied,
                                AppliedCommandCount = current.AppliedCommandCount + leadApplied,
                            };
                            StartCg01DadSourceTravel(
                                sequence,
                                sequence.LeadTravel,
                                sequence.BibleTravel.Package.TargetTransform,
                                stage5,
                                () =>
                                {
                                    if (current.ActiveStage == sequence.SayToDoneStage)
                                        Persist();
                                });
                            var sayDoneApplied = ExecuteSourceCommands(sequence.SayToDoneCommands);
                            current = current with
                            {
                                ActiveStage = sequence.SayToDoneStage,
                                DisplayedObjectiveIndex = sequence.DisplayedObjectiveIndex,
                                AppliedPackageFormIds = current.AppliedPackageFormIds
                                    .Append(sequence.LeadTravel.Package.FormId).ToArray(),
                                AccountedCommandCount = current.AccountedCommandCount + sayDoneApplied,
                                AppliedCommandCount = current.AppliedCommandCount + sayDoneApplied,
                            };
                            return false;
                        }
                    });
            };
        }
        void Gate()
        {
            if (current.ActiveStage != interaction.SourceStage)
                return;
            var applied = interaction.ExecuteStageResult(interaction.GateStage);
            SetCg01WorldReferenceOpen(interaction.GateReferenceFormId, true);
            current = current with
            {
                ActiveStage = interaction.GateStage,
                PlaypenGateOpen = true,
                DisplayedObjectiveIndex = interaction.GateStage,
                AccountedCommandCount = current.AccountedCommandCount + applied,
                AppliedCommandCount = current.AppliedCommandCount + applied
            };
            Persist();
        }
        void Exit()
        {
            if (current.ActiveStage != interaction.GateStage)
                return;
            var applied = interaction.ExecuteStageResult(interaction.ExitStage);
            current = current with
            {
                ActiveStage = interaction.ExitStage,
                DisplayedObjectiveIndex = interaction.ExitStage,
                AccountedCommandCount = current.AccountedCommandCount + applied,
                AppliedCommandCount = current.AppliedCommandCount + applied
            };
            Persist();
        }
        void Book()
        {
            if (current.ActiveStage != interaction.ExitStage &&
                    current.ActiveStage != interaction.BookStage ||
                current.ActiveStage == interaction.BookStage && current.SpecialBookAccepted)
                return;
            if (current.ActiveStage < interaction.BookStage)
            {
                var applied = interaction.ExecuteStageResult(interaction.BookStage);
                current = current with
                {
                    ActiveStage = interaction.BookStage,
                    AccountedCommandCount = current.AccountedCommandCount + applied,
                    AppliedCommandCount = current.AppliedCommandCount + applied
                };
                Cg01WorldReference(interaction.BookReferenceFormId)
                    .SetMeta("opennv_special_book_menu_points", interaction.MenuPoints);
                Persist();
            }
            if (_cg01SpecialBookMenu is not null)
                throw new InvalidOperationException(
                    "Fallout 3 SPECIAL book menu is already active.");
            _cg01SpecialBookMenu = new Fo3SpecialBookMenuRuntime(
                interaction,
                (_cg01ToddlerWorld ?? throw new InvalidOperationException(
                    "Fallout 3 SPECIAL input owner is absent.")).Contract,
                current.SpecialValues,
                values =>
                {
                    current = current with { SpecialValues = values };
                    Persist();
                },
                values =>
                {
                    current = current with
                    {
                        SpecialValues = values,
                        SpecialBookAccepted = true,
                        TimerRemainingSeconds = interaction.TimerTransition.InitialSeconds,
                        TimerAdvancing = true
                    };
                    _cg01SpecialBookMenu = null;
                    Persist();
                    StartStage50Timer();
                });
            _cg01SpecialBookMenu.Open(
                Cg01WorldReference(interaction.BookReferenceFormId),
                (_cg01ToddlerWorld ?? throw new InvalidOperationException(
                    "Fallout 3 SPECIAL player is absent.")).Player);
        }
        (_cg01ToddlerWorld ?? throw new InvalidOperationException(
            "Fallout 3 CG01 interaction world is absent."))
            .InstallStage20Interactions(
                _vaultBirthCoverage ?? throw new InvalidOperationException(
                    "Fallout 3 CG01 interaction scene is absent."),
                interaction, Gate, Exit, Book);
        (_cg01ToddlerWorld ?? throw new InvalidOperationException(
            "Fallout 3 CG01 Dad-lead world is absent."))
            .InstallDadLeadEndTrigger(
                _vaultBirthCoverage ?? throw new InvalidOperationException(
                    "Fallout 3 CG01 Dad-lead scene is absent."),
                interaction.TimerTransition.DadLead.EndTrigger,
                () =>
                {
                    var trigger = interaction.TimerTransition.DadLead.EndTrigger;
                    if (current.ActiveStage != trigger.SourceStage)
                        return;
                    var completion = interaction.TimerTransition.DadLead.Completion;
                    current = current with
                    {
                        ActiveStage = trigger.TargetStage,
                        TimerRemainingSeconds = completion.TimerInitialSeconds,
                        TimerAdvancing = true,
                        AccountedCommandCount = current.AccountedCommandCount + 1 +
                            completion.Stage90CommandCount,
                        AppliedCommandCount = current.AppliedCommandCount + 1 +
                            completion.Stage90CommandCount,
                    };
                    var stage90World = _cg01ToddlerWorld ??
                        throw new InvalidOperationException(
                            "Fallout 3 CG01 stage-90 player is absent.");
                    stage90World.Player.SetMeta("opennv_objectives_completed", true);
                    stage90World.Player.SetMeta("opennv_auto_display_objectives", false);
                    stage90World.Player.SetMeta("opennv_quest_updates_enabled", false);
                    StartStage90ImageSpace(completion.ImageSpaceModifier);
                    StartStage90Sound(completion.Sound);
                    _cg01Stage90TimerTick = delta =>
                    {
                        current = current with
                        {
                            TimerRemainingSeconds = Math.Max(
                                0.0, current.TimerRemainingSeconds - delta),
                        };
                        if (current.TimerRemainingSeconds > 0.0)
                            return;
                        _cg01Stage90TimerTick = null;
                        current = current with
                        {
                            ActiveQuestFormId = completion.NextQuestFormId,
                            ActiveQuestEditorId = completion.NextQuestEditorId,
                            ActiveStage = completion.Cg02Stage0.TargetStage,
                            TimerRemainingSeconds = completion.Cg02Stage0.IntroRuntime?.InitialSeconds
                                ?? throw new InvalidOperationException(
                                    "Fallout 3 CG02 intro timer contract is absent."),
                            TimerAdvancing = true,
                            ImageSpaceElapsedSeconds = Math.Min(
                                completion.ImageSpaceModifier.DurationSeconds,
                                _stage90ImageSpaceElapsedSeconds + delta),
                            Stage90SoundStarted = true,
                            AccountedCommandCount = current.AccountedCommandCount +
                                completion.Stage100CommandCount +
                                completion.Cg02Stage0.Stage5CommandCount +
                                completion.Cg02Stage0.Stage0CommandCount,
                            AppliedCommandCount = current.AppliedCommandCount +
                                completion.Stage100CommandCount +
                                completion.Cg02Stage0.Stage5CommandCount +
                                completion.Cg02Stage0.Stage0CommandCount,
                            NextBoundary = new Fo3Cg01Stage12Boundary(
                                false, completion.NextBoundaryBlocker),
                        };
                        var world = _cg01ToddlerWorld ?? throw new InvalidOperationException(
                            "Fallout 3 CG01 completion player is absent.");
                        world.Player.ApplySourceScale(completion.PlayerScale);
                        ApplyCg02Stage5State(world.Player, completion.Cg02Stage0);
                        world.Player.StopAtAuthoredTrigger();
                        world.Player.MoveToSourceTransform(
                            completion.Cg02Stage0.PlayerMoveTransform,
                            (_vaultBirthCoverage ?? throw new InvalidOperationException(
                                "Fallout 3 CG02 player move scene is absent.")).Contract);
                        world.Player.SetMeta("opennv_player_toddler", completion.PlayerToddler);
                        world.Player.SetMeta("opennv_no_activation_sound", false);
                        var dad = _vaultBirthCoverage?.Cg01DadActor.Placement ??
                            throw new InvalidOperationException(
                                "Fallout 3 CG01 completion Dad is absent.");
                        if (!dad.GetMeta("opennv_source_form_id").AsString().Equals(
                                completion.DisabledDadReferenceFormId,
                                StringComparison.OrdinalIgnoreCase))
                            throw new InvalidOperationException(
                                "Fallout 3 CG01 completion Dad identity differs.");
                        dad.Visible = false;
                        dad.ProcessMode = ProcessModeEnum.Disabled;
                        dad.SetMeta("opennv_enabled", 0);
                        _cg02IntroBegin = () => StartCg02IntroRuntime(
                            completion.Cg02Stage0,
                            world.Player,
                            () =>
                            {
                                var intro = completion.Cg02Stage0.IntroRuntime!;
                                current = current with
                                {
                                    ActiveStage = intro.TargetStage,
                                    TimerRemainingSeconds = 0.0,
                                    TimerAdvancing = false,
                                    AccountedCommandCount = current.AccountedCommandCount +
                                        intro.FinalCommandCount,
                                    AppliedCommandCount = current.AppliedCommandCount +
                                        intro.FinalCommandCount,
                                };
                                Persist();
                                var speech = intro.DadSpeechRuntime ??
                                    throw new InvalidOperationException(
                                        "Fallout 3 CG02 Dad speech contract is absent.");
                                StartCg02DadSpeechRuntime(
                                    speech,
                                    world.Player,
                                    current.AppliedInfoFormIds,
                                    infoFormId =>
                                    {
                                        current = current with
                                        {
                                            AppliedInfoFormIds = current.AppliedInfoFormIds
                                                .Append(infoFormId).ToArray(),
                                        };
                                        Persist();
                                    },
                                    () =>
                                    {
                                        current = current with
                                        {
                                            ActiveStage = speech.TargetStage,
                                            AccountedCommandCount = current.AccountedCommandCount +
                                                speech.FinalCommandCount,
                                            AppliedCommandCount = current.AppliedCommandCount +
                                                speech.FinalCommandCount,
                                            NextBoundary = new Fo3Cg01Stage12Boundary(
                                                false, speech.NextBoundaryBlocker),
                                        };
                                        Persist();
                                        StartOverseer(
                                            speech.OverseerSpeechRuntime ??
                                                throw new InvalidOperationException(
                                                    "Fallout 3 CG02 Overseer speech is absent."),
                                            world.Player);
                                    });
                            },
                            current.TimerRemainingSeconds);
                        Persist();
                        StartCg02TransitionMovie(completion.Cg02Stage0.TransitionMovie);
                    };
                });
        var restoredCompletion = interaction.TimerTransition.DadLead.Completion;
        var restoredOverseer = restoredCompletion.Cg02Stage0.IntroRuntime?
            .DadSpeechRuntime?.OverseerSpeechRuntime;
        var restoredParty = restoredOverseer?.DadPartyRuntime;
        var restoredBirthday = restoredParty?.BirthdayInteractionsRuntime;
        var restoredPost = restoredBirthday?.ButchRuntime?.PostIntercomRuntime;
        var restoredGift = restoredPost?.ReactorGiftRuntime;
        var restoredCg03 = restoredGift?.PictureRuntime.CompletionRuntime
            .NextQuestRuntime;
        if ((current.ActiveQuestFormId.Equals(
                restoredCompletion.NextQuestFormId, StringComparison.OrdinalIgnoreCase) &&
            (current.ActiveStage == restoredCompletion.Cg02Stage0.TargetStage ||
             current.ActiveStage == restoredCompletion.Cg02Stage0.IntroRuntime?.TargetStage ||
             current.ActiveStage == restoredCompletion.Cg02Stage0.IntroRuntime?
                 .DadSpeechRuntime?.TargetStage ||
             restoredOverseer is not null &&
                 (current.ActiveStage == restoredOverseer.TargetStage ||
                  restoredOverseer.StageResults.ContainsKey(current.ActiveStage)) ||
             current.ActiveStage == restoredParty?.TargetStage ||
             restoredBirthday?.StageResults.ContainsKey(current.ActiveStage) == true ||
             restoredBirthday?.CakeRuntime is { } restoredCake &&
                 (current.ActiveStage == restoredCake.TriggerStage ||
                  current.ActiveStage == restoredCake.TargetStage) ||
             restoredBirthday?.ButchRuntime is { } restoredButch &&
                 (current.ActiveStage == restoredButch.SceneDoneStage ||
                  current.ActiveStage == restoredButch.AggregateStage ||
                  current.ActiveStage == restoredButch.IntercomStage) ||
             restoredPost is not null &&
                 (current.ActiveStage == restoredPost.AnswerStage ||
                  current.ActiveStage == restoredPost.GoodbyeStage ||
                  current.ActiveStage == restoredPost.TargetStage) ||
             restoredGift is not null &&
                 (current.ActiveStage == restoredGift.JonasStage ||
                  current.ActiveStage == restoredGift.TargetStage ||
                  current.ActiveStage == restoredGift.RangeStage ||
                  current.ActiveStage == restoredGift.HitStage ||
                  current.ActiveStage == restoredGift.CombatStage ||
                  current.ActiveStage == restoredGift.DeathStage ||
                  current.ActiveStage == restoredGift.CompletionStage ||
                  current.ActiveStage == restoredGift.PictureRuntime.PictureStage ||
                  current.ActiveStage == restoredGift.PictureRuntime.TimerStage ||
                  current.ActiveStage == restoredGift.PictureRuntime
                     .CompletionRuntime.FlashStage)) ||
             restoredGift is not null &&
                 current.ActiveQuestFormId.Equals(
                     restoredGift.PictureRuntime.CompletionRuntime.NextQuestFormId,
                     StringComparison.OrdinalIgnoreCase) &&
                 (current.ActiveStage == restoredGift.PictureRuntime
                      .CompletionRuntime.NextQuestTargetStage ||
                  restoredCg03 is not null &&
                  current.ActiveStage == restoredCg03.SpeechStage)))
        {
            (_cg01ToddlerWorld ?? throw new InvalidOperationException(
                "Fallout 3 CG01 restored completion player is absent."))
                .Player.ApplySourceScale(restoredCompletion.PlayerScale);
            var restoredPlayer = _cg01ToddlerWorld.Player;
            restoredPlayer.SetMeta("opennv_player_toddler", restoredCompletion.PlayerToddler);
            restoredPlayer.SetMeta("opennv_no_activation_sound", false);
            restoredPlayer.SetMeta("opennv_objectives_completed", true);
            restoredPlayer.SetMeta("opennv_auto_display_objectives", false);
            restoredPlayer.SetMeta("opennv_quest_updates_enabled", false);
            ApplyCg02Stage5State(restoredPlayer, restoredCompletion.Cg02Stage0);
            restoredPlayer.StopAtAuthoredTrigger();
            restoredPlayer.MoveToSourceTransform(
                restoredCompletion.Cg02Stage0.PlayerMoveTransform,
                (_vaultBirthCoverage ?? throw new InvalidOperationException(
                    "Fallout 3 restored CG02 player move scene is absent.")).Contract);
            if (current.ImageSpaceElapsedSeconds <
                restoredCompletion.ImageSpaceModifier.DurationSeconds)
            {
                StartStage90ImageSpace(restoredCompletion.ImageSpaceModifier);
                _stage90ImageSpaceElapsedSeconds = current.ImageSpaceElapsedSeconds;
            }
            var dad = (_vaultBirthCoverage ?? throw new InvalidOperationException(
                "Fallout 3 CG01 restored completion Dad is absent."))
                .Cg01DadActor.Placement;
            dad.Visible = false;
            dad.ProcessMode = ProcessModeEnum.Disabled;
            dad.SetMeta("opennv_enabled", 0);
            EnsureCg02IntroActors(
                restoredCompletion.Cg02Stage0.IntroRuntime ??
                    throw new InvalidOperationException(
                        "Fallout 3 restored CG02 intro is absent."),
                restoredPlayer);
            if (current.TimerAdvancing)
            {
                StartCg02IntroRuntime(
                    restoredCompletion.Cg02Stage0,
                    restoredPlayer,
                    () =>
                    {
                        var intro = restoredCompletion.Cg02Stage0.IntroRuntime!;
                        current = current with
                        {
                            ActiveStage = intro.TargetStage,
                            TimerRemainingSeconds = 0.0,
                            TimerAdvancing = false,
                            AccountedCommandCount = current.AccountedCommandCount +
                                intro.FinalCommandCount,
                            AppliedCommandCount = current.AppliedCommandCount +
                                intro.FinalCommandCount,
                        };
                        Persist();
                        var speech = intro.DadSpeechRuntime ??
                            throw new InvalidOperationException(
                                "Fallout 3 restored CG02 Dad speech is absent.");
                        StartCg02DadSpeechRuntime(
                            speech,
                            restoredPlayer,
                            current.AppliedInfoFormIds,
                            infoFormId =>
                            {
                                current = current with
                                {
                                    AppliedInfoFormIds = current.AppliedInfoFormIds
                                        .Append(infoFormId).ToArray(),
                                };
                                Persist();
                            },
                            () =>
                            {
                                current = current with
                                {
                                    ActiveStage = speech.TargetStage,
                                    AccountedCommandCount = current.AccountedCommandCount +
                                        speech.FinalCommandCount,
                                    AppliedCommandCount = current.AppliedCommandCount +
                                        speech.FinalCommandCount,
                                    NextBoundary = new Fo3Cg01Stage12Boundary(
                                        false, speech.NextBoundaryBlocker),
                                };
                                Persist();
                                StartOverseer(
                                    speech.OverseerSpeechRuntime ??
                                        throw new InvalidOperationException(
                                            "Fallout 3 restored CG02 Overseer speech is absent."),
                                    restoredPlayer);
                            });
                    },
                    current.TimerRemainingSeconds);
            }
            else if (current.ActiveStage ==
                restoredCompletion.Cg02Stage0.IntroRuntime?.TargetStage)
            {
                var speech = restoredCompletion.Cg02Stage0.IntroRuntime.DadSpeechRuntime ??
                    throw new InvalidOperationException(
                        "Fallout 3 restored CG02 Dad speech is absent.");
                StartCg02DadSpeechRuntime(
                    speech,
                    restoredPlayer,
                    current.AppliedInfoFormIds,
                    infoFormId =>
                    {
                        current = current with
                        {
                            AppliedInfoFormIds = current.AppliedInfoFormIds
                                .Append(infoFormId).ToArray(),
                        };
                        Persist();
                    },
                    () =>
                    {
                        current = current with
                        {
                            ActiveStage = speech.TargetStage,
                            AccountedCommandCount = current.AccountedCommandCount +
                                speech.FinalCommandCount,
                            AppliedCommandCount = current.AppliedCommandCount +
                                speech.FinalCommandCount,
                            NextBoundary = new Fo3Cg01Stage12Boundary(
                                false, speech.NextBoundaryBlocker),
                        };
                        Persist();
                        StartOverseer(
                            speech.OverseerSpeechRuntime ??
                                throw new InvalidOperationException(
                                    "Fallout 3 restored CG02 Overseer speech is absent."),
                            restoredPlayer);
                    });
            }
            else if (restoredOverseer is not null &&
                (current.ActiveStage == restoredOverseer.SourceStage ||
                 current.ActiveStage == restoredOverseer.TargetStage ||
                 restoredOverseer.StageResults.ContainsKey(current.ActiveStage)))
            {
                if (current.ActiveStage != restoredOverseer.TargetStage)
                    StartOverseer(restoredOverseer, restoredPlayer);
                else
                    StartDadParty(
                        restoredParty ?? throw new InvalidOperationException(
                            "Fallout 3 restored CG02 Dad party is absent."),
                        restoredPlayer);
            }
            else if (restoredBirthday is not null &&
                (restoredBirthday.StageResults.ContainsKey(current.ActiveStage) ||
                 restoredBirthday.CakeRuntime is { } cake &&
                    (current.ActiveStage == cake.TriggerStage ||
                     current.ActiveStage == cake.TargetStage) ||
                 restoredBirthday.ButchRuntime is { } butch &&
                    (current.ActiveStage == butch.SceneDoneStage ||
                     current.ActiveStage == butch.AggregateStage ||
                     current.ActiveStage == butch.IntercomStage) ||
                 restoredPost is not null &&
                    (current.ActiveStage == restoredPost.AnswerStage ||
                     current.ActiveStage == restoredPost.GoodbyeStage ||
                     current.ActiveStage == restoredPost.TargetStage) ||
                 restoredGift is not null &&
                    (current.ActiveStage == restoredGift.JonasStage ||
                     current.ActiveStage == restoredGift.TargetStage ||
                     current.ActiveStage == restoredGift.RangeStage ||
                     current.ActiveStage == restoredGift.HitStage ||
                     current.ActiveStage == restoredGift.CombatStage ||
                     current.ActiveStage == restoredGift.DeathStage ||
                     current.ActiveStage == restoredGift.CompletionStage ||
                     current.ActiveStage == restoredGift.PictureRuntime.PictureStage ||
                     current.ActiveStage == restoredGift.PictureRuntime.TimerStage ||
                     current.ActiveStage == restoredGift.PictureRuntime
                         .CompletionRuntime.FlashStage) ||
                 restoredGift is not null &&
                    current.ActiveQuestFormId.Equals(
                        restoredGift.PictureRuntime.CompletionRuntime.NextQuestFormId,
                        StringComparison.OrdinalIgnoreCase) &&
                    (current.ActiveStage == restoredGift.PictureRuntime
                         .CompletionRuntime.NextQuestTargetStage ||
                     restoredCg03 is not null &&
                     current.ActiveStage == restoredCg03.SpeechStage)))
            {
                InstallBirthday(restoredBirthday, restoredPlayer);
            }
        }
        if (current.ActiveStage == interaction.BookStage && !current.SpecialBookAccepted)
            Book();
        else if (current.TimerAdvancing)
            StartStage50Timer();
    }

}
