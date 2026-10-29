using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

// Runtime-created references have their own identity. Their base remains a
// winning owned record; no generated object masquerades as an ESM REFR.
internal sealed record FalloutDroppedReferenceSnapshot(Guid Identity, FalloutFormKey Base,
    FalloutReferencePlacement Placement, FalloutItemVariant Item,
    IReadOnlyList<FalloutObjectPhysicsSnapshot>? Physics = null,
    IReadOnlyList<FalloutItemVariant>? Ammunition = null)
{
    internal FalloutDroppedReferenceSnapshot Copy() => this with
    { Placement = Placement.Copy(), Physics = Physics?.Select(body => body.Copy()).ToArray(), Ammunition = Ammunition?.ToArray() };

    internal void Validate(FalloutPluginStack records)
    {
        ValidateState();
        var type = records.GetEffective(Base).Signature;
        if (!FalloutReferenceWorld.IsInventoryItem(type) || records.GetEffective(Placement.Cell).Signature != "CELL" ||
            type != "WEAP" && (Item.WeaponMods != 0 || Item.Instance is not null || Item.Magazine is not null) ||
            Item.Magazine is { } magazine && records.GetEffective(magazine.Ammunition).Signature != "AMMO")
            throw new InvalidDataException("Dropped reference base or cell is invalid.");
        if (Item.WeaponMods != 0)
        {
            var declared = FalloutWeaponModifications.Read(records, Base).Aggregate(0, (mask, mod) => mask | (1 << mod.Slot));
            if ((Item.WeaponMods & declared) != Item.WeaponMods) throw new InvalidDataException("Dropped reference has an undeclared weapon modification.");
        }
    }

    internal void ValidateState()
    {
        if (Identity == Guid.Empty || string.IsNullOrWhiteSpace(Base.OwnerPlugin) || Base.ObjectId == 0 || Item.Count <= 0 || Item.Hotkey is not null ||
            (Item.WeaponMods & ~7) != 0 || Item.Condition is { } condition && (!float.IsFinite(condition) || condition is < 0 or > 1) ||
            Item.Instance is { } instance && (instance == Guid.Empty || Item.Count != 1) ||
            Item.Magazine is { } magazine && (Item.Instance is null || magazine.Loaded < 0 || magazine.Ammunition.ObjectId == 0))
            throw new InvalidDataException("Dropped reference has invalid identity, base, contents or placement.");
        Placement.Validate();
        if (Physics is not null) FalloutObjectPhysicsSnapshot.ValidateSet(Physics);
        if ((Ammunition?.Sum(extra => extra.Count) ?? 0) != (Item.Magazine?.Loaded ?? 0) ||
            Ammunition?.Any(extra => extra.Count <= 0 || extra.Hotkey is not null || extra.Instance is not null ||
                extra.Magazine is not null || extra.WeaponMods != 0) == true)
            throw new InvalidDataException("Dropped magazine lost its exact ammunition contents.");
    }
}

internal sealed partial class FalloutReferenceWorld
{
    private readonly Dictionary<Guid, FalloutDroppedReferenceSnapshot> _droppedItems = [];
    private readonly Dictionary<Guid, Func<FalloutDroppedReferenceSnapshot>> _droppedCaptures = [];
    internal long DroppedItemRevision { get; private set; }
    internal IReadOnlyList<FalloutDroppedReferenceSnapshot> DroppedItems => _droppedItems.Values.ToArray();

