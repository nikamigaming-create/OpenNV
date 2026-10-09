using System.Collections.Immutable;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal enum NativePluginMutexApi : uint
{
    CreateA = 1, CreateW, CreateExA, CreateExW, OpenA, OpenW, Release, Close, Wait, WaitEx, WaitMany, WaitManyEx,
}
internal sealed record NativePluginMutexName(uint Address, uint Rva, string ModuleSha256, ImmutableArray<byte> Bytes,
    string WindowsName, bool Wide, uint CodePage);
internal readonly record struct NativePluginMutexHandle(ulong Capability, uint Handle);
internal sealed record NativePluginMutexRequest(NativePluginMutexApi Api, uint Thread, uint LastError,
    uint Security, uint SecurityLength, uint SecurityDescriptor, uint Inherit, uint Flags, uint Access,
    uint Timeout, uint WaitAll, uint Alertable, NativePluginMutexName? Name, IReadOnlyList<NativePluginMutexHandle> Handles);
internal sealed record NativePluginMutexReceipt(ulong Generation, ulong Module, ulong Sequence, ulong Call,
    uint Thread, NativePluginMutexApi Api, NativePluginMutexName? Name, ulong Capability, ulong Object,
    uint Handle, uint Result, uint LastError, bool DeferredDetach, string Disposition, NativePluginMutexRequest Signature);
internal sealed record NativePluginMutexDetachReceipt(ulong Generation, ulong Module, ulong Call, uint Image,
    bool Returned, uint? Result, uint? LastError);
internal sealed record NativePluginMutexPending(ulong Sequence, ulong Call, NativePluginMutexRequest Signature);
internal sealed record NativePluginMutexComparisonReceipt(uint CreatedHandle, uint KnownHandle, ulong KnownCapability,
    bool SameObject, uint LastError);

internal sealed class NativePluginMutexObject(ulong id)
{
    internal ulong Id { get; } = id;
    internal uint OwnerThread { get; set; }
    internal uint Depth { get; set; }
    // Local refs are known; foreign refs and global object destruction are not.
    internal HashSet<ulong> Handles { get; } = [];
}
internal sealed class NativePluginMutexLiveHandle(ulong capability, uint handle, NativePluginMutexObject obj)
{
    internal ulong Capability { get; } = capability;
    internal uint Handle { get; } = handle;
    internal NativePluginMutexObject Object { get; } = obj;
}
