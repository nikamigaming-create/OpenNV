namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutPlayerMoves
{
    internal void EnqueueSource(FalloutPlayerMove move, FalloutPlayerTransferPayload payload)
    {
        if (Error is not null) throw new NotSupportedException("Player movement retains its source failure: " + Error);
        if (payload.FactoryReceipt is not { Writer: FalloutPlayerRawTransferWriter.MoveTo } receipt ||
            receipt.RequestSource != move.Source || receipt.Target != move.Destination ||
            receipt.Offsets != FalloutPlayerTransferVector.Read([move.X, move.Y, move.Z]))
            throw new InvalidDataException("MoveTo request differs from its actual raw allocation writer.");
        FalloutPlayerRawTransferFactory.Require(payload, receipt.Source);
        _ = SourcePending.StoreSource(FalloutPlayerPendingKind.MoveTo, move, null,
            "actual-source-Player-MoveTo-command", payload);
    }
}
