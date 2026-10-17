namespace OpenNV.Runtime.Content;

internal static partial class FalloutExecutableStringTable
{
    private static void RequirePlayerActorProcessQueries(byte[] code, Image image, int caller)
    {
        var query = caller + 0x99;
        if (!Match(code, query, [0x8b, 0x4f, 0x60, 0x89, 0x45, 0xe0, 0xa1]) ||
            !Match(code, query + 0xe, [0x8b, 0x11, 0x8b, 0x92, 0x48, 1, 0, 0, 0xff, 0xd2, 0x85, 0xc0, 0x74, 0x12]) ||
            !Match(code, query + 0x1c, [0x8b, 0x4f, 0x60, 0x8b, 1, 0x8b, 0x80, 0x48, 1, 0, 0, 0xff, 0xd0, 0x8b, 0x40, 8, 0xeb, 2, 0x33, 0xc0, 0x8b, 0xcf, 0x89, 0x45, 0x8c, 0xe8]))
            throw new NotSupportedException("Player source process getter order, repeated nonnull read or entry form changed.");
        var wrapper = checked(query + 0x3a + System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(query + 0x36)));
        if (!Match(code, wrapper, [0x8b, 0x49, 0x60, 0x85, 0xc9, 0x74, 8, 0x8b, 1, 0xff, 0xa0, 0xb4, 1, 0, 0, 0x33, 0xc0, 0xc3]))
            throw new NotSupportedException("Player Actor animation-context wrapper lost its independent nullable process branch.");
        if (!Match(code, query + 0x49, [0x89, 0x85, 0x5c, 0xff, 0xff, 0xff]))
            throw new NotSupportedException("Player animation-context getter lost its independent returned-pointer local store.");
        int? postWrapper = null;
        foreach (var (at, tail) in new[] { (caller + 0x27e, new byte[] { 0x89, 0x45, 0xd0, 0x85, 0xc0, 0x74, 0x65 }),
            (caller + 0x2f1, new byte[] { 0x85, 0xc0, 0x74, 0x56 }) })
        {
            var target = checked(at + 7 + System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(at + 3)));
            if (!Match(code, at, [0x8b, 0xcf, 0xe8]) || postWrapper is { } prior && prior != target ||
                !Match(code, target, [0x8b, 0x49, 0x60, 0x85, 0xc9, 0x74, 8, 0x8b, 1, 0xff, 0xa0, 0x88, 2, 0, 0, 0x33, 0xc0, 0xc3]) ||
                !Match(code, at + 7, tail))
                throw new NotSupportedException("Player post-process context getter order or independent null-child branches changed.");
            postWrapper = target;
        }
        var high = image.SourceClassTables(".?AVHighProcess@@", 165).ToArray();
        var middle = image.SourceClassTables(".?AVMiddleHighProcess@@", 165).ToArray();
        var basis = image.SourceClassTables(".?AVBaseProcess@@", 165).ToArray();
        if (high.Length != 1 || middle.Length != 1 || basis.Length != 1)
            throw new NotSupportedException("Player process queries lack unique source class declarations.");
        foreach (var (slot, field) in new[] { (0x148u, 0x114u), (0x1b4u, 0x1c0u), (0x288u, 0x138u) })
        {
            var getter = Word(image.Read(high[0] + slot, 4), 0);
            if (Word(image.Read(middle[0] + slot, 4), 0) != getter ||
                !Match(image.Read(getter, 7), 0, [0x8b, 0x81, (byte)field, (byte)(field >> 8), 0, 0, 0xc3]))
                throw new NotSupportedException("Player High/MiddleHigh virtual is not the selected pure nullable-field getter.");
            var empty = Word(image.Read(basis[0] + slot, 4), 0);
            if (!Match(image.Read(empty, 3), 0, [0x33, 0xc0, 0xc3]))
                throw new NotSupportedException("Player Base process virtual has an unowned nonnull result.");
        }
        // The same MiddleHigh constructor publishes its actual class table
        // and stores null into both fields before the High constructor returns.
        var constructors = new List<int>();
        for (var at = 0; at <= code.Length - 0x153; ++at)
            if (Match(code, at, [0xc7, 6]) && Word(code, at + 2) == middle[0] &&
                Match(code, at + 0x58, [0xc7, 0x86, 0x38, 1, 0, 0, 0, 0, 0, 0]) &&
                Match(code, at + 0xcc, [0xc7, 0x86, 0x14, 1, 0, 0, 0, 0, 0, 0]) &&
                Match(code, at + 0x149, [0xc7, 0x86, 0xc0, 1, 0, 0, 0, 0, 0, 0]))
                constructors.Add(at);
        if (constructors.Count != 1)
            throw new NotSupportedException("Player process nullable fields have no unique actual constructor stores.");
        var highFactories = 0; var highConstructor = -1;
        for (var at = 5; at <= code.Length - 6; ++at)
            if (Match(code, at, [0xc7, 6]) && Word(code, at + 2) == high[0] && code[at - 5] == 0xe8 &&
                checked(at + System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(at - 4))) == constructors[0] - 0x32)
            { ++highFactories; highConstructor = at; }
        if (highFactories != 1)
            throw new NotSupportedException("Player High process did not inherit its actual MiddleHigh constructor fields.");
        RequirePlayerProcessSceneUpdate(code, image, constructors[0]);
        var timer = RequirePlayerSceneScalarStores(code, image, caller);
        RequirePlayerPostActionProcess(code, image, caller, constructors[0], highConstructor, timer);
        RequirePlayerIdleAnimation(code, image, caller, constructors[0], highConstructor, timer);
        RequirePlayerMovementFlags(code, image, caller, highConstructor);
        RequireActorPhysicalActivity(code, image, constructors[0], highConstructor);
        RequirePlayerWeaponDrawnFields(code, image, constructors[0], high[0], middle[0]);
    }
}
