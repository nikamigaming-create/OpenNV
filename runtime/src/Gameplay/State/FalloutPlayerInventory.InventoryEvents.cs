using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutPlayerInventory
{
    // These methods operate only on the transaction's detached copies. The
    // outer transfer still owns physical instances, loaded rounds and publication.
    internal IReadOnlyList<FalloutItemAddition> StageEventTransferTo(FalloutPlayerInventory target,
        IReadOnlyList<FalloutInventoryTransfer> transfers,
        Func<FalloutItemAddition, FalloutItemAddition> prepare,
        Action<IReadOnlyList<FalloutItemAddition>> dispatch)
    {
        var moved = new List<FalloutItemAddition>();
        foreach (var transfer in PlanLoadedRoundBatch(target, transfers.Select(value =>
            new FalloutTradeTransfer(value.Item, value.Count, true)).ToArray()))
        {
            var item = Item(transfer.Form) ?? throw new InvalidOperationException("Event transfer item is absent.");
            var remaining = transfer.Count;
            var variants = (item.Variants ?? [new FalloutItemVariant(item.Count)]).ToArray();
            var incoming = new List<FalloutItemAddition>();
            for (var index = 0; index < variants.Length && remaining != 0; index++)
            {
                var count = Math.Min(remaining, variants[index].Count);
                var original = new FalloutItemAddition(item.FormKey, variants[index] with { Count = count, Hotkey = null });
                var prepared = prepare(original);
                if (prepared.Form != original.Form || prepared.Variant.Count != count)
                    throw new InvalidDataException("Event preparation changed a source transfer identity or quantity.");
                if (count != variants[index].Count && prepared.Variant.ScriptState != variants[index].ScriptState)
                    throw new NotSupportedException("A split scripted stack needs its native event-list split owner.");
                variants[index] = prepared.Variant with { Count = variants[index].Count, Hotkey = variants[index].Hotkey };
                incoming.Add(prepared);
                remaining -= count;
            }
            if (remaining != 0) throw new InvalidDataException("Event transfer variants do not cover its quantity.");
            _items[item.FormKey] = item with { Variants = variants };
            TransferToCore(target, transfer.Form, transfer.Count, null, false);
            moved.AddRange(incoming);
            dispatch(incoming);
        }
        return moved;
    }

    private FalloutFormKey? _scriptContainer;
    private Func<FalloutFormKey, int>? _scriptBaseCount;
    private bool _scriptContainerIsActor;

    internal void BindScriptContainer(FalloutFormKey container, Func<FalloutFormKey, int>? baseCount = null, bool actor = true)
    {
        if (_scriptContainer is { } previous && previous != container)
            throw new InvalidOperationException("Inventory script container changed without transferring the actual item.");
        if (_scriptContainer == container && _scriptBaseCount is not null) return;
        if (baseCount is not null && _scriptOrder is { } order)
            foreach (var entry in order.Where(value => value.BaseContribution is not null))
                if (baseCount(entry.Item) != entry.BaseContribution)
                    throw new InvalidDataException("Inventory script entry differs from its winning source base contribution.");
        _scriptContainer = container;
        _scriptBaseCount = baseCount;
        _scriptContainerIsActor = actor;
    }

    internal void PrepareInventoryScripts(FalloutPluginStack records, IReadOnlySet<FalloutFormKey>? forms = null)
    {
        var changes = new List<FalloutCampaignItem>();
        foreach (var item in _items.Values)
        {
            if (forms is not null && !forms.Contains(item.FormKey)) continue;
            if (FalloutScriptLocals.AttachedScript(records, records.GetEffective(item.FormKey)) is null) continue;
            var variants = (item.Variants ?? [new FalloutItemVariant(item.Count)]).ToArray();
            var changed = false;
            for (var index = 0; index < variants.Length; index++)
            {
                var state = FalloutInventoryScriptState.Prepare(records, records.GetEffective(item.FormKey), variants[index]);
                if (ReferenceEquals(state, variants[index].ScriptState)) continue;
                variants[index] = variants[index] with { ScriptState = state }; changed = true;
            }
            if (changed) changes.Add(item with { Variants = variants });
        }
        Publish(changes);
    }

    private void RequireEquipmentScriptOwner(FalloutFormKey form)
    {
        var item = Item(form)!;
        var variants = item.Variants ?? [];
        if (!variants.Any(value => value.ScriptState is not null)) return;
        if (_scriptContainer is null)
            throw new NotSupportedException("Equipment script has no actual actor container.");
        if (variants.Count != 1 && item.RecordType != "WEAP")
            throw new NotSupportedException("Equipment event has no unique worn scripted armor instance.");
        if (variants[0].ScriptState is null)
            throw new NotSupportedException("Selected worn variant has no actual script event-list.");
    }

    private void AdmitInventoryEquipment(FalloutFormKey form, uint mask)
    {
        var item = Item(form) ?? throw new InvalidOperationException("Equipment script source item disappeared.");
        var variants = (item.Variants ?? [new FalloutItemVariant(item.Count)]).ToArray();
        if (!variants.Any(value => value.ScriptState is not null)) return;
        var actor = _scriptContainer ?? throw new NotSupportedException("Equipment event has no actual actor container.");
        // Existing equipment selection publishes the worn weapon instance at
        // index zero. Multiple scripted armor instances remain explicit.
        if (variants.Length != 1 && item.RecordType != "WEAP")
            throw new NotSupportedException("Equipment event has no unique worn scripted armor instance.");
        if (variants[0].ScriptState is not { } state)
            throw new InvalidDataException("Worn equipment has no retained script event-list.");
        variants[0] = variants[0] with { ScriptState = state.Admit(actor, mask) };
        _items[form] = item with { Variants = variants };
    }

    internal void AdmitInventoryWeaponFire(FalloutPluginStack records, FalloutFormKey weapon, FalloutFormKey actor)
    {
        if (EquippedObject(records, 5) != weapon) throw new InvalidOperationException("Weapon OnFire is not the actual equipped weapon.");
        var item = Item(weapon) ?? throw new InvalidOperationException("Weapon OnFire item disappeared.");
        if (item.Variants is { Count: > 0 } variants && variants[0].ScriptState is { } state)
            StoreInventoryScriptState(weapon, state.Admit(actor, 256));
        else if (FalloutScriptLocals.AttachedScript(records, records.GetEffective(weapon)) is not null)
            throw new NotSupportedException("Weapon OnFire selected instance has no retained event-list.");
    }

    internal bool HasPendingInventoryWeaponHit => _items.Values.Any(item =>
        item.Variants?.Any(variant => variant.ScriptState?.Events?.Any(value => (value.Mask & 128u) != 0) == true) == true);

    internal void AdmitInventoryWeaponHit(FalloutFormKey weapon, Guid? sourceInstance, FalloutFormKey target)
    {
        if (sourceInstance is not { } identity)
            throw new NotSupportedException("Weapon OnHit has no captured discharge event-list identity.");
        var item = Item(weapon);
        var matches = item?.Variants?.Where(variant => variant.ScriptState?.Identity == identity).ToArray() ?? [];
        if (matches.Length != 1 || matches[0].ScriptState is not { } state)
            throw new NotSupportedException("Weapon OnHit discharge instance left its attacking inventory; retired-item event delivery is unbound.");
        StoreInventoryScriptState(weapon, state.Admit(target, 128));
    }

    internal void StoreInventoryScriptState(FalloutFormKey form, FalloutInventoryScriptState state)
    {
        state.ValidateShape();
        var item = Item(form);
        if (item is null) return; // RemoveMe has actually removed this incoming instance.
        var variants = (item.Variants ?? [new FalloutItemVariant(item.Count)]).ToArray();
        var indices = Enumerable.Range(0, variants.Length)
            .Where(index => variants[index].ScriptState?.Identity == state.Identity).ToArray();
        if (indices.Length == 0) return;
        if (indices.Length != 1 || variants[indices[0]].Count != 1)
            throw new InvalidDataException("Inventory event-list identity is ambiguous.");
        var previous = variants[indices[0]].ScriptState!;
        if (previous.Script != state.Script || previous.Sha256 != state.Sha256 || previous.LeveledItem != state.LeveledItem)
            throw new InvalidDataException("Inventory script state commit changed its retained source identity or provenance.");
        if (previous.Script == state.Script && previous.Sha256 == state.Sha256 && previous.Error == state.Error && previous.LeveledItem == state.LeveledItem &&
            previous.Locals.SequenceEqual(state.Locals) && (previous.Events ?? []).SequenceEqual(state.Events ?? [])) return;
        if (_changeInProgress) throw new InvalidOperationException("Inventory script state cannot interleave a prepared transfer.");
        variants[indices[0]] = variants[indices[0]] with { ScriptState = state };
        // Committing cells/events of this exact retained list changes neither
        // inventory topology nor native traversal. In particular an isolated
        // legacy event may commit without granting structural mutation rights.
        _items[form] = item with { Variants = variants };
        ++Revision;
    }

    internal void RemoveInventoryScriptInstance(FalloutFormKey form, Guid identity)
    {
        var item = Item(form) ?? throw new InvalidOperationException("RemoveMe incoming item is absent.");
        var variants = item.Variants ?? [new FalloutItemVariant(item.Count)];
        var indices = Enumerable.Range(0, variants.Count)
            .Where(index => variants[index].ScriptState?.Identity == identity).ToArray();
        if (indices.Length != 1 || variants[indices[0]].Count != 1)
            throw new InvalidDataException("RemoveMe has no unique actual inventory event-list instance.");
        // Reuse the instance replacement owner so a different worn weapon
        // variant survives removal of this newly received, unworn instance.
        ReplaceInstances(form, [(indices[0], 1)], []);
    }

    internal FalloutInventoryChangeLease PrepareScriptReplacement(FalloutPlayerInventory staged, long expectedRevision)
    {
        RequireNoChangeInProgress(); RequireRevision(expectedRevision);
        var before = Copy(); var after = staged.Copy();
        ValidateWeaponInstances(after.Items); after.ValidateMagazineAmmunition();
        var changed = !SameSnapshot(before.Capture(), after.Capture());
        var revision = changed ? checked(expectedRevision + 1) : expectedRevision;
        FalloutInventoryChangeLease? binding = changed ? PrepareChange?.Invoke(before.Capture(), after.Capture()) : null;
        var started = false;
        return new(() =>
        {
            RequireNoChangeInProgress(); RequireRevision(expectedRevision);
            _changeInProgress = true; started = true;
            if (changed) PublishPrepared(after, revision);
            _scriptMutationRevision = after._scriptMutationRevision;
            binding?.Commit();
        }, () =>
        {
            try
            {
                if (started) PublishPrepared(before, expectedRevision);
                if (started) _scriptMutationRevision = before._scriptMutationRevision;
                binding?.Dispose();
            }
            finally { if (started) _changeInProgress = false; }
        }, () =>
        {
            try { binding?.Complete(); }
            finally { if (started) _changeInProgress = false; }
        });
    }
}
