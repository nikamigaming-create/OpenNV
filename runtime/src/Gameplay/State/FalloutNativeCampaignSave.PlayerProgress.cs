namespace OpenNV.Runtime.Gameplay.State;

internal static partial class FalloutNativeCampaignSave
{
    // The main current-schema validator calls this after capturing all three
    // actual owners. These fields are validation joins, not duplicate values.
    private static void ValidatePlayerProgress(FalloutPlayerProgressSnapshot progress,
        GameplayVitals vitals, FalloutPlayerActorValuesSnapshot actorValues)
    {
        ArgumentNullException.ThrowIfNull(progress);
        ArgumentNullException.ThrowIfNull(vitals);
        ArgumentNullException.ThrowIfNull(actorValues);
        FalloutPlayerProgress.Validate(progress);
        vitals.Validate(); FalloutPlayerActorValues.Validate(actorValues);
        if (progress.Level != vitals.Level || progress.Experience != vitals.ExperiencePoints ||
            progress.NextThreshold != vitals.NextLevelExperiencePoints || progress.Reference != actorValues.Reference ||
            progress.Player != actorValues.Player || progress.PlayerSha256 != actorValues.PlayerSha256 ||
            progress.StatsOwner != actorValues.StatsOwner || progress.StatsSha256 != actorValues.StatsSha256 ||
            progress.Skills.PlayerWinner != actorValues.PlayerWinner || progress.Skills.StatsWinner != actorValues.StatsWinner)
            throw new InvalidDataException("Campaign progress differs from the actual player vitals/SPECIAL/skill owners.");
    }
}
