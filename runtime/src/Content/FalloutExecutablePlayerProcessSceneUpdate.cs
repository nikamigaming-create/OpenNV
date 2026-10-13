namespace OpenNV.Runtime.Content;

internal static partial class FalloutExecutableStringTable
{
    private static void RequirePlayerProcessSceneUpdate(byte[] code, Image image, int middleConstructor)
    {
        var high = Class(".?AVHighProcess@@", 286);
        var player = Class(".?AVPlayerCharacter@@", 191);
        foreach (var name in new[] { ".?AVBaseProcess@@", ".?AVLowProcess@@", ".?AVMiddleLowProcess@@", ".?AVMiddleHighProcess@@" })
            if (!Match(image.Read(Slot(Class(name, 281), 0x460), 3), 0, [0xc2, 4, 0]))
                throw new NotSupportedException("Non-High scene update no longer returns without children.");
        if (!Match(image.Read(Slot(high, 0x474), 7), 0, [0x8a, 0x81, 0x8c, 1, 0, 0, 0xc3]) ||
            !Match(image.Read(Slot(high, 0x46c), 8), 0, [0xc6, 0x81, 0x8c, 1, 0, 0, 0, 0xc3]) ||
            !Match(image.Read(Slot(player, 0x21c), 3), 0, [0x32, 0xc0, 0xc3]) ||
            !Match(code, middleConstructor + 0x2d1, [0x66, 0xc7, 0x86, 0x8c, 1, 0, 0, 0, 0]))
            throw new NotSupportedException("Player High scene update lost its independent inherited mask constructor/getter/clear or actual Player predicate.");
        var update = SourceOffset(code, image.CodeBase, Slot(high, 0x460));
        RequirePlayerActionConditionConstructors(code, image, middleConstructor);
        if (!Match(code, update + 0xd, [0x8b, 3, 0x8b, 0x80, 0x74, 4, 0, 0, 0xff, 0xd0]) ||
            !Match(code, update + 0x1e, [0x8b, 0x92, 0x1c, 2, 0, 0, 0xff, 0xd2, 0x84, 0xc0]) ||
            !Match(code, update + 0x2e, [0x38, 0x45, 0x0b, 0x0f, 0x84, 0xd5, 2, 0, 0]) ||
            !Match(code, update + 0x30c, [0x8b, 3, 0x8b, 0xcb, 0xff, 0x90, 0x6c, 4, 0, 0, 0x3b, 0x3d]) ||
            !Match(code, update + 0x31c, [0x74, 9, 0x6a, 0, 0x8b, 0xcf, 0xe8]))
            throw new NotSupportedException("High scene zero-mask branch changed getter/predicate/clear order or Player-only skip.");
        uint Class(string name, int slots)
        {
            var tables = image.SourceClassTables(name, slots).ToArray();
            return tables.Length == 1 ? tables[0] : throw new NotSupportedException("Player process scene class is absent or ambiguous: " + name);
        }
        uint Slot(uint table, uint slot) => Word(image.Read(table + slot, 4), 0);
    }
}
