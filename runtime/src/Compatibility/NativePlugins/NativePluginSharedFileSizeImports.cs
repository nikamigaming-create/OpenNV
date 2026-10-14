namespace OpenNV.Runtime.Compatibility.NativePlugins;

// This capability consumes a retained shared HANDLE; it grants no path,
// constructor, CRT stream or supplemental module file-opening authority.
internal static class NativePluginSharedFileSizeImports
{
    internal const string Name = "GetFileSizeEx";
    internal static bool Owns(string library, string name) => name == Name &&
        (library is "kernel32.dll" or "kernelbase.dll" ||
            library.StartsWith("api-ms-win-core-file-", StringComparison.Ordinal));
}
