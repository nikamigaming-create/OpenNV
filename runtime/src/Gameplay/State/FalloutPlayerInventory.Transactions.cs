using System.Text.Json;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutInventoryTransfer(FalloutFormKey Item, int Count);
internal sealed class FalloutInventoryCleanupException(string message, Exception cause)
    : InvalidOperationException(message, cause)
{ }

internal sealed partial class FalloutPlayerInventory
{
    internal Func<FalloutOpeningInventoryGrant, FalloutOpeningInventoryGrant, FalloutInventoryChangeLease?>? PrepareChange { get; set; }
    private bool _changeInProgress;

    private void RequireNoChangeInProgress()
    {
        if (_scriptIterationActive && !_scriptTraversalOwned)
            throw new NotSupportedException("Inventory mutation requires its retained native script traversal/restart owner.");
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

    internal void ModifyWithPreparedOwner(Action<FalloutPlayerInventory> plan, FalloutInventoryChangeLease effects) =>
        ApplyRemovalTransaction(null, (staged, _) => plan(staged), effects);

    internal void TransferItemsWithPreparedOwner(FalloutPlayerInventory target,
        IReadOnlyList<FalloutInventoryTransfer> transfers, FalloutInventoryChangeLease effects,
        Action<FalloutPlayerInventory, FalloutPlayerInventory> stage)
    {
        ArgumentNullException.ThrowIfNull(transfers); ArgumentNullException.ThrowIfNull(stage);
        ArgumentNullException.ThrowIfNull(effects);
        if (transfers.Any(transfer => transfer is null)) throw new InvalidDataException("Inventory event transfer is absent.");
        ApplyRemovalTransaction(target, (source, destination) =>
        {
            // The source event owner stages the actual incoming variants and
            // dispatches their events in the same detached inventory plan.
            stage(source, destination!);
        }, effects);
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

    internal void ConsumeDeployedWeapon(FalloutFormKey weapon, FalloutItemVariant selected, long revision,
        FalloutInventoryChangeLease publication)
    {
        RequireRevision(revision);
        if (Item(weapon) is not { RecordType: "WEAP" } item || !Equipped.Contains(item.RuntimeFormId) || SelectedWeapon(weapon).Extra != selected)
            throw new InvalidOperationException("Deployed weapon selection changed before source release.");
        // The ordinary consuming weapon removes from the selected stack while
        // retaining equipment if another instance remains. Drop is different.
        ConsumeOne(weapon, publication);
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
        if (_scriptIterationActive && !_scriptTraversalOwned || other is { _scriptIterationActive: true, _scriptTraversalOwned: false })
            throw new NotSupportedException("Inventory mutation requires its retained native script traversal/restart owner.");
        if (_changeInProgress || other?._changeInProgress == true)
            throw new InvalidOperationException("Inventory removal cannot interleave another authoritative transaction.");
        _changeInProgress = true;
        if (other is not null) other._changeInProgress = true;
        FalloutInventoryChangeLease? first = null, second = null;
        var ownersDisposed = false;
        void DisposeOwners(Exception? transactionFailure)
        {
            if (ownersDisposed) return;
            ownersDisposed = true;
            var cleanupFailures = new List<Exception>();
            foreach (var owner in new[] { effects, second, first })
                try { owner?.Dispose(); } catch (Exception error) { cleanupFailures.Add(error); }
            if (cleanupFailures.Count == 0) return;
            if (transactionFailure is not null) cleanupFailures.Insert(0, transactionFailure);
            throw new FalloutInventoryCleanupException("Inventory cleanup failed: " +
                string.Join("; ", cleanupFailures.Select(error => error.Message)), new AggregateException(cleanupFailures));
        }
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
            if (!changed && !otherChanged && effects is null)
            {
                _scriptMutationRevision = staged._scriptMutationRevision;
                if (other is not null) other._scriptMutationRevision = stagedOther!._scriptMutationRevision;
                return;
            }
            var revision = Revision; var otherRevision = other?.Revision ?? 0;
            if (changed) _ = checked(revision + 1);
            if (otherChanged) _ = checked(otherRevision + 1);
            first = changed ? PrepareChange?.Invoke(before.Capture(), after) : null;
            second = otherChanged ? other!.PrepareChange?.Invoke(beforeOther!.Capture(), afterOther!) : null;
            try
            {
                if (changed) PublishPrepared(staged, revision + 1);
                if (otherChanged) other!.PublishPrepared(stagedOther!, otherRevision + 1);
                _scriptMutationRevision = staged._scriptMutationRevision;
                if (other is not null) other._scriptMutationRevision = stagedOther!._scriptMutationRevision;
                first?.Commit(); second?.Commit(); effects?.Commit();
            }
            catch
            {
                // Rollback callbacks run with the original item state restored.
                if (changed) PublishPrepared(before, revision);
                if (otherChanged) other!.PublishPrepared(beforeOther!, otherRevision);
                _scriptMutationRevision = before._scriptMutationRevision;
                if (other is not null) other._scriptMutationRevision = beforeOther!._scriptMutationRevision;
                throw;
            }
            // All state is committed. Attempt every owner's finalization even
            // if an earlier callback fails; compensation is no longer valid.
            var finalizationFailures = new List<Exception>();
            foreach (var finalizer in new[] { first, second, effects })
                try { finalizer?.Complete(); } catch (Exception error) { finalizationFailures.Add(error); }
            if (finalizationFailures.Count != 0)
                throw new InvalidOperationException("Committed inventory finalization failed.",
                    new AggregateException(finalizationFailures));
        }
        catch (Exception error)
        {
            // Own compensation before publishing an escaping failure. In
            // particular, an unhandled rethrow must not be relied on to enter
            // this method's finally and discover its rollback exception there.
            DisposeOwners(error);
            throw;
        }
        finally
        {
            try { DisposeOwners(null); }
            finally
            {
                _changeInProgress = false;
                if (other is not null) other._changeInProgress = false;
            }
        }
    }

    private void PublishPrepared(FalloutPlayerInventory prepared, long revision)
    {
        _items.Clear(); foreach (var item in prepared._items) _items.Add(item.Key, item.Value);
        _equipped.Clear(); _equipped.UnionWith(prepared._equipped);
        _random = prepared._random;
        _traversal = prepared._traversal;
        _scriptOrder = CopyScriptOrder(prepared._scriptOrder);
        _scriptInitializations = prepared._scriptInitializations.ToArray();
        _scriptMutationRevision = prepared._scriptMutationRevision;
        Revision = revision;
    }

    internal static bool SameSnapshot(FalloutOpeningInventoryGrant first, FalloutOpeningInventoryGrant second) =>
        JsonSerializer.Serialize(first) == JsonSerializer.Serialize(second);
}
