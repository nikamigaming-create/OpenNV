using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal enum FalloutMainPlayerCellStep
{
    PendingRead, PendingWorldPrelude, PendingOwnedChildRelease, PendingDestination,
    PendingSceneScalar, PendingCallback, PendingFurniture, PendingDeferredDestruction,
    PendingNullStore, PendingFlagQueries, PendingFlagChild, HeldInterfaceQuery,
    HeldChild, SceneMode, SceneRead, SceneClock, SceneChild, ParentCellRead, PositionRead,
    InteriorQuery, ContainmentQuery, TargetCellRead, TargetPhaseQuery, WorldLoad,
    WorldBracketSet, PlayerBracketSet, CellAttach, RootStore, PlayerBracketClear,
    WorldBracketClear, OptionalTreeChild,
}
internal enum FalloutMainPlayerCellDisposition { Entered, PendingReturned, HeldReturned, SceneModeReturned, CellUnchanged, CellReturned, Failed }
internal sealed record FalloutMainPlayerCellChild(FalloutMainPlayerCellStep Step, long Entered,
    long? Returned, string Owner, bool? Boolean, string? FailureType, string? Error);
internal sealed record FalloutMainPlayerCellCall(Guid MainInvocation, long MainOrdinal, Guid Process,
    long Entered, long Changed, FalloutMainPlayerCellDisposition Disposition,
    Guid? Pending, FalloutPlayerPendingKind? PendingKind, bool? CachedMenu,
    FalloutCellProcessIdentity? BeforeCell, FalloutCellProcessIdentity? AfterCell,
    IReadOnlyList<FalloutMainPlayerCellChild> Children, string? FailureType, string? Error);
internal sealed record FalloutMainPlayerCellSnapshot(string Schema, FalloutMainPlayerCellSource Source,
    string Stack, Guid CapturedProcess, long Changed, long Calls, FalloutMainPlayerCellCall? LastCall,
    FalloutPlayerPendingSlotSnapshot Pending, FalloutActorProcessRuntimeHandoff? ColdHandoff,
    bool? WorldBracket, bool? PlayerBracket, FalloutMainPlayerRootBinding? RootBinding, FalloutMainPlayerPendingSnapshot PendingConsumers);
internal sealed record FalloutMainPlayerRootBinding(FalloutFormKey Cell, Guid Process, ulong NativeRoot, long Changed);
internal sealed record FalloutMainPlayerSourcePosition(uint XBits, uint YBits, uint ZBits,
    FalloutActorProcessFact<FalloutSourceFloatRounding> Rounding);
internal sealed record FalloutMainPlayerSourceCell(FalloutCellProcessIdentity Source, byte CellFlags, int? X, int? Y);
internal sealed record FalloutMainPlayerCellTarget(FalloutCellProcessIdentity Source, int SourcePhase);

// A child token is usable only while the same selected Main invocation owns the
// original Player step. Native tasks do not manufacture their own source call.
internal sealed class FalloutMainPlayerCellInvocation
{
    internal FalloutMainScriptInvocation Main { get; }
    internal FalloutMainPlayerCellStep Step { get; }
    internal FalloutActorProcessRuntimeState Owner => Main.Owner;
    internal FalloutMainPlayerCellInvocation(FalloutMainScriptInvocation main, FalloutMainPlayerCellStep step)
    { Main = main; Step = step; }
    internal void Require(FalloutMainPlayerCellStep step) => Owner.RequireMainPlayerCellChild(this, step);
}

internal interface IFalloutMainPlayerCellConsumers
{
    FalloutMainPlayerCellSource Source { get; }
    string Owner { get; }
    void PendingWorldPrelude(FalloutMainPlayerCellInvocation invocation);
    void ReleasePendingOwnedChild(FalloutMainPlayerCellInvocation invocation);
    Task<bool> TransferPending(FalloutMainPlayerCellInvocation invocation, FalloutPlayerPendingRequest request);
    void StorePendingSceneScalar(FalloutMainPlayerCellInvocation invocation, FalloutPlayerPendingRequest request);
    void InvokePendingCallback(FalloutMainPlayerCellInvocation invocation, FalloutPlayerPendingRequest request);
    void PendingFurniture(FalloutMainPlayerCellInvocation invocation, FalloutPlayerPendingRequest request);
    void RetirePendingDeferredChildren(FalloutMainPlayerCellInvocation invocation);
    bool PendingFlagQueries(FalloutMainPlayerCellInvocation invocation);
    void PendingFlagChild(FalloutMainPlayerCellInvocation invocation);
    bool HeldInterface(FalloutMainPlayerCellInvocation invocation);
    void HeldChild(FalloutMainPlayerCellInvocation invocation);
    bool SceneMode(FalloutMainPlayerCellInvocation invocation);
    bool ScenePresent(FalloutMainPlayerCellInvocation invocation);
    void SceneClock(FalloutMainPlayerCellInvocation invocation);
    void SceneChild(FalloutMainPlayerCellInvocation invocation, bool alternate);
    FalloutMainPlayerSourceCell? ParentCell(FalloutMainPlayerCellInvocation invocation);
    FalloutMainPlayerSourcePosition Position(FalloutMainPlayerCellInvocation invocation);
    FalloutMainPlayerCellTarget? TargetCell(FalloutMainPlayerCellInvocation invocation, FalloutMainPlayerSourceCell before,
        FalloutMainPlayerSourcePosition position);
    Task LoadTarget(FalloutMainPlayerCellInvocation invocation, FalloutMainPlayerCellTarget target,
        FalloutMainPlayerSourcePosition position);
    void SetWorldBracket(FalloutMainPlayerCellInvocation invocation, bool value);
    void SetPlayerBracket(FalloutMainPlayerCellInvocation invocation, bool value);
    void AttachCell(FalloutMainPlayerCellInvocation invocation, FalloutMainPlayerCellTarget target);
    void StoreRoot(FalloutMainPlayerCellInvocation invocation, FalloutMainPlayerCellTarget target);
    void OptionalTreeChild(FalloutMainPlayerCellInvocation invocation);
}
