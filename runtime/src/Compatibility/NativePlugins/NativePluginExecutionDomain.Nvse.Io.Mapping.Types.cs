namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal enum NativePluginMappingOperation : uint { Create = 1, Open = 2, Map = 3, Close = 4, Unmap = 5, Flush = 6 }
internal sealed record NativePluginMappingReceipt(ulong Sequence, ulong Generation, ulong Parent,
    NativePluginMappingOperation Operation, ulong Capability, ulong Object, ulong BackingFile,
    uint HandleOrAddress, uint ProtectionOrAccess, ulong Offset, ulong LogicalBytes, ulong RegionBytes,
    uint RegionState, uint RegionProtection, uint Error, bool Success, string? DeclaredName, string? PhysicalName,
    ulong? DeclaredBackingFile, uint? DeclaredFileHandle);

internal sealed partial class NativePluginExecutionDomain
{
    private const uint MappingCallback = 20;
    private sealed record MappingObject(ulong Id, ulong BackingFile, bool Writable, ulong Maximum, uint Protection,
        string? Name, string? PhysicalName);
    private sealed record MappingHandle(ulong Id, uint Handle, ulong Object, uint RequestFlags);
    private sealed record MappingView(ulong Id, uint Address, ulong Object, uint Access, ulong Offset,
        ulong LogicalBytes, ulong RegionBytes, uint State, uint Protection);
    private sealed record MappingPlan(ulong Id, NativePluginMappingOperation Operation, ulong Object,
        ulong File, uint Handle, bool Writable, uint ProtectionOrAccess, ulong Maximum, ulong Offset,
        ulong Bytes, uint Preferred, string? Name, string? PhysicalName);
    private readonly Dictionary<ulong, MappingObject> _mappingObjects = [];
    private readonly Dictionary<ulong, MappingHandle> _mappingHandles = [];
    private readonly Dictionary<ulong, MappingView> _mappingViews = [];
    private readonly Dictionary<ulong, MappingPlan> _mappingPlans = [];
    private readonly List<NativePluginMappingReceipt> _mappingReceipts = [];
    private ulong _nextMappingCapability;
    internal IReadOnlyList<NativePluginMappingReceipt> NvseMappingReceipts => _mappingReceipts.AsReadOnly();
}
