using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutStandalonePostProcessContext(Guid Main, long MainOrdinal, long Entered, long Changed,
    IReadOnlyList<FalloutProcessSceneContextQuery> Queries, bool Returned, string? Failure);

internal sealed partial class FalloutStandalonePlayerSceneState
{
    private FalloutStandalonePostProcessContext? _postProcessContext;
    private Func<FalloutProcessSceneContextQuery>? _readPostProcessContext;
    internal void BindPostProcessContext(Func<FalloutProcessSceneContextQuery> read)
    {
        RequireIdle(); ArgumentNullException.ThrowIfNull(read);
        if (_readPostProcessContext is not null) throw new InvalidOperationException("Player post-process context already has its living owner.");
        _readPostProcessContext = read;
    }
    private void ReadPostProcessContext(Guid main, long ordinal)
    {
        var entered = Next(); var fault = _reentry;
        _postProcessContext = new(main, ordinal, entered, entered, [], false, null);
        try
        {
            var update = _processSceneUpdate?.Result ?? throw new InvalidOperationException("Context continuation lost its actual process update.");
            var expected = update.Before with { Changed = update.Changed };
            // Two calls read a third independent process field. They cannot
            // reuse the earlier animation-context slot or a Godot tree.
            for (var index = 0; index < 2; ++index)
            {
                var query = (_readPostProcessContext ?? throw new NotSupportedException("source-Player-post-process-current-context-query-unbound"))();
                RequireThread();
                if (_reentry != fault || query.Process != expected)
                    throw new InvalidOperationException("Post-process context changed its actual process/epoch or swallowed a Player owner reentry refusal.");
                _postProcessContext = _postProcessContext with { Queries = _postProcessContext.Queries.Append(query).ToArray(), Changed = Next() };
                if (query.Context is not null)
                    throw new NotSupportedException("source-Player-post-process-nonnull-context-and-node-consumers-unbound");
            }
            _postProcessContext = _postProcessContext with { Returned = true, Changed = Next() };
        }
        catch (Exception error)
        {
            _postProcessContext = _postProcessContext with { Failure = error.Message, Changed = Next() }; throw;
        }
    }
    private static void ValidatePostProcessContext(FalloutStandalonePlayerSceneSnapshot saved)
    {
        if (saved.PostProcessContext is not { } call)
        {
            if (saved.Prelude?.FailedAt is FalloutStandaloneScenePreludeStep.TimedActions or FalloutStandaloneScenePreludeStep.PlayerSceneContinuation)
                throw new InvalidDataException("Player scene continuation lost its returned context queries.");
            return;
        }
        if (saved.Prelude is not { FailedAt: FalloutStandaloneScenePreludeStep.PostProcessContext or FalloutStandaloneScenePreludeStep.TimedActions or FalloutStandaloneScenePreludeStep.PlayerSceneContinuation } prefix ||
            saved.ProcessSceneUpdate is not { Result: { } update } process || call.Main != prefix.Main || call.MainOrdinal != prefix.MainOrdinal ||
            call.Entered <= process.Changed || call.Changed <= call.Entered || call.Changed > saved.Sequence || call.Queries is null ||
            call.Queries.Count > 2 || call.Returned && call.Queries.Count != 2 || call.Returned == (call.Failure is not null) ||
            (prefix.FailedAt is FalloutStandaloneScenePreludeStep.TimedActions or FalloutStandaloneScenePreludeStep.PlayerSceneContinuation) != call.Returned ||
            call.Failure is not null && call.Failure != saved.Failure ||
            call.Queries.Any(query => query.Process != update.Before with { Changed = update.Changed } || query.Context is not null))
            throw new InvalidDataException("Player post-process context lost its independent getter order, epoch or retained failure.");
    }
}
