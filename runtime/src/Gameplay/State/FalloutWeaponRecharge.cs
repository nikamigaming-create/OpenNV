using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

/// <summary>Generated charge belongs to an individual weapon, never an AMMO loot stack.</summary>
internal static class FalloutWeaponRecharge
{
    internal static FalloutItemMagazine Materialize(FalloutPlayerInventory inventory, FalloutWeaponPresentation weapon)
    {
        if (!float.IsFinite(weapon.AmmoRegenerationSeconds) || weapon.AmmoRegenerationSeconds <= 0 ||
            weapon.ClipSize <= 0 || !weapon.HasAmmunitionSource || weapon.Ammunition.Count == 0)
            throw new InvalidDataException("Generated magazine has no source recharge interval or ammunition.");
        if (inventory.Item(weapon.Form) is not { } item || !inventory.Equipped.Contains(item.RuntimeFormId))
            throw new InvalidOperationException("Generated magazine materialization requires its actual equipped weapon process.");
        var selected = inventory.EnsureWeaponInstance(weapon.Form);
        if (selected.Extra.Magazine is { Generated: true } retained)
        {
            Validate(weapon, retained);
            return retained;
        }
        var ammunition = selected.Extra.Magazine?.Ammunition is { } prior && weapon.Ammunition.Contains(prior)
            ? prior : weapon.Ammunition[0];
        // Initial full charge is the current materialization policy, unverified
        // against retail acquisition/equip timing. Existing per-item charge
        // survives stowing, transfer and cold restoration; equip never refills it.
        var magazine = new FalloutItemMagazine(ammunition,
            Math.Min(weapon.ClipSize, selected.Extra.Magazine?.Loaded ?? weapon.ClipSize), Generated: true);
        inventory.SetWeaponMagazine(weapon.Form, selected.Extra.Instance!.Value, magazine);
        return magazine;
    }

    internal static void Advance(FalloutPlayerInventory inventory, FalloutWeaponPresentation weapon, double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0)
            throw new InvalidDataException("Weapon recharge simulation time is invalid.");
        if (seconds == 0 || weapon.AmmoRegenerationSeconds == 0) return;
        var item = inventory.Item(weapon.Form);
        if (item is null || !inventory.Equipped.Contains(item.RuntimeFormId)) return;
        var previous = Materialize(inventory, weapon);
        var next = Advance(previous, weapon, seconds);
        if (next != previous)
            inventory.SetWeaponMagazine(weapon.Form, inventory.SelectedWeapon(weapon.Form).Extra.Instance!.Value, next);
    }

    internal static FalloutItemMagazine Advance(FalloutItemMagazine magazine, FalloutWeaponPresentation weapon, double seconds)
    {
        Validate(weapon, magazine);
        if (!double.IsFinite(seconds) || seconds < 0)
            throw new InvalidDataException("Weapon recharge simulation time is invalid.");
        var missing = weapon.ClipSize - magazine.Loaded;
        var accumulated = magazine.RechargeSeconds + seconds;
        if (!double.IsFinite(accumulated)) throw new InvalidDataException("Weapon recharge clock overflowed.");
        // Equipped-only advancement and discarding credit at full charge are
        // explicit current policies. Exact retail stowed/reset timing is unverified.
        if (missing == 0 || accumulated >= missing * (double)weapon.AmmoRegenerationSeconds)
            return magazine with { Loaded = weapon.ClipSize, RechargeSeconds = 0 };
        var rounds = (int)Math.Floor(accumulated / weapon.AmmoRegenerationSeconds);
        return magazine with
        {
            Loaded = magazine.Loaded + rounds,
            RechargeSeconds = accumulated - rounds * (double)weapon.AmmoRegenerationSeconds
        };
    }

    internal static void Validate(FalloutWeaponPresentation weapon, FalloutItemMagazine magazine)
    {
        if (!magazine.Generated || !float.IsFinite(weapon.AmmoRegenerationSeconds) || weapon.AmmoRegenerationSeconds <= 0 ||
            !weapon.Ammunition.Contains(magazine.Ammunition) || magazine.Loaded < 0 || magazine.Loaded > weapon.ClipSize ||
            !double.IsFinite(magazine.RechargeSeconds) || magazine.RechargeSeconds < 0 ||
            magazine.RechargeSeconds >= weapon.AmmoRegenerationSeconds || magazine.Loaded == weapon.ClipSize && magazine.RechargeSeconds != 0)
            throw new InvalidDataException("Generated magazine differs from its source weapon or recharge clock.");
    }

    internal static void ValidateSource(FalloutPluginStack records, IEnumerable<FalloutCampaignItem> items)
    {
        foreach (var item in items)
            foreach (var extra in item.Variants ?? [])
                if (extra.Magazine is { Generated: true } magazine)
                    Validate(FalloutWeaponPresentation.Read(records, item.FormKey, weaponMods: extra.WeaponMods), magazine);
    }
}
