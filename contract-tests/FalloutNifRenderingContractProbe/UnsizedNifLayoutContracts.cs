using System.Security.Cryptography;
using System.Text;
using OpenNV.Runtime.Formats.Gamebryo;

internal static class UnsizedNifLayoutContracts
{
    private const uint FileVersion = 0x14000004;

    internal static void Run()
    {
        var blocks = Fixture();
        var bytes = File(blocks);
        var original = SHA256.HashData(bytes);
        var ranges = new List<FalloutNifReadRange>();
        var nif = FalloutNifFile.Read(bytes, ranges.Add);
        Require(nif.FileVersion == FileVersion && nif.UserVersion2 == 11 && nif.Strings.Count == 0 &&
            nif.Blocks.Count == blocks.Length && nif.Roots.SequenceEqual([0, 10]),
            "Unsized header invented size/string tables or changed source identities.");
        Require(ranges.Sum(range => range.Length) == bytes.Length && ranges.First().Offset == 0 &&
            ranges.Zip(ranges.Skip(1)).All(pair => pair.First.Offset + pair.First.Length == pair.Second.Offset),
            "Sequential header/body/footer indexing skipped, duplicated or guessed source bytes.");
        var nextOffset = nif.Blocks[0].Offset;
        for (var index = 0; index < blocks.Length; index++)
        {
            var block = nif.Blocks[index];
            Require(block.Index == index && block.Offset == nextOffset && block.Size == blocks[index].Payload.Length,
                "A sequential block boundary differs from independently authored extents.");
            nextOffset += block.Size;
            ranges.Clear();
            var decoded = nif.ReadObject(index);
            Require(ranges.Sum(range => range.Length) == block.Size && ranges.First().Offset == block.Offset &&
                ranges.Zip(ranges.Skip(1)).All(pair => pair.First.Offset + pair.First.Length == pair.Second.Offset) &&
                ReferenceEquals(nif.ReadObject(index), decoded),
                "Bounded lazy decode changed source extents or failed immutable reuse.");
        }
        Require(nif.ReadNode(0) is { Name: "root", Flags: 0x120e, Children: [2, 4] } &&
            nif.ReadNode(2).CollisionObject == 5 &&
            nif.ReadObject(1) is FalloutNifBsxFlags { Name: "flags", Flags: 2 } &&
            nif.ReadObject(9) is FalloutNifStringExtraData { Name: "note", Value: "authored \u03bb declaration" },
            "Inline source names, node refs or extra data were treated as string indices.");
        Require(nif.ReadGeometry(4) is { Data: 3, SkinInstance: -1, MaterialNames: [],
                MaterialExtraData: [], ActiveMaterial: -1, Dirty: false } &&
            nif.ReadMeshData(3) is { Vertices.Length: 3, TextureCoordinates.Length: 2,
                StoredUvSets: 2, DecodedUvSets: 2, Triangles: [{ A: 0, B: 1, C: 2 }] } &&
            nif.ReadGeometry(10).Data == 11 && nif.ReadMeshData(11).Triangles.Length == 1,
            "Legacy material or geometry-data fields were read using a later layout.");
        var body = (FalloutNifRigidBody)nif.ReadObject(6);
        Require(body.Shape == 8 && body.Filter is { Layer: 1, Flags: 0x92, Group: 0x3412 } &&
            body.InfoFilter is { Layer: 1, Flags: 0x34, Group: 0x5678 } && body.Mass == 0 &&
            body.MotionSystem == 7 && body.BodyFlags == 0x12345678 && body.Constraints.Length == 0 &&
            body.SourceBytes.Span.SequenceEqual(blocks[6].Payload) &&
            nif.ReadObject(8) is FalloutNifBoxShape { Material: 7, Radius: .125f, Dimensions: { X: 3, Y: 2, Z: 1 } },
            "Unsized body/shape decode changed raw filter, material or source-byte identity.");
        Require(original.SequenceEqual(SHA256.HashData(bytes)), "Unsized reader changed original source bytes.");

        for (var index = 0; index < blocks.Length; index++)
        {
            var truncated = blocks.ToArray();
            truncated[index] = (blocks[index].Type, blocks[index].Payload[..^1]);
            Reject(() => FalloutNifFile.Read(File(truncated)));
            var padded = blocks.ToArray();
            padded[index] = (blocks[index].Type, [.. blocks[index].Payload, 0]);
            Reject(() => FalloutNifFile.Read(File(padded)));
        }
        var unknown = blocks.ToArray(); unknown[9] = ("NiBinaryExtraData", blocks[9].Payload);
        Reject(() => FalloutNifFile.Read(File(unknown)), "sequential layout");
        var namedShader = blocks.ToArray(); namedShader[4] = ("NiTriStrips", Geometry("render", 3, true));
        Reject(() => FalloutNifFile.Read(File(namedShader)), "legacy named shader");
        var invalidReference = blocks.ToArray(); invalidReference[5] = ("bhkCollisionObject", Bytes(w =>
        { w.Write(2); w.Write((ushort)1); w.Write(blocks.Length); }));
        Reject(() => FalloutNifFile.Read(File(invalidReference)));
        var invalidIndex = blocks.ToArray(); invalidIndex[3] = ("NiTriStripsData", Mesh(true, 3));
        Reject(() => FalloutNifFile.Read(File(invalidIndex)));
        Reject(() => FalloutNifFile.Read(bytes[..^1]));
        Reject(() => FalloutNifFile.Read(bytes.Concat(new byte[] { 0 }).ToArray()));
        Reject(() => FalloutNifFile.Read(File(blocks, 14)));
        Reject(() => FalloutNifFile.Read(File(blocks, 11, FalloutNifFile.Version)));
        Console.WriteLine("OPENNV_UNSIZED_NIF_LAYOUT_CONTRACT_PASS fileVersion=20.0.0.4 stream=11 inlineStrings=true exactSequentialExtents=true independentBodyBytes=true multipleUvs=true sourceImmutable=true unknownLayoutsRefused=true malformedRejected=true parity=unverified");
    }

