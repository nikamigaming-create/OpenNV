namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed record NativePluginParallelHardwareSelection(string Path, string Sha256, string SourceOwner, uint Rva);

// This zero-argument query retains the genuine provider's cached hardware
// concurrency. Worker creation and callbacks have separate, unowned lifetimes.
internal static class NativePluginParallelHardwareImports
{
    internal const string Library = "msvcp140_atomic_wait.dll";
    internal const string HardwareThreads = "__std_parallel_algorithms_hw_threads";
    internal static bool Owns(string library, string name) =>
        library.Equals(Library, StringComparison.OrdinalIgnoreCase) && name == HardwareThreads;
    internal static string? Owner(string library, string name) => Owns(library, name)
        ? "actual-selected-x86-MSVC-hardware-query:" + library + "!" + name : null;

    internal static NativePluginParallelHardwareSelection? Read(string modulePath, string moduleSha256)
    {
        var module = NativePluginImageDeclarations.Read(modulePath, dll: true);
        if (!module.Sha256.Equals(moduleSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Parallel hardware source changed before original import admission.");
        if (!module.Imports.Any(row => Owns(row.Library, row.Name ?? string.Empty))) return null;
        if (module.DelayImports) throw new NotSupportedException("Parallel hardware delay imports have no entered loader owner.");
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.SystemX86), Library);
        var provider = NativePluginImageDeclarations.Read(path, dll: true);
        var export = provider.Exports.SingleOrDefault(row => row.Name == HardwareThreads)
            ?? throw new NotSupportedException("Selected Microsoft provider lacks its original hardware query.");
        if (!export.Executable || export.Forwarded || export.Rva == 0)
            throw new NotSupportedException("Parallel hardware query lacks its direct selected x86 executable export.");
        return new(path, provider.Sha256, "actual-selected-MSVC-parallel-hardware:" + provider.Sha256, export.Rva);
    }
}
