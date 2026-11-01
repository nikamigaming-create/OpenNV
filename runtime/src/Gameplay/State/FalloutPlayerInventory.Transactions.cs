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
            if (transfers.Any(transfer => transfer is null)) throw new InvalidDataException("Inventory transfer is absent.");
            foreach (var transfer in source.PlanLoadedRoundBatch(destination!, transfers.Select(value =>
                new FalloutTradeTransfer(value.Item, value.Count, true)).ToArray()))
            {
                source.TransferToCore(destination!, transfer.Form, transfer.Count, null, false);
            }
        });
    }

    internal void TransferToWithPreparedOwner(FalloutPlayerInventory target, FalloutFormKey form, int count,
        int variantIndex, FalloutInventoryChangeLease effects) =>
        ApplyRemovalTransaction(target, (source, destination) =>
            source.TransferToCore(destination!, form, count, variantIndex, false), effects);

    internal void ConsumeOne(FalloutFormKey item, FalloutInventoryChangeLease effects,
        int? variant = null, long? expectedRevision = null, Action<FalloutPlayerInventory>? stageEffects = null)
    {
        ArgumentNullException.ThrowIfNull(effects);
        if (variant is not null)
            RequireRevision(expectedRevision ?? throw new InvalidOperationException("Selected consumption has no inventory revision."));
        ApplyRemovalTransaction(null, (source, _) =>
        {
            if (source.Item(item) is not { Count: > 0 }) throw new InvalidOperationException("Consumed item is no longer present.");
            if (variant is { } selected) source.RemoveVariantCore(item, selected, 1);
            else source.RemoveCore(item, 1, true);
            stageEffects?.Invoke(source);
        }, effects);
    }

    internal void ConsumeWorldItem(FalloutInventoryChangeLease effects) =>
        ApplyRemovalTransaction(null, (_, _) => { }, effects);

    // Publish addition and a prepared external owner together. This primitive
    // does not dispatch item scripts: their instance identity and effects must
    // be prepared separately before they can use this transaction.
    internal void AddWithPreparedOwner(FalloutPluginStack records, FalloutFormKey form, int count, int level,
        bool silent, FalloutInventoryChangeLease effects, FalloutGlobalState? globals = null,
        FalloutItemVariant? extra = null, Action<FalloutFormKey>? validateAddition = null,
        Action<FalloutPlayerInventory>? stageAddition = null)
    {
        ArgumentNullException.ThrowIfNull(effects);
        IReadOnlyList<FalloutHudEvent> notices = [];
        ApplyRemovalTransaction(null, (staged, _) =>
        {
            var previous = staged.Items.ToDictionary(item => item.FormKey, item => item.Count);
            staged.Add(records, form, count, level, true, globals, extra, validateAddition);
            notices = silent ? [] : staged.Items.Select(item => new
            { item.FormKey, Count = item.Count - previous.GetValueOrDefault(item.FormKey) })
                .Where(item => item.Count > 0).Select(item => new FalloutHudEvent(FalloutHudEventKind.ItemAdded,
                    item.FormKey, item.Count)).ToArray();
            Notifications.RequirePublish(notices);
            stageAddition?.Invoke(staged);
        }, effects);
        Notifications.Publish(notices);
    }

    private void ApplyRemovalTransaction(FalloutPlayerInventory? other,
        Action<FalloutPlayerInventory, FalloutPlayerInventory?> plan, FalloutInventoryChangeLease? effects = null)
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
            staged.NormalizeMagazineAmmunition(); stagedOther?.NormalizeMagazineAmmunition();
            ValidateWeaponInstances(staged.Items);
            if (stagedOther is not null) ValidateWeaponInstances(stagedOther.Items);
            var after = staged.Capture(); var afterOther = stagedOther?.Capture();
            var changed = !SameSnapshot(before.Capture(), after) || !ReferenceEquals(before._traversal, staged._traversal);
            var otherChanged = other is not null && (!SameSnapshot(beforeOther!.Capture(), afterOther!) ||
                !ReferenceEquals(beforeOther!._traversal, stagedOther!._traversal));
            if (!changed && !otherChanged && effects is null) return;
            var revision = Revision; var otherRevision = other?.Revision ?? 0;
            if (changed) _ = checked(revision + 1);
            if (otherChanged) _ = checked(otherRevision + 1);
            first = changed ? PrepareChange?.Invoke(before.Capture(), after) : null;
            second = otherChanged ? other!.PrepareChange?.Invoke(beforeOther!.Capture(), afterOther!) : null;
            try
            {
                if (changed) PublishPrepared(staged, revision + 1);
                if (otherChanged) other!.PublishPrepared(stagedOther!, otherRevision + 1);
                first?.Commit(); second?.Commit(); effects?.Commit();
            }
            catch
            {
                // Rollback callbacks run with the original item state restored.
                if (changed) PublishPrepared(before, revision);
                if (otherChanged) other!.PublishPrepared(beforeOther!, otherRevision);
                effects?.Dispose(); second?.Dispose(); first?.Dispose();
                throw;
            }
            first?.Complete(); second?.Complete(); effects?.Complete();
        }
        finally
        {
            effects?.Dispose(); second?.Dispose(); first?.Dispose();
            _changeInProgress = false;
            if (other is not null) other._changeInProgress = false;
        }
    }

    private void PublishPrepared(FalloutPlayerInventory prepared, long revision)
    {
        _items.Clear(); foreach (var item in prepared._items) _items.Add(item.Key, item.Value);
        _equipped.Clear(); _equipped.UnionWith(prepared._equipped);
        _random = prepared._random;
        _traversal = prepared._traversal;
        Revision = revision;
    }

    internal static bool SameSnapshot(FalloutOpeningInventoryGrant first, FalloutOpeningInventoryGrant second) =>
        JsonSerializer.Serialize(first) == JsonSerializer.Serialize(second);
}
