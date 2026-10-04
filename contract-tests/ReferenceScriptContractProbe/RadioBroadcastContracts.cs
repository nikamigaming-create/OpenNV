using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class RadioBroadcastContracts
{
    internal static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-broadcast-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllBytes(Path.Combine(directory, "Broadcast.esm"), Fixture());
            using var records = FalloutPluginStack.Load(directory, ["Broadcast.esm"]);
            using var world = new FalloutReferenceWorld(records);
            Require(world.GetBroadcastState(Key(900)) && !world.GetBroadcastState(Key(902)), "Broadcast defaults differ from winning TACT flags.");
            var voiceStops = 0;
            using var voice = records.SoundVoices.Register(Key(500), Key(900), "synthetic-radio-voice", () => true, () => ++voiceStops);
            var enabled = world.Get(Key(900)).Enabled;
            var receiver = JsonSerializer.Serialize(world.PipBoyRadio.Capture());
            world.SetBroadcastState(Key(900), 0);
            Require(!world.GetBroadcastState(Key(900)) && world.GetBroadcastState(Key(901)) &&
                world.Get(Key(900)).Enabled == enabled && voiceStops == 0 && records.SoundVoices.ActiveVoices == 1 &&
                JsonSerializer.Serialize(world.PipBoyRadio.Capture()) == receiver, "Broadcast state leaked to another placement, enable state, receiver or voice.");
            foreach (var invalid in new[] { -1d, .5d, 2d, double.NaN, double.PositiveInfinity }) Reject(() => world.SetBroadcastState(Key(900), invalid));
            var count = world.InstanceCount;
            Reject(() => world.SetBroadcastState(Key(903), 0));
            Require(world.InstanceCount == count && !world.GetBroadcastState(Key(900)), "Invalid broadcast command partially changed world state.");
            var quests = new FalloutQuestState(records);
            var executor = new FalloutReferenceScripts(records, world, quests,
                new((_, _) => false, _ => throw new InvalidDataException("Broadcast command escaped its state owner.")));
            var quest = records.GetEffective(Key(1101));
            executor.ExecuteStage(quest, quest.ReadSubrecords().ToArray(), "RadioA.SetBroadcastState 1");
            Require(world.GetBroadcastState(Key(900)), "Source result did not bind the placed station.");
            var self = records.GetEffective(Key(900));
            executor.ExecuteStage(self, self.ReadSubrecords().ToArray(), "SetBroadcastState 0");
            Require(!world.GetBroadcastState(Key(900)), "Calling-reference broadcast command lost its owner.");
            var saved = JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))!;
            using var cold = new FalloutReferenceWorld(records);
            cold.Restore(saved);
            Require(!cold.GetBroadcastState(Key(900)) && cold.GetBroadcastState(Key(901)) &&
                JsonSerializer.Serialize(cold.Capture()) == JsonSerializer.Serialize(saved), "Cold restoration lost per-reference broadcast state.");
            using var malformed = new FalloutReferenceWorld(records);
            var ordinary = world.Get(Key(903)).Capture();
            Reject(() => malformed.Restore([.. saved, ordinary with { BroadcastState = true }]));
            Require(malformed.InstanceCount == 0, "Invalid saved non-radio state partially restored references.");
            foreach (var shared in new[] { false, true })
            {
                using var scriptWorld = new FalloutReferenceWorld(records);
                var scriptQuests = new FalloutQuestState(records);
                var source = new FalloutReferenceScripts(records, scriptWorld, scriptQuests,
                    new((_, _) => false, _ => throw new InvalidDataException("Source broadcast escaped its owner.")));
                var scripts = new FalloutQuestScripts(records, scriptQuests, new HashSet<FalloutFormKey>(), new(),
                    defaultProcessingDelay: 0, references: scriptWorld);
                scripts.Host = new((_, _) => throw new InvalidDataException("Broadcast fixture changed a stage."), _ => 0,
                    shared ? source.ExecuteProgram : null);
                scripts.Advance(0);
                Require(scripts.Capture().Instances.Single().Error is null && !scriptWorld.GetBroadcastState(Key(900)) &&
                    scriptQuests.Variable(Key(1101), 1) == 0 && scriptQuests.Variable(Key(1101), 2) == 1,
                    "Shared/fallback broadcast commands or explicit/postfix queries diverged.");
            }
            Console.WriteLine("OPENNV_RADIO_BROADCAST_CONTRACT_PASS sourceDefaults=true perReference=true independentReceiverVoiceEnable=true " +
                "sharedAndFallback=true selfAndExplicit=true postfixQuery=true cold=true invalidAtomic=true broadcastTimelineUnowned=true");
        }
        finally { foreach (var path in Directory.EnumerateFiles(directory)) File.Delete(path); Directory.Delete(directory); }
    }

    private static byte[] Fixture()
    {
        var header = new byte[20]; BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12), 2); header[16] = 1;
        var first = new byte[24]; BinaryPrimitives.WriteUInt32LittleEndian(first, 1);
        var second = new byte[24]; BinaryPrimitives.WriteUInt32LittleEndian(second, 2);
        var radio = new byte[16]; BinaryPrimitives.WriteUInt32LittleEndian(radio.AsSpan(4), 1);
        return Join(Record("TES4", 0, 0, Field("HEDR", new byte[12])), Record("TACT", 10, 0x40020000), Record("TACT", 11, 0x20000),
            Record("DOOR", 20, 0), Record("SOUN", 500, 0), Record("CELL", 800, 0, Field("DATA", [1])),
            Group(800, Reference(900, 10, "RadioA", radio), Reference(901, 10, "RadioB", radio),
                Reference(902, 11, "RadioC", radio), Reference(903, 20, "DoorA", null)),
            Record("SCPT", 1100, 0, Field("SCRO", BitConverter.GetBytes(900u)), Field("SCRO", BitConverter.GetBytes(901u)),
                Field("SCHR", header), Field("SLSD", first), Field("SCVR", Text("observed")),
                Field("SLSD", second), Field("SCVR", Text("postfix")),
                Field("SCTX", Text("short observed\nshort postfix\nbegin GameMode\nRadioA.SetBroadcastState 0\n" +
                    "set observed to RadioA.GetBroadcastState\nset postfix to (RadioB).GetBroadcastState\nend"))),
            Record("QUST", 1101, 0, Field("EDID", Text("BroadcastQuest")), Field("DATA", [1, 0]), Field("SCRI", BitConverter.GetBytes(1100u)),
                Field("SCRO", BitConverter.GetBytes(900u)), Field("SCRO", BitConverter.GetBytes(901u))));
    }
    private static FalloutFormKey Key(uint id) => new("Broadcast.esm", id);
    private static void Require(bool value, string message) { if (!value) throw new InvalidDataException(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidDataException("Invalid broadcast contract was admitted.");
    }
    private static byte[] Reference(uint id, uint source, string name, byte[]? radio) => Record("REFR", id, 0,
        Field("EDID", Text(name)), Field("NAME", BitConverter.GetBytes(source)), Field("DATA", new byte[24]),
        radio is null ? [] : Field("XRDO", radio));
    private static byte[] Text(string value) => Encoding.ASCII.GetBytes(value + '\0');
    private static byte[] Join(params byte[][] fields) => fields.SelectMany(value => value).ToArray();
    private static byte[] Group(uint label, params byte[][] fields)
    {
        var payload = Join(fields); var result = new byte[24 + payload.Length]; Encoding.ASCII.GetBytes("GRUP").CopyTo(result, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), (uint)result.Length); BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(8), label);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(12), 6); payload.CopyTo(result, 24); return result;
    }
    private static byte[] Field(string name, byte[] bytes)
    {
        var result = new byte[6 + bytes.Length]; Encoding.ASCII.GetBytes(name).CopyTo(result, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(4), checked((ushort)bytes.Length)); bytes.CopyTo(result, 6); return result;
    }
    private static byte[] Record(string name, uint id, uint flags, params byte[][] fields)
    {
        var payload = Join(fields); var result = new byte[24 + payload.Length]; Encoding.ASCII.GetBytes(name).CopyTo(result, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), (uint)payload.Length); BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(8), flags);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(12), id); payload.CopyTo(result, 24); return result;
    }
}
