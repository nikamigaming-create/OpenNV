using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Campaigns.Classic;

/// <summary>FO1 level-one skill inputs. Earned perks and later actor effects need their own live owners.</summary>
internal static class ClassicFo1StartingSkills
{
    private static ClassicSkillRule Rule(int id, int basis, int multiplier, params int[] stats) =>
        new(ClassicCharacterProfile.SkillNames[id], basis, multiplier, stats, 1, id >= 6)
        { SpecialDivisor = stats.Length == 2 ? 2 : 1 };

    internal static readonly ClassicSkillContract Contract = new(ClassicSkillContract.ExpectedSchema,
        "fallout1-retail-observed/v1", 7, 1, 10, 20, 200, 20, 0, -10,
        [
            Rule(0, 35, 1, 5), Rule(1, 10, 1, 5), Rule(2, 10, 1, 5),
            Rule(3, 65, 1, 5, 0), Rule(4, 55, 1, 5, 0), Rule(5, 40, 1, 5),
            Rule(6, 30, 1, 1, 4), Rule(7, 15, 1, 1, 4), Rule(8, 25, 1, 5),
            Rule(9, 20, 1, 1, 5), Rule(10, 20, 1, 5), Rule(11, 20, 1, 1, 5),
            Rule(12, 25, 2, 4), Rule(13, 20, 1, 4), Rule(14, 25, 2, 3),
            Rule(15, 20, 2, 3), Rule(16, 20, 3, 6), Rule(17, 5, 1, 2, 4),
        ]);

    internal static int[] Special(ClassicCharacterProfile profile, int gameTimeHour)
    {
        if (gameTimeHour is < 0 or > 2359 || gameTimeHour % 100 >= 60)
            throw new ArgumentOutOfRangeException(nameof(gameTimeHour));
        return ClassicStartingStats.SpecialAt(profile, gameTimeHour is >= 600 and < 1800 ? -1 : 1);
    }

    internal static int[] Resolve(ClassicCharacterProfile profile, int gameTimeHour, ClassicSkillDifficulty difficulty)
    {
        var special = Special(profile, gameTimeHour);
        return Enumerable.Range(0, 18).Select(skill =>
        {
            var adjustment = (profile.Traits.Contains(15) ? -10 : 0) + (profile.Traits.Contains(14) ? 10 : 0);
            if (profile.Traits.Contains(10)) adjustment += skill < 6 ? -10 : skill is 6 or 7 or 14 or 15 ? 15 : 0;
            return ClassicSkillOwner.Resolve(Contract, ClassicCharacterProfile.SkillNames[skill],
                new(special, profile.SkillBonuses[skill], profile.TaggedSkills.Contains(skill), adjustment, 0, difficulty));
        }).ToArray();
    }
}
