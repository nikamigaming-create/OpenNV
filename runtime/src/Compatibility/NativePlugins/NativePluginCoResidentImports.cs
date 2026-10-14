namespace OpenNV.Runtime.Compatibility.NativePlugins;

// Additional private I/O, CRT, worker and game interfaces need their own
// module-bound owners rather than the first DLL's policy. Genuine Windows
// exports and the existing source protection adapter form this bounded lane.
internal static class NativePluginCoResidentImports
{
    internal static void Require(NativePluginImageDeclaration source, NativePluginModuleReadIoSource? moduleRead = null)
    {
        moduleRead?.Check(source);
        if (source.DelayImports) throw new NotSupportedException("Co-resident delay imports need their genuine module loader lifetime.");
        foreach (var import in source.Imports)
        {
            var library = import.Library.ToLowerInvariant();
            var name = import.Name ?? throw new NotSupportedException("Co-resident ordinal imports require their exact provider declaration.");
            if (NativePluginSharedFileSizeImports.Owns(library, name)) continue;
            if (moduleRead?.Owns(library, name) == true) continue;
            if (NativePluginModuleReadIoSource.OwnsExport(library, name))
                throw new NotSupportedException("Co-resident original read import needs its actual module-bound source/constructor owner: " + library + "!" + name);
            var windows = library is "kernel32.dll" or "kernelbase.dll" || library.StartsWith("api-ms-win-core-", StringComparison.Ordinal);
            if (!windows || NativePluginIoImports.Owned.Contains(name) || NativePluginIoImports.Unowned.Contains(name) ||
                NativePluginPlatformImports.Owner(library, name) is null)
                throw new NotSupportedException("Co-resident original import lacks its independent callable/I/O/provider owner: " + library + "!" + name);
        }
    }
}
