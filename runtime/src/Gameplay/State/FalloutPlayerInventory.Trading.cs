using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutTradeTransfer(FalloutFormKey Form, int Count, bool FromThisInventory);

internal sealed partial class FalloutPlayerInventory
{
    internal void Exchange(FalloutPlayerInventory other, IReadOnlyList<FalloutTradeTransfer> transfers)
    {
        ArgumentNullException.ThrowIfNull(other);
        ArgumentNullException.ThrowIfNull(transfers);
        if (ReferenceEquals(this, other)) throw new InvalidOperationException("Trade needs distinct inventory owners.");
        if (transfers.Count == 0) throw new InvalidOperationException("Trade contains no item transfers.");

        var thisItems = new Dictionary<FalloutFormKey, FalloutCampaignItem>(_items);
        var otherItems = new Dictionary<FalloutFormKey, FalloutCampaignItem>(other._items);
        var thisEquipped = new HashSet<uint>(_equipped);
        var otherEquipped = new HashSet<uint>(other._equipped);
        foreach (var transfer in transfers)
        {
            if (transfer is null || transfer.Form.ObjectId == 0 || transfer.Count <= 0)
                throw new InvalidDataException("Trade transfer has an invalid item or quantity.");
            var sourceItems = transfer.FromThisInventory ? thisItems : otherItems;
            var targetItems = transfer.FromThisInventory ? otherItems : thisItems;
            var sourceEquipped = transfer.FromThisInventory ? thisEquipped : otherEquipped;
            Move(sourceItems, targetItems, sourceEquipped, transfer);
        }

        Validate(thisItems.Values);
        Validate(otherItems.Values);
        _items.Clear();
        foreach (var item in thisItems) _items.Add(item.Key, item.Value);
        other._items.Clear();
        foreach (var item in otherItems) other._items.Add(item.Key, item.Value);
        _equipped.Clear();
        _equipped.UnionWith(thisEquipped);
        other._equipped.Clear();
        other._equipped.UnionWith(otherEquipped);
        ++Revision;
        ++other.Revision;

        Notifications.Publish(transfers.GroupBy(transfer =>
                (transfer.FromThisInventory ? FalloutHudEventKind.ItemRemoved : FalloutHudEventKind.ItemAdded, transfer.Form))
            .Select(group => new FalloutHudEvent(group.Key.Item1, group.Key.Form,
                group.Aggregate(0, (count, transfer) => checked(count + transfer.Count))))
            .ToArray());
    }

    private static void Move(Dictionary<FalloutFormKey, FalloutCampaignItem> sourceItems,
        Dictionary<FalloutFormKey, FalloutCampaignItem> targetItems, HashSet<uint> sourceEquipped,
        FalloutTradeTransfer transfer)
    {
        if (!sourceItems.TryGetValue(transfer.Form, out var source) || transfer.Count > source.Count)
            throw new InvalidOperationException($"Trade item quantity is no longer available: {transfer.Form}.");
        if (sourceEquipped.Contains(source.RuntimeFormId))
        {
            if (transfer.Count != source.Count)
                throw new NotSupportedException("Partial transfer of an equipped stack needs item-instance selection.");
            sourceEquipped.Remove(source.RuntimeFormId);
        }

        var remaining = (source.Variants ?? [new(source.Count)]).ToList();
        var selected = new List<FalloutItemVariant>();
        var pending = transfer.Count;
        for (var index = 0; index < remaining.Count && pending > 0; index++)
        {
            var count = Math.Min(pending, remaining[index].Count);
            selected.Add(remaining[index] with { Count = count });
            remaining[index] = remaining[index] with { Count = remaining[index].Count - count };
            pending -= count;
        }
        if (pending != 0) throw new InvalidDataException("Trade item variants differ from the inventory count.");

        var left = source.Count - transfer.Count;
        if (left == 0) sourceItems.Remove(transfer.Form);
        else sourceItems[transfer.Form] = source with
        {
            Count = left,
            Variants = remaining.Where(value => value.Count != 0).ToArray()
        };

        var previous = targetItems.GetValueOrDefault(transfer.Form);
        if (previous is not null && (previous.RuntimeFormId != source.RuntimeFormId ||
            previous.RecordType != source.RecordType || previous.EditorId != source.EditorId))
            throw new InvalidDataException("Trade inventories disagree on the source item identity.");
        var variants = (previous?.Variants ?? (previous is null ? [] : [new(previous.Count)])).ToList();
        foreach (var moved in selected)
        {
            var index = variants.FindIndex(value => value with { Count = 1 } == moved with { Count = 1 });
            if (index < 0) variants.Add(moved);
            else variants[index] = variants[index] with { Count = checked(variants[index].Count + moved.Count) };
        }
        targetItems[transfer.Form] = source with
        {
            Count = checked((previous?.Count ?? 0) + transfer.Count),
            Variants = variants
        };
    }

    private static void Validate(IEnumerable<FalloutCampaignItem> items)
    {
        var materialized = items.ToArray();
        if (materialized.Any(item => item.Count <= 0 || item.Variants is { } variants &&
            (variants.Sum(value => value.Count) != item.Count || variants.Any(value => value.Count <= 0 ||
                value.Condition is { } condition && (!float.IsFinite(condition) || condition is < 0 or > 1)))) ||
            materialized.Select(item => item.FormKey).Distinct().Count() != materialized.Length)
            throw new InvalidDataException("Trade transaction contains an invalid staged inventory.");
    }
}
