using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class OwnedDialogueInventoryProbe
{
    internal static void Run(string game, string mod, string root, string actorId, string topicId, string[] dependencies)
    {
        using var content = new FalloutModStackSelection([new(mod, root, dependencies)]).Resolve(game).OpenSource();
        using var records = FalloutPluginStack.Load(content.PluginSources);
        using var world = new FalloutReferenceWorld(records);
        var actor = FalloutDialogueTopic.Find(records, "ACHR", actorId);
        var identity = FalloutDialogueSpeaker.Read(records, FalloutDialogueTopic.RequiredForm(actor, "NAME"));
        var topic = FalloutDialogueTopic.Read(records, topicId);
        var conditions = topic.Infos.SelectMany(info => info.Conditions.Select(data => FalloutCondition.Read(info.Record, data)))
            .Where(condition => condition.Function == 47).ToArray();
        if (conditions.Length == 0) throw new InvalidDataException("Source topic has no inventory conditions.");
        var player = new FalloutPlayerInventory(123); var playerReference = records.RuntimeFormKey(0x14);
        var commands = new FalloutInventoryCommands(records, world, player, () => 1);
        var fixtureItems = conditions.SelectMany(condition => Entries(condition.FormArgument1)).Distinct()
            .Where(key => records.GetEffective(key).Signature is "MISC" or "ALCH" or "WEAP" or "ARMO" or "KEYM" or "AMMO").ToArray();
        foreach (var item in fixtureItems) player.Add(records, item, 3, 1, true);
        var quests = new FalloutQuestState(records);
        foreach (var condition in conditions)
        {
            var before = SHA256.HashData(condition.Owner.ReadData());
            var expectedSubject = condition.RunOn switch
            {
                0 => actor.FormKey,
                1 => playerReference,
                2 => condition.Owner.Plugin.AdjustOptionalFormId(condition.Reference) ?? throw new InvalidDataException("Item-count subject is absent."),
                _ => throw new NotSupportedException("Source item-count scope is unbound."),
            };
            var subjectInventory = expectedSubject == playerReference ? player : world.Inventory(expectedSubject, 1).Contents;
            var snapshot = JsonSerializer.Serialize(subjectInventory.Capture());
            FalloutFormKey? queried = null, argument = null;
            double Query(FalloutFormKey reference, FalloutFormKey item) { queried = reference; argument = item; return commands.ItemCount(reference, item); }
            var context = new FalloutDialogueConditions(records, quests, actor.FormKey, identity,
                runtime: _ => throw new InvalidOperationException("Inventory query escaped its state owner."), itemCount: Query);
            var expected = Total(subjectInventory, condition.FormArgument1);
            if (context.Evaluate(condition) != (float)expected || queried != expectedSubject || argument != condition.FormArgument1 ||
                snapshot != JsonSerializer.Serialize(subjectInventory.Capture()))
                throw new InvalidDataException("Source inventory subject, argument, retained count or read-only state changed.");
            var listenerInventory = world.Inventory(actor.FormKey, 1).Contents;
            var directed = new FalloutDialogueConditions(records, quests, playerReference, identity,
                listener: actor.FormKey, listenerIdentity: identity, itemCount: Query);
            if (directed.Evaluate(condition with { RunOn = 1 }) != (float)Total(listenerInventory, condition.FormArgument1) ||
                queried != actor.FormKey || !before.SequenceEqual(SHA256.HashData(condition.Owner.ReadData())))
                throw new InvalidDataException("NPC listener used another inventory or changed source bytes.");
        }
        foreach (var item in fixtureItems) player.Remove(item, 1, true);
        var changed = new FalloutDialogueConditions(records, quests, actor.FormKey, identity, itemCount: commands.ItemCount);
        foreach (var condition in conditions.Where(condition => condition.RunOn == 1))
            if (changed.Evaluate(condition) != (float)Total(player, condition.FormArgument1))
                throw new InvalidDataException("Source inventory query cached a stale player count.");
        Console.WriteLine($"OPENNV_OWNED_DIALOGUE_INVENTORY_PASS topic={topic.Topic.FormKey} queries={conditions.Length} " +
            $"self={conditions.Count(value => value.RunOn == 0)} target={conditions.Count(value => value.RunOn == 1)} " +
            $"explicit={conditions.Count(value => value.RunOn == 2)} lists={conditions.Count(value => records.GetEffective(value.FormArgument1).Signature == "FLST")} " +
            "npcListener=true liveChanges=true sourceReadonly=true inventoryState=fixture ordinaryInput=separate parity=unverified");

        IEnumerable<FalloutFormKey> Entries(FalloutFormKey key)
        {
            var form = records.GetEffective(key);
            if (form.Signature != "FLST") return [key];
            return form.ReadSubrecords().Where(field => field.Signature == "LNAM").Select(field =>
            {
                if (field.Data.Length != 4) throw new InvalidDataException("Source form-list item extent is invalid.");
                return form.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span));
            }).ToArray();
        }
        double Total(FalloutPlayerInventory inventory, FalloutFormKey argument) => Entries(argument)
            .SelectMany(entry => inventory.Items.Where(item => item.FormKey == entry && item.RecordType != "NOTE"))
            .Sum(item => (double)item.Count);
    }
}
