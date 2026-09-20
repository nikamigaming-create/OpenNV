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
        var variants = item.Variants;
        if (variants is { Count: > 1 })
            throw new NotSupportedException("Equipped weapon condition stack selection is unbound.");
        var condition = variants is { Count: 1 } ? variants[0].Condition ?? 1 : 1;
        if (!float.IsFinite(condition) || condition is < 0 or > 1)
            throw new InvalidDataException("Weapon condition is not a finite fraction.");
        return condition * weapon.ConditionHealth > 0;
    }

    internal static FalloutCampaignItem AfterShot(FalloutPluginStack records, FalloutWeaponPresentation weapon,
        FalloutWeaponShot shot, FalloutCampaignItem item)
    {
        if (shot.Weapon != weapon.Form || item.FormKey != weapon.Form || item.RecordType != "WEAP" || item.Count != 1)
            throw new InvalidDataException("Weapon condition update differs from the fired instance.");
        if (!CanUse(weapon, item)) throw new InvalidOperationException("A broken weapon cannot be fired.");

        var loss = ItemHealthLoss(records, weapon.Form, shot);
        if (loss == 0) return item;
        var variants = item.Variants ?? [new FalloutItemVariant(1)];
        if (variants.Count != 1 || variants[0].Count != 1)
            throw new NotSupportedException("Equipped weapon condition stack selection is unbound.");
        var current = variants[0].Condition ?? 1;
        var remainingHealth = Math.Max(0, current * weapon.ConditionHealth - loss);
        var condition = (float)(remainingHealth / weapon.ConditionHealth);
        if (!float.IsFinite(condition)) throw new InvalidDataException("Weapon condition loss is not finite.");
        return item with { Variants = [variants[0] with { Condition = condition }] };
    }

    private static float ItemHealthLoss(FalloutPluginStack records, FalloutFormKey weapon, FalloutWeaponShot shot)
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

        foreach (var effect in shot.AmmoEffects.Where(effect => effect.Type == FalloutAmmoEffect.WeaponCondition))
            loss = effect.Apply(loss);
        if (!float.IsFinite(loss)) throw new InvalidDataException("Ammo-adjusted weapon condition loss is not finite.");
        return Math.Max(0, loss);
    }
}
