using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

// Effect declarations distinguish an integrated value change from a reversible
// modifier. The latter is applied once and removed once, not multiplied by time.
internal static class FalloutIngestibleApplications
{
    internal static bool Reversible(FalloutIngestibleEffect effect) => (effect.Flags & 2) != 0;

    internal static void Validate(FalloutIngestibleEffect effect, Func<int, bool> temporaryPool)
    {
        ValidateResistance(effect);
        if (effect.Archetype is not (0 or 34) || effect.Area != 0 || effect.Range != 0 ||
            (effect.Flags & 0x10) == 0 || (effect.Flags & ~0x11803f7u) != 0 ||
            effect.AdditionalPresentation || effect.Conditions.Any(condition => condition.Function is not (449 or 586)))
            throw new NotSupportedException($"Ingestible effect {effect.Form} requires its archetype, condition or presentation owner.");
        if (Reversible(effect))
        {
            if (effect.Archetype != 0 || effect.EffectiveDuration == 0 || !temporaryPool(effect.ActorValue))
                throw new NotSupportedException($"Ingestible effect {effect.Form} requires its reversible actor-value pool.");
        }
        else if (effect.ActorValue is not (16 or 54))
            throw new NotSupportedException($"Ingestible effect {effect.Form} requires its actor-value damage/restore owner.");
    }

    internal static void ValidateResistance(FalloutIngestibleEffect effect)
    {
        if (effect.Resistance != -1 && (effect.ActorValue != 54 || effect.Resistance != 20))
            throw new NotSupportedException($"Ingestible {effect.Form} has an unbound resistance actor value.");
        // Hostile health effects need pain/hit delivery in addition to the value
        // transaction. The Painless declaration is an actual presentation rule.
        if ((effect.Flags & 1) != 0 && (effect.Flags & 0x1000000) == 0)
            throw new NotSupportedException("Hostile ingestion requires its hit/pain presentation owner.");
    }

    internal static float Magnitude(FalloutPluginStack records, FalloutIngestible source,
        FalloutIngestibleEffect effect, Func<int, float> actorValue, bool player = true)
    {
        ValidateResistance(effect);
        var result = (effect.Flags & 0x100) != 0 ? 0 : effect.Magnitude;
        if ((effect.Flags & 5) == 0)
        {
            if ((source.Flags & 2) != 0) result *= Scale("Survival", 44);
            if ((source.Flags & 4) != 0) result *= Scale("Medicine", 37);
        }
        if (effect.Resistance == 20)
        {
            var resistance = actorValue(20);
            var cap = player ? FalloutGameSettingFloats.Read(records, "fPlayerMaxResistance") : 100;
            if (!float.IsFinite(resistance) || !float.IsFinite(cap) || cap is < 0 or > 100)
                throw new InvalidDataException("Ingestible radiation resistance is invalid.");
            result *= 1 - Math.Clamp(resistance, 0, cap) / 100;
        }
        result *= (effect.Flags & 4) == 0 ? 1 : -1;
        return float.IsFinite(result) ? result : throw new InvalidDataException("Ingestible magnitude is not finite.");

        float Scale(string name, int value)
        {
            var level = actorValue(value);
            if (!float.IsFinite(level) || level < 0) throw new InvalidDataException("Ingestible skill source is invalid.");
            return FalloutGameSettingFloats.Read(records, "fMagic" + name + "SkillBase") +
                FalloutGameSettingFloats.Read(records, "fMagic" + name + "SkillMult") * level / 100;
        }
    }
}
