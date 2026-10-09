using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal enum FalloutMainQueueFrameStep
{
    WorkingContextFive, FirstEntryWalk, WorldPrelude, TaskPrelude, TaskContext,
    PlayerCurrentCell, WorldFinal, FirstChild, SecondEntryWalk, ArrayRelease, FinalChild, Complete
}
internal sealed record FalloutMainQueueFrameReceipt(Guid Invocation, string Owner, bool Full,
    uint BeforeWord, FalloutMainQueueFrameStep Next, bool ConsumerEntered,
    long Entered, long Changed, string? Failure);
internal sealed record FalloutMainFrameSnapshot(string Schema, string Contract, string Stack,
    Guid CapturedProcess, long Sequence, uint Word, IReadOnlyList<FalloutMainQueueFrameReceipt> Windows,
    string? Boundary, FalloutActorProcessRuntimeHandoff? ColdHandoff);

// Every callback names the actual selected source consumer. An empty renderer
// collection is not evidence that the original Main array/child is empty.
internal interface IFalloutMainQueueFrameConsumers
{
    string Owner { get; }
    void SetWorkingContextFive();
    void WalkFirstEntries();
    void WorldPrelude();
    void TaskPrelude();
    void TaskContext();
    void PlayerCurrentCell();
    void WorldFinal();
    void FirstChild();
    void WalkSecondEntries();
    void ReleaseArray();
    void FinalChild();
}

internal sealed record FalloutQueuedPriorityCell(FalloutCellProcessIdentity Source, Guid Instance,
    bool Interior, int? X, int? Y);
internal sealed record FalloutQueuedPriorityCache(FalloutQueuedPriorityCell Cell, int Priority, long Changed);
internal enum FalloutSourceFloatRounding { NearestEven, Down, Up, TowardZero }
internal sealed record FalloutQueuedPriorityPosition(uint XBits, uint YBits,
    FalloutActorProcessFact<FalloutSourceFloatRounding> Rounding);
internal sealed record FalloutQueuedPriorityInputs(Func<FalloutActorProcessFact<uint>> GridDiameter,
    FalloutActorProcessFact<int> GridX, FalloutActorProcessFact<int> GridY,
    FalloutQueuedPriorityPosition? Position, string Owner);
internal sealed record FalloutQueuedPrioritySnapshot(string Schema, string Contract, string Stack,
    Guid CapturedProcess, long Sequence, FalloutQueuedPriorityCache? Cache,
    FalloutActorProcessRuntimeHandoff? ColdHandoff);

internal enum FalloutSourceQueuedDispatch { Queued, Inline }
internal sealed record FalloutSourceQueuedRoute(FalloutSourceQueuedDispatch Route, string Owner,
    uint? CallingThread, uint? MainThread);
