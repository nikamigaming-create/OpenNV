namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    internal bool ActualQueuedReadRegistryConstructed => _queuedReferenceWork is not null;
    internal void BindActualQueuedNativePresentationThread(object owner) =>
        (_queuedReferenceWork ?? throw new NotSupportedException("Actual source native read provider is absent."))
            .BindNativePresentationThread(owner);
    internal void RetainSourceQueuedNativeCallerFailure(Guid identity, Exception error) =>
        QueuedReferences.RetainFailure(identity, error);
}
