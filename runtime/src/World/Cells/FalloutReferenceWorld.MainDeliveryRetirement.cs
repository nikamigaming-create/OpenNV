namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    internal FalloutMainNativeDeliveryBindings? CampaignMainNativeDeliveryBindings =>
        _processRuntime?.ReadMainNativeDeliveryBindings();

    internal void RequireCampaignMainNativeDeliveryClosed() => _processRuntime?.RequireMainNativeDeliveryClosed();

    internal void CloseCampaignMainNativeDeliveryLeases(ref IDisposable? caller, ref IDisposable? fade)
    {
        if (_processRuntime is not { } process)
            throw new InvalidOperationException("Actual Main delivery lost its constructed source process before retirement.");
        process.CloseMainNativeDeliveryLeases(ref caller, ref fade);
    }
}
