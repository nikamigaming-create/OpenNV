using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutMagazineSnapshot(FalloutFormKey Weapon, FalloutFormKey Ammunition, int Loaded,
    bool UsesInventoryAmmo = true, Guid Instance = default, bool Generated = false);
internal sealed record FalloutWeaponHandlingSnapshot(bool Drawn, IReadOnlyList<FalloutMagazineSnapshot> Magazines,
    ulong? ShotRandomState = null, ulong? AttackRandomState = null);

/// <summary>Inventory-backed magazine and draw state, shared by both player views.</summary>
internal sealed class FalloutWeaponHandling(FalloutPlayerInventory inventory, bool nativeNpc = false)
{
    // Unlimited NPC ammunition is a process resource, never physical loot.
    // Actual rounds live on the stable item instance and travel with it.
    private readonly Dictionary<Guid, FalloutMagazineSnapshot> _virtualMagazines = [];
    private bool _restored;
    private readonly FalloutSoundRandomState _shotRandom = new(BitConverter.ToUInt64(System.Security.Cryptography.RandomNumberGenerator.GetBytes(sizeof(ulong))));
    private readonly FalloutSoundRandomState _attackRandom = new(BitConverter.ToUInt64(System.Security.Cryptography.RandomNumberGenerator.GetBytes(sizeof(ulong))));
    internal bool Drawn { get; private set; } = true;
    internal void SetDrawn(bool drawn) => Drawn = drawn;

    private FalloutMagazineSnapshot? Magazine(FalloutFormKey weapon)
    {
        if (inventory.Item(weapon) is null) return null;
        var selected = inventory.SelectedWeapon(weapon).Extra;
        if (selected.Instance is not { } instance) return null;
        if (_virtualMagazines.TryGetValue(instance, out var virtualMagazine)) return virtualMagazine;
        if (selected.Magazine is { } actual) return new(weapon, actual.Ammunition, actual.Loaded, !actual.Generated, instance, actual.Generated);
        return null;
    }

    internal int Loaded(FalloutFormKey weapon) => Magazine(weapon) is { } entry
        ? entry.UsesInventoryAmmo ? Math.Min(entry.Loaded, inventory.Item(entry.Ammunition)?.Count ?? 0) : entry.Loaded : 0;

    internal bool NeedsAutomaticReload(FalloutWeaponPresentation weapon, int loadedBeforeDischarge) =>
        loadedBeforeDischarge > 0 && Loaded(weapon.Form) == 0 &&
        Magazine(weapon.Form) is { UsesInventoryAmmo: true } process &&
        inventory.Item(process.Ammunition) is { Count: > 0 } && CanReload(weapon);

    internal bool CanReload(FalloutWeaponPresentation weapon)
    {
        if (weapon.AmmoRegenerationSeconds > 0) return false;
        if (!weapon.HasAmmunitionSource || weapon.AmmoUse == 0 || !CanUse(weapon)) return false;
        var ammo = Ammunition(weapon);
        var instance = inventory.SelectedWeapon(weapon.Form).Extra.Instance;
        var available = ammo is null ? 0 : Math.Max(0, (inventory.Item(ammo.Value)?.Count ?? 0) - inventory.LoadedInventoryRounds(ammo.Value, instance));
        return ammo is not null && Loaded(weapon.Form) < (nativeNpc && !weapon.NpcsUseAmmo ? weapon.ClipSize : Math.Min(weapon.ClipSize, available));
    }

    internal FalloutFormKey? Ammunition(FalloutWeaponPresentation weapon)
    {
        if (Magazine(weapon.Form) is { } previous && (!previous.UsesInventoryAmmo || inventory.Item(previous.Ammunition) is { Count: > 0 }))
            return previous.Ammunition;
        var carried = weapon.Ammunition.Where(ammo => inventory.Item(ammo) is { Count: > 0 })
            .Select(ammo => (FalloutFormKey?)ammo).FirstOrDefault();
        if (carried is not null) return carried;
        if (weapon.AmmoRegenerationSeconds > 0 && weapon.Ammunition.Count != 0) return weapon.Ammunition[0];
        if (nativeNpc && !weapon.NpcsUseAmmo && weapon.Ammunition.Count != 0) return weapon.Ammunition[0];
        return null;
    }

