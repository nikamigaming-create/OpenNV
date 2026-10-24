using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal static class FalloutLevelUpSkillBudget
{
    internal const string SourceContractSha256 = "1e4b3e05142864c106a1cda753e4ada486598e4062734898fe06b443d275a07a";

    internal static int ReadBudget(int permanentIntelligence, int gainedLevel,
        IReadOnlyList<FalloutPerkEntry> entries, Func<FalloutCondition, float> livePlayerConditions)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(livePlayerConditions);
        if (gainedLevel is < 2 or > ushort.MaxValue)
            throw new InvalidDataException("Level-up skill budget requires the gained source level word.");

        // The caller supplies the source integer Intelligence query. Keep its
        // upper cap separate from any actor-value normalization or lower bound.
        var intelligence = Math.Min(permanentIntelligence, 10);
        var scaled = (float)(10d + intelligence * .5d);
        var initial = (int)MathF.Floor(scaled);
        if (gainedLevel % 2 == 0 && intelligence % 2 == 1) initial++;

        // Numeric perk effects consume the integer award narrowed to Float32.
        // The menu then truncates; it does not round or ceil a fractional perk.
        var adjusted = FalloutPlayerPerkNumericEffects.Apply(10, initial, entries, livePlayerConditions);
        var budget = Math.Truncate((double)adjusted);
        if (budget < 0 || budget > int.MaxValue)
            throw new NotSupportedException("Level-up skill budget requires an unadmitted signed allocation or integer overflow policy.");
        return checked((int)budget);
    }
}
