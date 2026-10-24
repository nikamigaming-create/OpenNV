using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal static class FalloutHackingPerkEffects
{
    private const byte ScienceBonusEntryPoint = 30;

    // This changes puzzle generation Science, not the player's Science value
    // or the separate skill requirement for starting a terminal interaction.
    internal static float ApplyScienceBonus(float currentScience, IReadOnlyList<FalloutPerkEntry> entries,
        Func<FalloutCondition, float> livePlayerConditions)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(livePlayerConditions);
        return FalloutPlayerPerkNumericEffects.Apply(ScienceBonusEntryPoint, currentScience, entries,
            livePlayerConditions);
    }
}
