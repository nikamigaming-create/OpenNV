using System.Buffers.Binary;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static partial class InventoryQueryContracts
{
    private static void EquipmentQueries()
    {
        RepairAdmissionQueries();
        var directory = Directory.CreateTempSubdirectory("opennv-equipped-query-");
        try
        {
            File.WriteAllBytes(Path.Combine(directory.FullName, "Equipment.esm"), Join(Header(),
                Weapon(0x1f4, "SourceUnarmedFallback", 0), Weapon(2, "EquipmentGun", 5), Weapon(4, "EquipmentOtherGun", 3), QueryArmor(10), QueryArmor(11),
                Record("NPC_", 7), Record("NPC_", 0x100, Field("ACBS", new byte[24])), Record("CELL", 0x800, Field("DATA", [1])),
                Record("ACTI", 0x50, Field("EDID", Text("EquipmentObject"))),
                Group(0x800, Join(Reference("ACHR", 0x900, 0x100, "EquipmentActor"),
                    Reference("REFR", 0x901, 0x50, "EquipmentObjectRef")))));
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
                Record("FLST", 0x0200013c, Member(0), Member(0x01000002)),
                Record("FLST", 0x0200013d, Member(0x010001f4)),
                Record("FLST", 0x0200013e, Member(0x02000131)),
                Record("FLST", 0x0200013f, Member(0x01000002), Field("LNAM", [1, 0])),
                Record("FLST", 0x02000140, Member(0x01000004)),
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
            var lockedStack = new FalloutPlayerInventory(432);
            lockedStack.Add(records, Key(2), 3, 1, true);
            lockedStack.Equip(records, Key(2), noUnequip: true);
            var lockedInstances = lockedStack.Instances(Key(2));
            Check(lockedStack.Item(Key(2)) is { Count: 3, UnequipLocked: true } &&
                lockedInstances.Count == 2 && lockedInstances[0].Extra.Count == 1 &&
                lockedInstances[0].Extra.Instance is not null && lockedInstances[1].Extra.Count == 2 &&
                lockedInstances[1].Extra.Instance is null,
                "Explicit no-unequip restored the pre-selection weapon stack and lost its instance.");
            var lockedSave = lockedStack.Capture();
            var coldLocked = new FalloutPlayerInventory();
            coldLocked.Restore(lockedSave.Inventory, lockedSave.EquippedRuntimeFormIds.ToArray(), lockedSave.InventoryRandomState);
            Check(JsonSerializer.Serialize(coldLocked.Capture()) == JsonSerializer.Serialize(lockedSave),
                "Explicit equipment lock or selected weapon identity did not survive cold restore.");
            var commands = new FalloutInventoryCommands(records, world, player, () => 1);
            Reject(() => commands.IsEquipped(actor, Key(2)));
            Reject(() => commands.IsWeaponInList(actor, List(0x13d)));
            var actorProcess = world.Get(actor);
            var processPlacement = world.Placement(actor);
            Func<FalloutReferencePlacement> spatialProcess = () => processPlacement;
            Func<FalloutConditionQuery, float> nativeProcess = _ => throw new InvalidOperationException("Weapon-list query invoked an unrelated native condition.");
            actorProcess.QuerySpatialPlacement = spatialProcess;
            actorProcess.QueryNativeCondition = nativeProcess;
            Reject(() => commands.IsWeaponInList(actor, List(0x13d)));
            Check(commands.IsWeaponInList(playerKey, List(0x13d)) && !commands.IsWeaponInList(playerKey, List(0x130)),
                "Unarmed process query used possession or omitted its source engine fallback.");
            Reject(() => commands.IsEquipped(actor, List(0x130)));
            var owned = world.Inventory(actor, 1);
            owned.ResolveWeaponSelection(records, null);
            var unarmedBefore = JsonSerializer.Serialize(world.Capture());
            Check(owned.WeaponSelectionResolved && !commands.IsEquipped(actor, Key(2)) &&
                !commands.IsEquipped(actor, List(0x130)) && unarmedBefore == JsonSerializer.Serialize(world.Capture()),
                "An explicitly resolved unarmed process was confused with unknown weapon selection or mutated by a query.");
            using (var unarmedWorld = new FalloutReferenceWorld(records))
            {
                unarmedWorld.Restore(JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(unarmedBefore)!);
                Check(unarmedWorld.Inventory(actor, 1).WeaponSelectionResolved &&
                    unarmedWorld.EquippedObject(actor, 5, 1) is null,
                    "Cold restore lost explicit unarmed selection while retaining carried weapons.");
            }
            inventory.Add(records, Key(2), 1, 1, true);
            Check(owned.WeaponSelectionResolved && !commands.IsEquipped(actor, Key(2)),
                "A contents change erased the actual unarmed process decision before its selection owner ran.");
            Check(owned.WeaponSelectionRefreshPending, "Changed contents did not retain a pending process refresh.");
            using (var pendingWorld = new FalloutReferenceWorld(records))
            {
                pendingWorld.Restore(JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))!);
                var pending = pendingWorld.Inventory(actor, 1);
                Check(pending.WeaponSelectionResolved && pending.WeaponSelectionRefreshPending &&
                    pendingWorld.EquippedObject(actor, 5, 1) is null,
                    "Cold restore lost a pending selection refresh or replaced the known decision before its process ran.");
                pending.ResolveWeaponSelection(records, Key(2));
                Check(pending.WeaponSelectionResolved && !pending.WeaponSelectionRefreshPending &&
                    pendingWorld.EquippedObject(actor, 5, 1) == Key(2),
                    "The cold selection owner did not flush its pending refresh and publish the actual choice.");
            }
            owned.ResolveWeaponSelection(records, Key(2));
            Check(commands.IsEquipped(actor, Key(2)) && !owned.WeaponSelectionRefreshPending,
                "Actual weapon selection was not published or its completed refresh remained pending.");
            inventory.Remove(Key(2), 2, true);
            Check(!owned.WeaponSelectionResolved, "Removing the selected weapon retained a stale equipped decision.");
            Reject(() => commands.IsEquipped(actor, Key(4)));
            inventory.Add(records, Key(2), 2, 1, true);
            Check(!commands.IsEquipped(actor, List(0x132)) && !commands.IsEquipped(playerKey, List(0x134)) &&
                !commands.IsEquipped(playerKey, List(0x135)) && !commands.IsEquipped(playerKey, List(0x139)) &&
                !commands.IsEquipped(playerKey, List(0x13a)), "Nested, cyclic or empty FLST recursively queried equipment.");
            player.Equip(records, Key(2)); world.EquipItem(actor, Key(2), false, 1);
            player.Equip(records, Key(10)); world.EquipItem(actor, Key(10), false, 1);
            Check(commands.IsWeaponInList(playerKey, List(0x13c)) && commands.IsWeaponInList(actor, List(0x13c)) &&
                commands.IsWeaponInList(playerKey, List(0x13f)) && !commands.IsWeaponInList(playerKey, List(0x13e)) &&
                !commands.IsWeaponInList(playerKey, List(0x140)) && !commands.IsWeaponInList(Key(0x901), List(0x13c)),
                "Weapon-list query used possession, recursed, stopped at null or decoded an unvisited malformed tail.");
            actorProcess.QuerySpatialPlacement = null;
            Reject(() => commands.IsWeaponInList(actor, List(0x13c)));
            actorProcess.QuerySpatialPlacement = spatialProcess;
            actorProcess.QueryNativeCondition = null;
            Reject(() => commands.IsWeaponInList(actor, List(0x13c)));
            actorProcess.QueryNativeCondition = nativeProcess;
            world.FormLists.Add(List(0x140), Key(2));
            Check(commands.IsWeaponInList(playerKey, List(0x140)), "Weapon-list query ignored mutable list state.");
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
            var weaponRuntime = new FalloutConditionRuntime(records, quests, world) { WeaponInList = commands.IsWeaponInList };
            var weaponCondition = new FalloutCondition(records.GetEffective(List(0x130)), 0, 0, 399, 0x0200013c, 0, 0, 0);
            Check(weaponRuntime.Evaluate(weaponCondition, new(actor, playerKey)) == 1 &&
                weaponRuntime.Evaluate(weaponCondition with { Argument1 = 0 }, new(actor, playerKey)) == 0,
                "Shared weapon-list condition lost declaring-master/null-list semantics.");
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
                scripts.ExecuteStage(quest, fields,
                    "if player.IsWeaponInList EquipmentList != 1 || EquipmentActor.IsWeaponInList EquipmentList != 1\nUnexpectedEquipment\nendif");
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
            world.Inventory(actor, 1).ResolveWeaponSelection(records, null);
            Check(commands.IsWeaponInList(actor, List(0x13d)) && cold.IsWeaponInList(playerKey, List(0x130)),
                "Published NPC unarmed selection or cold player weapon identity lost source membership.");
            Console.WriteLine("OPENNV_EQUIPPED_LIST_QUERY_CONTRACT_PASS direct=true oneOuterList=true sourceMasters=true nullTerminates=true " +
                "nestedAndCyclesNotRecursive=true firstMatch=true malformedRefused=true liveNpcAndPlayer=true armorAndWeapon=true " +
                "unownedWeaponRefused=true resolvedUnarmed=true contentsChangeRetainsDecision=true selectedRemovalInvalidates=true scriptAndCondition=true readonlyAfterResolution=true cold=true");
        }
        finally { directory.Delete(true); }
    }

    private static byte[] QueryArmor(uint id)
    {
        var biped = new byte[8]; BinaryPrimitives.WriteUInt32LittleEndian(biped, 1);
        return Record("ARMO", id, Field("EDID", Text("EquipmentArmor" + id)), Field("DATA", new byte[12]), Field("BMDT", biped));
    }
}
