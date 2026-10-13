namespace OpenNV.Runtime.Content;

internal static partial class FalloutExecutableStringTable
{
    private static void RequirePlayerIdlePackages(byte[] code, Image image, int middleConstructor)
    {
        var high = Table(".?AVHighProcess@@", 460);
        var getter = Offset(Slot(high, 0x278));
        if (!Match(code, getter, [0x56, 0x8b, 0xf1, 0x8b, 6, 0xff, 0x90, 8, 2, 0, 0,
            0x8b, 0x16, 0x8b, 0xce, 0x5e, 0x85, 0xc0, 0x74, 6, 0xff, 0xa2, 8, 2, 0, 0,
            0xff, 0xa2, 0x28, 2, 0, 0]) ||
            !Match(image.Read(Slot(high, 0x208), 7), 0, [0x8b, 0x81, 0xe4, 0, 0, 0, 0xc3]) ||
            !Match(image.Read(Slot(high, 0x228), 4), 0, [0x8b, 0x41, 4, 0xc3]) ||
            !Match(code, middleConstructor + 0x35, [0x8d, 0x8e, 0xe4, 0, 0, 0]))
            throw new NotSupportedException("Idle package lost its distinct primary/current getters, repeated primary read or constructor.");
        var packageFields = Child(middleConstructor + 0x3f);
        if (!Match(code, packageFields, [0xc7, 1, 0, 0, 0, 0]))
            throw new NotSupportedException("Idle primary package does not begin with its own constructor-null field.");
        var middleLow = Child(middleConstructor - 5);
        var low = Child(middleLow + 0x29);
        var basis = Child(low + 0x29);
        if (!Match(code, basis + 0xa, [0x8d, 0x4e, 4]) || Child(basis + 0x13) != packageFields)
            throw new NotSupportedException("Current package did not inherit its separate Base constructor-null slot.");
        var actor = Offset(Slot(Table(".?AVActor@@", 191), 0x2f4));
        var writer = Offset(Slot(high, 0x22c));
        if (!Match(code, actor + 0x192, [0x8b, 0x4f, 0x60, 0x57, 0x53, 0x8b, 1, 0xff, 0x90, 0x2c, 2, 0, 0]) ||
            !Match(code, writer + 0x15, [0x8d, 0x4f, 4, 0xe8]))
            throw new NotSupportedException("Actor package assignment did not address its independent current-package slot.");
        var loader = Offset(Slot(Table(".?AVTESPackage@@", 42), 0x20));
        if (!Match(code, loader + 0x598, [0x3d, 0x50, 0x4b, 0x44, 0x54]) ||
            !Match(code, loader + 0x6d3, [0x6a, 0x0c]) ||
            !Match(code, loader + 0x718, [0x0f, 0xbe, 0x45, 0xe8]) ||
            !Match(code, loader + 0x72c, [0x66, 0x0f, 0xd6, 0x47, 0x1c]) ||
            !Match(code, loader + 0x4bc, [0x8b, 0x47, 0x34, 0x85, 0xc0]) ||
            !Match(code, loader + 0x98d, [0x89, 0x47, 0x34]))
            throw new NotSupportedException("Idle package kind/collection did not retain its own PKDT bytes and nullable idle collection.");
        // IDLB, IDLC, IDLF, IDLN, IDLA and IDLT all allocate the same nullable
        // collection. An explicitly empty IDLC still makes that field nonnull.
        foreach (var signature in new[] { 0x424c4449u, 0x434c4449u, 0x464c4449u,
            0x4e4c4449u, 0x414c4449u, 0x544c4449u })
        {
            var found = false;
            for (var at = loader; at < loader + 0x9a7; ++at)
                if (code[at] == 0x3d && Word(code, at + 1) == signature) { found = true; break; }
            if (!found) throw new NotSupportedException("Idle collection loader lost one of its source record fields.");
        }
        uint Table(string name, int slots)
        {
            var tables = image.SourceClassTables(name, slots).ToArray();
            return tables.Length == 1 ? tables[0] : throw new NotSupportedException("Idle package has no unique source class: " + name);
        }
        uint Slot(uint table, uint slot) => Word(image.Read(table + slot, 4), 0);
        int Offset(uint address) => SourceOffset(code, image.CodeBase, address);
        int Child(int at)
        {
            if (code[at] != 0xe8) throw new NotSupportedException("Idle package constructor lost its actual direct parent call.");
            return checked(at + 5 + System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(at + 1)));
        }
    }
}
