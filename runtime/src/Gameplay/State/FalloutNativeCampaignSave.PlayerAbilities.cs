using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal static partial class FalloutNativeCampaignSave
{
    private static void ValidateCurrentPlayerAbilityState(FalloutNativeCampaignState state)
    {
        var skills = state.PlayerSkillValues ?? throw new InvalidDataException("Current skill authority is absent.");
        var effects = state.PlayerAbilityScripts ?? throw new InvalidDataException("Current ability-script authority is absent.");
        FalloutPlayerSkills.ValidateValues(skills);
        FalloutPlayerAbilityScripts.Validate(effects);
        var progress = state.PlayerProgress ?? throw new InvalidDataException("Current skill advancement authority is absent.");
        var other = progress.Skills;
        if (skills.Schema != other.Schema || skills.Reference != other.Reference || skills.Player != other.Player ||
            skills.PlayerWinner != other.PlayerWinner || skills.PlayerSha256 != other.PlayerSha256 ||
            skills.StatsOwner != other.StatsOwner || skills.StatsWinner != other.StatsWinner || skills.StatsSha256 != other.StatsSha256 ||
            !skills.Sources.SequenceEqual(other.Sources) ||
            !skills.Pools.OrderBy(pair => pair.Key).SequenceEqual(other.Pools.OrderBy(pair => pair.Key)) ||
            effects.Reference != skills.Reference || effects.Player != skills.Player ||
            effects.PlayerWinner != skills.PlayerWinner || effects.PlayerSha256 != skills.PlayerSha256)
            throw new InvalidDataException("Current progress, skill and effect captures differ from their same live player owners.");
    }

    private static void ValidateCurrentPlayerAbilitySource(FalloutPluginStack records, FalloutNativeCampaignState state)
    {
        ValidateCurrentPlayerAbilityState(state);
        FalloutPlayerSkills.ValidateValueSource(records, state.PlayerSkillValues!);
        FalloutPlayerAbilityScripts.ValidateSource(records, state.PlayerAbilityScripts!);
    }
}
