using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutStandalonePostActionsCall(Guid Main, long MainOrdinal, long Entered, long Changed,
    FalloutPlayerPostActionFields Before, FalloutPlayerPostActionFields After,
    FalloutProcessPostActionUpdate? ProcessUpdate, IReadOnlyList<FalloutProcessSceneContextQuery> ContextQueries,
    bool RemovedEight, int FlagStores, FalloutProcessDetectionActionUpdate? DetectionUpdate,
    uint? SceneArgumentBits, int TimerStores, bool Returned, string? Failure,
    IReadOnlyList<FalloutPlayerControllerPostActionsTrace>? ControllerChildren = null,
    FalloutStandaloneSceneReferenceQueryCall? BodyReference = null);

internal sealed partial class FalloutStandalonePlayerSceneState
{
    private FalloutPlayerPostActionFields _postActionFields = FalloutPlayerPostActionFields.Constructed;
    private FalloutStandalonePostActionsCall? _postActionsCall;
    private Func<Func<float>, FalloutProcessPostActionUpdate>? _updatePostActions;
    private Func<Func<float>, FalloutProcessDetectionActionUpdate>? _updateDetectionAction;
    internal void BindPostActions(Func<Func<float>, FalloutProcessPostActionUpdate> update,
        Func<Func<float>, FalloutProcessDetectionActionUpdate> detection)
    {
        RequireIdle(); ArgumentNullException.ThrowIfNull(update); ArgumentNullException.ThrowIfNull(detection);
        if (_updatePostActions is not null || _updateDetectionAction is not null)
            throw new InvalidOperationException("Player post-action continuation already has its actual process owners.");
        _updatePostActions = update; _updateDetectionAction = detection;
    }
    private void UpdatePostActions(Guid main, long ordinal, Func<float>? readScaledTimer)
    {
        var entered = Next(); var fault = _reentry;
        _postActionsCall = new(main, ordinal, entered, entered, _postActionFields, _postActionFields, null, [], false, 0, null, null, 0, false, null);
        try
        {
            var expected = _timedActionsCall?.Query?.Process ?? throw new InvalidOperationException("Post-action update lost its actual condition query.");
            var update = (_updatePostActions ?? throw new NotSupportedException("source-Player-current-process-post-action-update-virtual-unbound"))(ReadScaled);
            Check(); RequirePostActionUpdate(update, expected);
            _postActionsCall = _postActionsCall with { ProcessUpdate = update, Changed = Next() };
            expected = update.Before with { Changed = update.Changed };
            for (var index = 0; index < 2; ++index)
            {
                var query = (_readPostProcessContext ?? throw new NotSupportedException("source-Player-post-action-current-context-query-unbound"))();
                Check();
                if (query.Process != expected) throw new InvalidOperationException("Post-action context changed its actual current process/epoch.");
                _postActionsCall = _postActionsCall with { ContextQueries = _postActionsCall.ContextQueries.Append(query).ToArray(), Changed = Next() };
                if (query.Context is not null) ExecutePostActionControllerContext(main, ordinal, query, index);
                if (index == 0)
                {
                    _timedActions.RemoveFirst(8, null);
                    _postActionsCall = _postActionsCall with { RemovedEight = true, Changed = Next() };
                }
                else
                {
                    _postActionFields = _postActionFields with { ContextFlag = 0 };
                    _postActionsCall = _postActionsCall with { After = _postActionFields, FlagStores = 1, Changed = Next() };
                }
            }
            var detection = (_updateDetectionAction ?? throw new NotSupportedException("source-Player-current-process-detection-action-update-unbound"))(ReadScaled);
            Check(); RequireDetectionActionUpdate(detection, expected);
            _postActionsCall = _postActionsCall with { DetectionUpdate = detection, Changed = Next() };
            if ((_postActionFields.CombatListFlag & 0xff) != 0)
                throw new NotSupportedException("source-Player-combat-list-current-process-consumers-unbound");
            var argument = _clock?.ArgumentBits ?? throw new InvalidOperationException("Post-action timers lost their actual scene call argument.");
            _postActionFields = _postActionFields.UpdateTimers(BitConverter.UInt32BitsToSingle(argument));
            _postActionsCall = _postActionsCall with { After = _postActionFields, SceneArgumentBits = argument, TimerStores = 2, Returned = true, Changed = Next() };
        }
        catch (Exception error)
        {
            _postActionsCall = _postActionsCall with { After = _postActionFields, Failure = error.Message, Changed = Next() }; throw;
        }
        float ReadScaled()
        {
            var seconds = (readScaledTimer ?? throw new NotSupportedException("source-Player-post-action-current-Main-scaled-timer-unbound"))();
            Check(); return seconds;
        }
        void Check()
        {
            RequireThread();
            if (_reentry != fault) throw new InvalidOperationException("Post-action consumer swallowed a Player owner reentry refusal.");
        }
    }
    private static void RequirePostActionUpdate(FalloutProcessPostActionUpdate result, FalloutProcessSceneQuery expected)
    {
        var high = expected.Level == FalloutDetectionProcessLevel.High;
        if (result.Before != expected || !result.Returned || high != (result.BeforeScalars is not null) || high != (result.AfterScalars is not null) ||
            (high ? result.Changed <= expected.Changed : result.Changed != expected.Changed) ||
            (result.BeforeScalars is { } before ? before.SubtractsTimer
                ? result.ScaledTimerBits is not { } timer || result.Stores != 1 || result.AfterScalars != before.Subtract(BitConverter.UInt32BitsToSingle(timer))
                : result.ScaledTimerBits is not null || result.Stores != 2 || result.AfterScalars != before.Publish(
                    result.Light?.ValueBits is { } value ? BitConverter.UInt32BitsToSingle(value) : 0)
                : result.ScaledTimerBits is not null || result.Stores != 0))
            throw new InvalidDataException("Post-action update lost its actual process, separate timer arm or ordered source stores.");
        if (result.Light is { } light && (light.Query != expected || !light.Returned || light.Failure is not null || light.ValueBits is null))
            throw new InvalidDataException("Post-action scalar borrowed an unfinished or foreign source light call.");
    }
    private static void RequireDetectionActionUpdate(FalloutProcessDetectionActionUpdate result, FalloutProcessSceneQuery expected)
    {
        var high = expected.Level == FalloutDetectionProcessLevel.High;
        if (result.Before != expected || !result.Returned || high != (result.BeforeScalars is not null) || high != (result.AfterScalars is not null) ||
            (high ? result.Changed <= expected.Changed : result.Changed != expected.Changed) ||
            (result.BeforeScalars is { } before ? before.SubtractsTimer
                ? result.ScaledTimerBits is not { } timer || result.SettingBits is not null || result.Stores != 1 || result.AfterScalars != before.Subtract(BitConverter.UInt32BitsToSingle(timer))
                : result.SettingBits is not { } setting || result.ScaledTimerBits is not null || result.Stores != 2 || result.AfterScalars != before.Reset(BitConverter.UInt32BitsToSingle(setting))
                : result.ScaledTimerBits is not null || result.SettingBits is not null || result.Stores != 0))
            throw new InvalidDataException("Detection action lost its actual process, distinct setting/reset branch or Main timer subtraction.");
    }
    private static void ValidatePostActions(FalloutStandalonePlayerSceneSnapshot saved)
    {
        if (saved.PostActionFields is null || saved.PostActionFields.CombatListFlag != 0)
            throw new InvalidDataException("Player post-action fields lost their actual constructor.");
        if (saved.PostActionsCall is not { } call)
        {
            if (saved.PostActionFields != FalloutPlayerPostActionFields.Constructed ||
                saved.Prelude?.FailedAt == FalloutStandaloneScenePreludeStep.PlayerSceneContinuation)
                throw new InvalidDataException("Player post-action fields omitted their entered writer.");
            return;
        }
        if (saved.Prelude is not { FailedAt: FalloutStandaloneScenePreludeStep.PlayerSceneContinuation } prefix ||
            saved.TimedActionsCall is not { Returned: true, Query: { } condition } timed ||
            call.Main != prefix.Main || call.MainOrdinal != prefix.MainOrdinal || call.Entered <= timed.Changed ||
            call.Changed <= call.Entered || call.Changed > saved.Sequence || call.Before != FalloutPlayerPostActionFields.Constructed ||
            call.After != saved.PostActionFields || call.ContextQueries is null || call.ContextQueries.Count > 2 ||
            call.Returned == (call.Failure is not null) || call.Failure is not null && call.Failure != saved.Failure ||
            call.FlagStores is < 0 or > 1 || call.TimerStores is not (0 or 2) ||
            (call.ContextQueries.Count > 0 && (call.ContextQueries[0].Context is null ||
                call.ControllerChildren?.Any(child => child.QueryOrdinal == 0 && child.Returned && child.RemoveEight) == true)) != call.RemovedEight ||
            (call.ContextQueries.Count == 2 && (call.ContextQueries[1].Context is null ||
                call.ControllerChildren?.Any(child => child.QueryOrdinal == 1 && child.Returned && child.ClearContextFlag) == true)) != (call.FlagStores == 1) ||
            call.Returned && (call.ProcessUpdate is null || call.ContextQueries.Count != 2 || call.DetectionUpdate is null || call.TimerStores != 2) ||
            call.DetectionUpdate is not null && call.FlagStores != 1 || call.TimerStores != 0 && call.DetectionUpdate is null ||
            call.ContextQueries.Count != 0 && call.ProcessUpdate is null ||
            (call.TimerStores == 2) != (call.SceneArgumentBits is not null) ||
            call.SceneArgumentBits is { } argument && argument != saved.Clock?.ArgumentBits)
            throw new InvalidDataException("Player post-action continuation lost its current caller, field stores or partial failure order.");
        var fields = call.Before with { ContextFlag = call.FlagStores == 1 ? (byte)0 : call.Before.ContextFlag };
        if (call.SceneArgumentBits is { } seconds) fields = fields.UpdateTimers(BitConverter.UInt32BitsToSingle(seconds));
        if (fields != call.After) throw new InvalidDataException("Player post-action fields invented a writer or omitted their actual scene timers.");
        if (call.ProcessUpdate is { } update)
        {
            RequirePostActionUpdate(update, condition.Process);
            var expected = update.Before with { Changed = update.Changed };
            if (call.ContextQueries.Any(query => query.Process != expected) ||
                call.ContextQueries.Select((query, index) => (query, index)).Any(item => item.query.Context is not null &&
                    call.ControllerChildren?.Any(child => child.QueryOrdinal == item.index && child.Returned) != true &&
                    (call.Returned || item.index != call.ContextQueries.Count - 1 || call.Failure is null)))
                throw new InvalidDataException("Post-action context receipt lost its distinct current queries or nonnull refusal.");
            if (call.DetectionUpdate is { } detection) RequireDetectionActionUpdate(detection, expected);
        }
        ValidatePostActionControllerChildren(saved, call);
    }
}
