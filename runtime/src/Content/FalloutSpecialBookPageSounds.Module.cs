using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutSpecialBookPageSoundModuleAssociation(uint Buffer, int Capacity, uint Writer,
    uint ForwardCall, uint BackwardCall, string EditorId);

internal static partial class FalloutExecutableStringTable
{
    internal static FalloutSpecialBookPageSoundModuleAssociation? ReadSpecialBookPageSoundModule(
        FalloutSpecialBookPageSoundsSource source, byte[] plugin, uint loadRva)
    {
        source.CheckSame(ReadSpecialBookPageSounds(source.RuntimePath)); var (code, image) = Load(plugin);
        var load = checked(image.Base + loadRva);
        return ReadSpecialBookPageSoundModuleAssociation(code, image.CodeBase, load, source,
            image.IsReadableDataExtent, image.Literal, image.Read, image.ImportName);
    }

    internal static FalloutSpecialBookPageSoundModuleAssociation? ReadSpecialBookPageSoundModuleAssociation(byte[] code,
        uint codeBase, uint load, FalloutSpecialBookPageSoundsSource source, Func<uint, int, bool> readable,
        Func<uint, string?> literal, Func<uint, int, byte[]> read, Func<uint, string?> import)
    {
        byte[] Operand(uint value)
        { var bytes = new byte[5]; bytes[0] = 0xb9; BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(1), value); return bytes; }
        var hasForward = code.AsSpan().IndexOf(Operand(source.Input(FalloutSpecialBookPageDirection.Forward).Operand)) >= 0;
        var hasBackward = code.AsSpan().IndexOf(Operand(source.Input(FalloutSpecialBookPageDirection.Backward).Operand)) >= 0;
        if (!hasForward && !hasBackward) return null;
        if (!hasForward || !hasBackward) throw new NotSupportedException("Book source module has an incomplete independent page input pair.");
        var rows = InterfaceSoundInstructions(code, codeBase, load); var boundaries = rows.Select(row => row.At).ToHashSet();
        var candidates = new List<FalloutSpecialBookPageSoundModuleAssociation>();
        foreach (var row in rows)
        {
            var at = row.At;
            if (code.Length - at < 29 || row.Opcode != 0xbe) continue;
            var body = code.AsSpan(at, 29);
            if (body[5] != 0xb9 || !body.Slice(10, 2).SequenceEqual(new byte[] { 0x8b, 0xd6 }) || body[12] != 0xe8 ||
                !body.Slice(17, 2).SequenceEqual(new byte[] { 0x8b, 0xd6 }) || body[19] != 0xb9 || body[24] != 0xe8 ||
                U32(body, 6) != source.Input(FalloutSpecialBookPageDirection.Forward).Operand ||
                U32(body, 20) != source.Input(FalloutSpecialBookPageDirection.Backward).Operand) continue;
            if (new[] { 5, 10, 12, 17, 19, 24 }.Any(offset => !boundaries.Contains(at + offset)))
                throw new InvalidDataException("Book module sound pair enters a source instruction operand.");
            var writer = checked(at + 17 + BinaryPrimitives.ReadInt32LittleEndian(body[13..]));
            var secondWriter = checked(at + 29 + BinaryPrimitives.ReadInt32LittleEndian(body[25..]));
            if (writer != secondWriter) throw new NotSupportedException("Book sound directions changed their actual shared DWORD writer.");
            _ = ReadSourceCStringPointerWriter(code, writer, import);
            var buffer = U32(body, 1); var value = literal(buffer) ?? throw new InvalidDataException("Book module has no actual source CString.");
            RequireSoundEditorId(value); var capacity = checked(value.Length + 1);
            if (!readable(buffer, capacity) || ReadSoundEditorIdBytes(read(buffer, capacity)) != value)
                throw new InvalidDataException("Book module sound has no complete readable non-executable source data extent.");
            candidates.Add(new(buffer, capacity, checked(codeBase + (uint)writer), checked(codeBase + (uint)at + 12),
                checked(codeBase + (uint)at + 24), value));
        }
        if (candidates.Count > 1) throw new InvalidDataException("Book page source pair has multiple original module producers.");
        return candidates.SingleOrDefault() ?? throw new NotSupportedException("Book input operands have no admitted original Load pair.");
    }

    // The helper has no result predicate between its real first SDK return and
    // the DWORD store. Preserve that failed prefix, the same output cell and
    // second real SDK call; no authored store substitutes for the original.
    internal static uint ReadSourceCStringPointerWriter(ReadOnlySpan<byte> code, int writer, Func<uint, string?> import)
    {
        if (writer < 0 || writer > code.Length - 47) throw new InvalidDataException("Source pointer helper leaves its actual code extent.");
        var body = code.Slice(writer, 47);
        if (!body[..6].SequenceEqual(new byte[] { 0x55, 0x8b, 0xec, 0x51, 0x53, 0x57 }) ||
            !body.Slice(6, 7).SequenceEqual(new byte[] { 0x8d, 0x45, 0xfc, 0x8b, 0xd9, 0x50, 0x6a }) ||
            body[13] != 0x40 || !body.Slice(14, 7).SequenceEqual(new byte[] { 0x6a, 4, 0x53, 0x8b, 0xfa, 0xff, 0x15 }) ||
            !body.Slice(25, 5).SequenceEqual(new byte[] { 0x8d, 0x45, 0xfc, 0x89, 0x3b }) ||
            !body.Slice(30, 7).SequenceEqual(new byte[] { 0x50, 0xff, 0x75, 0xfc, 0x6a, 4, 0x53 }) ||
            !body.Slice(37, 2).SequenceEqual(new byte[] { 0xff, 0x15 }) ||
            U32(body, 21) != U32(body, 39) || import(U32(body, 21)) != "VirtualProtect" ||
            !body.Slice(43, 4).SequenceEqual(new byte[] { 0x5f, 0x5b, 0xc9, 0xc3 }))
            throw new NotSupportedException("Source pointer helper changed its original SDK/store/restore ABI and order.");
        return U32(body, 21);
    }
}
