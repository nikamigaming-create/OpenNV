namespace OpenNV.Runtime.Content;

internal static partial class FalloutExecutableStringTable
{
    private static void RequireStatusBaseAssociations(byte[] code, Image image, uint player, int basis, int registration)
    {
        if (!Match(code, basis + 0x3e, [0x8b, 0x80, 0x7c, 4, 0, 0]) ||
            !Match(code, basis + 0x49, [0xff, 0xd0, 0x80, 0x7d, 0x0b, 0, 0xd9, 0x5d, 0xf4, 0x74, 0x1e,
                0x8d, 0x46, 0xe0, 0x83, 0xf8, 0x0d, 0x76, 0x16, 0xf3, 0x0f, 0x10, 0x45, 0xf4]) ||
            code[basis + 0x104] != 0xe8 || !Match(code, basis + 0x10c, [0x84, 0xc0, 0x74, 0xb5]))
            throw new NotSupportedException("Status base getter lost its independent override presence or formula fallback.");
        var descriptorTable = Word(code, basis + 0x85);
        if (!Match(code, registration + 0x25, [0x8b, 0x5d, 8, 0x83, 0x3c, 0x9d]) ||
            Word(code, registration + 0x2b) != descriptorTable ||
            !Match(code, registration + 0xb3, [0x89, 0x3c, 0x9d]) || Word(code, registration + 0xb6) != descriptorTable)
            throw new NotSupportedException("Status defaults and formula reads do not select the actual named descriptor array.");
        var formula = Child(basis + 0x104);
        if (!Match(code, formula, [0x55, 0x8b, 0xec, 0x8b, 0x4d, 0x0c, 0x32, 0xd2, 0x56, 0x8b, 0x75, 0x10,
                0xc7, 6, 0, 0, 0, 0, 0x83, 0xf9, 0x48, 0x77, 0x26, 0x8b, 4, 0x8d]) ||
            Word(code, formula + 0x1a) != descriptorTable ||
            !Match(code, formula + 0x1e, [0x85, 0xc0, 0x74, 0x1b, 0x8b, 0x40, 0x4c, 0x89, 0x45, 0x10,
                0x85, 0xc0, 0x74, 0x11]) || !Match(code, formula + 0x3d, [0x8a, 0xc2, 0x5e, 0x5d, 0xc3]))
            throw new NotSupportedException("Status formula field lost its source null-function return and independent output.");
        var dispatch = Offset(Word(image.Read(player + 0x47c, 4), 0));
        if (!Match(code, dispatch, [0x55, 0x8b, 0xec, 0x5d, 0xe9]))
            throw new NotSupportedException("Player status override lost its actual Actor virtual dispatch.");
        var actor = Child(dispatch + 4);
        if (!Match(code, actor, [0x55, 0x8b, 0xec, 0x81, 0xc1, 0xd4, 0, 0, 0, 0x5d, 0xe9]))
            throw new NotSupportedException("Actor status override changed its separate mutable owner.");
        var query = Child(actor + 0xa);
        if (!Match(code, query, [0x55, 0x8b, 0xec, 0xff, 0x75, 8, 0xe8]) ||
            !Match(code, query + 0xb, [0x8b, 0x4d, 0x0c, 0x85, 0xc0, 0x0f, 0x95, 0xc2, 0x88, 0x11, 0x85, 0xc0,
                0x74, 0x11, 0xf3, 0x0f, 0x10, 0x40, 4]) ||
            !Match(code, query + 0x2a, [0xc7, 0x45, 8, 0, 0, 0, 0, 0xd9, 0x45, 8, 0x5d, 0xc2, 8, 0]))
            throw new NotSupportedException("Status override query conflated keyed presence with its Float32 value.");
        var lookup = Child(query + 6);
        if (!Match(code, lookup + 4, [0x8a, 0x5d, 8, 0x33, 0xd2, 0x56, 0x8b, 0x71, 0x0c]) ||
            !Match(code, lookup + 0x30, [0x8b, 1, 0x85, 0xc0, 0x74, 0x10, 0x85, 0xd2, 0x75, 0x0c,
                0x38, 0x18, 0x8b, 0x49, 4, 0x0f, 0x44, 0xd0, 0x85, 0xc9, 0x75, 0xea]))
            throw new NotSupportedException("Status override lost its actual keyed mutable lookup.");
        int Offset(uint address) => SourceOffset(code, image.CodeBase, address);
        int Child(int at) => checked(at + 5 + System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(at + 1)));
    }
}
