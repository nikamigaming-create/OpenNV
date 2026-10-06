using System.Text.Json;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutInventoryTransfer(FalloutFormKey Item, int Count);

internal sealed partial class FalloutPlayerInventory
{
    internal Func<FalloutOpeningInventoryGrant, FalloutOpeningInventoryGrant, FalloutInventoryChangeLease?>? PrepareChange { get; set; }
    private bool _changeInProgress;

    private void RequireNoChangeInProgress()
    {
        if (_changeInProgress) throw new InvalidOperationException("Inventory mutation cannot interleave a prepared transfer.");
    }

    internal void TransferItemsTo(FalloutPlayerInventory target, IReadOnlyList<FalloutInventoryTransfer> transfers)
    {
        ArgumentNullException.ThrowIfNull(transfers);
        if (transfers.Count == 0) return;
        ApplyRemovalTransaction(target, (source, destination) =>
        {
            foreach (var transfer in transfers)
            {
                if (transfer is null) throw new InvalidDataException("Inventory transfer is absent.");
                source.TransferToCore(destination!, transfer.Item, transfer.Count, null, false);
            }
        });
    }

    private void ApplyRemovalTransaction(FalloutPlayerInventory? other,
        Action<FalloutPlayerInventory, FalloutPlayerInventory?> plan)
    {
        if (ReferenceEquals(this, other)) throw new InvalidOperationException("Inventory transfer needs distinct owners.");
        if (_changeInProgress || other?._changeInProgress == true)
            throw new InvalidOperationException("Inventory removal cannot interleave another authoritative transaction.");
        _changeInProgress = true;
        if (other is not null) other._changeInProgress = true;
        FalloutInventoryChangeLease? first = null, second = null;
        try
        {
            var before = Copy(); var beforeOther = other?.Copy();
            var staged = Copy(); var stagedOther = other?.Copy();
            plan(staged, stagedOther);
            var after = staged.Capture(); var afterOther = stagedOther?.Capture();
            var changed = !SameSnapshot(before.Capture(), after);
            var otherChanged = other is not null && !SameSnapshot(beforeOther!.Capture(), afterOther!);
            if (!changed && !otherChanged) return;
            var revision = Revision; var otherRevision = other?.Revision ?? 0;
            if (changed) _ = checked(revision + 1);
            if (otherChanged) _ = checked(otherRevision + 1);
            first = changed ? PrepareChange?.Invoke(before.Capture(), after) : null;
            second = otherChanged ? other!.PrepareChange?.Invoke(beforeOther!.Capture(), afterOther!) : null;
            try
            {
                if (changed) PublishPrepared(staged, revision + 1);
                if (otherChanged) other!.PublishPrepared(stagedOther!, otherRevision + 1);
                first?.Commit(); second?.Commit();
            }
            catch
            {
                // Rollback callbacks run with the original item state restored.
                if (changed) PublishPrepared(before, revision);
                if (otherChanged) other!.PublishPrepared(beforeOther!, otherRevision);
                second?.Dispose(); first?.Dispose();
                throw;
            }
            first?.Complete(); second?.Complete();
        }
        finally
        {
            second?.Dispose(); first?.Dispose();
            _changeInProgress = false;
            if (other is not null) other._changeInProgress = false;
        }
    }

    private void PublishPrepared(FalloutPlayerInventory prepared, long revision)
    {
        _items.Clear(); foreach (var item in prepared._items) _items.Add(item.Key, item.Value);
        _equipped.Clear(); _equipped.UnionWith(prepared._equipped);
        _random = prepared._random;
        Revision = revision;
    }

    internal static bool SameSnapshot(FalloutOpeningInventoryGrant first, FalloutOpeningInventoryGrant second) =>
        JsonSerializer.Serialize(first) == JsonSerializer.Serialize(second);
}
