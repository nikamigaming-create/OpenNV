using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutPlayerInventory
{
    // An empty new inventory has a known empty change-entry script order.
    // Null is retained missing history, never reconstructed from UI sorting.
    private IReadOnlyList<FalloutInventoryScriptOrderEntry>? _scriptOrder = [];
    private IReadOnlyList<FalloutInventoryScriptInitialization> _scriptInitializations = [];

    private static IReadOnlyList<FalloutInventoryScriptOrderEntry>? CopyScriptOrder(
        IReadOnlyList<FalloutInventoryScriptOrderEntry>? order) => order?.Select(entry =>
            entry with { Instances = entry.Instances.ToArray() }).ToArray();

    private void RestoreScriptOrder(FalloutCampaignInventory snapshot)
    {
        var actual = snapshot.Items.SelectMany(item => (item.Variants ?? [])
            .Where(value => value.ScriptState is not null).Select(value => (item.FormKey, value.ScriptState!.Identity))).ToArray();
        _scriptInitializations = snapshot.ScriptInitializations?.ToArray() ?? [];
        if (_scriptInitializations.Any(call => call is null || !actual.Any(value => value.Identity == call.Identity)))
            throw new InvalidDataException("Initial inventory script call lost its actual event-list identity.");
        var order = snapshot.ScriptOrder;
        if (order is null)
        {
            _scriptOrder = null;
            return;
        }
        if (order.Any(entry => entry is null || entry.Item.ObjectId == 0 || entry.Instances is null ||
            entry.Instances.Any(identity => identity == Guid.Empty) || entry.BaseContribution is < 0) ||
            order.Select(entry => entry.Item).Distinct().Count() != order.Count ||
            order.SelectMany(entry => entry.Instances).Distinct().Count() != order.Sum(entry => entry.Instances.Count) ||
            actual.Any(value => !order.Any(entry => entry.Item == value.FormKey && entry.Instances.Contains(value.Identity))) ||
            order.Any(entry => entry.Instances.Any(identity => !actual.Contains((entry.Item, identity)))))
            throw new InvalidDataException("Inventory script traversal does not own the retained event-list identities.");
        _scriptOrder = CopyScriptOrder(order);
    }

    internal void ReserveSourceChangeEntries(IEnumerable<FalloutFormKey> forms,
        Func<FalloutFormKey, int> contribution, bool? leveledItem)
    {
        if (_scriptOrder is null) return;
        var order = _scriptOrder.ToList();
        foreach (var form in forms)
            if (order.All(entry => entry.Item != form)) order.Add(new(form, [], contribution(form), leveledItem));
        _scriptOrder = order.ToArray();
    }

    private void ObserveNativeAddition(IEnumerable<FalloutFormKey> forms, bool? leveledItem)
    {
        if (_scriptOrder is null) return;
        var order = _scriptOrder.ToList();
        foreach (var form in forms)
        {
            var index = order.FindIndex(entry => entry.Item == form);
            if (index < 0) order.Add(new(form, [], _scriptBaseCount?.Invoke(form), leveledItem));
            else if (order[index].LeveledItem != leveledItem) order[index] = order[index] with { LeveledItem = null };
        }
        _scriptOrder = order.ToArray();
    }

    internal IReadOnlyList<FalloutInventoryScriptOrderEntry> NativeScriptEntries => _scriptOrder ??
        throw new NotSupportedException("Inventory change-entry traversal has no retained source owner.");

    internal bool HasNativeScriptOrder => _scriptOrder is not null;

    // These identities are retained owners, not an inferred traversal order.
    internal IReadOnlyList<(FalloutFormKey Item, Guid Identity)> RetainedScriptInstances() => _items.Values
        .SelectMany(item => (item.Variants ?? []).Where(value => value.ScriptState is not null)
            .Select(value => (item.FormKey, value.ScriptState!.Identity))).ToArray();

    private void ObserveScriptPublication(IEnumerable<FalloutCampaignItem> replacements)
    {
        if (_scriptOrder is null) return;
        var originalOrder = _scriptOrder;
        var pending = replacements.ToArray();
        try
        {
            foreach (var replacement in pending)
                if (Item(replacement.FormKey) is { } previous) ObserveScriptRemoval(previous, replacement);
            // Removal can legitimately retire incomplete traversal knowledge
            // in an inventory without a bound script owner. Preserve unknown.
            if (_scriptOrder is null) return;
            var order = _scriptOrder.ToList();
            foreach (var item in pending)
            {
                var next = (item.Variants ?? []).Where(value => value.ScriptState is not null)
                    .Select(value => value.ScriptState!.Identity).ToArray();
                var index = order.FindIndex(entry => entry.Item == item.FormKey);
                if (next.Length == 0) continue;
                var known = index < 0 ? Array.Empty<Guid>() : order[index].Instances;
                var added = next.Where(identity => !known.Contains(identity)).ToArray();
                if (added.Length == 0) continue;
                // New form entries append. A newly added extra-data list prepends
                // within its existing form entry; source variant/UI order differs.
                var entry = new FalloutInventoryScriptOrderEntry(item.FormKey,
                    added.Reverse().Concat(known).ToArray(), index < 0 ? _scriptBaseCount?.Invoke(item.FormKey) : order[index].BaseContribution);
                if (index < 0) order.Add(entry); else order[index] = entry;
            }
            _scriptOrder = order.ToArray();
        }
        catch { _scriptOrder = originalOrder; throw; }
    }

    private void ObserveScriptRemoval(FalloutCampaignItem previous, FalloutCampaignItem? next)
    {
        var before = (previous.Variants ?? []).Where(value => value.ScriptState is not null).ToArray();
        var after = (next?.Variants ?? []).Where(value => value.ScriptState is not null)
            .Select(value => value.ScriptState!.Identity).ToHashSet();
        var removed = before.Where(value => !after.Contains(value.ScriptState!.Identity)).ToArray();
        var countRemoved = (next?.Count ?? 0) < previous.Count;
        if (!countRemoved && removed.Length == 0) return;
        if (removed.Length != 0 && _equipped.Contains(previous.RuntimeFormId))
            throw new NotSupportedException("Retiring worn scripted equipment needs its native event-list handoff owner.");
        if (removed.Any(value => value.ScriptState!.Identity != _runningInventoryScript &&
            _scriptInitializations.Any(call => call.Identity == value.ScriptState.Identity)))
            throw new NotSupportedException("Retiring an inventory instance still owns its source initialization continuation.");
        if (_scriptOrder is null) return; // Retained missing history stays unknown; quantities still have their own owner.
        var entry = _scriptOrder.SingleOrDefault(value => value.Item == previous.FormKey);
        if (entry is null)
        {
            ObserveNativeAddition([previous.FormKey], false);
            entry = _scriptOrder!.Single(value => value.Item == previous.FormKey);
        }
        if (entry.BaseContribution is not { } contribution || before.Length != 0 && before.Sum(value => value.Count) != previous.Count)
            RefuseOrInvalidate("Script entry retirement needs its complete source delta/extended-instance population.");
        if (_scriptOrder is null) return;
        contribution = entry.BaseContribution!.Value;
        var sourceFlags = removed.Select(value => value.ScriptState!.LeveledItem).Distinct().ToArray();
        var provenance = removed.Length == 0 ? entry.LeveledItem : sourceFlags.Length == 1 ? sourceFlags[0] : null;
        if (!_scriptContainerIsActor && provenance is null)
            RefuseOrInvalidate("Non-actor entry retirement needs its actual ExtraLeveledItem retention owner.");
        if (_scriptOrder is null) return;
        var retired = removed.Select(value => value.ScriptState!.Identity).ToHashSet();
        var remaining = entry.Instances.Where(identity => !retired.Contains(identity)).ToArray();
        var delta = checked((next?.Count ?? 0) - contribution);
        // A fully represented scripted list owns the nonnull-empty case. A
        // nonscripted nonzero-base entry needs the actual native extra list.
        if (delta == 0 && contribution != 0 && before.Length == 0)
            RefuseOrInvalidate("Zero-delta nonscript entry cleanup needs its actual native extended-list owner.");
        if (_scriptOrder is null) return;
        var retainLeveled = !_scriptContainerIsActor && provenance == true;
        var unlink = !retainLeveled && delta == 0 && (contribution == 0 || remaining.Length == 0);
        _scriptOrder = _scriptOrder.Where(value => !unlink || value.Item != previous.FormKey)
            .Select(value => value.Item == previous.FormKey ? value with { Instances = remaining } : value).ToArray();

        void RefuseOrInvalidate(string error)
        {
            if (_scriptIterationActive) throw new NotSupportedException(error);
            _scriptOrder = null;
        }
    }

    internal IReadOnlyList<FalloutInventoryScriptInitialization> ScriptInitializations => _scriptInitializations;

    internal void CompleteScriptInitialization(FalloutInventoryScriptInitialization call)
    {
        if (_scriptInitializations.Count == 0 || _scriptInitializations[0] != call)
            throw new InvalidOperationException("Initial inventory script call does not own the pending source receipt.");
        _scriptInitializations = _scriptInitializations.Skip(1).ToArray();
        ++Revision;
    }

    internal void InitializeSourceInventoryScripts(FalloutPluginStack records,
        IReadOnlyList<(FalloutFormKey Form, int Count)> sourceRows, bool gameMode)
    {
        if (_scriptInitializations.Count != 0 || _items.Values.Any(item => item.Variants?.Any(value => value.ScriptState is not null) == true))
            throw new InvalidOperationException("Initial script event-lists require a fresh source inventory.");
        var calls = new List<FalloutInventoryScriptInitialization>();
        var initialized = new Dictionary<FalloutFormKey, IReadOnlyList<Guid>>();
        foreach (var row in sourceRows)
        {
            var definition = records.GetEffective(row.Form);
            if (FalloutScriptLocals.AttachedScript(records, definition) is null) continue;
            if (initialized.TryGetValue(row.Form, out var existing))
            {
                calls.AddRange(existing.Select(identity => new FalloutInventoryScriptInitialization(identity, gameMode)));
                continue;
            }
            var item = Item(row.Form) ?? throw new InvalidDataException("Initial script item disappeared.");
            var input = item.Variants ?? [new FalloutItemVariant(item.Count)];
            var variants = new List<FalloutItemVariant>();
            var created = new List<Guid>();
            var remaining = row.Count;
            foreach (var original in input)
            {
                var take = Math.Min(remaining, original.Count);
                for (var index = 0; index < take; index++)
                {
                    var instance = original with { Count = 1 };
                    var state = FalloutInventoryScriptState.Prepare(records, definition, instance)! with { LeveledItem = false };
                    variants.Add(instance with { ScriptState = state });
                    created.Add(state.Identity); calls.Add(new(state.Identity, gameMode));
                }
                if (take != original.Count) variants.Add(original with { Count = original.Count - take });
                remaining -= take;
            }
            if (remaining != 0) throw new InvalidDataException("Initial source row exceeds actual inventory quantity.");
            Publish([item with { Variants = variants }]);
            if (_scriptOrder is { } order)
                _scriptOrder = order.Select(entry => entry.Item == row.Form
                    ? entry with { BaseContribution = checked(sourceRows.Where(value => value.Form == row.Form).Sum(value => value.Count)) }
                    : entry).ToArray();
            initialized.Add(row.Form, created.AsEnumerable().Reverse().ToArray());
        }
        foreach (var entry in NativeScriptEntries.ToArray())
        {
            var item = Item(entry.Item);
            if (item is null || FalloutScriptLocals.AttachedScript(records, records.GetEffective(entry.Item)) is null) continue;
            var baseCount = sourceRows.Where(row => row.Form == entry.Item).Sum(row => row.Count);
            if (item.Count == baseCount) continue; // Native changed pass skips delta0.
            if (initialized.TryGetValue(entry.Item, out var existing))
            {
                calls.AddRange(existing.Select(identity => new FalloutInventoryScriptInitialization(identity, gameMode)));
                continue;
            }
            if (item.Count != 1 || (item.Variants ?? []).Count > 1)
                throw new NotSupportedException("Initial changed scripted stack needs its native existing-XDL allocation/count owner.");
            var variant = (item.Variants ?? [new FalloutItemVariant(1)])[0];
            var state = FalloutInventoryScriptState.Prepare(records, records.GetEffective(entry.Item), variant)!;
            Publish([item with { Variants = [variant with { ScriptState = state }] }]);
            calls.Add(new(state.Identity, gameMode)); initialized.Add(entry.Item, [state.Identity]);
        }
        if (_items.Values.Any(item => FalloutScriptLocals.AttachedScript(records, records.GetEffective(item.FormKey)) is not null &&
            !initialized.ContainsKey(item.FormKey)))
            throw new NotSupportedException("Initial changed/leveled scripted items need their native initialization producer.");
        _scriptInitializations = calls.ToArray();
    }

    internal IReadOnlyList<(FalloutFormKey Item, Guid Identity)> ScriptInstances()
    {
        var states = _items.Values.SelectMany(item => (item.Variants ?? [])
            .Where(value => value.ScriptState is not null).Select(value => (item.FormKey, value.ScriptState!.Identity))).ToHashSet();
        if (states.Count == 0) return [];
        if (_scriptOrder is null)
            throw new NotSupportedException("Inventory script change-entry traversal has no retained source owner.");
        var ordered = _scriptOrder.SelectMany(entry => entry.Instances.Select(identity => (entry.Item, Identity: identity)))
            .Where(states.Contains).ToArray();
        if (ordered.Length != states.Count)
            throw new InvalidDataException("Inventory script traversal lost an actual event-list identity.");
        return ordered;
    }
}
