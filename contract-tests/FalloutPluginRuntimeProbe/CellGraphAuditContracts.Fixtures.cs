using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

internal static partial class CellGraphAuditContracts
{
    private const string Plugin = "FalloutNV.esm";
    private static readonly Guid OldBuild = Guid.Parse("55f29a43-ecf7-4335-8eb2-f38d6e35a9e1");
    private static readonly DateTime OldTime = new(2001, 2, 3, 4, 5, 6, DateTimeKind.Utc);
    private static FalloutFormKey Key(uint id) => new(Plugin, id);
    private static string[] Arguments(string game, string output, string configuration, string checkpoint, string snapshot) =>
        [game, output, "--seed", Plugin + ":800", "--runtime-config", configuration, "--metadata", "Probe",
            "--checkpoint", checkpoint, "--snapshot", snapshot, "--sample-native", "-3", "4", "5"];

    private static void WriteSnapshot(string path, string compatibility, FalloutPluginStack? records)
    {
        var references = records is null ? [] : records.EffectiveCellChildren(Key(0x800), new HashSet<string> { "REFR" })
            .Select(record => record.FormKey.ToString()).ToArray();
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            detail = "complete-runtime-snapshot", cell = Key(0x800).ToString(),
            reviewScope = new { sourceCompatibilityId = compatibility, runtimeBuild = OldBuild, capturedUtc = OldTime, references },
            missingRuntimeReferences = new[] { "world/active-cell/" + Key(0x800) + "/" + Key(0x812) },
            referenceDivergences = Array.Empty<object>(), actorDivergences = Array.Empty<object>(), actors = Array.Empty<object>()
        }));
    }

    private static byte[] PluginFixture()
    {
        var header = new byte[12]; BinaryPrimitives.WriteSingleLittleEndian(header, 1.34f);
        var light = new byte[32]; BinaryPrimitives.WriteInt32LittleEndian(light, -1);
        BinaryPrimitives.WriteUInt32LittleEndian(light.AsSpan(4), 200);
        light[8] = 80; light[9] = 120; light[10] = 160;
        BinaryPrimitives.WriteSingleLittleEndian(light.AsSpan(16), 1);
        BinaryPrimitives.WriteSingleLittleEndian(light.AsSpan(20), 90);
        return Join(Record("TES4", 0, 0, Field("HEDR", header)),
            Record("DOOR", 0x100, 0, Field("EDID", Text("FixtureDoor"))),
            Record("STAT", 0x101, 0, Field("MODL", Text("box.nif"))),
            Record("LIGH", 0x102, 0, Field("MODL", Text("modeled-light.nif")), Field("DATA", light)),
            Record("STAT", 0x103, 0, Field("EDID", Text("FixtureEnableRoot"))),
            Record("STAT", 0x104, 0, Field("MODL", Text("missing.nif"))),
            Cell(0x800, "ProbeHub"), Group(0x800,
                Reference(0x810, 0x100, teleport: 0x820), Reference(0x811, 0x100, teleport: 0x830),
                Reference(0x812, 0x101), Reference(0x813, 0x102), Reference(0x814, 0x103, flags: 0x800),
                Reference(0x815, 0x101, parent: 0x814, opposite: true)),
            Cell(0x801, "OtherWing"), Group(0x801, Reference(0x820, 0x100, teleport: 0x810), Reference(0x821, 0x104)),
            Cell(0x802, "ProbeDetachedEmpty"),
            Record("CELL", 0x803, 0, Field("EDID", Text("ProbeExterior")), Field("DATA", [0]), Field("XCLC", new byte[8])),
            Group(0x803, Reference(0x830, 0x100, teleport: 0x811)),
            Record("CELL", 0x804, 0, Field("EDID", Text("ProbeInvalid")), Field("DATA", [1]), Field("XCLC", new byte[2])),
            Group(0x804, Reference(0x840, 0x101)));
    }

    private static byte[] Reference(uint id, uint basis, uint flags = 0, uint? teleport = null, uint? parent = null, bool opposite = false)
    {
        var fields = new List<byte[]> { Field("NAME", BitConverter.GetBytes(basis)), Field("DATA", new byte[24]) };
        if (teleport is { } target)
        {
            var bytes = new byte[32]; BinaryPrimitives.WriteUInt32LittleEndian(bytes, target); fields.Add(Field("XTEL", bytes));
        }
        if (parent is { } root)
        {
            var bytes = new byte[8]; BinaryPrimitives.WriteUInt32LittleEndian(bytes, root); bytes[4] = opposite ? (byte)1 : (byte)0;
            fields.Add(Field("XESP", bytes));
        }
        return Record("REFR", id, flags, fields.ToArray());
    }
    private static byte[] Cell(uint id, string editor) => Record("CELL", id, 0, Field("DATA", [1]), Field("EDID", Text(editor)));
    private static byte[] Text(string value) => Encoding.ASCII.GetBytes(value + '\0');
    private static byte[] Join(params byte[][] values) => values.SelectMany(value => value).ToArray();
    private static byte[] Field(string signature, byte[] payload)
    {
        var bytes = new byte[6 + payload.Length]; Encoding.ASCII.GetBytes(signature).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), checked((ushort)payload.Length)); payload.CopyTo(bytes, 6); return bytes;
    }
    private static byte[] Record(string signature, uint id, uint flags, params byte[][] fields)
    {
        var payload = Join(fields); var bytes = new byte[24 + payload.Length]; Encoding.ASCII.GetBytes(signature).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)payload.Length); BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), flags);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), id); BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(20), 15);
        payload.CopyTo(bytes, 24); return bytes;
    }
    private static byte[] Group(uint cell, params byte[][] records)
    {
        var payload = Join(records); var bytes = new byte[24 + payload.Length]; Encoding.ASCII.GetBytes("GRUP").CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)bytes.Length); BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), cell);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12), 6); payload.CopyTo(bytes, 24); return bytes;
    }

    private static byte[] NifFixture(bool rootVariant = false, bool rotateChild = false, bool multiRoot = false, bool texture = false)
    {
        var body = new byte[236]; BinaryPrimitives.WriteInt32LittleEndian(body, 4); body[4] = 1;
        BinaryPrimitives.WriteSingleLittleEndian(body.AsSpan(80), 1);
        var blocks = new List<(string Type, byte[] Payload)>
        {
            ("NiNode", Node(rootVariant ? new float[] { -93, 61, 47 } : [73, -41, 29], rootVariant ? 5 : 2.5f, -1, [1], rotate: !rootVariant)),
            ("NiNode", Node([10, 20, 30], .5f, 2, [], rotateChild)),
            ("bhkCollisionObject", Bytes(writer => { writer.Write(1); writer.Write((ushort)1); writer.Write(3); })),
            ("bhkRigidBody", body),
            ("bhkBoxShape", Bytes(writer =>
            { writer.Write(7U); writer.Write(.1f); writer.Write(new byte[8]); writer.Write(2f); writer.Write(3f); writer.Write(4f); writer.Write(0f); }))
        };
        if (texture) blocks.Add(("BSShaderTextureSet", Bytes(writer =>
        {
            writer.Write(1); var path = "textures/probe.dds"; writer.Write(path.Length); writer.Write(Encoding.ASCII.GetBytes(path));
        })));
        return Bytes(writer =>
        {
            void Sized(string value) { writer.Write(value.Length); writer.Write(Encoding.ASCII.GetBytes(value)); }
            writer.Write(Encoding.ASCII.GetBytes("Gamebryo File Format, Version 20.2.0.7\n"));
            writer.Write(FalloutNifFile.Version); writer.Write((byte)1); writer.Write(FalloutNifFile.UserVersion);
            writer.Write(blocks.Count); writer.Write(34U); writer.Write(new byte[] { 1, 0, 1, 0, 1, 0 });
            writer.Write((ushort)blocks.Count); foreach (var block in blocks) Sized(block.Type);
            for (var index = 0; index < blocks.Count; index++) writer.Write((ushort)index);
            foreach (var block in blocks) writer.Write(block.Payload.Length);
            writer.Write(1U); writer.Write(7U); Sized("fixture"); writer.Write(0U);
            foreach (var block in blocks) writer.Write(block.Payload);
            writer.Write(multiRoot ? 2U : 1U); writer.Write(0); if (multiRoot) writer.Write(1);
        });
    }
    private static byte[] Node(float[] position, float scale, int collision, int[] children, bool rotate = false) => Bytes(writer =>
    {
        writer.Write(0); writer.Write(0U); writer.Write(-1); writer.Write(14U);
        foreach (var value in position) writer.Write(value);
        foreach (var value in rotate ? new float[] { 0, -1, 0, 1, 0, 0, 0, 0, 1 } : [1, 0, 0, 0, 1, 0, 0, 0, 1]) writer.Write(value);
        writer.Write(scale); writer.Write(0U); writer.Write(collision); writer.Write(children.Length);
        foreach (var child in children) writer.Write(child); writer.Write(0U);
    });
    private static byte[] Bytes(Action<BinaryWriter> write)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream); write(writer); return stream.ToArray();
    }
    private static byte[] DdsFixture()
    {
        var bytes = new byte[136]; "DDS "u8.CopyTo(bytes);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 124);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), 0x81007);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), 4); BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), 4);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(20), 8); BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(28), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(76), 32); BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(80), 4);
        "DXT1"u8.CopyTo(bytes.AsSpan(84)); BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(108), 0x1000);
        return bytes;
    }
}
