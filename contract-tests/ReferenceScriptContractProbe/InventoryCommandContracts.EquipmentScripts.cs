using System.Buffers.Binary;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static partial class InventoryCommandContracts
{
    private static void RunEquipmentScriptEvents()
    {
        var directory = Directory.CreateTempSubdirectory("opennv-equipment-script-");
        try
        {
            byte[] Script(uint id, string body)
            {
                var header = new byte[20]; BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), 5);
                BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12), 1);
                var local = new byte[24]; BinaryPrimitives.WriteUInt32LittleEndian(local, 1); local[16] = 1;
                return Record("SCPT", id, Field("SCHR", header), Field("SCDA", []), Field("SCTX", Text("short Visits\n" + body)),
                    Field("SLSD", local), Field("SCVR", Text("Visits")), Field("SCRO", BitConverter.GetBytes(0x14u)),
                    Field("SCRO", BitConverter.GetBytes(0x31u)), Field("SCRO", BitConverter.GetBytes(12u)),
                    Field("SCRO", BitConverter.GetBytes(2u)), Field("SCRO", BitConverter.GetBytes(13u)));
            }
            byte[] Scripted(string type, uint id, uint script)
            {
                var slots = new byte[8]; slots[0] = 1;
                return Record(type, id, [Field("EDID", Text("ScriptItem" + id)), Field("DATA", new byte[type == "ARMO" ? 12 : 8]),
                    Field("SCRI", BitConverter.GetBytes(script)), .. type == "ARMO" ? new[] { Field("BMDT", slots) } : []]);
            }
            byte[] Count(uint form, int count) => BitConverter.GetBytes(form).Concat(BitConverter.GetBytes(count)).ToArray();
            var header = new byte[12]; BinaryPrimitives.WriteSingleLittleEndian(header, 1.34f);
            var armor = "begin OnUnequip player\nset Visits to Visits + 100\nend\nbegin OnEquip player\nset Visits to Visits + 10\nend\n" +
                "begin GameMode\nif GetSelf != 0\nUnknownSyntheticSelf\nendif\nif GetContainer == 0\nset Visits to Visits + 1000\nelse\nset Visits to Visits + 1\nendif\nset OrderTrace to OrderTrace * 10 + 1\nend";
            var carried = "begin GameMode\nset Visits to Visits + 1\nset OrderTrace to OrderTrace * 10 + 2\nend";
            File.WriteAllBytes(Path.Combine(directory.FullName, "Items.esm"), Join(
                Record("TES4", 0, Field("HEDR", header)), Record("NPC_", 7, Field("ACBS", new byte[24])),
                Scripted("ARMO", 6, 0x210), Scripted("MISC", 0x17, 0x211), Scripted("MISC", 8, 0x211), Scripted("MISC", 9, 0x212), Item("MISC", 2, "NonscriptHead", 8),
                Scripted("MISC", 10, 0x213), Scripted("MISC", 11, 0x214), Scripted("MISC", 12, 0x215), Scripted("ARMO", 13, 0x216),
                Script(0x210, armor), Script(0x211, carried),
                Script(0x212, "begin GameMode\nset Visits to Visits + 1\nUnknownInventoryReachedEffect\nend"),
                Script(0x213, "begin GameMode\nif Visits == 0\nset Visits to 1\nplayer.AddItem ScriptItem12 1 1\nendif\nset OrderTrace to OrderTrace * 10 + 1\nend"),
                Script(0x214, "begin GameMode\nset OrderTrace to OrderTrace * 10 + 2\nend"),
                Script(0x215, "begin OnAdd player\nset Visits to Visits + 10\nset OrderTrace to OrderTrace * 10 + 4\nend\nbegin GameMode\nset Visits to Visits + 1\nset OrderTrace to OrderTrace * 10 + 3\nend"),
                Script(0x216, "begin OnEquip player\nplayer.UnequipItem ScriptItem13\nend\nbegin OnUnequip player\nset Visits to Visits + 100\nend"),
                Record("GMST", 0x33, Field("EDID", Text("sEquipItemOnPlayer")), Field("DATA", Text("was equipped"))),
                Record("GMST", 0x32, Field("EDID", Text("sUnequipItemOnPlayer")), Field("DATA", Text("was unequipped"))),
                Record("GLOB", 0x31, Field("EDID", Text("OrderTrace")), Field("FNAM", [(byte)'f']), Field("FLTV", BitConverter.GetBytes(0f))),
                Record("CONT", 0x100, Field("CNTO", Count(6, 2)), Field("CNTO", Count(6, 1))),
                Record("CONT", 0x101, Field("CNTO", Count(6, 1))),
                Record("CELL", 0x800, Field("DATA", [1])),
                Group(0x800, Join(Reference("REFR", 0x900, 0x100, "DuplicateScriptContainer"),
                    Reference("REFR", 0x901, 0x101, "BaseScriptContainer")))));
            using var records = FalloutPluginStack.Load(directory.FullName, ["Items.esm"]);
            using var world = new FalloutReferenceWorld(records);
            world.LoadCell(FalloutCellSceneReader.Read(records, Key(0x800)));
            var player = new FalloutPlayerInventory(7); world.BindPlayerInventory(player);
            var quests = new FalloutQuestState(records); var globals = FalloutGlobalState.Read(records);
            var questScripts = new FalloutQuestScripts(records, quests, new HashSet<FalloutFormKey>(), player,
                defaultProcessingDelay: 0, references: world);
            var stages = new FalloutQuestStages(records, quests, (_, _, _) => [], _ => 0);
            var acquisition = new FalloutInventoryAcquisition(records, world, player, quests, questScripts, () => stages, () => 1, globals);
            FalloutReferenceScripts Executor(FalloutPlayerInventory actual)
            {
                world.InventoryAcquisition = new(records, world, actual, quests, questScripts, () => stages, () => 1, globals);
                return new(records, world, quests,
                    new((_, _) => false, _ => throw new NotSupportedException("Synthetic event selected an external effect."),
                        Globals: globals, PlayerLevel: () => 1, Inventory: new(records, world, actual, () => 1, globals)));
            }
            var executor = Executor(player);
            var untypedProgram = FalloutGameModeProgram.Read("begin GameMode\nreturn\nend");
            Reject(() => world.ScriptManualSaves.Execute(Key(6), records.GetEffective(Key(0x210)), untypedProgram, []).ToArray());
            Reject(() => world.ScriptManualSaves.Execute(Key(6), records.GetEffective(Key(0x210)), untypedProgram, [],
                new(Key(6), Guid.Empty, Key(0x14), () => true)).ToArray());
            Reject(() => world.ScriptManualSaves.Execute(Key(6), records.GetEffective(Key(0x210)), untypedProgram, [],
                new(Key(6), Guid.NewGuid(), Key(0x14), () => false)).ToArray());
            FalloutInventoryScriptState State(FalloutPlayerInventory inventory, uint form) => inventory.Item(Key(form))!.Variants!
                .First(value => value.ScriptState is not null).ScriptState!;
            double Visits(FalloutPlayerInventory inventory, uint form) => State(inventory, form)
                .Resolve(records.GetEffective(State(inventory, form).Script)).Read("Visits").Number;
            acquisition.Add(Key(6), 1, true);
            player.Equip(records, Key(6)); player.Unequip(records, Key(6)); player.Equip(records, Key(6));
            Require(Visits(player, 6) == 0 && State(player, 6).Events!.Single().Mask == 10,
                "Equipment admission ran its callback eagerly or lost/coalesced the wrong actor event mask.");
            var paused = executor.AdvanceInventory(Key(0x14), 0, false);
            Require(paused.Sum(value => value.Blocks) == 2 && paused.All(value => value.Error is null) &&
                Visits(player, 6) == 110 && globals.Get(Key(0x31)) == 0 && State(player, 6).Events is null,
                "Paused equipment processing ran GameMode or lost source authored event selection/local state: " +
                JsonSerializer.Serialize(new { Results = paused, Visits = Visits(player, 6),
                    OrderTrace = globals.Get(Key(0x31)), State = State(player, 6) }));
            executor.AdvanceInventory(Key(0x14), .25);
            Require(Visits(player, 6) == 111, "Real carried script GameMode did not use its actual container/non-reference self.");
            acquisition.Add(Key(0x17), 1, true);
            var first = State(player, 0x17).Identity;
            acquisition.Add(Key(0x17), 1, true);
            var second = player.Item(Key(0x17))!.Variants!.Select(value => value.ScriptState!.Identity).Single(value => value != first);
            Require(player.ScriptInstances().SequenceEqual(new[] { (Key(6), State(player, 6).Identity), (Key(0x17), second), (Key(0x17), first) }),
                "Actual new outer entry/inner extra-list traversal used UI or variant append ordering.");
            player.Unequip(records, Key(6));
            var saved = JsonSerializer.Deserialize<FalloutOpeningInventoryGrant>(JsonSerializer.Serialize(player.Capture()))!;
            var cold = new FalloutPlayerInventory(); cold.Restore(saved.Inventory, saved.EquippedRuntimeFormIds.ToArray(), saved.InventoryRandomState);
            world.BindPlayerInventory(cold);
            var coldExecutor = Executor(cold);
            coldExecutor.AdvanceInventory(Key(0x14), 0, false);
            Require(Visits(cold, 6) == 211 && State(cold, 6).Events is null &&
                cold.ScriptInstances().Select(value => value.Identity).SequenceEqual(player.ScriptInstances().Select(value => value.Identity)),
                "Cold equipment script lost actual local/order/pending-event state or replayed another event.");
            coldExecutor.AdvanceInventory(Key(0x14), 0, false);
            Require(Visits(cold, 6) == 211, "Completed cold equipment event replayed without a new source producer.");
            var duplicate = world.Inventory(Key(0x900), 1).Contents;
            Require(duplicate.Item(Key(6))!.Count == 3 && duplicate.Item(Key(6))!.Variants!.Count(value => value.ScriptState is not null) == 1 &&
                duplicate.ScriptInitializations.Count == 2 && duplicate.ScriptInitializations[0].Identity == duplicate.ScriptInitializations[1].Identity,
                "Duplicate CNTO initialization fabricated summed/max event-list allocation or discarded repeated actual calls.");
            var initialSnapshot = JsonSerializer.Deserialize<FalloutOpeningInventoryGrant>(JsonSerializer.Serialize(duplicate.Capture()))!;
            var initialCold = new FalloutPlayerInventory(); initialCold.Restore(initialSnapshot.Inventory,
                initialSnapshot.EquippedRuntimeFormIds.ToArray(), initialSnapshot.InventoryRandomState);
            world.Get(Key(0x900)).Inventory = new(initialCold); duplicate = initialCold;
            var initialized = coldExecutor.AdvanceInventory(Key(0x900), .1);
            Require(initialized.All(value => value.Error is null) && Visits(duplicate, 6) == 2001 && duplicate.ScriptInitializations.Count == 0,
                "Initial inventory context used a container, replayed fresh locals, or lost regular carried context.");
            coldExecutor.AdvanceInventory(Key(0x900), .1);
            Require(Visits(duplicate, 6) == 2002, "Completed cold initial inventory script calls replayed.");
            var baseInventory = world.Inventory(Key(0x901), 1).Contents;
            coldExecutor.AdvanceInventory(Key(0x901), .1);
            baseInventory.Remove(Key(6), 1, true);
            Require(baseInventory.Capture().Inventory.ScriptOrder!.Single().Item == Key(6),
                "Negative source-base delta incorrectly erased its native outer entry.");
            baseInventory.Add(records, Key(0x17), 1, 1, true);
            baseInventory.Add(records, Key(6), 1, 1, true);
            Require(baseInventory.ScriptInstances().Select(value => value.Item).SequenceEqual(new[] { Key(6), Key(0x17) }),
                "Re-add lost a retained negative-base-delta outer entry position.");
            cold.Add(records, Key(8), 1, 1, true);
            cold.Remove(Key(0x17), 2, true); cold.Add(records, Key(0x17), 1, 1, true);
            Require(cold.ScriptInstances().Select(value => value.Item).SequenceEqual(new[] { Key(6), Key(8), Key(0x17) }),
                "Genuinely erased zero-delta entry re-add reused an obsolete outer position.");
            var beforeFailure = JsonSerializer.Serialize(new { Player = cold.Capture(), Target = duplicate.Capture() });
            cold.PrepareChange = (_, _) => new(() => throw new InvalidDataException("Synthetic equipment-order commit failure."), () => { });
            Reject(() => cold.TransferItemsTo(duplicate, [new(Key(0x17), 1)]));
            Require(beforeFailure == JsonSerializer.Serialize(new { Player = cold.Capture(), Target = duplicate.Capture() }),
                "Failed transfer lost source/target script entry, local or pending-event/order ownership.");
            cold.PrepareChange = null;
            cold.Add(records, Key(9), 1, 1, true);
            var refused = coldExecutor.AdvanceInventory(Key(0x14), .1);
            Require(refused.Any(value => value.Item == Key(9) && value.Error is not null) && Visits(cold, 9) == 1,
                "Unsupported reached inventory command discarded its actually executed local prefix.");
            coldExecutor.AdvanceInventory(Key(0x14), .1);
            Require(Visits(cold, 9) == 1 && State(cold, 9).Error is not null,
                "A later GameMode replayed an unowned inventory command prefix.");
            var faulted = JsonSerializer.Deserialize<FalloutOpeningInventoryGrant>(JsonSerializer.Serialize(cold.Capture()))!;
            var stopped = new FalloutPlayerInventory(); stopped.Restore(faulted.Inventory,
                faulted.EquippedRuntimeFormIds.ToArray(), faulted.InventoryRandomState);
            world.BindPlayerInventory(stopped); Executor(stopped).AdvanceInventory(Key(0x14), .1);
            Require(Visits(stopped, 9) == 1 && State(stopped, 9).Error is not null,
                "Cold resume replayed a retained failed item-script invocation.");
            var missingOrder = new FalloutPlayerInventory();
            missingOrder.Restore(faulted.Inventory with { ScriptOrder = null }, faulted.EquippedRuntimeFormIds.ToArray());
            world.BindPlayerInventory(missingOrder);
            var missingOrderBefore = JsonSerializer.Serialize(missingOrder.Capture());
            var missingOrderResults = Executor(missingOrder).AdvanceInventory(Key(0x14), .1);
            Require(missingOrderResults.Count > 0 && missingOrderResults.All(value => value.Blocks == 0 && value.Error is not null) &&
                missingOrderResults.Any(value => value.Error!.Contains("retained source order", StringComparison.Ordinal)) &&
                missingOrderBefore == JsonSerializer.Serialize(missingOrder.Capture()),
                "Missing traversal ran an unowned frame, hid its divergence or changed retained failed-script state.");
            foreach (var nonscriptHead in new[] { false, true })
            {
                var running = new FalloutPlayerInventory(); world.BindPlayerInventory(running);
                if (nonscriptHead) running.Add(records, Key(2), 1, 1, true);
                running.Add(records, Key(10), 1, 1, true); running.Add(records, Key(11), 1, 1, true);
                var runningExecutor = Executor(running); globals.Set(Key(0x31), 0);
                var samePass = runningExecutor.AdvanceInventory(Key(0x14), .1);
                Require(samePass.All(value => value.Error is null) && globals.Get(Key(0x31)) == (nonscriptHead ? 11243 : 1243) &&
                    Visits(running, 12) == 11 && State(running, 12).Events is null,
                    "Native same-pass head.next restart lost a nonscript head, appended script or deferred OnAdd/GameMode order.");
                running.Add(records, Key(13), 1, 1, true); running.Equip(records, Key(13));
                var noticeOrdinal = running.Notifications.Capture().LastOrdinal;
                var liveMask = runningExecutor.AdvanceInventory(Key(0x14), 0, false);
                Require(liveMask.All(value => value.Error is null) && Visits(running, 13) == 100 && State(running, 13).Events is null,
                    "A newly admitted equipment mask was discarded instead of tested by a later authored block.");
                var unequipNotice = running.Notifications.Capture();
                Require(unequipNotice.LastOrdinal == noticeOrdinal + 1 && unequipNotice.Pending.Last().Event is
                    { Kind: FalloutHudEventKind.ItemUnequipped, Count: 0, Text: " was unequipped." } actualNotice && actualNotice.Source == Key(13),
                    "Default player UnequipItem lost its source display-name/GMST notice or substituted the editor ID.");
                running.Equip(records, Key(13));
                new FalloutInventoryCommands(records, world, running, () => 1, globals).Execute(
                    new(FalloutInventoryCommandKind.Unequip, Key(0x14), Item: Key(13), Silent: true));
                Require(!running.Equipped.Contains(13u) && running.Notifications.Capture().LastOrdinal == unequipNotice.LastOrdinal,
                    "Explicit silent UnequipItem failed its actual mutation or published a visible notice.");
                running.Equip(records, Key(13));
                var refusedInventory = new FalloutPlayerInventory(); var refusedItems = running.Capture();
                refusedInventory.Restore(refusedItems.Inventory, refusedItems.EquippedRuntimeFormIds.ToArray(), refusedItems.InventoryRandomState);
                var beforeNoticeFailure = JsonSerializer.Serialize(refusedInventory.Capture());
                refusedInventory.Notifications.Restore(new(long.MaxValue, null, 0, []));
                Reject(() => new FalloutInventoryCommands(records, world, refusedInventory, () => 1, globals).Execute(
                    new(FalloutInventoryCommandKind.Unequip, Key(0x14), Item: Key(13), Silent: false)));
                Require(JsonSerializer.Serialize(refusedInventory.Capture()) == beforeNoticeFailure &&
                    refusedInventory.Notifications.Capture().LastOrdinal == long.MaxValue,
                    "Refused notification preflight changed actual equipment, event masks or queue publication.");
            }
            var sourceArmor = records.GetEffective(Key(6));
            var ambiguous = new FalloutPlayerInventory();
            ambiguous.Restore(new([cold.Item(Key(6))! with { Count = 2, Variants = [new(1), new(1)] }], null, []), []);
            world.BindPlayerInventory(ambiguous);
            var refusedBefore = JsonSerializer.Serialize(ambiguous.Capture());
            Reject(() => ambiguous.Equip(records, sourceArmor.FormKey));
            Require(refusedBefore == JsonSerializer.Serialize(ambiguous.Capture()),
                "Refused ambiguous scripted equipment published event-list allocation or order before admission.");
            var noContainer = new FalloutPlayerInventory();
            noContainer.Restore(new([cold.Item(Key(6))! with { Variants = null }], null, []), []);
            refusedBefore = JsonSerializer.Serialize(noContainer.Capture()); Reject(() => noContainer.Equip(records, Key(6)));
            Require(refusedBefore == JsonSerializer.Serialize(noContainer.Capture()),
                "Refused equipment without an actual container published script-list state.");
        }
        finally { directory.Delete(true); }
    }
}
