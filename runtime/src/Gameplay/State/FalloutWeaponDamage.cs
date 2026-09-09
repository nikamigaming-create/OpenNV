using System.Buffers.Binary;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal readonly record struct FalloutWeaponDamage(float Amount, float LimbMultiplier, float Skill, float Condition);

internal sealed class FalloutWeaponDamageResolver(FalloutPluginStack records, FalloutPlayerInventory inventory,
    Func<int, float> actorValue, Func<IReadOnlyList<FalloutPerkEntry>> perks)
{
    private readonly float _weaponScale = FalloutGameSettingFloats.Read(records, "fDamageWeaponMult");
    private readonly float _skillBase = FalloutGameSettingFloats.Read(records, "fDamageSkillBase");
    private readonly float _skillScale = FalloutGameSettingFloats.Read(records, "fDamageSkillMult");

    internal FalloutWeaponDamage Resolve(FalloutWeaponShot shot)
    {
        var weapon = records.GetEffective(shot.Weapon);
        var data = weapon.ReadSubrecords().Single(field => field.Signature == "DNAM").Data.Span;
        if (data.Length < 120) throw new NotSupportedException("Weapon limb/skill layout is unbound.");
        var skill = actorValue(BinaryPrimitives.ReadInt32LittleEndian(data[104..]));
        var limb = FalloutProjectile.Number(data, 116);
        var item = inventory.Item(shot.Weapon) ?? throw new InvalidOperationException("Fired weapon is absent from inventory.");
        if (item.Variants is { Count: > 1 }) throw new NotSupportedException("Equipped weapon condition stack selection is unbound.");
        var condition = item.Variants is { Count: 1 } variants ? variants[0].Condition ?? 1 : 1;
        var damage = shot.BaseDamage * _weaponScale * (_skillBase + _skillScale * skill / 100) * NewVegasConditionMultiplier(condition);
        foreach (var perk in perks().Where(perk => perk.Entry == 0))
        {
            if (perk.Conditions.Count != 0) throw new NotSupportedException("Conditional weapon damage perk is unbound.");
            damage = perk.Function switch
            {
                1 => perk.Value,
                2 => damage + perk.Value,
                3 => damage * perk.Value,
                _ => throw new NotSupportedException("Weapon damage perk operation is unbound.")
            };
        }
        if (!float.IsFinite(damage) || damage < 0 || limb < 0) throw new InvalidDataException("Weapon damage is invalid.");
        return new(damage, limb, skill, condition);
    }

    // NV's normal-hit and inventory-damage paths share this rule. It is flat
    // above 75% condition and loses 0.67 damage fraction per condition fraction
    // below that threshold. FO3's condition GMSTs do not own this NV curve.
    internal static float NewVegasConditionMultiplier(float condition)
    {
        if (!float.IsFinite(condition) || condition is < 0 or > 1)
            throw new InvalidDataException("Weapon condition must be a finite fraction.");
        return condition > .75f ? 1 : (float)(1 - (.75 - condition) * (double).67f);
    }
}
