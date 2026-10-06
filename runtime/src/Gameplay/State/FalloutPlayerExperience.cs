using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

// RewardXP awards the engine-created player's XP even when an object, quest
// or global function invokes it. Vitals remain the sole saved XP value.
internal sealed class FalloutPlayerExperience(FalloutPluginStack records, FalloutPlayerVitals vitals,
    Func<IReadOnlyList<FalloutPerkEntry>> perkEntries)
{
    internal int Reward(double operand)
    {
        if (!double.IsFinite(operand) || operand != Math.Truncate(operand) || operand < int.MinValue || operand > int.MaxValue)
            throw new InvalidDataException("RewardXP requires one signed integer.");
        var amount = (int)operand;
        var before = vitals.State;
        var cap = unchecked((int)FalloutGameSettingIntegers.Read(records, "iMaxCharacterLevel"));
        if (before.Level >= cap) return 0;

        // The engine applies AdjustExperiencePoints before its upward rounding.
        // The existing entry reader retains these entries but does not yet own
        // their acquired-rank/priority dispatch. Refuse before publishing XP.
        if (perkEntries().Any(entry => entry.Entry == 9))
            throw new NotSupportedException("RewardXP requires the active source XP perk dispatch owner.");
        var rounded = (double)MathF.Ceiling(amount);
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
        return checked(published - before.ExperiencePoints);
    }
}