    internal void CompleteReload(FalloutWeaponPresentation weapon)
    {
        if (weapon.AmmoRegenerationSeconds > 0) return;
        if (!weapon.HasAmmunitionSource || weapon.AmmoUse == 0) return;
        if (!CanUse(weapon))
            throw new NotSupportedException("Reload requires one selected, equipped weapon instance.");
        var ammo = Ammunition(weapon);
        if (ammo is null) return;
        // Carried totals include loaded rounds. Reload assigns the magazine;
        // only an accepted shot may remove ammunition from the inventory.
        var consumes = !nativeNpc || weapon.NpcsUseAmmo;
        var instance = inventory.EnsureWeaponInstance(weapon.Form).Extra.Instance!.Value;
        if (consumes)
        {
            var available = Math.Max(0, inventory.Item(ammo.Value)!.Count - inventory.LoadedInventoryRounds(ammo.Value, instance));
            inventory.SetWeaponMagazine(weapon.Form, instance, new(ammo.Value, Math.Min(weapon.ClipSize, available)));
        }
        else _virtualMagazines[instance] = new(weapon.Form, ammo.Value, weapon.ClipSize, false, instance);
    }

    internal bool InsertReloadRound(FalloutWeaponPresentation weapon, Guid instance, FalloutFormKey ammo)
    {
        if (instance == Guid.Empty || inventory.Item(weapon.Form) is null ||
            inventory.SelectedWeapon(weapon.Form).Extra.Instance != instance || Ammunition(weapon) != ammo)
            throw new InvalidDataException("Reload insert no longer belongs to its admitted weapon instance and ammunition.");
        if (!CanReload(weapon)) return false;
        var loaded = Loaded(weapon.Form);
        var next = checked(loaded + 1);
        var consumes = !nativeNpc || weapon.NpcsUseAmmo;
        if (consumes)
        {
            var available = Math.Max(0, inventory.Item(ammo)!.Count - inventory.LoadedInventoryRounds(ammo, instance));
            if (next > Math.Min(weapon.ClipSize, available)) return false;
            inventory.SetWeaponMagazine(weapon.Form, instance, new(ammo, next));
        }
        else _virtualMagazines[instance] = new(weapon.Form, ammo, next, false, instance);
        return true;
    }

    internal bool CanUse(FalloutWeaponPresentation weapon) => IsUnarmedSelection(weapon) ||
        inventory.Item(weapon.Form) is { Count: > 0 } item &&
        inventory.Equipped.Contains(item.RuntimeFormId) && FalloutWeaponCondition.CanUse(weapon, item);

    private bool IsUnarmedSelection(FalloutWeaponPresentation weapon) =>
        weapon is { DefaultUnarmed: true, WeaponAnimationType: 0, IsMeleeWeapon: true, HasAmmunitionSource: false } &&
        !inventory.Items.Any(item => item.RecordType == "WEAP" && inventory.Equipped.Contains(item.RuntimeFormId));

    internal bool CanFire(FalloutWeaponPresentation weapon)
    {
        if (!Drawn || !CanUse(weapon)) return false;
        // NAM0=None is infinite ammo; AmmoUse=0 still gates on a carried allowed type.
        if (!weapon.HasAmmunitionSource) return true;
        if (weapon.AmmoUse == 0) return Ammunition(weapon) is not null;
        return Loaded(weapon.Form) >= weapon.AmmoUse;
    }

    internal void MaterializeRecharge(FalloutWeaponPresentation weapon)
    {
        var instance = inventory.EnsureWeaponInstance(weapon.Form).Extra.Instance!.Value;
        _virtualMagazines.Remove(instance);
        FalloutWeaponRecharge.Materialize(inventory, weapon);
    }

    internal void AdvanceRecharge(FalloutWeaponPresentation weapon, double simulationSeconds)
    {
        if (weapon.AmmoRegenerationSeconds > 0 && simulationSeconds > 0 &&
            inventory.Item(weapon.Form) is { } item && inventory.Equipped.Contains(item.RuntimeFormId))
            MaterializeRecharge(weapon);
        FalloutWeaponRecharge.Advance(inventory, weapon, simulationSeconds);
    }

    internal void ApplyMeleeConditionWear(FalloutWeaponPresentation weapon, FalloutPluginStack records)
    {
        if (IsUnarmedSelection(weapon)) return;
        var item = inventory.Item(weapon.Form) ?? throw new InvalidOperationException("Attacking weapon is absent.");
        inventory.Publish([FalloutWeaponCondition.AfterMeleeStrike(records, weapon, item)]);
    }

