using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

internal static partial class FalloutExecutableStringTable
{
    private static void RequirePlayerModelLoadTail(byte[] code, Image image, int load)
    {
        var worldGetter = Call(load + 0x58c);
        Need(worldGetter, [0x8b, 0x49, 0x60, 0x85, 0xc9], "actual current process before the following controller read");
        Need(load + 0x5b3, [0x8b, 0x49, 8, 0x85, 0xc9], "actual current phantom world before model registration");
        var refresh = Call(load + 0x611);
        Need(refresh, [0x8b, 0x0d], "actual independent Pip-Boy singleton");
        Need(refresh + 0xa, [0x80, 0x39, 0], "current singleton first byte refresh predicate");
        Need(refresh + 0xd, [0x0f, 0x85], "active refresh is an independent source child");
        var singleton = Word(code, refresh + 2); var publications = new List<int>();
        for (var at = 0x2f; at <= code.Length - 9; ++at)
            if (code[at] == 0xa3 && Word(code, at + 1) == singleton && Match(code, at - 0x2f, [0x68, 0x54, 5, 0, 0]) &&
                code[at - 0x10] == 0xe8 && Match(code, at + 5, [0x8b, 0x4d, 8])) publications.Add(at);
        if (publications is not [var publication])
            throw new NotSupportedException("Player loader lost its actual Pip-Boy singleton factory publication.");
        var constructor = Call(publication - 0x10);
        Need(constructor + 0x142, [0x66, 0xc7, 7, 0, 0], "same actual manager first flag constructor zero");
        Need(load + 0x616, [0x80, 0xbf, 0xa, 7, 0, 0, 0], "current independent toddler flag");
        Need(load + 0x61d, [0x74, 0x1e], "inactive toddler skips the activation child");
        Need(load + 0x657, [0x89, 0xb7, 0x80, 2, 0, 0], "actual saved outer kind restoration after all reached children");

        int Call(int at) => code[at] == 0xe8 ? checked(at + 5 + BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(at + 1))) :
            throw new NotSupportedException("Player model-load tail lost an actual direct child.");
        void Need(int at, byte[] expected, string role)
        { if (!Match(code, at, expected)) throw new NotSupportedException("Player model-load tail changed its " + role + "."); }
    }
}
