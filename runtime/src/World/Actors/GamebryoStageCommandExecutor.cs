namespace OpenNV.Runtime.World.Actors;

internal sealed record SourceGamebryoStageCommand<T>(
    int SourceIndex,
    GamebryoStageCommandKind Kind,
    T Value);

internal static class GamebryoStageCommandExecutor
{
    internal static void ExecuteOne<T>(
        IReadOnlyList<SourceGamebryoStageCommand<T>> orderedCommands,
        int sourceIndex,
        Func<SourceGamebryoStageCommand<T>, bool> applyAndPersist)
    {
        Validate(orderedCommands);
        if (sourceIndex < 0 || sourceIndex >= orderedCommands.Count)
            throw new InvalidOperationException("Source stage command cursor is invalid.");
        Apply(orderedCommands[sourceIndex], applyAndPersist);
    }

    internal static void ExecuteAll<T>(
        IReadOnlyList<SourceGamebryoStageCommand<T>> orderedCommands,
        Func<SourceGamebryoStageCommand<T>, bool> applyAndPersist)
    {
        Validate(orderedCommands);
        foreach (var command in orderedCommands)
            Apply(command, applyAndPersist);
    }

    private static void Validate<T>(
        IReadOnlyList<SourceGamebryoStageCommand<T>> orderedCommands)
    {
        if (orderedCommands.Count == 0 ||
            orderedCommands.Where((command, index) => command.SourceIndex != index).Any() ||
            orderedCommands.Any(command => !Enum.IsDefined(command.Kind)))
            throw new InvalidOperationException("Source stage command ordering is invalid.");
    }

    private static void Apply<T>(
        SourceGamebryoStageCommand<T> command,
        Func<SourceGamebryoStageCommand<T>, bool> applyAndPersist)
    {
        if (!applyAndPersist(command))
            throw new InvalidOperationException(
                $"Source stage command mutation was not persisted: {command.Kind}.");
    }
}

internal enum GamebryoStageCommandKind
{
    SetTimer,
    SetQuestVariable,
    SetLocationSpecificLoadScreensOnly,
    SetInCharacterGeneration,
    SetStage,
    Dialogue,
    ShowMenu,
    Objective,
    SetDestroyed,
    PlayIdle,
    PlayerControls,
    AddScriptPackage,
    RemoveScriptPackage,
    ImageSpaceModifier,
    PlaySound,
    AddItem,
    RemoveItem,
    EquipItem,
    ReferenceEnabled,
    Enable,
    Disable,
    MoveToReference,
    SetPlayerScale,
    SetPlayerToddler,
    SetPlayerYoung,
    SetNoActivationSound,
    PlayMovie,
    ActorIntent,
    ActorValueDelta,
    SetScriptVariable,
    StartQuest,
    StopQuest,
    SetGlobal,
    AutoDisplayObjectives,
    Achievement,
    Autosave,
    DeferredStage,
}
