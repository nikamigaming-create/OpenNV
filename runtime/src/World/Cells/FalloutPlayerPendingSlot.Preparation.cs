namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutPlayerPendingSlot
{
    internal void RequireSourcePreparation(FalloutPlayerPendingRequest request)
    {
        Require(request);
        if (_entered is not null || _setting || !ReferenceEquals(_pending, request) || _selectedSource is null ||
            request.SourcePayload?.FactoryReceipt?.Source.Pending.Player != _selectedSource)
            throw new InvalidOperationException("CELL value preparation requires the exact idle selected Player allocation.");
    }

    internal void RequireSourcePreparationCompleted(FalloutPlayerPendingRequest request)
    {
        ObjectDisposedException.ThrowIf(_retired, this);
        if (_entered is not null || _setting || Error is not null || _pending is not null || _revision != request.Revision ||
            _selectedSource is null || request.SourcePayload?.FactoryReceipt?.Source.Pending.Player != _selectedSource ||
            _completion is not { } completion || completion.Identity != request.Identity || completion.Revision != request.Revision)
            throw new InvalidOperationException("Prepared CELL has no exact idle Player null-store receipt.");
    }
}
