namespace OpenNV.Runtime.Campaigns.Classic;

internal enum ClassicRetailRandomLifecyclePhase
{
    ProcessInitialized,
    NewGameReset,
    LoadReset,
    SourceCallRequired,
}

internal sealed record ClassicRetailRandomLifecycleEvent(
    string EventId,
    string OwnerId,
    int? Minimum,
    int? Maximum,
    int? Value);

internal sealed record ClassicRetailRandomLifecycleState(
    ClassicRetailSeedState SeedState,
    ClassicRetailRandomState RandomState,
    ClassicRetailRandomLifecyclePhase Phase,
    bool Exact,
    string? Boundary,
    IReadOnlyList<ClassicRetailRandomLifecycleEvent> Events)
{
    internal void Validate(ClassicRetailRandomContract contract)
    {
        contract.Validate();
        RandomState.Validate(contract);
        if (SeedState.ResetCount <= 0 ||
            Exact && (Phase == ClassicRetailRandomLifecyclePhase.SourceCallRequired ||
                Boundary is not null) ||
            !Exact && (Phase != ClassicRetailRandomLifecyclePhase.SourceCallRequired ||
                string.IsNullOrWhiteSpace(Boundary)) ||
            Events.Count == 0 || Events.Any(row =>
                string.IsNullOrWhiteSpace(row.EventId) ||
                string.IsNullOrWhiteSpace(row.OwnerId) ||
                (row.Minimum is null) != (row.Maximum is null) ||
                (row.Value is null) != (row.Minimum is null) ||
                row.Minimum > row.Maximum ||
                row.Value is { } value &&
                    (value < row.Minimum!.Value || value > row.Maximum!.Value)))
            throw new InvalidOperationException(
                "Classic retail random lifecycle state is invalid.");
    }
}

internal sealed record ClassicRetailRandomLifecycleValue(
    ClassicRetailRandomLifecycleState State,
    int Value);

internal static class ClassicRetailRandomLifecycle
{
    internal static ClassicRetailRandomLifecycleState InitializeFromExactBuildClock(
        ClassicRetailRandomContract contract) =>
        FromSeeded(
            ClassicRetailSeedOwner.InitializeFromExactBuildClock(contract),
            ClassicRetailRandomLifecyclePhase.ProcessInitialized,
            "process-initialization",
            "exact-build-external-seed");

    internal static ClassicRetailRandomLifecycleState Initialize(
        uint elapsedMilliseconds,
        ClassicRetailRandomContract contract) =>
        FromSeeded(
            ClassicRetailSeedOwner.Initialize(elapsedMilliseconds, contract),
            ClassicRetailRandomLifecyclePhase.ProcessInitialized,
            "process-initialization",
            "exact-build-external-seed");

    internal static ClassicRetailRandomLifecycleState ResetForNewGame(
        ClassicRetailRandomLifecycleState state,
        ClassicRetailRandomContract contract)
    {
        RequireExact(state, contract);
        return FromSeeded(
            ClassicRetailSeedOwner.ResetForNewGame(state.SeedState, contract),
            ClassicRetailRandomLifecyclePhase.NewGameReset,
            "new-game-reset",
            "character-creation-handoff",
            state.Events);
    }

    internal static ClassicRetailRandomLifecycleState ResetForLoad(
        ClassicRetailRandomLifecycleState state,
        ClassicRetailRandomContract contract)
    {
        RequireExact(state, contract);
        return FromSeeded(
            ClassicRetailSeedOwner.ResetForLoad(state.SeedState, contract),
            ClassicRetailRandomLifecyclePhase.LoadReset,
            "load-reset",
            "save-load-handoff",
            state.Events);
    }

    internal static ClassicRetailRandomLifecycleValue Consume(
        ClassicRetailRandomLifecycleState state,
        ClassicRetailRandomContract contract,
        string eventId,
        string ownerId,
        int minimum,
        int maximum)
    {
        RequireExact(state, contract);
        if (string.IsNullOrWhiteSpace(eventId) || string.IsNullOrWhiteSpace(ownerId))
            throw new InvalidOperationException(
                "Classic retail random call owner is invalid.");
        var result = ClassicRetailRandom.Next(state.RandomState, minimum, maximum, contract);
        var next = state with
        {
            RandomState = result.State,
            Events = Append(state.Events, new ClassicRetailRandomLifecycleEvent(
                eventId, ownerId, minimum, maximum, result.Value)),
        };
        next.Validate(contract);
        return new ClassicRetailRandomLifecycleValue(next, result.Value);
    }

    internal static ClassicRetailRandomLifecycleState RequireSourceCall(
        ClassicRetailRandomLifecycleState state,
        ClassicRetailRandomContract contract,
        string eventId,
        string ownerId)
    {
        RequireExact(state, contract);
        if (string.IsNullOrWhiteSpace(eventId) || string.IsNullOrWhiteSpace(ownerId))
            throw new InvalidOperationException(
                "Classic retail random source boundary is invalid.");
        var next = state with
        {
            Phase = ClassicRetailRandomLifecyclePhase.SourceCallRequired,
            Exact = false,
            Boundary = $"{eventId}:{ownerId}",
            Events = Append(state.Events, new ClassicRetailRandomLifecycleEvent(
                eventId, ownerId, null, null, null)),
        };
        next.Validate(contract);
        return next;
    }

    private static ClassicRetailRandomLifecycleState FromSeeded(
        ClassicRetailSeededRandom seeded,
        ClassicRetailRandomLifecyclePhase phase,
        string eventId,
        string ownerId,
        IReadOnlyList<ClassicRetailRandomLifecycleEvent>? existing = null)
    {
        var events = Append(
            existing ?? [],
            new ClassicRetailRandomLifecycleEvent(eventId, ownerId, null, null, null));
        return new ClassicRetailRandomLifecycleState(
            seeded.SeedState,
            seeded.RandomState,
            phase,
            true,
            null,
            events);
    }

    private static IReadOnlyList<ClassicRetailRandomLifecycleEvent> Append(
        IReadOnlyList<ClassicRetailRandomLifecycleEvent> events,
        ClassicRetailRandomLifecycleEvent value) => [.. events, value];

    private static void RequireExact(
        ClassicRetailRandomLifecycleState state,
        ClassicRetailRandomContract contract)
    {
        state.Validate(contract);
        if (!state.Exact)
            throw new InvalidOperationException(
                $"Classic retail random state stopped at source boundary: {state.Boundary}");
    }
}
