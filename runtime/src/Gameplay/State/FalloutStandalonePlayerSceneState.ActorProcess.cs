using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutStandalonePlayerSceneState
{
    private Func<FalloutProcessSceneQuery>? _readProcessQueries;
    private FalloutStandaloneActorProcessQuery? _actorProcessQuery;

    internal void BindActorProcessQueries(Func<FalloutProcessSceneQuery> read)
    {
        RequireIdle(); ArgumentNullException.ThrowIfNull(read);
        if (_readProcessQueries is not null) throw new InvalidOperationException("Player scene process query is already bound.");
        _readProcessQueries = read;
    }

    private void ReadActorProcessQueries(Guid main, long ordinal)
    {
        var read = _readProcessQueries ?? throw new NotSupportedException("source-Player-scene-Actor-process-current-owner-unbound");
        var entered = Next();
        var fault = _reentry;
        _actorProcessQuery = new(main, ordinal, _process, entered, entered, null, 0, false, null);
        try
        {
            var entry = read(); RequireQuery(entry);
            _actorProcessQuery = _actorProcessQuery with { Result = entry, EntryCalls = 1, Changed = Next() };
            Check();
            // The null entry arm calls the entry getter once. Nonnull entry
            // writers/consumers must be owned before its repeated read is legal.
            var animation = read(); RequireQuery(animation);
            if (animation != entry) throw new InvalidOperationException("Player process changed before its independent animation-context query.");
            _actorProcessQuery = _actorProcessQuery with { AnimationReturned = true, Changed = Next() };
            Check();
        }
        catch (Exception error)
        {
            _actorProcessQuery = _actorProcessQuery with { Failure = error.Message, Changed = Next() }; throw;
        }
        void Check()
        {
            RequireThread();
            if (_reentry != fault) throw new InvalidOperationException("Player process getter swallowed a scene owner reentry refusal.");
        }
    }

    private void RequireQuery(FalloutProcessSceneQuery query)
    {
        if (query is null || query.Stack != _stack || query.ExecutableSha256 != Source.Main.EngineSha256 ||
            query.Actor is null || !query.Actor.EnginePlayer || query.Process == Guid.Empty || query.GameplayOwnership == Guid.Empty ||
            query.Epoch < 1 || query.Changed < 1 || query.Level != FalloutDetectionProcessLevel.High)
            throw new InvalidDataException("Player process query lost its actual source class, actor, epoch or gameplay lease.");
        if (query.WeaponEntry is not null || query.AnimationContext is not null || query.IdlePrimaryPackage is not null)
            throw new NotSupportedException("source-Player-scene-process-nonnull-entry-or-animation-consumers-unowned");
    }

    private static void ValidateActorProcessQuery(FalloutStandalonePlayerSceneSnapshot saved)
    {
        if (saved.ActorProcessQuery is not { } call)
        {
            if (saved.Prelude?.FailedAt is FalloutStandaloneScenePreludeStep.ActorSceneContinuation or FalloutStandaloneScenePreludeStep.QueuedSceneNodes or FalloutStandaloneScenePreludeStep.ProcessSceneUpdate or FalloutStandaloneScenePreludeStep.PostProcessContext or FalloutStandaloneScenePreludeStep.TimedActions or FalloutStandaloneScenePreludeStep.PlayerSceneContinuation)
                throw new InvalidDataException("Player continuation lost its returned actor process queries.");
            return;
        }
        var query = call.Result;
        if (saved.Prelude is not { FailedAt: FalloutStandaloneScenePreludeStep.ActorProcessQuery or FalloutStandaloneScenePreludeStep.ActorSceneContinuation or FalloutStandaloneScenePreludeStep.QueuedSceneNodes or FalloutStandaloneScenePreludeStep.ProcessSceneUpdate or FalloutStandaloneScenePreludeStep.PostProcessContext or FalloutStandaloneScenePreludeStep.TimedActions or FalloutStandaloneScenePreludeStep.PlayerSceneContinuation } prefix ||
            saved.InputManagerQuery is not { Returned: true } input || call.Main != prefix.Main || call.MainOrdinal != prefix.MainOrdinal ||
            call.Process != input.Process || call.Entered <= input.Changed || call.Changed < call.Entered || call.Changed > saved.Sequence ||
            call.EntryCalls is < 0 or > 1 || (call.Result is null) != (call.EntryCalls == 0) ||
            call.AnimationReturned && call.EntryCalls != 1 ||
            (prefix.FailedAt is FalloutStandaloneScenePreludeStep.ActorSceneContinuation or FalloutStandaloneScenePreludeStep.QueuedSceneNodes or FalloutStandaloneScenePreludeStep.ProcessSceneUpdate or FalloutStandaloneScenePreludeStep.PostProcessContext or FalloutStandaloneScenePreludeStep.TimedActions or FalloutStandaloneScenePreludeStep.PlayerSceneContinuation) && (!call.AnimationReturned || call.Failure is not null) ||
            prefix.FailedAt == FalloutStandaloneScenePreludeStep.ActorProcessQuery && call.Failure is null ||
            call.Failure is not null && (string.IsNullOrWhiteSpace(call.Failure) || call.Failure != saved.Failure ||
                prefix.FailedAt != FalloutStandaloneScenePreludeStep.ActorProcessQuery) ||
            !call.AnimationReturned && call.Failure is null)
            throw new InvalidDataException("Player process query lost its returned children or retained failed prefix.");
        if (query is null) return;
        if (query.Stack != saved.Stack || query.ExecutableSha256 != saved.Source.Main.EngineSha256 ||
            query.Actor is null || !query.Actor.EnginePlayer || query.Process == Guid.Empty || query.GameplayOwnership == Guid.Empty ||
            query.Epoch < 1 || query.Changed < 1 || query.Level != FalloutDetectionProcessLevel.High ||
            query.WeaponEntry is not null || query.AnimationContext is not null || query.IdlePrimaryPackage is not null)
            throw new InvalidDataException("Player process receipt changed its actual independent nullable getters or retained source owner.");
        query.Actor.Validate();
    }
}
