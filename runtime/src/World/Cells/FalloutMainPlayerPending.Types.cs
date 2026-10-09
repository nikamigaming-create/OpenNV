using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutPlayerOwnedChildMutation(long Changed, Guid? Before, Guid? After, string Owner, bool Returned,
    string? FailureType, string? Error);
internal sealed record FalloutMainPlayerPendingSnapshot(string Schema, FalloutMainPlayerPendingSource Source, string Stack,
    Guid CapturedProcess, long Sequence, Guid? OwnedChild, byte Flags, FalloutPlayerOwnedChildMutation? ChildMutation,
    FalloutPlayerControllerScalarReceipt? Scalar, long Callbacks, Guid? LastCallbackRequest, Guid? LastFurnitureRequest,
    long FlagStores, string? FailureType, string? Error, FalloutActorProcessRuntimeHandoff? Handoff,
    FalloutExteriorCellLoaderSnapshot ExteriorLoaders, FalloutCharacterControllerSnapshot? CharacterController);
internal sealed record FalloutExteriorCellLoaderTaskSource(Guid Identity, uint Key, FalloutCellProcessIdentity Cell,
    int X, int Y, Guid Invocation, string Owner);
internal sealed record FalloutExteriorCellLoaderCancellation(Guid Main, IReadOnlyList<Guid> Tasks, int Returned,
    string? FailureType, string? Error, long Changed);
internal sealed record FalloutExteriorCellLoaderSnapshot(string Source, string Stack, Guid Process,
    long Sequence, IReadOnlyList<FalloutExteriorCellLoaderTaskSource> Tasks,
    FalloutExteriorCellLoaderCancellation? LastCancellation, string? FailureType, string? Error,
    FalloutActorProcessRuntimeHandoff? Handoff);

internal interface IFalloutExteriorCellLoaderTask
{
    FalloutExteriorCellLoaderTaskSource Source { get; }
    // This is the original TaskManager cancellation child, not cancellation of
    // an arbitrary CLR decoder Task and not actual native destruction.
    void RequestSourceCancellation(FalloutMainPlayerCellInvocation invocation);
    bool Retired { get; }
}
