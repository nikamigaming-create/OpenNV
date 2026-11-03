using System.Buffers.Binary;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

// WEAP EITM is delivered by an actual contact, with the victim as CTDA
// subject and the attacker as target. Tooltip-only declarations have no
// runtime effect; their editor names are not a gameplay dispatch table.
internal static class FalloutWeaponTouchEffects
{
    internal static FalloutIngestible? Read(FalloutPluginStack records, FalloutFormKey weapon)
    {
        var record = records.GetEffective(weapon);
        if (record.Signature != "WEAP") throw new InvalidDataException("Weapon effect delivery requires WEAP.");
        var fields = record.ReadSubrecords().Where(field => field.Signature == "EITM").ToArray();
        if (fields.Length == 0) return null;
        if (fields.Length != 1 || fields[0].Data.Length != 4)
            throw new InvalidDataException("Weapon object effect requires one four-byte EITM.");
        var form = record.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(fields[0].Data.Span));
        return form is { } source ? FalloutIngestible.ReadWeaponEnchantment(records, source) : null;
    }

    internal static FalloutIngestible Select(FalloutIngestible source, Func<FalloutCondition, float> condition, bool areaDelivered = false)
    {
        var effects = source.Effects.Where(effect => (effect.Flags & 0x2000) == 0 &&
            FalloutCondition.AllPass(effect.Conditions, condition)).ToArray();
        // Select before admission: a robot-only script is not an active effect
        // on a creature. Admit the complete active set before any AV mutation.
        foreach (var effect in effects)
        {
            if (effect.Archetype != 0 || effect.Range != 1 || effect.Area != 0 && !areaDelivered ||
                (effect.Flags & 0x20) == 0 || (effect.Flags & ~0x1f5u) != 0 ||
                effect.EffectiveDuration != 0 || effect.AdditionalPresentation ||
                effect.Script is not null || effect.Resistance != -1 ||
                effect.ActorValue is not (>= 5 and <= 11 or 16 or 22))
                throw new NotSupportedException($"Weapon touch effect {effect.Form} needs its active duration, area, value, script, resistance or presentation owner.");
            if (!float.IsFinite(effect.Magnitude) || effect.Magnitude < 0)
                throw new InvalidDataException("Weapon touch effect magnitude is invalid.");
            if (effect.Area != 0 && ((source.EnchantmentFlags.GetValueOrDefault() & 2) == 0 || effect.Conditions.Count != 0))
                throw new NotSupportedException("Touch health area needs its stacking or conditional delivery owner.");
            if (effect.ActorValue == 16 && (effect.Flags & 4) == 0)
                throw new NotSupportedException("Weapon health restoration needs its separate delivery owner.");
        }
        return source with { Effects = effects.Select(effect => effect with { Conditions = [] }).ToArray() };
    }

    internal static float HealthDamage(FalloutIngestible selected)
    {
        var amount = selected.Effects.Where(effect => effect.ActorValue == 16)
            .Sum(effect => (effect.Flags & 0x100) == 0 ? effect.Magnitude : 0);
        return float.IsFinite(amount) && amount >= 0 ? amount :
            throw new InvalidDataException("Weapon effect health damage exceeds finite storage.");
    }
}
