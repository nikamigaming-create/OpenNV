using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutStandaloneActorDistanceSort(Guid Process, Guid Constructor, string Source, string Stack,
    long Entered, long Changed, int Limit, FalloutInterfaceActorDistanceSnapshot Before,
    FalloutInterfaceActorDistanceSnapshot After, int DistanceQueries, bool Returned, string? Failure);

internal sealed partial class FalloutStandaloneInterfaceState
{
    private readonly FalloutInterfaceActorDistanceList _actorDistances = new();
    private FalloutStandaloneActorDistanceSort? _actorDistanceSort;
    internal FalloutStandaloneActorDistanceSort? LastActorDistanceSort => _actorDistanceSort;
    internal bool IncludeActorDistance(FalloutFormKey? actor, int limit, Func<FalloutFormKey, float?> readContextValue)
    {
        RequireCurrent();
        try { var result = _actorDistances.Add(actor, limit, readContextValue); if (result) Next(); return result; }
        catch (Exception error) { RetainFailure(error); throw; }
    }
    internal void RemoveActorDistance(FalloutFormKey actor)
    {
        RequireCurrent();
        try { if (_actorDistances.Remove(actor)) Next(); }
        catch (Exception error) { RetainFailure(error); throw; }
    }
    internal FalloutStandaloneActorDistanceSort SortActorDistances(int limit, Func<FalloutFormKey, float> readDistance)
    {
        RequireCurrent(); ArgumentNullException.ThrowIfNull(readDistance);
        var before = _actorDistances.Capture(); var entered = Next(); var queries = 0;
        var revision = _sequence;
        _actorDistanceSort = new(_process, _constructor, Source.Identity, _stack, entered, entered, limit, before, before, 0, false, null);
        try
        {
            _actorDistances.Sort(limit, key =>
            {
                ++queries; var value = readDistance(key); RequireUnchangedOwner(); return value;
            });
            RequireUnchangedOwner();
            _actorDistanceSort = _actorDistanceSort with { After = _actorDistances.Capture(), DistanceQueries = queries, Returned = true, Changed = Next() };
            return _actorDistanceSort;
        }
        catch (Exception error)
        {
            _actorDistanceSort = _actorDistanceSort with { After = _actorDistances.Capture(), DistanceQueries = queries, Failure = error.Message, Changed = Next() };
            RetainFailure(error); throw;
        }
        void RequireUnchangedOwner()
        {
            RequireCurrent();
            if (_sequence != revision) throw new InvalidOperationException("Actor-distance query changed its actual interface owner during sorting.");
        }
    }
    private static void ValidateActorDistances(FalloutStandaloneInterfaceSnapshot saved)
    {
        FalloutInterfaceActorDistanceList.Validate(saved.ActorDistances!);
        if (saved.ActorDistanceSort is not { } sort) return;
        FalloutInterfaceActorDistanceList.Validate(sort.Before); FalloutInterfaceActorDistanceList.Validate(sort.After);
        if (sort.Process == Guid.Empty || sort.Constructor == Guid.Empty || sort.Source != saved.Source.Identity || sort.Stack != saved.Stack ||
            sort.Entered < 1 || sort.Changed <= sort.Entered || sort.Changed > saved.Sequence || sort.DistanceQueries < 0 ||
            sort.Returned == (sort.Failure is not null) || sort.Returned && sort.After.Failure is not null ||
            sort.Returned && sort.After.Count > sort.Limit || sort.After.Sequence < sort.Before.Sequence ||
            sort.After.Sequence > saved.ActorDistances!.Sequence ||
            sort.After.Sequence == saved.ActorDistances.Sequence && (sort.After.Count != saved.ActorDistances.Count ||
                sort.After.Failure != saved.ActorDistances.Failure || !sort.After.Slots.SequenceEqual(saved.ActorDistances.Slots)) ||
            sort.Process != saved.CapturedProcess && (saved.ColdHandoff is not { } cold || sort.Process != cold.PreviousProcess || sort.Constructor != cold.PreviousConstructor) ||
            sort.Process == saved.CapturedProcess && sort.Constructor != saved.Constructor)
            throw new InvalidDataException("Interface actor sort lost its real current/cold owner or returned mutation order.");
    }
}
