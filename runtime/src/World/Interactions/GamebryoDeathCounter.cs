namespace OpenNV.Runtime.World.Interactions;

internal sealed record GamebryoDeathCounterContract(
    int Increment,
    int Threshold,
    int MinimumCombatStage,
    int CompletionStage);

internal sealed record GamebryoDeathCounterState(
    int Count,
    int QuestStage,
    bool ObjectiveDisplayed,
    bool ObjectiveCompleted);

internal sealed record GamebryoDeathCounterOutcome(
    int Count,
    IReadOnlyList<int> Stages,
    bool ResetAi);

internal static class GamebryoDeathCounter
{
    internal static GamebryoDeathCounterOutcome Advance(
        GamebryoDeathCounterContract contract,
        GamebryoDeathCounterState state)
    {
        if (contract.Increment <= 0 || contract.Threshold <= 0 ||
            contract.MinimumCombatStage < 0 ||
            contract.CompletionStage <= contract.MinimumCombatStage ||
            state.Count < 0 || state.Count >= contract.Threshold ||
            state.QuestStage < 0 || state.ObjectiveCompleted && !state.ObjectiveDisplayed)
            throw new InvalidOperationException(
                "Gamebryo death-counter contract is invalid.");
        var count = checked(state.Count + contract.Increment);
        if (count > contract.Threshold)
            throw new InvalidOperationException(
                "Gamebryo death counter crossed its source threshold.");
        var stages = new List<int>();
        if (!state.ObjectiveDisplayed &&
            state.QuestStage < contract.MinimumCombatStage)
            stages.Add(contract.MinimumCombatStage);
        var resetAi = count == contract.Threshold && !state.ObjectiveCompleted;
        if (resetAi)
            stages.Add(contract.CompletionStage);
        return new GamebryoDeathCounterOutcome(count, stages, resetAi);
    }
}
