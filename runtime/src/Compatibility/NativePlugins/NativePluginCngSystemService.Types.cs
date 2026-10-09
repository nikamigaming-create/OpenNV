namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal enum NativePluginCngServiceStep : uint
{
    Prepare = 0, Begin = 1, Write = 2, Execute = 3, Read = 4, Release = 5, Retire = 6, AbandonAfterClientExit = 7, PublishLocal = 8,
    SharedBind = 9, SharedRelease = 10, SharedBegin = 11, DetachScope = 12
}
internal sealed record NativePluginCngServiceImage(string Path, string Sha256);
internal sealed record NativePluginCngServiceResult(int Status, uint LastError, ulong Created,
    bool CopiedPresent, uint Copied, uint OutputLength);
internal sealed record NativePluginCngBufferExtent(bool Present, uint Length);
internal sealed record NativePluginCngServiceReceipt(ulong Sequence, ulong OriginalGeneration, ulong OriginalModule,
    ulong OriginalCall, uint OriginalThread, ulong ServiceGeneration, int ServiceProcess, ulong Invocation,
    NativePluginCryptoOperation Operation, ulong Target, ulong Created, int Status, uint LastError,
    bool CopiedPresent, uint Copied, uint OutputLength, string ProviderSource, string PrimitivesSource,
    uint Flags, uint CallerLastError, uint ObjectBytes, bool DestinationPresent, IReadOnlyList<NativePluginCngBufferExtent> Buffers);
internal sealed record NativePluginCngEmergencyReceipt(uint Operation, ulong Handle, int Status);
internal sealed record NativePluginCngLocalPublication(ulong OriginalCall, ulong ServiceHandle,
    uint NativeCapability, uint NativeDestination, bool Hash);

internal sealed partial class NativePluginExecutionDomain
{
    internal bool CngServiceProcessResourcesRetired => ChildExited && _process.ResourcesRetired;
    // Reuses the actual framed exchange, correlation/deadline and exact-child
    // owner. This method never starts another original module or admits its API.
    internal byte[] CngSystemExchange(byte[] request)
    {
        using var reader = Exchange(NativePluginDomainOperation.CngSystemService, request);
        var remaining = checked((int)(reader.BaseStream.Length - reader.BaseStream.Position));
        var reply = reader.ReadBytes(remaining); Finish(reader); return reply;
    }
}
