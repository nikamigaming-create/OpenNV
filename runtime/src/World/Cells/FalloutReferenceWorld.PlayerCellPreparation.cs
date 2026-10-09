namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    internal FalloutPlayerCellPreparation PrepareSourcePlayerCellPlacement(FalloutPlayerMove move)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var request = PlayerMoves.SourcePending.Next ?? throw new InvalidOperationException("Initial placement has no actual pending request.");
        if (!ReferenceEquals(request.Move, move)) throw new InvalidOperationException("Initial placement selected a different MoveTo invocation.");
        var payload = ReadMainPlayerPendingPayload(request); RequireSourcePlayerRawTransfer(payload);
        var cell = ReadPreparationCell(payload);
        return FalloutPlayerCellPreparation.Create(PlayerRawTransferSource, PreparationStack, ActualSourceProcessIdentity,
            PlayerMoves.SourcePending, request, cell);
    }

    internal void RequireCurrentSourcePlayerCellPlacement(FalloutPlayerCellPreparation prepared)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); ArgumentNullException.ThrowIfNull(prepared);
        RequireSourcePlayerRawTransfer(prepared.Payload);
        prepared.RequireCurrent(PlayerRawTransferSource, PreparationStack, ActualSourceProcessIdentity,
            PlayerMoves.SourcePending, ReadPreparationCell(prepared.Payload));
    }

    internal FalloutPlayerCellPreparationSnapshot CaptureSourcePlayerCellPlacement(FalloutPlayerCellPreparation prepared)
    { RequireCurrentSourcePlayerCellPlacement(prepared); return prepared.Capture(); }

    internal FalloutPlayerCellPreparation RestoreSourcePlayerCellPlacement(FalloutPlayerCellPreparationSnapshot saved)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); FalloutPlayerCellPreparation.Validate(saved);
        RequireSourcePlayerRawTransfer(saved.Request.SourcePayload!);
        return FalloutPlayerCellPreparation.Restore(saved, PlayerRawTransferSource, PreparationStack, ActualSourceProcessIdentity,
            PlayerMoves.SourcePending, ReadPreparationCell(saved.Request.SourcePayload!));
    }

    internal void RequireReturnedSourcePlayerCellPlacement(FalloutPlayerCellPreparation prepared)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); ArgumentNullException.ThrowIfNull(prepared);
        RequireSourcePlayerRawTransfer(prepared.Payload);
        if (ReadPreparationCell(prepared.Payload) != prepared.SourceCell)
            throw new InvalidDataException("Returned initial placement changed its winning source CELL.");
        ProcessRuntime.RequireReturnedPlayerCellPreparation(prepared);
    }

    private string PreparationStack => _processRuntimeStack ?? throw new NotSupportedException("Initial CELL preparation has no actual selected process stack.");
    private FalloutMainPlayerSourceCell ReadPreparationCell(FalloutPlayerTransferPayload payload)
    {
        if (payload.Target != FalloutPlayerTransferTarget.Cell || payload.Cell is not { } cell)
            throw new NotSupportedException("Initial worldspace/reference preparation requires its actual Main conversion/consumer.");
        return ReadMainPlayerSourceCell(cell);
    }
}
