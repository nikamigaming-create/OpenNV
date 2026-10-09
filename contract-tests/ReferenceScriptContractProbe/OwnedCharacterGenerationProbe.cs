using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class OwnedCharacterGenerationProbe
{
    internal static void Run(string mod, string root, string baseRoot, string questId, string[] dependencies)
    {
        var setup = new FalloutModStackSelection([new(mod, root, dependencies)]).Resolve(baseRoot);
        RuntimeLiveContentSource.Configure(baseRoot, RuntimeLiveContentSource.FalloutNewVegasGame,
            setup.ContentRoots.Skip(1).ToArray(), setup.ActivePlugins, setup.Settings);
        using var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        using var world = new FalloutReferenceWorld(records);
        var quest = FalloutDialogueTopic.Find(records, "QUST", questId);
        var commands = quest.ReadSubrecords().Where(field => field.Signature == "SCTX")
            .SelectMany(field => FalloutDialogueTopic.CodeLines(FalloutDialogueTopic.ScriptText(field.Data.Span)))
            .Where(line => line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[0].Equals("SetInCharGen", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (commands.Length == 0) throw new InvalidDataException("Selected owned quest has no character-generation policy command.");
        var player = records.GetEffective(records.RuntimeFormKey(7));
        var data = player.ReadSubrecords().Single(field => field.Signature == "DATA").Data.Span;
        if (data.Length != 11) throw new InvalidDataException("Selected owned player SPECIAL layout is unbound.");
        var special = new FalloutNativeSpecialState(data[4], data[5], data[6], data[7], data[8], data[9], data[10]);
        var vitals = new FalloutPlayerVitals(records, player.FormKey, special);
        var session = new FalloutScriptSession();
        var executor = new FalloutReferenceScripts(records, world, new FalloutQuestState(records), new((_, _) => false, effect =>
        {
            if (effect.Kind != FalloutReferenceEffectKind.CharacterGeneration) throw new InvalidDataException("Unexpected owned component effect.");
            session.SetInCharGen(effect.Enable);
        }, InCharGen: () => session.InCharGen));
        var script = FalloutScriptLocals.AttachedScript(records, quest) ?? throw new InvalidDataException("Owned quest has no compiled script owner.");
        foreach (var command in commands)
            executor.ExecuteProgram(quest, script, FalloutGameModeProgram.Read("begin GameMode\n" + command + "\nend"), 0);
        if (!session.InCharGen) throw new InvalidDataException("Selected owned component did not retain character-generation mode.");
        vitals.Publish(vitals.State with { ExperiencePoints = checked(vitals.State.NextLevelExperiencePoints + 1) });
        var coldSession = new FalloutScriptSession();
        coldSession.Restore(JsonSerializer.Deserialize<FalloutScriptSessionSnapshot>(JsonSerializer.Serialize(session.Capture()))!);
        var cold = new FalloutPlayerVitals(records, player.FormKey, special,
            JsonSerializer.Deserialize<GameplayVitals>(JsonSerializer.Serialize(vitals.State))!);
        coldSession.SetInCharGen(false);
        if (coldSession.InCharGen || cold.State.ExperiencePoints != vitals.State.ExperiencePoints || cold.State.Level != vitals.State.Level)
            throw new InvalidDataException("Owned character-generation publication consumed deferred XP or failed to publish its source flag.");
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            schema = "opennv-owned-character-generation-audit/v1", quest = quest.FormKey,
            winner = quest.Plugin.Name, commands = commands.Length, level = cold.State.Level,
            deferredExperience = cold.State.ExperiencePoints, nextLevelExperience = cold.State.NextLevelExperiencePoints,
            inCharGen = coldSession.InCharGen, cold = true, advancement = "requires-later-admitted-player-update", recording = false,
            boundary = "isolated-owned-command-component; XP-rewards-leveling-and-campaign-parity-unverified"
        }));
    }
}
