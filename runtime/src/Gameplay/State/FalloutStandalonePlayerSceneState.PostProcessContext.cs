using OpenNV.Runtime.World.Cells;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Physics;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutStandalonePostProcessContext(Guid Main, long MainOrdinal, long Entered, long Changed,
    IReadOnlyList<FalloutProcessSceneContextQuery> Queries, bool Returned, string? Failure,
    IReadOnlyList<FalloutPlayerControllerContextTrace>? ControllerChildren = null,
    FalloutStandaloneSceneReferenceQueryCall? ObjectReference = null);

internal sealed partial class FalloutStandalonePlayerSceneState
{
    private FalloutStandalonePostProcessContext? _postProcessContext;
    private Func<FalloutProcessSceneContextQuery>? _readPostProcessContext;
    private Func<FalloutProcessSceneContextQuery, IFalloutEarlyControllerLease>? _borrowPostProcessController;
    private FalloutControllerPostProcessInvocation? _enteredControllerContext;
    internal void BindPostProcessContext(Func<FalloutProcessSceneContextQuery> read)
    {
        RequireIdle(); ArgumentNullException.ThrowIfNull(read);
        if (_readPostProcessContext is not null) throw new InvalidOperationException("Player post-process context already has its living owner.");
        _readPostProcessContext = read;
    }
    internal void BindPostProcessController(Func<FalloutProcessSceneContextQuery, IFalloutEarlyControllerLease> borrow)
    {
        RequireIdle(); ArgumentNullException.ThrowIfNull(borrow);
        if (_borrowPostProcessController is not null) throw new InvalidOperationException("Player context already has its actual controller owner.");
        _borrowPostProcessController = borrow;
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
                    ExecuteControllerContext(main, ordinal, query, index);
            }
            _postProcessContext = _postProcessContext with { Returned = true, Changed = Next() };
        }
        catch (Exception error)
        {
            _postProcessContext = _postProcessContext with { Failure = error.Message, Changed = Next() }; throw;
        }
    }

    private void ExecuteControllerContext(Guid main, long ordinal, FalloutProcessSceneContextQuery query, int index)
    {
        using var lease = (_borrowPostProcessController ?? throw new NotSupportedException("source-Player-current-controller-context-owner-unbound"))(query);
        var controller = lease.Controller;
        var invocation = new FalloutControllerPostProcessInvocation(main, ordinal, _threadContext.Process,
            controller.Lineage.RuntimeProcess, _constructor, Environment.CurrentManagedThreadId, query, index);
        _enteredControllerContext = invocation;
        try
        {
            FalloutPlayerControllerContextConsumers.Execute(invocation,
                (entered, field, observe) =>
                {
                    lease.RequireCurrent();
                    return controller.ReadPostProcessField(entered, field, RequireEnteredControllerContext, observe);
                },
                () => ReadControllerNullObjectReference(invocation),
                trace =>
                {
                    RequireEnteredControllerContext(invocation);
                    var children = _postProcessContext!.ControllerChildren?.ToList() ?? [];
                    if (children.Count != 0 && children[^1].QueryOrdinal == index) children[^1] = trace;
                    else children.Add(trace);
                    _postProcessContext = _postProcessContext with { ControllerChildren = children.ToArray(), Changed = Next() };
                });
        }
        finally { _enteredControllerContext = null; }
    }

    private void RequireEnteredControllerContext(FalloutControllerPostProcessInvocation invocation)
    {
        RequireThread(); ObjectDisposedException.ThrowIf(_retired, this);
        if (!_writing || !ReferenceEquals(_enteredControllerContext, invocation) || _prelude is not { } prefix ||
            invocation.Main != prefix.Main || invocation.MainOrdinal != prefix.MainOrdinal ||
            invocation.PlayerProcess != _threadContext.Process || invocation.Constructor != _constructor ||
            invocation.ThreadId != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Controller child left its actual current Player/Main/context scope.");
        var queries = invocation.SourceScope switch
        {
            FalloutControllerPlayerSourceScope.PostProcessContext when prefix.Step == FalloutStandaloneScenePreludeStep.PostProcessContext &&
                _postProcessContext is { Returned: false, Failure: null } context => context.Queries,
            FalloutControllerPlayerSourceScope.PostActions when prefix.Step == FalloutStandaloneScenePreludeStep.PlayerSceneContinuation &&
                _postActionsCall is { Returned: false, Failure: null } actions => actions.ContextQueries,
            _ => throw new InvalidOperationException("Controller child changed its actual source Player scope.")
        };
        if (invocation.QueryOrdinal != queries.Count - 1 || !ReferenceEquals(queries[invocation.QueryOrdinal], invocation.Context))
            throw new InvalidOperationException("Controller child changed its actual last context query.");
    }

    private FalloutFormKey? ReadControllerNullObjectReference(FalloutControllerPostProcessInvocation invocation)
        => ReadNullControllerReference(invocation, () => _postProcessContext!.ObjectReference,
            call => _postProcessContext = _postProcessContext! with { ObjectReference = call, Changed = Next() });

    private FalloutFormKey? ReadNullControllerReference(FalloutControllerPostProcessInvocation invocation,
        Func<FalloutStandaloneSceneReferenceQueryCall?> read, Action<FalloutStandaloneSceneReferenceQueryCall> publish)
    {
        RequireEnteredControllerContext(invocation);
        publish(new(invocation.Main, invocation.MainOrdinal, _process, Next(), _sequence, null, null, false, null, false, null));
        try
        {
            var graph = new CurrentReferenceGraph(this, read, publish, () => RequireEnteredControllerContext(invocation));
            var result = FalloutSourceSceneReferenceQuery.Read(graph, null);
            publish(read()! with { Result = result, Returned = true, Changed = Next() });
            return result;
        }
        catch (Exception error)
        {
            publish(read()! with { Failure = error.Message, Changed = Next() });
            throw;
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
            call.Queries.Any(query => query.Process != update.Before with { Changed = update.Changed } || query.Context == Guid.Empty) ||
            // A reached nonnull context is a real retained failed prefix.
            // It must remain observable even while its consumers are absent.
            call.Queries.Where((query, index) => query.Context is not null &&
                !(call.ControllerChildren?.Any(child => child.QueryOrdinal == index && child.Returned) == true) &&
                (call.Returned || call.Failure is null || index != call.Queries.Count - 1)).Any())
            throw new InvalidDataException("Player post-process context lost its independent getter order, epoch or retained failure.");
        ValidateControllerContextChildren(saved, call);
    }
}
