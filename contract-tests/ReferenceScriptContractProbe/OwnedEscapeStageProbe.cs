using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class OwnedEscapeStageProbe
{
    internal static void Run(string game, string root, string[] dependencies)
    {
        using var content = new FalloutModStackSelection([new("ttw", root, dependencies)]).Resolve(game).OpenSource();
        using var records = FalloutPluginStack.Load(content.PluginSources);
        var quest = FalloutDialogueTopic.Find(records, "QUST", "CG04");
        var script = records.GetEffective(FalloutDialogueTopic.RequiredForm(quest, "SCRI"));
        var bindings = new FalloutScriptBindings(records, quest, script, script.ReadSubrecords());
        var package = records.GetEffective(new("Fallout3.esm", 0x067781));
        var deathCondition = FalloutCondition.Read(package).Single(condition => condition.Function == 84);
        var overseer = bindings.Reference("CG04OverseerREF");
        var wakePackage = FalloutScriptPackage.Read(records.GetEffective(new("FalloutNV.esm", 0x09df06)));
        var wakeEvent = wakePackage.EventPrograms["POBA"];
        var wakeTopic = FalloutDialogueTopic.Find(records, "DIAL", "CG04AmataSpeech");
        var amata = records.GetEffective(new("Fallout3.esm", 0x0230e4));
        if (wakeEvent.Topic != wakeTopic.FormKey || FalloutDialogueTopic.CodeLines(wakeEvent.Source).Any() ||
            wakePackage.Events["POBA"] is null)
            throw new InvalidDataException("Original wake-up event changed its empty result, speech topic or following idle.");
        using var world = new FalloutReferenceWorld(records);
        if (world.Get(overseer).Base != deathCondition.FormArgument1 || world.GetDeadCount(deathCondition.FormArgument1) != 0)
            throw new InvalidDataException("Original Amata predicate does not bind its actual living father's base.");
        var hashes = new[] { quest, script, package, records.GetEffective(deathCondition.FormArgument1),
                wakePackage.EventPrograms["POBA"].Package, wakeTopic, amata }
            .Select(record => (Record: record, Hash: SHA256.HashData(record.ReadData()))).ToArray();
        var effects = new List<FalloutReferenceScriptEffect>();
        var results = new FalloutReferenceScripts(records, world, new(records), new((_, _) => false, effects.Add));
        results.ExecutePackageEvent(wakeEvent, amata.FormKey);
        if (effects.Count != 1 || effects[0].Kind != FalloutReferenceEffectKind.PackageEventTopic ||
            effects[0].Source != amata.FormKey || effects[0].Target != amata.FormKey ||
            effects[0].Argument != wakePackage.Form || effects[0].Topic != wakeTopic.FormKey || effects[0].PackageEvent != "POBA")
            throw new InvalidDataException("Original package topic lost the actual actor and source event identity.");
        if (FalloutCondition.AllPass([deathCondition], condition => world.GetDeadCount(condition.FormArgument1), true))
            throw new InvalidDataException("Original mourning predicate selected a living actor.");
        if (!world.KillActor(overseer, null, 1) || world.GetDeadCount(deathCondition.FormArgument1) != 1 ||
            !FalloutCondition.AllPass([deathCondition], condition => world.GetDeadCount(condition.FormArgument1), true))
            throw new InvalidDataException("Original mourning predicate lost the shared death transition.");
        using var cold = new FalloutReferenceWorld(records);
        cold.Restore(JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))!);
        if (cold.GetDeadCount(deathCondition.FormArgument1) != 1 || cold.KillActor(overseer, null, 1))
            throw new InvalidDataException("Owned death count lost cold state or counted the corpse twice.");
        foreach (var itemCount in new[] { 0, 1 })
        {
            var quests = new FalloutQuestState(records);
            var inventory = new FalloutPlayerInventory();
            var admitted = new List<IReadOnlyList<FalloutPluginSubrecord>>();
            var predicates = new List<FalloutCondition>();
            IEnumerable<bool> Inspect(FalloutPluginRecord _, IReadOnlyList<FalloutPluginSubrecord> entry, string source)
            { admitted.Add(entry); yield return true; }
            var stages = new FalloutQuestStages(records, quests, Inspect, condition =>
            {
                predicates.Add(condition);
                if (condition.Function != 47 || condition.RunOn != 2 ||
                    condition.Owner.Plugin.AdjustFormId(condition.Reference) != records.RuntimeFormKey(0x14))
                    throw new InvalidDataException("Original stage two changed its player inventory condition.");
                if (itemCount == 1 && inventory.Item(condition.FormArgument1) is null)
                    inventory.Add(records, condition.FormArgument1, 1, 1, true);
                return FalloutInventoryConditions.Evaluate(records, inventory, _ => false, condition) ??
                    throw new NotSupportedException("Owned result condition has no inventory owner.");
            }, evaluateRunOn: true);
            stages.Enter(quest.FormKey, 2);
            if (predicates.Count != 1 || admitted.Count != (itemCount == 0 ? 2 : 1) || stages.HasUnfinishedResults)
                throw new InvalidDataException("Original stage two did not select its optional grant and wake-up entries.");
        }
        if (hashes.Any(pair => !pair.Hash.SequenceEqual(SHA256.HashData(pair.Record.ReadData()))))
            throw new InvalidDataException("Escape audit changed winning source bytes.");
        Console.WriteLine("OPENNV_OWNED_ESCAPE_STAGE_PASS originalStage2Entries=true explicitPlayerCount=true " +
            "optionalGrantGuards=true originalAmataPredicate=true actorBase=true sharedDeath=true coldCount=true " +
            "corpseNotRepeated=true originalPackageTopic=true actualTopicCaller=true sourceUnchanged=true " +
            "fixture=isolated-selection-death-and-topic-request nativeVoice=unverified campaign=false framesRecorded=false");
    }
}
