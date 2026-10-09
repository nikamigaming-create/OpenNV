namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal static class NativePluginCrtStandardImports
{
    internal static readonly IReadOnlySet<string> Names = new HashSet<string>(StringComparer.Ordinal)
    {
        "__acrt_iob_func", "_fileno", "_get_osfhandle", "_dup", "_close", "_read", "_write", "_lseek", "_lseeki64",
        "_eof", "_filelength", "_filelengthi64",
    };
    internal static IReadOnlyList<NativePluginCrtProviderSelection> AddCppDependency(string module, string sha,
        IReadOnlyList<NativePluginCrtProviderSelection> providers)
    {
        var cpp = NativePluginCppRuntimeImports.Read(module, sha);
        if (cpp is null) return providers;
        if (providers.Count > 1) throw new NotSupportedException("C++ opaque FILE calls require one exact UCRT instance.");
        var existing = providers.SingleOrDefault();
        if (existing is not null && (!existing.Path.Equals(cpp.UcrtPath, StringComparison.OrdinalIgnoreCase) ||
            !existing.Sha256.Equals(cpp.UcrtSha256, StringComparison.OrdinalIgnoreCase)))
            throw new NotSupportedException("C++ and source FILE calls require the same exact UCRT instance.");
        var imports = existing?.Imports.ToDictionary(row => row.Key, row => row.Value, StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase);
        // This dependency comes from the actual selected C++ provider's import,
        // not a fabricated import on the original caller image.
        const string library = "ucrtbase.dll";
        var owned = imports.TryGetValue(library, out var prior) ? new HashSet<string>(prior, StringComparer.Ordinal) : new(StringComparer.Ordinal);
        owned.Add("__acrt_iob_func"); imports[library] = owned;
        return [new(cpp.UcrtPath, cpp.UcrtSha256, "actual-C++-UCRT-dependency:" + cpp.Sha256, imports)];
    }
}
