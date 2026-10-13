namespace OpenNV.Runtime.Content;

internal static partial class FalloutExecutableStringTable
{
    private static void RequirePlayerControlOrientation(byte[] code, Image image, int caller, int ordinary)
    {
        if (!Match(code, caller + 0x38, [0x8b, 0xcf, 0xe8]) ||
            !Match(code, caller + 0x3f, [0x80, 0x3d]) || code[caller + 0x45] != 0 ||
            !Match(code, caller + 0x46, [0xd9, 0x1d]) ||
            !Match(code, caller + 0x4c, [0x75, 0x0d, 0x80, 0x3d]) ||
            !Match(code, caller + 0x54, [0, 0x0f, 0x84]) ||
            !Match(code, Call(caller + 0x3a), [0xd9, 0x41, 0x20, 0xc3]) ||
            image.ReadLoaderData(Word(code, caller + 0x41), 1)[0] != 0)
            throw new NotSupportedException("Player controls changed their raw reference pitch getter or independent loaded camera-context byte.");
        if (!Match(code, ordinary + 0x51, [0x80, 0x3d]) || code[ordinary + 0x57] != 0 ||
            Word(code, caller + 0x50) != Word(code, ordinary + 0x53) ||
            image.ReadLoaderData(Word(code, caller + 0x50), 1)[0] != 0)
            throw new NotSupportedException("Player camera routing changed its independent loaded controller-context owner.");
        var tables = image.SourceClassTables(".?AVPlayerCharacter@@", 191).ToArray();
        if (tables.Length != 1) throw new NotSupportedException("Player yaw has no unique source virtual table.");
        if (!Match(image.Read(Word(image.Read(tables[0] + 0x100, 4), 0), 3), 0, [0xb0, 1, 0xc3]))
            throw new NotSupportedException("Player yaw lost its actual Character predicate.");
        var yaw = SourceOffset(code, image.CodeBase, Word(image.Read(tables[0] + 0x2bc, 4), 0));
        if (!Match(code, yaw, [0x55, 0x8b, 0xec, 0x51, 0x56, 0x6a, 0, 0x8b, 0xf1, 0xe8]) ||
            !Match(code, yaw + 0xe, [0xd9, 0x5d, 0xfc, 0xf3, 0x0f, 0x10, 0x45, 0xfc,
                0xf3, 0x0f, 0x58, 0x86, 0x40, 6, 0, 0, 0x51, 0xf3, 0x0f, 0x11, 4, 0x24, 0xe8]) ||
            !Match(code, yaw + 0x29, [0x83, 0xc4, 4, 0x5e, 0x8b, 0xe5, 0x5d, 0xc2, 4, 0]))
            throw new NotSupportedException("Player yaw changed its base getter, independent offset or Float32 normalization.");
        var reference = Call(yaw + 9); var normalize = Call(yaw + 0x24);
        if (!Match(code, reference, [0x55, 0x8b, 0xec, 0x83, 0xec, 8, 0x80, 0x7d, 8, 0,
                0x56, 0x8b, 0xf1, 0xf3, 0x0f, 0x10, 0x46, 0x28]) ||
            !Match(code, reference + 0x1c, [0x75, 0x35, 0x8b, 6, 0x8b, 0x80, 0, 1, 0, 0, 0xff, 0xd0,
                0x84, 0xc0, 0x74, 0x27, 0x8b, 0x4e, 0x1c, 0xe8]) ||
            !Match(code, reference + 0x34, [0x84, 0xc0, 0x74, 0x1b, 0xf3, 0x0f, 0x10, 0x45, 0xf8,
                0xf3, 0x0f, 0x58, 0x46, 0x24, 0x51, 0xf3, 0x0f, 0x11, 4, 0x24, 0xe8]) ||
            Call(reference + 0x48) != normalize ||
            !Match(code, Call(reference + 0x2f), [0x80, 0x79, 4, 0x2b, 0x75, 0x0d, 0x8b, 0x41, 0x34,
                0xc1, 0xe8, 0x17, 0xa8, 1, 0x74, 3, 0xb0, 1, 0xc3, 0x32, 0xc0, 0xc3]))
            throw new NotSupportedException("Reference yaw lost independent current rotation fields or exact humanoid tilt-flag predicate.");
        if (!Match(code, normalize, [0x55, 0x8b, 0xec, 0xf3, 0x0f, 0x10, 0x4d, 8, 0x0f, 0x57, 0xc0,
                0x83, 0xec, 8, 0x0f, 0x2f, 0xc1, 0x76, 0x15, 0xf3, 0x0f, 0x58, 0x0d]) ||
            !Match(code, normalize + 0x1b, [0x0f, 0x5a, 0xc1, 0xf2, 0x0f, 0x11, 0x45, 0xf8, 0xdd, 0x45, 0xf8,
                0xeb, 0x14, 0x0f, 0x2f, 0x0d]) ||
            Word(code, normalize + 0x17) != Word(code, normalize + 0x2b) ||
            Word(image.Read(Word(code, normalize + 0x17), 4), 0) != Gameplay.State.FalloutPlayerControlOrientation.TwoPiBits ||
            !Match(code, normalize + 0x2f, [0x72, 0x19, 0x0f, 0x5a, 0xc1, 0xf2, 0x0f, 0x11, 0x45, 0xf8,
                0xdd, 0x45, 0xf8, 0xdd, 5]) ||
            System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(image.Read(Word(code, normalize + 0x3e), 8)) != 0x401921fb60000000 ||
            !Match(code, normalize + 0x42, [0xe8]) ||
            !Match(code, normalize + 0x47, [0xd9, 0x5d, 8, 0xd9, 0x45, 8, 0x8b, 0xe5, 0x5d, 0xc3]))
            throw new NotSupportedException("Player yaw normalization changed its ordered Float32 add or signed Float64 remainder operands.");
        int Call(int at)
        {
            if (code[at] != 0xe8) throw new NotSupportedException("Player orientation getter lost its actual direct child.");
            return checked(at + 5 + System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(at + 1)));
        }
    }
}
