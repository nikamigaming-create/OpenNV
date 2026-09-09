using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

/// <summary>Shared player item state for native source commands and saves.</summary>
internal sealed partial class FalloutPlayerInventory
{
    private readonly Dictionary<FalloutFormKey, FalloutCampaignItem> _items = [];
    private readonly HashSet<uint> _equipped = [];
    private FalloutSoundRandomState _random = new(BitConverter.ToUInt64(System.Security.Cryptography.RandomNumberGenerator.GetBytes(sizeof(ulong))));
    internal FalloutHudNotifications Notifications { get; } = new();
    internal long Revision { get; private set; }
    internal IReadOnlyList<FalloutCampaignItem> Items => _items.Values.OrderBy(item => item.RuntimeFormId).ToArray();
    internal IReadOnlyList<uint> Equipped => _equipped.Order().ToArray();
    internal FalloutCampaignItem? Item(FalloutFormKey form) => _items.GetValueOrDefault(form);
    internal void TransferTo(FalloutPlayerInventory target, FalloutFormKey form, int count, int? variantIndex = null)
    {
        if (ReferenceEquals(this, target)) throw new InvalidOperationException("Inventory transfer needs distinct owners.");
        var source = Item(form) ?? throw new InvalidOperationException("Transferred item is absent.");
        if (count <= 0 || count > source.Count) throw new ArgumentOutOfRangeException(nameof(count));
        var remaining = (source.Variants ?? [new(source.Count)]).ToList();
        var selected = new List<FalloutItemVariant>();
        var toMove = count;
        for (var index = 0; index < remaining.Count && toMove > 0; index++)
        {
            if (variantIndex is { } only && index != only) continue;
            var take = Math.Min(toMove, remaining[index].Count);
            selected.Add(remaining[index] with { Count = take });
            remaining[index] = remaining[index] with { Count = remaining[index].Count - take };
            toMove -= take;
        }
        if (toMove != 0) throw new InvalidOperationException("Selected extra-data stack has too few items.");
        var previous = target.Item(form);
        var variants = (previous?.Variants ?? (previous is null ? [] : [new(previous.Count)])).ToList();
        foreach (var item in selected)
        {
            var index = variants.FindIndex(value => value with { Count = 1 } == item with { Count = 1 });
            if (index < 0) variants.Add(item);
            else variants[index] = variants[index] with { Count = checked(variants[index].Count + item.Count) };
        }
        var addition = source with { Count = checked((previous?.Count ?? 0) + count), Variants = variants };
        // Validate all additions before either side changes. Transfers preserve
        // instance condition/ownership and never reroll a leveled source list.
        target.Publish([addition]);
        if (count == source.Count) { _items.Remove(form); _equipped.Remove(source.RuntimeFormId); }
        else _items[form] = source with { Count = source.Count - count, Variants = remaining.Where(value => value.Count != 0).ToArray() };
        ++Revision;
    }
    internal void Remove(FalloutFormKey form, int count, bool silent)
    {
        if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));
        if (Item(form) is not { } previous) return;
        var removed = Math.Min(count, previous.Count);
        if (removed == previous.Count) { _items.Remove(form); _equipped.Remove(previous.RuntimeFormId); ++Revision; }
        else
        {
            // Distinct extra-data stacks require an explicit selection owner.
            if (previous.Variants is { Count: > 1 }) throw new NotSupportedException("RemoveItem selection across distinct condition/ownership stacks is unbound.");
            var remaining = previous.Count - removed;
            Publish([previous with { Count = remaining, Variants = previous.Variants is { Count: 1 } variants ?
                [variants[0] with { Count = remaining }] : null }]);
        }
        if (!silent) Notifications.Publish([new(FalloutHudEventKind.ItemRemoved, form, removed)]);
    }
    internal void Add(FalloutPluginStack records, FalloutFormKey form, int count, int level, bool silent,
        FalloutGlobalState? globals = null, FalloutItemVariant? extra = null)
    {
        var random = new FalloutSoundRandomState(_random.State);
        var additions = FalloutLeveledItems.Resolve(records, form, count, level, random.NextBounded, globals is null ? null : globals.Get, extra);
        var replacements = new List<FalloutCampaignItem>();
        foreach (var group in additions.GroupBy(addition => addition.Form))
        {
            var source = records.GetEffective(group.Key);
            var name = FalloutDialogueTopic.Text(source.ReadSubrecords().Single(field => field.Signature == "EDID").Data.Span);
            var previous = Item(group.Key);
            var variants = previous?.Variants?.ToList() ?? (previous is null ? [] : [new(previous.Count)]);
            foreach (var addition in group)
            {
                var index = variants.FindIndex(value => value.Condition == addition.Variant.Condition &&
                    value.Owner == addition.Variant.Owner && value.Global == addition.Variant.Global && value.FactionRank == addition.Variant.FactionRank);
                if (index < 0) variants.Add(addition.Variant);
                else variants[index] = variants[index] with { Count = checked(variants[index].Count + addition.Variant.Count) };
            }
            var total = checked((previous?.Count ?? 0) + group.Sum(addition => addition.Variant.Count));
            replacements.Add(FalloutCampaignInventoryResolver.Resolve(records,
                [new(records.RuntimeFormId(group.Key), name, source.Signature, total)], null).Items.Single() with
            { Variants = variants });
        }
        Publish(replacements); _random = random;
        if (!silent) Notifications.Publish(additions.GroupBy(addition => addition.Form)
            .Select(group => new FalloutHudEvent(FalloutHudEventKind.ItemAdded, group.Key, group.Sum(value => value.Variant.Count))).ToArray());
    }

    internal void Equip(FalloutPluginStack records, FalloutFormKey form)
    {
        var item = Item(form) ?? throw new InvalidOperationException("Cannot equip an item absent from inventory.");
        if (item.RecordType is not ("ARMO" or "WEAP")) throw new NotSupportedException("Equipped item has no armor/weapon owner.");
        uint Slots(FalloutFormKey key)
        {
            var data = records.GetEffective(key).ReadSubrecords().Single(field => field.Signature == "BMDT").Data;
            if (data.Length != 8) throw new InvalidDataException("Armor slot extent is invalid.");
            return System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(data.Span);
        }
        var slots = item.RecordType == "ARMO" ? Slots(form) : 0;
        foreach (var equipped in _equipped.ToArray())
        {
            var other = records.GetEffective(records.RuntimeFormKey(equipped));
            if (item.RecordType == "WEAP" && other.Signature == "WEAP" || item.RecordType == "ARMO" &&
                other.Signature == "ARMO" && (slots & Slots(other.FormKey)) != 0) _equipped.Remove(equipped);
        }
        _equipped.Add(item.RuntimeFormId);
        ++Revision;
    }
    internal bool Unequip(FalloutPluginStack records, FalloutFormKey form)
    {
        if (!_equipped.Remove(records.RuntimeFormId(form))) return false;
        ++Revision;
        return true;
    }
    internal void Publish(IReadOnlyCollection<FalloutCampaignItem> replacements)
    {
        if (replacements.Any(item => item.Count <= 0 || item.Variants is { } variants &&
            (variants.Sum(value => value.Count) != item.Count || variants.Any(value => value.Count <= 0 ||
                value.Condition is { } condition && (!float.IsFinite(condition) || condition < 0 || condition > 1)))) ||
            replacements.Select(item => item.FormKey).Distinct().Count() != replacements.Count)
            throw new InvalidDataException("Player inventory transaction contains invalid or duplicate items.");
        foreach (var item in replacements) _items[item.FormKey] = item;
        if (replacements.Count != 0) ++Revision;
    }
    internal void AddGrant(FalloutOpeningInventoryGrant grant)
    {
        var additions = grant.Inventory.Items.Select(item =>
        {
            var previous = Item(item.FormKey);
            return previous is null ? item : item with
            {
                Count = checked(item.Count + previous.Count),
                Variants = (previous.Variants ?? [new(previous.Count)]).Concat(item.Variants ?? [new(item.Count)]).ToArray(),
            };
        }).ToArray();
        if (grant.EquippedRuntimeFormIds.Any(id => !additions.Any(item => item.RuntimeFormId == id) && !_items.Values.Any(item => item.RuntimeFormId == id)))
            throw new InvalidDataException("Equipped grant item is absent from inventory.");
        Publish(additions);
        _equipped.UnionWith(grant.EquippedRuntimeFormIds);
    }
    internal void Restore(FalloutCampaignInventory inventory, IReadOnlyCollection<uint> equipped, ulong? randomState = null)
    {
        if (_items.Count != 0 || _equipped.Count != 0) throw new InvalidOperationException("Inventory restoration requires a fresh owner.");
        if (equipped.Distinct().Count() != equipped.Count || equipped.Any(id => !inventory.Items.Any(item => item.RuntimeFormId == id)))
            throw new InvalidDataException("Restored equipment is absent or duplicated.");
        Publish(inventory.Items.ToArray());
        _equipped.UnionWith(equipped);
        if (randomState is { } state) _random = new(state);
    }
    internal FalloutOpeningInventoryGrant Capture() => new(new(Items, null), Equipped, _random.State);
}
