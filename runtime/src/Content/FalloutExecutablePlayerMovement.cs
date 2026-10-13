namespace OpenNV.Runtime.Content;

internal static partial class FalloutExecutableStringTable
{
    private static void RequirePlayerMovementFlags(byte[] code, Image image, int caller, int highConstructor)
    {
        var high = Table(".?AVHighProcess@@", 412);
        if (!Match(image.Read(Slot(high, 0x610), 7), 0, [0x8b, 0x81, 0x14, 4, 0, 0, 0xc3]) ||
            !Match(image.Read(Slot(high, 0x614), 11), 0, [0xc7, 0x81, 0x14, 4, 0, 0, 0, 0, 0, 0, 0xc3]) ||
            !Match(code, highConstructor + 0x4ff, [0xc7, 0x86, 0x14, 4, 0, 0, 0, 0, 0, 0]))
            throw new NotSupportedException("High movement word lost its separate constructor, getter or clear.");
        foreach (var name in new[] { ".?AVBaseProcess@@", ".?AVLowProcess@@", ".?AVMiddleLowProcess@@", ".?AVMiddleHighProcess@@" })
        {
            var lower = Table(name, 412);
            if (!Match(image.Read(Slot(lower, 0x610), 3), 0, [0x33, 0xc0, 0xc3]) ||
                !Match(image.Read(Slot(lower, 0x614), 3), 0, [0xc2, 0, 0]))
                throw new NotSupportedException("Lower process movement word gained an independent producer or clear.");
        }
        var entry = caller + 0x75a;
        if (!Match(code, entry, [0x8b, 0x4f, 0x60, 0x8b, 1, 0x8b, 0x80, 0x10, 6, 0, 0, 0xff, 0xd0,
                0x8b, 0x4f, 0x60, 0x89, 0x45, 0xdc, 0x8b, 0x11, 0xff, 0x92, 0x14, 6, 0, 0, 0x8b, 0x45, 0xdc, 0xa8, 4, 0x74, 0x22]) ||
            !Match(code, entry + 0x44, [0xa8, 8, 0x74, 0x1f]) ||
            !Match(code, entry + 0x67, [0x8b, 0xcf, 0xe8]) ||
            !Match(code, entry + 0x6e, [0x0f, 0xb7, 0xc0, 0x8b, 0xcf, 0x89, 0x45, 0xd8, 0xe8]) ||
            Child(entry + 0x69) != Child(entry + 0x76) ||
            !Match(code, entry + 0x7b, [0xa9, 0, 4, 0, 0, 0x0f, 0x84]))
            throw new NotSupportedException("Player movement prefix changed its read/clear/mask tests or repeated Actor wrapper calls.");
        var wrapper = Child(entry + 0x69);
        if (!Match(code, entry + 0xea, [0x8b, 0x8f, 0xa8, 0, 0, 0, 0x25, 0, 0xcc, 0, 0, 0x89, 0x45, 0xec, 0xe8]))
            throw new NotSupportedException("Movement lost its independent following current Actor field child.");
        if (!Match(code, wrapper, [0x8b, 0x89, 0x84, 1, 0, 0, 0x85, 0xc9, 0x74, 5, 0x8b, 1, 0xff, 0x60, 0x20, 0x33, 0xc0, 0xc3]))
            throw new NotSupportedException("Actor mover wrapper lost its own nullable field and current virtual getter.");
        var mover = Table(".?AVPlayerMover@@", 10);
        if (!Match(image.Read(Slot(mover, 0x20), 7), 0, [0x8b, 0x81, 0x94, 0, 0, 0, 0xc3]))
            throw new NotSupportedException("Player mover virtual no longer reads its independent movement word.");
        var constructors = new List<int>();
        for (var at = 0x12; at <= code.Length - 0x4a; ++at)
        {
            if (!Match(code, at, [0xc7, 6]) || Word(code, at + 2) != mover ||
                !Match(code, at + 0x23, [0xc7, 0x86, 0x94, 0, 0, 0, 0, 0, 0, 0]) ||
                !Match(code, at + 0x2d, [0xc7, 0x86, 0x9c, 0, 0, 0, 0, 0, 0x80, 0xbf]) ||
                !Match(code, at + 0x37, [0xc7, 0x86, 0x98, 0, 0, 0, 0, 0, 0, 0]) ||
                !Match(code, at + 6, [0xf3, 0x0f, 0x7e, 5]) ||
                !Match(code, at + 0xe, [0x66, 0x0f, 0xd6, 0x86, 0x88, 0, 0, 0, 0xa1]) ||
                !Match(code, at + 0x1b, [0x89, 0x86, 0x90, 0, 0, 0]) ||
                !Match(code, at + 0x41, [0x5e, 0x8b, 0xe5, 0x5d, 0xc2, 4, 0])) continue;
            var vector = Word(code, at + 0xa);
            if (Word(code, at + 0x17) != vector + 8 ||
                image.ReadLoaderData(vector, 12).Any(value => value != 0))
                throw new NotSupportedException("Player mover constructor vector is not its own loader positive-zero source.");
            constructors.Add(at - 0x12);
        }
        if (constructors.Count != 1)
            throw new NotSupportedException("Player mover has no unique selected constructor fields.");
        var factories = 0;
        for (var at = 0; at <= code.Length - 0x3c; ++at)
            if (Match(code, at, [0x8b, 0x8f, 0x84, 1, 0, 0, 0x85, 0xc9, 0x74, 6,
                    0x8b, 1, 0x6a, 1, 0xff, 0x10, 0x68, 0xa0, 0, 0, 0, 0xb9]) &&
                Match(code, at + 0x1f, [0x89, 0x45, 0xf0, 0xc6, 0x45, 0xfc, 0x1c, 0x85, 0xc0, 0x74, 0xa, 0x57, 0x8b, 0xc8, 0xe8]) &&
                Child(at + 0x2d) == constructors[0] &&
                Match(code, at + 0x32, [0xeb, 2, 0x33, 0xc0, 0x89, 0x87, 0x84, 1, 0, 0])) ++factories;
        if (factories != 1)
            throw new NotSupportedException("Player lost its actual owned mover allocation/publication and old-owner release.");
        RequirePlayerMovementControl(code, image, caller);

        uint Table(string name, int slots)
        {
            var values = image.SourceClassTables(name, slots).ToArray();
            return values.Length == 1 ? values[0] : throw new NotSupportedException("Movement source class is absent or ambiguous: " + name);
        }
        uint Slot(uint table, uint slot) => Word(image.Read(table + slot, 4), 0);
        int Child(int at)
        {
            if (code[at] != 0xe8) throw new NotSupportedException("Movement owner lost a direct source child call.");
            return checked(at + 5 + System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(at + 1)));
        }
    }
}
