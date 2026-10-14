using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutStandalonePlayerSceneState
{
    private FalloutPlayerFollowingContinuationCall? _followingContinuation;
    private Action<FalloutPlayerSourceCaller, Action<FalloutPlayerFollowingContinuationCall>>? _updateFollowingContinuation;
    private Guid? _enteredFollowingContinuation;

    internal void BindPlayerFollowingContinuation(FalloutPlayerFollowingContinuationSource source,
        Action<FalloutPlayerSourceCaller, Action<FalloutPlayerFollowingContinuationCall>> update)
    {
        RequireIdle(); source.Validate(); ArgumentNullException.ThrowIfNull(update);
        if (source.Controls.Main != Source.Main || _updateFollowingContinuation is not null)
            throw new InvalidOperationException("Following Player services changed their actual source binding.");
        _updateFollowingContinuation = update;
    }

    internal void RequireFollowingContinuationInvocation(FalloutPlayerSourceCaller caller)
    {
        RequireOpacityInvocation(caller.Main, caller.Ordinal);
        if (_enteredFollowingContinuation != caller.Invocation ||
            _weaponContinuation is not { Returned: true, Failure: null } weapon ||
            weapon.PlayerProcess != _process || weapon.Main != caller.Main || weapon.MainOrdinal != caller.Ordinal ||
            weapon.Invocation != caller.Invocation ||
            _followingServicesCall is not { Step: FalloutStandaloneFollowingServicesStep.Returned } following ||
            following.Main != caller.Main || following.MainOrdinal != caller.Ordinal || following.Invocation != caller.Invocation)
            throw new InvalidOperationException("Following Player services require their actual returned weapon caller.");
    }

    private void UpdatePlayerFollowingContinuation(Guid main, long ordinal)
    {
        var update = _updateFollowingContinuation ??
            throw new NotSupportedException("source-Player-following-weapon-frame-services-unbound");
        var weapon = _weaponContinuation ?? throw new InvalidOperationException("Following services lost their weapon prefix.");
        var caller = new FalloutPlayerSourceCaller(main, ordinal, weapon.Invocation);
        _enteredFollowingContinuation = caller.Invocation;
        try
        {
            RequireFollowingContinuationInvocation(caller);
            update(caller, Observe);
        }
        finally { _enteredFollowingContinuation = null; }

        void Observe(FalloutPlayerFollowingContinuationCall call)
        {
            RequireFollowingContinuationInvocation(caller);
            if (call.Caller != caller || call.PlayerProcess != _process || call.Stack != _stack)
                throw new InvalidDataException("Following Player child returned another source/process invocation.");
            _followingContinuation = call; Next();
        }
    }

    private static void ValidateFollowingContinuation(FalloutStandalonePlayerSceneSnapshot saved)
    {
        if (saved.FollowingContinuation is not { } call) return;
        var source = FalloutPlayerFollowingContinuationSource.Reviewed(FalloutPlayerMovementControlSource.Reviewed(saved.Source.Main));
        FalloutPlayerFollowingContinuation.Validate(call, source, saved.Stack);
        if (!ChildBelongsToParentProcessLineage(saved, call.PlayerProcess) ||
            saved.WeaponContinuation is not { Returned: true, Failure: null } weapon ||
            call.PlayerProcess != weapon.PlayerProcess || call.Caller.Main != weapon.Main ||
            call.Caller.Ordinal != weapon.MainOrdinal || call.Caller.Invocation != weapon.Invocation)
            throw new InvalidDataException("Following Player history lost its actual returned weapon/process caller.");
        var inhibition = saved.Inhibition ?? throw new InvalidDataException("Following history omitted its shared inhibition field owner.");
        var history = new FalloutPlayerInhibitionReadHistory(inhibition, source, saved.Stack);
        for (var index = 0; index < call.InhibitionReads.Count; index++)
        {
            var read = call.InhibitionReads[index];
            history.Require(read, FalloutPlayerInhibitionConsumer.Following, call.Caller, call.PlayerProcess, (FalloutPlayerInhibitionLane)index);
            if (index > 0 && (read.Factory != call.InhibitionReads[0].Factory || read.Revision != call.InhibitionReads[0].Revision))
                throw new InvalidDataException("Following predicate changed its actual atomic inhibition factory or revision.");
        }
        if (call.Timer is { } timer)
        {
            var owner = saved.FollowingTimerFlag ?? throw new InvalidDataException("Following history omitted its timer byte owner.");
            FalloutPlayerFollowingTimerFlagState.RequireHistoricalPublication(owner, source, saved.Stack, timer.Process, timer.Factory);
            if (timer.Revision > owner.Sequence)
                throw new InvalidDataException("Following history retained a future timer byte revision.");
        }
        foreach (var query in call.ReadyItemQueries ?? [])
        {
            if (saved.InputManagerQuery is not { Returned: true } input || query.Factory != input.Factory ||
                query.Provider != input.Provider || query.Window != input.Window || query.Invocation.PlayerProcess != input.Process)
                throw new InvalidDataException("Ready Item history borrowed another actual input manager factory.");
        }
    }
}
