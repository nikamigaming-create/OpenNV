using System.Buffers.Binary;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal readonly record struct FalloutWeaponDamage(float Amount, float LimbMultiplier, float Skill, float Condition,
    IReadOnlyList<FalloutAmmoEffect>? AmmoEffects = null, Guid? WeaponScriptInstance = null);

internal sealed class FalloutWeaponDamageResolver(FalloutPluginStack records, FalloutPlayerInventory inventory,
    Func<int, float> actorValue, Func<IReadOnlyList<FalloutPerkEntry>> perks,
    Func<FalloutCondition, float>? evaluateCondition = null)
{
    private float WeaponScale => FalloutGameSettingFloats.Read(records, "fDamageWeaponMult");
    private float SkillBase => FalloutGameSettingFloats.Read(records, "fDamageSkillBase");
    private float SkillScale => FalloutGameSettingFloats.Read(records, "fDamageSkillMult");

    // WEAP damage is one discharge's total. Every actual projectile contact
    // applies its share before the target's independent DT/DR and limb rules.
    // AMMO projectile overrides therefore preserve total damage (e.g. a slug),
    // rather than multiplying damage by the number of emitted projectiles.
    internal FalloutWeaponDamage Resolve(FalloutWeaponShot shot)
    {
        if (shot.Projectiles <= 0) throw new InvalidDataException("Projectile damage requires a positive emitted count.");
        var damage = Resolve(shot.Weapon, shot.BaseDamage, shot.AmmoEffects);
        return damage with { Amount = damage.Amount / shot.Projectiles };
    }

    internal FalloutWeaponDamage Resolve(FalloutWeaponShot shot, float baseDamage)
        => Resolve(shot.Weapon, baseDamage, shot.AmmoEffects);

    internal FalloutWeaponDamage Resolve(FalloutFormKey form, float baseDamage)
        => Resolve(form, baseDamage, []);

    internal FalloutWeaponDamage ResolveInventory(FalloutFormKey form, float baseDamage,
        FalloutItemVariant selected, IReadOnlyList<FalloutAmmoEffect> ammoEffects)
    {
        if (!inventory.Instances(form).Any(instance => instance.Extra == selected))
            throw new InvalidOperationException("Inventory damage selected variant is stale.");
        return Resolve(form, baseDamage, ammoEffects, selected);
    }

    private FalloutWeaponDamage Resolve(FalloutFormKey form, float baseDamage, IReadOnlyList<FalloutAmmoEffect> ammoEffects,
        FalloutItemVariant? selected = null)
    {
        var weapon = records.GetEffective(form);
        var data = weapon.ReadSubrecords().Single(field => field.Signature == "DNAM").Data.Span;
        if (data.Length < 120) throw new NotSupportedException("Weapon limb/skill layout is unbound.");
        var skill = actorValue(BinaryPrimitives.ReadInt32LittleEndian(data[104..]));
        var limb = FalloutProjectile.Number(data, 116);
        var unarmed = selected is null && form == FalloutDefaultUnarmedWeapon.Form(records) &&
            BinaryPrimitives.ReadUInt32LittleEndian(data) == 0 &&
            !inventory.Items.Any(item => item.RecordType == "WEAP" && inventory.Equipped.Contains(item.RuntimeFormId));
        var item = inventory.Item(form);
        if (!unarmed && item is null) throw new InvalidOperationException("Used weapon is absent from inventory.");
        var condition = unarmed ? 1 : selected is null ? FalloutWeaponCondition.SelectedCondition(item!) : selected.Condition ?? 1;
        baseDamage = FalloutWeaponModifications.Damage(records, form, unarmed ? (byte)0 : selected?.WeaponMods ?? inventory.WeaponModMask(form), baseDamage);
        // The engine's bare hand-to-hand owner adds derived AV UnarmedDamage
        // after the weapon contribution; it does not scale that AV by skill again.
        var damage = unarmed ? baseDamage * WeaponScale + actorValue(56) :
            baseDamage * WeaponScale * (SkillBase + SkillScale * skill / 100) * NewVegasConditionMultiplier(condition);
        foreach (var effect in ammoEffects.Where(effect => effect.Type == FalloutAmmoEffect.Damage))
            damage = Math.Max(0, effect.Apply(damage));
        foreach (var perk in perks().Where(perk => perk.Entry == 0))
        {
            perk.RequireActorConditionScope();
            if (perk.Conditions.Count != 0)
            {
                if (evaluateCondition is null)
                    throw new NotSupportedException("Conditional weapon damage perk has no player condition owner.");
                if (!FalloutCondition.AllPass(perk.Conditions, evaluateCondition)) continue;
            }
            damage = perk.Function switch
            {
                1 => perk.Value,
                2 => damage + perk.Value,
                3 => damage * perk.Value,
                _ => throw new NotSupportedException("Weapon damage perk operation is unbound.")
            };
        }
        if (!float.IsFinite(damage) || damage < 0 || limb < 0) throw new InvalidDataException("Weapon damage is invalid.");
        var sourceInstance = unarmed ? null : (selected ?? inventory.SelectedWeapon(form).Extra).ScriptState?.Identity;
        if (!unarmed && sourceInstance is null && FalloutScriptLocals.AttachedScript(records, weapon) is not null)
            throw new NotSupportedException("Weapon damage has no actual retained source item event-list.");
        return new(damage, limb, skill, condition, ammoEffects, sourceInstance);
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
