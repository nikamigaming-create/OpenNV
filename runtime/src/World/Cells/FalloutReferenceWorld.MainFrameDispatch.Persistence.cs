namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutSourceFrameDispatchSnapshot(FalloutQueuedPrioritySnapshot Priority);

internal sealed partial class FalloutReferenceWorld
{
    private FalloutSourceFrameDispatchSnapshot CaptureSourceFrameDispatch() => new(
        (_sourceQueuePriority ?? throw new NotSupportedException("Actual native caller priority owner is absent.")).Capture());
    internal static void ValidateSourceFrameDispatch(FalloutSourceFrameDispatchSnapshot saved)
    {
        if (saved is null || saved.Priority is null)
            throw new InvalidDataException("Current source queue omitted its original caller priority/task owners.");
        FalloutQueuedReferencePriority.Validate(saved.Priority);
    }
}
