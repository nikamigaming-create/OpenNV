using System.Buffers.Binary;
using System.Text;
using OpenNV.Runtime.Formats.Gamebryo;

// First-party joint frames and controller lists. No original model data.
internal static class FalloutNifPhysicsFixture
{
    internal static byte[] ControllerChain(string mode = "blend-middle", uint version2 = 34)
    {
        var types = mode switch
        {
            "blend-before" => new[] { "bhkBlendController", "NiTransformController" },
            "blend-after" => new[] { "NiTransformController", "bhkBlendController" },
            _ => new[] { "bhkBlendController", "NiTransformController", "bhkBlendController" },
        };
        var interpolator = types.Length + 1;
        var unrelated = interpolator + 1;
        var blocks = new List<(string Type, byte[] Bytes)> { ("NiNode", Node(1, version2)) };
        for (var ordinal = 0; ordinal < types.Length; ordinal++)
        {
            var block = ordinal + 1;
            var next = ordinal + 1 < types.Length ? block + 1 : -1;
            var type = types[ordinal];
            var bytes = Bytes(writer =>
            {
                writer.Write(next); writer.Write((ushort)0x004c);
                writer.Write(1f); writer.Write(0f);
                writer.Write(type == "NiTransformController" ? 0f : float.MaxValue);
                writer.Write(type == "NiTransformController" ? 0f : float.MinValue);
                writer.Write(0);
                if (type == "bhkBlendController") writer.Write(0U);
                else writer.Write(interpolator);
            });
            blocks.Add((type, bytes));
        }
        blocks.Add(("NiTransformInterpolator", Bytes(writer =>
        {
            foreach (var value in new float[] { 2, 3, 4, 1, 0, 0, 0, 1 }) writer.Write(value);
            writer.Write(-1);
        })));
        blocks.Add(("NiNode", Node(-1, version2)));

        void Integer(int block, int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(blocks[block].Bytes.AsSpan(offset), value);
        switch (mode)
        {
            case "cycle": Integer(types.Length, 0, 1); break;
            case "foreign-target": Integer(2, 22, unrelated); break;
            case "node-suffix": Integer(types.Length, 0, unrelated); break;
            case "invalid-next": Integer(1, 0, -2); break;
            case "missing-next": Integer(1, 0, blocks.Count); break;
            case "unknown-keys": Integer(1, 26, 5); break;
            case "active-blend":
                BinaryPrimitives.WriteSingleLittleEndian(blocks[1].Bytes.AsSpan(14), 0);
                BinaryPrimitives.WriteSingleLittleEndian(blocks[1].Bytes.AsSpan(18), 1);
                break;
            case "wrong-flags": BinaryPrimitives.WriteUInt16LittleEndian(blocks[1].Bytes.AsSpan(4), 0x0040); break;
            case "nonfinite": BinaryPrimitives.WriteSingleLittleEndian(blocks[1].Bytes.AsSpan(6), float.NaN); break;
            case "truncated": blocks[1] = (blocks[1].Type, blocks[1].Bytes[..^1]); break;
            case "trailing": blocks[1] = (blocks[1].Type, [.. blocks[1].Bytes, 0xa5]); break;
            case "unowned-float-suffix":
                blocks[3] = ("NiFloatExtraDataController", Bytes(writer =>
                {
                    writer.Write(-1); writer.Write((ushort)0x004c); writer.Write(1f); writer.Write(0f);
                    writer.Write(float.MaxValue); writer.Write(float.MinValue); writer.Write(0);
                    writer.Write(-1); writer.Write(1);
                }));
                break;
        }
        return File(blocks, version2, ["AuthoredNode", "DeclaredFloat"], 0);
    }

    internal static byte[] Constraint(string type, uint version2 = 34, byte motorType = 0,
        bool enabled = false, uint wrappedType = 7, string mode = "valid")
    {
        if (type == "bhkLimitedHingeConstraint") wrappedType = 2;
        if (type == "bhkHingeConstraint") wrappedType = 1;
        var descriptor = Bytes(writer =>
        {
            writer.Write(2U); writer.Write(0); writer.Write(1); writer.Write(1U);
            if (type == "bhkMalleableConstraint")
            {
                writer.Write(wrappedType); writer.Write(2U);
                writer.Write(-1); writer.Write(-1); writer.Write(1U);
            }
            void Vector(float x, float y, float z, uint padding)
            { writer.Write(x); writer.Write(y); writer.Write(z); writer.Write(padding); }
            Vector(1, 0, 0, 0); Vector(0, 1, 0, uint.MaxValue);
            Vector(0, 0, 1, 0x81234567); Vector(.1f, -.2f, .3f, uint.MaxValue);
            Vector(0, 1, 0, 0x80); Vector(0, 0, 1, 0);
            Vector(1, 0, 0, uint.MaxValue); Vector(-.4f, .5f, -.6f, 0x12345678);
            if (wrappedType != 1)
            {
                if (wrappedType == 7) { writer.Write(.9f); writer.Write(-.2f); writer.Write(.4f); }
                writer.Write(-.3f); writer.Write(.6f); writer.Write(12f); writer.Write(motorType);
                if (motorType is 1 or 2 or 3)
                {
                    writer.Write(-1000f); writer.Write(2500f);
                    if (motorType == 1)
                    { writer.Write(.8f); writer.Write(.5f); writer.Write(1f); writer.Write(2f); }
                    if (motorType == 2) { writer.Write(.2f); writer.Write(-4f); writer.Write(true); }
                    if (motorType == 3) { writer.Write(3f); writer.Write(.7f); }
                    writer.Write(enabled);
                }
            }
            if (type == "bhkMalleableConstraint") writer.Write(.8f);
        });
        var headerBytes = type == "bhkMalleableConstraint" ? 36 : 16;
        switch (mode)
        {
            case "same-entity": BinaryPrimitives.WriteInt32LittleEndian(descriptor.AsSpan(8), 0); break;
            case "missing-entity": BinaryPrimitives.WriteInt32LittleEndian(descriptor.AsSpan(8), 3); break;
            case "null-entity": BinaryPrimitives.WriteInt32LittleEndian(descriptor.AsSpan(8), -1); break;
            case "priority": BinaryPrimitives.WriteUInt32LittleEndian(descriptor.AsSpan(12), 2); break;
            case "entity-count": BinaryPrimitives.WriteUInt32LittleEndian(descriptor, 1); break;
            case "nested-entity": BinaryPrimitives.WriteInt32LittleEndian(descriptor.AsSpan(28), 0); break;
            case "nested-count": BinaryPrimitives.WriteUInt32LittleEndian(descriptor.AsSpan(20), 1); break;
            case "nested-priority": BinaryPrimitives.WriteUInt32LittleEndian(descriptor.AsSpan(32), 3); break;
            case "nan-vector": BinaryPrimitives.WriteSingleLittleEndian(descriptor.AsSpan(headerBytes), float.NaN); break;
            case "infinite-limit": BinaryPrimitives.WriteSingleLittleEndian(descriptor.AsSpan(headerBytes + 128), float.PositiveInfinity); break;
            case "bad-frame": BinaryPrimitives.WriteSingleLittleEndian(descriptor.AsSpan(headerBytes), 2); break;
            case "nonfinite-motor": BinaryPrimitives.WriteSingleLittleEndian(descriptor.AsSpan(headerBytes + 129 + (wrappedType == 7 ? 24 : 12)), float.NaN); break;
            case "bad-motor-bool": descriptor[^1] = 2; break;
            case "truncated": descriptor = descriptor[..^1]; break;
            case "trailing": descriptor = [.. descriptor, 0xa5]; break;
        }
        var bodies = new byte[236];
        BinaryPrimitives.WriteInt32LittleEndian(bodies, -1);
        BinaryPrimitives.WriteSingleLittleEndian(bodies.AsSpan(80), 1);
        var blocks = new List<(string Type, byte[] Bytes)>
        {
            ("bhkRigidBody", bodies),
            (mode == "wrong-entity-type" ? "NiNode" : "bhkRigidBodyT",
                mode == "wrong-entity-type" ? Node(-1, version2) : (byte[])bodies.Clone()),
            (type, descriptor),
        };
        return File(blocks, version2, ["AuthoredNode"], 2);
    }

    private static byte[] Node(int controller, uint version2) => Bytes(writer =>
    {
        writer.Write(0); writer.Write(0); writer.Write(controller);
        if (version2 < 27) writer.Write((ushort)14); else writer.Write(14U);
        foreach (var value in new float[] { 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1, 1 }) writer.Write(value);
        writer.Write(0); writer.Write(-1); writer.Write(0); writer.Write(0);
    });

    private static byte[] Bytes(Action<BinaryWriter> write)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        write(writer); return stream.ToArray();
    }

