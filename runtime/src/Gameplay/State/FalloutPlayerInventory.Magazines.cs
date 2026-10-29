using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutPlayerInventory
{
    internal IReadOnlyList<FalloutItemVariant> RemovalExtras(FalloutFormKey form, int count)
    {
        var item = Item(form) ?? throw new InvalidOperationException("Removed contents are absent.");
        if (count < 0 || count > item.Count) throw new ArgumentOutOfRangeException(nameof(count));
        var result = new List<FalloutItemVariant>();
        foreach (var extra in item.Variants ?? [new FalloutItemVariant(item.Count)])
        {
            var take = Math.Min(count, extra.Count);
            if (take != 0) result.Add(extra with { Count = take, Hotkey = null });
            count -= take;
        }
        if (count != 0) throw new InvalidDataException("Removed contents differ from their inventory variants.");
        return result;
    }

    private static void ValidateWeaponInstances(IEnumerable<FalloutCampaignItem> items)
    {
        var identities = new HashSet<Guid>();
        foreach (var item in items)
            foreach (var extra in item.Variants ?? [])
            {
                if (extra.Instance is { } instance && (instance == Guid.Empty || extra.Count != 1 ||
                    item.RecordType != "WEAP" || !identities.Add(instance)))
                    throw new InvalidDataException("Inventory weapon instance identity is invalid or duplicated.");
                if (extra.Magazine is { } magazine && (extra.Instance is null || magazine.Loaded < 0 ||
                    magazine.Ammunition.ObjectId == 0 || string.IsNullOrWhiteSpace(magazine.Ammunition.OwnerPlugin)))
                    throw new InvalidDataException("Inventory magazine has no individual weapon or valid ammunition.");
            }
    }

    // Inventory totals include loaded rounds. Removing loose ammunition can
    // unload a clip, but can never leave more reserved rounds than exist.
    private void NormalizeMagazineAmmunition()
    {
        var available = Items.Where(item => item.RecordType == "AMMO").ToDictionary(item => item.FormKey, item => item.Count);
        foreach (var item in Items.Where(item => item.RecordType == "WEAP" && item.Variants is not null))
        {
            var changed = false;
            var variants = item.Variants!.Select(extra =>
            {
                if (extra.Magazine is not { } magazine) return extra;
                var rounds = Math.Min(magazine.Loaded, available.GetValueOrDefault(magazine.Ammunition));
                available[magazine.Ammunition] = available.GetValueOrDefault(magazine.Ammunition) - rounds;
                changed |= rounds != magazine.Loaded;
                return extra with { Magazine = magazine with { Loaded = rounds } };
            }).ToArray();
            if (changed) { _items[item.FormKey] = item with { Variants = variants }; ++Revision; }
        }
    }

    private void ValidateMagazineAmmunition()
    {
        foreach (var group in Items.SelectMany(item => item.Variants ?? []).Where(extra => extra.Magazine is not null)
            .GroupBy(extra => extra.Magazine!.Ammunition))
        {
            var loaded = group.Sum(extra => extra.Magazine!.Loaded);
            if (loaded == 0) continue;
            if (Item(group.Key) is not { RecordType: "AMMO" } ammo || loaded > ammo.Count)
                throw new InvalidDataException("Loaded weapon rounds exceed the authoritative inventory ammunition total.");
        }
    }

    private static IReadOnlyList<(FalloutFormKey Ammunition, int Loaded)> LoadedRoundTransfers(IEnumerable<FalloutItemVariant> variants) =>
        variants.Where(extra => extra.Magazine is { Loaded: > 0 }).GroupBy(extra => extra.Magazine!.Ammunition)
            .Select(group => (group.Key, group.Sum(extra => extra.Magazine!.Loaded))).ToArray();

    // An explicit ammunition line is the total requested, including rounds in
    // transferred clips. Guns publish first; that included prefix is not moved
    // a second time when the ammunition line is visited.
    private IReadOnlyList<FalloutTradeTransfer> PlanLoadedRoundBatch(FalloutPlayerInventory other,
        IReadOnlyList<FalloutTradeTransfer> requests)
    {
        var automatic = new Dictionary<(bool Direction, FalloutFormKey Form), int>();
        foreach (var group in requests.GroupBy(value => (value.FromThisInventory, value.Form)))
        {
            var source = group.Key.FromThisInventory ? this : other;
            var item = source.Item(group.Key.Form) ?? throw new InvalidOperationException("Batch transfer item is absent.");
            if (group.Any(value => value.Count <= 0) || group.Sum(value => value.Count) > item.Count)
                throw new InvalidOperationException("Batch transfer exceeds the source item quantity.");
            if (item.RecordType != "WEAP") continue;
            foreach (var rounds in LoadedRoundTransfers(source.RemovalExtras(item.FormKey, group.Sum(value => value.Count))))
            {
                var key = (group.Key.FromThisInventory, rounds.Ammunition);
                automatic[key] = checked(automatic.GetValueOrDefault(key) + rounds.Loaded);
            }
        }
        foreach (var group in requests.GroupBy(value => (value.FromThisInventory, value.Form)))
            if (automatic.GetValueOrDefault(group.Key) > group.Sum(value => value.Count))
                throw new InvalidOperationException("Requested ammunition total is smaller than the transferred weapon's loaded rounds.");
        var result = new List<FalloutTradeTransfer>();
        foreach (var request in requests.OrderBy(value => (value.FromThisInventory ? this : other).Item(value.Form)!.RecordType == "WEAP" ? 0 : 1))
        {
            var key = (request.FromThisInventory, request.Form);
            var included = Math.Min(request.Count, automatic.GetValueOrDefault(key));
            automatic[key] = automatic.GetValueOrDefault(key) - included;
            if (request.Count > included) result.Add(request with { Count = request.Count - included });
        }
        return result;
    }
}
