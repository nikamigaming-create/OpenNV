namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal enum NativePluginImportProviderKind : uint { Cng = 1 }
internal enum NativePluginLoaderAttemptPhase { Prepared, Entering, Mapped, Failed, Retired }
internal sealed record NativePluginImportProviderExport(string Name, uint Rva);
internal sealed record NativePluginImportProviderImage(NativePluginImportProviderKind Kind, string Library,
    string Path, string Sha256, IReadOnlyList<NativePluginImportProviderExport> Exports,
    IReadOnlyList<NativePluginImageImport> OriginalImports);
internal sealed record NativePluginImportProviderReceipt(ulong Generation, ulong Attempt, string OriginalSha256,
    NativePluginImportProviderKind Kind, string Library, string Path, string Sha256, uint Image,
    uint Thread, bool Bound, uint ProcessAttach, uint ProcessDetach, uint ActiveCalls,
    IReadOnlyList<ulong> Calls, bool LoaderReferenceRetired, bool MappingPresent);

// A source attempt is distinct from a successfully mapped original DLL and
// from its actual Query/Load receipts. Only its correlated CNG callback lane
// may execute while Windows performs original TLS/CRT/entry admission.
internal sealed class NativePluginOriginalLoaderAttempt(string path, string sha256, ulong id,
    IReadOnlyList<NativePluginImportProviderImage> providers)
{
    internal string Path { get; } = path;
    internal string Sha256 { get; } = sha256;
    internal ulong Id { get; } = id;
    internal IReadOnlyList<NativePluginImportProviderImage> Providers { get; } = providers;
    internal NativePluginLoaderAttemptPhase Phase { get; set; } = NativePluginLoaderAttemptPhase.Prepared;
}
