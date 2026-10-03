using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class InventoryCommandContracts
{
    internal static void Run()
    {
        var directory = Directory.CreateTempSubdirectory("opennv-inventory-commands-");
        try
        {
            var header = new byte[12]; BinaryPrimitives.WriteSingleLittleEndian(header, 1.34f);
            var questItem = Item("MISC", 4, "QuestItem", 8); BinaryPrimitives.WriteUInt32LittleEndian(questItem.AsSpan(8), 0x400);
            File.WriteAllBytes(Path.Combine(directory.FullName, "Items.esm"), Join(
                Record("TES4", 0, Field("HEDR", header)), Item("WEAP", 1, "Gun", 15), Item("MISC", 2, "Loot", 8),
                Armor(3, "Protected", 6, true), questItem, Armor(5, "Suit", 2), Armor(6, "OtherSuit", 2),
                Record("NPC_", 7, Field("CNTO", Join(BitConverter.GetBytes(2u), BitConverter.GetBytes(3)))),
                Item("WEAP", 8, "OtherGun", 15), Record("NPC_", 0x100, Field("ACBS", new byte[24]),
                    Field("CNTO", Join(BitConverter.GetBytes(2u), BitConverter.GetBytes(3)))),
                Record("CONT", 0x110), Record("CELL", 0x800, Field("DATA", [1])), Group(0x800,
                    Join(Reference("ACHR", 0x900, 0x100, "Actor"), Reference("REFR", 0x901, 0x110, "Chest"))),
                Record("QUST", 0x600, [Field("EDID", Text("InventoryQuest")), .. new uint[] { 0x14, 1, 2, 3, 4, 5, 6, 8, 0x900, 0x901 }
                    .Select(id => Field("SCRO", BitConverter.GetBytes(id)))])));
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
            var executor = new FalloutReferenceScripts(records, world, new(records), new((_, _) => false,
                effect =>
                {
                    if (effect.Kind != FalloutReferenceEffectKind.AddItem || effect.Target != Key(0x14))
                        throw new InvalidDataException("Unexpected fixture effect.");
                    inventory.Add(records, effect.Argument!.Value, effect.Value, 1, effect.Enable);
                }, Inventory: commands));
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
            var overflow = new FalloutPlayerInventory(456); overflow.Add(records, Key(2), int.MaxValue, 1, true);
            inventory.Add(records, Key(2), 1, 1, true);
            before = JsonSerializer.Serialize(inventory.Capture()); var targetBefore = JsonSerializer.Serialize(overflow.Capture());
            Reject(() => inventory.RemoveAll(records, true, overflow, false, false));
            Require(before == JsonSerializer.Serialize(inventory.Capture()) && targetBefore == JsonSerializer.Serialize(overflow.Capture()),
                "Failed whole-container transfer published a partial transaction.");
            Console.WriteLine("OPENNV_INVENTORY_COMMAND_CONTRACT_PASS source-dispatch protected-items transfer-variants lock cold-lock reset-identity cold-reset typed-form lazy-branch atomic-failure");
        }
        finally { directory.Delete(true); }
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
