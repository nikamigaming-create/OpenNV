namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed record NativePluginMutexWindowsCaller(uint Image, uint Create, uint Open, uint ConvertError);
internal sealed record NativePluginMutexDirectoryPublication(ulong Capability, uint Handle, string Directory, byte[] RelativeName);
internal sealed record NativePluginMutexDirectoryReceipt(ulong Generation, ulong Module, ulong Call, uint Thread,
    ulong Capability, string Scope, string Directory, uint Session, uint Handle, string WindowsSha256, string DescriptorSha256,
    uint? RestrictedOpenStatus, uint? RestrictedOpenError, bool Published, bool NativeClosed, uint? NativeCloseResult,
    uint? NativeCloseError, bool ChildClosed, bool ParentClosed);
internal sealed record NativePluginMutexNativeStatusReceipt(ulong Call, ulong Mutex, ulong Directory,
    uint Status, uint Result, uint LastError);

internal sealed class NativePluginMutexDirectoryLease(ulong id, string scope, string directory,
    NativePluginObjectDirectoryHandle parent, string descriptorSha256)
{
    internal ulong Id { get; } = id;
    internal string Scope { get; } = scope;
    internal string Directory { get; } = directory;
    internal NativePluginObjectDirectoryHandle Parent { get; } = parent;
    internal string DescriptorSha256 { get; } = descriptorSha256;
    internal uint? RestrictedStatus { get; set; }
    internal uint? RestrictedError { get; set; }
    internal uint Remote { get; set; }
    internal bool Published { get; set; }
    internal bool NativeClosed { get; set; }
    internal uint? CloseResult { get; set; }
    internal uint? CloseError { get; set; }
    internal bool ChildClosed { get; set; }
    internal bool ParentClosed { get; set; }
}
