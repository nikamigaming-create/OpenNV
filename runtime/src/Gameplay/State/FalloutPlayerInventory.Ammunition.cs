using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutPlayerInventory
{
    // Validate both changes before publishing either. A bad source return must
    // not consume a round, advance randomness or partially fill another stack.
    internal void ConsumeAmmunition(FalloutFormKey form, int count, FalloutCampaignItem? returned)
    {
        var source = Item(form) ?? throw new InvalidOperationException("Fired ammunition is absent.");
        if (source.RecordType != "AMMO" || count <= 0 || count > source.Count)
            throw new InvalidDataException("Ammunition consumption is invalid.");
        if (source.Variants is { Count: > 1 } && count != source.Count)
            throw new NotSupportedException("Firing across distinct ammunition ownership stacks needs its selection owner.");
        if (returned is not null && (returned.RecordType is not ("MISC" or "AMMO") || returned.Count <= 0 || returned.Variants is { Count: > 0 }))
            throw new InvalidDataException("Recovered ammunition has invalid source inventory data.");
        var remaining = source.Count - count;
        FalloutCampaignItem? addition = null;
        if (returned is not null)
        {
            var previous = returned.FormKey == form ? source with
            {
                Count = remaining,
                Variants = remaining == 0 ? null : source.Variants is { Count: 1 } variants ? [variants[0] with { Count = remaining }] : null
            } : Item(returned.FormKey);
            var stacks = (previous?.Variants ?? (previous is { Count: > 0 } ? [new(previous.Count)] : Array.Empty<FalloutItemVariant>())).ToList();
            var plain = stacks.FindIndex(value => value with { Count = 1 } == new FalloutItemVariant(1));
            if (plain < 0) stacks.Add(new(returned.Count));
            else stacks[plain] = stacks[plain] with { Count = checked(stacks[plain].Count + returned.Count) };
            addition = returned with { Count = checked((previous?.Count ?? 0) + returned.Count), Variants = stacks };
        }
        Remove(form, count, true);
        if (addition is not null) Publish([addition]);
    }
}