    internal FalloutDroppedReferenceSnapshot PrepareDroppedItem(FalloutPlayerInventory inventory,
        FalloutFormKey form, int variant, int count, FalloutReferencePlacement placement)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var item = inventory.Item(form) ?? throw new InvalidOperationException("Dropped item is absent.");
        if (!FalloutInventoryAccess.CanTransfer(records, records.GetEffective(form), true) || item.UnequipLocked)
            throw new InvalidOperationException("This item cannot be dropped.");
        var extra = inventory.Instances(form)[variant].Extra;
        if (count <= 0 || count > extra.Count) throw new ArgumentOutOfRangeException(nameof(count));
        RequireDroppedItemEventOwner(form, "OnDrop");
        // Do not strand an item whose return to player inventory is refused by
        // the shared acquisition owner. Scripted pickup remains explicit.
        RequirePlayerItemAcquisition(records, records.GetEffective(form), records.RuntimeFormKey(0x14));
        if (extra.Magazine is { Loaded: > 0 } magazine)
            RequirePlayerItemAcquisition(records, records.GetEffective(magazine.Ammunition), records.RuntimeFormKey(0x14));
        var result = new FalloutDroppedReferenceSnapshot(Guid.NewGuid(), form, placement.Copy(), extra with { Count = count, Hotkey = null });
        if (extra.Magazine is { Loaded: > 0 } contents)
            result = result with { Ammunition = inventory.RemovalExtras(contents.Ammunition, contents.Loaded) };
        result.Validate(records);
        return result;
    }

    internal FalloutInventoryChangeLease PrepareDroppedPublication(FalloutDroppedReferenceSnapshot item)
    {
        item.Validate(records);
        var added = false;
        return new(() =>
        {
            if (!_droppedItems.TryAdd(item.Identity, item.Copy())) throw new InvalidOperationException("Dropped identity was already published.");
            added = true; ++DroppedItemRevision;
        }, () => { if (added) { _droppedItems.Remove(item.Identity); ++DroppedItemRevision; } });
    }

    internal void TakeDroppedItem(Guid identity, FalloutPlayerInventory inventory, int level,
        FalloutGlobalState? globals, FalloutInventoryChangeLease presentation)
    {
        var item = _droppedItems.GetValueOrDefault(identity) ?? throw new InvalidOperationException("Dropped item is no longer available.");
        RequirePlayerItemAcquisition(records, records.GetEffective(item.Base), records.RuntimeFormKey(0x14));
        if (item.Item.Magazine is { Loaded: > 0 } loaded)
            RequirePlayerItemAcquisition(records, records.GetEffective(loaded.Ammunition), records.RuntimeFormKey(0x14));
        var removed = false;
        using var effects = new FalloutInventoryChangeLease(() =>
        {
            if (!_droppedItems.Remove(identity)) throw new InvalidOperationException("Dropped item changed during pickup.");
            removed = true; ++DroppedItemRevision;
            presentation.Commit();
        }, () =>
        {
            presentation.Dispose();
            if (removed) { _droppedItems.Add(identity, item); ++DroppedItemRevision; }
        }, presentation.Complete);
        inventory.AddWithPreparedOwner(records, item.Base, item.Item.Count, level, false, effects, globals, item.Item,
            stageAddition: staged =>
            {
                if (item.Item.Magazine is { Loaded: > 0 } magazine)
                    foreach (var extra in item.Ammunition!)
                        staged.Add(records, magazine.Ammunition, extra.Count, level, true, globals, extra);
            });
    }

    internal void BindDroppedCapture(Guid identity, Func<FalloutDroppedReferenceSnapshot> capture)
    {
        if (!_droppedItems.ContainsKey(identity) || !_droppedCaptures.TryAdd(identity, capture))
            throw new InvalidOperationException("Dropped reference has no unique physical owner.");
    }

    internal void RetireDroppedCapture(Guid identity, Func<FalloutDroppedReferenceSnapshot> capture)
    {
        if (!_droppedCaptures.TryGetValue(identity, out var owner) || !ReferenceEquals(owner, capture)) return;
        if (_droppedItems.ContainsKey(identity)) _droppedItems[identity] = capture().Copy();
        _droppedCaptures.Remove(identity);
    }

    internal IReadOnlyList<FalloutDroppedReferenceSnapshot> CaptureDroppedItems() => _droppedItems.Values
        .Select(item => _droppedCaptures.TryGetValue(item.Identity, out var capture) ? capture().Copy() : item.Copy()).ToArray();

    internal void RestoreDroppedItems(IReadOnlyList<FalloutDroppedReferenceSnapshot>? items)
    {
        if (_droppedItems.Count != 0 || _droppedCaptures.Count != 0) throw new InvalidOperationException("Dropped reference restoration needs a fresh world.");
        var values = items ?? [];
        foreach (var item in values) item.Validate(records);
        if (values.Select(item => item.Identity).Distinct().Count() != values.Count)
            throw new InvalidDataException("Saved dropped reference identity is duplicated.");
        foreach (var item in values) _droppedItems.Add(item.Identity, item.Copy());
        ++DroppedItemRevision;
    }

    internal void RequireDroppedItemEventOwner(FalloutFormKey form, string eventName)
    {
        var item = records.GetEffective(form);
        var script = FalloutScriptLocals.AttachedScript(records, item);
        if (script is null) return;
        var source = script.ReadSubrecords().Where(field => field.Signature == "SCTX").ToArray();
        if (source.Length != 1) throw new NotSupportedException("Dropped item script has no unambiguous event source.");
        if (FalloutGameModeProgram.ReadEvents(FalloutDialogueTopic.ScriptText(source[0].Data.Span))
            .Any(block => block.Event.Equals(eventName, StringComparison.OrdinalIgnoreCase)))
            throw new NotSupportedException($"Dynamic item {eventName} needs its script-instance execution owner.");
    }
}
