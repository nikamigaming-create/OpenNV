namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed record NativePluginFileSizeReceipt(ulong Ordinal, ulong Generation, ulong Parent,
    ulong Module, ulong ResourceModule, ulong Route, uint Handle, bool OutputPresent,
    ulong? BeforeBits, ulong? AfterBits, uint? SdkResult, uint? LastError,
    bool Returned, bool ReleasedAfterChildExit);

internal sealed partial class NativePluginExecutionDomain
{
    private const uint FileSizeCallback = 32;
    private NativePluginFileSizeReceipt? _fileSizeBorrow;
    private ulong _fileSizeOrdinal;
    internal NativePluginFileSizeReceipt? NvseLastFileSizeReceipt { get; private set; }
    internal ulong NvseFileSizeCalls => _fileSizeOrdinal;

    private byte[] DispatchSharedFileSize(ulong parent, ulong module, BinaryReader reader)
    {
        var stage = reader.ReadUInt32(); var ordinal = reader.ReadUInt64();
        var resource = reader.ReadUInt64(); var route = reader.ReadUInt64(); var handle = reader.ReadUInt32();
        var present = reader.ReadUInt32(); var before = reader.ReadUInt64(); var after = reader.ReadUInt64();
        var result = reader.ReadUInt32(); var last = reader.ReadUInt32(); Finish(reader);
        var io = _privateIo ?? throw new NotSupportedException("Shared file size has no actual retained process file owner.");
        _ = RetainedNvseModule(module);
        if (_primaryNvseModule is not { } provider || resource != provider.Module || provider.Generation != Generation ||
            provider.Phase is NativeNvsePhase.Retired or NativeNvsePhase.Faulted || present > 1 || ordinal == 0 ||
            present == 0 && (before != 0 || after != 0) ||
            route == 0 && handle is not (0 or uint.MaxValue) ||
            route != 0 && (!_ioFiles.TryGetValue(route, out var file) || file.Handle != handle))
            throw new InvalidDataException("Shared file size changed its current real HANDLE, module or output lifetime.");
        if (stage == 1)
        {
            if (_fileSizeBorrow is not null || ordinal != checked(_fileSizeOrdinal + 1) || result != 0 || last != 0 || after != 0)
                throw new InvalidDataException("Shared file size repeated or changed its entered native call prefix.");
            _fileSizeOrdinal = ordinal;
            _fileSizeBorrow = new(ordinal, Generation, parent, module, resource, route, handle, present != 0,
                present != 0 ? before : null, null, null, null, false, false);
            NvseLastFileSizeReceipt = _fileSizeBorrow;
        }
        else if (stage == 2)
        {
            if (_fileSizeBorrow is not { } entered || entered.Ordinal != ordinal || entered.Parent != parent ||
                entered.Module != module || entered.ResourceModule != resource || entered.Route != route ||
                entered.Handle != handle || entered.OutputPresent != (present != 0) ||
                entered.BeforeBits != (present != 0 ? (ulong?)before : null) || result != 0 && present == 0)
                throw new InvalidDataException("Shared file size lost its actual entered caller/output prefix.");
            // Retain the real SDK output before independent host bookkeeping.
            // A bookkeeping failure must not erase the returned OS observation.
            _fileSizeBorrow = entered with
            {
                AfterBits = present != 0 ? after : null,
                SdkResult = result,
                LastError = last
            };
            NvseLastFileSizeReceipt = _fileSizeBorrow;
            if (route != 0) io.RecordHandle(parent, route, 11, 0, result != 0 ? 0 : last);
            NvseLastFileSizeReceipt = _fileSizeBorrow with { Returned = true };
            _fileSizeBorrow = null;
        }
        else throw new InvalidDataException("Shared file size has no declared invocation stage.");
        return Payload(writer => writer.Write(1U));
    }
    private void RequireNativeFileSizeRetired()
    {
        if (_fileSizeBorrow is null) return;
        if (!ChildExited) throw new InvalidDataException("Shared file size still borrows its actual native caller and HANDLE.");
        NvseLastFileSizeReceipt = _fileSizeBorrow with { ReleasedAfterChildExit = true };
        _fileSizeBorrow = null;
    }
    private void RequireNativeFileSizeSaveOwned()
    {
        if (_fileSizeOrdinal != 0)
            throw new NotSupportedException("Original shared-HANDLE size caller buffers/native globals have no complete cold owner.");
    }
}
