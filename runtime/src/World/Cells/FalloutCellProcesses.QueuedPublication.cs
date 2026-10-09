using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutCellProcesses
{
    internal void RequireQueuedActorPublication(FalloutFormKey reference, ulong actualNativeRoot)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (actualNativeRoot == 0) throw new InvalidDataException("Queued source actor publication has no actual native identity.");
        var owners = _attachments.Values.Where(attachment => !attachment.Retired && attachment.RootPublished &&
            attachment.Process == _process && attachment.Failure is null && attachment.Children.Any(child =>
                child.Source.Reference == reference && child.Phase == FalloutCellProcessChildPhase.Published &&
                child.Failure is null && child.NativeObjects.Contains(actualNativeRoot))).ToArray();
        if (owners.Length != 1 || owners[0].CellEpochs.Any(cell =>
                RequireHealthy(cell.Key).Epoch != cell.Value || RequireHealthy(cell.Key).Phase != FalloutCellProcessPhase.Attached))
            throw new NotSupportedException("Queued native actor has no single returned current source CELL publication/epoch.");
    }
}

internal sealed partial class FalloutReferenceWorld
{
    internal void RequireSourceQueuedActorPublication(FalloutFormKey reference, ulong actualNativeRoot) =>
        CellProcesses.RequireQueuedActorPublication(reference, actualNativeRoot);
    internal void ForgetRetiredSourceQueuedRead(Guid identity) =>
        (_queuedReferenceWork ?? throw new NotSupportedException("Actual source read registry is absent.")).ForgetRetired(identity);
}
