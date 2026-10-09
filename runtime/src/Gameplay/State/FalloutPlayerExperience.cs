using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

// RewardXP awards the engine-created player's XP even when an object, quest
// or global function invokes it. Vitals remain the sole saved XP value.
internal sealed class FalloutPlayerExperience(FalloutPluginStack records, FalloutPlayerVitals vitals,
    Func<IReadOnlyList<FalloutPerkEntry>> perkEntries)
{
    private Func<FalloutCondition, float>? _perkConditions;
    internal event Action<int, int>? ExperienceChanged;

    internal void BindPerkConditions(Func<FalloutCondition, float> evaluate)
    {
        ArgumentNullException.ThrowIfNull(evaluate);
        if (_perkConditions is not null) throw new InvalidOperationException("XP perk conditions are already bound.");
        _perkConditions = evaluate;
    }

    internal int Reward(double operand)
    {
        if (!double.IsFinite(operand) || operand != Math.Truncate(operand) || operand < int.MinValue || operand > int.MaxValue)
            throw new InvalidDataException("RewardXP requires one signed integer.");
        var amount = (int)operand;
        var before = vitals.State;
        var cap = unchecked((int)FalloutGameSettingIntegers.Read(records, "iMaxCharacterLevel"));
        if (cap < 1) throw new InvalidDataException("Source maximum player level is not positive.");
        if (before.Level >= cap) return 0;

        var adjusted = FalloutPlayerPerkNumericEffects.Apply(9, amount, perkEntries(), condition =>
            (_perkConditions ?? throw new NotSupportedException("XP perk conditions have no live actor owner."))(condition));
        var rounded = (double)MathF.Ceiling(adjusted);
        if (rounded < int.MinValue || rounded > int.MaxValue)
            throw new NotSupportedException("RewardXP exceeds admitted signed XP storage.");
        var total = (long)before.ExperiencePoints + (int)rounded;
        if (total is < int.MinValue or > int.MaxValue)
            throw new NotSupportedException("RewardXP requires an unadmitted signed XP overflow policy.");
        var maximum = vitals.ExperienceThreshold(cap);
        var after = Math.Min(total, maximum);
        if (after < 0)
            throw new NotSupportedException("RewardXP below zero requires the signed player XP pool owner.");
        var published = checked((int)after);
        vitals.Publish(before with { ExperiencePoints = published });
        if (published != before.ExperiencePoints) ExperienceChanged?.Invoke(before.ExperiencePoints, published);
        return checked(published - before.ExperiencePoints);
    }
}
