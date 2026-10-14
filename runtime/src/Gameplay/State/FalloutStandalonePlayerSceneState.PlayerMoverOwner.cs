using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutStandalonePlayerSceneState
{
    private FalloutPlayerMoverDeltaState? _lateMoverDelta;
    private FalloutPlayerActorMovementState? _actorMovement;
    private FalloutPlayerMoverDeltaSnapshot? _historicalMoverDelta;
    private FalloutPlayerActorMovementSnapshot? _historicalActorMovement;
    private Func<FalloutPlayerLateFrameInvocation, FalloutPlayerMoverDeltaCall>? _updateLateMoverDelta;

    internal FalloutPlayerReferenceOrientationOwner RequirePlayerReferenceOrientation() =>
        _referenceOrientation ?? throw new NotSupportedException("source-Player-current-reference-orientation-owner-unbound");

    internal void BindLateMoverDelta(FalloutPlayerMoverDeltaState mover, FalloutPlayerActorMovementState actor,
        Func<FalloutPlayerLateFrameInvocation, FalloutPlayerMoverDeltaCall> update)
    {
        RequireIdle(); ArgumentNullException.ThrowIfNull(mover); ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(update);
        if (_lateMoverDelta is not null || _actorMovement is not null || mover.Process != _process ||
            actor.Process != _process || mover.Constructor != _constructor || actor.Constructor != _constructor ||
            mover.Stack != _stack || actor.Stack != _stack || actor.Source.Mover != mover.Source ||
            mover.Source.Controller.Fields.Controls.Main != Source.Main)
            throw new InvalidOperationException("Late mover replaced its actual Player/Actor source owners.");
        _lateMoverDelta = mover; _actorMovement = actor; _updateLateMoverDelta = update;
    }

    internal void RequireLateMoverDeltaInvocation(FalloutPlayerLateFrameInvocation invocation)
    {
        RequireLateFrameCaller(invocation.Caller);
        if (_lateFrameCall is not { Step: FalloutPlayerLateFrameStep.MoverDelta, Failure: null } late ||
            late.Invocation != invocation || invocation.ThreadId != Environment.CurrentManagedThreadId ||
            _prelude is not { Step: FalloutStandaloneScenePreludeStep.PlayerSceneContinuation } prelude ||
            invocation.Prelude != prelude.Invocation || invocation.Caller.Main != prelude.Main ||
            invocation.Caller.Ordinal != prelude.MainOrdinal ||
            _clock is not { Step: FalloutStandaloneSceneClockStep.VirtualEntered, ArgumentBits: { } argument } ||
            invocation.ArgumentBits != argument || _actorProcessQuery?.Result != invocation.Common)
            throw new InvalidOperationException("Mover delta left its actual entered Player/Main/Common/clock scope.");
    }

    private static void ValidatePlayerMoverOwners(FalloutStandalonePlayerSceneSnapshot saved)
    {
        if (saved.MoverDelta is { } mover)
        {
            FalloutPlayerMoverDeltaState.Validate(mover);
            if (mover.Stack != saved.Stack || mover.Source.Controller.Fields.Controls.Main != saved.Source.Main ||
                !ChildBelongsToParentProcessLineage(saved, mover.PlayerProcess) ||
                mover.PlayerProcess == saved.CapturedProcess && mover.Constructor != saved.Constructor ||
                !SameBoundedLateChild(mover.LastCall, saved.LateFrame?.LastCall?.MoverDelta))
                throw new InvalidDataException("Mover delta history lost its actual Player/source/late-frame caller.");
        }
        else if (saved.LateFrame?.LastCall?.MoverDelta is not null)
            throw new InvalidDataException("Late frame omitted its actual mover owner.");
        if (saved.ActorMovement is { } actor)
        {
            FalloutPlayerActorMovementState.Validate(actor);
            if (saved.MoverDelta is not { } parent || actor.Source.Mover != parent.Source ||
                actor.Stack != saved.Stack || !ChildBelongsToParentProcessLineage(saved, actor.PlayerProcess) ||
                actor.PlayerProcess == saved.CapturedProcess && actor.Constructor != saved.Constructor ||
                !SameBoundedLateChild(actor.LastCall, parent.LastCall?.ActorMovement))
                throw new InvalidDataException("Actor movement history lost its actual entered mover/source caller.");
        }
        else if (saved.MoverDelta?.LastCall?.ActorMovement is not null)
            throw new InvalidDataException("Mover delta omitted its actual Actor movement owner.");
    }
}
