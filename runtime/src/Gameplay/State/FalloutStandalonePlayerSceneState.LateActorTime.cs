using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutStandalonePlayerSceneState
{
    private FalloutPlayerLateActorTimeState? _lateActorTime;
    private FalloutPlayerLateActorSnapshot? _historicalLateActorTime;

    internal void BindLateActorTime(FalloutPlayerLateActorTimeSource source,
        FalloutProcessSceneQuery current, FalloutActorProcessCommonState common,
        FalloutActorProcessRuntimeState runtime, FalloutActorUpdateState updates,
        FalloutReferenceScriptExtraInitializer reference, FalloutGameTime calendar,
        FalloutSourceFrameTimerState timer, FalloutNumericIniSettings settings, Action requireLifetime)
    {
        RequireIdle();
        if (_lateActorTime is not null || source.Following.Controls.Main != Source.Main)
            throw new InvalidOperationException("Player Actor-time changed its source or replayed its constructor binding.");
        _lateActorTime = new(source, _stack, _process, _constructor, current, common, runtime,
            updates, reference, calendar, timer, settings, () => _sceneScalars,
            (value, committed) => { _sceneScalars = value; Next(); committed(); },
            () => _clockKind, RequireLateActorTimeInvocation, requireLifetime, _historicalLateActorTime);
    }

    private void RequireLateActorTimeInvocation(FalloutPlayerLateFrameInvocation invocation)
    {
        RequireFollowingContinuationInvocation(invocation.Caller);
        if (_lateFrameCall is not { Step: FalloutPlayerLateFrameStep.ActorTime, Failure: null } late ||
            late.Invocation != invocation || invocation.PlayerProcess != _process || invocation.PlayerConstructor != _constructor ||
            invocation.ThreadId != Environment.CurrentManagedThreadId ||
            _prelude is not { Step: FalloutStandaloneScenePreludeStep.PlayerSceneContinuation } prelude ||
            invocation.Prelude != prelude.Invocation || invocation.Caller.Main != prelude.Main ||
            invocation.Caller.Ordinal != prelude.MainOrdinal ||
            _clock is not { Step: FalloutStandaloneSceneClockStep.VirtualEntered, ArgumentBits: { } argument } ||
            invocation.ArgumentBits != argument || _actorProcessQuery?.Result != invocation.Common)
            throw new InvalidOperationException("Actor-time left the actual entered Player/Main, Common, clock or constructor.");
    }

    private static void ValidateLateActorTime(FalloutStandalonePlayerSceneSnapshot saved)
    {
        if (saved.LateActorTime is not { } child) return;
        FalloutPlayerLateActorTimeState.Validate(child);
        if (child.Source.Following.Controls.Main != saved.Source.Main || child.Stack != saved.Stack ||
            !ChildBelongsToParentProcessLineage(saved, child.PlayerProcess) ||
            child.PlayerProcess == saved.CapturedProcess && child.PlayerConstructor != saved.Constructor)
            throw new InvalidDataException("Actor-time snapshot borrowed another actual Player constructor or source.");
        if (child.LastCall is not { } call) return;
        if (saved.FollowingContinuation is not { } following || call.Invocation.Caller != following.Caller ||
            call.Invocation.PlayerProcess != following.PlayerProcess || saved.Prelude is not { } prelude ||
            call.Invocation.Prelude != prelude.Invocation || saved.Clock?.ArgumentBits != call.Invocation.ArgumentBits)
            throw new InvalidDataException("Actor-time history lost its actual entered following frame and clock.");
    }
}
