namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal static class NativePluginModuleRuntimeImports
{
    // Public CRT ABIs already used by the product. FILE calls still enter the
    // existing typed adapters, whose actual peer guard records a sticky failure.
    internal static bool Owns(string library, string name) =>
        NativePluginCrtExtendedImports.Libraries.Contains(library) && NativePluginCrtImports.Owned.Contains(name) ||
        (library is "ucrtbase.dll" or "vcruntime140.dll" || library.StartsWith("api-ms-win-crt-", StringComparison.Ordinal)) &&
        NativePluginPlatformImports.Owner(library, name) is not null &&
        !NativePluginIoImports.Owned.Contains(name) && !NativePluginIoImports.Unowned.Contains(name);
}
