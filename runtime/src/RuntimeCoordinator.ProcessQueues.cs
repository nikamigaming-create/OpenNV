using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    private IReadOnlyList<Task> StopActualQueuedSourceReads()
    {
        BindActualNativeQueuedCallerThread();
        return _nativeReferences?.ActualQueuedReadRegistryConstructed == true ? _nativeReferences.StopActualQueuedSourceReads() : [];
    }
    private void RetireReturnedActualQueuedSourceReads()
    {
        BindActualNativeQueuedCallerThread();
        RequireNativeQueuedActorCallersRetired();
        if (_nativeReferences?.ActualQueuedReadRegistryConstructed == true) _nativeReferences.RetireReturnedActualQueuedSourceReads();
    }
}