    private static byte[] File(IReadOnlyList<(string Type, byte[] Bytes)> blocks, uint version2, string[] strings, int root) => Bytes(writer =>
    {
        writer.Write(Encoding.ASCII.GetBytes("Gamebryo File Format, Version 20.2.0.7\n"));
        writer.Write(FalloutNifFile.Version); writer.Write((byte)1); writer.Write(FalloutNifFile.UserVersion);
        writer.Write((uint)blocks.Count); writer.Write(version2); writer.Write(new byte[] { 1, 0, 1, 0, 1, 0 });
        var types = blocks.Select(block => block.Type).Distinct(StringComparer.Ordinal).ToArray();
        writer.Write((ushort)types.Length);
        foreach (var type in types) { var bytes = Encoding.ASCII.GetBytes(type); writer.Write(bytes.Length); writer.Write(bytes); }
        foreach (var block in blocks) writer.Write((ushort)Array.IndexOf(types, block.Type));
        foreach (var block in blocks) writer.Write(block.Bytes.Length);
        writer.Write(strings.Length); writer.Write(strings.Max(value => Encoding.UTF8.GetByteCount(value)));
        foreach (var value in strings) { var bytes = Encoding.UTF8.GetBytes(value); writer.Write(bytes.Length); writer.Write(bytes); }
        writer.Write(0U);
        foreach (var block in blocks) writer.Write(block.Bytes);
        writer.Write(1U); writer.Write(root);
    });
}
