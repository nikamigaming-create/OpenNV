using System.Buffers.Binary;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static partial class InventoryCommandContracts
{
    private static void RunAcquisitionEvents()
    {
        RunEquipmentScriptEvents();
        var directory = Directory.CreateTempSubdirectory("opennv-inventory-event-");
        try
        {
            byte[] Script(uint id, string source, bool local = false)
            {
                var cell = new byte[24]; BinaryPrimitives.WriteUInt32LittleEndian(cell, 1); cell[16] = 1;
                return Record("SCPT", id, [Field("SCHR", new byte[20]), Field("SCDA", []),
                    Field("SCTX", Text(source)), Field("SCRO", BitConverter.GetBytes(0x14u)),
                    Field("SCRO", BitConverter.GetBytes(2u)),
                    .. local ? new[] { Field("SLSD", cell), Field("SCVR", Text("Visits")) } : []]);
            }
            byte[] Scripted(uint id, uint script) => Record("MISC", id, Field("EDID", Text("Event" + id)),
                Field("DATA", new byte[8]), Field("SCRI", BitConverter.GetBytes(script)));
            var header = new byte[12]; BinaryPrimitives.WriteSingleLittleEndian(header, 1.34f);
            File.WriteAllBytes(Path.Combine(directory.FullName, "Items.esm"), Join(
                Record("TES4", 0, Field("HEDR", header)), Record("NPC_", 7, Field("ACBS", new byte[24])),
                Item("MISC", 2, "Product", 8), Scripted(1, 0x200), Scripted(3, 0x201), Scripted(4, 0x202),
                Script(0x200, "scn Convert\nbegin OnAdd player\nplayer.AddItem Product 1 1\nRemoveMe\nend"),
                Script(0x201, "scn Guard\nshort Visits\nbegin OnAdd player\nif Visits == 0\nplayer.AddItem Product 1 1\nset Visits to 1\nendif\nend", true),
                Script(0x202, "scn Refuse\nbegin OnAdd player\nplayer.AddItem Product 1 1\nUnknownReachedEffect\nend"),
                Record("NOTE", 5, Field("EDID", Text("CallbackNote"))),
                Record("CONT", 0x100), Record("CELL", 0x800, Field("DATA", [1])),
                Group(0x800, Reference("REFR", 0x900, 0x100, "SourceContainer"))));
            using var records = FalloutPluginStack.Load(directory.FullName, ["Items.esm"]);
            using var world = new FalloutReferenceWorld(records);
            world.LoadCell(FalloutCellSceneReader.Read(records, Key(0x800)));
            var player = new FalloutPlayerInventory(7); world.BindPlayerInventory(player);
            var container = world.Inventory(Key(0x900), 1).Contents;
            var quests = new FalloutQuestState(records);
            var scripts = new FalloutQuestScripts(records, quests, new HashSet<FalloutFormKey>(), player,
                defaultProcessingDelay: 0, references: world);
            var stages = new FalloutQuestStages(records, quests, (_, _, _) => [], _ => 0);
            var owner = new FalloutInventoryAcquisition(records, world, player, quests, scripts, () => stages, () => 1);
            world.InventoryAcquisition = owner;
            container.Add(records, Key(1), 1, 1, true);
            player.Add(records, Key(2), 1, 1, true);
            owner.Transfer(container, player, Key(0x900), Key(0x14), [new(Key(1), 1)]);
            Require(container.Item(Key(1)) is null && player.Item(Key(1)) is null && player.Item(Key(2))?.Count == 2,
                "Source OnAdd conversion did not atomically add its actual replacement and RemoveMe incoming instance.");

            container.Add(records, Key(3), 1, 1, true);
            owner.Transfer(container, player, Key(0x900), Key(0x14), [new(Key(3), 1)]);
            var state = player.Item(Key(3))!.Variants!.Single().ScriptState!;
            Require(player.Item(Key(2))?.Count == 3 && state.Resolve(records.GetEffective(Key(0x201))).Read("Visits").Number == 1,
                "OnAdd did not persist its actual typed source local.");
            owner.Transfer(player, container, Key(0x14), Key(0x900), [new(Key(3), 1)]);
            var saved = JsonSerializer.Deserialize<FalloutOpeningInventoryGrant>(JsonSerializer.Serialize(container.Capture()))!;
            var restored = new FalloutPlayerInventory();
            restored.Restore(saved.Inventory, saved.EquippedRuntimeFormIds.ToArray(), saved.InventoryRandomState);
            FalloutInventoryScriptState.ValidateInventory(records, restored.Items);
            Require(restored.Item(Key(3))!.Variants!.Single().ScriptState!.Identity == state.Identity &&
                restored.Item(Key(3))!.Variants!.Single().ScriptState!.Resolve(records.GetEffective(Key(0x201))).Read("Visits").Number == 1,
                "Cold inventory JSON lost source event-list identity or local cells.");
            owner.Transfer(container, player, Key(0x900), Key(0x14), [new(Key(3), 1)]);
            Require(player.Item(Key(2))?.Count == 3 && player.Item(Key(3))!.Variants!.Single().ScriptState!.Identity == state.Identity,
                "Returning a retained script instance replayed its one-time source branch.");

            container.Add(records, Key(4), 1, 1, true);
            var before = JsonSerializer.Serialize(new { Source = container.Capture(), Player = player.Capture(), Messages = scripts.MessageResults.Capture() });
            Reject(() => owner.Transfer(container, player, Key(0x900), Key(0x14), [new(Key(4), 1)]));
            Require(before == JsonSerializer.Serialize(new { Source = container.Capture(), Player = player.Capture(), Messages = scripts.MessageResults.Capture() }),
                "Unsupported reached OnAdd published a nested inventory prefix or result owner.");

            container.Add(records, Key(2), 1, 1, true);
            var beforeCallback = JsonSerializer.Serialize(new { Source = container.Capture(), Player = player.Capture() });
            var independentWrites = 0;
            player.PrepareChange = (_, _) => new(() => { independentWrites++; world.PlayerNotes.AddMembership(Key(5)); }, () => { });
            Reject(() => owner.Transfer(container, player, Key(0x900), Key(0x14), [new(Key(2), 1)]));
            Require(independentWrites == 1 && beforeCallback == JsonSerializer.Serialize(new { Source = container.Capture(), Player = player.Capture() }) && world.PlayerNotes.Contains(Key(5)),
                "Inventory callback freshness failure erased an independent note write or published item changes.");
            player.PrepareChange = null;
            var committedCount = player.Item(Key(2))!.Count;
            var failedFinalizers = 0;
            player.PrepareChange = (_, _) => new(() => { }, () => { },
                () => { failedFinalizers++; throw new InvalidDataException("Synthetic finalizer failure."); });
            Reject(() => owner.Transfer(container, player, Key(0x900), Key(0x14), [new(Key(2), 1)]));
            Require(failedFinalizers == 1 && container.Item(Key(2)) is null && player.Item(Key(2))?.Count == committedCount + 1 &&
                owner.Failure is not null && player.Item(Key(3))!.Variants!.Single().ScriptState!.Identity == state.Identity,
                "Finalizer failure rolled back part of an already committed inventory/event state or lost its failure latch.");
            player.PrepareChange = null;
            container.Add(records, Key(2), 1, 1, true);
            var afterFailure = JsonSerializer.Serialize(new { Source = container.Capture(), Player = player.Capture() });
            Reject(() => owner.Transfer(container, player, Key(0x900), Key(0x14), [new(Key(2), 1)]));
            Require(afterFailure == JsonSerializer.Serialize(new { Source = container.Capture(), Player = player.Capture() }),
                "Latched committed-event failure admitted another transaction or left inventory locks held.");

            var partialCommits = 0; var compensations = 0; var externalParticipant = 0;
            container.PrepareChange = (_, _) => new(
                () => { partialCommits++; externalParticipant = 1; throw new InvalidDataException("Synthetic commit refusal."); },
                () => { compensations++; externalParticipant = 0; throw new InvalidDataException("Synthetic rollback failure."); });
            var cleanupBefore = JsonSerializer.Serialize(new { Source = container.Capture(), Player = player.Capture() });
            var cleanupFailed = false;
            try { container.TransferItemsTo(player, [new(Key(2), 1)]); }
            catch (FalloutInventoryCleanupException error)
            {
                cleanupFailed = error.ToString().Contains("Synthetic commit refusal.", StringComparison.Ordinal) &&
                    error.ToString().Contains("Synthetic rollback failure.", StringComparison.Ordinal);
            }
            Require(cleanupFailed && partialCommits == 1 && compensations == 1 && externalParticipant == 0 &&
                cleanupBefore == JsonSerializer.Serialize(new { Source = container.Capture(), Player = player.Capture() }),
                $"Rollback failure lost its original exception or changed restored state: commits={partialCommits}; rollbacks={compensations}; external={externalParticipant}.");
            container.PrepareChange = null;
            container.Add(records, Key(2), 1, 1, true); player.Add(records, Key(2), 1, 1, true);

        }
        finally { directory.Delete(true); }
    }
}
