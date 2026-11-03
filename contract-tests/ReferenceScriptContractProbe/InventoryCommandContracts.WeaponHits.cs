using System.Buffers.Binary;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static partial class InventoryCommandContracts
{
    private static void RunWeaponHitScripts()
    {
        var directory = Directory.CreateTempSubdirectory("opennv-inventory-hit-");
        try
        {
            const string program = "short Visits\nbegin OnHit\nref Target\nset Visits to Visits + 1\nset Target to GetOwnerLastTarget\nif Target.GetIsCreatureType 6\nTarget.DamageAV Health 10\nelse\nTarget.DamageAV Fatigue 35\nendif\nend\nbegin OnFire\nset Visits to Visits + 10\nplayer.DetonatePlacedExplosives\nend";
            var header = new byte[20]; BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), 0);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12), 2);
            var number = new byte[24]; BinaryPrimitives.WriteUInt32LittleEndian(number, 1); number[16] = 1;
            var targetLocal = new byte[24]; BinaryPrimitives.WriteUInt32LittleEndian(targetLocal, 2);
            var weapon = new byte[196]; BinaryPrimitives.WriteUInt32LittleEndian(weapon, 3);
            BinaryPrimitives.WriteSingleLittleEndian(weapon.AsSpan(4), 1);
            BinaryPrimitives.WriteSingleLittleEndian(weapon.AsSpan(60), 1);
            var acbs = new byte[24]; acbs[8] = 1;
            var creature = new byte[17]; creature[0] = 6;
            var inherited = (byte[])acbs.Clone(); inherited[22] = 1;
            File.WriteAllBytes(Path.Combine(directory.FullName, "Items.esm"), Join(
                Record("TES4", 0, Field("HEDR", new byte[12])), Record("NPC_", 7, Field("ACBS", acbs)),
                Record("NPC_", 0x100, Field("ACBS", acbs), Field("CNTO", BitConverter.GetBytes(1u).Concat(BitConverter.GetBytes(1)).ToArray())),
                Record("CREA", 0x101, Field("ACBS", acbs), Field("DATA", new byte[17])),
                Record("CREA", 0x102, Field("ACBS", acbs), Field("DATA", creature)),
                Record("CREA", 0x103, Field("ACBS", inherited), Field("DATA", new byte[17]), Field("TPLT", BitConverter.GetBytes(0x102u))),
                Record("WEAP", 1, Field("EDID", Text("ContactWeapon")), Field("DATA", new byte[15]), Field("DNAM", weapon),
                    Field("ETYP", BitConverter.GetBytes(2)), Field("SCRI", BitConverter.GetBytes(0x200u))),
                Record("WEAP", 2, Field("EDID", Text("OtherContactWeapon")), Field("DATA", new byte[15]), Field("DNAM", weapon),
                    Field("ETYP", BitConverter.GetBytes(2)), Field("SCRI", BitConverter.GetBytes(0x201u))),
                Record("SCPT", 0x200, Field("SCHR", header), Field("SCDA", []), Field("SCRO", BitConverter.GetBytes(0x14u)), Field("SCTX", Text(program)),
                    Field("SLSD", number), Field("SCVR", Text("Visits")), Field("SLSD", targetLocal), Field("SCVR", Text("Target"))),
                Record("SCPT", 0x201, Field("SCHR", header), Field("SCDA", []),
                    Field("SCTX", Text("short Visits\nref Target\nbegin GameMode\nset Visits to Visits + 1000\nend\nbegin OnEquip\nRemoveMe\nend")),
                    Field("SLSD", number), Field("SCVR", Text("Visits")), Field("SLSD", targetLocal), Field("SCVR", Text("Target"))),
                Record("CELL", 0x800, Field("DATA", [1])), Group(0x800, Join(
                    Reference("ACHR", 0x900, 0x100, "Attacker"), Reference("ACRE", 0x901, 0x101, "TargetA"),
                    Reference("ACRE", 0x902, 0x103, "TargetB")))));
            using var records = FalloutPluginStack.Load(directory.FullName, ["Items.esm"]);
            using var world = new FalloutReferenceWorld(records);
            var cell = FalloutCellSceneReader.Read(records, Key(0x800)); world.LoadCell(cell);
            var player = new FalloutPlayerInventory(); world.BindPlayerInventory(player);
            var globals = FalloutGlobalState.Read(records);
            player.Add(records, Key(1), 1, 1, true, globals); player.PrepareInventoryScripts(records); player.Equip(records, Key(1));
            var npc = world.Inventory(Key(0x900), 1, globals).Contents;
            npc.Equip(records, Key(1));
            var writes = new List<(FalloutFormKey Target, string Value, string Operation, double Amount)>();
            var scripts = new FalloutReferenceScripts(records, world, new(records), new((_, _) => false,
                _ => throw new InvalidDataException("Unexpected inventory hit effect."), Globals: globals, PlayerLevel: () => 1,
                ChangeActorValue: (actor, value, operation, amount) => writes.Add((actor, value, operation, amount))));
            FalloutInventoryScriptState State(FalloutPlayerInventory inventory) => inventory.Item(Key(1))!.Variants![0].ScriptState!;
            double Visits(FalloutPlayerInventory inventory) => State(inventory).Resolve(records.GetEffective(Key(0x200))).Read("Visits").Number;
            player.AdmitInventoryWeaponFire(records, Key(1), Key(0x14));
            var fireResult = scripts.AdvanceInventory(Key(0x14), 0, false);
            Require(fireResult.All(value => value.Error is null) && Visits(player) == 10 && State(player).Events is null,
                "Source OnFire failed to execute its actual retained instance and remote command: " + JsonSerializer.Serialize(fireResult));
            scripts.AdvanceInventory(Key(0x14), 0, false);
            Require(Visits(player) == 10, "OnFire replayed without a new actual action.");
            player.StoreInventoryScriptState(Key(1), State(player) with
            { Locals = new FalloutScriptEffectLocals(records.GetEffective(Key(0x200))).Capture() });
            Reject(() => world.OwnerLastTarget(Key(0x14)));
            world.RecordActorWeaponContact(Key(0x901), Key(0x14), Key(1), State(player).Identity);
            world.RecordActorWeaponContact(Key(0x901), Key(0x14), Key(1), State(player).Identity);
            world.RecordActorWeaponContact(Key(0x902), Key(0x14), Key(1), State(player).Identity);
            Require(Visits(player) == 0 && writes.Count == 0 && State(player).Events!.Count(value => (value.Mask & 128) != 0) == 2,
                "Actual contacts ran OnHit eagerly, lost coalescence or borrowed victim events as last target.");
            Require(world.OwnerLastTarget(Key(0x14)) == Key(0x902) && world.IsCreatureType(Key(0x902), 6),
                "Outgoing hit receipt or source UseTraits classification lost its actual owner.");
            Reject(() => world.Capture());
            var result = scripts.AdvanceInventory(Key(0x14), 0, false);
            Require(result.All(value => value.Error is null) && Visits(player) == 1 && writes is [{ Target: var hit, Amount: 10 }] &&
                hit == Key(0x902) && writes[0].Value.Equals("health", StringComparison.OrdinalIgnoreCase),
                "Coalesced authored OnHit did not query latest actual outgoing target and deliver its real actor-value command: " + JsonSerializer.Serialize(result));
            world.RecordActorWeaponContact(Key(0x901), Key(0x900), Key(1), State(npc).Identity);
            result = scripts.AdvanceInventory(Key(0x900), 0, false);
            Require(result.All(value => value.Error is null) && Visits(npc) == 1 && writes.Count == 2 &&
                writes[1].Target == Key(0x901) && writes[1].Amount == 35 && writes[1].Value.Equals("fatigue", StringComparison.OrdinalIgnoreCase),
                "NPC inventory contact borrowed player instance, target or value delivery.");
            var saved = JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))!;
            using var cold = new FalloutReferenceWorld(records); cold.Restore(saved);
            Reject(() => cold.OwnerLastTarget(Key(0x900)));
            world.RetireOutgoingWeaponTarget(Key(0x14)); Reject(() => world.OwnerLastTarget(Key(0x14)));
            world.RecordActorWeaponContact(Key(0x901), Key(0x14), Key(1), State(player).Identity);
            Require(world.OwnerLastTarget(Key(0x14)) == Key(0x901), "New real contact did not reacquire known process scope.");
            scripts.AdvanceInventory(Key(0x14), 0, false);
            player.Add(records, Key(2), 1, 1, true, globals); player.PrepareInventoryScripts(records);
            var retained = player.Capture();
            var legacy = new FalloutPlayerInventory();
            legacy.Restore(retained.Inventory with { ScriptOrder = null }, retained.EquippedRuntimeFormIds.ToArray(), retained.InventoryRandomState);
            world.BindPlayerInventory(legacy);
            world.RecordActorWeaponContact(Key(0x902), Key(0x14), Key(1), State(player).Identity);
            var beforeLegacy = Visits(legacy);
            result = scripts.AdvanceInventory(Key(0x14), .25);
            Require(result.Sum(value => value.Blocks) == 1 && result.Any(value => value.Error is not null) &&
                Visits(legacy) == beforeLegacy + 1 && State(legacy).Events is null && State(legacy).Error is null &&
                legacy.Capture().Inventory.ScriptOrder is null && legacy.Item(Key(2))!.Variants![0].ScriptState!.Resolve(records.GetEffective(Key(0x201))).Read("Visits").Number == 0,
                "Legacy traversal blocked one actual OnHit owner, invented history, ran unordered GameMode or hid its qualification: " + JsonSerializer.Serialize(result));
            world.RecordActorWeaponContact(Key(0x901), Key(0x14), Key(1), State(player).Identity);
            var otherState = legacy.Item(Key(2))!.Variants![0].ScriptState!;
            legacy.StoreInventoryScriptState(Key(2), otherState.Admit(Key(0x14), 2));
            var beforeAmbiguous = JsonSerializer.Serialize(legacy.Capture());
            result = scripts.AdvanceInventory(Key(0x14), .25);
            Require(result.Sum(value => value.Blocks) == 0 && result.All(value => value.Error is not null) &&
                beforeAmbiguous == JsonSerializer.Serialize(legacy.Capture()),
                "Unknown traversal invented an order between multiple pending actual event lists.");
            legacy.StoreInventoryScriptState(Key(1), State(legacy) with { Events = null });
            result = scripts.AdvanceInventory(Key(0x14), .25);
            Require(result.Any(value => value.Error?.Contains("full ordered frame", StringComparison.Ordinal) == true) &&
                legacy.Item(Key(2)) is not null && legacy.Capture().Inventory.ScriptOrder is null,
                "Isolated legacy event skipped its own GameMode block or mutated inventory without full frame ownership.");
            var switchingSnapshot = player.Capture();
            var switching = new FalloutPlayerInventory();
            switching.Restore(switchingSnapshot.Inventory, switchingSnapshot.EquippedRuntimeFormIds.ToArray(),
                switchingSnapshot.InventoryRandomState);
            switching.BindScriptContainer(Key(0x14));
            var launchedItem = switching.Item(Key(1))!;
            var launchedVariant = launchedItem.Variants![0];
            var launchedIdentity = launchedVariant.ScriptState!.Identity;
            var replacementState = launchedVariant.ScriptState! with { Identity = Guid.NewGuid(), Events = null };
            var replacement = launchedVariant with { Instance = Guid.NewGuid(), ScriptState = replacementState };
            switching.Publish([launchedItem with { Count = 2, Variants = [replacement, launchedVariant with
            { ScriptState = launchedVariant.ScriptState! with { Events = null } }] }]);
            switching.AdmitInventoryWeaponHit(Key(1), launchedIdentity, Key(0x901));
            Require(switching.Item(Key(1))!.Variants![0].ScriptState!.Events is null &&
                switching.Item(Key(1))!.Variants![1].ScriptState!.Events!.Single().Mask == 128,
                "Delayed contact selected the current same-form instance instead of its captured launch event-list.");
            switching.StoreInventoryScriptState(Key(1), switching.Item(Key(1))!.Variants![1].ScriptState! with { Events = null });
            switching.Equip(records, Key(2));
            switching.AdmitInventoryWeaponHit(Key(1), launchedIdentity, Key(0x902));
            Require(switching.Item(Key(1))!.Variants![1].ScriptState!.Events!.Single().Actor == Key(0x902) &&
                switching.Item(Key(2))!.Variants![0].ScriptState!.Events?.Any(value => (value.Mask & 128) != 0) != true,
                "Delayed contact dispatched the newly equipped different weapon's script.");
            var retiredRejected = false;
            try { new FalloutPlayerInventory().AdmitInventoryWeaponHit(Key(1), launchedIdentity, Key(0x901)); }
            catch (NotSupportedException) { retiredRejected = true; }
            Require(retiredRejected, "Retired launch item silently borrowed a different current weapon instance.");
            using (legacy.EnterScriptIteration(traversalOwned: false))
            {
                Reject(() => legacy.Unequip(records, Key(1)));
                Require(legacy.EquippedObject(records, 5) == Key(1), "Partial-order invocation mutated actual equipment before refusal.");
            }
        }
        finally { directory.Delete(true); }
    }
    internal static void RunOwnedWeaponHitScripts(string root)
    {
        RuntimeLiveContentSource.Configure(root, RuntimeLiveContentSource.FalloutNewVegasGame);
        using var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        foreach (var (form, expected) in new[] { (0x0e5391u, 35d), (0x0e58bbu, 50d), (0x12d852u, 20d), (0x13316du, 50d) })
        {
            using var world = new FalloutReferenceWorld(records);
            var player = new FalloutPlayerInventory(); world.BindPlayerInventory(player);
            var weapon = records.RuntimeFormKey(form);
            var actor = records.RuntimeFormKey(0x14);
            var target = records.RuntimeFormKey(0x157b41);
            var globals = FalloutGlobalState.Read(records);
            player.Add(records, weapon, 1, 1, true, globals); player.PrepareInventoryScripts(records); player.Equip(records, weapon);
            var retained = player.Capture();
            var legacy = new FalloutPlayerInventory();
            legacy.Restore(retained.Inventory with { ScriptOrder = null }, retained.EquippedRuntimeFormIds.ToArray(), retained.InventoryRandomState);
            world.BindPlayerInventory(legacy);
            var writes = new List<(FalloutFormKey Target, string Value, string Operation, double Amount)>();
            var scripts = new FalloutReferenceScripts(records, world, new(records), new((_, _) => false,
                _ => throw new InvalidDataException("Unexpected owned weapon script effect."), Globals: globals, PlayerLevel: () => 1,
                ChangeActorValue: (reference, value, operation, amount) => writes.Add((reference, value, operation, amount))));
            world.RecordActorWeaponContact(target, actor, weapon, legacy.Item(weapon)!.Variants![0].ScriptState!.Identity);
            var results = scripts.AdvanceInventory(actor, .01);
            var state = legacy.Item(weapon)!.Variants![0].ScriptState!;
            Require(results.Sum(value => value.Blocks) == 1 && state.Error is null && state.Events is null &&
                writes.Count == 1 && writes[0].Target == target && writes[0].Amount == expected &&
                writes[0].Value.Equals("fatigue", StringComparison.OrdinalIgnoreCase) &&
                writes[0].Operation.Equals("damageav", StringComparison.OrdinalIgnoreCase),
                "Actual authored inventory OnHit failed its block-local reference owner or Fatigue delivery: " +
                weapon + " " + JsonSerializer.Serialize(results));
        }
        Console.WriteLine("OPENNV_OWNED_INVENTORY_ONHIT_PASS authoredGlovesGoldenTapeProd=true blockLocalRef=true legacyEventDrain=true fatigueDelivery=true ordinaryGameplay=unverified");
    }

}
