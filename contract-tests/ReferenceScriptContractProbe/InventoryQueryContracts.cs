using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class InventoryQueryContracts
{
    internal static void Run()
    {
        var directory = Directory.CreateTempSubdirectory("opennv-inventory-query-");
        try
        {
            var scriptHeader = new byte[20]; BinaryPrimitives.WriteUInt32LittleEndian(scriptHeader.AsSpan(12), 2); scriptHeader[16] = 1;
            const string script = "short done\nshort result\nbegin GameMode\nif done == 0\n" +
                "set result to player.GetItemCount Loot + (player).GetItemCount Gun\nset done to 1\nendif\nend";
            File.WriteAllBytes(Path.Combine(directory.FullName, "Items.esm"), Join(Header(),
                Item("MISC", 1, "Loot", 8), Item("WEAP", 2, "Gun", 15), Record("NOTE", 3),
                Record("FLST", 0x120, Field("LNAM", U32(1))),
                Record("FLST", 0x125, Field("EDID", Text("ActorList")), Field("LNAM", U32(0x100))),
                Record("FLST", 0x126, Field("EDID", Text("ReferenceList")), Field("LNAM", U32(0x900))),
                Record("FLST", 0x127, Field("EDID", Text("PlayerList")), Field("LNAM", U32(7))), Record("NPC_", 7),
                Record("NPC_", 0x100, Field("ACBS", new byte[24]), Field("CNTO", Join(U32(1), U32(3)))),
                Record("PERK", 0x130), Record("CONT", 0x110), Record("CELL", 0x800, Field("DATA", [1])),
                Group(0x800, Join(Reference("ACHR", 0x900, 0x100, "Actor"), Reference("REFR", 0x901, 0x110, "Chest"))),
                Record("SCPT", 0x601, Field("SCHR", scriptHeader), Local(1, "done"), Local(2, "result"),
                    Field("SCRO", U32(0x14)), Field("SCRO", U32(1)), Field("SCRO", U32(2)), Field("SCTX", Text(script))),
                Record("QUST", 0x600, Field("DATA", [1, 0]), Field("SCRI", U32(0x601)),
                    Field("SCRO", U32(0x14)), Field("SCRO", U32(1)), Field("SCRO", U32(2)),
                    Field("SCRO", U32(0x900)), Field("SCRO", U32(0x901)),
                    Field("SCRO", U32(0x125)), Field("SCRO", U32(0x126)), Field("SCRO", U32(0x127)))));
            File.WriteAllBytes(Path.Combine(directory.FullName, "Other.esm"), Header());
            var conditionBytes = new byte[28]; BinaryPrimitives.WriteUInt16LittleEndian(conditionBytes.AsSpan(8), 47);
            BinaryPrimitives.WriteUInt32LittleEndian(conditionBytes.AsSpan(12), 0x01000001);
            File.WriteAllBytes(Path.Combine(directory.FullName, "Queries.esp"), Join(Header("Other.esm", "Items.esm"),
                Record("INFO", 0x02000810, Field("CTDA", conditionBytes)),
                Record("FLST", 0x02000120, Field("EDID", Text("CountList")), Field("LNAM", U32(0x01000001)),
                    Field("LNAM", U32(0x01000002)), Field("LNAM", U32(0x01000001)), Field("LNAM", U32(0x01000120)),
                    Field("LNAM", U32(0x01000100)), Field("LNAM", U32(0x01000003))),
                Record("FLST", 0x02000121), Record("FLST", 0x02000122, Field("LNAM", [1, 0]))));
            using var records = FalloutPluginStack.Load(directory.FullName, ["Items.esm", "Other.esm", "Queries.esp"]);
            FalloutFormKey Key(uint id) => new("Items.esm", id);
            using var world = new FalloutReferenceWorld(records);
            world.LoadCell(FalloutCellSceneReader.Read(records, Key(0x800)));
            var player = new FalloutPlayerInventory(123); player.Add(records, Key(1), 2, 1, true); player.Add(records, Key(2), 1, 1, true);
            var chest = world.Inventory(Key(0x901), 1).Contents; chest.Add(records, Key(1), 4, 1, true);
            var commands = new FalloutInventoryCommands(records, world, player, () => 1);
            var condition = FalloutCondition.Read(records.GetEffective(new("Queries.esp", 0x810))).Single();
            var identity = new FalloutDialogueSpeaker(Key(0x100), Key(0x100), Key(0x850), "Synthetic", null, false);
            FalloutFormKey? queried = null;
            double Query(FalloutFormKey reference, FalloutFormKey item) { queried = reference; return commands.ItemCount(reference, item); }
            var context = new FalloutDialogueConditions(records, new(records), Key(0x900), identity, itemCount: Query);
            Check(condition.FormArgument1 == Key(1) && context.Evaluate(condition) == 3 && queried == Key(0x900), "Speaker count or source masters changed.");
            Check(context.Evaluate(condition with { RunOn = 1 }) == 2 && queried == Key(0x14), "Target count did not read the player.");
            var directed = new FalloutDialogueConditions(records, new(records), Key(0x900), identity, listener: Key(0x901), itemCount: Query);
            Check(directed.Evaluate(condition with { RunOn = 1 }) == 4 && queried == Key(0x901), "A supplied listener read player inventory.");
            Check(context.Evaluate(condition with { RunOn = 2, Reference = 0x01000901 }) == 4 && queried == Key(0x901), "Explicit subject lost source masters.");
            var before = JsonSerializer.Serialize(player.Capture());
            Check(commands.ItemCount(Key(0x14), new("Queries.esp", 0x120)) == 5 &&
                commands.ItemCount(Key(0x14), new("Queries.esp", 0x121)) == 0 &&
                commands.ItemCount(Key(0x14), Key(3)) == 0 && before == JsonSerializer.Serialize(player.Capture()),
                "Direct list totals, repeated entries, nested-list exclusion or read-only state changed.");
            Check(FalloutInventoryConditions.Evaluate(records, player, _ => false, condition) == 2, "Recipe item count escaped shared ownership.");
            var perk = condition with { Function = 449, Argument1 = 0x01000130, Argument2 = 0 };
            var granted = false;
            bool HasPerk(FalloutFormKey form)
            {
                Check(form == Key(0x130), "Dialogue perk lost declaring source masters.");
                return granted;
            }
            float? Perk(FalloutCondition query) => FalloutInventoryConditions.EvaluateDialoguePlayer(records, player, HasPerk, query);
            Check(Perk(perk) is null && Perk(perk with { RunOn = 2, Reference = 0x01000900 }) is null &&
                Perk(perk with { RunOn = 1 }) == 0 && Perk(perk with { RunOn = 2, Reference = 0x01000014 }) == 0,
                "Dialogue player perk scope confused speaker, target or explicit player.");
            granted = true;
            Check(Perk(perk with { RunOn = 1 }) == 1 && Perk(perk with { RunOn = 2, Reference = 0x01000014 }) == 1 &&
                Perk(perk with { RunOn = 2 }) is null && Perk(perk with { RunOn = 3 }) is null &&
                Perk(perk with { RunOn = 1, Argument2 = 1 }) is null,
                "Dialogue perk retained stale state or admitted an unowned subject/rank query.");
            Reject(() => context.Evaluate(condition with { RunOn = 2 })); Reject(() => context.Evaluate(condition with { RunOn = 3 }));
            Reject(() => new FalloutDialogueConditions(records, new(records), Key(0x900), identity).Evaluate(condition));
            Reject(() => commands.ItemCount(Key(0x100), Key(1))); Reject(() => commands.ItemCount(Key(0x14), new("Queries.esp", 0x122)));
            var executor = new FalloutReferenceScripts(records, world, new(records), new((_, _) => false, _ => { }, Inventory: commands));
            var quest = records.GetEffective(Key(0x600)); var fields = quest.ReadSubrecords().ToArray();
            executor.ExecuteStage(quest, fields, "if Actor.GetItemCount Loot != 3\nUnexpectedCount\nendif\n" +
                "if (player).GetItemCount (Loot) != 2\nUnexpectedCount\nendif\nif Chest.GetItemCount Loot != 4\nUnexpectedCount\nendif");
            Reject(() => executor.ExecuteStage(quest, fields, "player.GetItemCount Loot 1"));
            var referencesBefore = JsonSerializer.Serialize(world.Capture());
            executor.ExecuteStage(quest, fields, "if Actor.IsInList ActorList != 1 || (Actor).IsInList (ReferenceList) != 0\nUnexpectedMembership\nendif\n" +
                "if player.IsInList PlayerList != 1 || player.IsInList ActorList != 0\nUnexpectedMembership\nendif");
            Check(referencesBefore == JsonSerializer.Serialize(world.Capture()), "IsInList changed authoritative reference state.");
            Reject(() => executor.ExecuteStage(quest, fields, "Actor.IsInList ActorList 1"));
            Reject(() => executor.ExecuteStage(quest, fields, "Actor.IsInList Loot"));
            var quests = new FalloutQuestState(records);
            var scripts = new FalloutQuestScripts(records, quests, new HashSet<FalloutFormKey>(), player, defaultProcessingDelay: 0, references: world);
            scripts.Advance(0);
            Check(quests.Variable(Key(0x600), 1) == 1 && quests.Variable(Key(0x600), 2) == 3, "Fallback/typed quest query lost its value or suffix.");
            player.Remove(Key(1), 1, true);
            Check(context.Evaluate(condition with { RunOn = 1 }) == 1 && directed.Evaluate(condition with { RunOn = 1 }) == 4,
                "Changed player inventory was stale or changed another owner.");
            var saved = JsonSerializer.Deserialize<FalloutOpeningInventoryGrant>(JsonSerializer.Serialize(player.Capture()))!;
            var cold = new FalloutPlayerInventory(); cold.Restore(saved.Inventory, saved.EquippedRuntimeFormIds.ToArray(), saved.InventoryRandomState);
            Check(new FalloutInventoryCommands(records, world, cold, () => 1).ItemCount(Key(0x14), new("Queries.esp", 0x120)) == 3,
                "Cold query did not read retained item counts.");
            player.Add(records, Key(1), int.MaxValue - 1, 1, true);
            Check(commands.ItemCount(Key(0x14), new("Queries.esp", 0x120)) == 2d * int.MaxValue + 1, "List total overflowed the source numeric result.");
            Console.WriteLine("OPENNV_INVENTORY_QUERY_CONTRACT_PASS self=true player=true listener=true explicit=true sourceMasters=true list=true liveChanges=true readonly=true cold=true fallback=true typed=true missingOwnerRefused=true");
        }
        finally { directory.Delete(true); }
    }

    private static void Check(bool valid, string error) { if (!valid) throw new InvalidDataException(error); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or InvalidOperationException or NotSupportedException or KeyNotFoundException) { return; }
        throw new InvalidDataException("Unbound inventory query was admitted.");
    }
    private static byte[] U32(uint value) => BitConverter.GetBytes(value);
    private static byte[] Text(string value) => Encoding.ASCII.GetBytes(value + '\0');
    private static byte[] Join(params byte[][] fields) => fields.SelectMany(field => field).ToArray();
    private static byte[] Header(params string[] masters) => Record("TES4", 0, Join(masters.Select(master => Join(Field("MAST", Text(master)), Field("DATA", new byte[8]))).ToArray()));
    private static byte[] Local(uint index, string name)
    {
        var data = new byte[24]; BinaryPrimitives.WriteUInt32LittleEndian(data, index);
        return Join(Field("SLSD", data), Field("SCVR", Text(name)));
    }
    private static byte[] Item(string type, uint id, string name, int extent) => Record(type, id, Field("EDID", Text(name)), Field("DATA", new byte[extent]));
    private static byte[] Reference(string type, uint id, uint form, string name) => Record(type, id, Field("NAME", U32(form)), Field("DATA", new byte[24]), Field("EDID", Text(name)));
    private static byte[] Group(uint cell, byte[] data)
    {
        var result = new byte[24 + data.Length]; Encoding.ASCII.GetBytes("GRUP").CopyTo(result, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), (uint)result.Length); BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(8), cell);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(12), 6); data.CopyTo(result, 24); return result;
    }
    private static byte[] Field(string name, byte[] data)
    {
        var result = new byte[6 + data.Length]; Encoding.ASCII.GetBytes(name).CopyTo(result, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(4), checked((ushort)data.Length)); data.CopyTo(result, 6); return result;
    }
    private static byte[] Record(string name, uint id, params byte[][] fields)
    {
        var data = Join(fields); var result = new byte[24 + data.Length]; Encoding.ASCII.GetBytes(name).CopyTo(result, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), (uint)data.Length); BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(12), id);
        data.CopyTo(result, 24); return result;
    }
}
