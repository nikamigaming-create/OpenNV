using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.Bots;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.Presentation.Ui;
using OpenNV.Runtime.World.Actors;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    private FalloutPluginStack? _campaignGoalStack;
    private FalloutCampaignPortalGraph? _campaignPortals;
    private readonly Dictionary<FalloutFormKey, IReadOnlyList<FalloutQuestTarget>> _campaignTargets = [];
    private readonly HashSet<FalloutFormKey> _botCombatQuestObjectives = [];

    private BotCampaignObservation ObserveNativeCampaign(IReadOnlyList<BotCampaignControl> controls, string entry)
    {
        var scene = _nativeActiveCell?.Cell.FormKey.ToString() ?? "opening-menu";
        var choices = new List<BotCampaignChoice>();
        if (_nativePlayer is not { } player)
        {
            var start = entry == "new" ? controls.SingleOrDefault(control => control.Name == "sYes" &&
                control.Path.Contains("/StartMenu/SourceCanvas/confirm_container/", StringComparison.Ordinal)) : null;
            start ??= controls.SingleOrDefault(control => control.Name == (entry == "new" ? "sNew" : "sContinue"));
            if (start is not null) choices.Add(new(start.Name, start.Path, start.Text, "startup", "opening-menu"));
            return new(null, (long)Engine.GetProcessFrames(), scene, 0, _nativeLoadingLayer is not null ||
                _loadingScreen is not null, GetTree().Paused, false, false, false, [], choices);
        }
        // The dialogue remains drawn beneath barter. Only its topmost modal
        // can receive pointer input until the player closes it.
        foreach (var control in ReactiveCampaignBot.ActiveMenuControls(controls))
            if (control.Kind is "dialogue" or "terminal" or "terminal-back" or "barter-back" or "message" or "chargen" or "special")
                choices.Add(new(control.Identity ?? throw new InvalidDataException("Source choice has no identity."),
                    control.Path, control.Text, control.Kind,
                    control.Menu ?? throw new InvalidDataException("Source choice has no owning menu."),
                    control.Goodbye || control.Kind == "terminal-back"));
        var binding = NativeBotSkillBinding(player);
        var world = _nativeReferences;
        var driver = _nativeOpeningStageDriver;
        var movieActive = _nativeBootstrapMovie || GetChildren().OfType<NativeGamebryoMovie>().Any(movie => !movie.IsQueuedForDeletion()) ||
            driver?.GetChildren().OfType<NativeGamebryoMovie>().Any(movie => !movie.IsQueuedForDeletion()) == true;
        var loading = world is null || driver is null || movieActive || NativeBotLoading(world);
        var defeated = _nativeDeathPresented || player.IsDefeated?.Invoke() == true;
        var error = driver?.BlockingExecutionFault ?? _nativeQuestScripts?.StartupError ??
            world?.PlayerMoves.Error ?? _nativeReferencePresentation?.Error ?? _nativeTerminalMenu?.Error;
        var goals = new List<BotCampaignGoal>();
        if (!loading && !defeated && !player.ModalInput && !GetTree().Paused)
        {
            var records = _nativePluginStack ?? throw new InvalidOperationException("Campaign has no winning source stack.");
            if (!ReferenceEquals(records, _campaignGoalStack))
            {
                _campaignGoalStack = records; _campaignTargets.Clear(); _campaignPortals = null;
            }
            var questSnapshots = driver!.Quests.Capture();
            var hasActiveObjective = questSnapshots.Any(quest => quest.Active && !quest.Completed &&
                quest.Objectives?.Any(objective => objective.Displayed && !objective.Completed) == true);
            _botCombatQuestObjectives.Clear();
            foreach (var quest in questSnapshots.Where(quest => quest.Active && !quest.Completed))
                if (quest.Objectives?.Any(objective => objective.Displayed && !objective.Completed &&
                    ReactiveCampaignBot.RequestsDefeat(driver.Quests.ObjectiveText(quest.Quest, (uint)objective.Index))) == true)
                    _botCombatQuestObjectives.Add(quest.Quest);
            foreach (var quest in questSnapshots.Where(quest =>
                         !quest.Completed && (quest.Running == true || quest.Active) &&
                         quest.Objectives?.Any(objective => objective.Displayed && !objective.Completed) == true))
            {
                var priority = quest.Active ? 1000 : 0;
                var header = records.GetEffective(quest.Quest).ReadSubrecords().Single(field => field.Signature == "DATA").Data;
                if (header.Length is not (2 or 8)) throw new InvalidDataException("Campaign quest priority has an invalid source extent.");
                priority += header.Span[1];
                try
                {
                    if (!_campaignTargets.TryGetValue(quest.Quest, out var targets))
                        _campaignTargets.Add(quest.Quest, targets = FalloutQuestTargets.Read(records, quest.Quest));
                    var questGoalsBefore = goals.Count;
                    foreach (var target in targets)
                    {
                        var objective = driver.Quests.Objective(quest.Quest, target.Objective);
                        if (!objective.Displayed || objective.Completed) continue;
                        if (!FalloutCondition.AllPass(target.Conditions, condition => driver.EvaluateTargetCondition(target.Reference, condition), evaluateRunOn: true)) continue;
                        try
                        {
                            var source = records.GetEffective(target.Reference);
                            var basis = records.GetEffective(FalloutDialogueTopic.RequiredForm(source, "NAME"));
                            var actorTarget = source.Signature is "ACHR" or "ACRE";
                            var placement = actorTarget
                                ? world!.SpatialPlacement(target.Reference, null, _configuration.World.GameUnitsToMeters)
                                : world!.Placement(target.Reference);
                            var current = _nativeActiveCell!.Cell;
                            var destination = FalloutCellSceneReader.ReadDefinition(records, placement.Cell);
                            var targetPriority = priority;
                            if (!hasActiveObjective && (placement.Cell == current.FormKey ||
                                current.Worldspace is not null && current.Worldspace == destination.Worldspace))
                            {
                                var sourcePosition = new Vector3(placement.Position[0], placement.Position[2], -placement.Position[1]) *
                                    _configuration.World.GameUnitsToMeters;
                                targetPriority += ReactiveCampaignBot.NearbyUnselectedQuestBonus(player.GlobalPosition.DistanceTo(sourcePosition));
                            }
                            var action = target.Reference;
                            var objectiveText = driver.Quests.ObjectiveText(quest.Quest, target.Objective);
                            var defeatObjective = ReactiveCampaignBot.RequestsDefeat(objectiveText);
                            var hostile = actorTarget && defeatObjective && world.IsResident(target.Reference) &&
                                NativeBotHostileToPlayer(world, world.Get(target.Reference), records.RuntimeFormKey(0x14));
                            var mode = ReactiveCampaignBot.SourceTargetMode(basis.Signature is "STAT" or "LIGH" or "TREE",
                                actorTarget, defeatObjective, hostile);
                            if (actorTarget && ReactiveCampaignBot.RequestsFollow(objectiveText) &&
                                world.CurrentPackage(target.Reference) is { } targetPackage &&
                                FalloutPackageData.Read(records.GetEffective(targetPackage)).Procedure is 1 or 2 or 6)
                            {
                                mode = "follow";
                            }
                            if (source.Signature is "ACHR" or "ACRE" && world.IsDead(target.Reference) && mode == "shoot") continue;
                            if (placement.Cell != current.FormKey &&
                                (current.Worldspace is null || current.Worldspace != destination.Worldspace))
                            {
                                _campaignPortals ??= new(records);
                                var route = _campaignPortals.Find(current.FormKey, placement.Cell,
                                    reference => world.CanActivate(reference) &&
                                        (world.Lock(reference) is not { } gate ||
                                            gate.Key is { } key && _nativeInventory.Item(key) is not null));
                                if (route.Count == 0) throw new InvalidDataException("Cross-cell campaign goal has no source portal action.");
                                action = route[0];
                                mode = world.IsResident(action) ? "interact" : "travel";
                            }
                            else if (mode != "follow" && !world.IsResident(target.Reference) && placement.Cell != current.FormKey) mode = "travel";
                            var identity = quest.Quest + ":" + target.Objective + ":" + target.Ordinal + ":" + mode + ":" + scene;
                            goals.Add(new(identity, quest.Quest.ToString(), target.Objective, target.Reference.ToString(),
                                action.ToString(), mode, targetPriority));
                        }
                        catch (Exception targetError) when (targetError is InvalidDataException or
                            InvalidOperationException or NotSupportedException or KeyNotFoundException)
                        {
                            var identity = quest.Quest + ":" + target.Objective + ":" + target.Ordinal + ":interact:" + scene;
                            goals.Add(new(identity, quest.Quest.ToString(), target.Objective,
                                target.Reference.ToString(), target.Reference.ToString(), "interact", priority,
                                "Source campaign target " + target.Reference + ": " + targetError.Message));
                        }
                    }
                    // Quest-linked hostile actors may not have acquired the
                    // player yet. An explicit defeat objective lets the input
                    // driver approach their real resident bodies rather than
                    // treating a map landmark as the required interaction.
                    foreach (var objective in quest.Objectives?.Where(objective => objective.Displayed && !objective.Completed &&
                                 ReactiveCampaignBot.RequestsDefeat(driver.Quests.ObjectiveText(quest.Quest, (uint)objective.Index))) ?? [])
                    {
                        var candidates = world!.ResidentInstances.Where(instance =>
                                instance.Script?.ReferencedForms.Contains(quest.Quest) == true &&
                                !instance.Unconscious && !instance.Restrained && world.IsEnabled(instance.Reference) &&
                                _nativeReferencePresentation?.Nodes.TryGetValue(instance.Reference, out var candidateNode) == true &&
                                candidateNode is RuntimeNativeNpc or RuntimeNativeCreature &&
                                GodotObject.IsInstanceValid(candidateNode) && candidateNode.IsInsideTree() &&
                                !world.IsDead(instance.Reference) &&
                                NativeBotHostileToPlayer(world, instance, records.RuntimeFormKey(0x14)))
                            .OrderBy(instance => player.GlobalPosition.DistanceSquaredTo(
                                _nativeReferencePresentation!.Nodes[instance.Reference].GlobalPosition));
                        foreach (var candidate in candidates)
                        {
                            var identity = quest.Quest + ":" + objective.Index + ":actor:" + candidate.Reference + ":" + scene;
                            goals.Add(new(identity, quest.Quest.ToString(), (uint)objective.Index,
                                candidate.Reference.ToString(), candidate.Reference.ToString(), "shoot", priority + 1));
                        }
                    }
                    if (goals.Count == questGoalsBefore)
                    {
                        var unfulfilled = quest.Objectives?.Where(obj => obj.Displayed && !obj.Completed).ToArray() ?? [];
                        foreach (var objective in unfulfilled)
                        {
                            var candidateReferences = world!.ResidentInstances
                                .Where(instance => instance.Script?.ReferencedForms.Contains(quest.Quest) == true &&
                                    records.TryGetEffective(instance.Base, out var baseRecord) && baseRecord.Signature is not ("NPC_" or "CREA") &&
                                    !records.GetEffective(instance.Reference).ReadSubrecords().Any(field => field.Signature == "XPRM") &&
                                    world.IsEnabled(instance.Reference) && !instance.Deleted && !instance.DeletePending &&
                                    !instance.Taken)
                                .OrderBy(instance => instance.Reference.ObjectId)
                                .ToArray();

                            foreach (var candidate in candidateReferences)
                            {
                                if (candidate.Variables.Values.Any(v => v != 0)) continue;
                                var identity = quest.Quest + ":" + objective.Index + ":" + candidate.Reference + ":" + scene;
                                goals.Add(new(identity, quest.Quest.ToString(), (uint)objective.Index,
                                    candidate.Reference.ToString(), candidate.Reference.ToString(), "shoot", priority));
                            }

                            if (goals.Count == questGoalsBefore && targets.Count > 0)
                            {
                                var fallbackTarget = targets[0].Reference;
                                var identity = quest.Quest + ":" + objective.Index + ":fallback:" + fallbackTarget + ":" + scene;
                                goals.Add(new(identity, quest.Quest.ToString(), (uint)objective.Index,
                                    fallbackTarget.ToString(), fallbackTarget.ToString(), "interact", priority - 10));
                            }
                        }
                    }
                }
                catch (Exception questError) when (questError is InvalidDataException or
                    InvalidOperationException or NotSupportedException or KeyNotFoundException)
                {
                    goals.Add(new(quest.Quest + ":unowned:" + scene, quest.Quest.ToString(), 0, "", "", "interact",
                        priority, "Source campaign quest " + quest.Quest + ": " + questError.Message));
                }
            }
        }
        var save = _nativeManualSaves.Receipt is { } receipt
            ? new BotCampaignSave(receipt.Generation, receipt.Disposition, receipt.CommittedSlot?.Id, receipt.Error) : null;
        var canSave = !loading && !player.ModalInput && !GetTree().Paused &&
            NativeActiveMenus()?.Any() != true &&
            NativeSourceManualSaveBlocker() is null &&
            !_nativeManualSaves.Pending &&
            _nativeManualSavePreparation is null &&
            (_nativeQuestScripts is null || !_nativeQuestScripts.Scripts.Sounds.HasActiveLoops) &&
            (world is null || !world.Sounds.HasActiveLoops) &&
            world is not null &&
            world.PendingProcedureCaptureCount == 0 &&
            world.PendingHitEventCount == 0 &&
            !world.PlayerMoves.Pending &&
            driver is not null && !driver.SpeechActive &&
            driver.PresentedConversationSpeaker is null &&
            driver.ObserveManualSaveAdmission().Kind != RuntimeManualSaveAdmissionKind.Refused;
        return new(binding, (long)Engine.GetProcessFrames(), scene, driver?.Quests.ProgressRevision ?? 0,
            loading, GetTree().Paused, player.ModalInput, defeated, canSave, goals, choices, save, error,
            _campaignPortals is not null, _campaignPortals?.Issues.Select(issue => issue.Error).ToArray(),
            driver?.SpeechActive == true);
    }
}
