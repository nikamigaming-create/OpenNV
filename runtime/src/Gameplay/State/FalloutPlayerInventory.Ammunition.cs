using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutPlayerInventory
{
    // Stage consumed rounds, recovered items and weapon wear together. A bad
    // source return or weapon variant must not partially mutate the inventory.
    internal void ConsumeAmmunition(FalloutFormKey form, int count, FalloutCampaignItem? returned,
        FalloutCampaignItem? wornWeapon = null)
    {
        var source = Item(form) ?? throw new InvalidOperationException("Fired ammunition is absent.");
        if (source.RecordType != "AMMO" || count <= 0 || count > source.Count)
            throw new InvalidDataException("Ammunition consumption is invalid.");
        if (returned is not null && (returned.RecordType is not ("MISC" or "AMMO") || returned.Count <= 0 || returned.Variants is { Count: > 0 }))
            throw new InvalidDataException("Recovered ammunition has invalid source inventory data.");
        if (wornWeapon is not null)
        {
            var weapon = Item(wornWeapon.FormKey) ?? throw new InvalidOperationException("Fired weapon is absent.");
            if (weapon.RecordType != "WEAP" || wornWeapon.RecordType != "WEAP" || wornWeapon.Count != weapon.Count ||
                wornWeapon.RuntimeFormId != weapon.RuntimeFormId || wornWeapon.EditorId != weapon.EditorId ||
                wornWeapon.FormKey == form || wornWeapon.FormKey == returned?.FormKey)
                throw new InvalidDataException("Weapon wear does not match the carried weapon instance.");
        }

        var staged = new Dictionary<FalloutFormKey, FalloutCampaignItem>(_items);
        var remaining = source.Count - count;
        var remainingVariants = remaining == 0 ? null : AfterRemovalVariants(source, count);
        if (remaining == 0) staged.Remove(form);
        else staged[form] = source with { Count = remaining, Variants = remainingVariants };
        if (returned is not null)
        {
            var previous = staged.GetValueOrDefault(returned.FormKey);
            if (previous is not null && (previous.RuntimeFormId != returned.RuntimeFormId ||
                previous.RecordType != returned.RecordType || previous.EditorId != returned.EditorId))
                throw new InvalidDataException("Recovered item identity differs from its existing inventory stack.");
            var stacks = (previous?.Variants ?? (previous is { Count: > 0 } ? [new(previous.Count)] : Array.Empty<FalloutItemVariant>())).ToList();
            var plain = stacks.FindIndex(value => value with { Count = 1 } == new FalloutItemVariant(1));
            if (plain < 0) stacks.Add(new(returned.Count));
            else stacks[plain] = stacks[plain] with { Count = checked(stacks[plain].Count + returned.Count) };
            staged[returned.FormKey] = returned with { Count = checked((previous?.Count ?? 0) + returned.Count), Variants = stacks };
        }
        if (wornWeapon is not null) staged[wornWeapon.FormKey] = wornWeapon;
        if (staged.Values.Any(item => item.Count <= 0 || item.Variants is { } variants &&
            (variants.Sum(value => value.Count) != item.Count || variants.Any(value => value.Count <= 0 ||
                value.Condition is { } condition && (!float.IsFinite(condition) || condition < 0 || condition > 1)))))
            throw new InvalidDataException("Weapon shot inventory transaction contains invalid item variants.");

        _items.Clear();
        foreach (var item in staged) _items.Add(item.Key, item.Value);
        ++Revision;
    }
}