    private static (string Type, byte[] Payload)[] Fixture() =>
    [
        ("NiNode", Node("root", -1, [2, 4], [1])),
        ("BSXFlags", Bytes(w => { Text(w, "flags"); w.Write(2U); })),
        ("NiNode", Node("collision group", 5, [])),
        ("NiTriStripsData", Mesh(true)),
        ("NiTriStrips", Geometry("render", 3)),
        ("bhkCollisionObject", Bytes(w => { w.Write(2); w.Write((ushort)1); w.Write(6); })),
        ("bhkRigidBodyT", Body()),
        ("NiMaterialProperty", Bytes(w =>
        {
            Net(w, "material");
            foreach (var value in new[] { .1f, .2f, .3f, .4f, .5f, .6f, .7f, .8f, .9f, .25f, .5f, .75f, 13, .8f }) w.Write(value);
        })),
        ("bhkBoxShape", Bytes(w =>
        { w.Write(7U); w.Write(.125f); w.Write(new byte[8]); w.Write(3f); w.Write(2f); w.Write(1f); w.Write(.5f); })),
        ("NiStringExtraData", Bytes(w => { Text(w, "note"); Text(w, "authored \u03bb declaration"); })),
        ("NiTriShape", Geometry("triangle", 11)),
        ("NiTriShapeData", Mesh(false)),
    ];

    private static byte[] Node(string name, int collision, int[] children, int[]? extra = null) => Bytes(w =>
    { Av(w, name, collision, [], extra); Refs(w, children); Refs(w, []); });

    private static byte[] Geometry(string name, int data, bool namedShader = false) => Bytes(w =>
    {
        Av(w, name, -1, [7]); w.Write(data); w.Write(-1); w.Write(namedShader);
        if (namedShader) { Text(w, "authored-shader"); w.Write(17); }
    });

    private static byte[] Mesh(bool strips, ushort lastIndex = 2) => Bytes(w =>
    {
        w.Write(19); w.Write((ushort)3); w.Write((byte)0); w.Write((byte)0); w.Write(true);
        foreach (var value in new float[] { 0, 0, 0, 1, 0, 0, 0, 1, 0 }) w.Write(value);
        w.Write((ushort)2); w.Write(true);
        for (var index = 0; index < 3; index++) { w.Write(0f); w.Write(0f); w.Write(1f); }
        w.Write(0f); w.Write(0f); w.Write(0f); w.Write(2f); w.Write(false);
        foreach (var value in new float[] { 0, 0, 1, 0, 0, 1, .25f, .5f, .75f, .5f, .25f, 1 }) w.Write(value);
        w.Write((ushort)0x4000); w.Write(-1); w.Write((ushort)1);
        if (strips) { w.Write((ushort)1); w.Write((ushort)3); }
        else w.Write(3U);
        w.Write(true); w.Write((ushort)0); w.Write((ushort)1); w.Write(lastIndex);
        if (!strips) w.Write((ushort)0);
    });

