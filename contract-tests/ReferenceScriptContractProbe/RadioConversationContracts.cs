using System.Buffers.Binary;
using System.Text;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static class RadioConversationContracts
{
    internal static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-radio-links-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var range = new byte[16]; range[4] = 1;
            File.WriteAllBytes(Path.Combine(directory, "Radio.esm"), Join(Record("TES4", 0, 0),
                Record("VTYP", 20, 0, Field("EDID", Text("Voice"))), Record("RACE", 40, 0),
                Record("NPC_", 10, 0, Field("ACBS", new byte[24]), Field("VTCK", BitConverter.GetBytes(20u)), Field("RNAM", BitConverter.GetBytes(40u))),
                Record("TACT", 11, 0x20000, Field("VNAM", BitConverter.GetBytes(20u))),
                Record("QUST", 1101, 0, Field("EDID", Text("RadioQuest")), Field("DATA", [1, 20])),
                Record("CELL", 800, 0, Field("DATA", [1])),
                Group(800, 6, Record("REFR", 900, 0, Field("NAME", BitConverter.GetBytes(11u)), Field("DATA", new byte[24]), Field("XRDO", range))),
                Record("DIAL", 200, 0, Field("EDID", Text("RadioHello")), Field("DATA", [7, 0])),
                Group(200, 7, Info(301, 0, 8), Info(302, 1, 9)),
                Record("DIAL", 201, 0, Field("EDID", Text("OrdinaryTopic")), Field("DATA", [1, 0]))));
            using var records = FalloutPluginStack.Load(directory, ["Radio.esm"]);
            using var world = new FalloutReferenceWorld(records);
            var quests = new FalloutQuestState(records); var phase = 0;
            var radio = new FalloutRadioConversation(records, world, quests,
                (_, condition) => throw new InvalidDataException($"Unexpected condition {condition.Function}."),
                _ => phase, new HashSet<FalloutFormKey>());
            Reject(() => radio.Start(Key(900), Key(201)));
            Require(radio.Station is null && !radio.Active, "Invalid radio topic changed the transmitter.");
            radio.Start(Key(900));
            Require(radio.Info?.Record.FormKey == Key(301) && radio.VoiceIdentity().Actor == Key(10),
                "Radio lost station predicate identity, default topic or remote voice actor.");
            Reject(() => radio.Start(Key(900)));
            Require(radio.Info?.Record.FormKey == Key(301) && radio.CompletedLines == 0, "Rejected interruption restarted a source line.");
            phase = 1; radio.CompleteLine();
            Require(radio.Info?.Record.FormKey == Key(302) && radio.CompletedLines == 1, "Radio reselected against stale source state.");
            radio.CompleteLine();
            Require(!radio.Active && radio.CompletedLines == 2 && !world.GetBroadcastState(Key(900)), "Goodbye changed mode or lost completion.");
            Reject(() => radio.CompleteLine());
            phase = 0; radio.Start(Key(900)); phase = 2; radio.CompleteLine();
            Require(!radio.Active && radio.CompletedLines == 3 && !world.GetBroadcastState(Key(900)),
                "An exhausted, fully evaluated link set faulted instead of ending its original radio request.");
            Reject(() => radio.CompleteLine());
            world.SetBroadcastState(Key(900), 1);
            Reject(() => radio.Start(Key(900)));
            Require(!radio.Active && world.GetBroadcastState(Key(900)), "Missing continuous owner changed mode.");
            Console.WriteLine("OPENNV_RADIO_CONVERSATION_PASS fullReader=true stationPredicates=true remoteVoice=true defaultTopic=true " +
                "sourceLinks=true freshConditions=true goodbye=true modePreserved=true interruptionAtomic=true continuousRefused=true");
        }
        finally { foreach (var path in Directory.EnumerateFiles(directory)) File.Delete(path); Directory.Delete(directory); }
    }
    private static byte[] Info(uint id, int phase, byte flags)
    {
        var response = new byte[16]; response[12] = 1;
        var identity = new byte[28]; BinaryPrimitives.WriteSingleLittleEndian(identity.AsSpan(4), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(identity.AsSpan(8), 72); BinaryPrimitives.WriteUInt32LittleEndian(identity.AsSpan(12), 11);
        var condition = new byte[28]; BinaryPrimitives.WriteSingleLittleEndian(condition.AsSpan(4), phase);
        BinaryPrimitives.WriteUInt16LittleEndian(condition.AsSpan(8), 58); BinaryPrimitives.WriteUInt32LittleEndian(condition.AsSpan(12), 1101);
        return Record("INFO", id, 0, Field("DATA", [7, 0, flags, 0]), Field("QSTI", BitConverter.GetBytes(1101u)),
            Field("TRDT", response), Field("NAM1", Text("Announcement")), Field("CTDA", identity), Field("CTDA", condition),
            Field("TCLT", BitConverter.GetBytes(200u)), Field("ANAM", BitConverter.GetBytes(10u)));
    }
    private static FalloutFormKey Key(uint id) => new("Radio.esm", id);
    private static void Require(bool value, string message) { if (!value) throw new InvalidDataException(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception error) when (error is InvalidDataException or NotSupportedException or InvalidOperationException) { return; }
        throw new InvalidDataException("Missing radio owner was admitted.");
    }
    private static byte[] Text(string value) => Encoding.ASCII.GetBytes(value + '\0');
    private static byte[] Join(params byte[][] fields) => fields.SelectMany(value => value).ToArray();
    private static byte[] Group(uint label, int type, params byte[][] fields)
    {
        var payload = Join(fields); var result = new byte[24 + payload.Length]; Encoding.ASCII.GetBytes("GRUP").CopyTo(result, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), (uint)result.Length); BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(8), label);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(12), type); payload.CopyTo(result, 24); return result;
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
