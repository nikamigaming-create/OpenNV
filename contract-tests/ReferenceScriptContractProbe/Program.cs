using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

var directory = Path.Combine(Path.GetTempPath(), "opennv-reference-contract-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
try
{
    File.WriteAllBytes(Path.Combine(directory, "Base.esm"), Header()
        .Concat(Script(1)).Concat(Record("SCPT", 0x501, Local(1, "shared")))
        .Concat(Record("SCPT", 0x502, Local(1, "count"), Local(1, "conflictingName")))
        .Concat(Record("QUST", 0x600, Field("EDID", Text("TestQuest")), Field("SCRI", BitConverter.GetBytes(0x501u))))
        .Concat(Record("ACTI", 0x700, Field("EDID", Text("ModelLessActivator")), Field("SCRI", BitConverter.GetBytes(0x500u))))
        .Concat(Cell(0x800, Reference(0x900, "FirstREF"), Reference(0x901, "SecondREF")))
        .Concat(Cell(0x801, Reference(0x902, "PeerREF"))).ToArray());
    File.WriteAllBytes(Path.Combine(directory, "Patch.esp"), Header("Base.esm").Concat(Script(2)).ToArray());
    using var records = FalloutPluginStack.Load(directory, ["Base.esm", "Patch.esp"]);
    Reject(() => FalloutScriptLocals.Read(records.GetEffective(Key(0x502))));
    var firstCell = FalloutCellSceneReader.Read(records, Key(0x800));
    var secondCell = FalloutCellSceneReader.Read(records, Key(0x801));
    using var world = new FalloutReferenceWorld(records);
    var first = world.LoadCell(firstCell);
    var peer = world.LoadCell(secondCell).Single();
    Require(world.InstanceCount == 3 && firstCell.BaseObjects.Values.All(value => value.ModelPath is null), "Model-less reference lifetime failed.");
    Require(ReferenceEquals(first[0].Script, first[1].Script) && ReferenceEquals(first[0].Script, peer.Script), "Script definitions are not reused.");
    var quests = new FalloutQuestState(records);
    var scripts = new FalloutReferenceScripts(records, world, quests, new((_, _) => false,
        _ => throw new InvalidDataException("Unexpected presentation effect in scalar contract.")));
    Require(scripts.Dispatch(Key(0x900), "OnTriggerEnter", Key(0x14)).Blocks == 2, "Filtered and unfiltered source event order failed.");
    Require(first[0].Read(1) == 2 && first[1].Read(1) == 0 && peer.Read(1) == 1 && quests.Variable(Key(0x600), 1) == 2,
        "Winning override, per-instance isolation, cross-cell reference write or ordered quest write failed.");
    Require(scripts.Dispatch(Key(0x901), "OnTriggerEnter", Key(0x902)).Blocks == 2 && first[1].Read(1) == 0 && first[1].Read(2) == 5,
        "Mismatched event filter ran or matching blocks lost source order.");
    first[0].Write(2, 1.0000000000000002);
    for (var iteration = 0; iteration < 30; ++iteration)
    {
        scripts.UnloadCell(Key(0x800)); world.UnloadCell(Key(0x800));
        Reject(() => scripts.Dispatch(Key(0x900), "GameMode"));
        world.LoadCell(firstCell);
    }
    Require(world.InstanceCount == 3 && world.ScriptDefinitionCount == 1 && first[0].Read(2) == 1.0000000000000002,
        "Cell teardown grew or reset mutable/reference state.");
    first[0].Write(1, 6);
    var failure = scripts.Dispatch(Key(0x900), "GameMode");
    Require(failure.Error?.Contains("MissingOperation", StringComparison.Ordinal) == true && first[0].Read(1) == 7,
        "Reached unsupported command lost the executed prefix or explicit failure.");
    Require(scripts.Dispatch(Key(0x901), "GameMode").Error is null, "One instance's failure poisoned another instance of the same script.");
    var saved = JsonSerializer.Serialize(world.Capture());
    var snapshots = JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(saved)!;
    using var restored = new FalloutReferenceWorld(records);
    restored.Restore(snapshots);
    restored.LoadCell(firstCell); restored.LoadCell(secondCell);
    Require(JsonSerializer.Serialize(restored.Capture()) == saved, "Cold state changed local Float64 bits or fault identity.");
    var coldScripts = new FalloutReferenceScripts(records, restored, quests, new((_, _) => false, _ => { }));
    Require(coldScripts.Dispatch(Key(0x900), "GameMode").Error == failure.Error && restored.Get(Key(0x900)).Read(1) == 7,
        "Cold restoration reran a failed block or discarded its error.");
    using var rejected = new FalloutReferenceWorld(records);
    Reject(() => rejected.Restore([snapshots[0], snapshots[0]]));
    Require(rejected.InstanceCount == 0, "Failed restore partially published reference state.");
    Reject(() => rejected.Restore([snapshots[0] with { Variables = new Dictionary<uint, double> { [1] = double.NaN, [2] = 0 } }]));
    using var originalRecords = FalloutPluginStack.Load(directory, ["Base.esm"]);
    using var wrongSource = new FalloutReferenceWorld(originalRecords);
    Reject(() => wrongSource.Restore(snapshots));
    Require(wrongSource.InstanceCount == 0, "Changed winning script source was admitted during restore.");
    Console.WriteLine("OPENNV_REFERENCE_SCRIPT_CONTRACT_PASS modelLess=true winningOverride=true instanceIsolation=true eventFilters=true sourceOrder=true crossCell=true teardown=true coldState=true explicitFailure=true sourceDriftRejected=true");
}
finally
{
    File.Delete(Path.Combine(directory, "Base.esm"));
    File.Delete(Path.Combine(directory, "Patch.esp"));
    Directory.Delete(directory);
}

static FalloutFormKey Key(uint id) => new("Base.esm", id);
static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
static void Reject(Action action)
{
    try { action(); }
    catch (Exception error) when (error is InvalidDataException or InvalidOperationException or NotSupportedException) { return; }
    throw new InvalidDataException("Invalid state/event was admitted.");
}
static byte[] Script(int increment)
{
    var source = $"scn ObjectScript\nshort count\nfloat timer\nbegin OnTriggerEnter player\n" +
        $"set count to count + {increment}\nset PeerREF.count to PeerREF.count + 1\nset TestQuest.shared to TestQuest.shared + count\nend\n" +
        "begin OnTriggerEnter\nset timer to timer + 1\nend\nbegin OnTriggerEnter PeerREF\nset timer to timer + 4\nend\n" +
        "begin GameMode\nif count >= 6\nset count to count + 1\nMissingOperation\nendif\nend";
    return Record("SCPT", 0x500, Field("EDID", Text("ObjectScript")), Local(1, "count"), Local(1, "count"), Local(2, "timer"),
        Field("SCRO", BitConverter.GetBytes(0x14u)), Field("SCRO", BitConverter.GetBytes(0x902u)),
        Field("SCRO", BitConverter.GetBytes(0x600u)), Field("SCTX", Text(source)));
}
static byte[] Local(uint index, string name)
{
    var data = new byte[24]; BinaryPrimitives.WriteUInt32LittleEndian(data, index);
    return Field("SLSD", data).Concat(Field("SCVR", Text(name))).ToArray();
}
static byte[] Reference(uint id, string name) => Record("REFR", id, Field("EDID", Text(name)),
    Field("NAME", BitConverter.GetBytes(0x700u)), Field("DATA", new byte[24]));
static byte[] Cell(uint id, params byte[][] references)
{
    var body = references.SelectMany(bytes => bytes).ToArray();
    var group = new byte[24 + body.Length]; Encoding.ASCII.GetBytes("GRUP").CopyTo(group, 0);
    BinaryPrimitives.WriteUInt32LittleEndian(group.AsSpan(4), (uint)group.Length);
    BinaryPrimitives.WriteUInt32LittleEndian(group.AsSpan(8), id);
    BinaryPrimitives.WriteInt32LittleEndian(group.AsSpan(12), 6); body.CopyTo(group, 24);
    return Record("CELL", id, Field("DATA", [1])).Concat(group).ToArray();
}
static byte[] Header(string? master = null)
{
    var data = new byte[12]; BinaryPrimitives.WriteSingleLittleEndian(data, 1.34f);
    return master is null ? Record("TES4", 0, Field("HEDR", data)) :
        Record("TES4", 0, Field("HEDR", data), Field("MAST", Text(master)), Field("DATA", new byte[8]));
}
static byte[] Text(string text) => Encoding.ASCII.GetBytes(text + '\0');
static byte[] Field(string signature, byte[] data)
{
    var bytes = new byte[6 + data.Length]; Encoding.ASCII.GetBytes(signature).CopyTo(bytes, 0);
    BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), checked((ushort)data.Length)); data.CopyTo(bytes, 6); return bytes;
}
static byte[] Record(string signature, uint id, params byte[][] fields)
{
    var data = fields.SelectMany(bytes => bytes).ToArray();
    var bytes = new byte[24 + data.Length]; Encoding.ASCII.GetBytes(signature).CopyTo(bytes, 0);
    BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)data.Length);
    BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), id); data.CopyTo(bytes, 24); return bytes;
}
