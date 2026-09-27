using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

internal static class QuestScriptSaveProbe
{
    internal static void Run(string root, string path)
    {
        RuntimeLiveContentSource.Configure(root, RuntimeLiveContentSource.FalloutNewVegasGame);
        using var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        var save = JsonSerializer.Deserialize<FalloutNativeCampaignState>(File.ReadAllText(path)) ??
            throw new InvalidDataException("Owned save is empty.");
        var snapshot = save.Scripts ?? throw new InvalidDataException("Owned save has no script state.");
        var quests = new FalloutQuestState(records);
        quests.Restore(save.Quests ?? throw new InvalidDataException("Owned save has no quest state."));
        var originalQuests = JsonSerializer.Serialize(quests.Capture());
        FalloutQuestScripts Scripts() => new(records, quests, new HashSet<FalloutFormKey>(), new());
        var scripts = Scripts(); scripts.Restore(snapshot);
        var restored = scripts.Capture();
        var owners = restored.Instances.ToDictionary(instance => instance.Quest);
        foreach (var original in snapshot.Instances)
            if (!owners.TryGetValue(original.Quest, out var current) || current != original)
                throw new InvalidOperationException($"Owned quest script state changed on restore: {original.Quest}.");
        var cold = Scripts(); cold.Restore(restored);
        if (JsonSerializer.Serialize(cold.Capture()) != JsonSerializer.Serialize(restored) ||
            JsonSerializer.Serialize(quests.Capture()) != originalQuests)
            throw new InvalidOperationException("Owned quest/script state changed on cold restoration.");
        Console.WriteLine($"OPENNV_OWNED_QUEST_SAVE_PASS parser={snapshot.ParserVersion}->{restored.ParserVersion} " +
            $"retained={snapshot.Instances.Count} newlyAdmitted={restored.Instances.Count - snapshot.Instances.Count} " +
            "clocks=true failures=true questProgress=true coldRoundTrip=true sourceReadOnly=true");
    }
}
