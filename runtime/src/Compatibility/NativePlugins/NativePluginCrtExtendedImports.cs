namespace OpenNV.Runtime.Compatibility.NativePlugins;

// Each admitted spelling has an actual selected-provider callable below.
// Standard FILE globals, CRT descriptors and C++ file construction are separate
// owners; a stream method cannot turn those absent objects into admitted FILEs.
internal static class NativePluginCrtExtendedImports
{
    internal static readonly IReadOnlySet<string> Stream = new HashSet<string>(StringComparer.Ordinal)
    {
        "fgetc", "getc", "ungetc", "fgetpos", "fsetpos", "setvbuf", "_get_stream_buffer_pointers",
        "_lock_file", "_unlock_file",
    };
    internal static readonly IReadOnlySet<string> Environment = new HashSet<string>(StringComparer.Ordinal)
    {
        "getenv_s", "_putenv_s",
    };
    internal static readonly IReadOnlySet<string> Libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "ucrtbase.dll", "api-ms-win-crt-stdio-l1-1-0.dll", "api-ms-win-crt-filesystem-l1-1-0.dll",
        "api-ms-win-crt-environment-l1-1-0.dll",
    };
    internal static bool Owns(string library, string name)
        => Libraries.Contains(library) &&
            (Stream.Contains(name) && library != "api-ms-win-crt-environment-l1-1-0.dll" ||
             Environment.Contains(name) && library is "ucrtbase.dll" or "api-ms-win-crt-environment-l1-1-0.dll");
}
