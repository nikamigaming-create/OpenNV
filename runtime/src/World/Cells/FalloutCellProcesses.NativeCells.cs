using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutCellProcesses
{
    internal FalloutCellNativeSource ReadNativeCellSource(FalloutFormKey cell) => _source.ReadNativeCellSource(cell);
    internal void FailNativeCell(Guid attachment, FalloutFormKey cell, Exception error, IReadOnlyList<ulong> nativeObjects)
    {
        var owner = RequireAttachment(attachment); var index = NativeCellIndex(owner, cell);
        if (nativeObjects.Any(value => value == 0) || nativeObjects.Distinct().Count() != nativeObjects.Count)
            throw new InvalidDataException("Failed terrain construction lost its actual still-owned native identities.");
        var retained = owner.CellConsumers[index].NativeObjects.Concat(nativeObjects).Distinct().ToArray();
        RequireExclusiveNativeCellObjects(owner, index, retained);
        var entries = owner.CellConsumers.ToArray();
        entries[index] = entries[index] with { Phase = FalloutCellProcessChildPhase.Failed, NativeObjects = retained,
            Owner = "actual-source-LAND-native-factory-failed:" + (string.IsNullOrWhiteSpace(error.Message) ? error.GetType().Name : error.Message) };
        _attachments[attachment] = owner with { CellConsumers = entries };
        FailSharedGraph(attachment, error);
    }
    private FalloutCellNativeConsumers ConstructNativeCell(FalloutFormKey cell)
    {
        var source = _source.ReadNativeCellSource(cell);
        return new(source, source.Landscape is null ? FalloutCellProcessChildPhase.SourceNoDraw : FalloutCellProcessChildPhase.Pending,
            [], source.Landscape is null ? "source-CELL-outside-exterior-LAND-consumer-scope" : "actual-source-LAND-consumer-pending");
    }
    internal void CompleteNativeCell(Guid attachment, FalloutFormKey cell, FalloutCellNativeSource source,
        IReadOnlyList<ulong> nativeObjects, string originalOwner)
    {
        var owner = RequireAttachment(attachment);
        Operation(owner.CellEpochs.Keys.ToArray(), originalOwner, () =>
        {
            var index = NativeCellIndex(owner, cell); var pending = owner.CellConsumers[index];
            if (source != _source.ReadNativeCellSource(cell) || source != pending.Source || owner.Retired ||
                owner.NativeRoot == 0 || pending.Phase != FalloutCellProcessChildPhase.Pending || source.Landscape is null ||
                nativeObjects.Count == 0 || nativeObjects.Any(value => value == 0) || nativeObjects.Distinct().Count() != nativeObjects.Count)
                throw new InvalidDataException("Native CELL terrain consumer has no exact pending winning-source/native lease.");
            RequireExclusiveNativeCellObjects(owner, index, nativeObjects);
            var entries = owner.CellConsumers.ToArray();
            entries[index] = pending with { Phase = FalloutCellProcessChildPhase.Published, NativeObjects = nativeObjects.ToArray(), Owner = originalOwner };
            _attachments[attachment] = owner with { CellConsumers = entries };
        });
    }
    internal void RetireNativeCell(Guid attachment, FalloutFormKey cell, string originalOwner)
    {
        var owner = RequireAttachment(attachment);
        var graph = ActiveSharedGraph(attachment);
        Operation(owner.CellEpochs.Keys.ToArray(), originalOwner, () =>
        {
            var shared = graph is { Phase: FalloutCellSharedGraphPhase.Publishing or FalloutCellSharedGraphPhase.Cancelling };
            if (shared && !SharedRetiringCells(graph!).Contains(cell, FalloutFormKeyComparer.Instance))
                throw new InvalidOperationException("Shared graph cannot retire a retained actual CELL terrain consumer.");
            if (!shared &&
                owner.CellEpochs.Keys.Any(key => Require(key).Phase != FalloutCellProcessPhase.Detaching))
                throw new InvalidOperationException("Actual terrain retirement precedes CELL graph/root detach entry.");
            var index = NativeCellIndex(owner, cell); var entries = owner.CellConsumers.ToArray();
            entries[index] = entries[index] with { Phase = FalloutCellProcessChildPhase.Retired, NativeObjects = [], Owner = originalOwner };
            _attachments[attachment] = owner with { CellConsumers = entries };
            if (shared)
                StoreGraph(graph! with { DestroyedCellConsumers = graph!.DestroyedCellConsumers.Append(cell).Distinct(FalloutFormKeyComparer.Instance).ToArray() });
        });
    }
    private void RequireNativeCellsPublished(FalloutCellProcessAttachment attachment)
    {
        if (attachment.CellConsumers.Count != attachment.CellEpochs.Count ||
            !attachment.CellConsumers.Select(value => value.Source.Cell.Cell).ToHashSet(FalloutFormKeyComparer.Instance)
                .SetEquals(attachment.CellEpochs.Keys))
            throw new InvalidDataException("Native shared root does not account for every current source CELL terrain scope.");
        foreach (var (entry, index) in attachment.CellConsumers.Select((value, index) => (value, index)))
        {
            if (entry.Source != _source.ReadNativeCellSource(entry.Source.Cell.Cell) ||
                entry.Source.Landscape is null && (entry.Phase != FalloutCellProcessChildPhase.SourceNoDraw || entry.NativeObjects.Count != 0) ||
                entry.Source.Landscape is not null && (entry.Phase != FalloutCellProcessChildPhase.Published || entry.NativeObjects.Count == 0))
                throw new InvalidDataException("Actual CELL terrain publication has an unreturned source/native consumer.");
            RequireExclusiveNativeCellObjects(attachment, index, entry.NativeObjects);
        }
    }
    private void RequireExclusiveNativeCellObjects(FalloutCellProcessAttachment owner, int ownIndex, IReadOnlyList<ulong> objects)
    {
        var claimed = objects.ToHashSet();
        foreach (var attachment in _attachments.Values.Where(value => !value.Retired))
            if (claimed.Contains(attachment.NativeRoot) || attachment.Children.Any(child => child.NativeObjects.Any(claimed.Contains)) ||
                attachment.CellConsumers.Where(value => attachment.Identity != owner.Identity ||
                    value.Source.Cell.Cell != owner.CellConsumers[ownIndex].Source.Cell.Cell)
                    .Any(value => value.NativeObjects.Any(claimed.Contains)))
                throw new InvalidDataException("Actual terrain consumer borrowed a root, reference child or another CELL's native lease.");
    }
    private static int NativeCellIndex(FalloutCellProcessAttachment attachment, FalloutFormKey cell)
    {
        var matches = attachment.CellConsumers.Select((value, index) => (value, index)).Where(value => value.value.Source.Cell.Cell == cell).ToArray();
        return matches.Length == 1 ? matches[0].index : throw new InvalidDataException("Native CELL consumer has a foreign/ambiguous source scope.");
    }
}
