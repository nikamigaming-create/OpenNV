using System.Buffers.Binary;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.FaceGen;

internal static class FaceControlContracts
{
    internal static void Run()
    {
        var npc = Floats(1, 2); var race = Floats(3, -1);
        Require(FalloutFaceGenControls.Project(npc, race, [2, 3]) == 11, "Projection must use NPC plus race without normalizing the axis.");
        var edited = FalloutFaceGenControls.SetControl(npc, race, [2, 3], 12);
        Require(edited.SequenceEqual(Floats(3, 5)), "Setter must use the true projection, retain source axis magnitude and subtract race again.");
        FalloutCtlAffineAxis[] axes = [new([1, 1], 10), new([1, -2], -2)];
        var attributed = FalloutFaceGenControls.SetAttribute(npc, race, axes, 0, 21);
        Require(Math.Abs(FalloutFaceGenControls.Attribute(attributed, race, axes[0]) - 21) < 1e-5 &&
            Math.Abs(FalloutFaceGenControls.Attribute(attributed, race, axes[1]) - FalloutFaceGenControls.Attribute(npc, race, axes[1])) < 1e-5,
            "An affine edit must preserve the other attribute on non-orthogonal axes.");
        Reject(() => FalloutFaceGenControls.SetControl(npc, race, [1], 0));
        Reject(() => FalloutFaceGenControls.SetControl(npc, race, [1, float.NaN], 0));
        Reject(() => FalloutFaceGenControls.SetAttribute(npc, race, [axes[0], axes[0]], 0, 0));
        var settings = new Dictionary<uint, string> { [100] = "sRSMShapeOption01", [200] = "sArbitraryShape", [300] = "sArbitraryTone", [400] = "fUnusualMin:Interface" };
        using var data = new MemoryStream(); using var writer = new BinaryWriter(data);
        void Store(byte opcode, byte mod, int at) { writer.Write(opcode); writer.Write(mod); writer.Write(at); }
        void Immediate(int at, int value) { Store(0xc7, 0x85, at); writer.Write(value); }
        void Setting(uint value) { writer.Write((byte)0xb9); writer.Write(value); writer.Write((byte)0xe8); writer.Write(0); }
        void Row(int at, int page, uint label)
        {
            Immediate(at, page);
            if (label == 0) Immediate(at + 4, 0); else { Setting(label); Store(0x89, 0x85, at + 4); }
            Setting(400); Store(0xd9, 0x9d, at + 8);
            writer.Write((byte)0xd9); writer.Write((byte)0x05); writer.Write(900U); Store(0xd9, 0x9d, at + 12);
        }
        Row(-1000, 12, 100); Row(-984, 20, 0); Row(-968, 18, 200);
        Row(-2000, 19, 300);
        writer.Write(new byte[] { 0x6a, 0, 0x6a, 0, 0x6a, 1, 0x8d, 0x85 }); writer.Write(-3000);
        writer.Write((byte)0x50); writer.Write((byte)0xe8); writer.Write(0);
        writer.Write(new byte[] { 0x8b, 0x4d, 0xf4, 0x64, 0x89, 0x0d, 0, 0, 0, 0 });
        var code = data.ToArray();
        var table = FalloutExecutableStringTable.ReadFaceControls(code, settings, key => key == 900 ? 7 : throw new InvalidDataException());
        Require(table.Controls.Count == 3 && table.Controls[1].Index == 2 && table.Controls[1].Page == 18 && table.Controls[2].Group == 2 &&
            table.Controls[0].Minimum.Resolve(_ => -6) == -6 && table.Controls[0].Maximum.Resolve(_ => -6) == 7 && table.TextureOrder.SequenceEqual(new[] { 0 }),
            "Compiled table must retain hidden indices, arbitrary labels, source limits, page association and texture order.");
        Reject(() => FalloutExecutableStringTable.ReadFaceControls(code[..^10], settings, _ => 7));
        Reject(() => FalloutExecutableStringTable.ReadFaceControls(code.Concat(code).ToArray(), settings, _ => 7));
        Console.WriteLine("OPENNV_FACE_CONTROL_EDIT_PASS combinedSource=true sourceAxisMagnitude=true affineCoupling=true sourceMenuIndices=true sourceLimits=true malformedFails=true");
    }
    private static byte[] Floats(params float[] values)
    {
        var result = new byte[values.Length * 4];
        for (var i = 0; i < values.Length; i++) BinaryPrimitives.WriteSingleLittleEndian(result.AsSpan(i * 4), values[i]);
        return result;
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
    private static void Reject(Action action) { try { action(); } catch (Exception e) when (e is InvalidDataException or NotSupportedException) { return; } throw new Exception("Invalid control input was admitted."); }
}
