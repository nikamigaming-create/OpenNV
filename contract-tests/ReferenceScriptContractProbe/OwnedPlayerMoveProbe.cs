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
        var fields = script.ReadSubrecords().ToArray();
        var source = fields.Single(field => field.Signature == "SCTX").Data;
        var menu = FalloutGameModeProgram.Read(source.Span, "MenuMode");
        var effects = new List<FalloutReferenceScriptEffect>();
        var executor = new FalloutReferenceScripts(records, world, quests, new((_, _) => false, effect =>
        {
            if (effect.Kind != FalloutReferenceEffectKind.ReferenceEnable)
                throw new NotSupportedException($"Owned movement fixture has no presentation for {effect.Kind}.");
            effects.Add(effect);
        }, Globals: globals));
        quests.SetRunning(quest.FormKey, true);
        executor.ExecuteProgram(quest, script, menu, .01);
        var move = world.PlayerMoves.Next ?? throw new InvalidDataException("Owned startup source queued no movement.");
        if (move.Source != quest.FormKey) throw new InvalidDataException("Owned move has a different source owner.");
        var origin = world.Placement(move.Destination);
        var destination = world.ResolvePlayerMove(move, origin, 1);
        var after = JsonSerializer.Serialize(quests.Capture());
        executor.ExecuteProgram(quest, script, menu, .01);
        if (!ReferenceEquals(move, world.PlayerMoves.Next) || JsonSerializer.Serialize(quests.Capture()) != after)
            throw new InvalidDataException("Owned startup repeated its movement prefix.");
        world.PlayerMoves.Complete(move);
        if (world.PlayerMoves.Pending) throw new InvalidDataException("Owned startup queued duplicate movement.");
        using var cold = new FalloutReferenceWorld(records);
        cold.Restore(JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))!);
        if (cold.PlayerMoves.Pending) throw new InvalidDataException("Cold owner retained startup movement.");
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            schema = "opennv-owned-player-move-audit/v1", source = quest.FormKey, script = script.FormKey,
            winner = quest.Plugin.Name, setup.ActivePlugins, move, destination,
            followingStatements = "executed-through-shared-quest-and-reference-owners", effects = effects.Select(effect => effect.Kind),
            prefixNotRepeated = true, cold = true, recording = false,
            boundary = "explicit-owned-MenuMode-fixture; ordinary-campaign-start-travel-and-presentation-unverified"
        }));
    }
}
