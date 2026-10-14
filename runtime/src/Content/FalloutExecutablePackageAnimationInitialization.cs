using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

internal static partial class FalloutExecutableStringTable
{
    private static void RequirePackageAnimationConfiguredStart(byte[] code, Image image)
    {
        var declarations = ReadStaticSettings(image);
        uint Value(string name) => declarations.Where(row => row.Collection == SettingCollection.Main &&
            row.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray() is [var declaration] && declaration.Descriptor != 0
            ? checked(declaration.Descriptor + 4) : throw new NotSupportedException("Player animation startup lacks its actual typed INI declaration.");
        var cell = Value("sStartingCell:General");
        var x = Value("sStartingCellX:General");
        var entries = Enumerable.Range(0, code.Length - 0x310).Where(at => Match(code, at,
            [0x55, 0x8b, 0xec, 0x8b, 0x55, 8, 0x83, 0xec, 8, 0x53, 0x56, 0x8b, 0x35]) && Word(code, at + 13) == cell).ToArray();
        if (entries is not [var entry]) throw new NotSupportedException("Player animation configured-start selector is absent or ambiguous.");
        Need(entry + 0x39, [0x0f, 0x84], "empty configured CELL branch");
        if (Conditional(entry + 0x39) != entry + 0x100)
            throw new NotSupportedException("Player animation startup changed its empty CELL grid selector.");
        Need(entry + 0x100, [0x8b, 0x15], "independent configured grid-X read");
        if (Word(code, entry + 0x102) != x)
            throw new NotSupportedException("Player animation startup borrowed another INI field for grid X.");
        Need(entry + 0x110, [0x8a, 1, 0x41, 0x84, 0xc0, 0x75, 0xf9, 0x2b, 0xce, 0x0f, 0x84], "grid-X string extent before CELL lookup");
        var returned = Conditional(entry + 0x119);
        Need(returned, [0x5f, 0x5e, 0x5b, 0x8b, 0xe5, 0x5d, 0xc3], "genuine empty-grid return");
        var callers = Enumerable.Range(0, code.Length - 10).Where(at => Match(code, at, [0x6a, 0, 0xe8]) &&
            at + 7 + BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(at + 3)) == entry && Match(code, at + 7, [0xf3, 0x0f, 0x7e])).ToArray();
        if (callers.Length != 1)
            throw new NotSupportedException("Player animation startup lacks its actual null explicit CELL caller.");

        int Conditional(int at) => checked(at + 6 + BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(at + 2)));
        void Need(int at, byte[] bytes, string role)
        { if (!Match(code, at, bytes)) throw new NotSupportedException("Player animation startup changed its original " + role + "."); }
    }
}
