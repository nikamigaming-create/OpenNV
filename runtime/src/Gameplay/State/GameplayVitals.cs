namespace OpenNV.Runtime.Gameplay.State;

internal sealed record GameplayVitals(
    int Level,
    int HitPoints,
    int MaximumHitPoints,
    int ActionPoints,
    int MaximumActionPoints,
    int ExperiencePoints,
    int NextLevelExperiencePoints, float HitPointFraction = 0,
    IReadOnlyDictionary<byte, float>? LimbDamage = null, float RadiationRads = 0)
{
    internal float ExactHitPoints => HitPoints - HitPointFraction;
    internal GameplayVitals Damage(float amount)
    {
        if (!float.IsFinite(amount) || amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
        var remaining = Math.Max(0, ExactHitPoints - amount);
        var displayed = (int)MathF.Ceiling(remaining);
        return this with { HitPoints = displayed, HitPointFraction = displayed - remaining };
    }
    internal GameplayVitals Damage(float amount, byte part, float limbMultiplier)
    {
        if (part > 14 || !float.IsFinite(limbMultiplier) || limbMultiplier < 0)
            throw new ArgumentOutOfRangeException(nameof(part));
        var state = Damage(amount);
        var limbs = new Dictionary<byte, float>(LimbDamage ?? new Dictionary<byte, float>());
        limbs[part] = limbs.GetValueOrDefault(part) + amount * limbMultiplier;
        if (!float.IsFinite(limbs[part])) throw new InvalidDataException("Player limb damage exceeds finite storage.");
        return state with { LimbDamage = limbs };
    }
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
            ExperiencePoints < 0 || NextLevelExperiencePoints <= ExperiencePoints ||
            !float.IsFinite(HitPointFraction) || HitPointFraction is < 0 or >= 1 || ExactHitPoints < 0 ||
            !float.IsFinite(RadiationRads) || RadiationRads < 0 ||
            LimbDamage is { } limbs && limbs.Any(pair => pair.Key > 14 || !float.IsFinite(pair.Value) || pair.Value < 0))
            throw new InvalidOperationException("Saved gameplay vitals are invalid.");
    }
}
