using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static class EncounterZoneContracts
{
    internal static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-encounters-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var acbs = new byte[24]; BinaryPrimitives.WriteUInt16LittleEndian(acbs.AsSpan(22), 2);
            File.WriteAllBytes(Path.Combine(directory, "Zones.esm"), Join(
                Record("TES4", 0, Field("HEDR", Join(BitConverter.GetBytes(1.34f), new byte[8]))),
                Record("GMST", 0x800, Field("EDID", Text("fLevelScalingMult")), Field("DATA", BitConverter.GetBytes(.75f))),
                Record("GMST", 0x801, Field("EDID", Text("fLeveledActorMultBoss")), Field("DATA", BitConverter.GetBytes(2f))),
                Zone(0x810, 10, 0), Zone(0x811, 10, 2), Zone(0x812, 0, 1),
                Record("ECZN", 0x813, Field("DATA", new byte[7])), Zone(0x814, 1, 4),
                Record("CREA", 0x820, Field("ACBS", acbs), Field("TPLT", BitConverter.GetBytes(0x830u))),
                Record("CREA", 0x821, Field("ACBS", new byte[24])), Record("CREA", 0x822, Field("ACBS", new byte[24])),
                Record("LVLC", 0x830, Field("LVLD", [0]), Field("LVLF", [0]), Entry(1, 0x821), Entry(15, 0x822)),
                Cell(0x840, 0x810, Reference(0x850), Reference(0x851, Field("XLCM", BitConverter.GetBytes(3)))),
                Cell(0x841, 0x810, Reference(0x852)),
                Cell(0x842, 0x811, Reference(0x853), Reference(0x854, Field("XEZN", BitConverter.GetBytes(0x810u)))),
                Record("WRLD", 0x860, Field("XEZN", BitConverter.GetBytes(0x811u))),
                Group(0x860, 1, Join(
                    WithFlags(Record("CELL", 0x861, Field("DATA", [0])), 0x400),
                    Group(0x861, 6, Join(Reference(0x855), Reference(0x856))),
                    Record("CELL", 0x862, Field("DATA", [0]), Field("XCLC", new byte[8]), Field("XEZN", BitConverter.GetBytes(0x810u))),
                    Record("CELL", 0x863, Field("DATA", [0]), Field("XCLC", Join(BitConverter.GetBytes(1), new byte[4])))))));
            using var records = FalloutPluginStack.Load(directory, ["Zones.esm"]);
            FalloutFormKey Key(uint id) => new("Zones.esm", id);
            using var world = new FalloutReferenceWorld(records);
            Check(world.EnterEncounterCell(Key(0x840), 1) == 10, "Zone minimum was not applied.");
            Check(world.EnterEncounterCell(Key(0x841), 50) == 10, "Connected cells recalculated an entered zone.");
            Check(world.InitializeActorTemplates(Key(0x850), 50).Capture().Choices.Single().Actor == Key(0x821), "Template ignored the retained zone level.");
            Check(world.InitializeActorTemplates(Key(0x851), 1).Capture().Choices.Single().Actor == Key(0x822), "Boss modifier did not use the source multiplier.");
            Check(world.InitializeActorTemplates(Key(0x853), 1).Level == 1, "Match-below-minimum was ignored.");
            Check(world.InitializeActorTemplates(Key(0x854), 1).Level == 10, "Reference zone did not override CELL zone.");
            Check(world.InitializeActorTemplates(Key(0x855), 1).Level == 10, "Persistent exterior actor ignored its spatial CELL zone.");
            world.SetPlacement(Key(0x856), new(Key(0x861), [5000, 0, 0], [0, 0, 0]));
            Check(world.InitializeActorTemplates(Key(0x856), 1).Level == 1, "Moved exterior actor ignored spatial residency or WRLD inheritance.");
            Check(FalloutEncounterZone.Read(records.GetEffective(Key(0x812))).InitialLevel(20, .75f) == 20, "Automatic zone level changed the player level.");
            Check(FalloutEncounterZone.Read(records.GetEffective(Key(0x810))).InitialLevel(20, .75f) == 15, "Zone scaling setting was ignored.");
            Reject(() => FalloutEncounterZone.Read(records.GetEffective(Key(0x813))));
            Reject(() => FalloutEncounterZone.Read(records.GetEffective(Key(0x814))));
            var saved = JsonSerializer.Serialize(world.CaptureEncounterZones());
            using var cold = new FalloutReferenceWorld(records);
            cold.RestoreEncounterZones(JsonSerializer.Deserialize<FalloutEncounterZoneSnapshot[]>(saved));
            cold.Restore(JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))!);
            Check(cold.EnterEncounterCell(Key(0x841), 50) == 10 && JsonSerializer.Serialize(cold.CaptureEncounterZones()) == saved,
                "Cold continuation recalculated encounter levels.");
            Check(JsonSerializer.Serialize(cold.Get(Key(0x851)).Templates!.Capture()) == JsonSerializer.Serialize(world.Get(Key(0x851)).Templates!.Capture()),
                "Cold continuation changed list difficulty or actor identity.");
            using var invalid = new FalloutReferenceWorld(records);
            var first = world.CaptureEncounterZones()[0];
            Reject(() => invalid.RestoreEncounterZones([first, first]));
            Check(invalid.CaptureEncounterZones().Count == 0, "Failed restore published a partial zone state.");
            Reject(() => invalid.RestoreEncounterZones([first with { SourceSha256 = new string('0', 64) }]));
            Reject(() => invalid.RestoreEncounterZones([first with { Level = 99 }]));
            Console.WriteLine("OPENNV_ENCOUNTER_ZONE_PASS minimum=true matchBelow=true scaling=true referenceOverride=true sharedCells=true persistentExterior=true movedExterior=true worldInheritance=true boss=true cold=true malformedRejected=true atomicRestore=true");
        }
        finally { Directory.Delete(directory, true); }
    }

    private static byte[] Zone(uint id, byte minimum, byte flags) => Record("ECZN", id, Field("DATA", [0, 0, 0, 0, 0, minimum, flags, 0]));
    private static byte[] Reference(uint id, params byte[][] extra) => Record("ACRE", id,
        Field("NAME", BitConverter.GetBytes(0x820u)), Field("DATA", new byte[24]), Join(extra));
    private static byte[] Cell(uint id, uint zone, params byte[][] children)
    {
        return Join(Record("CELL", id, Field("DATA", [1]), Field("XEZN", BitConverter.GetBytes(zone))), Group(id, 6, Join(children)));
    }
    private static byte[] Group(uint id, int type, byte[] data)
    {
        var group = new byte[24 + data.Length];
        Encoding.ASCII.GetBytes("GRUP").CopyTo(group, 0); BitConverter.GetBytes(group.Length).CopyTo(group, 4);
        BitConverter.GetBytes(id).CopyTo(group, 8); BitConverter.GetBytes(type).CopyTo(group, 12); data.CopyTo(group, 24);
        return group;
    }
    private static byte[] WithFlags(byte[] record, uint flags) { BitConverter.GetBytes(flags).CopyTo(record, 8); return record; }
    private static byte[] Entry(ushort level, uint actor) => Field("LVLO", Join(BitConverter.GetBytes(level), new byte[2], BitConverter.GetBytes(actor), BitConverter.GetBytes(1)));
    private static byte[] Text(string text) => Encoding.ASCII.GetBytes(text + '\0');
    private static byte[] Join(params byte[][] items) => items.SelectMany(value => value).ToArray();
    private static byte[] Field(string signature, byte[] data) => Join(Encoding.ASCII.GetBytes(signature), BitConverter.GetBytes(checked((ushort)data.Length)), data);
    private static byte[] Record(string signature, uint id, params byte[][] fields)
    {
        var data = Join(fields); var header = new byte[24]; Encoding.ASCII.GetBytes(signature).CopyTo(header, 0);
        BitConverter.GetBytes(data.Length).CopyTo(header, 4); BitConverter.GetBytes(id).CopyTo(header, 12); return Join(header, data);
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidDataException("Invalid encounter data was accepted.");
    }
}
