using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Actors;

namespace OpenNV.Runtime.Gameplay.State;

// Admission only: never apply a numeric effect, start a script, draw random
// values, publish a rank or change an actor-value pool. Optional callbacks must
// prove capability without executing gameplay conditions or effect programs.
internal static class FalloutPlayerPerkCapabilities
{
    internal static void RequireFirstRank(FalloutLevelUpPerkSource source, int currentRank,
        IReadOnlyList<FalloutPerkEntry> candidateEntries, IReadOnlyList<FalloutPerkEntry> acquiredEntries,
        IReadOnlyList<FalloutAbilityModifier> modifiers, IReadOnlyList<FalloutAbilityScript> scripts,
        IReadOnlyList<int> skillActorValues,
        Action<FalloutCondition>? requireNumericCondition = null,
        Action<FalloutCondition>? requireAbilityCondition = null,
        Action<FalloutAbilityScript>? requireAbilityScript = null,
        Action<byte, IReadOnlyList<FalloutPerkEntry>>? requireSingleActiveOrder = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(candidateEntries);
        ArgumentNullException.ThrowIfNull(acquiredEntries);
        ArgumentNullException.ThrowIfNull(modifiers);
        ArgumentNullException.ThrowIfNull(scripts);
        ArgumentNullException.ThrowIfNull(skillActorValues);
        if (currentRank != 0 || source.MaximumRank < 1)
            throw new NotSupportedException("Perk capability admission requires an unowned first source rank.");

        // Check the complete first-rank declaration so a caller cannot silently
        // omit quest effects, numeric entries or an entire spell projection.
        var effects = source.Effects.Where(effect => effect.Rank == 0).ToArray();
        if (effects.Any(effect => effect.Type is not (1 or 2)))
            throw new NotSupportedException("Perk quest/unknown effects require their immediate acquisition owner.");
        var numeric = effects.Where(effect => effect.Type == 2).ToArray();
        if (candidateEntries.Count != numeric.Length ||
            candidateEntries.Select(entry => entry.SourceIndex).Distinct().Count() != candidateEntries.Count ||
            numeric.Any(effect => !candidateEntries.Any(entry => entry.SourceIndex == effect.SourceIndex &&
                entry.Entry == effect.EntryPoint)))
            throw new InvalidDataException("Perk numeric projection differs from its complete first-rank source.");
        var spells = effects.Where(effect => effect.Type == 1).Select(effect => effect.Form ??
            throw new InvalidDataException("Perk spell effect has no source form.")).ToHashSet();
        var projectedSpells = modifiers.Select(effect => effect.Spell).Concat(scripts.Select(effect => effect.Spell)).ToHashSet();
        if (!spells.SetEquals(projectedSpells))
            throw new InvalidDataException("Perk ability projection omitted or added a source spell.");

        foreach (var entry in candidateEntries) RequireNumeric(entry, requireNumericCondition);
        foreach (var point in candidateEntries.Select(entry => entry.Entry).Distinct().Where(SingleActiveOwner))
        {
            var combined = acquiredEntries.Where(entry => entry.Entry == point)
                .Concat(candidateEntries.Where(entry => entry.Entry == point)).ToArray();
            foreach (var entry in combined) RequireNumeric(entry, requireNumericCondition);
            if (combined.Length > 1)
                (requireSingleActiveOrder ?? throw new NotSupportedException(
                    $"Perk entry {point} requires proof of the shared owner's single-active-entry restriction."))(point, combined);
        }
        foreach (var effect in modifiers)
        {
            if (!float.IsFinite(effect.Amount) || effect.Effect.ObjectId == 0)
                throw new InvalidDataException("Perk ability modifier has invalid source identity or magnitude.");
            var owner = ModifierOwner(effect.ActorValue, effect.Pool, skillActorValues);
            if (owner is null)
                throw new NotSupportedException($"Perk ability value {effect.ActorValue}/{effect.Pool} has no current player effect owner.");
            // Defense currently rejects conditional acquired resistance.
            if (effect.ActorValue == 76 && effect.Conditions.Count != 0)
                throw new NotSupportedException("Conditional perk damage threshold requires its defense condition owner.");
            RequireConditions(effect.Conditions, requireAbilityCondition, false);
        }
        foreach (var script in scripts)
        {
            if (script.Effect.ObjectId == 0 || script.Script.ObjectId == 0 || script.EffectOrdinal < 0)
                throw new InvalidDataException("Perk script effect has invalid source identity.");
            RequireConditions(script.Conditions, requireAbilityCondition, false);
            (requireAbilityScript ?? throw new NotSupportedException(
                "Perk script acquisition requires a nonexecuting lifecycle/program capability owner."))(script);
        }
    }

