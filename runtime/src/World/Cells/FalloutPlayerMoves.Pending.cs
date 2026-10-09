using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutPlayerMoves
{
    internal FalloutPlayerPendingSlot SourcePending { get; } = new();
    internal FalloutPlayerPendingRequest StoreDoor(FalloutFormKey door, string activationOwner) =>
        SourcePending.Store(FalloutPlayerPendingKind.Door, null, door, activationOwner);
    internal void RetireSourcePending() => SourcePending.Retire();
    private FalloutPlayerPendingRequest RequireMove(FalloutPlayerMove move)
    {
        var request = SourcePending.Next;
        if (request is null || request.Kind != FalloutPlayerPendingKind.MoveTo || !ReferenceEquals(request.Move, move))
            throw new InvalidOperationException("Player MoveTo completion is superseded or has another exact source request owner.");
        return request;
    }
}
