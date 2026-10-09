namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutQueuedTaskPriorityEntry(Guid Identity, ulong Key, int? State,
    IReadOnlyList<Guid?> Children, bool Retired, long Changed, string? Failure, string? Boundary);
internal sealed record FalloutQueuedTaskPrioritySnapshot(string Schema, string Contract, string Stack,
    Guid CapturedProcess, long Sequence, IReadOnlyList<FalloutQueuedTaskPriorityEntry> Tasks,
    FalloutActorProcessRuntimeHandoff? ColdHandoff);
internal sealed record FalloutSourceTaskKeyChange(Guid Identity, int Requested, ulong Before, ulong After,
    int ObservedState, bool Accepted, bool Removed, bool Requeued);

// The manager's actual source enqueue/removal owns these callbacks. A Task's
// completion Boolean never supplies its native/source state word.
internal interface IFalloutSourceTaskPriorityConsumer
{
    int ReadState(Guid task);
    int CompareExchangeState(Guid task, int replacement, int expected);
    void StoreKey(Guid task, ulong key);
    void Remove(Guid task);
    void Enqueue(Guid task);
}
