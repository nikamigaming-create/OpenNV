using System.Buffers.Binary;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutPlayerInventory
{
    internal FalloutFormKey? EquippedObject(FalloutPluginStack records, uint slot)
    {
        var mask = slot < 20 ? 1u << (int)slot : uint.MaxValue;
        var matches = _equipped.Select(records.RuntimeFormKey).Where(key =>
        {
            var record = records.GetEffective(key);
            if (record.Signature == "WEAP") return (mask & (1u << 5)) != 0;
            if (record.Signature != "ARMO") return false;
            var data = record.ReadSubrecords().Single(field => field.Signature == "BMDT").Data;
            if (data.Length != 8) throw new InvalidDataException("Equipped armor has invalid biped slots.");
            return (BinaryPrimitives.ReadUInt32LittleEndian(data.Span) & mask) != 0;
        }).ToArray();
        if (matches.Length > 1) throw new NotSupportedException("Multiple worn matches require source inventory-entry ordering.");
        return matches.Length == 0 ? null : matches[0];
    }

    internal void RemoveAll(FalloutPluginStack records, bool player, FalloutPlayerInventory? destination,
        bool retainOwnership, bool notifyDestination)
    {
        if (ReferenceEquals(this, destination)) return;
        // Prepare the whole transaction before publishing either inventory.
        var source = Copy();
        var target = destination?.Copy();
        var removed = Items.Where(item => Removable(records, records.GetEffective(item.FormKey), player)).ToArray();
        foreach (var item in removed)
        {
            if (target is not null)
            {
                source.TransferTo(target, item.FormKey, item.Count, force: true);
                if (!retainOwnership)
                {
                    // TransferTo merges equal variants; clear ownership only on
                    // the moved entries, never on previously owned target items.
                    var before = destination!.Item(item.FormKey);
                    var variants = (before?.Variants ?? (before is null ? [] : [new(before.Count)])).ToList();
                    foreach (var variant in item.Variants ?? [new(item.Count)])
                    {
                        var moved = variant with { Owner = null, Global = null, FactionRank = null };
                        var index = variants.FindIndex(value => value with { Count = 1 } == moved with { Count = 1 });
                        if (index < 0) variants.Add(moved);
                        else variants[index] = variants[index] with { Count = checked(variants[index].Count + moved.Count) };
                    }
                    target._items[item.FormKey] = target.Item(item.FormKey)! with { Variants = variants };
                }
                target._items[item.FormKey] = target.Item(item.FormKey)! with { UnequipLocked = destination!.Item(item.FormKey)?.UnequipLocked ?? false };
            }
            else source.Remove(item.FormKey, item.Count, silent: true);
        }
        Replace(source.Capture());
        if (destination is not null)
        {
            destination.Replace(target!.Capture());
            if (notifyDestination) destination.Notifications.Publish(removed.Select(item =>
                new FalloutHudEvent(FalloutHudEventKind.ItemAdded, item.FormKey, item.Count)).ToArray());
        }
    }

    private static bool Removable(FalloutPluginStack records, FalloutPluginRecord item, bool player)
    {
        if (player && records.QuestObjects.IsQuestObject(item.FormKey)) return false;
        if (item.Signature != "ARMO") return true;
        var data = item.ReadSubrecords().Single(field => field.Signature == "BMDT").Data;
        if (data.Length != 8) throw new InvalidDataException("Removed armor has invalid biped flags.");
        return (data.Span[4] & 0x40) == 0;
    }

    private FalloutPlayerInventory Copy()
    {
        var copy = new FalloutPlayerInventory();
        var snapshot = Capture();
        copy.Restore(snapshot.Inventory, snapshot.EquippedRuntimeFormIds.ToArray(), snapshot.InventoryRandomState);
        return copy;
    }

    internal void Replace(FalloutOpeningInventoryGrant snapshot)
    {
        var validated = new FalloutPlayerInventory();
        validated.Restore(snapshot.Inventory, snapshot.EquippedRuntimeFormIds.ToArray(), snapshot.InventoryRandomState);
        _items.Clear(); foreach (var pair in validated._items) _items.Add(pair.Key, pair.Value);
        _equipped.Clear(); _equipped.UnionWith(validated._equipped);
        _random = validated._random;
        ++Revision;
    }
}
