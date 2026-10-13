using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutPlayerSceneQueueEvent(FalloutFormKey Reference, string Operation);
internal sealed record FalloutPlayerSceneQueueCall(Guid Main, long MainOrdinal, long Entered, long Changed,
    IReadOnlyList<FalloutFormKey> Before, IReadOnlyList<FalloutFormKey> After,
    IReadOnlyList<FalloutPlayerSceneQueueEvent> Events, bool Returned, string? Failure);

internal sealed partial class FalloutStandalonePlayerSceneState
{
    private readonly FalloutPlayerSceneReferenceQueue _queuedReferences = new();
    private FalloutPlayerSceneQueueCall? _sceneQueueCall;
    private IFalloutPlayerSceneQueuedNodes? _queuedNodeConsumers;
    internal void BindQueuedSceneNodes(IFalloutPlayerSceneQueuedNodes consumers)
    {
        RequireIdle(); ArgumentNullException.ThrowIfNull(consumers);
        if (_queuedNodeConsumers is not null || consumers.Process != _process || consumers.Stack != _stack)
            throw new InvalidOperationException("Player queued nodes changed their actual process/stack or rebound a living owner.");
        consumers.RequireCurrent(); _queuedNodeConsumers = consumers;
    }
    private void UpdateQueuedSceneReferences(Guid main, long ordinal)
    {
        var entered = Next(); var fault = _reentry;
        _sceneQueueCall = new(main, ordinal, entered, entered, _queuedReferences.References,
            _queuedReferences.References, [], false, null);
        try
        {
            _queuedReferences.Update(_queuedNodeConsumers, (reference, operation) =>
            {
                RequireThread();
                if (_reentry != fault) throw new InvalidOperationException("Queued scene child swallowed a Player owner reentry refusal.");
                _sceneQueueCall = _sceneQueueCall! with
                {
                    After = _queuedReferences.References,
                    Events = _sceneQueueCall.Events.Append(new(reference, operation)).ToArray(),
                    Changed = Next()
                };
            });
            _sceneQueueCall = _sceneQueueCall with { After = _queuedReferences.References, Returned = true, Changed = Next() };
        }
        catch (Exception error)
        {
            _sceneQueueCall = _sceneQueueCall with { After = _queuedReferences.References, Failure = error.Message, Changed = Next() };
            throw;
        }
    }
    private static void ValidateQueuedSceneReferences(FalloutStandalonePlayerSceneSnapshot saved)
    {
        if (saved.QueuedReferences is null) throw new InvalidDataException("Player scene lost its actual reference queue.");
        foreach (var reference in saved.QueuedReferences) FalloutPlayerSceneReferenceQueue.ValidateReference(reference);
        if (saved.SceneQueueCall is not { } call)
        {
            if (saved.Prelude?.FailedAt is FalloutStandaloneScenePreludeStep.ProcessSceneUpdate or FalloutStandaloneScenePreludeStep.PostProcessContext or FalloutStandaloneScenePreludeStep.TimedActions or FalloutStandaloneScenePreludeStep.PlayerSceneContinuation)
                throw new InvalidDataException("Player process continuation lost its returned reference queue.");
            return;
        }
        if (saved.Prelude is not { FailedAt: FalloutStandaloneScenePreludeStep.QueuedSceneNodes or FalloutStandaloneScenePreludeStep.ProcessSceneUpdate or FalloutStandaloneScenePreludeStep.PostProcessContext or FalloutStandaloneScenePreludeStep.TimedActions or FalloutStandaloneScenePreludeStep.PlayerSceneContinuation } prefix ||
            saved.SceneScalarCall is not { Returned: true } scalars || call.Main != prefix.Main || call.MainOrdinal != prefix.MainOrdinal ||
            call.Entered <= scalars.Changed || call.Changed <= call.Entered || call.Changed > saved.Sequence ||
            call.Before is null || call.After is null || call.Events is null || !call.After.SequenceEqual(saved.QueuedReferences) ||
            call.Returned == (call.Failure is not null) ||
            (prefix.FailedAt is FalloutStandaloneScenePreludeStep.ProcessSceneUpdate or FalloutStandaloneScenePreludeStep.PostProcessContext or FalloutStandaloneScenePreludeStep.TimedActions or FalloutStandaloneScenePreludeStep.PlayerSceneContinuation) != call.Returned)
            throw new InvalidDataException("Player scene queue lost its exact ordered child/failure history.");
        foreach (var reference in call.Before.Concat(call.After)) FalloutPlayerSceneReferenceQueue.ValidateReference(reference);
        foreach (var item in call.Events)
        {
            if (item is null || item.Operation is not ("node-read" or "retained" or "not-retained" or "flag-zero-cleared" or "node-updated" or "removed" or "already-removed"))
                throw new InvalidDataException("Player queue omitted or invented an actual child event.");
            FalloutPlayerSceneReferenceQueue.ValidateReference(item.Reference);
        }
    }
}
