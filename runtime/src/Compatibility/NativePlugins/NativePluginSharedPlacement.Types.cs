namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed record NativePluginSharedPlacementRequest(uint Kind, ulong Object, ulong View, uint Handle,
    ulong Maximum, ulong Offset, uint Length, uint Preferred);
internal sealed record NativePluginSharedPlacementResult(ulong Id, uint Address, uint Extent, uint Error);
internal sealed record NativePluginSharedMemoryObservation(ulong Base, ulong AllocationBase, ulong Extent,
    uint State, uint Protection, uint Type, uint AllocationProtection)
{
    internal ulong End => checked(Base + Extent);
}
internal enum NativePluginSharedPlacementMemory : byte { None, Placeholder, Mapped, SourceQuarantined, ClosedProcess }
internal sealed record NativePluginSharedPlacementApiReceipt(ulong Placement, int Process, string Operation,
    uint RequestedAddress, ulong ReturnedAddress, uint Extent, bool Succeeded, uint Error, uint PreviousProtection);
internal sealed record NativePluginSharedPlacementReceipt(ulong Id, ulong OriginalGeneration, ulong ServiceGeneration,
    int OriginalProcess, int ServiceProcess, ulong OriginalModule, uint OriginalThread, ulong OriginalCall,
    NativePluginSharedPlacementRequest Source, uint Address, uint Extent, bool Published, bool SourceReleased,
    bool CngBorrowEntered, bool CngBorrowRetired, NativePluginSharedPlacementMemory OriginalMemory,
    NativePluginSharedPlacementMemory ServiceMemory, bool ParentSectionClosed, string? Failure);

// The collection publishes this projection; mutable leases never cross into a
// plugin. A failed operation retains its exact section and every acquired VM
// range, including a partially failed independent rollback.
internal sealed class NativePluginSharedPlacementLease(ulong id, ulong call,
    NativePluginSharedPlacementRequest source)
{
    internal ulong Id { get; } = id;
    internal ulong Call { get; } = call;
    internal NativePluginSharedPlacementRequest Source { get; } = source;
    internal NativePluginCngSectionHandle? Section { get; set; }
    internal uint Address { get; set; }
    internal uint Extent { get; set; }
    internal uint OriginalAddress { get; set; }
    internal uint ServiceAddress { get; set; }
    internal NativePluginSharedPlacementMemory OriginalMemory { get; set; }
    internal NativePluginSharedPlacementMemory ServiceMemory { get; set; }
    internal bool Published { get; set; }
    internal bool SourceReleased { get; set; }
    internal bool CngBorrowEntered { get; set; }
    internal bool CngBorrowRetired { get; set; }
    internal bool ReleaseEntered { get; set; }
    internal Exception? Failure { get; set; }
}
