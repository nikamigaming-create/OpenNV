namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal enum NativePluginProfileKind : uint
{
    SectionW = 1, SectionA = 2, StructW = 3, StructA = 4,
    WriteSectionW = 5, WriteSectionA = 6, WriteStructW = 7, WriteStructA = 8,
    NamesW = 9, NamesA = 10,
    StringW = 11, StringA = 12, IntW = 13, IntA = 14, WriteStringW = 15, WriteStringA = 16,
}
internal sealed record NativePluginProfileReceipt(ulong Sequence, ulong Generation, ulong Parent,
    ulong Route, NativePluginProfileKind Kind, uint Requested, uint Result, uint LastError, bool Mutating,
    string VirtualPath, string PhysicalPath, string? WinnerSha256, NativePluginIoRole? Role);

internal sealed partial class NativePluginPrivateIo
{
    private readonly List<NativePluginProfileReceipt> _profileReceipts = [];
    internal IReadOnlyList<NativePluginProfileReceipt> ProfileReceipts => _profileReceipts.AsReadOnly();
    internal void ProfileResult(ulong parent, ulong id, NativePluginProfileKind kind, uint requested, uint result, uint last, bool mutating)
    {
        var writes = kind is NativePluginProfileKind.WriteSectionW or NativePluginProfileKind.WriteSectionA or
            NativePluginProfileKind.WriteStructW or NativePluginProfileKind.WriteStructA or
            NativePluginProfileKind.WriteStringW or NativePluginProfileKind.WriteStringA;
        if (!Enum.IsDefined(kind) || !_routes.TryGetValue(id, out var route) ||
            route.Api != (writes ? 9U : 8U) || route.Action != (writes ? NativePluginIoAction.ProfileWrite : NativePluginIoAction.ProfileRead) ||
            route.PhysicalPath is null || writes && route.Role != NativePluginIoRole.Configuration || mutating && !writes)
            throw new InvalidDataException("Profile result has no exact source configuration/call route.");
        if (kind is NativePluginProfileKind.SectionW or NativePluginProfileKind.SectionA or NativePluginProfileKind.NamesW or NativePluginProfileKind.NamesA or
            NativePluginProfileKind.StringW or NativePluginProfileKind.StringA)
        {
            if (requested != 0 && result >= requested || requested == 0 && result != 0)
                throw new InvalidDataException("Profile character result exceeds its actual native buffer.");
        }
        // A failed BOOL or zero character result remains an actual source result,
        // independently of Win32's unspecified/stale LastError after success.
        _profileReceipts.Add(new(checked(++_sequence), _generation ?? throw new InvalidOperationException("Profile has no generation."),
            parent, id, kind, requested, result, last, mutating, route.VirtualPath, route.PhysicalPath, route.WinnerSha256, route.Role));
        _routes.Remove(id);
        // Win32 BOOL failure can leave LastError zero. Only the actual result
        // controls a committed private write, never a guessed error predicate.
        if (mutating && result != 0) { _deleted.Remove(route.VirtualPath); PersistDeletions(); }
    }
}

internal sealed partial class NativePluginExecutionDomain
{
    private const uint ProfileResultCallback = 10;
    private readonly List<NativePluginProfileReceipt> _retiredProfileReceipts = [];
    internal IReadOnlyList<NativePluginProfileReceipt> NvseProfileReceipts => _privateIo?.ProfileReceipts ?? _retiredProfileReceipts.AsReadOnly();
    private void RetainPrivateProfileReceipts()
    {
        if (_privateIo is null) return;
        _retiredProfileReceipts.Clear(); _retiredProfileReceipts.AddRange(_privateIo.ProfileReceipts);
    }
    private byte[] DispatchPrivateProfile(Frame frame, ulong parent, NativePluginPrivateIo owner, BinaryReader reader)
    {
        if (frame.Operation != ProfileResultCallback) throw new NotSupportedException("Unknown extended profile callback.");
        var id = reader.ReadUInt64(); var kind = (NativePluginProfileKind)reader.ReadUInt32();
        var requested = reader.ReadUInt32(); var result = reader.ReadUInt32(); var last = reader.ReadUInt32(); var mutating = reader.ReadUInt32(); Finish(reader);
        if (mutating > 1) throw new InvalidDataException("Profile result has an invalid mutation disposition.");
        owner.ProfileResult(parent, id, kind, requested, result, last, mutating == 1);
        return Payload(writer => writer.Write(1U));
    }
}
