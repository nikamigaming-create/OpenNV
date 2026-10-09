namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutDetectionCache
{
    internal void ClearPendingForOriginalProducer()
    {
        if (_process != FalloutDetectionProcessLevel.High || _commitPhase != FalloutDetectionCommitPhase.Idle)
            throw new InvalidOperationException("Original High producer cannot clear an unfinished commit transaction.");
        _pendingDetected.Clear(); _pendingDetecting.Clear(); _revision = checked(_revision + 1);
    }
}
