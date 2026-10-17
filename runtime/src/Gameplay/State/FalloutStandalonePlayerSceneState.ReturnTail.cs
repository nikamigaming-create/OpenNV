namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutStandalonePlayerSceneState
{
    private FalloutPlayerReturnTailState? _lateReturnTail;
    private FalloutPlayerReturnTailSnapshot? _historicalReturnTail;
    private Func<FalloutPlayerLateFrameInvocation, FalloutPlayerReturnTailCall>? _updateReturnTail;
    internal void BindLateReturnTail(FalloutPlayerReturnTailState tail,
        Func<FalloutPlayerLateFrameInvocation, FalloutPlayerReturnTailCall> update)
    {
        RequireIdle(); ArgumentNullException.ThrowIfNull(tail); ArgumentNullException.ThrowIfNull(update);
        if (_lateReturnTail is not null || tail.Process != _process || tail.Constructor != _constructor || tail.Stack != _stack)
            throw new InvalidOperationException("Player return updater changed its actual Scene lifetime.");
        _lateReturnTail = tail; _updateReturnTail = update;
    }
    internal void RequireReturnTailInvocation(FalloutPlayerLateFrameInvocation frame)
    {
        RequireThread();
        if (!_writing || _retired || _lateFrameCall is not { Step: FalloutPlayerLateFrameStep.Tail, Failure: null } call ||
            call.Invocation != frame || call.MoverDelta is not { Returned: true, Failure: null } || frame.PlayerProcess != _process ||
            frame.PlayerConstructor != _constructor || frame.ThreadId != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Player return updater lost its reached source tail or returned mover child.");
    }
}
