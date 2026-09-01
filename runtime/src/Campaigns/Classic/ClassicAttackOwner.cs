namespace OpenNV.Runtime.Campaigns.Classic;

internal enum ClassicAttackBoundary
{
    Ready,
    OutOfRange,
    InsufficientActionPoints,
    ActionPointCostRequired,
    RangeRequired,
    HitRollRequired,
}

internal sealed record ClassicAttackSource(
    string AttackPid,
    int MinimumDamage,
    int MaximumDamage,
    int? DamageType,
    int? MaximumRangeHexes,
    int? ActionPointCost,
    int? AnimationCode,
    string HitResolution)
{
    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(AttackPid) || MinimumDamage < 0 ||
            MaximumDamage < MinimumDamage || DamageType is < 0 || MaximumRangeHexes is <= 0 ||
            ActionPointCost is <= 0 || AnimationCode is < 0 ||
            string.IsNullOrWhiteSpace(HitResolution))
            throw new InvalidOperationException("Classic attack source contract is invalid.");
    }
}

internal sealed record ClassicAttackIntent(
    string ActorId,
    string TargetId,
    int DistanceHexes,
    int ActorActionPoints,
    ClassicAttackSource Source,
    ClassicAttackBoundary Boundary);

internal sealed record ClassicResolvedDamage(
    int TargetHitPoints,
    int DamageApplied,
    bool TargetDefeated);

internal sealed record ClassicAttackResolutionContract(
    string ExactBuild,
    int MinimumPercentRoll,
    int MaximumPercentRoll,
    int MinimumHitChance,
    int MaximumHitChance,
    int PercentScale,
    int MarginPercentPerUpgradePoint)
{
    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(ExactBuild) || MinimumPercentRoll <= 0 ||
            MaximumPercentRoll < MinimumPercentRoll ||
            MinimumHitChance < MinimumPercentRoll ||
            MaximumHitChance > MaximumPercentRoll ||
            MinimumHitChance > MaximumHitChance || PercentScale <= 0 ||
            MarginPercentPerUpgradePoint <= 0)
            throw new InvalidOperationException(
                "Classic attack-resolution contract is invalid.");
    }
}

internal sealed record ClassicAttackRolls(
    int HitPercent,
    int Damage,
    int? CriticalUpgradePercent,
    int? FumbleUpgradePercent);

internal sealed record ClassicAttackDefense(
    int ArmorClass,
    int DamageThreshold,
    int DamageResistancePercent,
    int HitPoints)
{
    internal void Validate(ClassicAttackResolutionContract contract)
    {
        if (ArmorClass < 0 || DamageThreshold < 0 ||
            DamageResistancePercent is < 0 ||
            DamageResistancePercent > contract.PercentScale || HitPoints <= 0)
            throw new InvalidOperationException("Classic attack defense is invalid.");
    }
}

internal sealed record ClassicAttackOffense(
    int AttackSkill,
    int CriticalChance,
    int DamageBonus,
    int Strength,
    int MinimumStrength,
    int Ammunition,
    int AmmunitionPerAttack)
{
    internal void Validate()
    {
        if (AttackSkill < 0 || CriticalChance < 0 || DamageBonus < 0 ||
            Strength <= 0 || MinimumStrength <= 0 || Strength < MinimumStrength ||
            Ammunition < 0 || AmmunitionPerAttack < 0 ||
            AmmunitionPerAttack > Ammunition)
            throw new InvalidOperationException("Classic attack offense is invalid.");
    }
}

internal enum ClassicAttackOutcome
{
    Miss,
    Hit,
    CriticalResolutionRequired,
    FumbleResolutionRequired,
}

internal sealed record ClassicAttackResolution(
    ClassicAttackOutcome Outcome,
    int HitChance,
    int ActorActionPoints,
    int Ammunition,
    int TargetHitPoints,
    int DamageApplied,
    bool TargetDefeated);

internal static class ClassicAttackOwner
{
    internal const string EngineRollRequired = "engine-roll-required";
    internal const string EngineResolved = "engine-resolved";

