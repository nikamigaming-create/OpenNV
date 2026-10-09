using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    private IReadOnlyList<Task> StopActualQueuedSourceReads() =>
        _nativeReferences?.SourceProcessQueuesConfigured == true ? _nativeReferences.StopActualQueuedSourceReads() : [];
    private void RetireReturnedActualQueuedSourceReads()
    {
        if (_nativeReferences?.SourceProcessQueuesConfigured == true) _nativeReferences.RetireReturnedActualQueuedSourceReads();
    }
}
