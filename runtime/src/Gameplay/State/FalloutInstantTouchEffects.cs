using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Actors;

namespace OpenNV.Runtime.Gameplay.State;

// A delivered ENCH transaction. The caller supplies actual target/caster CTDA
// context and commits the entire prepared pool set once. No inventory item,
// synthetic spell, elapsed-time effect, visual or sound is substituted.
internal static class FalloutInstantTouchEffects
{
    internal static void RequireSourceOwner(FalloutIngestible source, bool areaDelivered = false, bool healthDelivered = false)
    {
        if (source.Effects.Count == 0) throw new InvalidDataException("Touch enchantment has no effects.");
        foreach (var effect in source.Effects)
        {
            if ((effect.Flags & 0x2000) != 0) continue; // Source DisplayNameOnly has no delivered gameplay effect.
            const uint supportedFlags = 0x1f5;
            if (effect.Archetype != 0 || effect.Range != 1 || (effect.Flags & 0x20) == 0 ||
                (effect.Flags & ~supportedFlags) != 0 || effect.EffectiveDuration != 0 ||
                effect.AdditionalPresentation || effect.Script is not null || effect.Resistance != -1)
                throw new NotSupportedException($"Touch effect {effect.Form} requires its duration, area, archetype, resistance or presentation owner.");
            if (effect.Area != 0 && (!areaDelivered || (source.EnchantmentFlags.GetValueOrDefault() & 2) == 0 || effect.Conditions.Count != 0))
                throw new NotSupportedException($"Touch effect {effect.Form} requires its area, stacking or conditional duplicate-delivery owner.");
            if (effect.ActorValue is not (>= 5 and <= 11 or 22) && !(healthDelivered && effect.ActorValue == 16 && (effect.Flags & 4) != 0))
                throw new NotSupportedException($"Touch effect {effect.Form} requires its actor-value {effect.ActorValue} delivery owner.");
            if (!float.IsFinite(effect.Magnitude) || effect.Magnitude < 0)
                throw new InvalidDataException("Touch effect magnitude is invalid.");
        }
    }

    internal static IReadOnlyDictionary<int, FalloutActorValue> Prepare(FalloutPluginStack records,
        FalloutIngestible source, Func<int, FalloutActorValue> read, Func<FalloutCondition, float> condition,
        Func<int, float>? readCurrent = null, bool areaDelivered = false)
    {
        // Validate every declaration, including conditionally inactive effects,
        // before selecting or changing any value. A supported first effect must
        // never conceal a later script/DOT/EFSH/SOUN declaration.
        RequireSourceOwner(source, areaDelivered);
        var selected = source.Effects.Where(effect => (effect.Flags & 0x2000) == 0 && FalloutCondition.AllPass(effect.Conditions, condition)).ToArray();
        var after = new Dictionary<int, FalloutActorValue>();
        var currentOffsets = new Dictionary<int, float>();
        foreach (var effect in selected)
        {
            var before = after.TryGetValue(effect.ActorValue, out var staged) ? staged : read(effect.ActorValue);
            if (!currentOffsets.ContainsKey(effect.ActorValue))
                currentOffsets.Add(effect.ActorValue, (readCurrent?.Invoke(effect.ActorValue) ?? before.Current) - before.Current);
            var magnitude = (effect.Flags & 0x100) != 0 ? 0 : effect.Magnitude;
            var detrimental = (effect.Flags & 4) != 0;
            after[effect.ActorValue] = detrimental && effect.ActorValue == 22
                ? FalloutFatigue.Damage(records, before, magnitude, before.Current + currentOffsets[effect.ActorValue])
                : FalloutActorValueCommands.Apply(before, detrimental ? FalloutActorValueOperation.Damage : FalloutActorValueOperation.Restore,
                    magnitude, before.Current);
        }
        return after;
    }
}
