namespace OpenNV.Runtime.Campaigns.Classic;

internal enum ClassicTargetTurnAction
{
    None,
    AdjacentAttackRequired,
    MovementRequired,
}

internal sealed record ClassicTargetTurnResult(
    int ActionPoints,
    int AiPacket,
    int Team,
    ClassicTargetTurnAction Action);

internal static class ClassicCombatTurnOwner
{
    internal static ClassicTargetTurnResult BeginTargetTurn(
        int sourceCurrentActionPoints,
        int sourceMaximumActionPoints,
        int aiPacket,
        int team,
        bool attackRequested,
        int actorTile,
        int targetTile,
        IReadOnlySet<int> actorAdjacentTiles)
    {
        if (sourceCurrentActionPoints < 0 || sourceMaximumActionPoints <= 0 ||
            sourceCurrentActionPoints > sourceMaximumActionPoints ||
            actorTile < 0 || targetTile < 0 || actorTile == targetTile ||
            actorAdjacentTiles.Count == 0 || actorAdjacentTiles.Contains(actorTile))
            throw new InvalidOperationException("Classic target-turn source state is invalid.");
        var action = !attackRequested
            ? ClassicTargetTurnAction.None
            : actorAdjacentTiles.Contains(targetTile)
                ? ClassicTargetTurnAction.AdjacentAttackRequired
                : ClassicTargetTurnAction.MovementRequired;
        return new ClassicTargetTurnResult(
            sourceMaximumActionPoints,
            aiPacket,
            team,
            action);
    }
}
