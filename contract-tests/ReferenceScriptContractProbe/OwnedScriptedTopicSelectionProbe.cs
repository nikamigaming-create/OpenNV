using System.Security.Cryptography;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class OwnedScriptedTopicSelectionProbe
{
    internal static void Run(string game, string mod, string root, string actorId, string topicId,
        string questId, short stage, string packageIdentity, string expectedIdentity, string[] dependencies)
    {
        using var content = new FalloutModStackSelection([new(mod, root, dependencies)]).Resolve(game).OpenSource();
        using var records = FalloutPluginStack.Load(content.PluginSources);
        using var world = new FalloutReferenceWorld(records);
        var actor = new[] { "ACHR", "ACRE", "REFR" }.SelectMany(records.EffectiveRecords).Single(record =>
            record.ReadSubrecords().Any(field => field.Signature == "EDID" &&
                FalloutDialogueTopic.Text(field.Data.Span).Equals(actorId, StringComparison.OrdinalIgnoreCase)));
        var identity = FalloutDialogueSpeaker.Read(records, FalloutDialogueTopic.RequiredForm(actor, "NAME"));
        var topic = FalloutDialogueTopic.Read(records, topicId);
        var quest = FalloutDialogueTopic.Find(records, "QUST", questId);
        var package = packageIdentity == "none" ? (FalloutFormKey?)null : Parse(packageIdentity); var expected = Parse(expectedIdentity);
        if (package is { } packageKey && records.GetEffective(packageKey).Signature != "PACK" || records.GetEffective(expected).Signature != "INFO")
            throw new InvalidDataException("Selected package/response fixture has a wrong source type.");
        var sourceRecords = new[] { actor, topic.Topic, quest, records.GetEffective(expected) }.AsEnumerable();
        if (package is { } ownedPackage) sourceRecords = sourceRecords.Append(records.GetEffective(ownedPackage));
        var hashes = sourceRecords
            .ToDictionary(record => record.FormKey, record => SHA256.HashData(record.ReadData()));
        var quests = new FalloutQuestState(records); quests.SetRunning(quest.FormKey, true); quests.EnterStage(quest.FormKey, stage);
        var globals = FalloutGlobalState.Read(records);
        var inventory = new FalloutInventoryCommands(records, world, new FalloutPlayerInventory(), () => 1, globals);
        var queries = new FalloutActorQueries();
        using var vampire = queries.BindVampire(() => FalloutExecutableStringTable.ReadVampireQueryDeclaration(Path.Combine(game, "FalloutNV.exe")));
        FalloutDialogueConditions Context(FalloutQuestState state) => new(records, state, actor.FormKey, identity,
            condition => FalloutPlatformConditions.Evaluate(condition) ?? condition.Function switch
            {
                74 => globals.Get(condition.FormArgument1),
                53 => (float)world.Get(condition.FormArgument1).Read(condition.Argument2),
                _ => throw new NotSupportedException($"Isolated source selection query {condition.Function}/{condition.RunOn} is unbound."),
            }, factions: world.ActorFactions, playerFemale: () => false,
            currentPackage: reference => reference == actor.FormKey ? package : null,
            vampireQuery: queries.GetVampire, itemCount: inventory.ItemCount);
        FalloutDialogueInfo Select(FalloutQuestState state)
        {
            var selected = new FalloutDialogueQuestSelection(records, state).Select(topic, identity.Actor,
                new HashSet<FalloutFormKey>(), key => state.Stage(key), Context(state).Evaluate, _ => 0);
            if (selected is null || selected.Record.FormKey != expected || selected.Responses.Count == 0)
                throw new InvalidDataException($"Owned scripted topic selected {selected?.Record.FormKey.ToString() ?? "none"}, expected {expected}; " +
                    $"speakerBase={identity.Actor} quest={quest.FormKey} stage={stage}.");
            return selected;
        }
        var selected = Select(quests);
        var cold = new FalloutQuestState(records); cold.Restore(quests.Capture()); Select(cold);
        foreach (var (key, hash) in hashes)
            if (!hash.SequenceEqual(SHA256.HashData(records.GetEffective(key).ReadData())))
                throw new InvalidDataException("Scripted selection changed owned source bytes.");
        Console.WriteLine($"OPENNV_OWNED_SCRIPTED_TOPIC_SELECTION_PASS actor={actor.FormKey} topic={topic.Topic.FormKey} " +
            $"info={selected.Record.FormKey} quest={selected.Quest} sourceReadonly=true coldQuestSelection=true " +
            "fixture=explicit-stage-package-empty-player-inventory-male-context-zero-random-draw resultsAudioAndCampaign=separate parity=unverified");
    }

    private static FalloutFormKey Parse(string identity)
    {
        var fields = identity.Split(':');
        return fields.Length == 2 ? new(fields[0], Convert.ToUInt32(fields[1], 16)) :
            throw new InvalidDataException("Owned selection fixture identity must be plugin:hexForm.");
    }
}
