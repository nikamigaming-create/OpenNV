using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutStandaloneSceneReferenceQueryCall(Guid Main, long MainOrdinal, Guid Process,
    long Entered, long Changed, FalloutSourceSceneNode? Input, FalloutSourceSceneNode? FirstPerson,
    bool GetterReturned, FalloutFormKey? Result, bool Returned, string? Failure);

internal sealed partial class FalloutStandalonePlayerSceneState
{
    // The selected Player constructor explicitly stores a null reference and
    // a zero argument byte. No selection/crosshair/input writer is owned yet.
    private FalloutSourceSceneNode? _sceneReference;
    private byte _sceneReferenceArgument;
    private FalloutStandaloneSceneReferenceQueryCall? _referenceQuery;

    private void ReadInitialSceneReference(Guid main, long ordinal)
    {
        if (!_writing || _prelude is not { Step: FalloutStandaloneScenePreludeStep.ReferenceQuery } prefix ||
            prefix.Main != main || prefix.MainOrdinal != ordinal || _referenceQuery is not null)
            throw new InvalidOperationException("Scene reference query has no actual entered Player virtual prefix.");
        _referenceQuery = new(main, ordinal, _process, Next(), _sequence, _sceneReference, null, false, null, false, null);
        try
        {
            var result = FalloutSourceSceneReferenceQuery.Read(new CurrentReferenceGraph(this), _sceneReference);
            _referenceQuery = _referenceQuery with { Result = result, Returned = true, Changed = Next() };
        }
        catch (Exception failure)
        {
            _referenceQuery = _referenceQuery with { Failure = failure.Message, Changed = Next() }; throw;
        }
    }

    private sealed class CurrentReferenceGraph(FalloutStandalonePlayerSceneState state) : IFalloutSourceSceneReferenceGraph
    {
        public FalloutStandalonePlayerSceneSource Source => state.Source;
        public Guid Process => state._process;
        public void RequireCurrent()
        {
            state.RequireThread(); ObjectDisposedException.ThrowIf(state._retired, state);
            if (!state._writing || state._referenceQuery is not { Returned: false, Failure: null } query ||
                query.Process != Process || state._prelude?.Step != FalloutStandaloneScenePreludeStep.ReferenceQuery)
                throw new InvalidOperationException("Reference query lost its actual entered source child.");
        }
        public FalloutSourceSceneNode? ReadForcedFirstPerson()
        {
            RequireCurrent();
            FalloutSourceSceneNode? result = null;
            if (state._first is { Released: null } first)
            {
                if (first.Publication.Process != Process || state._livingFirst is null || !state.ReadLiving(state._livingFirst))
                    throw new NotSupportedException("Forced Player first-person getter has no current native factory rebind.");
                result = new(Process, first.Publication.Factory, first.Publication.NativeRoot);
            }
            // The explicit source argument bypasses view selection and TLS.
            // Constructor-null and an actual returned release remain null;
            // a historical live field never becomes a current native root.
            state._referenceQuery = state._referenceQuery! with
            { FirstPerson = result, GetterReturned = true, Changed = state.Next() };
            return result;
        }
        public FalloutFormKey ReadPlayerReference() =>
            throw new NotSupportedException("source-Player-scene-reference-actual-Player-reference-map-unbound");
        public FalloutSourceSceneReference? ReadNodeReference(FalloutSourceSceneNode node) =>
            throw new NotSupportedException("source-scene-node-virtual-reference-factory-unbound");
        public FalloutSourceSceneNode? ReadParent(FalloutSourceSceneNode node) =>
            throw new NotSupportedException("source-scene-node-actual-parent-field-owner-unbound");
    }

    private static void ValidateSceneReferenceQuery(FalloutStandalonePlayerSceneSnapshot saved)
    {
        if (saved.SceneReference is not null || saved.SceneReferenceArgument != 0)
            throw new InvalidDataException("Scene reference continuation has no actual writer/native cold factory.");
        if (saved.ReferenceQuery is not { } query)
        {
            if (saved.Prelude?.FailedAt is FalloutStandaloneScenePreludeStep.ReferenceQuery or FalloutStandaloneScenePreludeStep.ReferenceFrame)
                throw new InvalidDataException("The entered source reference-query child lost its retained prefix.");
            return;
        }
        if (saved.Prelude is not { } prefix || saved.Clock is not { } clock ||
            query.Main != prefix.Main || query.MainOrdinal != prefix.MainOrdinal || query.Process == Guid.Empty ||
            query.Process != saved.CapturedProcess && saved.ColdHandoff?.PreviousProcess != query.Process ||
            query.Entered <= prefix.Entered || query.Changed < query.Entered || query.Changed > saved.Sequence ||
            query.Input != saved.SceneReference || query.Result is not null ||
            query.Returned != (query.Failure is null) || query.Returned && !query.GetterReturned ||
            !query.GetterReturned && query.FirstPerson is not null ||
            query.Failure is { Length: 0 } || query.Failure is not null && query.Failure != saved.Failure ||
            query.FirstPerson is { } node && (node.Process != query.Process || node.Factory == Guid.Empty || node.NativeRoot == 0) ||
            query.Returned && prefix.FailedAt != FalloutStandaloneScenePreludeStep.ReferenceFrame ||
            !query.Returned && prefix.FailedAt != FalloutStandaloneScenePreludeStep.ReferenceQuery ||
            clock.Step != FalloutStandaloneSceneClockStep.Failed)
            throw new InvalidDataException("Scene reference query lost its actual getter, source return or following failed child.");
        if (saved.ColdHandoff is { } handoff && (query.Process == handoff.CurrentProcess
            ? query.Entered <= handoff.Changed : query.Changed >= handoff.Changed))
            throw new InvalidDataException("Scene reference query moved across its actual cold process handoff.");
        var history = saved.FirstPersonHistory.Where(owner => owner.Stored < query.Entered)
            .OrderBy(owner => owner.Stored).ToArray();
        if (history.Select(owner => owner.Stored).Distinct().Count() != history.Length)
            throw new InvalidDataException("Scene reference query has ambiguous first-person field publication order.");
        foreach (var process in history.GroupBy(owner => owner.Publication.Process))
        {
            FalloutStandaloneFirstPersonOwner? previous = null;
            foreach (var owner in process)
            {
                if (previous is not null && (previous.Released is null || previous.Released >= owner.Stored))
                    throw new InvalidDataException("Scene reference query retained overlapping live fields in one source process.");
                previous = owner;
            }
        }
        if (query.GetterReturned)
        {
            // A cold handoff retains the old field until a real new factory
            // replaces it. Filtering by current process would invent null.
            // A released latest field is null; it never resurrects an older row.
            var field = history.LastOrDefault();
            var first = field is not null && (field.Released is null || field.Released > query.Entered) ? field : null;
            if (first is not null && (first.Publication.Process != query.Process ||
                first.Released is { } released && released <= query.Changed))
                throw new InvalidDataException("Forced first-person query returned an unrebound or concurrently released source field.");
            var expected = first is not null
                ? new FalloutSourceSceneNode(first.Publication.Process, first.Publication.Factory, first.Publication.NativeRoot) : null;
            if (query.FirstPerson != expected)
                throw new InvalidDataException("Forced first-person query changed its exact retained current factory field.");
        }
    }
}
