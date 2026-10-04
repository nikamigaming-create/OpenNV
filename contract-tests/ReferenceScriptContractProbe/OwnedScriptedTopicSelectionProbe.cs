using System.Security.Cryptography;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class OwnedScriptedTopicSelectionProbe
{
    internal static void ImmediateTtw(string game, string root, string[] dependencies)
    {
        using var content = new FalloutModStackSelection([new("ttw", root, dependencies)]).Resolve(game).OpenSource();
        using var records = FalloutPluginStack.Load(content.PluginSources);
        using var world = new FalloutReferenceWorld(records);
        var quest = FalloutDialogueTopic.Find(records, "QUST", "CG03");
        var caller = FalloutDialogueTopic.Find(records, "ACHR", "CG03AmataREF");
        var topic = FalloutDialogueTopic.Read(records, "CG03BullyIntroConv");
        var expected = new FalloutFormKey("Fallout3.esm", 0x854f7);
        var info = topic.Infos.Single(value => value.Record.FormKey == expected);
        var sourceHash = SHA256.HashData(info.Record.ReadData());
        var quests = new FalloutQuestState(records); quests.EnterStage(quest.FormKey, 20);
        var slots = FalloutScriptLocals.Read(FalloutScriptLocals.AttachedScript(records, quest)!);
        var index = slots["bullyConv"];
        var identity = FalloutDialogueSpeaker.Read(records, FalloutDialogueTopic.RequiredForm(caller, "NAME"));
        var context = new FalloutDialogueConditions(records, quests, caller.FormKey, identity,
            runtime: value => value.Function == 53 ? (float)world.ReadVariable(quests, value.FormArgument1, value.Argument2) :
                throw new NotSupportedException("Original immediate dialogue reached an unowned fixture query."),
            listener: FalloutDialogueTopic.Find(records, "ACHR", "CG03ButchREF").FormKey);
        if ((info.Flags & 8) == 0 || !FalloutDialogueTopic.Eligible(info, identity.Actor, new HashSet<FalloutFormKey>(),
            key => quests.Stage(key), context.Evaluate))
            throw new InvalidDataException("Original immediate dialogue no longer owns the selected initial result.");
        FalloutDialogueTopic.RequireFlags(info, conversation: false, npcConversation: true, immediateResults: true);
        var scripts = new FalloutReferenceScripts(records, world, quests,
            new((_, _) => false, _ => throw new NotSupportedException("Original immediate result requested an unrelated fixture effect.")));
        scripts.ExecuteResult(info, caller.FormKey, true);
        if (quests.Variable(quest.FormKey, index) != 0)
            throw new InvalidDataException("Original immediate begin block changed the end-result variable.");
        scripts.ExecuteResult(info, caller.FormKey, false);
        if (quests.Variable(quest.FormKey, index) != 1)
            throw new InvalidDataException("Original immediate end block did not execute at dialogue generation.");
        var cold = new FalloutQuestState(records); cold.Restore(quests.Capture());
        if (cold.Variable(quest.FormKey, index) != 1 || !sourceHash.SequenceEqual(SHA256.HashData(info.Record.ReadData())))
            throw new InvalidDataException("Immediate source result lost its cold quest value or changed source bytes.");
        Console.WriteLine($"OPENNV_OWNED_IMMEDIATE_DIALOGUE_PASS info={expected} speaker={caller.FormKey} " +
            "sourceFlags=true beginEndOrder=true originalEndResult=true coldQuestValue=true sourceUnchanged=true " +
            "fixture=isolated-stage-and-result audioAndCampaign=separate parity=unverified");
    }

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
                53 => (float)world.ReadVariable(state, condition.FormArgument1, condition.Argument2),
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
