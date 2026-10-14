using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutStandalonePlayerSceneState
{
    private readonly FalloutPlayerInhibitionState _inhibition;
    private readonly FalloutPlayerFollowingTimerFlagState _followingTimerFlag;
    internal FalloutPlayerInhibitionState PlayerInhibition => _inhibition;

    internal void PublishFollowingFieldInput(FalloutDirectInputState input)
    {
        RequireIdle(); ArgumentNullException.ThrowIfNull(input);
        if (_inhibition.HasActualInput) _inhibition.RequireActualInput(input);
        else _inhibition.BindActualInput(input);
        if (_followingTimerFlag.HasActualInput) _followingTimerFlag.RequireActualInput(input);
        else _followingTimerFlag.BindActualInput(input);
        _actorMover.BindActualInput(input);
    }

    private void RequireInhibitionInvocation(FalloutPlayerInhibitionConsumer consumer, FalloutPlayerSourceCaller caller)
    {
        RequireThread(); ObjectDisposedException.ThrowIf(_retired, this);
        if (!_writing || _reading || _clock is not { Step: FalloutStandaloneSceneClockStep.VirtualEntered } clock ||
            clock.Main != caller.Main || clock.MainOrdinal != caller.Ordinal ||
            _threadContextEntry is not { ScopeEntered: true, InitializerReturned: true } context ||
            context.Process != _process || context.Main != caller.Main || context.MainOrdinal != caller.Ordinal ||
            context.Thread != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Player inhibition read escaped its actual entered Main/thread caller.");
        if (consumer == FalloutPlayerInhibitionConsumer.Movement)
        {
            if (_prelude?.Invocation != caller.Invocation || _movementCall is not { Step: "movement-control-child", Failure: null } movement ||
                movement.Main != caller.Main || movement.MainOrdinal != caller.Ordinal)
                throw new InvalidOperationException("Movement inhibition lost its actual Prelude invocation.");
            return;
        }
        if (consumer != FalloutPlayerInhibitionConsumer.Following)
            throw new InvalidDataException("Player inhibition selected an unknown source consumer.");
        RequireFollowingContinuationInvocation(caller);
    }

    internal FalloutPlayerInhibitionPredicate ReadFollowingInhibition(FalloutPlayerSourceCaller caller,
        Action<FalloutPlayerInhibitionRead> observe) =>
        _inhibition.ReadCurrent(FalloutPlayerInhibitionConsumer.Following, caller, observe);

    internal FalloutPlayerFollowingTimerFlagRead ReadFollowingTimerByte(FalloutPlayerSourceCaller caller) =>
        _followingTimerFlag.ReadCurrent(caller);

    private static void ValidateInhibitionOwners(FalloutStandalonePlayerSceneSnapshot saved)
    {
        var source = FalloutPlayerFollowingContinuationSource.Reviewed(FalloutPlayerMovementControlSource.Reviewed(saved.Source.Main));
        var inhibition = saved.Inhibition ?? throw new InvalidDataException("Player scene omitted its actual shared inhibition owner.");
        var timer = saved.FollowingTimerFlag ?? throw new InvalidDataException("Player scene omitted its independent following timer byte.");
        FalloutPlayerInhibitionState.Validate(inhibition, source, saved.Stack);
        FalloutPlayerFollowingTimerFlagState.Validate(timer, source, saved.Stack);
        if (inhibition.Process != saved.CapturedProcess || timer.Process != saved.CapturedProcess ||
            inhibition.Constructor != saved.Constructor || timer.Constructor != saved.Constructor)
            throw new InvalidDataException("Player field owners changed their actual constructor/process association.");
        if (saved.Prelude is { Invocation: var invocation } && invocation == Guid.Empty)
            throw new InvalidDataException("Player Prelude lost its entered source invocation nonce.");
    }
}
