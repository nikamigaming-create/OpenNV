using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static class OwnedObjectiveContinuationProbe
{
    internal static void Run(string root)
    {
        RuntimeLiveContentSource.Configure(root, RuntimeLiveContentSource.FalloutNewVegasGame,
            activePlugins: RuntimeLiveContentSource.ResolveVanillaPlugins(root), includeImplicitPlugins: false);
        using var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        using var world = new FalloutReferenceWorld(records);
        var quests = new FalloutQuestState(records);
        var quest = FalloutDialogueTopic.Find(records, "QUST", "VMQ01").FormKey;
        var marker = FalloutDialogueTopic.Find(records, "REFR", "PrimmMapMarker").FormKey;
        var info = FalloutDialogueTopic.Decode(records.GetEffective(records.RuntimeFormKey(0x107afc)));
        var speaker = records.RuntimeFormKey(0x104e85);
        // Pin the observed absent-index command and its following real effect.
        var objective = FalloutQuestState.ReadObjectiveCommands(info.BeginScript).Single();
        if (objective.QuestEditorId != "VMQ01" || objective.Index != 15 || objective.Display || !objective.Value ||
            quests.ObjectiveCompleted(quest, 15) || quests.Capture().Single(value => value.Quest == quest).Objectives!.Any(value => value.Index == 15) ||
            world.MapMarker(marker).Visible)
            throw new InvalidDataException("Owned direction result differs from the observed absent-objective case.");
        var before = JsonSerializer.Serialize(quests.Capture());
        var revision = quests.Revision;
        var scripts = new FalloutReferenceScripts(records, world, quests,
            new((_, _) => false, _ => throw new InvalidDataException("Unexpected direction effect.")));
        scripts.ExecuteResult(info, speaker, true);
        scripts.ExecuteResult(info, speaker, false);
        if (!world.MapMarker(marker).Visible || quests.Revision != revision ||
            JsonSerializer.Serialize(quests.Capture()) != before)
            throw new InvalidDataException("Absent objective stopped its following map command or invented quest state.");
        Console.WriteLine("OPENNV_OWNED_OBJECTIVE_CONTINUATION_PASS absentIndexNoMutation=true followingMapCommand=true sourceReadonly=true gameplay=unverified");
    }
}
