namespace OpenNV.Runtime.Gameplay.State;

internal sealed record GameplayVitals(
    int Level,
    int HitPoints,
    int MaximumHitPoints,
    int ActionPoints,
    int MaximumActionPoints,
    int ExperiencePoints,
    int NextLevelExperiencePoints)
{
    internal void Validate()
    {
        if (Level <= 0 || MaximumHitPoints <= 0 || HitPoints < 0 ||
            HitPoints > MaximumHitPoints || MaximumActionPoints <= 0 ||
            ActionPoints < 0 || ActionPoints > MaximumActionPoints ||
            ExperiencePoints < 0 || NextLevelExperiencePoints <= ExperiencePoints)
            throw new InvalidOperationException("Saved gameplay vitals are invalid.");
    }
}
