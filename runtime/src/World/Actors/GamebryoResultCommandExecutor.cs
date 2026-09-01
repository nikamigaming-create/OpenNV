namespace OpenNV.Runtime.World.Actors;

internal sealed record SourceGamebryoResultCommand<T>(
    int SourceIndex,
    GamebryoResultCommandKind Kind,
    bool Terminal,
    T Value);

internal sealed record GamebryoResultExecution(int AppliedCount, bool Terminal);

internal static class GamebryoResultCommandExecutor
{
    internal static GamebryoResultExecution Execute<T>(
        IReadOnlyList<SourceGamebryoResultCommand<T>> orderedCommands,
        int startIndex,
        Func<SourceGamebryoResultCommand<T>, bool> applyAndPersist)
    {
        if (orderedCommands.Count == 0 ||
            startIndex < 0 || startIndex > orderedCommands.Count ||
            orderedCommands.Where((command, index) => command.SourceIndex != index).Any() ||
            orderedCommands.Any(command => !Enum.IsDefined(command.Kind)))
            throw new InvalidOperationException(
                "Source dialogue result command ordering is invalid.");
        var terminalIndices = orderedCommands
            .Where(command => command.Terminal)
            .Select(command => command.SourceIndex)
            .ToArray();
        if (terminalIndices.Length > 1 ||
            terminalIndices.Length == 1 && terminalIndices[0] != orderedCommands.Count - 1)
            throw new InvalidOperationException(
                "Source dialogue result terminal command ordering is invalid.");

        var applied = 0;
        for (var index = startIndex; index < orderedCommands.Count; index++)
        {
            var command = orderedCommands[index];
            var persisted = applyAndPersist(command);
            if (!persisted)
                throw new InvalidOperationException(
                    $"Source dialogue result state was not persisted: {command.Kind}.");
            applied++;
            if (command.Terminal)
                return new GamebryoResultExecution(applied, true);
        }
        return new GamebryoResultExecution(applied, false);
    }
}

internal enum GamebryoResultCommandKind
{
    ActorValueDelta,
    SetQuestVariable,
    SetDestroyed,
    AddItem,
    RemoveItem,
    EquipItem,
    PlayerControls,
    AddScriptPackage,
    RemoveScriptPackage,
    ImageSpaceModifier,
    ReferenceEnabled,
    ActorIntent,
    Objective,
    StartQuest,
    StopQuest,
    SetGlobal,
    AutoDisplayObjectives,
    Achievement,
    Autosave,
    SetTimer,
    SetStage,
    SayTo,
    DeferredStage,
}
