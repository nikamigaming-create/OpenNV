using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutCellProcesses
{
    private bool BeginSharedRootRetirement(FalloutCellProcessAttachment owner, ulong actualRoot, string originalOwner)
    {
        var change = RequireActiveSharedGraph(owner.Identity);
        if (owner.NativeRoot != actualRoot || change.NativeRoot != actualRoot || owner.Retired)
            throw new InvalidDataException("Shared root retirement crossed its actual retained native owner.");
        foreach (var (cell, epoch) in owner.CellEpochs)
            if (Require(cell).Epoch != epoch || Require(cell).Phase is not
                (FalloutCellProcessPhase.Attaching or FalloutCellProcessPhase.Attached or FalloutCellProcessPhase.Detaching))
                throw new InvalidDataException("Shared root retirement crossed another CELL's unfinished phase/epoch.");
        foreach (var cell in owner.CellEpochs.Keys.Where(cell => Require(cell).Phase != FalloutCellProcessPhase.Detaching))
            Write(cell, FalloutCellProcessOperation.BeginDetach, FalloutCellProcessPhase.Detaching, owner.Identity, originalOwner);
        _attachments[owner.Identity] = owner with { RootPublished = false };
        StoreGraph(change with { Phase = FalloutCellSharedGraphPhase.RetiringRoot });
        return true;
    }
    private void SharedRootRetired(FalloutCellProcessAttachment owner)
    {
        if (ActiveSharedGraph(owner.Identity) is not { Phase: FalloutCellSharedGraphPhase.RetiringRoot } change) return;
        StoreGraph(change with { Phase = FalloutCellSharedGraphPhase.RootRetired,
            DestroyedReferences = owner.Children.Select(child => child.Source.Reference).ToArray(),
            DestroyedCellConsumers = owner.CellConsumers.Select(cell => cell.Source.Cell.Cell).ToArray() });
    }
    private static bool TransitionCellOwned(FalloutCellProcessesSnapshot saved, FalloutCellProcessTransition transition,
        FalloutCellProcessAttachment current)
    {
        if (current.CellEpochs.ContainsKey(transition.Cell)) return true;
        // A removed source CELL remains in its exact consumed graph receipt.
        // It is not a current native member and cannot regain residency from history.
        return saved.SharedGraphs.Any(change => change.Attachment == current.Identity &&
            (change.BeforeEpochs.TryGetValue(transition.Cell, out var before) && before == transition.CellEpoch ||
                change.AfterEpochs.TryGetValue(transition.Cell, out var after) && after == transition.CellEpoch));
    }
}