    internal bool ConsumeShot(FalloutWeaponPresentation weapon, FalloutWeaponShot shot, FalloutPluginStack records)
    {
        if (shot.Weapon != weapon.Form || shot.Ammunition is { } ammunition && !weapon.Ammunition.Contains(ammunition) ||
            shot.Ammunition is null && weapon.HasAmmunitionSource)
            throw new InvalidDataException("Shot does not belong to its selected weapon/ammunition.");
        if (!CanFire(weapon)) return false;
        var consumedWeapon = weapon.IsThrownWeapon || weapon.IsMine;
        if (consumedWeapon && !weapon.HasAmmunitionSource)
        {
            inventory.Remove(weapon.Form, 1, silent: true);
            return true;
        }
        if (!weapon.HasAmmunitionSource || weapon.AmmoUse == 0)
        {
            if (weapon.HasAmmunitionSource && Ammunition(weapon) != shot.Ammunition)
                throw new InvalidOperationException("Ammunition changed before its ammo-free shot event.");
            if (consumedWeapon) inventory.Remove(weapon.Form, 1, silent: true);
            else inventory.Publish([FalloutWeaponCondition.AfterShot(records, weapon, shot, inventory.Item(weapon.Form)!)]);
            return true;
        }
        var previous = Magazine(weapon.Form) ?? throw new InvalidOperationException("Firing weapon has no instance magazine.");
        if (previous.Ammunition != shot.Ammunition) throw new InvalidOperationException("Ammunition changed before its shot event.");
        var loaded = Loaded(weapon.Form);
        if (previous.Generated)
        {
            if (weapon.AmmoRegenerationSeconds <= 0) throw new InvalidDataException("Generated ammunition policy changed before firing.");
            if (shot.RecoveredItem is not null && shot.RecoveryPercent > 0)
                throw new NotSupportedException("Generated charge with consumed-ammunition returns needs its source return policy.");
            var item = inventory.Item(weapon.Form)!;
            var magazine = inventory.SelectedWeapon(weapon.Form).Extra.Magazine!;
            FalloutWeaponRecharge.Validate(weapon, magazine);
            var worn = FalloutWeaponCondition.AfterShot(records, weapon, shot, item);
            inventory.Publish([FalloutPlayerInventory.WithWeaponMagazine(worn, previous.Instance,
                magazine with { Loaded = loaded - weapon.AmmoUse })]);
            return true;
        }
        if (!previous.UsesInventoryAmmo)
        {
            if (!nativeNpc || weapon.NpcsUseAmmo) throw new InvalidDataException("Actor ammunition policy changed before its shot.");
            _virtualMagazines[previous.Instance] = previous with { Loaded = loaded - weapon.AmmoUse };
            if (consumedWeapon) inventory.Remove(weapon.Form, 1, silent: true);
            return true;
        }
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
        var wornWeapon = consumedWeapon ? inventory.Item(weapon.Form)! : FalloutWeaponCondition.AfterShot(records, weapon, shot, inventory.Item(weapon.Form)!);
        wornWeapon = FalloutPlayerInventory.WithWeaponMagazine(wornWeapon, previous.Instance,
            new(previous.Ammunition, loaded - weapon.AmmoUse));
        inventory.ConsumeAmmunition(shot.Ammunition!.Value, weapon.AmmoUse, addition, wornWeapon);
        _shotRandom.Restore(random.State);
        if (consumedWeapon) inventory.Remove(weapon.Form, 1, silent: true);
        return true;
    }

    internal FalloutWeaponHandlingSnapshot Capture() => Reconcile(new(Drawn,
        inventory.Items.Where(item => item.RecordType == "WEAP").SelectMany(item => (item.Variants ?? [])
            .Where(extra => extra.Magazine is not null && !_virtualMagazines.ContainsKey(extra.Instance!.Value)).Select(extra => new FalloutMagazineSnapshot(item.FormKey,
                extra.Magazine!.Ammunition, extra.Magazine.Loaded, !extra.Magazine.Generated, extra.Instance!.Value, extra.Magazine.Generated)))
            .Concat(_virtualMagazines.Values).OrderBy(value => value.Weapon.ToString()).ThenBy(value => value.Instance).ToArray(),
        _shotRandom.State, _attackRandom.State), inventory.Items);

