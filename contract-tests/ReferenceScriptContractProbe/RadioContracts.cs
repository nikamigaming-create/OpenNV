using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class RadioContracts
{
    internal static void Run()
    {
        RadioBroadcastContracts.Run();
        var directory = Path.Combine(Path.GetTempPath(), "opennv-radio-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var header = new byte[12]; BinaryPrimitives.WriteSingleLittleEndian(header, 1.34f);
            var bytes = Join(Record("TES4", 0, 0, Field("HEDR", header)),
                Record("TACT", 10, 0x40020000), Record("TACT", 11, 0x10020000), Record("DOOR", 20, 0), Record("SOUN", 500, 0),
                Cell(800, true), Group(800, 6, Reference(900, 10, 0, Radio(0, 100, 901)), Reference(901, 20, 0, Field("DATA", Position(50))),
                    Reference(903, 10, 0, Radio(1)), Reference(904, 10, 0, Radio(3)), Reference(905, 10, 0, Field("XRDO", new byte[15])),
                    Reference(907, 10, 0, Radio(4)), Reference(908, 11, 0, Radio(1)), Reference(909, 10, 0x800, Radio(1)),
                    Door(910, 911), Door(912, 913)),
                Cell(801, true), Group(801, 6, Door(911, 910)), Cell(802, true),
                Record("QUST", 1000, 0, Field("EDID", Encoding.ASCII.GetBytes("ReceiverQuest\0"))),
                Group(700, 1, Cell(804, false), Group(804, 6, Reference(902, 10, 0, Radio(2)),
                    Reference(906, 10, 0, Radio(4), Field("DATA", Position(4200))), Door(913, 912)), Cell(805, false)));
            var path = Path.Combine(directory, "Radio.esm"); File.WriteAllBytes(path, bytes);
            using (var records = FalloutPluginStack.Load(directory, ["Radio.esm"]))
            using (var world = new FalloutReferenceWorld(records))
            {
                var queue = new FalloutHudNotifications(); var signals = 0;
                var radio = new FalloutRadioStations(records, world, queue) { SignalDiscovered = () => signals++ };
                var player = new FalloutReferencePlacement(Key(800), [0, 0, 0], [0, 0, 0]);
                radio.Refresh(player, force: true);
                bool Available(uint id) => radio.Reception.Single(value => value.Station.Reference == Key(id)).Available;
                Require(Available(900) && Available(902) && Available(903) && Available(904) && Available(907) && !Available(909),
                    "Station refresh lost radius/linked-world/linked-interior/current-cell or disabled semantics.");
                Require(radio.Available.All(value => value.Reference != Key(908)) && radio.SourceErrors.ContainsKey(Key(905)) && signals == 0,
                    "Non-Pipboy or malformed transmitter entered discovery, or effects were missing.");
                var ordinal = queue.Capture().LastOrdinal;
                radio.Refresh(player, force: true);
                Require(signals == 0 && queue.Capture().LastOrdinal == ordinal && radio.ForcedUpdates == 2, "Forced refresh replayed discovery effects.");
                radio.Refresh(player with { Position = [150, 0, 0] });
                Require(!Available(900), "Exact radius boundary was admitted or the position reference was ignored.");
                radio.Refresh(player with { Cell = Key(801) });
                Require(Available(904) && Available(902) && !Available(907) && radio.Reception.Single(value => value.Station.Reference == Key(900)).Error is not null,
                    "Portal reception invented radius distance or confused linked and current cells.");
                radio.Refresh(player with { Cell = Key(802) });
                Require(!Available(904) && !Available(902), "Disconnected interior received a linked signal.");
                radio.Refresh(player with { Cell = Key(805), Position = [4201, 0, 0] });
                Require(Available(906) && !Available(904), "Exterior current-cell reception used persistent parent identity or leaked an interior signal.");
                world.SetEnabled(Key(909), true);
                radio.Refresh(player);
                Require(!Available(909), "Queued enable mutated applied radio availability early.");
                world.AdvanceEnableChanges(0, new(1, 1), _ => false); radio.Refresh(player);
                Require(Available(909), "Applied enable did not refresh radio reception.");
                var retained = JsonSerializer.Deserialize<FalloutRadioStationsSnapshot>(JsonSerializer.Serialize(radio.Capture()))!;
                var cold = new FalloutRadioStations(records, world, new()) { SignalDiscovered = () => throw new InvalidDataException("Cold discovery replay.") };
                cold.Restore(retained); cold.Refresh(player);
                Require(cold.Capture().Discovered.SequenceEqual(retained.Discovered), "Cold refresh lost discovery history.");
                var fresh = new FalloutRadioStations(records, world, new());
                Reject(() => fresh.Restore(new([Key(900), Key(900)]))); Reject(() => fresh.Restore(new([Key(905)])));
                Require(fresh.Capture().Discovered.Count == 0, "Failed radio restore partially mutated discovery.");
                Reject(() => FalloutRadioStation.Read(records, records.GetEffective(Key(905))));
                Declaration();
                Receiver(records, world, radio);
            }
            File.WriteAllBytes(Path.Combine(directory, "Patch.esp"), Join(Record("TES4", 0, 0, Field("HEDR", header), Field("MAST", Encoding.ASCII.GetBytes("Radio.esm\0")), Field("DATA", new byte[8])),
                Group(800, 6, Reference(900, 10, 0, Radio(1)))));
            using var patched = FalloutPluginStack.Load(directory, ["Radio.esm", "Patch.esp"]);
            Require(FalloutRadioStation.Read(patched, patched.GetEffective(Key(900))).Range == FalloutRadioRange.Everywhere,
                "Radio reader ignored the winning override or its master-relative base.");
            Console.WriteLine("OPENNV_RADIO_CONTRACT_PASS winning-master range anchor enable non-pipboy discovery once-only cold malformed retained-portal-divergence hud-declarations receiver-source-off lease-retirement independent-voices last-station-cold active-save-refused unbound-tuning-atomic");
        }
        finally { Directory.Delete(directory, true); }
    }

    private static void Receiver(FalloutPluginStack records, FalloutReferenceWorld world, FalloutRadioStations radio)
    {
        var before = JsonSerializer.Serialize(world.PipBoyRadio.Capture());
        Reject(() => radio.SelectPipBoy(Key(902)));
        Require(before == JsonSerializer.Serialize(world.PipBoyRadio.Capture()) && world.PipBoyRadio.CurrentStation is null,
            "Unowned tuning published a station without a broadcast lease.");
        var stops = 0;
        var unrelatedStops = 0;
        using var unrelated = records.SoundVoices.Register(Key(500), null, "synthetic-world-radio", () => true, () => ++unrelatedStops);
        using var playback = world.PipBoyRadio.Bind(_ => new RadioLease(() => ++stops));
        radio.SelectPipBoy(Key(902));
        Require(world.PipBoyRadio.CurrentStation == Key(902), "Prepared receiver did not own the selected source station.");
        Reject(() => world.PipBoyRadio.Capture());
        var quest = records.GetEffective(Key(1000));
        var scripts = new FalloutReferenceScripts(records, world, new(records), new((_, _) => false,
            _ => throw new InvalidDataException("Source radio off escaped its receiver owner.")));
        scripts.ExecuteStage(quest, quest.ReadSubrecords().ToArray(), "PipBoyRadioOff");
        Require(stops == 1 && world.PipBoyRadio.CurrentStation is null && world.PipBoyRadio.LastStation == Key(902) &&
            world.PipBoyRadio.OffRequests == 1 && records.SoundVoices.ActiveVoices == 1 && unrelatedStops == 0,
            "Source radio off lost the last station, repeated retirement or touched unrelated sound voices.");
        scripts.ExecuteStage(quest, quest.ReadSubrecords().ToArray(), "PipBoyRadioOff");
        Require(stops == 1 && world.PipBoyRadio.OffRequests == 2, "Repeated radio off repeated playback retirement.");
        var saved = JsonSerializer.Deserialize<FalloutPipBoyRadioSnapshot>(JsonSerializer.Serialize(world.PipBoyRadio.Capture()))!;
        using var cold = new FalloutReferenceWorld(records);
        cold.PipBoyRadio.Restore(saved);
        Require(cold.PipBoyRadio.CurrentStation is null && cold.PipBoyRadio.LastStation == Key(902) &&
            JsonSerializer.Serialize(cold.PipBoyRadio.Capture()) == JsonSerializer.Serialize(saved),
            "Cold receiver lost its last station or replayed radio audio.");
        using var changed = new FalloutReferenceWorld(records);
        Reject(() => changed.PipBoyRadio.Restore(saved with { SourceSha256 = new string('0', 64) }));
        Require(changed.PipBoyRadio.LastStation is null, "Source mismatch changed the receiver before validation.");
    }

    private sealed class RadioLease(Action stop) : IDisposable
    {
        private Action? _stop = stop;
        public void Dispose() { var callback = _stop; _stop = null; callback?.Invoke(); }
    }

    private static void Declaration()
    {
        RadioHudDeclarationContracts.Run();
        var code = new byte[180]; code[20] = 0x68; BitConverter.GetBytes(1u).CopyTo(code, 21);
        code[60] = 0xb9; BitConverter.GetBytes(2u).CopyTo(code, 61); code[65] = 0xe8;
        code[80] = 0xd9; code[81] = 0x05; BitConverter.GetBytes(3u).CopyTo(code, 82);
        code[90] = 0x68; BitConverter.GetBytes(4u).CopyTo(code, 91);
        string? Literal(uint key) => key == 1 ? "UISourceSignal" : key == 4 ? "source/icon.dds" : null;
        var declaration = FalloutExecutableStringTable.ReadRadioHudDeclaration(code, Literal, new Dictionary<uint, string> { [2] = "sRadioStationDiscovered" }, _ => 3.25f);
        Require(declaration == new FalloutRadioHudDeclaration("source/icon.dds", 3.25f, "UISourceSignal"), "Radio HUD lost owned declarations.");
        Reject(() => FalloutExecutableStringTable.ReadRadioHudDeclaration(code, Literal, new Dictionary<uint, string> { [2] = "sRadioStationDiscovered" }, _ => float.NaN));
    }
    private static FalloutFormKey Key(uint id) => new("Radio.esm", id);
    private static void Require(bool value, string message) { if (!value) throw new InvalidDataException(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidDataException("Invalid radio contract was admitted.");
    }
    private static byte[] Radio(uint range, float radius = 0, uint position = 0)
    {
        var data = new byte[16]; BinaryPrimitives.WriteSingleLittleEndian(data, radius);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), range); BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(12), position);
        return Field("XRDO", data);
    }
    private static byte[] Position(float x) { var data = new byte[24]; BinaryPrimitives.WriteSingleLittleEndian(data, x); return data; }
    private static byte[] Door(uint id, uint target) { var data = new byte[32]; BinaryPrimitives.WriteUInt32LittleEndian(data, target); return Reference(id, 20, 0, Field("XTEL", data)); }
    private static byte[] Reference(uint id, uint source, uint flags, params byte[][] fields) => Record("REFR", id, flags,
        [Field("NAME", BitConverter.GetBytes(source)), fields.SingleOrDefault(value => Encoding.ASCII.GetString(value, 0, 4) == "DATA") ?? Field("DATA", new byte[24]),
            .. fields.Where(value => Encoding.ASCII.GetString(value, 0, 4) != "DATA")]);
    private static byte[] Cell(uint id, bool interior) => Record("CELL", id, 0, [Field("DATA", [interior ? (byte)1 : (byte)0]),
        Field("EDID", Encoding.ASCII.GetBytes("Cell" + id + "\0")), .. (interior ? Array.Empty<byte[]>() : new[] { Field("XCLC", new byte[12]) })]);
    private static byte[] Join(params byte[][] fields) => fields.SelectMany(value => value).ToArray();
    private static byte[] Group(uint label, int type, params byte[][] fields)
    {
        var payload = Join(fields); var bytes = new byte[24 + payload.Length]; Encoding.ASCII.GetBytes("GRUP").CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)bytes.Length); BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), label);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12), type); payload.CopyTo(bytes, 24); return bytes;
    }
    private static byte[] Field(string signature, byte[] data)
    {
        var bytes = new byte[6 + data.Length]; Encoding.ASCII.GetBytes(signature).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), (ushort)data.Length); data.CopyTo(bytes, 6); return bytes;
    }
    private static byte[] Record(string signature, uint id, uint flags, params byte[][] fields)
    {
        var payload = Join(fields); var bytes = new byte[24 + payload.Length]; Encoding.ASCII.GetBytes(signature).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)payload.Length); BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), flags);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), id); payload.CopyTo(bytes, 24); return bytes;
    }
}
