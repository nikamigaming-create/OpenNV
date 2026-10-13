namespace OpenNV.Runtime.Compatibility.NativePlugins;

// Preserve the original provider entry, including ST0 returns and the private
// x86 tan entry's XMM0 argument/result. There is no managed math replacement.
internal static class NativePluginCrtPureImports
{
    internal static readonly IReadOnlySet<string> Libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "ucrtbase.dll", "api-ms-win-crt-string-l1-1-0.dll",
        "api-ms-win-crt-convert-l1-1-0.dll", "api-ms-win-crt-math-l1-1-0.dll",
    };
    internal static bool Owns(string library, string name) => library.ToLowerInvariant() switch
    {
        "ucrtbase.dll" => name is "strncat_s" or "atoi" or "fmaxf" or "floor" or "round" or "_libm_sse2_tan_precise",
        "api-ms-win-crt-string-l1-1-0.dll" => name == "strncat_s",
        "api-ms-win-crt-convert-l1-1-0.dll" => name == "atoi",
        "api-ms-win-crt-math-l1-1-0.dll" => name is "fmaxf" or "floor" or "round" or "_libm_sse2_tan_precise",
        _ => false,
    };

    internal static IReadOnlyDictionary<string, uint> SelectExports(NativePluginCrtProviderSelection selection,
        IEnumerable<NativePluginImageExport> sourceExports)
    {
        var source = sourceExports.ToDictionary(row => row.Name, StringComparer.Ordinal);
        var result = new Dictionary<string, uint>(StringComparer.Ordinal);
        foreach (var pair in selection.Imports)
            foreach (var name in pair.Value.Where(name => Owns(pair.Key, name)))
            {
                if (!source.TryGetValue(name, out var export) || export.Rva == 0 || !export.Executable || export.Forwarded)
                    throw new NotSupportedException("Pure CRT import lacks its exact direct executable provider export: " + pair.Key + "!" + name);
                result.TryAdd(name, export.Rva);
            }
        return result;
    }

    internal static void RequireBinding(NativePluginCrtProviderSelection selection, IReadOnlyDictionary<string, uint> exports,
        string library, string name, uint module, uint address, uint rva)
    {
        if (!Owns(library, name) || !selection.Imports.TryGetValue(library, out var selected) || !selected.Contains(name) ||
            !exports.TryGetValue(name, out var expected) || expected != rva || module == 0 || address == 0 ||
            (ulong)module + rva > uint.MaxValue || address != module + rva)
            throw new InvalidDataException("Pure CRT binding differs from its selected library/name and actual mapped export coordinate.");
    }
}
