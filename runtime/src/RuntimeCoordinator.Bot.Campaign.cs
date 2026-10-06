using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.Bots;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    private FalloutPluginStack? _campaignGoalStack;
    private FalloutCampaignPortalGraph? _campaignPortals;
    private readonly Dictionary<FalloutFormKey, IReadOnlyList<FalloutQuestTarget>> _campaignTargets = [];

    private BotCampaignObservation ObserveNativeCampaign(IReadOnlyList<BotCampaignControl> controls, string entry)
    {
        var scene = _nativeActiveCell?.Cell.FormKey.ToString() ?? "opening-menu";
        var choices = new List<BotCampaignChoice>();
        if (_nativePlayer is not { } player)
        {
            var start = controls.SingleOrDefault(control => control.Name == (entry == "new" ? "sNew" : "sContinue"));
            if (start is not null) choices.Add(new(start.Name, start.Path, start.Text, "startup", "opening-menu"));
            return new(null, (long)Engine.GetFramesDrawn(), scene, 0, _nativeLoadingLayer is not null ||
                _loadingScreen is not null, GetTree().Paused, false, false, false, [], choices);
        }
        foreach (var control in controls)
            if (control.Kind is "dialogue" or "terminal" or "terminal-back" or "message")
                choices.Add(new(control.Identity ?? throw new InvalidDataException("Source choice has no identity."),
                    control.Path, control.Text, control.Kind,
                    control.Menu ?? throw new InvalidDataException("Source choice has no owning menu."),
                    control.Goodbye || control.Kind == "terminal-back"));
        var binding = NativeBotSkillBinding(player);
        var world = _nativeReferences;
        var driver = _nativeOpeningStageDriver;
        var loading = world is null || driver is null || NativeBotLoading(world);
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
            foreach (var quest in driver!.Quests.Capture().Where(quest =>
                         !quest.Completed && quest.Running == true &&
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
                    foreach (var target in targets)
                    {
                        var objective = driver.Quests.Objective(quest.Quest, target.Objective);
                        if (!objective.Displayed || objective.Completed) continue;
                        if (!FalloutCondition.AllPass(target.Conditions, driver.EvaluateMessageCondition, evaluateRunOn: true)) continue;
                        var identity = quest.Quest + ":" + target.Objective + ":" + target.Ordinal + ":" + scene;
                        try
                        {
                            var placement = world!.Placement(target.Reference);
                            var source = records.GetEffective(target.Reference);
                            var basis = records.GetEffective(FalloutDialogueTopic.RequiredForm(source, "NAME"));
                            var current = _nativeActiveCell!.Cell;
                            var destination = FalloutCellSceneReader.ReadDefinition(records, placement.Cell);
                            var action = target.Reference;
                            var mode = basis.Signature is "STAT" or "LIGH" or "TREE" ? "travel" : "interact";
                            if (placement.Cell != current.FormKey &&
                                (current.Worldspace is null || current.Worldspace != destination.Worldspace))
                            {
                                _campaignPortals ??= new(records);
                                var route = _campaignPortals.Find(current.FormKey, placement.Cell,
                                    reference => world.CanActivate(reference));
                                if (route.Count == 0) throw new InvalidDataException("Cross-cell campaign goal has no source portal action.");
                                action = route[0]; mode = "interact";
                            }
                            else if (placement.Cell != current.FormKey) mode = "travel";
                            goals.Add(new(identity, quest.Quest.ToString(), target.Objective, target.Reference.ToString(),
                                action.ToString(), mode, priority));
                        }
                        catch (Exception targetError) when (targetError is InvalidDataException or
                            InvalidOperationException or NotSupportedException or KeyNotFoundException)
                        {
                            goals.Add(new(identity, quest.Quest.ToString(), target.Objective,
                                target.Reference.ToString(), target.Reference.ToString(), "interact", priority,
                                "Source campaign target " + target.Reference + ": " + targetError.Message));
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
        return new(binding, (long)Engine.GetFramesDrawn(), scene, driver?.Quests.ProgressRevision ?? 0,
            loading, GetTree().Paused, player.ModalInput, defeated, !loading && !player.ModalInput &&
            !GetTree().Paused && NativeActiveMenus()?.Any() != true, goals, choices, save, error,
            _campaignPortals is not null, _campaignPortals?.Issues.Select(issue => issue.Error).ToArray());
    }
}