    internal static ClassicAttackResolution ResolveObservedRolls(
        ClassicAttackIntent intent,
        ClassicAttackOffense offense,
        ClassicAttackDefense defense,
        ClassicAttackRolls rolls,
        ClassicAttackResolutionContract contract)
    {
        contract.Validate();
        offense.Validate();
        defense.Validate(contract);
        if (intent.Boundary != ClassicAttackBoundary.HitRollRequired ||
            intent.Source.HitResolution != EngineRollRequired ||
            rolls.HitPercent < contract.MinimumPercentRoll ||
            rolls.HitPercent > contract.MaximumPercentRoll ||
            rolls.Damage < intent.Source.MinimumDamage ||
            rolls.Damage > intent.Source.MaximumDamage)
            throw new InvalidOperationException(
                "Classic attack observed-roll state is invalid.");

        var actionPoints = intent.ActorActionPoints - intent.Source.ActionPointCost!.Value;
        var remainingAmmunition = offense.Ammunition - offense.AmmunitionPerAttack;
        var hitChance = Math.Clamp(
            offense.AttackSkill - defense.ArmorClass,
            contract.MinimumHitChance,
            contract.MaximumHitChance);
        if (rolls.HitPercent > hitChance)
        {
            var fumbleChance = (rolls.HitPercent - hitChance) /
                contract.MarginPercentPerUpgradePoint;
            if (fumbleChance > 0 && rolls.FumbleUpgradePercent is null)
                return new ClassicAttackResolution(
                    ClassicAttackOutcome.FumbleResolutionRequired,
                    hitChance,
                    actionPoints,
                    remainingAmmunition,
                    defense.HitPoints,
                    0,
                    false);
            if (rolls.FumbleUpgradePercent is { } fumbleRoll &&
                fumbleRoll <= fumbleChance)
                return new ClassicAttackResolution(
                    ClassicAttackOutcome.FumbleResolutionRequired,
                    hitChance,
                    actionPoints,
                    remainingAmmunition,
                    defense.HitPoints,
                    0,
                    false);
            return new ClassicAttackResolution(
                ClassicAttackOutcome.Miss,
                hitChance,
                actionPoints,
                remainingAmmunition,
                defense.HitPoints,
                0,
                false);
        }

        var criticalUpgradeChance = offense.CriticalChance +
            (hitChance - rolls.HitPercent) /
            contract.MarginPercentPerUpgradePoint;
        if (criticalUpgradeChance > 0 && rolls.CriticalUpgradePercent is null)
            return new ClassicAttackResolution(
                ClassicAttackOutcome.CriticalResolutionRequired,
                hitChance,
                actionPoints,
                remainingAmmunition,
                defense.HitPoints,
                0,
                false);
        if (rolls.CriticalUpgradePercent is { } criticalRoll &&
            criticalRoll <= criticalUpgradeChance)
            return new ClassicAttackResolution(
                ClassicAttackOutcome.CriticalResolutionRequired,
                hitChance,
                actionPoints,
                remainingAmmunition,
                defense.HitPoints,
                0,
                false);

        var afterThreshold = Math.Max(
            0,
            rolls.Damage + offense.DamageBonus - defense.DamageThreshold);
        var resisted = afterThreshold * defense.DamageResistancePercent /
            contract.PercentScale;
        var applied = Math.Min(defense.HitPoints, afterThreshold - resisted);
        var hitPoints = defense.HitPoints - applied;
        return new ClassicAttackResolution(
            ClassicAttackOutcome.Hit,
            hitChance,
            actionPoints,
            remainingAmmunition,
            hitPoints,
            applied,
            hitPoints == 0);
    }

    internal static ClassicAttackIntent Prepare(
        string actorId,
        string targetId,
        int distanceHexes,
        int actorActionPoints,
        ClassicAttackSource source)
    {
        source.Validate();
        if (string.IsNullOrWhiteSpace(actorId) || string.IsNullOrWhiteSpace(targetId) ||
            actorId == targetId || distanceHexes <= 0 || actorActionPoints < 0)
            throw new InvalidOperationException("Classic attack actor state is invalid.");
        ClassicAttackBoundary boundary;
        if (source.ActionPointCost is null)
            boundary = ClassicAttackBoundary.ActionPointCostRequired;
        else if (source.MaximumRangeHexes is null)
            boundary = ClassicAttackBoundary.RangeRequired;
        else if (distanceHexes > source.MaximumRangeHexes.Value)
            boundary = ClassicAttackBoundary.OutOfRange;
        else if (actorActionPoints < source.ActionPointCost.Value)
            boundary = ClassicAttackBoundary.InsufficientActionPoints;
        else if (source.HitResolution == EngineRollRequired)
            boundary = ClassicAttackBoundary.HitRollRequired;
        else if (source.HitResolution == EngineResolved)
            boundary = ClassicAttackBoundary.Ready;
        else
            throw new InvalidOperationException(
                "Classic attack hit-resolution contract is unsupported.");
        return new ClassicAttackIntent(
            actorId,
            targetId,
            distanceHexes,
            actorActionPoints,
            source,
            boundary);
    }

    internal static ClassicResolvedDamage ApplyEngineResolvedDamage(
        ClassicAttackIntent intent,
        int engineResolvedDamage,
        int targetHitPoints)
    {
        if (intent.Boundary != ClassicAttackBoundary.Ready || targetHitPoints <= 0 ||
            intent.Source.HitResolution != EngineResolved || engineResolvedDamage < 0)
            throw new InvalidOperationException(
                "Classic attack cannot apply unresolved engine damage.");
        var applied = Math.Min(targetHitPoints, engineResolvedDamage);
        var remaining = targetHitPoints - applied;
        return new ClassicResolvedDamage(remaining, applied, remaining == 0);
    }
}
