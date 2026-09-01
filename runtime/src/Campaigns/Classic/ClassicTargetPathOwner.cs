using OpenNV.Runtime.Campaigns.Fallout1;

namespace OpenNV.Runtime.Campaigns.Classic;

internal enum ClassicTargetPathBoundary
{
    Ready,
    NoPath,
    DoorStateRequired,
    MultihexCoverageRequired,
    MoveAnimationRequired,
    StepActionPointCostRequired,
    InsufficientActionPoints,
    AttackRangeReached,
}

internal sealed record ClassicTargetPathContract(
    string MapSha256,
    bool DoorStateComplete,
    bool MultihexCoverageComplete,
    int? StepActionPointCost,
    string? MoveAnimation);

internal sealed record ClassicTargetPathState(
    int CurrentTile,
    int TargetTile,
    int ActionPoints,
    int Rotation,
    int CompletedSteps,
    IReadOnlyList<int> Path,
    ClassicTargetPathContract Contract,
    ClassicTargetPathBoundary Boundary);

internal static class ClassicTargetPathOwner
{
    private const int Sha256HexCharacters = 64;

    internal static ClassicTargetPathState Plan(
        int actorTile,
        int targetTile,
        int actionPoints,
        IReadOnlySet<int> sourceWalkableTiles,
        ClassicTargetPathContract contract)
    {
        if (actorTile < 0 || targetTile < 0 || actorTile == targetTile || actionPoints < 0 ||
            contract.MapSha256.Length != Sha256HexCharacters ||
            !contract.MapSha256.All(Uri.IsHexDigit) ||
            contract.StepActionPointCost is <= 0 ||
            contract.MoveAnimation is { } animation && string.IsNullOrWhiteSpace(animation))
            throw new InvalidOperationException("Classic target-path source state is invalid.");
        var goals = Fo1HexMath.Neighbors(targetTile)
            .Where(sourceWalkableTiles.Contains)
            .Order()
            .ToHashSet();
        var path = ShortestPath(actorTile, goals, sourceWalkableTiles);
        var boundary = !contract.DoorStateComplete
            ? ClassicTargetPathBoundary.DoorStateRequired
            : !contract.MultihexCoverageComplete
                ? ClassicTargetPathBoundary.MultihexCoverageRequired
                : path.Count == 0
                    ? ClassicTargetPathBoundary.NoPath
                    : contract.MoveAnimation is null
                        ? ClassicTargetPathBoundary.MoveAnimationRequired
                    : contract.StepActionPointCost is null
                        ? ClassicTargetPathBoundary.StepActionPointCostRequired
                        : path.Count == 1
                            ? ClassicTargetPathBoundary.AttackRangeReached
                            : actionPoints < contract.StepActionPointCost.Value
                                ? ClassicTargetPathBoundary.InsufficientActionPoints
                                : ClassicTargetPathBoundary.Ready;
        return new ClassicTargetPathState(
            actorTile,
            targetTile,
            actionPoints,
            0,
            0,
            path,
            contract,
            boundary);
    }

    internal static ClassicTargetPathState Step(ClassicTargetPathState state)
    {
        if (state.Boundary != ClassicTargetPathBoundary.Ready ||
            state.Contract.StepActionPointCost is not { } stepCost ||
            state.Path.Count < 2 || state.Path[0] != state.CurrentTile)
            throw new InvalidOperationException("Classic target path is not ready to step.");
        var destination = state.Path[1];
        var rotation = Enumerable.Range(0, Fo1HexMath.DirectionCount)
            .Single(value => Fo1HexMath.TileInDirection(state.CurrentTile, value) == destination);
        var remaining = state.Path.Skip(1).ToArray();
        var actionPoints = state.ActionPoints - stepCost;
        var boundary = remaining.Length == 1
            ? ClassicTargetPathBoundary.AttackRangeReached
            : actionPoints < stepCost
                ? ClassicTargetPathBoundary.InsufficientActionPoints
                : ClassicTargetPathBoundary.Ready;
        return state with
        {
            CurrentTile = destination,
            ActionPoints = actionPoints,
            Rotation = rotation,
            CompletedSteps = state.CompletedSteps + 1,
            Path = remaining,
            Boundary = boundary,
        };
    }

    internal static ClassicAttackIntent PrepareAttack(
        ClassicTargetPathState state,
        string actorId,
        string targetId,
        ClassicAttackSource attack)
    {
        if (state.Boundary != ClassicTargetPathBoundary.AttackRangeReached)
            throw new InvalidOperationException(
                "Classic target path has not reached attack range.");
        return ClassicAttackOwner.Prepare(
            actorId,
            targetId,
            Fo1HexMath.Distance(state.CurrentTile, state.TargetTile),
            state.ActionPoints,
            attack);
    }

    private static IReadOnlyList<int> ShortestPath(
        int start,
        IReadOnlySet<int> goals,
        IReadOnlySet<int> sourceWalkableTiles)
    {
        if (goals.Contains(start))
            return [start];
        var parents = new Dictionary<int, int> { [start] = -1 };
        var queue = new Queue<int>();
        queue.Enqueue(start);
        var goal = -1;
        while (queue.Count > 0 && goal < 0)
        {
            var tile = queue.Dequeue();
            foreach (var neighbor in Fo1HexMath.Neighbors(tile))
            {
                if (!sourceWalkableTiles.Contains(neighbor) || !parents.TryAdd(neighbor, tile))
                    continue;
                if (goals.Contains(neighbor))
                {
                    goal = neighbor;
                    break;
                }
                queue.Enqueue(neighbor);
            }
        }
        if (goal < 0)
            return [];
        var reversed = new List<int>();
        for (var tile = goal; tile >= 0; tile = parents[tile])
            reversed.Add(tile);
        reversed.Reverse();
        return reversed;
    }
}
