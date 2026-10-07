using System.Buffers.Binary;
using OpenNV.Runtime.Content;

internal static class PooledFaceControlContracts
{
    internal static void Run()
    {
        var settings = new Dictionary<uint, string>
        {
            [100] = "sRSMShapeOption01", [120] = "sOtherShape", [140] = "sToneA", [160] = "sToneB",
            [200] = "fOwnedMinimum", [220] = "fOwnedMaximum",
        };
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        void Bytes(params byte[] bytes) => writer.Write(bytes);
        void Absolute(uint address) { Bytes(0xa1); writer.Write(address); }
        void Scalar(byte opcode, byte register, int address)
        { Bytes(0xf3, 0x0f, opcode, register); writer.Write(address); }
        void Immediate(int frame, int value) { Bytes(0xc7, 0x85); writer.Write(frame); writer.Write(value); }
        void Store(byte register, int frame) { Bytes(0x89, (byte)(0x85 + register * 8)); writer.Write(frame); }
        void Call(int target) { Bytes(0xe8); writer.Write(target - checked((int)stream.Position + 4)); }
        void Lea(int frame) { Bytes(0x8d, 0x85); writer.Write(frame); }
        Absolute(104); Scalar(0x10, 0x0d, 204); Scalar(0x10, 0x15, 224);
        // Pool and reorder the assignments instead of mirroring a row emitter.
        Bytes(0x89, 0xc3); Absolute(124); Store(0, -964); Store(3, -996);
        Scalar(0x11, 0x8d, -1300); Scalar(0x11, 0x95, -1304);
        Absolute(144); Store(0, -1400); Absolute(164); Store(0, -1404);
        Immediate(-968, 11); Scalar(0x11, 0x8d, -960); Scalar(0x11, 0x95, -956);
        var hiddenPage = checked((int)stream.Position + 6);
        Immediate(-984, 20); Immediate(-980, 0); Immediate(-976, 0); Immediate(-972, 0);
        Immediate(-1000, 5); Scalar(0x11, 0x8d, -992); Scalar(0x11, 0x95, -988);
        Call(8000);
        Bytes(0x8d, 0xb5); writer.Write(-996);
        Bytes(0x57, 0x6a, 0); Lea(-1500); Bytes(0x6a, 0, 0x50); Call(9000);
        Bytes(0x83, 0xc6, 16, 0x8b, 0x4c, 0x8b, 0x34);
        void Tone(byte index, byte? previous, int label)
        {
            Bytes(0x6a, index, 0x6a, 0);
            if (previous is { } old) { Bytes(0xc7, 0x40, 8); writer.Write((uint)old); }
            Lea(-1500); Bytes(0x6a, 1, 0x50); Call(9000);
            // The displacement's low byte is E8; it is data, not a call.
            Scalar(0x5d, 0x85, -1304); Bytes(0x8b, 0x4e, 0x44);
            Scalar(0x5f, 0x85, -1300); Bytes(0xff, 0xb5); writer.Write(label); Call(10000);
        }
        Tone(7, null, -1400); Tone(3, 7, -1404);
        Bytes(0x8b, 0x4d, 0xf4, 0x64, 0x89, 0x0d, 0, 0, 0, 0);
        var code = stream.ToArray();
        FalloutFaceControlTable Read(byte[] bytes) => FalloutExecutableStringTable.ReadFaceControls(bytes, settings, _ => throw new InvalidDataException("Foreign constant."));
        var table = Read(code);
        Require(table.GeometryCount == 3 && table.Controls.Count == 4 && table.Controls[1].Index == 2 &&
            table.Controls[0].Page == 5 && table.Controls[1].Page == 11 && table.TextureOrder.SequenceEqual(new[] { 7, 3 }) &&
            table.Controls[2].Page == 4 && table.Controls[2].Setting == "sToneA" && table.Controls[3].Setting == "sToneB" &&
            table.Controls.All(row => row.Minimum.Setting == "fOwnedMinimum" && row.Maximum.Setting == "fOwnedMaximum"),
            "Pooled declarations must retain reordered fields, hidden indices, actual pages and shared cached limits.");
        var broken = code.ToArray(); BinaryPrimitives.WriteInt32LittleEndian(broken.AsSpan(hiddenPage), 0); Reject(() => Read(broken));
        broken = code.ToArray(); BinaryPrimitives.WriteInt32LittleEndian(broken.AsSpan(hiddenPage - 4), -983); Reject(() => Read(broken));
        settings[200] = "sForeignScalar"; Reject(() => Read(code)); settings[200] = "fOwnedMinimum";
        settings.Remove(140); Reject(() => Read(code)); settings[140] = "sToneA";
        Reject(() => Read(code[..^10])); Reject(() => Read(code.Concat(code).ToArray()));
        Headers();
        Console.WriteLine("OPENNV_POOLED_FACE_CONTROL_PASS reorderedAssignments=true registerCopies=true cachedLimits=true hiddenExtent=true sourceTonePages=true sourceToneOrder=true operandBoundaries=true malformedRefused=true");
    }

    private static void Headers()
    {
        var settings = new Dictionary<uint, string>(); var code = new byte[128];
        for (var index = 0; index < 16; index++)
        {
            settings[(uint)(100 + index * 12)] = index == 0 ? "sRSMCustomize" : "sSourceHeader" + index;
            var at = index * 8; code[at] = 0xa1; BinaryPrimitives.WriteUInt32LittleEndian(code.AsSpan(at + 1), (uint)(104 + index * 12));
            code[at + 5] = 0x89; code[at + 6] = 0x45; code[at + 7] = unchecked((byte)(-100 + index * 4));
        }
        var result = FalloutExecutableStringTable.ReadCreationHeaders(code, settings);
        Require(result.Count == 20 && result[4] == "sRSMCustomize" && result[^1] == "sSourceHeader15", "Direct typed payloads must retain all page headers in frame order.");
        code[15]++; Reject(() => FalloutExecutableStringTable.ReadCreationHeaders(code, settings)); code[15]--;
        Reject(() => FalloutExecutableStringTable.ReadCreationHeaders(code[..^1], settings));
        Reject(() => FalloutExecutableStringTable.ReadCreationHeaders(code.Concat(code).ToArray(), settings));
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidDataException("Invalid pooled control declaration was admitted.");
    }
}
