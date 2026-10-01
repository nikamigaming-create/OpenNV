using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class OwnedPlayerMoveProbe
{
    internal static void Run(string mod, string root, string game, string questId, string[] dependencies)
    {
        var setup = new FalloutModStackSelection([new(mod, root, dependencies)]).Resolve(game);
        using var content = setup.OpenSource();
        using var records = FalloutPluginStack.Load(content.PluginSources);
        using var world = new FalloutReferenceWorld(records);
        var quests = new FalloutQuestState(records);
        var globals = FalloutGlobalState.Read(records);
        var quest = FalloutDialogueTopic.Find(records, "QUST", questId);
        var script = records.GetEffective(FalloutDialogueTopic.RequiredForm(quest, "SCRI"));
        var claimed = records.EffectiveRecords("QUST").Where(record => record.FormKey != quest.FormKey).Select(record => record.FormKey).ToHashSet();
        var defaultDelay = FalloutInstallationSettings.Read(content).Number("MAIN", "fQuestScriptDelayTime");
        var scripts = new FalloutQuestScripts(records, quests, claimed, new FalloutPlayerInventory(), globals,
            defaultProcessingDelay: defaultDelay, references: world);
        var bootstrap = new FalloutNewGameBootstrap(records, FalloutInstallationSettings.Read(content), quests, scripts, world,
            (_, _, command, _) => throw new NotSupportedException($"Owned startup fixture has no presentation for {command}."),
            effect => throw new NotSupportedException($"Owned startup fixture has no presentation for {effect.Kind}."), () => true, globals);
        if (bootstrap.Quest.FormKey != quest.FormKey) throw new InvalidDataException("Selected startup quest differs from the requested owned fixture.");
        bootstrap.Start();
        bootstrap.Advance(1, [4]);
        var move = world.PlayerMoves.Next ?? throw new InvalidDataException("Owned startup source queued no movement.");
        if (move.Source != quest.FormKey) throw new InvalidDataException("Owned move has a different source owner.");
        var origin = world.Placement(move.Destination);
        var destination = world.ResolvePlayerMove(move, origin, 1);
        var after = JsonSerializer.Serialize(quests.Capture());
        bootstrap.Advance(1, [4]);
        if (!ReferenceEquals(move, world.PlayerMoves.Next) || JsonSerializer.Serialize(quests.Capture()) != after)
            throw new InvalidDataException("Owned startup repeated its movement prefix.");
        world.PlayerMoves.Complete(move);
        if (world.PlayerMoves.Pending) throw new InvalidDataException("Owned startup queued duplicate movement.");
        scripts.Advance(1);
        var hasChoice = scripts.TryTakeMessage(out var choice);
        var startup = scripts.Capture().Instances.Single(instance => instance.Quest == quest.FormKey);
        if (!hasChoice || choice is not { Buttons.Count: 2 } || choice.Request?.Caller != script.FormKey || startup.Error is not null)
            throw new InvalidDataException($"Owned startup did not publish its two-choice source message through the quest clock: " +
                $"error={startup.Error}, message={choice?.Form}, buttons={choice?.Buttons.Count}, caller={choice?.Request?.Caller}.");
        using var cold = new FalloutReferenceWorld(records);
        cold.Restore(JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))!);
        if (cold.PlayerMoves.Pending) throw new InvalidDataException("Cold owner retained startup movement.");
        var coldQuests = new FalloutQuestState(records); coldQuests.Restore(quests.Capture());
        var coldScripts = new FalloutQuestScripts(records, coldQuests, claimed, new FalloutPlayerInventory(), globals,
            defaultProcessingDelay: defaultDelay, references: cold);
        coldScripts.Restore(JsonSerializer.Deserialize<FalloutQuestScriptsSnapshot>(JsonSerializer.Serialize(scripts.Capture(choice)))!);
        if (!coldScripts.TryTakeMessage(out var restoredChoice) || restoredChoice?.Request != choice.Request ||
            coldScripts.Menus.Query() != 0 || coldQuests.Variable(quest.FormKey, FalloutScriptLocals.Read(script)["iDoOnce"]) != 2)
            throw new InvalidDataException("Cold startup lost its source choice/prefix or retained a transient menu frame.");
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            schema = "opennv-owned-player-move-audit/v1", source = quest.FormKey, script = script.FormKey,
            winner = quest.Plugin.Name, setup.ActivePlugins, move, destination,
            followingStatements = "executed-through-shared-quest-and-reference-owners", startup = bootstrap.State,
            menuScheduling = "shared-quest-clock-with-owned-default-and-authored-delay", campaignChoice = new { choice.Form, buttons = choice.Buttons.Count },
            prefixNotRepeated = true, cold = true, recording = false,
            boundary = "explicit-owned-MenuMode-fixture; ordinary-campaign-start-travel-and-presentation-unverified"
        }));
    }
}
