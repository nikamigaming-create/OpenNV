using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutStandaloneTimedActionsCall(Guid Main, long MainOrdinal, long Entered, long Changed,
    FalloutPlayerTimedActionsSnapshot Before, FalloutPlayerTimedActionsSnapshot After,
    FalloutProcessPlayerActionQuery? Query, string? Operation, IReadOnlyList<Guid> Expired,
    uint? ScaledTimerBits, int TimerStores, bool Returned, string? Failure);

internal sealed partial class FalloutStandalonePlayerSceneState
{
    private readonly FalloutPlayerTimedActions _timedActions = new();
    private FalloutStandaloneTimedActionsCall? _timedActionsCall;
    private Func<FalloutProcessPlayerActionQuery>? _readTimedActionCondition;
    private byte _timedActionsSuppressed;
    internal void BindTimedActionCondition(Func<FalloutProcessPlayerActionQuery> read)
    {
        RequireIdle(); ArgumentNullException.ThrowIfNull(read);
        if (_readTimedActionCondition is not null) throw new InvalidOperationException("Player timed actions already have their actual process query.");
        _readTimedActionCondition = read;
    }
    private void UpdateTimedActions(Guid main, long ordinal, Func<float>? readScaledTimer)
    {
        var entered = Next(); var fault = _reentry; var before = _timedActions.Capture();
        _timedActionsCall = new(main, ordinal, entered, entered, before, before, null, null, [], null, 0, false, null);
        try
        {
            var update = _processSceneUpdate?.Result ?? throw new InvalidOperationException("Timed actions lost their actual process update.");
            var query = (_readTimedActionCondition ?? throw new NotSupportedException("source-Player-timed-action-current-process-condition-unbound"))();
            Check();
            if (query.Process != update.Before with { Changed = update.Changed } ||
                (query.Condition == 0) != (query.SecondCondition is null))
                throw new InvalidOperationException("Player timed actions changed process/epoch or queried the second condition on the false arm.");
            _timedActionsCall = _timedActionsCall with { Query = query, Changed = Next() };
            string operation;
            if (query.Condition != 0 && query.SecondCondition != 0)
            {
                if (_timedActions.Find(9) is null)
                { _timedActions.Store(9, 0x41a00000, null, _timedActionsSuppressed); operation = "store-nine"; }
                else operation = "retain-nine";
            }
            else
            { _timedActions.RemoveFirst(9, null); operation = "remove-first-nine"; }
            _timedActionsCall = _timedActionsCall with { Operation = operation, After = _timedActions.Capture(), Changed = Next() };
            var expired = _timedActions.Expire();
            _timedActionsCall = _timedActionsCall with { Expired = expired, After = _timedActions.Capture(), Changed = Next() };
            if (_timedActions.Capture().Entries.Count != 0)
            {
                var scaled = (readScaledTimer ?? throw new NotSupportedException("source-Player-timed-action-current-Main-scaled-timer-unbound"))();
                Check();
                _timedActionsCall = _timedActionsCall with { ScaledTimerBits = BitConverter.SingleToUInt32Bits(scaled), Changed = Next() };
                var stores = _timedActions.Subtract(scaled);
                _timedActionsCall = _timedActionsCall with { TimerStores = stores, After = _timedActions.Capture(), Changed = Next() };
            }
            _timedActionsCall = _timedActionsCall with { Returned = true, Changed = Next() };
        }
        catch (Exception error)
        {
            _timedActionsCall = _timedActionsCall with { After = _timedActions.Capture(), Failure = error.Message, Changed = Next() }; throw;
        }
        void Check()
        {
            RequireThread();
            if (_reentry != fault) throw new InvalidOperationException("Timed action query swallowed a Player owner reentry refusal.");
        }
    }
    private static void ValidateTimedActions(FalloutStandalonePlayerSceneSnapshot saved)
    {
        if (saved.TimedActions is null || saved.TimedActionsSuppressed != 0)
            throw new InvalidDataException("Player timed actions lost their independent constructor fields.");
        FalloutPlayerTimedActions.Validate(saved.TimedActions);
        if (saved.TimedActionsCall is not { } call)
        {
            if (saved.TimedActions.Allocated || saved.TimedActions.Entries.Count != 0 ||
                saved.Prelude?.FailedAt == FalloutStandaloneScenePreludeStep.PlayerSceneContinuation)
                throw new InvalidDataException("Player timed action list has no actual returned writer.");
            return;
        }
        if (saved.Prelude is not { FailedAt: FalloutStandaloneScenePreludeStep.TimedActions or FalloutStandaloneScenePreludeStep.PlayerSceneContinuation } prefix ||
            saved.PostProcessContext is not { Returned: true } context || call.Main != prefix.Main || call.MainOrdinal != prefix.MainOrdinal ||
            call.Entered <= context.Changed || call.Changed <= call.Entered || call.Changed > saved.Sequence ||
            call.Before is null || call.After is null || call.Expired is null || call.Returned == (call.Failure is not null) ||
            (prefix.FailedAt == FalloutStandaloneScenePreludeStep.PlayerSceneContinuation) != call.Returned ||
            call.Failure is not null && call.Failure != saved.Failure ||
            call.After.Allocated != saved.TimedActions.Allocated || !call.After.Entries.SequenceEqual(saved.TimedActions.Entries))
            throw new InvalidDataException("Player timed actions lost their ordered child/failure history.");
        FalloutPlayerTimedActions.Validate(call.Before); FalloutPlayerTimedActions.Validate(call.After);
        // Other producers and nonzero condition writers are independent. The
        // implemented startup path must not invent their history on restore.
        if (call.Before.Allocated || call.Before.Entries.Count != 0 || call.After.Allocated || call.After.Entries.Count != 0 ||
            call.Expired.Count != 0 || call.ScaledTimerBits is not null || call.TimerStores != 0 ||
            call.Query is { } query && (saved.ProcessSceneUpdate?.Result is not { } process ||
                query.Process != process.Before with { Changed = process.Changed } || query.Condition != 0 || query.SecondCondition is not null) ||
            call.Operation is not (null or "remove-first-nine") || call.Returned && (call.Query is null || call.Operation is null))
            throw new InvalidDataException("Player timed actions admitted an unowned condition/list producer or omitted its real empty-list branch.");
    }
}
