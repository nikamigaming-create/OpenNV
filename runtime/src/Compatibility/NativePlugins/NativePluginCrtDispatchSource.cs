namespace OpenNV.Runtime.Compatibility.NativePlugins;

// Two independently selected compiler graphs share the same runtime children.
// A longer exception-state store changes every following source coordinate;
// it does not grant a callback or change a current provider predicate.
internal sealed class NativePluginCrtDispatchSource
{
    internal const string MemoryFamily = "PE32/dynamic-UCRT/bounded-memory-attach/no-pre-entry-I/O";
    internal const string EmptyFamily = "PE32/dynamic-UCRT/empty-initializers/word-state-attach/no-pre-entry-I/O";
    private readonly bool _wordState;
    private NativePluginCrtDispatchSource(bool wordState) => _wordState = wordState;
    internal bool EmptyInitializers => _wordState;
    internal string Family => _wordState ? EmptyFamily : MemoryFamily;
    internal uint DispatchBytes => _wordState ? 270U : 267U;
    internal uint AttachBytes => _wordState ? 266U : 263U;
    internal uint ProcessorBytes => _wordState ? 794U : 813U;
    internal uint ProcessorImportOffset => _wordState ? 0x19U : 0x16U;
    internal uint Dispatch(uint offset) => checked(offset + (_wordState && offset >= 0x26 ? 3U : 0U));
    internal uint Attach(uint offset) => checked(offset + (_wordState && offset >= 0x2d ? 3U : 0U));
    internal static NativePluginCrtDispatchSource Read(NativePluginConstructorImage image, uint dispatch)
    {
        var store = image.Read(dispatch + 0x22, 7, true);
        var wordState = store.AsSpan().SequenceEqual(new byte[] { 0xc7, 0x45, 0xfc, 0, 0, 0, 0 });
        if (!wordState && !store.AsSpan(0, 4).SequenceEqual(new byte[] { 0x83, 0x65, 0xfc, 0 }))
            throw new NotSupportedException("Original construction requires its actual exception-state store family.");
        var result = new NativePluginCrtDispatchSource(wordState);
        image.Need(dispatch + result.Dispatch(0x26), [0x83, 0xff, 1, 0x74, 0x0a, 0x83, 0xff, 2, 0x74, 5], "source reason dispatch");
        return result;
    }
    internal void RequireAttach(NativePluginConstructorImage image, uint attach) =>
        image.Need(attach + 0x29, _wordState ? [0xc7, 0x45, 0xfc, 0, 0, 0, 0] : [0x83, 0x65, 0xfc, 0], "matching attach exception-state store");
    internal static void RequireEmptyRange(NativePluginConstructorImage image, uint start, uint end)
    {
        // Both boundary elements belong to the actual retained range. An empty
        // table is not inferred from a count, name, missing receiver or renderer.
        if (start % 4 != 0 || end < start || end - start != 4)
            throw new NotSupportedException("Original construction requires independent nonempty initializer owners.");
        image.RequireReadableData(start, 8);
        if (image.Read(start, 8).Any(value => value != 0))
            throw new NotSupportedException("Original construction contains a nonnull initializer in its selected range.");
    }
    internal static void RequireDistinctFields(IReadOnlyList<NativePluginConstructorField> fields)
    {
        var ordered = fields.OrderBy(row => row.Rva).ToArray();
        for (var index = 1; index < ordered.Length; ++index)
            if ((ulong)ordered[index - 1].Rva + ordered[index - 1].Bytes > ordered[index].Rva)
                throw new NotSupportedException("Original constructor aliases independent runtime/predicate fields.");
    }
    internal uint[] RequireProcessorScratch(NativePluginConstructorImage image, uint cpu)
    {
        if (!_wordState)
        {
            foreach (var at in new uint[] { 0x29, 0x145 }) image.Need(cpu + at, [0x8d, 0x7d, 0xd8], "CPU stack scratch alias");
            foreach (var at in new uint[] { 0x71, 0xe8, 0x124 }) image.Need(cpu + at, [0x8d, 0x5d, 0xd8], "CPU stack scratch alias");
            return [0x35, 0x37, 0x3a, 0x3f, 0x74, 0x7e, 0x81, 0x84, 0xeb, 0xed, 0xf0, 0xf3,
                0x127, 0x129, 0x12c, 0x12f, 0x152, 0x154, 0x157, 0x15a];
        }
        image.Need(cpu, [0x55, 0x8b, 0xec, 0x83, 0x0d], "CPU module-local feature store");
        image.Need(cpu + 0x0a, [0x83, 0xec, 0x28, 0xc7, 5], "CPU current stack and zero initialization");
        if (NativePluginConstructorImage.Word(image.Read(cpu + 0x13, 4), 0) != 0)
            throw new NotSupportedException("Original CPU initializer changed its zero feature-state store.");
        foreach (var at in new uint[] { 0x2c, 0x13c }) image.Need(cpu + at, [0x8d, 0x7d, 0xd8], "CPU stack scratch alias");
        foreach (var at in new uint[] { 0xdf, 0x11b }) image.Need(cpu + at, [0x8d, 0x5d, 0xd8], "CPU stack scratch alias");
        foreach (var at in new uint[] { 0x38, 0x73, 0x149 })
            image.Need(cpu + at, [0x89, 7, 0x89, 0x77, 4, 0x89, 0x4f, 8, 0x89, 0x57, 0x0c], "CPU four-word EDI scratch output");
        foreach (var at in new uint[] { 0xe2, 0x11e })
            image.Need(cpu + at, [0x89, 3, 0x89, 0x73, 4, 0x89, 0x4b, 8, 0x89, 0x53, 0x0c], "CPU four-word EBX scratch output");
        image.Need(cpu + 0x313, [0x5f, 0x5e, 0x5b, 0x33, 0xc0, 0xc9, 0xc3], "CPU capability return");
        return [0x38, 0x3a, 0x3d, 0x40, 0x73, 0x75, 0x78, 0x7b, 0xe2, 0xe4, 0xe7, 0xea,
            0x11e, 0x120, 0x123, 0x126, 0x149, 0x14b, 0x14e, 0x151];
    }
}