    internal static FalloutWeaponHandlingSnapshot Reconcile(FalloutWeaponHandlingSnapshot snapshot, IReadOnlyList<FalloutCampaignItem> items)
    {
        Validate(snapshot);
        var carried = items.ToDictionary(item => item.FormKey);
        return snapshot with
        {
            Magazines = snapshot.Magazines.Where(value => carried.GetValueOrDefault(value.Weapon)?.Variants?.Any(extra => extra.Instance == value.Instance) == true)
                .Select(value => value.UsesInventoryAmmo || value.Generated ? value with
                {
                    Loaded = carried[value.Weapon].Variants!.Single(extra => extra.Instance == value.Instance).Magazine is { } magazine &&
                        magazine.Ammunition == value.Ammunition && magazine.Generated == value.Generated ? magazine.Loaded :
                        throw new InvalidDataException("Retained magazine has no matching physical instance contents.")
                } : value).ToArray()
        };
    }

    internal void Restore(FalloutWeaponHandlingSnapshot snapshot, Func<FalloutFormKey, FalloutWeaponPresentation> resolve,
        Func<FalloutFormKey, byte, FalloutWeaponPresentation>? resolveInstance = null)
    {
        if (_restored || _virtualMagazines.Count != 0) throw new InvalidOperationException("Weapon handling restoration needs a fresh owner.");
        Validate(snapshot);
        var restored = new List<FalloutMagazineSnapshot>();
        foreach (var entry in snapshot.Magazines)
        {
            var extra = inventory.Item(entry.Weapon)?.Variants?.SingleOrDefault(value => value.Instance == entry.Instance)
                ?? throw new InvalidDataException("Saved magazine has no matching weapon instance.");
            var weapon = resolveInstance?.Invoke(entry.Weapon, extra.WeaponMods) ?? resolve(entry.Weapon);
            if (weapon.Form != entry.Weapon || !weapon.Ammunition.Contains(entry.Ammunition) ||
                (extra.Magazine?.Generated == true) != entry.Generated ||
                entry.Loaded > weapon.ClipSize || !entry.Generated && !entry.UsesInventoryAmmo && (!nativeNpc || weapon.NpcsUseAmmo) ||
                entry.UsesInventoryAmmo && (entry.Loaded > (inventory.Item(entry.Ammunition)?.Count ?? 0) ||
                    extra.Magazine is not { } magazine || magazine.Ammunition != entry.Ammunition || entry.Loaded != magazine.Loaded))
                throw new InvalidDataException("Saved magazine differs from its owned weapon or carried ammunition.");
            if (entry.Generated)
            {
                if (extra.Magazine is not { Generated: true } generated || generated.Ammunition != entry.Ammunition || generated.Loaded != entry.Loaded)
                    throw new InvalidDataException("Saved generated magazine has no matching individual charge owner.");
                FalloutWeaponRecharge.Validate(weapon, generated);
            }
            restored.Add(entry);
        }
        foreach (var entry in restored)
            if (entry.UsesInventoryAmmo) inventory.SetWeaponMagazine(entry.Weapon, entry.Instance, new(entry.Ammunition, entry.Loaded));
            else if (!entry.Generated) _virtualMagazines.Add(entry.Instance, entry);
        Drawn = snapshot.Drawn;
        if (snapshot.ShotRandomState is { } random) _shotRandom.Restore(random);
        if (snapshot.AttackRandomState is { } attackRandom) _attackRandom.Restore(attackRandom);
        _restored = true;
    }

    internal static void Validate(FalloutWeaponHandlingSnapshot snapshot)
    {
        if (snapshot.Magazines is null || snapshot.Magazines.Any(value => value is null || value.Loaded < 0 ||
            value.Weapon.ObjectId == 0 || value.Ammunition.ObjectId == 0 || value.Instance == Guid.Empty || value.Generated && value.UsesInventoryAmmo) ||
            snapshot.Magazines.Select(value => value.Instance).Distinct().Count() != snapshot.Magazines.Count)
            throw new InvalidDataException("Saved weapon handling state is invalid.");
    }

    internal float NextShotRandomUnit() => _shotRandom.NextUnitFloat();
    internal uint NextShotRandomUInt32() => _shotRandom.NextUInt32();
    internal uint NextAttackRandomUInt32() => _attackRandom.NextUInt32();
}
