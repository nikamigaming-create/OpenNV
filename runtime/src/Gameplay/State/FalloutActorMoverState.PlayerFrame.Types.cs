using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutActorMoverFrameInvocation(FalloutPlayerSourceCaller Caller, Guid PlayerProcess,
    FalloutProcessSceneQuery Common, int ThreadId);
internal enum FalloutActorMoverFrameStoreKind { Flags, DefaultVector }
internal sealed record FalloutActorMoverFrameStoreOperation(FalloutActorMoverFrameInvocation Invocation,
    Guid Factory, FalloutActorMoverFrameStoreKind Kind, long CallOrdinal, long Entered, long Changed,
    ushort? RequestedWord, uint? PreviousFlags, IReadOnlyList<FalloutActorMoverStore> Stores,
    bool Returned, string? Failure);

// Bounded latest-call history and counters share the existing mover fields.
// This sequence counts frame operations only; package mutations retain their
// separate sequence and immutable history. No per-frame journal is accumulated.
internal sealed record FalloutActorMoverPlayerFrameSnapshot(long Sequence, long FlagCalls, long VectorCalls,
    long FlagStores, long VectorXYStores, long VectorZStores,
    FalloutActorMoverFrameStoreOperation? Flags, FalloutActorMoverFrameStoreOperation? Vector,
    string? Failure);
