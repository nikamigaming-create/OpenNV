namespace OpenNV.Runtime.Content;

internal static partial class FalloutExecutableStringTable
{
    private static void RequirePlayerPostActionProcess(byte[] code, Image image, int caller,
        int middleConstructor, int highConstructor, uint scaledTimer)
    {
        var high = Class(".?AVHighProcess@@"); var middle = Class(".?AVMiddleHighProcess@@");
        var update = SourceOffset(code, image.CodeBase, Slot(high, 0x4f8));
        if (Slot(high, 0x4f8) != Slot(middle, 0x4f8) ||
            !Match(code, caller + 0x4ab, [0x8b, 0x4f, 0x60, 0x6a, 0, 0x57, 0x8b, 1, 0xff, 0x90, 0xf8, 4, 0, 0]) ||
            !Match(code, middleConstructor + 0x62, [0x33, 0xc0]) ||
            !Match(code, middleConstructor + 0x72, [0x89, 0x86, 0xb0, 1, 0, 0, 0x89, 0x86, 0xb4, 1, 0, 0, 0x89, 0x86, 0x20, 2, 0, 0]) ||
            !Match(code, highConstructor + 0x5f2, [0xc7, 0x86, 0xbc, 3, 0, 0, 0, 0, 0, 0, 0xc7, 0x86, 0xc0, 3, 0, 0, 0, 0, 0, 0]) ||
            !Match(code, highConstructor + 0x4b6, [0xc7, 0x86, 0xb4, 3, 0, 0, 0, 0, 0, 0, 0xc7, 0x86, 0xb8, 3, 0, 0, 0, 0, 0, 0]))
            throw new NotSupportedException("Player post-action process changed its current virtual or independent constructor cells.");
        foreach (var (slot, field) in new[] { (0x504u, 0x3c0u), (0x654u, 0x3b8u) })
            if (!Match(image.Read(Slot(high, slot), 7), 0, [0xd9, 0x81, (byte)field, (byte)(field >> 8), 0, 0, 0xc3]) ||
                !Match(image.Read(Slot(middle, slot), 3), 0, [0xd9, 0xee, 0xc3]))
                throw new NotSupportedException("Player post-action timer getter changed High cells or MiddleHigh's zero result.");
        foreach (var (slot, field) in new[] { (0x508u, 0x3c0u), (0x72cu, 0x3bcu) })
            if (!Match(image.Read(Slot(high, slot), 20), 0, [0x55, 0x8b, 0xec, 0xf3, 0x0f, 0x10, 0x45, 8,
                0xf3, 0x0f, 0x11, 0x81, (byte)field, (byte)(field >> 8), 0, 0, 0x5d, 0xc2, 4, 0]) ||
                !Match(image.Read(Slot(middle, slot), 3), 0, [0xc2, 4, 0]))
                throw new NotSupportedException("Player post-action setters lost their independent cells or empty MiddleHigh dispatch.");
        foreach (var name in new[] { ".?AVBaseProcess@@", ".?AVLowProcess@@", ".?AVMiddleLowProcess@@" })
            if (!Match(image.Read(Slot(Class(name), 0x4f8), 3), 0, [0xc2, 8, 0]))
                throw new NotSupportedException("Low process post-action virtual has a new unowned consumer.");
        if (!Match(code, update + 0x2c, [0x0f, 0x57, 0xc0, 0xf3, 0x0f, 0x11, 0x45, 0xf0, 0x8b, 0x80, 4, 5, 0, 0, 0xff, 0xd0,
                0x0f, 0x57, 0xc0, 0xd9, 0x5d, 0xdc, 0x0f, 0x2f, 0x45, 0xdc, 0x0f, 0x82]) ||
            !Match(code, update + 0x4c, [0x8b, 0x8f, 0x20, 2, 0, 0, 0xf3, 0x0f, 0x10, 0x0d]) ||
            Word(image.Read(Word(code, update + 0x56), 4), 0) != 0x42c80000 ||
            !Match(code, update + 0x5a, [0x85, 0xc9, 0x0f, 0x84]) ||
            checked(update + 0x62 + System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(update + 0x5e))) != update + 0x2f1 ||
            !Match(code, update + 0x2f1, [0xf3, 0x0f, 0x5f, 0x45, 0xf0, 0x8b, 7, 0x51, 0x8b, 0xcf, 0xf3, 0x0f, 0x5d, 0xc8,
                0xf3, 0x0f, 0x11, 0x4d, 8, 0xf3, 0x0f, 0x11, 0x0c, 0x24, 0xff, 0x90, 0x2c, 7, 0, 0]) ||
            !Match(code, update + 0x31c, [0xf2, 0x0f, 0x59, 5]) ||
            BitConverter.ToDouble(image.Read(Word(code, update + 0x320), 8)) != .01d ||
            !Match(code, update + 0x324, [0xf2, 0x0f, 0x58, 5]) ||
            BitConverter.ToDouble(image.Read(Word(code, update + 0x328), 8)) != 3d ||
            !Match(code, update + 0x32c, [0x66, 0x0f, 0x5a, 0xc0, 0xf3, 0x0f, 0x11, 4, 0x24, 0xff, 0x90, 8, 5, 0, 0]) ||
            !Match(code, update + 0x34f, [0x8b, 0x37, 0x8b, 0xcf, 0x8b, 0x86, 4, 5, 0, 0, 0xff, 0xd0, 0xd8, 0x25]) ||
            Word(code, update + 0x35d) != scaledTimer ||
            !Match(code, update + 0x361, [0x51, 0x8b, 0xcf, 0xd9, 0x1c, 0x24, 0xff, 0x96, 8, 5, 0, 0]))
            throw new NotSupportedException("Post-action process lost its ordered/unordered timer arm, null-context stores or Main timer association.");
        RequirePlayerDetectionAction(code, image, caller, high, middle, scaledTimer);
        uint Class(string name)
        {
            var tables = image.SourceClassTables(name, 460).ToArray();
            return tables.Length == 1 ? tables[0] : throw new NotSupportedException("Post-action process class is absent or ambiguous: " + name);
        }
        uint Slot(uint table, uint slot) => Word(image.Read(table + slot, 4), 0);
    }

    private static void RequirePlayerDetectionAction(byte[] code, Image image, int caller, uint high, uint middle, uint scaledTimer)
    {
        var subtract = SourceOffset(code, image.CodeBase, Slot(high, 0x650));
        var reset = SourceOffset(code, image.CodeBase, Slot(high, 0x4a0));
        var resetTimer = SourceOffset(code, image.CodeBase, Slot(high, 0x4a4));
        if (!Match(code, caller + 0x574, [0x8b, 0x4f, 0x60, 0x8b, 1, 0x8b, 0x80, 0x54, 6, 0, 0, 0xff, 0xd0]) ||
            !Match(code, caller + 0x581, [0x8b, 0x4f, 0x60, 0xd9, 0x5d, 0x9c, 0xf3, 0x0f, 0x10, 0x45, 0x9c, 0x0f, 0x2f, 5]) ||
            Word(image.Read(Word(code, caller + 0x58f), 4), 0) != 0 ||
            !Match(code, caller + 0x593, [0x8b, 1, 0x76, 8, 0xff, 0x90, 0x50, 6, 0, 0, 0xeb, 0x0e,
                0x51, 0xc7, 4, 0x24, 0, 0, 0, 0, 0xff, 0x90, 0xa0, 4, 0, 0]) ||
            !Match(code, subtract, [0xf3, 0x0f, 0x10, 0x81, 0xb8, 3, 0, 0, 0xf3, 0x0f, 0x5c, 5]) ||
            Word(code, subtract + 0xc) != scaledTimer ||
            !Match(code, subtract + 0x10, [0xf3, 0x0f, 0x11, 0x81, 0xb8, 3, 0, 0, 0xc3]) ||
            !Match(code, reset, [0x55, 0x8b, 0xec, 0x56, 0x8b, 0xf1, 0x8b, 6, 0xff, 0x90, 0xa4, 4, 0, 0,
                0xf3, 0x0f, 0x10, 0x45, 8, 0xf3, 0x0f, 0x11, 0x86, 0xb4, 3, 0, 0, 0x5e, 0x5d, 0xc2, 4, 0]) ||
            !Match(code, resetTimer, [0xf3, 0x0f, 0x10, 5]) ||
            !Match(code, resetTimer + 8, [0xf3, 0x0f, 0x11, 0x81, 0xb8, 3, 0, 0, 0xc3]) ||
            !Match(image.Read(Slot(middle, 0x650), 3), 0, [0xc2, 0, 0]) ||
            !Match(image.Read(Slot(middle, 0x4a0), 3), 0, [0xc2, 4, 0]) ||
            ReadStaticSettings(image).SingleOrDefault(row => row.Descriptor + 4 == Word(code, resetTimer + 4)) is not
            { Name: "fDetectionActionTimer", Collection: SettingCollection.Game })
            throw new NotSupportedException("Player detection action lost its separate reset/subtract dispatch or winning Game setting consumer.");
        uint Slot(uint table, uint slot) => Word(image.Read(table + slot, 4), 0);
    }

    private static void RequirePlayerPostActionFields(byte[] code, Image image, int caller, int constructor)
    {
        if (!Match(code, constructor + 0x3f1, [0xc6, 0x87, 0xc1, 1, 0, 0, 1]) ||
            !Match(code, constructor + 0x88b, [0xc7, 0x87, 8, 7, 0, 0, 0, 0, 0, 0]) ||
            !Match(code, constructor + 0xab6, [0xc7, 0x87, 0xe8, 1, 0, 0, 0, 0, 0x80, 0xbf]) ||
            !Match(code, constructor + 0xaca, [0xc7, 0x87, 0xf0, 1, 0, 0, 0, 0, 0x80, 0xbf]) ||
            !Match(code, caller + 0x52e, [0x6a, 0, 0x6a, 8, 0x8b, 0xcf, 0xe8]) ||
            !Match(code, caller + 0x56d, [0xc6, 0x87, 0xc1, 1, 0, 0, 0]) ||
            !Match(code, caller + 0x5ad, [0x80, 0xbf, 8, 7, 0, 0, 0, 0x0f, 0x84]) ||
            checked(caller + 0x5ba + System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(caller + 0x5b6))) != caller + 0x697)
            throw new NotSupportedException("Player post-action fields lost their actual constructor, null-context removal/store or combat-list gate.");
        var wrapper = Child(caller + 0x280);
        foreach (var (at, tail) in new[] { (caller + 0x4bb, new byte[] { 0x85, 0xc0, 0x74, 0x6a }),
            (caller + 0x53b, new byte[] { 0x85, 0xc0, 0x74, 0x29 }) })
            if (Child(at) != wrapper || !Match(code, at + 5, tail))
                throw new NotSupportedException("Player post-action context calls changed their actual getter or separate null arms.");
        if (!Match(code, caller + 0x697, [0xf3, 0x0f, 0x10, 0x87, 0xe8, 1, 0, 0, 0x0f, 0x57, 0xdb, 0x0f, 0x2f, 0xc3, 0xf3, 0x0f, 0x10, 0x0d]) ||
            Word(image.Read(Word(code, caller + 0x6a9), 4), 0) != 0xbf800000 ||
            !Match(code, caller + 0x6ad, [0xf3, 0x0f, 0x10, 0x53, 8, 0x76, 6, 0xf3, 0x0f, 0x5c, 0xc2, 0xeb, 3, 0x0f, 0x28, 0xc1,
                0xf3, 0x0f, 0x11, 0x87, 0xe8, 1, 0, 0, 0xf3, 0x0f, 0x10, 0x87, 0xf0, 1, 0, 0, 0x0f, 0x2f, 0xc3,
                0x76, 6, 0xf3, 0x0f, 0x5c, 0xc2, 0xeb, 3, 0x0f, 0x28, 0xc1, 0xf3, 0x0f, 0x11, 0x87, 0xf0, 1, 0, 0]))
            throw new NotSupportedException("Player post-action timers lost their separate source scene argument, negative sentinel or unclamped stores.");
        RequireInterfaceActorDistances(code, image, caller);
        int Child(int at)
        {
            if (code[at] != 0xe8) throw new NotSupportedException("Player post-action context has no actual source call.");
            return checked(at + 5 + System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(at + 1)));
        }
    }
}
