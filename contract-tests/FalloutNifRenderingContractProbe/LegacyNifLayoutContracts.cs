using System.Text;
using OpenNV.Runtime.Formats.Gamebryo;

internal static partial class LegacyNifLayoutContracts
{
    internal static void Run()
    {
        foreach (var version in new uint[] { 14, 21, 26, 30, 34 })
        {
            var node = Node(version);
            var material = Material(version);
            var noLighting = NoLighting(version);
            var transform = Bytes(w =>
            {
                w.Write(3); w.Write(7U); w.Write(.25f); w.Write(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
                Matrix(w);
            });
            var phantom = Bytes(w =>
            {
                w.Write(4); w.Write((byte)12); w.Write((byte)0xa5); w.Write((ushort)0x1234);
                w.Write(0x98765432U); w.Write((byte)2); w.Write(new byte[] { 4, 5, 6 });
                w.Write(7U); w.Write(8U); w.Write(0x80000009U);
                w.Write(new byte[] { 9, 10, 11, 12, 13, 14, 15, 16 }); Matrix(w);
            });
            (string, byte[])[] blocks = [("NiNode", node), ("NiMaterialProperty", material),
                ("BSShaderNoLightingProperty", noLighting), ("bhkBoxShape", Bytes(w =>
                { w.Write(7U); w.Write(.25f); w.Write(new byte[8]); w.Write(2f); w.Write(3f); w.Write(4f); w.Write(.5f); })),
                ("bhkTransformShape", transform), ("bhkSimpleShapePhantom", phantom),
                ("bhkSPCollisionObject", Bytes(w => { w.Write(0); w.Write((ushort)1); w.Write(5); }))];
            var reads = new List<FalloutNifReadRange>();
            var nif = FalloutNifFile.Read(File(version, blocks), reads.Add);
            foreach (var block in nif.Blocks)
            {
                reads.Clear();
                _ = nif.ReadObject(block.Index);
                Require(reads.Sum(read => read.Length) == block.Size && reads.First().Offset == block.Offset &&
                    reads.Zip(reads.Skip(1)).All(pair => pair.First.Offset + pair.First.Length == pair.Second.Offset),
                    "Versioned block decode skipped or duplicated source bytes.");
            }
            var root = nif.ReadNode(0);
            Require(root.Flags == (version <= 26 ? 0xa50eU : 0x1234a50eU) &&
                root.Transform.Translation == new FalloutNifVector3(10, -20, 30) &&
                root.Transform.Scale == 1.25f && root.CollisionObject == 6,
                "AV flags width lost source flags, transform or collision identity.");
            var value = (FalloutNifMaterialProperty)nif.ReadObject(1);
            Require(value.Ambient == (version < 26 ? new FalloutNifColor3(.1f, .2f, .3f) : null) &&
                value.Diffuse == (version < 26 ? new FalloutNifColor3(.4f, .5f, .6f) : null) &&
                value.Specular == new FalloutNifColor3(.7f, .8f, .9f) &&
                value.Emissive == new FalloutNifColor3(.25f, .5f, .75f) && value.Glossiness == 13 && value.Alpha == .8f &&
                value.EmissiveMultiple == (version <= 21 ? 1 : 2.5f), "Legacy material field gates or defaults differ.");
            var unlit = (FalloutNifNoLightingProperty)nif.ReadObject(2);
            Require(unlit.TextureClampMode == 2 && unlit.FileName == "" &&
                unlit.FalloffStartAngle == (version <= 26 ? 1 : .8f) && unlit.FalloffStopAngle == (version <= 26 ? 0 : .2f) &&
                unlit.FalloffStartOpacity == (version <= 26 ? 1 : .75f) && unlit.FalloffStopOpacity == (version <= 26 ? 0 : .25f),
                "Versioned no-lighting falloff bytes or documented defaults differ.");
            Require(nif.ReadObject(5) is FalloutNifSimpleShapePhantom
                { Shape: 4, Filter.Layer: 12, Filter.Flags: 0xa5, Filter.Group: 0x1234,
                    WorldUnused: 0x98765432, BroadPhaseType: 2, BroadPhaseUnused: [4, 5, 6],
                    Property: { Data: 7, Size: 8, CapacityAndFlags: 0x80000009 },
                    Unused: [9, 10, 11, 12, 13, 14, 15, 16], MatrixRowMajor: [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 5, -7, 9, 1] },
                "Shaped phantom discarded its filter, property, transform or reserved bytes.");
            Require(nif.ReadObject(4) is FalloutNifConvexTransformShape
                { Child: 3, Material: 7, Radius: .25f, Unused: [1, 2, 3, 4, 5, 6, 7, 8] } &&
                nif.ReadObject(6) is FalloutNifCollisionObject { Target: 0, Flags: 1, Body: 5, IsBlend: false },
                "Transform shape or phantom attachment lost its source identity.");
            var padded = blocks.ToArray();
            var paddedPhantom = phantom.ToArray();
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(paddedPhantom.AsSpan(48), 0x7fc12345);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(paddedPhantom.AsSpan(64), 0xff800000);
            padded[5] = ("bhkSimpleShapePhantom", paddedPhantom);
            var paddedValue = (FalloutNifSimpleShapePhantom)FalloutNifFile.Read(File(version, padded)).ReadObject(5);
            Require(BitConverter.SingleToUInt32Bits(paddedValue.MatrixRowMajor[3]) == 0x7fc12345 &&
                BitConverter.SingleToUInt32Bits(paddedValue.MatrixRowMajor[7]) == 0xff800000,
                "Havok transform padding was interpreted as a semantic finite matrix lane.");
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(paddedPhantom.AsSpan(36), 0x7fc12345);
            Reject(() => FalloutNifFile.Read(File(version, padded)).ReadObject(5));
            for (var index = 0; index < blocks.Length; index++)
            {
                var malformed = blocks.ToArray();
                malformed[index] = (blocks[index].Item1, blocks[index].Item2[..^1]);
                Reject(() => FalloutNifFile.Read(File(version, malformed)).ReadObject(index));
                malformed[index] = (blocks[index].Item1, [.. blocks[index].Item2, 0]);
                Reject(() => FalloutNifFile.Read(File(version, malformed)).ReadObject(index));
            }
            var wrongWidth = blocks.ToArray(); wrongWidth[0] = ("NiNode", Node(version <= 26 ? 34U : 21U));
            Reject(() => FalloutNifFile.Read(File(version, wrongWidth)).ReadNode(0));
        }
        Reject(() => FalloutNifFile.Read(File(22, [("NiNode", Node(21))])));
        RunShaderLayouts();
        UnsizedNifLayoutContracts.Run();
        Console.WriteLine("OPENNV_LEGACY_NIF_LAYOUT_CONTRACT_PASS streams=14,21,26,30,34 flagsWidths=true materialColors=true falloffGates=true phantomSourcePreserved=true malformedRejected=true");
    }

    private static byte[] Node(uint version) => Bytes(w =>
    {
        Net(w);
        if (version <= 26) w.Write((ushort)0xa50e); else w.Write(0x1234a50eU);
        foreach (var value in new float[] { 10, -20, 30, 1, 0, 0, 0, 1, 0, 0, 0, 1, 1.25f }) w.Write(value);
        w.Write(0U); w.Write(6); w.Write(0U); w.Write(0U);
    });
    private static byte[] Material(uint version) => Bytes(w =>
    {
        Net(w);
        if (version < 26) foreach (var value in new[] { .1f, .2f, .3f, .4f, .5f, .6f }) w.Write(value);
        foreach (var value in new[] { .7f, .8f, .9f, .25f, .5f, .75f, 13, .8f }) w.Write(value);
        if (version > 21) w.Write(2.5f);
    });
    private static byte[] NoLighting(uint version) => Bytes(w =>
    {
        Net(w); w.Write((ushort)1); w.Write(33U); w.Write(0x82000000U); w.Write(1U); w.Write(1f); w.Write(2U); w.Write(0U);
        if (version > 26) foreach (var value in new[] { .8f, .2f, .75f, .25f }) w.Write(value);
    });
    private static void Matrix(BinaryWriter w)
    {
        foreach (var value in new float[] { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 5, -7, 9, 1 }) w.Write(value);
    }
    private static void Net(BinaryWriter w) { w.Write(0); w.Write(0U); w.Write(-1); }
    private static byte[] Bytes(Action<BinaryWriter> write)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        write(writer); return stream.ToArray();
    }
    private static byte[] File(uint version, (string Type, byte[] Payload)[] blocks) => Bytes(w =>
    {
        void Text(string text) { w.Write(text.Length); w.Write(Encoding.ASCII.GetBytes(text)); }
        w.Write(Encoding.ASCII.GetBytes("Gamebryo File Format, Version 20.2.0.7\n"));
        w.Write(FalloutNifFile.Version); w.Write((byte)1); w.Write(FalloutNifFile.UserVersion);
        w.Write(blocks.Length); w.Write(version); w.Write(new byte[] { 1, 0, 1, 0, 1, 0 });
        w.Write((ushort)blocks.Length); foreach (var block in blocks) Text(block.Type);
        for (var index = 0; index < blocks.Length; index++) w.Write((ushort)index);
        foreach (var block in blocks) w.Write(block.Payload.Length);
        w.Write(1U); w.Write(7U); Text("fixture"); w.Write(0U);
        foreach (var block in blocks) w.Write(block.Payload);
        w.Write(1U); w.Write(0);
    });
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Reject(Action read)
    {
        try { read(); } catch (Exception failure) when (failure is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidOperationException("Malformed or unknown legacy NIF layout was admitted.");
    }
}