    private static void RequireNumeric(FalloutPerkEntry entry, Action<FalloutCondition>? requireCondition)
    {
        if (NumericOwner(entry.Entry) is null)
            throw new NotSupportedException($"Perk entry {entry.Entry} has no current numeric dispatch owner.");
        entry.RequireActorConditionScope();
        if (entry.Function is not (1 or 2 or 3))
            throw new NotSupportedException($"Perk entry {entry.Entry} function {entry.Function} has no numeric operation owner.");
        if (!float.IsFinite(entry.Value)) throw new InvalidDataException("Perk numeric operand is non-finite.");
        // Damage/spread call AllPass without run-on adaptation. Shared scalar
        // dispatch explicitly delegates run-on handling to its live evaluator.
        RequireConditions(entry.Conditions, requireCondition, SingleActiveOwner(entry.Entry));
    }

    private static void RequireConditions(IReadOnlyList<FalloutCondition> conditions,
        Action<FalloutCondition>? requireCondition, bool allowRunOn)
    {
        foreach (var condition in conditions)
        {
            if ((condition.Flags & 0x1e) != 0 || condition.Flags >> 5 > 5 ||
                !float.IsFinite(condition.Comparison) || !allowRunOn && condition.RunOn != 0)
                throw new NotSupportedException("Perk condition requires its flags, comparison or run-on owner.");
            (requireCondition ?? throw new NotSupportedException(
                "Conditional perk effects require a nonexecuting condition capability owner."))(condition);
        }
    }

    // These are present call sites, not a format-wide claim that all entry
    // points or every source ordering/context is implemented.
    internal static string? NumericOwner(byte entryPoint) => entryPoint switch
    {
        0 => "FalloutWeaponDamageResolver.Resolve",
        9 => "FalloutPlayerExperience.Reward",
        10 => "FalloutLevelUpSkillBudget.ReadBudget",
        20 => "RuntimeNativeOpeningStageDriver.BeginTerminalHacking",
        22 => "FalloutPlayerMovementSpeed.Overencumbered",
        30 => "FalloutHackingPerkEffects.ApplyScienceBonus",
        32 => "RuntimeNativeOpeningStageDriver.IgnoreBrokenLock",
        34 => "FalloutWeaponSpread.PlayerMedianDeviationDegrees",
        42 => "FalloutPlayerMovementSpeed.Calculate",
        _ => null
    };

    private static bool SingleActiveOwner(byte entryPoint) => entryPoint is 9 or 10 or 20 or 22 or 30 or 32 or 42;

    internal static string? ModifierOwner(int actorValue, FalloutActorValuePool pool, IReadOnlyList<int> skillActorValues)
    {
        if (pool is not (FalloutActorValuePool.Permanent or FalloutActorValuePool.Temporary or FalloutActorValuePool.Damage)) return null;
        if (FalloutPlayerActorValues.HasPool(actorValue)) return "FalloutPlayerActorValues.Pool";
        if (skillActorValues.Contains(actorValue))
            return pool == FalloutActorValuePool.Damage ? null : "FalloutPlayerSkills.ReadSkill";
        if (actorValue == 13) return "FalloutPlayerSkills.Value:CarryWeight";
        if (pool == FalloutActorValuePool.Damage) return null;
        return actorValue switch
        {
            20 => "FalloutPlayerSkills.Value:RadResist",
            76 => "FalloutReferenceWorld.Defense:unconditional-threshold",
            _ => null
        };
    }
}
