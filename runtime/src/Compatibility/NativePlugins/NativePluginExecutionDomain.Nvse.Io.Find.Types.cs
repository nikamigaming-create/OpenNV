namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed record NativePluginDirectorySource(string PhysicalDirectory, string SourceOwner);
internal sealed record NativePluginDirectorySelection(IReadOnlyList<NativePluginDirectorySource> Sources,
    Func<string, string?> ResolveWinningDirectory, Action VerifyCurrent, string SourceOwner);
internal sealed record NativePluginFindSource(string PhysicalDirectory, string SourceOwner, string? OnlyEntry = null, bool Absent = false);
internal sealed record NativePluginFindData(uint Attributes, ulong Created, ulong Accessed, ulong Written,
    ulong Size, uint Reserved0, uint Reserved1, string Name, string AlternateName);
internal sealed record NativePluginFindReceipt(ulong Sequence, ulong Generation, ulong Parent, ulong Search,
    uint Handle, uint Operation, uint Result, uint LastError, string Pattern, string SourceOwner,
    NativePluginFindData? Data, string? VirtualPath, string? PhysicalPath, bool? Winner);

internal sealed class NativePluginFindSearch(ulong id, string directory, string pattern,
    NativePluginDirectorySelection selection, IReadOnlyList<NativePluginFindSource> sources)
{
    internal ulong Id { get; } = id;
    internal string Directory { get; } = directory;
    internal string Pattern { get; } = pattern;
    internal NativePluginDirectorySelection Selection { get; } = selection;
    internal IReadOnlyList<NativePluginFindSource> Sources { get; } = sources;
    internal Dictionary<uint, uint> Handles { get; } = [];
    internal HashSet<string> Produced { get; } = new(StringComparer.OrdinalIgnoreCase);
    internal NativePluginFindData? Pending;
    internal uint? PublicHandle;
    internal bool? Wide;
    internal bool Closed;
}

internal sealed partial class NativePluginPrivateIo
{
    private readonly Dictionary<ulong, NativePluginFindSearch> _findSearches = [];
    private readonly List<NativePluginFindReceipt> _findReceipts = [];
    private readonly List<NativePluginFindReceipt> _findCloseFailures = [];
    internal IReadOnlyList<NativePluginFindReceipt> FindReceipts => _findReceipts.AsReadOnly();
}
