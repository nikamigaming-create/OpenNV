using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutMagazineSnapshot(FalloutFormKey Weapon, FalloutFormKey Ammunition, int Loaded);
internal sealed record FalloutWeaponHandlingSnapshot(bool Drawn, IReadOnlyList<FalloutMagazineSnapshot> Magazines, ulong? ShotRandomState = null);

/// <summary>Inventory-backed magazine and draw state, shared by both player views.</summary>
internal sealed class FalloutWeaponHandling(FalloutPlayerInventory inventory)
{
    private readonly Dictionary<FalloutFormKey, FalloutMagazineSnapshot> _magazines = [];
    private readonly FalloutSoundRandomState _shotRandom = new(BitConverter.ToUInt64(System.Security.Cryptography.RandomNumberGenerator.GetBytes(sizeof(ulong))));
    internal bool Drawn { get; private set; } = true;
    internal void SetDrawn(bool drawn) => Drawn = drawn;

    internal int Loaded(FalloutFormKey weapon) => _magazines.TryGetValue(weapon, out var entry)
        ? Math.Min(entry.Loaded, inventory.Item(entry.Ammunition)?.Count ?? 0) : 0;

    internal bool CanReload(FalloutWeaponPresentation weapon)
    {
        var ammo = Ammunition(weapon);
        return ammo is not null && Loaded(weapon.Form) < Math.Min(weapon.ClipSize, inventory.Item(ammo.Value)!.Count);
    }

    internal FalloutFormKey? Ammunition(FalloutWeaponPresentation weapon)
    {
        if (_magazines.TryGetValue(weapon.Form, out var previous) && inventory.Item(previous.Ammunition) is { Count: > 0 })
            return previous.Ammunition;
        return weapon.Ammunition.Where(ammo => inventory.Item(ammo) is { Count: > 0 }).Select(ammo => (FalloutFormKey?)ammo).FirstOrDefault();
    }

    internal void CompleteReload(FalloutWeaponPresentation weapon)
    {
        if (inventory.Item(weapon.Form) is not { Count: 1 } || !inventory.Equipped.Contains(inventory.Item(weapon.Form)!.RuntimeFormId))
            throw new NotSupportedException("Reload requires one selected, equipped weapon instance.");
        var ammo = Ammunition(weapon);
        if (ammo is null) return;
        // Carried totals include loaded rounds. Reload assigns the magazine;
        // only an accepted shot may remove ammunition from the inventory.
        _magazines[weapon.Form] = new(weapon.Form, ammo.Value, Math.Min(weapon.ClipSize, inventory.Item(ammo.Value)!.Count));
    }

    internal bool CanFire(FalloutWeaponPresentation weapon) => Drawn && weapon.AmmoUse > 0 &&
        Loaded(weapon.Form) >= weapon.AmmoUse && inventory.Item(weapon.Form) is { Count: 1 } item && inventory.Equipped.Contains(item.RuntimeFormId);

    internal bool ConsumeShot(FalloutWeaponPresentation weapon, FalloutWeaponShot shot, FalloutPluginStack records)
    {
        if (shot.Weapon != weapon.Form || !weapon.Ammunition.Contains(shot.Ammunition))
            throw new InvalidDataException("Shot does not belong to its selected weapon/ammunition.");
        if (!CanFire(weapon)) return false;
        var previous = _magazines[weapon.Form];
        if (previous.Ammunition != shot.Ammunition) throw new InvalidOperationException("Ammunition changed before its shot event.");
        var loaded = Loaded(weapon.Form);
        var random = new FalloutSoundRandomState(_shotRandom.State);
        var recovered = 0;
        if (shot.RecoveredItem is not null)
            for (var round = 0; round < weapon.AmmoUse; round++)
                if (random.NextUnitFloat() * 100 < shot.RecoveryPercent) recovered++;
        FalloutCampaignItem? addition = null;
        if (recovered > 0)
        {
            var key = shot.RecoveredItem!.Value;
            var source = records.GetEffective(key);
            addition = FalloutCampaignInventoryResolver.Resolve(records,
                [new(records.RuntimeFormId(key), FalloutDialogueTopic.Text(source.ReadSubrecords().Single(field => field.Signature == "EDID").Data.Span), source.Signature, recovered)], null).Items.Single();
        }
        inventory.ConsumeAmmunition(shot.Ammunition, weapon.AmmoUse, addition);
        _magazines[weapon.Form] = previous with { Loaded = loaded - weapon.AmmoUse };
        _shotRandom.Restore(random.State);
        return true;
    }

    internal FalloutWeaponHandlingSnapshot Capture() => new(Drawn,
        _magazines.Values.Where(value => inventory.Item(value.Weapon) is not null).Select(value => value with
        { Loaded = Math.Min(value.Loaded, inventory.Item(value.Ammunition)?.Count ?? 0) }).OrderBy(value => value.Weapon.ToString()).ToArray(), _shotRandom.State);

    internal void Restore(FalloutWeaponHandlingSnapshot snapshot, Func<FalloutFormKey, FalloutWeaponPresentation> resolve)
    {
        if (_magazines.Count != 0) throw new InvalidOperationException("Weapon handling restoration needs a fresh owner.");
        Validate(snapshot);
        var restored = new Dictionary<FalloutFormKey, FalloutMagazineSnapshot>();
        foreach (var entry in snapshot.Magazines)
        {
            var weapon = resolve(entry.Weapon);
            if (inventory.Item(entry.Weapon) is null || !weapon.Ammunition.Contains(entry.Ammunition) ||
                entry.Loaded > weapon.ClipSize || entry.Loaded > (inventory.Item(entry.Ammunition)?.Count ?? 0))
                throw new InvalidDataException("Saved magazine differs from its owned weapon or carried ammunition.");
            restored.Add(entry.Weapon, entry);
        }
        foreach (var entry in restored) _magazines.Add(entry.Key, entry.Value);
        Drawn = snapshot.Drawn;
        if (snapshot.ShotRandomState is { } random) _shotRandom.Restore(random);
    }

    internal static void Validate(FalloutWeaponHandlingSnapshot snapshot)
    {
        if (snapshot.Magazines is null || snapshot.Magazines.Any(value => value is null || value.Loaded < 0 ||
            value.Weapon.ObjectId == 0 || value.Ammunition.ObjectId == 0) ||
            snapshot.Magazines.Select(value => value.Weapon).Distinct().Count() != snapshot.Magazines.Count)
            throw new InvalidDataException("Saved weapon handling state is invalid.");
    }
}
