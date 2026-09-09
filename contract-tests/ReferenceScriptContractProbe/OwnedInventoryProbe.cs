using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class OwnedInventoryProbe
{
    internal static void Run(string root)
    {
        RuntimeLiveContentSource.Configure(root, RuntimeLiveContentSource.FalloutNewVegasGame);
        using var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        using var world = new FalloutReferenceWorld(records);
        world.LoadCell(FalloutCellSceneReader.Read(records, FalloutDialogueTopic.Find(records, "CELL", "GSDocMitchellHouse").FormKey));
        var quests = new FalloutQuestState(records);
        var player = records.RuntimeFormKey(0x14);
        var speaker = FalloutDialogueTopic.Find(records, "ACHR", "DocMitchellREF").FormKey;
        var inventory = new FalloutPlayerInventory();
        var notices = new List<FalloutReferenceEffectKind>();
        FalloutReferenceScripts Scripts(FalloutPlayerInventory target) => new(records, world, quests, new((_, _) => false, effect =>
        {
            notices.Add(effect.Kind);
            if (effect.Kind == FalloutReferenceEffectKind.AddNote)
            {
                if (target.Item(effect.Target!.Value) is null) target.Add(records, effect.Target.Value, 1, 1, true);
            }
            else if (effect.Kind == FalloutReferenceEffectKind.AddItem && effect.Target == player)
                target.Add(records, effect.Argument!.Value, effect.Value, 1, effect.Enable);
            else throw new NotSupportedException($"Inventory result {effect.Kind} is outside this audit.");
        }, IsPlayerTagSkill: name => name.Equals("Guns", StringComparison.OrdinalIgnoreCase)));
        var grant = FalloutDialogueTopic.Decode(records.GetEffective(records.RuntimeFormKey(0x1057e8)));
        Scripts(inventory).ExecuteResult(grant, speaker, true);
        var weapon = inventory.Items.Single(item => item.RecordType == "WEAP");
        if (weapon.Count != 1 || weapon.Variants?.Single().Condition is not (.3f or .35f or .4f) ||
            inventory.Items.Single(item => item.EditorId == "Stimpak").Count != 4 ||
            inventory.Items.Single(item => item.EditorId == "Caps001").Count != 18 ||
            inventory.Items.Single(item => item.EditorId == "Lockpick").Count != 6 ||
            inventory.Items.Single(item => item.RecordType == "NOTE").Count != 1 ||
            inventory.Items.Any(item => item.RecordType == "LVLI") || notices[0] != FalloutReferenceEffectKind.AddNote)
            throw new InvalidOperationException("Owned farewell results lost inventory, source condition or execution order.");
        foreach (var name in new[] { "PipBoy", "PipBoyGlove", "VaultSuit21" })
        {
            var item = FalloutDialogueTopic.Find(records, "ARMO", name).FormKey;
            inventory.Add(records, item, 1, 1, true); inventory.Equip(records, item);
        }
        if (inventory.Equipped.Count != 3) throw new InvalidOperationException("Owned Pip-Boy, glove and suit slots conflict.");
        var pipBoy = FalloutDialogueTopic.Find(records, "ARMO", "PipBoy").FormKey;
        var itemCount = inventory.Items.Count;
        inventory.Unequip(records, pipBoy);
        if (inventory.Equipped.Count != 2 || inventory.Items.Count != itemCount || inventory.Item(pipBoy)?.Count != 1)
            throw new InvalidOperationException("Unequip removed inventory or retained equipment.");
        inventory.Equip(records, pipBoy);
        var actor = FalloutDialogueTopic.Find(records, "ACHR", "McMurphyREF").FormKey;
        var shovel = FalloutDialogueTopic.Find(records, "WEAP", "dlc04shovel").FormKey;
        world.UnequipItem(actor, shovel, 1);
        var actorItems = world.Inventory(actor, 1);
        if (actorItems.Contents.Item(shovel) is not { Count: 1, Variants: [ { Condition: .45f } ] } || !actorItems.Unequipped.Contains(shovel))
            throw new InvalidOperationException("Original actor cleanup lost its actual inventory condition or unequipped state.");
        using var restoredWorld = new FalloutReferenceWorld(records);
        foreach (var id in new uint[] { 0x104c80, 0x104f03, 0x104f0a })
        {
            var key = records.RuntimeFormKey(id);
            var outfit = world.EquippedArmor(key, 1);
            if (outfit.Count == 0 || world.Inventory(key, 1).Contents.Items.Any(item => item.RecordType == "LVLI"))
                throw new InvalidOperationException("Owned NPC outfit did not consume its retained inventory selection.");
            var beforeOutfit = JsonSerializer.Serialize(world.Inventory(key, 1).Capture());
            _ = world.EquippedArmor(key, 1);
            if (beforeOutfit != JsonSerializer.Serialize(world.Inventory(key, 1).Capture()))
                throw new InvalidOperationException("Repeated actor presentation rerolled its outfit or changed its equipment.");
        }
        restoredWorld.Restore(JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))!);
        foreach (var id in new uint[] { 0x104c80, 0x104f03, 0x104f0a })
        {
            var key = records.RuntimeFormKey(id);
            if (!world.EquippedArmor(key, 1).SequenceEqual(restoredWorld.EquippedArmor(key, 1)))
                throw new InvalidOperationException("Cold actor appearance changed its selected outfit.");
        }
        if (JsonSerializer.Serialize(restoredWorld.Inventory(actor, 1).Capture()) != JsonSerializer.Serialize(actorItems.Capture()))
            throw new InvalidOperationException("Cold actor inventory lost its source items or script equipment changes.");
        var saved = JsonSerializer.Deserialize<FalloutOpeningInventoryGrant>(JsonSerializer.Serialize(inventory.Capture()))!;
        var cold = new FalloutPlayerInventory();
        cold.Restore(saved.Inventory, saved.EquippedRuntimeFormIds.ToArray(), saved.InventoryRandomState);
        Scripts(inventory).ExecuteResult(grant, speaker, true);
        Scripts(cold).ExecuteResult(grant, speaker, true);
        if (JsonSerializer.Serialize(inventory.Capture()) != JsonSerializer.Serialize(cold.Capture()))
            throw new InvalidOperationException("Cold inventory lost item variants, equipment or its next leveled-list draw.");
        Console.WriteLine("OPENNV_OWNED_INVENTORY_PASS sourceInfo=true tagBranches=true leveledChance=true weaponCondition=true equipmentSlots=true coldRandomContinuation=true retailRandomAndPixels=unverified");
    }
}
