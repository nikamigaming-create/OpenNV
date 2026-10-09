namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutPlayerPendingSlot
{
    // Only the original pending allocation/setter producer may supply this
    // complete value payload. Logical movement, body presence or door lookup
    // cannot infer callback nullness, raw rotation or transfer-argument cells.
    internal FalloutPlayerPendingRequest StoreSource(FalloutPlayerPendingKind kind, FalloutPlayerMove? move,
        OpenNV.Runtime.Content.FalloutFormKey? door, string owner, FalloutPlayerTransferPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload); payload.RequireRole(kind);
        return Store(kind, move, door, owner, payload);
    }
    private static void RequireSourcePayloadForCold(FalloutPlayerPendingSlotSnapshot saved)
    {
        if (saved.Pending?.SourcePayload?.Callback is not null)
            throw new NotSupportedException("Pending callback requires its genuine new-process native factory/context lease; a retained Guid is not that lease.");
    }
}
