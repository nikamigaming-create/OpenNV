namespace OpenNV.Runtime.Content;

internal static partial class FalloutExecutableStringTable
{
    private static void RequireActorPhysicalAdmissionConstructor(byte[] code, Image image, int player, int initialize)
    {
        if (!Match(code, initialize + 0x9d, [0xc7, 0x86, 0xfc, 0, 0, 0, 0, 0, 0, 0,
                0xc7, 0x86, 0, 1, 0, 0, 0, 0, 0, 0]) ||
            !Match(code, initialize + 0x60, [0x89, 0x46, 0x60]))
            throw new NotSupportedException("Actor physical admission lost its independent mode/state constructor or inherited process publication.");
        var low = Call(initialize + 0x4c);
        if (!Match(code, low + 0x2e, [0xc7, 6]) || Word(code, low + 0x30) != Table(".?AVLowProcess@@", 1))
            throw new NotSupportedException("Actor physical admission did not construct its actual inherited Low process.");
        var high = Call(player + 0x40a);
        if (!Match(code, high + 0x32, [0xc7, 6]) || Word(code, high + 0x34) != Table(".?AVHighProcess@@", 1) ||
            !Match(code, player + 0x41b, [0x8b, 1, 0xff, 0x77, 0x60]) ||
            !Match(code, player + 0x424, [0xff, 0x50, 4]) ||
            !Match(code, player + 0x44a, [0x8b, 0xcf, 0x89, 0x47, 0x60]))
            throw new NotSupportedException("Player physical admission changed High construction, inherited common copy or current-process publication.");
        var physical = Call(player + 0x4a0);
        if (!Match(code, physical + 0x58, [0x8b, 0x42, 0x28, 0x85, 0xc0, 0x74, 0x10, 0x83, 0xf8, 1]) ||
            !Match(code, physical + 0xc4, [0x8b, 0x81, 0, 1, 0, 0, 0x83, 0xe8, 2, 0x0f, 0x84]) ||
            !Match(code, physical + 0xd3, [0x83, 0xe8, 2, 0x0f, 0x84]) ||
            !Match(code, physical + 0xdc, [0x83, 0xb9, 0xfc, 0, 0, 0, 2, 0x74, 0x0f, 0x6a, 0xff, 0xe8]) ||
            !Match(code, physical + 0xec, [0xc6, 0x45, 0xef, 0, 0x84, 0xc0, 0x74, 4, 0xc6, 0x45, 0xef, 1]) ||
            !Match(code, physical + 0x44e, [0x80, 0x7d, 0xef, 0, 0x74, 0x54]) ||
            !Match(code, physical + 0x491, [0x8b, 0x80, 0xd0, 1, 0, 0, 0xff, 0xd0]) ||
            !Match(code, physical + 0x4a3, [0xe8]) ||
            !Match(code, physical + 0x551, [0x80, 0x7d, 0xef, 0, 0xb9, 1, 0, 0, 0, 0x6a, 0, 0x6a, 0,
                0xb8, 6, 0, 0, 0, 0x6a, 1, 0x0f, 0x45, 0xc1, 0x50, 0xff, 0x75, 0xe8, 0xe8]))
            throw new NotSupportedException("Actor physical admission changed its process/state guard, registration predicate or independent model-motion selection.");
        RequireActorPhysicalRegistrationPredicate(code, Call(physical + 0xe7));
        uint Table(string name, int slots)
        {
            var tables = image.SourceClassTables(name, slots).ToArray();
            return tables.Length == 1 ? tables[0] : throw new NotSupportedException("Physical constructor source class is absent or ambiguous: " + name);
        }
        int Call(int at)
        {
            if (code[at] != 0xe8) throw new NotSupportedException("Actor physical constructor lost its actual direct child.");
            return checked(at + 5 + System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(at + 1)));
        }
    }

    private static void RequireActorPhysicalActivity(byte[] code, Image image, int middle, int high)
    {
        var tables = new[] { ".?AVHighProcess@@", ".?AVMiddleHighProcess@@" }.Select(name =>
        {
            var found = image.SourceClassTables(name, 260).ToArray();
            return found.Length == 1 ? found[0] : throw new NotSupportedException("Physical activity source class is absent or ambiguous: " + name);
        }).ToArray();
        foreach (var table in tables)
            if (!Match(image.Read(Word(image.Read(table + 0x408, 4), 0), 8), 0, [0x0f, 0xbe, 0x81, 0x3c, 1, 0, 0, 0xc3]) ||
                !Match(image.Read(Word(image.Read(table + 0x40c, 4), 0), 16), 0,
                    [0x55, 0x8b, 0xec, 0x8a, 0x45, 8, 0x88, 0x81, 0x3c, 1, 0, 0, 0x5d, 0xc2, 4, 0]))
                throw new NotSupportedException("Physical activity lost its independent signed getter or byte writer.");
        if (!Match(code, middle + 0x1b7, [0x66, 0xc7, 0x86, 0x3c, 1, 0, 0, 0, 0]) ||
            code[high - 5] != 0xe8 ||
            checked(high + System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(high - 4))) != middle - 0x32)
            throw new NotSupportedException("High physical activity did not inherit its actual MiddleHigh constructor-zero byte.");
    }

    private static void RequireActorPhysicalRegistrationPredicate(byte[] code, int predicate)
    {
        if (!Match(code, predicate + 0x24, [0x3b, 0xbe, 0x4c, 2, 0, 0, 0x75, 0x16]) ||
            !Match(code, predicate + 0x3a, [0x8b, 0x86, 0x48, 2, 0, 0, 0xeb, 0x0e]) ||
            !Match(code, predicate + 0x42, [0x8b, 0x47, 0x5c, 0x85, 0xc0, 0x0f, 0x84]) ||
            !Match(code, predicate + 0x4d, [0x8b, 0x40, 0x14, 0x85, 0xc0, 0x0f, 0x84]) ||
            !Match(code, predicate + 0x58, [0xf7, 0x45, 8, 0, 4, 0, 0, 0x74, 0x75, 0x8b, 0x87, 0xfc, 0, 0, 0]) ||
            !Match(code, predicate + 0x82, [0x83, 0xf8, 4, 0x75, 0x4f]) ||
            !Match(code, predicate + 0xd6, [0x8b, 0x4f, 0x60, 0x85, 0xc9, 0x74, 0x55]) ||
            !Match(code, predicate + 0x12a, [0x5f, 0xb0, 1, 0x5e, 0x5d, 0xc2, 4, 0, 0x5f, 0x32, 0xc0, 0x5e, 0x5d, 0xc2, 4, 0]))
            throw new NotSupportedException("Actor physical registration changed its actual reference-model/mode/process predicate order.");
        foreach (var (offset, mode) in new[] { (0x67, 1), (0x70, 2), (0x79, 6) })
            if (!Match(code, predicate + offset, [0x83, 0xf8, (byte)mode, 0x0f, 0x84]) ||
                checked(predicate + offset + 9 + System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(predicate + offset + 5))) != predicate + 0x12a)
                throw new NotSupportedException("Actor physical registration lost its independent qualified source modes.");
        foreach (var (offset, activity) in new[] { (0xdd, 1), (0xed, 3), (0xfd, 2), (0x10d, 4), (0x11d, 5) })
            if (!Match(code, predicate + offset, [0x8b, 1, 0xff, 0x90, 8, 4, 0, 0, 0x83, 0xf8, (byte)activity]))
                throw new NotSupportedException("Actor physical registration cached or reordered its five current-process activity queries.");
    }
}
