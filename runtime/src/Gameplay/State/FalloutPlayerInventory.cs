using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

/// <summary>Shared player item state for native source commands and saves.</summary>
internal sealed class FalloutPlayerInventory
{
    private readonly Dictionary<FalloutFormKey, FalloutCampaignItem> _items = [];
    private readonly HashSet<uint> _equipped = [];
    internal IReadOnlyList<FalloutCampaignItem> Items => _items.Values.OrderBy(item => item.RuntimeFormId).ToArray();
    internal IReadOnlyList<uint> Equipped => _equipped.Order().ToArray();
    internal FalloutCampaignItem? Item(FalloutFormKey form) => _items.GetValueOrDefault(form);
    internal void Publish(IReadOnlyCollection<FalloutCampaignItem> replacements)
    {
        if (replacements.Any(item => item.Count <= 0) || replacements.Select(item => item.FormKey).Distinct().Count() != replacements.Count)
            throw new InvalidDataException("Player inventory transaction contains invalid or duplicate items.");
        foreach (var item in replacements) _items[item.FormKey] = item;
    }
    internal void AddGrant(FalloutOpeningInventoryGrant grant)
    {
        var additions = grant.Inventory.Items.Select(item => item with { Count = checked(item.Count + (Item(item.FormKey)?.Count ?? 0)) }).ToArray();
        if (grant.EquippedRuntimeFormIds.Any(id => !additions.Any(item => item.RuntimeFormId == id) && !_items.Values.Any(item => item.RuntimeFormId == id)))
            throw new InvalidDataException("Equipped grant item is absent from inventory.");
        Publish(additions);
        _equipped.UnionWith(grant.EquippedRuntimeFormIds);
    }
    internal void Restore(FalloutCampaignInventory inventory, IReadOnlyCollection<uint> equipped)
    {
        if (_items.Count != 0 || _equipped.Count != 0) throw new InvalidOperationException("Inventory restoration requires a fresh owner.");
        if (equipped.Distinct().Count() != equipped.Count || equipped.Any(id => !inventory.Items.Any(item => item.RuntimeFormId == id)))
            throw new InvalidDataException("Restored equipment is absent or duplicated.");
        Publish(inventory.Items.ToArray());
        _equipped.UnionWith(equipped);
    }
    internal FalloutOpeningInventoryGrant Capture() => new(new(Items, null), Equipped);
}
