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

internal static class ClassicAttackOwner
{
    internal const string EngineRollRequired = "engine-roll-required";
    internal const string EngineResolved = "engine-resolved";

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
