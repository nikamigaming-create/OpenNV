using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

// Effect declarations distinguish an integrated value change from a reversible
// modifier. The latter is applied once and removed once, not multiplied by time.
internal static class FalloutIngestibleApplications
{
    internal static bool Reversible(FalloutIngestibleEffect effect) => (effect.Flags & 2) != 0;

    internal static void Validate(FalloutIngestibleEffect effect, Func<int, bool> temporaryPool)
    {
        if (effect.Archetype is not (0 or 34) || effect.Area != 0 || effect.Range != 0 ||
            (effect.Flags & 0x10) == 0 || (effect.Flags & ~0x1803f7u) != 0 ||
            effect.AdditionalPresentation || effect.Conditions.Any(condition => condition.Function is not (449 or 586)))
            throw new NotSupportedException($"Ingestible effect {effect.Form} requires its archetype, condition or presentation owner.");
        if (Reversible(effect))
        {
            if (effect.Archetype != 0 || effect.EffectiveDuration == 0 || !temporaryPool(effect.ActorValue))
                throw new NotSupportedException($"Ingestible effect {effect.Form} requires its reversible actor-value pool.");
        }
        else if (effect.ActorValue != 16)
            throw new NotSupportedException($"Ingestible effect {effect.Form} requires its actor-value damage/restore owner.");
    }

    internal static float Magnitude(FalloutPluginStack records, FalloutIngestible source,
        FalloutIngestibleEffect effect, Func<int, float> actorValue)
    {
        var result = (effect.Flags & 0x100) != 0 ? 0 : effect.Magnitude;
        if ((effect.Flags & 5) == 0)
        {
            if ((source.Flags & 2) != 0) result *= Scale("Survival", 44);
            if ((source.Flags & 4) != 0) result *= Scale("Medicine", 37);
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
