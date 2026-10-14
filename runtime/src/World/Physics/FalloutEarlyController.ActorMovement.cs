using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Physics;

internal sealed record FalloutControllerActorMovementInvocation(Guid Call, FalloutPlayerActorMovementInvocation Parent,
    FalloutProcessSceneContextQuery Context, Guid RuntimeProcess);
internal sealed record FalloutControllerMovementFlagStore(FalloutControllerActorMovementInvocation Invocation,
    Guid Controller, long Changed, uint Before, uint After, bool Disabled, bool Returned = true, string? Failure = null);
internal sealed record FalloutControllerMovementFlagsRead(FalloutControllerActorMovementInvocation Invocation,
    Guid Controller, long Sequence, uint Flags);
internal sealed record FalloutControllerReferencePositionRead(FalloutControllerActorMovementInvocation Invocation,
    Guid Controller, long Sequence, FalloutCharacterControllerProxyTransform Proxy,
    FalloutPhysicalVector3 Offset, FalloutPhysicalVector3 SourcePosition);
internal sealed record FalloutControllerControlledMovementCall(FalloutControllerActorMovementInvocation Invocation,
    Guid Controller, long Entered, long Changed, byte State, uint? Phase, bool PhaseRead, bool Returned, string? Failure);
internal sealed record FalloutControllerMovementAttachmentsRead(FalloutControllerActorMovementInvocation Invocation,
    Guid Controller, long Sequence, Guid? Head, Guid? Tail, bool Returned);
internal sealed record FalloutControllerActorMovementSnapshot(long Sequence, long FlagCalls, long ControlledCalls,
    uint ControlledPhase, string? PhaseBoundary, FalloutControllerMovementFlagStore? FlagStore,
    FalloutControllerControlledMovementCall? Controlled, string? Failure);

internal sealed partial class FalloutEarlyController
{
    private uint _controlledMovementPhase;
    private string? _controlledMovementPhaseBoundary, _actorMovementFailure;
    private long _actorMovementSequence, _actorMovementFlagsCalls, _controlledMovementCalls;
    private FalloutControllerMovementFlagStore? _actorMovementFlagStore;
    private FalloutControllerControlledMovementCall? _controlledMovementCall;
    internal string? ActorMovementSaveBlocker => _actorMovementFailure ?? _controlledMovementPhaseBoundary;