    private static byte[] Body() => Bytes(w =>
    {
        void Filter(byte flags, ushort group) { w.Write((byte)1); w.Write(flags); w.Write(group); }
        void Response() { w.Write((byte)1); w.Write((byte)0); w.Write((ushort)0xffff); }
        void Vector(float x, float y, float z, float padding = 0) { w.Write(x); w.Write(y); w.Write(z); w.Write(padding); }
        w.Write(8); Filter(0x92, 0x3412); w.Write(0U); w.Write((byte)1); w.Write(new byte[3]);
        w.Write(0U); w.Write(0U); w.Write(0U); Response();
        w.Write(0U); Filter(0x34, 0x5678); w.Write(0U); Response(); w.Write(0U);
        Vector(2, -3, 4); Vector(0, 0, 0, 1); Vector(0, 0, 0); Vector(0, 0, 0);
        Vector(1, 0, 0); Vector(0, 1, 0); Vector(0, 0, 1); Vector(0, 0, 0);
        foreach (var value in new[] { 0f, .1f, .05f, .5f, .4f, 20f, 10f, .15f }) w.Write(value);
        w.Write((byte)7); w.Write((byte)1); w.Write((byte)1); w.Write((byte)1); w.Write(new byte[12]);
        w.Write(0U); w.Write(0x12345678U);
    });

    private static void Net(BinaryWriter writer, string name, int[]? extra = null)
    { Text(writer, name); Refs(writer, extra ?? []); writer.Write(-1); }
    private static void Av(BinaryWriter writer, string name, int collision, int[] properties, int[]? extra = null)
    {
        Net(writer, name, extra); writer.Write((ushort)0x120e);
        foreach (var value in new float[] { 2, -3, 4, 1, 0, 0, 0, 1, 0, 0, 0, 1, 1.25f }) writer.Write(value);
        Refs(writer, properties); writer.Write(collision);
    }
    private static void Refs(BinaryWriter writer, int[] refs)
    { writer.Write(refs.Length); foreach (var reference in refs) writer.Write(reference); }
    private static void Text(BinaryWriter writer, string text)
    { var bytes = Encoding.UTF8.GetBytes(text); writer.Write(bytes.Length); writer.Write(bytes); }
    private static byte[] Bytes(Action<BinaryWriter> write)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        write(writer); return stream.ToArray();
    }
    private static byte[] File((string Type, byte[] Payload)[] blocks, uint streamVersion = 11, uint binaryVersion = FileVersion) => Bytes(w =>
    {
        w.Write(Encoding.ASCII.GetBytes("Gamebryo File Format, Version 20.0.0.4\n"));
        w.Write(binaryVersion); w.Write((byte)1); w.Write(11U); w.Write(blocks.Length); w.Write(streamVersion);
        w.Write(new byte[] { 1, 0, 1, 0, 1, 0 });
        var types = blocks.Select(block => block.Type).Distinct(StringComparer.Ordinal).ToArray();
        w.Write((ushort)types.Length); foreach (var type in types) Text(w, type);
        foreach (var block in blocks) w.Write((ushort)Array.IndexOf(types, block.Type));
        w.Write(0U); // Groups; this layout has no size table or global strings.
        foreach (var block in blocks) w.Write(block.Payload);
        w.Write(2U); w.Write(0); w.Write(10);
    });
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
    private static void Reject(Action read, string? reason = null)
    {
        try { read(); }
        catch (Exception failure) when (failure is InvalidDataException or NotSupportedException)
        {
            Require(reason is null || failure.Message.Contains(reason, StringComparison.Ordinal),
                $"Refusal did not retain its specific source diagnosis: {failure.Message}");
            return;
        }
        throw new InvalidOperationException("Malformed or unowned unsized layout was admitted.");
    }
}
