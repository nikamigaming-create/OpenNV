using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutStandalonePlayerProcessSceneUpdate(Guid Main, long MainOrdinal, long Entered,
    long Changed, FalloutProcessPlayerSceneUpdate? Result, string? Failure);

internal sealed partial class FalloutStandalonePlayerSceneState
{
    private Func<FalloutProcessPlayerSceneUpdate>? _updateProcessScene;
    private FalloutStandalonePlayerProcessSceneUpdate? _processSceneUpdate;
    internal void BindProcessSceneUpdate(Func<FalloutProcessPlayerSceneUpdate> update)
    {
        RequireIdle(); ArgumentNullException.ThrowIfNull(update);
        if (_updateProcessScene is not null) throw new InvalidOperationException("Player process scene update already has its living owner.");
        _updateProcessScene = update;
    }
    private void UpdateProcessScene(Guid main, long ordinal)
    {
        var entered = Next(); var fault = _reentry;
        _processSceneUpdate = new(main, ordinal, entered, entered, null, null);
        try
        {
            var result = (_updateProcessScene ?? throw new NotSupportedException("source-Player-current-process-scene-update-virtual-unbound"))();
            RequireThread();
            if (_reentry != fault) throw new InvalidOperationException("Process scene update swallowed a Player owner reentry refusal.");
            if (_actorProcessQuery?.Result is not { } before || result.Before != before || !result.Returned ||
                (before.Level == FalloutDetectionProcessLevel.High ? result.Stores != 1 || result.BeforeMask != 0 || result.AfterMask != 0 || result.Changed <= before.Changed :
                    result.Stores != 0 || result.BeforeMask is not null || result.AfterMask is not null || result.Changed != before.Changed))
                throw new InvalidOperationException("Player scene update changed its actual process epoch/lease or zero-mask return.");
            _processSceneUpdate = _processSceneUpdate with { Result = result, Changed = Next() };
        }
        catch (Exception error)
        {
            _processSceneUpdate = _processSceneUpdate with { Failure = error.Message, Changed = Next() }; throw;
        }
    }
    private static void ValidateProcessSceneUpdate(FalloutStandalonePlayerSceneSnapshot saved)
    {
        if (saved.ProcessSceneUpdate is not { } call)
        {
            if (saved.Prelude?.FailedAt is FalloutStandaloneScenePreludeStep.PostProcessContext or FalloutStandaloneScenePreludeStep.TimedActions or FalloutStandaloneScenePreludeStep.PlayerSceneContinuation)
                throw new InvalidDataException("Player animation continuation lost its returned process scene update.");
            return;
        }
        if (saved.Prelude is not { FailedAt: FalloutStandaloneScenePreludeStep.ProcessSceneUpdate or FalloutStandaloneScenePreludeStep.PostProcessContext or FalloutStandaloneScenePreludeStep.TimedActions or FalloutStandaloneScenePreludeStep.PlayerSceneContinuation } prefix ||
            saved.SceneQueueCall is not { Returned: true } queue || call.Main != prefix.Main || call.MainOrdinal != prefix.MainOrdinal ||
            call.Entered <= queue.Changed || call.Changed <= call.Entered || call.Changed > saved.Sequence ||
            (call.Failure is null) != (call.Result is not null) ||
            (prefix.FailedAt is FalloutStandaloneScenePreludeStep.PostProcessContext or FalloutStandaloneScenePreludeStep.TimedActions or FalloutStandaloneScenePreludeStep.PlayerSceneContinuation) != (call.Result is not null) ||
            call.Failure is not null && call.Failure != saved.Failure)
            throw new InvalidDataException("Player process scene update lost its current caller or retained failure.");
        if (call.Result is { } result && (saved.ActorProcessQuery?.Result is not { } before || result.Before != before || !result.Returned ||
            (before.Level == FalloutDetectionProcessLevel.High ? result.Stores != 1 || result.BeforeMask != 0 || result.AfterMask != 0 || result.Changed <= before.Changed :
                result.Stores != 0 || result.BeforeMask is not null || result.AfterMask is not null || result.Changed != before.Changed)))
            throw new InvalidDataException("Player process scene update invented a mask writer, changed its epoch or skipped its actual clear.");
    }
}
