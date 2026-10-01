using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class FaceGeometryContracts
{
    internal static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-face-match-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "Faces.esm");
            File.WriteAllBytes(path, Join(Header(), Race(0x400), Race(0x410), Race(0x450), Race(0x470),
                Npc(7, 0x410, "", 5, Floats(50, 0, 2, 4), Floats(30, 2)),
                Npc(0x100, 0x400, "Target", 0, Floats(50, 20, 8, 10), Floats(30, 10), 0x500),
                Npc(0x101, 0x410, "a", 5, Floats(50, 100, 100, 100), Floats(30, 100)),
                Npc(0x102, 0x410, "A", 5, Floats(50, 0, 2, 4), Floats(30, 2)),
                Npc(0x103, 0x410, "!", 1, Floats(50, 200, 200, 200), Floats(30, 200)),
                Npc(0x104, 0x410, "Wrong sex", 4, Floats(50, 300), Floats(30, 300)),
                Npc(0x105, 0x400, "Female target", 1, Floats(50, 20, 8, 10), Floats(30, 10)),
                Npc(0x106, 0x450, "b", 0, Floats(50, 1), Floats(30, 1)),
                Npc(0x107, 0x450, "A", 0, Floats(50, 2), Floats(30, 2)),
                Npc(0x108, 0x470, "same", 4, Floats(50, 1), Floats(30, 1)),
                Npc(0x109, 0x470, "same", 4, Floats(50, 2), Floats(30, 2)),
                Record("MISC", 0x200), Script(), Record("CELL", 0x800, Field("DATA", [1])),
                Group(0x800, Join(Reference(0x900, 0x100), Reference(0x902, 0x105), Reference(0x903, 0x200))),
                Record("CELL", 0x801, Field("DATA", [1])), Group(0x801, Reference(0x901, 0x100))));
            var original = File.ReadAllBytes(path);
            using var records = FalloutPluginStack.Load(directory, ["Faces.esm"]);
            var controls = FalloutFaceGeometryControls.Read(Controls());
            using var world = new FalloutReferenceWorld(records, faceControls: controls);
            world.LoadCell(FalloutCellSceneReader.Read(records, Key(0x800)));
            FalloutActorAppearanceState player = new(true, Key(0x410), null, null,
                new(Key(7), Floats(50, 4, 6, 8), Floats(30, 6), Floats(50, 400)));
            world.BindPlayerAppearance(() => player);
            var presets = FalloutNpcFacePresets.Resolve(records, Key(7), Key(0x410), true);
            Require(presets.Select(row => row.FormKey).SequenceEqual(new[] { Key(0x102), Key(0x101) }),
                "Preset selection lost marked preference, sex, player exclusion or source case ordering.");
            var fallback = FalloutNpcFacePresets.Resolve(records, Key(7), Key(0x450), false);
            Require(fallback.Count == 2 && FalloutNpcFacePresets.First(fallback).FormKey == Key(0x107), "Unmarked preset fallback differs.");
            Require(FalloutNpcFacePresets.First(FalloutNpcFacePresets.Resolve(records, Key(7), Key(0x470), false)).FormKey == Key(0x109),
                "Equal first names lost source small-partition tie ordering.");
            Reject(() => FalloutNpcFacePresets.First([]));
            var scripts = new FalloutReferenceScripts(records, world, new FalloutQuestState(records),
                new((_, _) => false, _ => throw new InvalidDataException("Face matching invented a presentation effect.")));
            var result = scripts.Dispatch(Key(0x900), "GameMode");
            Require(result.Error is null && world.Get(Key(0x900)).Read(1) == 1 && world.Get(Key(0x900)).Read(2) == 1,
                "Face command lost typed dispatch or source statement order: " + result.Error);
            var face = world.ActorFace(Key(0x900));
            Require(face.SymmetricGeometry.SequenceEqual(Floats(50, 19, 9, 7)) && face.AsymmetricGeometry.SequenceEqual(Floats(30, 9)),
                "Matching must add source-minus-first-preset, truncate the percent, preserve age and subtract male race geometry.");
            Require(face.SymmetricTexture.SequenceEqual(Floats(50, 71)), "Matching changed target texture coefficients.");
            Require(!world.IsResident(Key(0x901)) && world.ActorFace(Key(0x901)).SymmetricGeometry.SequenceEqual(face.SymmetricGeometry) &&
                world.CaptureActorOverrides().Single().Target == Key(0x100), "Face geometry lost base or unloaded scope.");
            world.MatchFaceGeometry(Key(0x902), records.RuntimeFormKey(0x14), 50);
            Require(world.ActorFace(Key(0x902)).SymmetricGeometry.SequenceEqual(face.SymmetricGeometry),
                "Female targets did not use the command's male-race coefficient conversion.");
            var saved = JsonSerializer.Deserialize<FalloutActorOverrides[]>(JsonSerializer.Serialize(world.CaptureActorOverrides()))!;
            var exposed = world.CaptureActorOverrides();
            exposed[0].FaceGeometry!.SymmetricGeometry[0] ^= 0xff;
            Require(world.ActorFace(Key(0x900)).SymmetricGeometry.SequenceEqual(face.SymmetricGeometry),
                "A captured face array changed authoritative state.");
            using var cold = new FalloutReferenceWorld(records, faceControls: controls);
            cold.Restore(world.Capture()); cold.RestoreActorOverrides(saved);
            saved[0].FaceGeometry!.AsymmetricGeometry[0] ^= 0xff;
            Require(cold.ActorFace(Key(0x900)).AsymmetricGeometry.SequenceEqual(face.AsymmetricGeometry),
                "An admitted save array remained mutable through its caller.");
            saved[0].FaceGeometry!.AsymmetricGeometry[0] ^= 0xff;
            Require(cold.ActorFace(Key(0x901)).SymmetricGeometry.SequenceEqual(face.SymmetricGeometry) &&
                cold.ActorFace(Key(0x900)).SymmetricTexture.SequenceEqual(face.SymmetricTexture), "Cold face state differs.");
            var stored = saved[0].FaceGeometry!;
            foreach (var invalid in new[] { stored with { ModelSha256 = new string('0', 64) }, stored with { RaceSha256 = new string('0', 64) },
                stored with { ControlsSha256 = new string('0', 64) }, stored with { SymmetricGeometry = [0] },
                stored with { AsymmetricGeometry = Floats(30, float.NaN) }, stored with { ModelOwner = Key(0x102) } })
                Reject(() => cold.RestoreActorOverrides([saved[0] with { FaceGeometry = invalid }]));
            Reject(() => cold.RestoreActorOverrides([saved[0], saved[0]]));
            Require(cold.ActorFace(Key(0x900)).SymmetricGeometry.SequenceEqual(face.SymmetricGeometry), "Rejected save partially committed a face.");
            var revision = cold.AppearanceRevision; cold.RestoreActorOverrides([]);
            Require(cold.AppearanceRevision > revision && cold.ActorAppearanceOverride(Key(0x900)) is null &&
                cold.ActorFace(Key(0x900)).SymmetricGeometry.SequenceEqual(Floats(50, 20, 8, 10)), "Face removal lost source restoration/invalidation.");
            using var zero = new FalloutReferenceWorld(records, faceControls: controls);
            zero.BindPlayerAppearance(() => player);
            zero.MatchFaceGeometry(Key(0x902), records.RuntimeFormKey(0x14), 0);
            Require(zero.ActorFace(Key(0x902)).SymmetricGeometry.SequenceEqual(Floats(50, 19, 5, 5)), "Zero percent skipped native race conversion.");
            using var negative = new FalloutReferenceWorld(records, faceControls: controls);
            negative.BindPlayerAppearance(() => player);
            negative.MatchFaceGeometry(Key(0x902), records.RuntimeFormKey(0x14), -100);
            Require(negative.ActorFace(Key(0x902)).SymmetricGeometry.SequenceEqual(Floats(50, 19, -3, 1)), "Signed native percentage was clamped.");
            using var npcSource = new FalloutReferenceWorld(records, faceControls: controls);
            npcSource.MatchFaceGeometry(Key(0x900), Key(0x902), 100);
            Require(npcSource.ActorFace(Key(0x900)).SymmetricGeometry.SequenceEqual(Floats(50, 19, 5, 5)) &&
                npcSource.ActorFace(Key(0x900)).AsymmetricGeometry.SequenceEqual(Floats(30, 7)),
                "NPC source matching depended on the player appearance provider.");
            Reject(() => world.MatchFaceGeometry(records.RuntimeFormKey(0x14), Key(0x900), 50));
            Reject(() => world.MatchFaceGeometry(Key(0x903), records.RuntimeFormKey(0x14), 50));
            player = player with { FaceGen = player.FaceGen! with { SymmetricGeometry = Floats(50, float.PositiveInfinity) } };
            var before = world.Get(Key(0x900)).Read(1); var after = world.Get(Key(0x900)).Read(2);
            var stateBefore = JsonSerializer.Serialize(world.CaptureActorOverrides());
            result = scripts.Dispatch(Key(0x900), "GameMode");
            Require(result.Error is not null && world.Get(Key(0x900)).Read(1) == before + 1 && world.Get(Key(0x900)).Read(2) == after &&
                stateBefore == JsonSerializer.Serialize(world.CaptureActorOverrides()), "Failed matching lost its atomic state/prefix boundary.");
            _ = scripts.Dispatch(Key(0x900), "GameMode");
            Require(world.Get(Key(0x900)).Read(1) == before + 1, "Latched face fault replayed an additive prefix.");
            Require(File.ReadAllBytes(path).SequenceEqual(original), "Face matching wrote source records.");
            PresetSorting();
            Console.WriteLine("OPENNV_FACE_GEOMETRY_CONTRACT_PASS preset-order fallback player-source signed-percent displacement age texture base-scope unloaded cold source-drift atomic prefix");
        }
        finally { Directory.Delete(directory, true); }
    }

    private static void PresetSorting()
    {
        foreach (var (names, expected) in new[]
        {
            (new[] { "same", "same" }, new[] { 1, 0 }),
            (Enumerable.Repeat("same", 8).ToArray(), new[] { 1, 2, 3, 4, 5, 6, 7, 0 }),
            (Enumerable.Repeat("same", 9).ToArray(), Enumerable.Range(0, 9).ToArray()),
        })
        {
            var values = Enumerable.Range(0, names.Length).ToArray();
            FalloutFacePresetOrder.Sort(values, (left, right) => StringComparer.Ordinal.Compare(names[left], names[right]));
            Require(values.SequenceEqual(expected), "Preset sort changed admitted equal-name vectors.");
        }
        var random = new Random(74321);
        for (var count = 0; count < 300; count++)
        {
            var values = Enumerable.Range(0, count).Select(_ => random.Next(25)).ToArray();
            var expected = values.Order().ToArray();
            FalloutFacePresetOrder.Sort(values, (left, right) => left.CompareTo(right));
            Require(values.SequenceEqual(expected), "Preset sorter lost ordering, an element or partition progress.");
        }
    }

    private static byte[] Controls()
    {
        using var buffer = new MemoryStream(); using var writer = new BinaryWriter(buffer);
        writer.Write("FRCTL001"u8); writer.Write(1U); writer.Write(2U);
        foreach (var count in new[] { 50, 30, 50, 0 }) writer.Write(count);
        for (var group = 0; group < 4; group++) writer.Write(0);
        for (var population = 0; population < 5; population++)
            for (var attribute = 0; attribute < 2; attribute++)
                for (var domain = 0; domain < 2; domain++)
                {
                    for (var index = 0; index < 50; index++) writer.Write(index == 0 || attribute == 1 && index == 1 ? 1f : 0);
                    writer.Write(attribute == 0 ? 10f : -2f);
                }
        for (var index = 0; index < 20; index++) writer.Write(new byte[101 * 4]);
        for (var population = 0; population < 5; population++) writer.Write(new byte[(100 + 10000 + 2500 + 2500) * 4]);
        return buffer.ToArray();
    }
    private static FalloutFormKey Key(uint id) => new("Faces.esm", id);
    private static byte[] Floats(int count, params float[] values)
    {
        var bytes = new byte[count * 4];
        for (var index = 0; index < values.Length; index++) BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(index * 4), values[index]);
        return bytes;
    }
    private static byte[] Header() { var bytes = new byte[12]; BinaryPrimitives.WriteSingleLittleEndian(bytes, 1.34f); return Record("TES4", 0, Field("HEDR", bytes)); }
    private static byte[] Race(uint id) => Record("RACE", id, Field("MNAM", []), Field("FGGS", Floats(50, 1, 3, 5)),
        Field("FGGA", Floats(30, 3)), Field("FGTS", Floats(50, 77)), Field("FNAM", []), Field("FGGS", Floats(50, 100)),
        Field("FGGA", Floats(30, 100)), Field("FGTS", Floats(50, 100)));
    private static byte[] Npc(uint id, uint race, string name, uint flags, byte[] geometry, byte[] asymmetry, uint script = 0)
    {
        var config = new byte[24]; BinaryPrimitives.WriteUInt32LittleEndian(config, flags);
        return Record("NPC_", id, Field("ACBS", config), Field("RNAM", BitConverter.GetBytes(race)), Field("FULL", Text(name)),
            Field("FGGS", geometry), Field("FGGA", asymmetry), Field("FGTS", Floats(50, 71)), script == 0 ? [] : Field("SCRI", BitConverter.GetBytes(script)));
    }
    private static byte[] Reference(uint id, uint npc) => Record("ACHR", id, Field("NAME", BitConverter.GetBytes(npc)), Field("DATA", new byte[24]));
    private static byte[] Script()
    {
        var header = new byte[20]; BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12), 2); header[16] = 1;
        byte[] Local(uint index, string name) { var bytes = new byte[24]; BinaryPrimitives.WriteUInt32LittleEndian(bytes, index); return Join(Field("SLSD", bytes), Field("SCVR", Text(name))); }
        return Record("SCPT", 0x500, Field("SCHR", header), Local(1, "before"), Local(2, "after"), Field("SCRO", BitConverter.GetBytes(0x14u)),
            Field("SCTX", Text("begin GameMode\nset before to before + 1\nMatchFaceGeometry player 50.9\nset after to after + 1\nend")));
    }
    private static byte[] Group(uint cell, byte[] data) { var bytes = new byte[24 + data.Length]; Encoding.ASCII.GetBytes("GRUP").CopyTo(bytes, 0); BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)bytes.Length); BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), cell); BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), 6); data.CopyTo(bytes, 24); return bytes; }
    private static byte[] Record(string name, uint id, params byte[][] fields) { var data = Join(fields); var bytes = new byte[24 + data.Length]; Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0); BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)data.Length); BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), id); data.CopyTo(bytes, 24); return bytes; }
    private static byte[] Field(string name, byte[] data) { var bytes = new byte[6 + data.Length]; Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0); BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), (ushort)data.Length); data.CopyTo(bytes, 6); return bytes; }
    private static byte[] Text(string value) => Encoding.Latin1.GetBytes(value + "\0");
    private static byte[] Join(params byte[][] fields) => fields.SelectMany(field => field).ToArray();
    private static void Require(bool value, string message) { if (!value) throw new InvalidDataException(message); }
    private static void Reject(Action action) { try { action(); } catch (Exception error) when (error is InvalidDataException or NotSupportedException or KeyNotFoundException) { return; } throw new InvalidDataException("Invalid face state was admitted."); }
}