    internal FalloutControllerMovementFlagStore StoreActorMovementDisabledFlag(FalloutPlayerActorMovementSource source,
        FalloutControllerActorMovementInvocation invocation, bool disabled,
        Action<FalloutControllerActorMovementInvocation> requireActual)
    {
        RequireActorMovement(source, invocation, requireActual); var before = _flags; var fault = _fault; _busy = true;
        try
        {
            _flags = disabled ? before | FalloutPlayerActorMovementSource.ControllerDisabledMovementFlag :
                before & ~FalloutPlayerActorMovementSource.ControllerDisabledMovementFlag;
            ++_actorMovementFlagsCalls;
            _actorMovementFlagStore = new(invocation, Identity, checked(++_actorMovementSequence), before, _flags, disabled, false);
            RequireActorMovementAssociation(source, invocation, requireActual); _factory.RequireLifetime();
            if (_fault != fault) throw new InvalidOperationException("Controller movement flag store swallowed source reentry.");
            _actorMovementFlagStore = _actorMovementFlagStore with { Returned = true }; return _actorMovementFlagStore;
        }
        catch (Exception error)
        {
            _actorMovementFailure ??= error.Message; _factory.RetainFailure(_actorMovementFailure);
            if (_actorMovementFlagStore is not null) _actorMovementFlagStore = _actorMovementFlagStore with { Failure = error.Message };
            throw;
        }
        finally { _busy = false; }
    }
    internal FalloutControllerControlledMovementCall UpdateControlledActorMovement(FalloutPlayerActorMovementSource source,
        FalloutControllerActorMovementInvocation invocation, Action<FalloutControllerActorMovementInvocation> requireActual)
    {
        RequireActorMovement(source, invocation, requireActual);
        if (_controlledMovementCall is { } previous && (!previous.Returned || previous.Invocation.Parent.Frame.Caller.Main == invocation.Parent.Frame.Caller.Main))
            throw new InvalidOperationException("Controlled movement replayed a returned or failed actual child.");
        var entered = checked(++_actorMovementSequence); var fault = _fault; ++_controlledMovementCalls;
        _busy = true; _controlledMovementCall = new(invocation, Identity, entered, entered, _state.Snapshot.Current, null, false, false, null);
        try
        {
            Check();
            if (_state.Snapshot.Current == 2)
                throw new NotSupportedException("source-controller-controlled-state-two-current-zero-vector-and-delta-child-unbound");
            if (_controlledMovementPhaseBoundary is { } boundary) throw new NotSupportedException(boundary);
            _controlledMovementCall = _controlledMovementCall with { Phase = _controlledMovementPhase, PhaseRead = true };
            Check();
            if (_controlledMovementPhase == 2)
                throw new NotSupportedException("source-controller-controlled-phase-two-current-height-payload-and-transform-writer-unbound");
            _controlledMovementCall = _controlledMovementCall with { Returned = true, Changed = checked(++_actorMovementSequence) };
            return _controlledMovementCall;
        }
        catch (Exception error)
        {
            _actorMovementFailure ??= error.Message; _factory.RetainFailure(_actorMovementFailure);
            _controlledMovementCall = _controlledMovementCall with { Failure = error.Message, Changed = checked(++_actorMovementSequence) };
            throw;
        }
        finally { _busy = false; }
        void Check()
        {
            RequireActorMovementAssociation(source, invocation, requireActual); _factory.RequireLifetime();
            if (_fault != fault) throw new InvalidOperationException("Controlled movement swallowed a real controller reentry refusal.");
        }
    }
    internal FalloutControllerMovementFlagsRead ReadActorMovementFlags(FalloutPlayerActorMovementSource source,
        FalloutControllerActorMovementInvocation invocation, Action<FalloutControllerActorMovementInvocation> requireActual)
    {
        RequireActorMovement(source, invocation, requireActual);
        return new(invocation, Identity, _actorMovementSequence + _moverDeltaSequence + _sequence, _flags);
    }
    internal FalloutControllerReferencePositionRead ReadActorMovementReferencePosition(FalloutPlayerActorMovementSource source,
        FalloutControllerActorMovementInvocation invocation, Action<FalloutControllerActorMovementInvocation> requireActual)
    {
        RequireActorMovement(source, invocation, requireActual); var transform = Proxy.Transform;
        var position = _position.ReadReferencePositionBits(transform);
        RequireActorMovement(source, invocation, requireActual);
        return new(invocation, Identity, _actorMovementSequence + _moverDeltaSequence + _sequence, transform,
            Descriptor.Player.RetainedOffset, position);
    }
    internal FalloutControllerMovementAttachmentsRead ReadActorMovementAttachments(FalloutPlayerActorMovementSource source,
        FalloutControllerActorMovementInvocation invocation, Action<FalloutControllerActorMovementInvocation> requireActual)
    {
        RequireActorMovement(source, invocation, requireActual);
        if (_moverDeltaFields.QueueBoundary is { } boundary) throw new NotSupportedException(boundary);
        var read = new FalloutControllerMovementAttachmentsRead(invocation, Identity, _moverDeltaSequence,
            _moverDeltaFields.QueueHead, _moverDeltaFields.QueueTail, false);
        if (read.Head is not null || read.Tail is not null)
            throw new NotSupportedException("source-controller-movement-nonempty-reference-attachment-list-payload-unbound");
        RequireActorMovement(source, invocation, requireActual); return read with { Returned = true };
    }
    internal void ObserveUnownedControlledMovementPhaseWriter(string boundary)
    { RequireCurrent(); ArgumentException.ThrowIfNullOrWhiteSpace(boundary); _controlledMovementPhaseBoundary = boundary; }
    private void RequireActorMovement(FalloutPlayerActorMovementSource source, FalloutControllerActorMovementInvocation invocation,
        Action<FalloutControllerActorMovementInvocation> requireActual)
    {
        RequireCurrent(); RequireActorMovementAssociation(source, invocation, requireActual);
        if (_actorMovementFailure is { } failure) throw new NotSupportedException(failure);
    }
    private void RequireActorMovementAssociation(FalloutPlayerActorMovementSource source,
        FalloutControllerActorMovementInvocation invocation, Action<FalloutControllerActorMovementInvocation> requireActual)
    {
        source.Validate(); requireActual(invocation); var frame = invocation.Parent.Frame; var context = invocation.Context;
        if (source.Mover.Controller.Identity != Lineage.Source || invocation.Call == Guid.Empty ||
            frame.Common.Actor != Lineage.Actor || frame.Common.Process != Lineage.CommonProcess || frame.Common.Epoch != Lineage.Epoch ||
            context.Context != Identity || context.Process.Actor != Lineage.Actor || context.Process.Process != Lineage.CommonProcess ||
            context.Process.Epoch != Lineage.Epoch || context.Process.GameplayOwnership != frame.Common.GameplayOwnership ||
            invocation.RuntimeProcess != Lineage.RuntimeProcess || frame.PlayerConstructor != Lineage.Constructor ||
            frame.ThreadId != Lineage.Thread || frame.ThreadId != Environment.CurrentManagedThreadId)
            throw new InvalidDataException("Actor movement lost its retained same-controller/Common/Runtime/current frame association.");
    }
    internal FalloutControllerActorMovementSnapshot CaptureActorMovement() => new(_actorMovementSequence, _actorMovementFlagsCalls,
        _controlledMovementCalls, _controlledMovementPhase, _controlledMovementPhaseBoundary, _actorMovementFlagStore, _controlledMovementCall, _actorMovementFailure);
    internal void RestoreActorMovement(FalloutControllerActorMovementSnapshot? saved)
    {
        if (saved is null) return; ValidateActorMovement(saved);
        _actorMovementSequence = saved.Sequence; _actorMovementFlagsCalls = saved.FlagCalls; _controlledMovementCalls = saved.ControlledCalls;
        _controlledMovementPhase = saved.ControlledPhase; _controlledMovementPhaseBoundary = saved.PhaseBoundary;
        _actorMovementFlagStore = saved.FlagStore; _controlledMovementCall = saved.Controlled; _actorMovementFailure = saved.Failure;
    }
    internal static void ValidateActorMovement(FalloutControllerActorMovementSnapshot saved)
    {
        if (saved.Sequence < 0 || saved.FlagCalls < 0 || saved.ControlledCalls < 0 || saved.ControlledPhase != 0 ||
            saved.Sequence != saved.FlagCalls + saved.ControlledCalls * 2 || (saved.FlagCalls == 0) != (saved.FlagStore is null) ||
            (saved.ControlledCalls == 0) != (saved.Controlled is null) || saved.PhaseBoundary is { Length: 0 } || saved.Failure is { Length: 0 })
            throw new InvalidDataException("Controller Actor movement changed its bounded real phase/flag/call fields.");
        if (saved.FlagStore is { } flag && (flag.Controller == Guid.Empty || flag.Invocation.Call == Guid.Empty ||
            flag.Changed < 1 || flag.Changed > saved.Sequence || flag.Returned != (flag.Failure is null) ||
            flag.Failure is not null && flag.Failure != saved.Failure || flag.After != (flag.Disabled ?
                flag.Before | FalloutPlayerActorMovementSource.ControllerDisabledMovementFlag :
                flag.Before & ~FalloutPlayerActorMovementSource.ControllerDisabledMovementFlag)))
            throw new InvalidDataException("Controller movement flag differs from its actual source bit store.");
        if (saved.Controlled is { } call && (call.Controller == Guid.Empty || call.Invocation.Call == Guid.Empty ||
            call.Entered < 1 || call.Changed != call.Entered + 1 || call.Changed > saved.Sequence || call.Returned != (call.Failure is null) ||
            call.Returned && (!call.PhaseRead || call.State == 2 || call.Phase != 0) || call.Failure != saved.Failure))
            throw new InvalidDataException("Controlled movement omitted its actual state/phase predicates or fabricated a returned child.");
    }
}
