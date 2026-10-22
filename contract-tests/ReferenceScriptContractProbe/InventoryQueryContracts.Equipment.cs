using System.Buffers.Binary;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static partial class InventoryQueryContracts
{
    private static void EquipmentQueries()
    {
        var directory = Directory.CreateTempSubdirectory("opennv-equipped-query-");
        try
        {
            File.WriteAllBytes(Path.Combine(directory.FullName, "Equipment.esm"), Join(Header(),
                Weapon(2, "EquipmentGun", 5), Weapon(4, "EquipmentOtherGun", 3), QueryArmor(10), QueryArmor(11),
                Record("NPC_", 7), Record("NPC_", 0x100, Field("ACBS", new byte[24])), Record("CELL", 0x800, Field("DATA", [1])),
                Group(0x800, Reference("ACHR", 0x900, 0x100, "EquipmentActor"))));
            File.WriteAllBytes(Path.Combine(directory.FullName, "Other.esm"), Header());
            byte[] Member(uint raw) => Field("LNAM", U32(raw));
            File.WriteAllBytes(Path.Combine(directory.FullName, "EquipmentLists.esp"), Join(Header("Other.esm", "Equipment.esm"),
                Record("FLST", 0x02000130, Field("EDID", Text("EquipmentList")), Member(0x02000131), Member(0x01000100), Member(0x01000002), Member(0x01000004)),
                Record("FLST", 0x02000131, Member(0x01000002)),
                Record("FLST", 0x02000132, Member(0x02000131), Member(0x02000132), Member(0x01000100)),
                Record("FLST", 0x02000133, Member(0x01000002), Field("LNAM", [1, 0])),
                Record("FLST", 0x02000134, Member(0x02000135)), Record("FLST", 0x02000135, Member(0x02000134)),
                Record("FLST", 0x02000136, Member(0), Member(0x01000002), Field("LNAM", [1, 0])),
                Record("FLST", 0x02000137, Member(0x0100fffe)), Record("FLST", 0x02000138, Field("LNAM", [1, 0])),
                Record("FLST", 0x02000139), Record("FLST", 0x0200013a, Member(0x0200013a)),
                Record("FLST", 0x0200013b, Member(0x0100000a), Member(0x0100000b)),
                Record("QUST", 0x02000600, Field("DATA", [1, 0]), Field("SCRO", U32(0x01000014)),
                    Field("SCRO", U32(0x01000900)), Field("SCRO", U32(0x02000130)))));
            using var records = FalloutPluginStack.Load(directory.FullName, ["Equipment.esm", "Other.esm", "EquipmentLists.esp"]);
            FalloutFormKey Key(uint id) => new("Equipment.esm", id);
            FalloutFormKey List(uint id) => new("EquipmentLists.esp", id);
            var actor = Key(0x900); var playerKey = Key(0x14);
            using var world = new FalloutReferenceWorld(records);
            world.LoadCell(FalloutCellSceneReader.Read(records, Key(0x800)));
            var player = new FalloutPlayerInventory(321);
            var inventory = world.Inventory(actor, 1).Contents;
            _ = world.EquippedArmor(actor, 1); // Settle the empty initial armor owner before explicit equipment changes.
            foreach (var item in new uint[] { 2, 4, 10, 11 })
            {
                player.Add(records, Key(item), 1, 1, true);
                inventory.Add(records, Key(item), 1, 1, true);
            }
            var commands = new FalloutInventoryCommands(records, world, player, () => 1);
            Reject(() => commands.IsEquipped(actor, Key(2)));
            Reject(() => commands.IsEquipped(actor, List(0x130)));
            Check(!commands.IsEquipped(actor, List(0x132)) && !commands.IsEquipped(playerKey, List(0x134)) &&
                !commands.IsEquipped(playerKey, List(0x135)) && !commands.IsEquipped(playerKey, List(0x139)) &&
                !commands.IsEquipped(playerKey, List(0x13a)), "Nested, cyclic or empty FLST recursively queried equipment.");
            player.Equip(records, Key(2)); world.EquipItem(actor, Key(2), false, 1);
            player.Equip(records, Key(10)); world.EquipItem(actor, Key(10), false, 1);
            var condition = new FalloutCondition(records.GetEffective(List(0x130)), 0, 0, 182, 0x02000130, 0, 0, 0);
            var visited = new List<FalloutFormKey>();
            Check(FalloutEquippedItems.Any(records, List(0x130), item => { visited.Add(item); return item == Key(2); }) &&
                visited.SequenceEqual([Key(2)]), "Equipment list lost source-master adjustment, direct filtering or first-match short circuit.");
            Check(commands.IsEquipped(playerKey, List(0x133)) && commands.IsEquipped(actor, List(0x133)),
                "A successful first member evaluated its malformed tail.");
            Check(!commands.IsEquipped(playerKey, List(0x136)) && !commands.IsEquipped(actor, List(0x136)),
                "A null list entry did not terminate before its equipped/malformed tail.");
            Reject(() => commands.IsEquipped(playerKey, List(0x137)));
            Reject(() => commands.IsEquipped(playerKey, List(0x138)));
            Reject(() => commands.IsEquipped(playerKey, new("EquipmentLists.esp", 0x7fffff)));
            var quests = new FalloutQuestState(records);
            var scripts = new FalloutReferenceScripts(records, world, quests, new((_, _) => false, _ => { }, Inventory: commands));
            var quest = records.GetEffective(List(0x600)); var fields = quest.ReadSubrecords().ToArray();
            foreach (var equipped in new uint[] { 2, 4 })
            {
                player.Equip(records, Key(equipped)); world.EquipItem(actor, Key(equipped), false, 1);
                var playerBefore = JsonSerializer.Serialize(player.Capture());
                var worldBefore = JsonSerializer.Serialize(world.Capture());
                Check(commands.IsEquipped(playerKey, List(0x130)) && commands.IsEquipped(actor, List(0x130)) &&
                    world.IsEquipped(actor, List(0x130), 1) &&
                    commands.IsEquipped(actor, Key(2)) == (equipped == 2) && commands.IsEquipped(actor, Key(4)) == (equipped == 4) &&
                    FalloutInventoryConditions.Evaluate(records, player, _ => false, condition) == 1,
                    "Equipment query ignored a live player/NPC weapon switch or disagreed with the player CTDA owner.");
                scripts.ExecuteStage(quest, fields,
                    "if player.GetEquipped EquipmentList != 1 || EquipmentActor.GetEquipped EquipmentList != 1\nUnexpectedEquipment\nendif");
                Check(playerBefore == JsonSerializer.Serialize(player.Capture()) && worldBefore == JsonSerializer.Serialize(world.Capture()),
                    "Resolved equipment queries mutated player or NPC state.");
                if (equipped == 4) Reject(() => commands.IsEquipped(playerKey, List(0x133)));
            }
            player.Equip(records, Key(11)); world.EquipItem(actor, Key(11), false, 1);
            Check(!commands.IsEquipped(playerKey, Key(10)) && !commands.IsEquipped(actor, Key(10)) &&
                commands.IsEquipped(playerKey, List(0x13b)) && commands.IsEquipped(actor, List(0x13b)),
                "Live armor switch retained stale appearance equipment.");
            player.Unequip(records, Key(4), force: true);
            world.UnequipItem(actor, Key(4), 1);
            Reject(() => commands.IsEquipped(actor, List(0x130))); // The other weapon's process selection is still unresolved.
            world.UnequipItem(actor, Key(2), 1);
            Check(!commands.IsEquipped(playerKey, List(0x130)) && !commands.IsEquipped(actor, List(0x130)) &&
                FalloutInventoryConditions.Evaluate(records, player, _ => false, condition) == 0,
                "Explicitly unequipped weapons remained worn.");
            player.Unequip(records, Key(11), force: true); world.UnequipItem(actor, Key(11), 1);
            Check(!commands.IsEquipped(playerKey, List(0x13b)) && !commands.IsEquipped(actor, List(0x13b)),
                "Explicitly unequipped armor remained worn.");
            player.Equip(records, Key(2)); world.EquipItem(actor, Key(2), false, 1);
            player.Equip(records, Key(10)); world.EquipItem(actor, Key(10), false, 1);
            var savedPlayer = JsonSerializer.Deserialize<FalloutOpeningInventoryGrant>(JsonSerializer.Serialize(player.Capture()))!;
            var coldPlayer = new FalloutPlayerInventory();
            coldPlayer.Restore(savedPlayer.Inventory, savedPlayer.EquippedRuntimeFormIds.ToArray(), savedPlayer.InventoryRandomState);
            using var coldWorld = new FalloutReferenceWorld(records);
            coldWorld.Restore(JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))!);
            var cold = new FalloutInventoryCommands(records, coldWorld, coldPlayer, () => 1);
            var coldBefore = JsonSerializer.Serialize(coldWorld.Capture());
            Check(cold.IsEquipped(playerKey, List(0x130)) && cold.IsEquipped(actor, List(0x130)) &&
                cold.IsEquipped(playerKey, List(0x13b)) && cold.IsEquipped(actor, List(0x13b)) &&
                coldBefore == JsonSerializer.Serialize(coldWorld.Capture()), "Cold equipment membership changed or mutated retained NPC state.");
            Console.WriteLine("OPENNV_EQUIPPED_LIST_QUERY_CONTRACT_PASS direct=true oneOuterList=true sourceMasters=true nullTerminates=true " +
                "nestedAndCyclesNotRecursive=true firstMatch=true malformedRefused=true liveNpcAndPlayer=true armorAndWeapon=true " +
                "unownedWeaponRefused=true scriptAndCondition=true readonlyAfterResolution=true cold=true");
        }
        finally { directory.Delete(true); }
    }

    private static byte[] QueryArmor(uint id)
    {
        var biped = new byte[8]; BinaryPrimitives.WriteUInt32LittleEndian(biped, 1);
        return Record("ARMO", id, Field("EDID", Text("EquipmentArmor" + id)), Field("DATA", new byte[12]), Field("BMDT", biped));
    }
}
