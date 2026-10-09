namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed partial class NativePluginExecutionDomain
{
    private NativePluginMutexNamespaces? _mutexNamespaces;
    private readonly List<NativePluginMutexDirectoryReceipt> _retiredMutexDirectoryReceipts = [];
    private readonly List<NativePluginMutexNativeStatusReceipt> _retiredMutexNativeStatuses = [];
    internal IReadOnlyList<NativePluginMutexDirectoryReceipt> NvseMutexDirectoryReceipts =>
        _mutexNamespaces?.Receipts ?? _retiredMutexDirectoryReceipts.AsReadOnly();
    internal IReadOnlyList<NativePluginMutexNativeStatusReceipt> NvseMutexNativeStatuses =>
        _mutexNamespaces?.Statuses ?? _retiredMutexNativeStatuses.AsReadOnly();
    private bool MutexNamespaceOwnersRetired => _mutexNamespaces is null;
    private NativePluginMutexDirectoryPublication? PrepareMutexNamespace(ulong parent, NativePluginPrivateIo io,
        NativePluginMutexRequest request, NativePluginMutexWindowsCaller caller)
    {
        var named = request.Name is not null && request.Api is NativePluginMutexApi.CreateA or NativePluginMutexApi.CreateW or
            NativePluginMutexApi.CreateExA or NativePluginMutexApi.CreateExW or NativePluginMutexApi.OpenA or NativePluginMutexApi.OpenW;
        if (!named)
        {
            if (caller.Image != 0 || caller.Create != 0 || caller.Open != 0 || caller.ConvertError != 0)
                throw new InvalidDataException("Unnamed/handle mutex call invented a native namespace constructor.");
            return null;
        }
        var plugin = _nvsePlugin ?? throw new InvalidOperationException("Named mutex source has no entered original module.");
        _mutexNamespaces ??= new(_process, Generation, plugin.Module, NativeThread, io.RestrictingSid);
        return _mutexNamespaces.Acquire(parent, request.Name!, caller);
    }
    private void ObserveMutexNamespaceStatus(ulong parent, ulong id, uint valid, ulong capability, uint status,
        uint result, uint error)
    {
        var pending = _mutexLedger?.Pending ?? throw new InvalidDataException("NT mutex return has no actual C# SDK entry.");
        if (pending.Sequence != id || pending.Call != parent || valid > 1)
            throw new InvalidDataException("NT mutex return changed its call/sequence/status category.");
        var request = pending.Signature;
        var named = request.Name is not null && request.Api is NativePluginMutexApi.CreateA or NativePluginMutexApi.CreateW or
            NativePluginMutexApi.CreateExA or NativePluginMutexApi.CreateExW or NativePluginMutexApi.OpenA or NativePluginMutexApi.OpenW;
        if (!named)
        {
            if (valid != 0 || capability != 0 || status != 0)
                throw new InvalidDataException("Unnamed/handle mutex result fabricated a directory/NT status.");
            return;
        }
        if (valid == 0) throw new InvalidDataException("Actual named mutex return omitted its native NTSTATUS.");
        (_mutexNamespaces ?? throw new InvalidDataException("Named mutex has no directory owner.")).ObserveStatus(parent, id,
            capability, status, result, error, request.LastError, request.Api is NativePluginMutexApi.OpenA or NativePluginMutexApi.OpenW);
    }
    private byte[] ObserveMutexNamespaceClose(ulong parent, BinaryReader reader)
    {
        var capability = reader.ReadUInt64(); var handle = reader.ReadUInt32();
        var result = reader.ReadUInt32(); var error = reader.ReadUInt32(); Finish(reader);
        if (!_cngOriginalUnloading || _operation != NativePluginDomainOperation.UnloadNvse.ToString())
            throw new InvalidDataException("Directory closure is outside the returned actual original unload scope.");
        (_mutexLedger ?? throw new InvalidOperationException("Directory close has no source mutex ledger.")).RequireRetired();
        (_mutexNamespaces ?? throw new InvalidDataException("Directory close has no retained namespace source.")).ObserveClose(parent, capability, handle, result, error);
        return Payload(writer => writer.Write(1U));
    }
    private void ClearMutexNamespacesAfterChildExit()
    {
        if (!ChildExited) throw new InvalidOperationException("Mutex directories/Windows source require actual original child closure.");
        if (_mutexNamespaces is null) return;
        try { _mutexNamespaces.RetireAfterChildExit(); }
        finally
        {
            _retiredMutexDirectoryReceipts.Clear(); _retiredMutexDirectoryReceipts.AddRange(_mutexNamespaces.Receipts);
            _retiredMutexNativeStatuses.Clear(); _retiredMutexNativeStatuses.AddRange(_mutexNamespaces.Statuses);
            _retiredMutexFallbackObservations.Clear(); _retiredMutexFallbackObservations.AddRange(_mutexNamespaces.FallbackObservations);
        }
        _mutexNamespaces = null;
    }
}
