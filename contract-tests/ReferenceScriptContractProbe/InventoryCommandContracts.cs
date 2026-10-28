using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class InventoryCommandContracts
{
    internal static void Run(bool projectLocal = false)
    {
        var directory = projectLocal ? Directory.CreateDirectory(Path.Combine("local", "inventory-commands-" + Guid.NewGuid().ToString("N")))
            : Directory.CreateTempSubdirectory("opennv-inventory-commands-");
        try
        {
            var header = new byte[12]; BinaryPrimitives.WriteSingleLittleEndian(header, 1.34f);
            var questItem = Item("MISC", 4, "QuestItem", 8); BinaryPrimitives.WriteUInt32LittleEndian(questItem.AsSpan(8), 0x400);
            File.WriteAllBytes(Path.Combine(directory.FullName, "Items.esm"), Join(
                Record("TES4", 0, Field("HEDR", header)), Item("WEAP", 1, "Gun", 15), Item("MISC", 2, "Loot", 8),
                Record("SCPT", 0x200, Field("SCTX", Text("scn PickupScript\nbegin OnAdd player\nreturn\nend")), Field("SCRO", BitConverter.GetBytes(0x14u))),
                Record("SCPT", 0x201, Field("SCTX", Text("scn ActivateScript\nbegin OnActivate\nreturn\nend"))),
                Record("SCPT", 0x202, Field("SCTX", Text("scn OtherRecipient\nbegin OnAdd Actor\nreturn\nend")), Field("SCRO", BitConverter.GetBytes(0x900u))),
                Record("MISC", 9, Field("EDID", Text("ScriptedPickup")), Field("DATA", new byte[8]), Field("SCRI", BitConverter.GetBytes(0x200u))),
                Record("MISC", 10, Field("EDID", Text("ActivateOnly")), Field("DATA", new byte[8]), Field("SCRI", BitConverter.GetBytes(0x201u))),
                Record("MISC", 11, Field("EDID", Text("OtherRecipientItem")), Field("DATA", new byte[8]), Field("SCRI", BitConverter.GetBytes(0x202u))),
                Record("LVLI", 12, Field("EDID", Text("MixedList")), Field("LVLF", [4]), Field("LVLD", [0]),
                    Field("LVLO", LeveledEntry(2)), Field("LVLO", LeveledEntry(9))),
                Armor(3, "Protected", 6, true), questItem, Armor(5, "Suit", 2), Armor(6, "OtherSuit", 2),
                Record("NPC_", 7, Field("CNTO", Join(BitConverter.GetBytes(2u), BitConverter.GetBytes(3)))),
                Item("WEAP", 8, "OtherGun", 15), Record("NPC_", 0x100, Field("ACBS", new byte[24]),
                    Field("CNTO", Join(BitConverter.GetBytes(2u), BitConverter.GetBytes(3)))),
                Record("CREA", 0x101, Field("ACBS", new byte[24]), Field("CNTO", Join(BitConverter.GetBytes(2u), BitConverter.GetBytes(2)))),
                Record("CONT", 0x110), Record("ACTI", 0x111), Record("CELL", 0x800, Field("DATA", [1])), Group(0x800,
                    Join(Reference("ACHR", 0x900, 0x100, "Actor"), Reference("REFR", 0x901, 0x110, "Chest"),
                        Reference("ACRE", 0x902, 0x101, "Creature"), Reference("REFR", 0x903, 0x111, "Activator"))),
                Record("QUST", 0x600, [Field("EDID", Text("InventoryQuest")), .. new uint[] { 0x14, 1, 2, 3, 4, 5, 6, 8, 0x900, 0x901 }
                    .Concat([0x902u, 0x903u]).Select(id => Field("SCRO", BitConverter.GetBytes(id)))])));
            using var records = FalloutPluginStack.Load(directory.FullName, ["Items.esm"]);
            using var world = new FalloutReferenceWorld(records);
            world.LoadCell(FalloutCellSceneReader.Read(records, Key(0x800)));
            var inventory = new FalloutPlayerInventory(123);
            inventory.Replace(world.Inventory(Key(0x14), 1).Contents.Capture());
            world.BindPlayerInventory(inventory);
            Require(inventory.Items is [{ Count: 3, FormKey: var initialItem }] && initialItem == Key(2) &&
                ReferenceEquals(world.Inventory(Key(0x14), 1).Contents, inventory) &&
                world.Capture().All(value => value.Reference != Key(0x14)),
                "Engine player inventory lost its source base or created a reference record.");
            inventory.Remove(Key(2), 3, true);
            var commands = new FalloutInventoryCommands(records, world, inventory, () => 1);
            var tradePlayer = new FalloutPlayerInventory(100);
            var tradeMerchant = new FalloutPlayerInventory(101);
            tradePlayer.Add(records, Key(1), 1, 1, true, extra: new(1, .4f, Key(0x100)));
            tradePlayer.Equip(records, Key(1), true);
            tradeMerchant.Add(records, Key(1), 1, 1, true, extra: new(1, .8f, Key(0x100)));
            tradePlayer.Exchange(tradeMerchant, [new(Key(1), 1, false)]);
            Require(tradePlayer.Item(Key(1)) is { Count: 2, UnequipLocked: true, Variants: { } tradeVariants } &&
                tradeVariants.Any(value => value.Condition == .4f) && tradeVariants.Any(value => value.Condition == .8f) &&
                tradePlayer.IsEquipped(records, Key(1)) && !tradePlayer.Unequip(records, Key(1)),
                "Trade replaced the destination's equipped lock or item variants.");
            var tradeSnapshot = tradePlayer.Capture();
            var tradeCold = new FalloutPlayerInventory();
            tradeCold.Restore(tradeSnapshot.Inventory, tradeSnapshot.EquippedRuntimeFormIds.ToArray(), tradeSnapshot.InventoryRandomState);
            Require(tradeCold.Item(Key(1))?.UnequipLocked == true && !tradeCold.Unequip(records, Key(1)),
                "Cold trade inventory lost the existing equipment lock.");
            var equippedBefore = JsonSerializer.Serialize(tradePlayer.Capture());
            Reject(() => tradePlayer.RemoveVariant(Key(1), 0, 1, true, tradePlayer.Revision));
            Require(JsonSerializer.Serialize(tradePlayer.Capture()) == equippedBefore,
                "Partial selected removal lost an unbound worn instance.");
            var atomic = new FalloutPlayerInventory(1234);
            var atomicBefore = JsonSerializer.Serialize(atomic.Capture()); var external = 0;
            Reject(() => atomic.AddWithPreparedOwner(records, Key(2), 2, 1, false,
                new(() => { external = 1; throw new InvalidDataException("Prepared owner refused commit."); }, () => external = 0)));
            Require(external == 0 && JsonSerializer.Serialize(atomic.Capture()) == atomicBefore &&
                atomic.Notifications.Capture().LastOrdinal == 0, "Failed addition did not restore inventory, owner and HUD.");
            Reject(() => atomic.AddWithPreparedOwner(records, Key(2), 2, 1, false,
                new(() => atomic.Add(records, Key(2), 1, 1, true), () => { })));
            Require(JsonSerializer.Serialize(atomic.Capture()) == atomicBefore && atomic.Notifications.Capture().LastOrdinal == 0,
                "Nested addition interleaved a prepared inventory transaction.");
            atomic.AddWithPreparedOwner(records, Key(2), 2, 1, false, new(() => external = 1, () => external = 0));
            Require(atomic.Item(Key(2))?.Count == 2 && external == 1 && atomic.Notifications.Capture().LastOrdinal == 1,
                "Prepared addition did not commit inventory, owner and one source notice.");
            var selected = new FalloutPlayerInventory(333);
            selected.Add(records, Key(2), 2, 1, true, extra: new(2, .3f, Key(0x100)));
            var staleSelection = selected.Revision;
            selected.Add(records, Key(2), 1, 1, true, extra: new(1, .8f, Key(0x101)));
            var selectedBefore = JsonSerializer.Serialize(selected.Capture());
            Reject(() => selected.RemoveVariant(Key(2), 0, 1, false, staleSelection));
            Reject(() => selected.RemoveVariant(Key(2), 1, 2, false, selected.Revision));
            Require(JsonSerializer.Serialize(selected.Capture()) == selectedBefore && selected.Notifications.Capture().LastOrdinal == 0,
                "Rejected selected removal changed inventory or HUD.");
            selected.RemoveVariant(Key(2), 1, 1, false, selected.Revision);
            Require(selected.Item(Key(2)) is { Count: 2, Variants: { Count: 1 } surviving } &&
                surviving[0] is { Count: 2, Condition: .3f } && surviving[0].Owner == Key(0x100) &&
                selected.Notifications.Capture().LastOrdinal == 1,
                "Selected removal changed the wrong condition or ownership variant.");
            var selectedCold = new FalloutPlayerInventory(); var selectedSave = selected.Capture();
            selectedCold.Restore(selectedSave.Inventory, selectedSave.EquippedRuntimeFormIds.ToArray(), selectedSave.InventoryRandomState);
            Require(JsonSerializer.Serialize(selectedCold.Capture()) == JsonSerializer.Serialize(selectedSave),
                "Selected variant removal did not survive cold inventory restore.");
            var beforeScriptedPickup = JsonSerializer.Serialize(inventory.Capture());
            Reject(() => commands.Execute(new(FalloutInventoryCommandKind.Add, Key(0x14), Item: Key(9), Silent: false)));
            Require(beforeScriptedPickup == JsonSerializer.Serialize(inventory.Capture()) && inventory.Notifications.Capture().LastOrdinal == 0,
                "Unowned item OnAdd changed inventory or published a notice.");
            commands.Execute(new(FalloutInventoryCommandKind.Add, Key(0x14), Item: Key(10)));
            Require(inventory.Item(Key(10))?.Count == 1, "An activation-only item was refused as an OnAdd event.");
            inventory.Remove(Key(10), 1, true);
            commands.Execute(new(FalloutInventoryCommandKind.Add, Key(0x14), Item: Key(11)));
            Require(inventory.Item(Key(11))?.Count == 1, "A nonmatching actor filter blocked player acquisition.");
            inventory.Remove(Key(11), 1, true);
            Reject(() => FalloutReferenceWorld.RequirePlayerItemAcquisition(records, records.GetEffective(Key(11)), Key(0x900)));
            Reject(() => FalloutReferenceWorld.RequirePlayerItemAcquisition(records, records.GetEffective(Key(11))));
            var beforeLeveled = JsonSerializer.Serialize(inventory.Capture());
            Reject(() => commands.Execute(new(FalloutInventoryCommandKind.Add, Key(0x14), Item: Key(12))));
            Require(beforeLeveled == JsonSerializer.Serialize(inventory.Capture()),
                "Refused selected item from a leveled list changed inventory or random state.");
            inventory.Add(records, Key(2), 1, 1, true);
            var craft = new FalloutRecipe(records.GetEffective(Key(0x600)), "Synthetic", "Synthetic", -1, 0, null, null, [],
                [new(Key(2), 1)], [new(Key(12), 1)]);
            var beforeCraft = JsonSerializer.Serialize(inventory.Capture());
            Reject(() => inventory.Craft(records, craft, 1, null));
            Require(beforeCraft == JsonSerializer.Serialize(inventory.Capture()) && inventory.Notifications.Capture().LastOrdinal == 0,
                "Refused scripted recipe output consumed ingredients, advanced random state or published a notice.");
            inventory.Remove(Key(2), 1, true);
            var executor = new FalloutReferenceScripts(records, world, new(records), new((_, _) => false,
                _ => throw new InvalidDataException("Inventory command escaped its shared owner."), Inventory: commands));
            var quest = records.GetEffective(Key(0x600));
            void Script(string source) => executor.ExecuteStage(quest, quest.ReadSubrecords().ToArray(), source);
            foreach (var id in new uint[] { 1, 3, 4, 5, 6, 8 }) inventory.Add(records, Key(id), 1, 1, true);
            Script("player.EquipItem Gun 1 1\nplayer.EquipItem Suit 0 1\nplayer.EquipItem Protected 0 1");
            Require(commands.EquippedObject(Key(0x14), 5) == Key(1) && inventory.Item(Key(1))!.UnequipLocked,
                "Explicit equip did not publish base-form weapon identity and lock state.");
            Script("if player.GetEquippedObject 5 != Gun\nUnexpectedWeapon\nendif\nif 0\nplayer.EquipItem OtherGun 3 1\nendif");
            Require(!inventory.Unequip(records, Key(1)), "Ordinary input removed locked equipment.");
            inventory.Add(records, Key(1), 1, 1, true);
            Require(inventory.Item(Key(1))!.UnequipLocked, "Adding another item erased equipment lock.");
            Script("player.EquipItem OtherGun 0 1");
            Require(commands.EquippedObject(Key(0x14), 5) == Key(1), "A conflicting equip displaced locked gear.");
            var saved = JsonSerializer.Deserialize<FalloutOpeningInventoryGrant>(JsonSerializer.Serialize(inventory.Capture()))!;
            var cold = new FalloutPlayerInventory(); cold.Restore(saved.Inventory, saved.EquippedRuntimeFormIds.ToArray(), saved.InventoryRandomState);
            using (var coldPlayerWorld = new FalloutReferenceWorld(records))
            {
                coldPlayerWorld.BindPlayerInventory(cold);
                Require(ReferenceEquals(coldPlayerWorld.Inventory(Key(0x14), 1).Contents, cold) &&
                    coldPlayerWorld.Inventory(Key(0x14), 1).Contents.Item(Key(1))!.UnequipLocked,
                    "Cold engine player inventory was replaced by source defaults.");
            }
            Require(cold.Item(Key(1))!.UnequipLocked && !cold.Unequip(records, Key(1)), "Cold equipment lost no-unequip state.");
            Reject(() => new FalloutPlayerInventory().Restore(saved.Inventory, [], saved.InventoryRandomState));
            Require(cold.Unequip(records, Key(1), force: true) && !cold.Item(Key(1))!.UnequipLocked,
                "Explicit script unequip did not release equipment lock.");
            var equippedCount = inventory.Item(Key(1))!.Count;
            Script("player.UnequipItem Gun 0 1");
            Require(inventory.Item(Key(1)) is { UnequipLocked: false } && inventory.Item(Key(1))!.Count == equippedCount &&
                commands.EquippedObject(Key(0x14), 5) is null,
                "Original three-argument UnequipItem consumed gear or retained its no-unequip lock.");
            Script("player.EquipItem Gun 1 1");
            var unequipBefore = JsonSerializer.Serialize(inventory.Capture());
            Reject(() => Script("player.UnequipItem Gun 1 1"));
            Reject(() => Script("player.UnequipItem Gun 0 0"));
            Reject(() => Script("player.UnequipItem Gun"));
            Reject(() => Script("player.UnequipItem Gun 2 1"));
            Reject(() => Script("player.UnequipItem Gun 0 2"));
            Require(JsonSerializer.Serialize(inventory.Capture()) == unequipBefore,
                "Unowned admission/notification or invalid UnequipItem flags changed equipment.");
            Script("player.RemoveAllItems\nplayer.AddItem Suit 1 1\nplayer.EquipItem Suit 0 1\nplayer.EquipItem Protected 0 1");
            Require(inventory.Items.Select(item => item.FormKey).ToHashSet().SetEquals(new[] { Key(3), Key(4), Key(5) }) &&
                inventory.Equipped.Count == 2 && commands.EquippedObject(Key(0x14), 5) is null,
                "RemoveAllItems lost protected items or kept removed equipment.");
            Script("if player.GetEqObj 5 != 0\nUnexpectedWeapon\nendif");
            var before = JsonSerializer.Serialize(inventory.Capture());
            Reject(() => Script("player.EquipItem Suit 2 1"));
            Reject(() => Script("player.EquipItem Suit 0 0"));
            Require(JsonSerializer.Serialize(inventory.Capture()) == before, "Rejected equip flags changed inventory.");
            inventory.Add(records, Key(2), 2, 1, true, extra: new(2, .4f, Key(0x100)));
            var chest = world.Inventory(Key(0x901), 1).Contents;
            chest.Add(records, Key(2), 1, 1, true, extra: new(1, .8f, Key(0x100)));
            Script("player.RemoveAllItems Chest 0 1");
            Require(chest.Item(Key(2))!.Variants!.Any(value => value.Count == 2 && value.Condition == .4f && value.Owner is null) &&
                chest.Item(Key(2))!.Variants!.Any(value => value.Count == 1 && value.Condition == .8f && value.Owner == Key(0x100)),
                "Transfer lost condition or changed pre-existing ownership.");
            inventory.Add(records, Key(2), 1, 1, true, extra: new(1, .6f, Key(0x100)));
            Script("player.RemoveAllItems Chest 1 1");
            Require(chest.Item(Key(2))!.Variants!.Any(value => value.Condition == .6f && value.Owner == Key(0x100)),
                "Retain ownership did not preserve transferred instance ownership.");
            var actor = world.Inventory(Key(0x900), 1);
            var contents = actor.Contents;
            actor.Contents.Remove(Key(2), 2, true); actor.Contents.Add(records, Key(1), 1, 1, true);
            actor.Contents.Equip(records, Key(1), true); actor.Unequipped.Add(Key(5));
            var revision = world.ActorAppearanceRevision(Key(0x900));
            Script("Actor.ResetInventory");
            Require(ReferenceEquals(contents, actor.Contents) && actor.Contents.Items is [{ Count: 3, FormKey: var key }] && key == Key(2) &&
                actor.Contents.Equipped.Count == 0 && actor.Unequipped.Count == 0 && world.ActorAppearanceRevision(Key(0x900)) > revision,
                "ResetInventory stranded an owner, lost default contents or retained equipment overrides.");
            using var restored = new FalloutReferenceWorld(records);
            restored.Restore(JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))!);
            Require(JsonSerializer.Serialize(restored.Inventory(Key(0x900), 1).Capture()) == JsonSerializer.Serialize(actor.Capture()),
                "Cold reference restore changed rebuilt inventory or its random continuation.");
            CountCommands(records, world, inventory, quest);
            var overflow = new FalloutPlayerInventory(456); overflow.Add(records, Key(2), int.MaxValue, 1, true);
            inventory.Add(records, Key(2), 1, 1, true);
            before = JsonSerializer.Serialize(inventory.Capture()); var targetBefore = JsonSerializer.Serialize(overflow.Capture());
            Reject(() => inventory.RemoveAll(records, true, overflow, false, false));
            Require(before == JsonSerializer.Serialize(inventory.Capture()) && targetBefore == JsonSerializer.Serialize(overflow.Capture()),
                "Failed whole-container transfer published a partial transaction.");
            var hudOverflow = new FalloutPlayerInventory(789);
            hudOverflow.Notifications.Restore(new(long.MaxValue, null, 0, []));
            var pickupBefore = JsonSerializer.Serialize(hudOverflow.Capture());
            Reject(() => hudOverflow.Add(records, Key(2), 1, 1, false));
            Require(pickupBefore == JsonSerializer.Serialize(hudOverflow.Capture()) &&
                hudOverflow.Notifications.Capture().LastOrdinal == long.MaxValue,
                "Refused pickup notification changed inventory or its random continuation.");
            hudOverflow.Add(records, Key(2), 1, 1, true);
            Require(hudOverflow.Item(Key(2))?.Count == 1, "Silent addition incorrectly required a HUD ordinal.");
            Console.WriteLine("OPENNV_INVENTORY_COMMAND_CONTRACT_PASS source-dispatch protected-items transfer-variants lock cold-lock explicit-unequip source-flags cold-unequip reset-identity cold-reset typed-form lazy-branch atomic-failure actor-creature-container-counts worn-removal weapon-retirement cold-counts");
        }
        finally { directory.Delete(true); }
    }

    private static void CountCommands(FalloutPluginStack records, FalloutReferenceWorld world, FalloutPlayerInventory player, FalloutPluginRecord quest)
    {
        var retirementCalls = 0;
        var commands = new FalloutInventoryCommands(records, world, player, () => 1, prepareActorChange: _ =>
        {
            retirementCalls++;
            throw new NotSupportedException("Fixture has an active weapon presentation.");
        });
        var executor = new FalloutReferenceScripts(records, world, new(records), new((_, _) => false,
            _ => throw new InvalidDataException("Count command escaped its shared owner."), Inventory: commands));
        void Script(string source) => executor.ExecuteStage(quest, quest.ReadSubrecords().ToArray(), source);
        var playerBefore = JsonSerializer.Serialize(player.Capture());
        var actor = world.Inventory(Key(0x900), 1);
        var contents = actor.Contents;
        contents.Add(records, Key(2), 2, 1, true, extra: new(2, .6f, Key(0x100)));
        Script("Actor.AddItem Loot 2\nActor.RemoveItem Loot 4 1\nCreature.AddItem Loot 3 1\nCreature.RemoveItem Loot 1\nChest.AddItem Loot 2 1");
        Require(ReferenceEquals(contents, actor.Contents) && contents.Item(Key(2)) is { Count: 3, Variants: var variants } &&
            variants!.Any(value => value.Count == 2 && value.Condition == .6f && value.Owner == Key(0x100)) &&
            world.Inventory(Key(0x902), 1).Contents.Item(Key(2))!.Count == 4 &&
            playerBefore == JsonSerializer.Serialize(player.Capture()) && retirementCalls == 0,
            "Actor/creature count commands lost extra data, replaced their owner or changed player state.");
        actor.InitialArmorResolved = true;
        contents.Add(records, Key(5), 1, 1, true); contents.Equip(records, Key(5), true);
        var appearance = world.ActorAppearanceRevision(Key(0x900));
        Script("Actor.RemoveItem Suit 1");
        Require(contents.Item(Key(5)) is null && !contents.Equipped.Contains(5u) &&
            world.ActorAppearanceRevision(Key(0x900)) > appearance && retirementCalls == 0,
            "Removing worn armor did not retire its equipment identity and invalidate appearance independently of weapon pose.");
        appearance = world.ActorAppearanceRevision(Key(0x900));
        Script("Actor.RemoveItem Suit 1\nChest.RemoveItem Loot 2147483647 1");
        Require(world.ActorAppearanceRevision(Key(0x900)) == appearance && world.Inventory(Key(0x901), 1).Contents.Item(Key(2)) is null,
            "Absent removal created a revision or over-count removal did not clamp to retained contents.");
        contents.Add(records, Key(1), 2, 1, true); contents.Equip(records, Key(1), true);
        Script("Actor.RemoveItem Gun 1 1");
        Require(contents.Item(Key(1)) is { Count: 1, UnequipLocked: true } && contents.Equipped.Contains(1u) && retirementCalls == 0,
            "Partial weapon removal retired its remaining equipped stack.");
        var before = JsonSerializer.Serialize(world.Capture());
        Reject(() => Script("Actor.UnequipItem Gun 0 1"));
        Require(before == JsonSerializer.Serialize(world.Capture()) && retirementCalls == 1,
            "Rejected native UnequipItem retirement changed equipment before presentation admitted it.");
        Reject(() => Script("Actor.RemoveItem Gun 1 1"));
        Require(before == JsonSerializer.Serialize(world.Capture()) && retirementCalls == 2,
            "Rejected native weapon retirement published an inventory prefix.");
        Reject(() => Script("Actor.AddItem Loot 0 1")); Reject(() => Script("Creature.RemoveItem Loot 1.5"));
        Reject(() => Script("Actor.AddItem Loot 1 2")); Reject(() => Script("Activator.AddItem Loot 1"));
        Reject(() => commands.Execute(new(FalloutInventoryCommandKind.Remove, Key(0x900), Item: Key(2), Count: -1)));
        Require(before == JsonSerializer.Serialize(world.Capture()), "Rejected count or target arguments changed reference state.");
        using var cold = new FalloutReferenceWorld(records);
        cold.Restore(JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(before)!);
        foreach (var reference in new[] { Key(0x900), Key(0x901), Key(0x902) })
            Require(JsonSerializer.Serialize(cold.Inventory(reference, 1).Capture()) == JsonSerializer.Serialize(world.Inventory(reference, 1).Capture()),
                "Cold actor/creature/container counts, equipment or random state diverged.");
        var admitted = new FalloutInventoryCommands(records, world, player, () => 1, prepareActorChange: _ => retirementCalls++);
        var admittedExecutor = new FalloutReferenceScripts(records, world, new(records), new((_, _) => false,
            _ => throw new InvalidDataException("Unequip command escaped its owner."), Inventory: admitted));
        appearance = world.ActorAppearanceRevision(Key(0x900));
        admittedExecutor.ExecuteStage(quest, quest.ReadSubrecords().ToArray(), "Actor.UnequipItem Gun 0 1");
        Require(contents.Item(Key(1)) is { Count: 1, UnequipLocked: false } && contents.Equipped.Count == 0 &&
            actor.Unequipped.Contains(Key(1)) && world.ActorAppearanceRevision(Key(0x900)) > appearance && retirementCalls == 3,
            "Admitted actor UnequipItem lost its contents, appearance change or explicit unequipped history.");
        using var unequippedCold = new FalloutReferenceWorld(records);
        unequippedCold.Restore(JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))!);
        Require(JsonSerializer.Serialize(unequippedCold.Inventory(Key(0x900), 1).Capture()) == JsonSerializer.Serialize(actor.Capture()),
            "Cold actor restore changed source UnequipItem history or contents.");
    }

    private static FalloutFormKey Key(uint id) => new("Items.esm", id);
    private static void Require(bool valid, string error) { if (!valid) throw new InvalidDataException(error); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException or InvalidOperationException or OverflowException) { return; }
        throw new InvalidDataException("Invalid inventory operation was accepted.");
    }
    private static byte[] Item(string type, uint id, string name, int length) => Record(type, id, Field("EDID", Text(name)), Field("DATA", new byte[length]));
    private static byte[] LeveledEntry(uint id)
    {
        var entry = new byte[12]; BinaryPrimitives.WriteUInt16LittleEndian(entry, 1);
        BinaryPrimitives.WriteUInt32LittleEndian(entry.AsSpan(4), id); BinaryPrimitives.WriteUInt16LittleEndian(entry.AsSpan(8), 1);
        return entry;
    }
    private static byte[] Armor(uint id, string name, int slot, bool hidden = false)
    {
        var biped = new byte[8]; BinaryPrimitives.WriteUInt32LittleEndian(biped, 1u << slot); biped[4] = hidden ? (byte)0x40 : (byte)0;
        return Record("ARMO", id, Field("EDID", Text(name)), Field("DATA", new byte[12]), Field("BMDT", biped));
    }
    private static byte[] Reference(string type, uint id, uint form, string name) => Record(type, id,
        Field("NAME", BitConverter.GetBytes(form)), Field("DATA", new byte[24]), Field("EDID", Text(name)));
    private static byte[] Group(uint cell, byte[] data)
    {
        var bytes = new byte[24 + data.Length]; Encoding.ASCII.GetBytes("GRUP").CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)bytes.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), cell); BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), 6); data.CopyTo(bytes, 24); return bytes;
    }
    private static byte[] Record(string name, uint id, params byte[][] fields)
    {
        var data = Join(fields); var bytes = new byte[24 + data.Length]; Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)data.Length); BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), id); data.CopyTo(bytes, 24); return bytes;
    }
    private static byte[] Field(string name, byte[] data)
    {
        var bytes = new byte[6 + data.Length]; Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), (ushort)data.Length); data.CopyTo(bytes, 6); return bytes;
    }
    private static byte[] Text(string text) => Encoding.ASCII.GetBytes(text + '\0');
    private static byte[] Join(params byte[][] fields) => fields.SelectMany(field => field).ToArray();
}
