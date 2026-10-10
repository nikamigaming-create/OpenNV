using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutSourceTaskCohortEntry(Guid Identity, ulong Key, int? State,
    bool Retired, string? Failure, string? Boundary);
internal sealed record FalloutSourceTaskCohort(string Stack, string Executable,
    IReadOnlyList<FalloutSourceTaskCohortEntry> Tasks);
internal sealed record FalloutSourceTaskManagerThread(int Slot, int ManagedThread, Guid Process, Guid Constructor, long Changed);
internal sealed record FalloutSourceTaskManagerAttempt(Guid? Main, bool Retirement, Guid Constructor,
    int ManagedThread, int ExaminedBuckets, IReadOnlyList<Guid> Visited, int Returned,
    bool Complete, long Changed, string? FailureType, string? Error);
internal sealed record FalloutSourceTaskManagerSnapshot(string Schema, FalloutSourceTaskManagerDeclaration Source,
    FalloutMainPlayerCellSource Player, string Stack, Guid Process, Guid Constructor, long Sequence,
    IReadOnlyList<FalloutSourceTaskManagerThread> Threads, FalloutSourceTaskManagerAttempt? Last,
    string? FailureType, string? Error, FalloutActorProcessRuntimeHandoff? Cold);

// These methods are the actual task object's state, reference and virtual
// consumers. CLR Tasks, decode completion and native-node presence do not
// implement them. Missing concrete consumers remain an explicit refusal.
internal interface IFalloutSourceManagerTask
{
    Guid Identity { get; }
    Guid Process { get; }
    string Contract { get; }
    string Owner { get; }
    ulong Key { get; }
    int ReadState();
    int CompareExchangeState(int replacement, int expected);
    void AcquireIteratorReference();
    void ReleaseIteratorReference();
    void CancelVirtual(int previousState, Guid? child);
    void WaitForSourceStateChange(int state, bool yieldZero);
    int ReadSchedulerIndex(ulong key);
    bool RemoveSchedulerKey(ulong key);
    void DecrementSchedulerCounters(int index);
}
