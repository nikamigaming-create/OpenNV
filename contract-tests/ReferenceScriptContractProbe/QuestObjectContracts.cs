using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class QuestObjectContracts
{
    internal static void Run()
    {
        var directory = Directory.CreateTempSubdirectory("opennv-quest-object-");
        try
        {
            File.WriteAllBytes(Path.Combine(directory.FullName, "Items.esm"), Join(Header(),
                Item(1, 0x400, 3), Item(2, 0, 2), Record("NPC_", 7, 0), Quest(), Script()));
            File.WriteAllBytes(Path.Combine(directory.FullName, "Winner.esp"), Join(Header("Items.esm"), Item(1, 0x400, 7.5f)));
            using var records = FalloutPluginStack.Load(directory.FullName, ["Items.esm", "Winner.esp"]);
            var item = records.GetEffective(Key(1));
            var sourceHash = SHA256.HashData(item.ReadData());
            using var world = new FalloutReferenceWorld(records);
            var quests = new FalloutQuestState(records);
            var quest = records.GetEffective(Key(0x100));
            var inventory = new FalloutPlayerInventory();
            inventory.Add(records, Key(1), 2, 1, true);
            inventory.Add(records, Key(2), 1, 1, true);
            var skills = new FalloutPlayerSkills(records, () => new(5, 5, 5, 5, 5, 5, 5), _ => false,
                () => [], null, inventory, Key(0x14), () => Key(7), () => false);
            var executor = new FalloutReferenceScripts(records, world, quests, new((_, _) => false,
                _ => throw new NotSupportedException("Quest-object effect escaped its form owner.")));
            void Execute(string body) => executor.ExecuteStage(quest, quest.ReadSubrecords().ToArray(), body);
            Require(item.Plugin.Name == "Winner.esp" && !FalloutInventoryAccess.CanTransfer(records, item, true) && skills.Value(46) == 2,
                "Source winner's quest flag did not protect the item or exempt carried weight.");
            var initialRevision = inventory.Revision;
            Execute("SetQuestObject QuestLoot 0");
            Require(!records.QuestObjects.IsQuestObject(Key(1)) && FalloutInventoryAccess.CanTransfer(records, item, true) &&
                skills.Value(46) == 17 && inventory.Revision == initialRevision,
                "Shared form mutation did not invalidate weight independently of inventory counts.");
            var session = new FalloutScriptSession(questObjects: records.QuestObjects);
            var snapshot = JsonSerializer.Deserialize<FalloutScriptSessionSnapshot>(JsonSerializer.Serialize(session.Capture()))!;
            using (var coldRecords = FalloutPluginStack.Load(directory.FullName, ["Items.esm", "Winner.esp"]))
            {
                var cold = new FalloutScriptSession(questObjects: coldRecords.QuestObjects);
                cold.Restore(snapshot);
                Require(!coldRecords.QuestObjects.IsQuestObject(Key(1)) &&
                    FalloutInventoryAccess.CanTransfer(coldRecords, coldRecords.GetEffective(Key(1)), true) &&
                    JsonSerializer.Serialize(cold.Capture()) == JsonSerializer.Serialize(snapshot),
                    "Cold session lost the global quest-object flag.");
                var before = JsonSerializer.Serialize(cold.Capture());
                var revision = coldRecords.QuestObjects.Revision;
                var row = snapshot.QuestObjects!.Single();
                Reject(() => cold.Restore(snapshot with { QuestObjects = [row with { SourceFlags = row.SourceFlags ^ 2 }] }));
                Reject(() => cold.Restore(snapshot with { QuestObjects = [row with { SourceSha256 = new string('0', 64) }] }));
                Reject(() => cold.Restore(snapshot with { QuestObjects = [row, row] }));
                Require(before == JsonSerializer.Serialize(cold.Capture()) && revision == coldRecords.QuestObjects.Revision,
                    "Invalid restoration partially changed live form state.");
            }
            Execute("if 0\nSetQuestObject MissingForm 9\nendif\nSetQuestObject QuestLoot 1");
            Require(skills.Value(46) == 2 && records.QuestObjects.Capture().Count == 0,
                "Returning to the source flag retained an override or stale weight.");
            inventory.RemoveAll(records, true, null, false, false);
            Require(inventory.Items is [{ FormKey: var kept, Count: 2 }] && kept == Key(1),
                "RemoveAllItems did not retain the current quest item.");
            quests.SetRunning(quest.FormKey, true);
            var scripts = new FalloutQuestScripts(records, quests, new HashSet<FalloutFormKey> { quest.FormKey }, inventory,
                defaultProcessingDelay: 1);
            scripts.AdvanceClaimed(quest.FormKey, 1.1, new((_, _) => throw new InvalidDataException("Fallback changed a quest stage."), _ => 0));
            Require(!records.QuestObjects.IsQuestObject(Key(1)) && scripts.Capture().Instances.Single().Error is null,
                $"Fallback quest script did not share the result-script form owner: {JsonSerializer.Serialize(scripts.Capture().Instances)}.");
            var beforeFlag = JsonSerializer.Serialize(records.QuestObjects.Capture());
            Reject(() => Execute("SetQuestObject QuestLoot 2"));
            Reject(() => records.QuestObjects.Set(Key(7), true));
            Require(beforeFlag == JsonSerializer.Serialize(records.QuestObjects.Capture()), "Invalid flag/type changed shared form state.");
            inventory.RemoveAll(records, true, null, false, false);
            Require(inventory.Items.Count == 0 && item.Flags == 0x400 && sourceHash.AsSpan().SequenceEqual(SHA256.HashData(item.ReadData())),
                "Cleared flag did not release RemoveAllItems protection or mutated the source record.");
            Console.WriteLine("OPENNV_QUEST_OBJECT_PASS winningForm=true referenceAndQuestScripts=true transfer=true removeAll=true weightRevision=true cold=true invalidAtomic=true sourceReadonly=true actorLifecycle=unowned");
        }
        finally { directory.Delete(true); }
    }

    private static FalloutFormKey Key(uint id) => new("Items.esm", id);
    private static void Require(bool value, string error) { if (!value) throw new InvalidDataException(error); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidDataException("Invalid quest-object operation was admitted.");
    }
    private static byte[] Header(string? master = null)
    {
        var data = new byte[12]; BinaryPrimitives.WriteSingleLittleEndian(data, 1.34f);
        return master is null ? Record("TES4", 0, 0, Field("HEDR", data)) :
            Record("TES4", 0, 0, Field("HEDR", data), Field("MAST", Text(master)), Field("DATA", new byte[8]));
    }
    private static byte[] Item(uint id, uint flags, float weight)
    {
        var data = new byte[8]; BinaryPrimitives.WriteInt32LittleEndian(data, 10); BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(4), weight);
        return Record("MISC", id, flags, Field("EDID", Text(id == 1 ? "QuestLoot" : "OtherLoot")), Field("FULL", Text("Synthetic item")), Field("DATA", data));
    }
    private static byte[] Quest() => Record("QUST", 0x100, 0, Field("EDID", Text("FlagQuest")),
        Field("DATA", new byte[8]), Field("SCRI", BitConverter.GetBytes(0x200u)), Field("SCRO", BitConverter.GetBytes(1u)));
    private static byte[] Script()
    {
        var header = new byte[20]; header[16] = 1; BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), 1);
        return Record("SCPT", 0x200, 0, Field("SCHR", header), Field("SCRO", BitConverter.GetBytes(1u)),
            Field("SCTX", Text("begin GameMode\nSetQuestObject QuestLoot 0\nend")));
    }
    private static byte[] Record(string type, uint id, uint flags, params byte[][] fields)
    {
        var data = Join(fields); var bytes = new byte[24 + data.Length]; Encoding.ASCII.GetBytes(type).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)data.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), flags); BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), id);
        data.CopyTo(bytes, 24); return bytes;
    }
    private static byte[] Field(string type, byte[] data)
    {
        var bytes = new byte[6 + data.Length]; Encoding.ASCII.GetBytes(type).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), (ushort)data.Length); data.CopyTo(bytes, 6); return bytes;
    }
    private static byte[] Text(string value) => Encoding.ASCII.GetBytes(value + '\0');
    private static byte[] Join(params byte[][] fields) => fields.SelectMany(field => field).ToArray();
}
