namespace OpenNV.Runtime.Content;

internal static partial class FalloutExecutableStringTable
{
    private static void RequirePlayerActionConditionConstructors(byte[] code, Image image, int middleConstructor)
    {
        foreach (var (slot, field) in new[] { (0x400u, 0x228u), (0x450u, 0x135u) })
        {
            foreach (var name in new[] { ".?AVHighProcess@@", ".?AVMiddleHighProcess@@" })
                if (!Match(image.Read(Slot(Class(name), slot), 7), 0, [0x8a, 0x81, (byte)field, (byte)(field >> 8), 0, 0, 0xc3]))
                    throw new NotSupportedException("Player timed-action condition no longer reads its independent source process byte.");
            foreach (var name in new[] { ".?AVBaseProcess@@", ".?AVLowProcess@@", ".?AVMiddleLowProcess@@" })
                if (!Match(image.Read(Slot(Class(name), slot), 3), 0, [0x32, 0xc0, 0xc3]))
                    throw new NotSupportedException("Player low process action condition has an unowned nonzero result.");
        }
        if (!Match(code, middleConstructor + 0x1ae, [0x66, 0xc7, 0x86, 0x34, 1, 0, 0, 0, 0]) ||
            !Match(code, middleConstructor + 0x338, [0xc6, 0x86, 0x28, 2, 0, 0, 0]))
            throw new NotSupportedException("Player timed-action conditions lost their actual inherited constructor stores.");
        uint Class(string name)
        {
            var tables = image.SourceClassTables(name, 281).ToArray();
            return tables.Length == 1 ? tables[0] : throw new NotSupportedException("Player action process class is absent or ambiguous: " + name);
        }
        uint Slot(uint table, uint slot) => Word(image.Read(table + slot, 4), 0);
    }

    private static void RequirePlayerTimedActions(byte[] code, Image image, int caller, int constructor, uint scaledTimer)
    {
        var arm = caller + 0x352;
        if (!Match(code, constructor + 0x364, [0x89, 0x8f, 0x90, 5, 0, 0]) ||
            !Match(code, constructor + 0xb57, [0x66, 0xc7, 0x87, 0xa4, 9, 0, 0, 0, 0]) ||
            !Match(code, arm, [0x8b, 0xcf, 0xe8]) ||
            !Match(code, arm + 7, [0x84, 0xc0, 0x74, 0x32, 0x8b, 0x4f, 0x60, 0x8b, 1, 0x8b, 0x80, 0x50, 4, 0, 0, 0xff, 0xd0, 0x84, 0xc0, 0x74, 0x21, 0x6a, 9, 0x8b, 0xcf, 0xe8]) ||
            !Match(code, Call(arm + 2), [0x8b, 0x49, 0x60, 0x85, 0xc9, 0x74, 0x0a, 0x8b, 1, 0x8b, 0x80, 0, 4, 0, 0, 0xff, 0xe0, 0x32, 0xc0, 0xc3]))
            throw new NotSupportedException("Player timed actions changed independent constructor fields or short-circuit condition order.");
        var query = Call(arm + 0x20);
        var produce = Call(arm + 0x36);
        var remove = Call(arm + 0x43);
        if (!Match(code, arm + 0x3d, [0x6a, 0, 0x6a, 9, 0x8b, 0xcf, 0xe8]) ||
            !Match(code, arm + 0x25, [0x85, 0xc0, 0x75, 0x1f, 0x50, 0x51, 0xc7, 4, 0x24, 0, 0, 0xa0, 0x41, 0x8b, 0xcf, 0x6a, 9, 0xe8]) ||
            !Match(code, query, [0x55, 0x8b, 0xec, 0x8b, 0x89, 0x90, 5, 0, 0, 0x33, 0xc0, 0x85, 0xc9]) ||
            !Match(code, produce + 9, [0x80, 0xbf, 0xa4, 9, 0, 0, 0]) ||
            !Match(code, produce + 0xae, [0xf3, 0x0f, 0x10, 0x45, 0x0c, 0x89, 0x1e, 0x8b, 0x5d, 0x10, 0xf3, 0x0f, 0x11, 0x46, 4, 0x85, 0xdb]) ||
            !Match(code, remove + 0x23, [0x8b, 0x89, 0x90, 5, 0, 0, 0x85, 0xc9]) ||
            !Match(code, remove + 0x3c, [0x39, 0x16, 0x75, 9, 0x85, 0xc0, 0x74, 0x1e, 0x3b, 0x46, 8, 0x74, 0x19]))
            throw new NotSupportedException("Player timed action kind/time/context producer, first query or wildcard removal changed.");
        var prepend = Call(produce + 0xd7);
        if (!Match(code, prepend + 0xa, [0x8b, 7, 0x85, 0xc0, 0x74, 0x47, 0x83, 0x3e, 0, 0x74, 0x40]) ||
            !Match(code, prepend + 0x35, [0x8b, 0x46, 4, 0x89, 0x42, 4, 0x89, 0x56, 4, 0x8b, 7, 0x5f, 0x89, 6]))
            throw new NotSupportedException("Player timed action insertion lost its actual prepend order.");
        if (!Match(code, caller + 0x3b9, [0x8b, 0xc1, 0x0f, 0x57, 0xc0, 0x0f, 0x2f, 0x40, 4, 0x0f, 0x82]) ||
            !Match(code, caller + 0x3c8, [0x8b, 8, 0x83, 0xf9, 5, 0x0f, 0x84]) ||
            !Match(code, caller + 0x3d3, [0x83, 0xf9, 8, 0x0f, 0x84]) ||
            !Match(code, caller + 0x463, [0x8b, 0xb7, 0x90, 5, 0, 0, 0x8b, 0xd6]) ||
            !Match(code, caller + 0x48c, [0x8b, 8, 0x85, 0xc9, 0x74, 0x19, 0xf3, 0x0f, 0x10, 0x41, 4, 0xf3, 0x0f, 0x5c, 5]) ||
            Word(code, caller + 0x49b) != scaledTimer ||
            !Match(code, caller + 0x49f, [0xf3, 0x0f, 0x11, 0x41, 4, 0x8b, 0x40, 4, 0x85, 0xc0, 0x75, 0xe1]))
            throw new NotSupportedException("Player timed actions changed ordered expiry/protected kinds, restart or actual Main timer subtraction.");
        int Call(int at)
        {
            if (code[at] != 0xe8) throw new NotSupportedException("Player timed-action child has no actual source call.");
            return checked(at + 5 + System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(at + 1)));
        }
    }
}
