using System.Buffers.Binary;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal static class FalloutWeaponCondition
{
    private const float BaseItemHealthLossPerShot = .2f;
    private const uint DamageToWeaponOverride = 0x80;

    internal static bool CanUse(FalloutWeaponPresentation weapon, FalloutCampaignItem item)
    {
        if (weapon.ConditionHealth <= 0)
            throw new InvalidDataException("Weapon maximum condition health is not positive.");
        var condition = SelectedCondition(item);
        return condition * weapon.ConditionHealth > 0;
    }

    internal static float SelectedCondition(FalloutCampaignItem item) => SelectVariant(item).Condition;

    internal static FalloutCampaignItem AfterShot(FalloutPluginStack records, FalloutWeaponPresentation weapon,
        FalloutWeaponShot shot, FalloutCampaignItem item)
    {
        if (shot.Weapon != weapon.Form || item.FormKey != weapon.Form || item.RecordType != "WEAP" || item.Count <= 0)
            throw new InvalidDataException("Weapon condition update differs from the fired instance.");
        if (!CanUse(weapon, item)) throw new InvalidOperationException("A broken weapon cannot be fired.");

        var loss = ItemHealthLoss(records, weapon.Form, shot.AmmoEffects);
        return ApplyLoss(weapon, item, loss);
    }

    internal static FalloutCampaignItem AfterMeleeStrike(FalloutPluginStack records,
        FalloutWeaponPresentation weapon, FalloutCampaignItem item)
    {
        if (!weapon.IsMeleeWeapon)
            throw new InvalidDataException("Melee condition update requires a source melee equipment type.");
        if (item.FormKey != weapon.Form || item.RecordType != "WEAP" || item.Count <= 0)
            throw new InvalidDataException("Melee condition update differs from the equipped weapon instance.");
        if (!CanUse(weapon, item)) throw new InvalidOperationException("A broken weapon cannot attack.");
        return ApplyLoss(weapon, item, ItemHealthLoss(records, weapon.Form, []));
    }

    private static FalloutCampaignItem ApplyLoss(FalloutWeaponPresentation weapon,
        FalloutCampaignItem item, float loss)
    {
        if (loss == 0) return item;
        var selected = SelectVariant(item);
        if (selected.Condition <= 0) throw new InvalidOperationException("A broken weapon cannot lose more condition.");
        var current = selected.Condition;
        var remainingHealth = Math.Max(0, current * weapon.ConditionHealth - loss);
        var condition = (float)(remainingHealth / weapon.ConditionHealth);
        if (!float.IsFinite(condition)) throw new InvalidDataException("Weapon condition loss is not finite.");
        var worn = selected.Variant with { Count = 1, Condition = condition };
        var variants = new List<FalloutItemVariant>(selected.Variants.Count + 1);
        for (var index = 0; index < selected.Variants.Count; index++)
        {
            if (index != selected.Index)
            {
                variants.Add(selected.Variants[index]);
                continue;
            }
            variants.Add(worn);
            if (selected.Variant.Count > 1)
                variants.Add(selected.Variant with { Count = selected.Variant.Count - 1 });
        }
        return item with { Variants = variants };
    }

    private static (IReadOnlyList<FalloutItemVariant> Variants, int Index, FalloutItemVariant Variant, float Condition)
        SelectVariant(FalloutCampaignItem item)
    {
        if (item.Count <= 0) throw new InvalidDataException("Weapon inventory count is not positive.");
        var variants = item.Variants ?? [new FalloutItemVariant(item.Count)];
        if (variants.Count == 0 || variants.Sum(variant => variant.Count) != item.Count)
            throw new InvalidDataException("Weapon condition variants do not match the inventory count.");
        var first = -1;
        for (var index = 0; index < variants.Count; index++)
        {
            var variant = variants[index];
            var condition = variant.Condition ?? 1;
            if (variant.Count <= 0 || !float.IsFinite(condition) || condition is < 0 or > 1)
                throw new InvalidDataException("Weapon condition variant is invalid.");
            if (first < 0) first = index;
            if (condition > 0) return (variants, index, variant, condition);
        }
        var broken = variants[first];
        return (variants, first, broken, broken.Condition ?? 1);
    }

    private static float ItemHealthLoss(FalloutPluginStack records, FalloutFormKey weapon,
        IReadOnlyList<FalloutAmmoEffect> ammoEffects)
    {
        var source = records.GetEffective(weapon);
        var fields = source.ReadSubrecords().ToArray();
        var data = fields.Single(field => field.Signature == "DATA").Data.Span;
        var dnam = fields.Single(field => field.Signature == "DNAM").Data.Span;
        if (source.Signature != "WEAP" || data.Length != 15 || dnam.Length is not (120 or 124 or 136 or 200 or 204))
            throw new NotSupportedException("Weapon condition source layout is unbound.");

        var flags = BinaryPrimitives.ReadUInt32LittleEndian(dnam[56..]);
        var loss = BaseItemHealthLossPerShot;
        if ((flags & DamageToWeaponOverride) != 0)
        {
            var baseDamage = BinaryPrimitives.ReadInt16LittleEndian(data[12..]);
            var multiplier = FalloutProjectile.Number(dnam, 84);
            if (baseDamage < 0 || multiplier < 0)
                throw new InvalidDataException("Weapon damage-to-condition override is invalid.");
            loss = baseDamage * multiplier;
        }

        foreach (var effect in ammoEffects.Where(effect => effect.Type == FalloutAmmoEffect.WeaponCondition))
            loss = effect.Apply(loss);
        if (!float.IsFinite(loss)) throw new InvalidDataException("Ammo-adjusted weapon condition loss is not finite.");
        return Math.Max(0, loss);
    }
}
