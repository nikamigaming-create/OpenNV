using System.Buffers.Binary;
using System.Globalization;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal partial class RuntimeNativePlayer
{
    internal float InventoryCarryCapacity() => FalloutPlayerPerkNumericEffects.Apply(22,
        (_combatActorValue ?? throw new NotSupportedException("Inventory capacity has no player AV owner."))(13),
        (_combatPerks ?? throw new NotSupportedException("Inventory capacity has no player perk owner."))(),
        _combatCondition ?? throw new NotSupportedException("Inventory capacity has no player condition owner."));

    private FalloutFormKey? InventoryAmmunition(FalloutFormKey weapon)
    {
        var records = _presentationRecords ?? throw new NotSupportedException("Inventory weapon has no source owner.");
        var inventory = _presentationInventory ?? throw new NotSupportedException("Inventory weapon has no live inventory owner.");
        var item = inventory.Item(weapon) ?? throw new InvalidOperationException("Inventory weapon is no longer carried.");
        if (inventory.Equipped.Contains(item.RuntimeFormId) && inventory.SelectedWeapon(weapon).Extra.Magazine is { } magazine)
        {
            if (records.GetEffective(magazine.Ammunition).Signature != "AMMO")
                throw new InvalidDataException("Current weapon magazine is not AMMO.");
            return magazine.Ammunition;
        }
        return FalloutInventoryItemStatistics.DefaultAmmunition(records, weapon, _presentationFormLists);
    }

    internal string? InventoryAmmunitionText(FalloutFormKey weapon, FalloutItemVariant extra)
    {
        var ammunition = InventoryAmmunition(weapon);
        if (ammunition is null) return null;
        var records = _presentationRecords!;
        var inventory = _presentationInventory!;
        var source = records.GetEffective(weapon);
        var fields = source.ReadSubrecords().ToArray();
        var data = fields.Single(field => field.Signature == "DNAM").Data.Span;
        if (!FalloutWeaponPresentation.IsSupportedDnamExtent(data.Length) || data.Length < 180)
            throw new NotSupportedException("Inventory ammunition regeneration layout is unbound.");
        var name = FalloutInventoryItemStatistics.AmmoName(records, ammunition.Value);
        var regeneration = BinaryPrimitives.ReadSingleLittleEndian(data[176..]);
        if (!float.IsFinite(regeneration)) throw new InvalidDataException("Weapon regeneration rate is invalid.");
        var item = inventory.Item(weapon)!;
        if (!inventory.Instances(weapon).Any(instance => instance.Extra == extra))
            throw new InvalidOperationException("Inventory ammunition selected variant is stale.");
        if (regeneration != 0) return name;
        var total = inventory.Item(ammunition.Value)?.Count ?? 0;
        int loaded;
        if (inventory.Equipped.Contains(item.RuntimeFormId) && inventory.SelectedWeapon(weapon).Extra == extra)
            loaded = (_weaponHandling ?? throw new NotSupportedException("Inventory magazine has no current weapon owner.")).Loaded(weapon);
        else
        {
            var itemData = fields.Single(field => field.Signature == "DATA").Data.Span;
            if (itemData.Length != 15) throw new NotSupportedException("Inventory clip layout is unbound.");
            var mods = FalloutWeaponModifications.Read(records, weapon).Where(mod => (extra.WeaponMods & (1 << mod.Slot)) != 0).ToArray();
            if ((extra.WeaponMods & ~7) != 0 || mods.Aggregate(0, (mask, mod) => mask | (1 << mod.Slot)) != extra.WeaponMods)
                throw new InvalidDataException("Inventory clip has no matching installed modification slot.");
            var amount = mods.Any(mod => mod.Effect == 2)
                ? FalloutWeaponModifications.FirstAuthoredPrimaryValue(records, weapon, 2) : 0;
            if (amount < int.MinValue || (double)amount > int.MaxValue)
                throw new NotSupportedException("Inventory clip modifier exceeds source integer storage.");
            loaded = Math.Min(total, itemData[14] + unchecked((byte)(int)amount));
        }
        if (loaded < 0 || loaded > total) throw new InvalidDataException("Inventory ammo counts disagree with their actual carried total.");
        return string.Create(CultureInfo.InvariantCulture, $"{name} ({loaded}/{total - loaded})");
    }

    internal string InventoryStrengthText(FalloutFormKey weapon)
    {
        var records = _presentationRecords ?? throw new NotSupportedException("Inventory requirement has no source owner.");
        var data = records.GetEffective(weapon).ReadSubrecords().Single(field => field.Signature == "DNAM").Data.Span;
        if (!FalloutWeaponPresentation.IsSupportedDnamExtent(data.Length) || data.Length < 172)
            throw new NotSupportedException("Inventory strength requirement layout is unbound.");
        if ((_combatPerks ?? throw new NotSupportedException("Inventory requirement has no actual perk owner."))()
            .Any(perk => perk.Entry == 53))
            throw new NotSupportedException("Inventory strength reduction requires its weapon-context perk53 owner.");
        var requirement = BinaryPrimitives.ReadUInt32LittleEndian(data[168..]);
        if (requirement > int.MaxValue) throw new NotSupportedException("Inventory requirement exceeds source signed display storage.");
        return requirement.ToString(CultureInfo.InvariantCulture);
    }

    internal string InventoryDamageText(FalloutFormKey weapon, FalloutItemVariant extra)
    {
        var records = _presentationRecords ?? throw new NotSupportedException("Inventory damage has no source owner.");
        if (!(_presentationInventory ?? throw new NotSupportedException("Inventory damage has no live inventory owner."))
            .Instances(weapon).Any(instance => instance.Extra == extra))
            throw new InvalidOperationException("Inventory damage selected variant is stale.");
        var ammo = InventoryAmmunition(weapon);
        var shot = FalloutWeaponShot.Read(records, weapon, ammo, ammo is not null);
        shot.RequireRuntimeAttackOwner();
        if (shot.Projectile.Type is 4 or 8 || shot.WeaponAnimationType is 9 or 10 or 13 || shot.Projectile.ExplosionSource is not null)
            throw new NotSupportedException("Inventory beam/flame/explosive/thrown damage needs its effective source owner.");
        // The presentation-only multi-projectile division and continuous attack
        // families are not inferred from a normal single-projectile hit.
        if (shot.Projectiles != 1 || FalloutWeaponModifications.Read(records, weapon)
            .Any(mod => (extra.WeaponMods & (1 << mod.Slot)) != 0 && mod.Effect == 12))
            throw new NotSupportedException("Inventory multi-projectile damage needs its effective split owner.");
        if ((extra.Condition ?? 1) == 0) return "0";
        var result = (_damage ?? throw new NotSupportedException("Inventory damage has no actual player damage owner."))
            .ResolveInventory(weapon, shot.BaseDamage, extra, shot.AmmoEffects);
        return FalloutInventoryItemStatistics.RoundIncrement(result.Amount, 1).ToString("F0", CultureInfo.InvariantCulture);
    }
}
