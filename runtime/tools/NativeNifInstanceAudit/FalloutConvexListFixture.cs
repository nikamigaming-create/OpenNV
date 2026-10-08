using System.Buffers.Binary;
using System.Text;
using OpenNV.Runtime.Formats.Gamebryo;

// First-party mathematical solids only. No owned geometry or retail declarations
// are embedded here. Both managed and native contracts consume the full reader.
internal static class FalloutConvexListFixture
{
    internal static byte[] Create(bool dynamic = false, bool transformed = false, int[]? children = null,
        uint parentMaterial = 7, uint childMaterial = 7, bool cachedAabb = true, uint version2 = 34,
        bool concaveSecond = false)
    {
        var body = new byte[236];
        BinaryPrimitives.WriteInt32LittleEndian(body, transformed ? 6 : 3);
        body[4] = 7; body[5] = 0x24; BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(6), 0x4567);
        body[36] = 8; body[37] = 0x31; BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(38), 0x1234);
        BinaryPrimitives.WriteSingleLittleEndian(body.AsSpan(80), 1);
        BinaryPrimitives.WriteSingleLittleEndian(body.AsSpan(180), dynamic ? 3.25f : 0);
        BinaryPrimitives.WriteSingleLittleEndian(body.AsSpan(192), .6f);
        BinaryPrimitives.WriteSingleLittleEndian(body.AsSpan(196), .3f);
        body[212] = dynamic ? (byte)1 : (byte)7;
        var blocks = new List<(string Type, byte[] Bytes)>
        {
            ("NiNode", Bytes(writer =>
            {
                writer.Write(0); writer.Write(0); writer.Write(-1);
                if (version2 < 27) writer.Write((ushort)14); else writer.Write(14U);
                foreach (var value in new float[] { 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1, 1 }) writer.Write(value);
                writer.Write(0); writer.Write(1); writer.Write(0); writer.Write(0);
            })),
            ("bhkCollisionObject", Bytes(writer => { writer.Write(0); writer.Write((ushort)1); writer.Write(2); })),
            ("bhkRigidBody", body),
            ("bhkConvexListShape", List(children ?? [4, 5], parentMaterial, cachedAabb)),
            ("bhkConvexVerticesShape", Cube(-3, childMaterial)),
            ("bhkConvexVerticesShape", Cube(3, childMaterial)),
        };
        if (transformed)
            blocks.Add(("bhkConvexTransformShape", Transform(3, parentMaterial, quarterTurn: true, 2, 1, 3)));
        if (concaveSecond)
        {
            var data = blocks.Count;
            blocks[5] = ("bhkPackedNiTriStripsShape", Bytes(writer =>
            {
                writer.Write(new byte[8]); writer.Write(0f); writer.Write(0U);
                foreach (var value in new float[] { 1, 1, 1, 0, 0, 1, 1, 1, 0 }) writer.Write(value);
                writer.Write(data);
            }));
            blocks.Add(("hkPackedNiTriStripsData", Bytes(writer =>
            {
                writer.Write(1); writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)2); writer.Write((ushort)0);
                writer.Write(3); writer.Write((byte)0);
                foreach (var value in new float[] { 2, 0, 0, 4, 0, 0, 3, 1, 0 }) writer.Write(value);
                writer.Write((ushort)1); writer.Write(new byte[] { 7, 0, 0, 0 }); writer.Write(3U); writer.Write(childMaterial);
            })));
        }
        return File(blocks, version2);
    }

    internal static byte[] Rewrite(byte[] source, int blockIndex, Action<byte[]> change, bool truncate = false, bool append = false)
    {
        var file = FalloutNifFile.Read(source);
        var blocks = file.Blocks.Select(block => (Type: block.TypeName, Bytes: source.AsSpan(block.Offset, block.Size).ToArray())).ToArray();
        change(blocks[blockIndex].Bytes);
        if (truncate) blocks[blockIndex] = (blocks[blockIndex].Type, blocks[blockIndex].Bytes[..^1]);
        if (append) blocks[blockIndex] = (blocks[blockIndex].Type, [.. blocks[blockIndex].Bytes, 0xa5]);
        return File(blocks, file.UserVersion2);
    }

    internal static byte[] Wide(int count = 256)
    {
        if (count < 1) throw new ArgumentOutOfRangeException(nameof(count));
        var original = Create(); var file = FalloutNifFile.Read(original);
        var blocks = file.Blocks.Take(3).Select(block => (Type: block.TypeName,
            Bytes: original.AsSpan(block.Offset, block.Size).ToArray())).ToList();
        blocks.Add(("bhkConvexListShape", List(Enumerable.Range(4, count).ToArray(), 7, false)));
        for (var index = 0; index < count; ++index) blocks.Add(("bhkConvexVerticesShape", Cube(index * 3, 7)));
        return File(blocks, file.UserVersion2);
    }

    internal static byte[] Nested(bool dynamic = false)
    {
        var original = Create(dynamic); var file = FalloutNifFile.Read(original);
        var blocks = file.Blocks.Select(block => (Type: block.TypeName,
            Bytes: original.AsSpan(block.Offset, block.Size).ToArray())).ToList();
        blocks[3] = ("bhkConvexListShape", List([6], 7, false));
        blocks.Add(("bhkConvexListShape", List([4, 5], 7, true)));
        return File(blocks, file.UserVersion2);
    }

    internal static byte[] ChildTransformed(bool dynamic = false)
    {
        var original = Create(dynamic); var file = FalloutNifFile.Read(original);
        var blocks = file.Blocks.Select(block => (Type: block.TypeName,
            Bytes: original.AsSpan(block.Offset, block.Size).ToArray())).ToList();
        blocks[3] = ("bhkConvexListShape", List([6, 5], 7, false));
        blocks.Add(("bhkConvexTransformShape", Transform(4, 7, quarterTurn: false, 0, 3, 0)));
        return File(blocks, file.UserVersion2);
    }

    private static byte[] Transform(int child, uint material, bool quarterTurn, float x, float y, float z) => Bytes(writer =>
    {
        writer.Write(child); writer.Write(material); writer.Write(.05f); writer.Write(new byte[8]);
        var basis = quarterTurn ? new float[] { 0, -1, 0, 0, 1, 0, 0, 0, 0, 0, 1, 0 } :
            new float[] { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0 };
        foreach (var value in basis) writer.Write(value);
        writer.Write(x); writer.Write(y); writer.Write(z); writer.Write(1f);
    });

    private static byte[] List(int[] children, uint material, bool cachedAabb) => Bytes(writer =>
    {
        writer.Write(children.Length); foreach (var child in children) writer.Write(child);
        writer.Write(material); writer.Write(.05f); writer.Write(0x12345678U); writer.Write(0x7fc12345U);
        writer.Write(0x11111111U); writer.Write(0x22222222U); writer.Write(0xa0000022U);
        writer.Write(cachedAabb); writer.Write(.75f);
    });

    private static byte[] Cube(float centerX, uint material) => Bytes(writer =>
    {
        writer.Write(material); writer.Write(.05f); writer.Write(new byte[24]); writer.Write(8);
        foreach (var x in new[] { centerX - 1, centerX + 1 })
            foreach (var y in new[] { -1f, 1f })
                foreach (var z in new[] { -1f, 1f })
                { writer.Write(x); writer.Write(y); writer.Write(z); writer.Write(0f); }
        writer.Write(6);
        foreach (var plane in new float[][]
        {
            [-1, 0, 0, centerX - 1], [1, 0, 0, -(centerX + 1)],
            [0, -1, 0, -1], [0, 1, 0, -1], [0, 0, -1, -1], [0, 0, 1, -1],
        }) foreach (var value in plane) writer.Write(value);
    });

    private static byte[] File(IReadOnlyList<(string Type, byte[] Bytes)> blocks, uint version2) => Bytes(writer =>
    {
        writer.Write("Gamebryo File Format, Version 20.2.0.7\n"u8);
        writer.Write(FalloutNifFile.Version); writer.Write((byte)1); writer.Write(FalloutNifFile.UserVersion);
        writer.Write(blocks.Count); writer.Write(version2); writer.Write(new byte[] { 1, 0, 1, 0, 1, 0 });
        writer.Write((ushort)blocks.Count);
        foreach (var block in blocks) { writer.Write(block.Type.Length); writer.Write(Encoding.ASCII.GetBytes(block.Type)); }
        for (var index = 0; index < blocks.Count; ++index) writer.Write((ushort)index);
        foreach (var block in blocks) writer.Write(block.Bytes.Length);
        writer.Write(1); writer.Write(19); writer.Write(19); writer.Write("AuthoredConvexLists"u8);
        writer.Write(0); foreach (var block in blocks) writer.Write(block.Bytes);
        writer.Write(1); writer.Write(0);
    });

    private static byte[] Bytes(Action<BinaryWriter> write)
    {
        using var output = new MemoryStream(); using var writer = new BinaryWriter(output, Encoding.UTF8, true);
        write(writer); return output.ToArray();
    }
}
