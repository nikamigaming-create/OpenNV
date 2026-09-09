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
    internal static GameplayVitals Derive(int baseHealth, int level, int endurance, int agility,
        double healthEndurance, double healthLevel, double apBase, double apAgility,
        int experience, double xpBase, double xpBump)
    {
        static int Exact(double value)
        {
            if (!double.IsFinite(value) || value != Math.Truncate(value) || value <= 0 || value > int.MaxValue)
                throw new InvalidDataException("Owned vitals derivation requires a positive integral result.");
            return (int)value;
        }
        var hp = Exact(baseHealth + endurance * healthEndurance + (level - 1) * healthLevel);
        var ap = Exact(apBase + agility * apAgility);
        var next = Exact(level * ((level - 1) * xpBump / 2 + xpBase));
        var state = new GameplayVitals(level, hp, hp, ap, ap, experience, next);
        state.Validate();
        return state;
    }

    internal void Validate()
    {
        if (Level <= 0 || MaximumHitPoints <= 0 || HitPoints < 0 ||
            HitPoints > MaximumHitPoints || MaximumActionPoints <= 0 ||
            ActionPoints < 0 || ActionPoints > MaximumActionPoints ||
            ExperiencePoints < 0 || NextLevelExperiencePoints <= ExperiencePoints)
            throw new InvalidOperationException("Saved gameplay vitals are invalid.");
    }
}
