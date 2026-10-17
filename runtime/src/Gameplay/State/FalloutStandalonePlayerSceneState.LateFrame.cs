using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;
using System.Text.Json;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutStandalonePlayerSceneState
{
    internal const string LateViewBoundary = "source-Player-late-requested-view-child-unbound";
    internal const string LateTimerBoundary = "source-Player-late-inhibition-timer-raw-state-children-unbound";
    internal const string LateInhibitionBoundary = "source-Player-late-inhibition-timer-reset-package-children-unbound";
    internal const string LateMoverDeltaBoundary = "source-Player-late-mover-delta-virtual-child-unbound";
    private FalloutPlayerLateFramePrefixSource? _lateFrameSource;
    private FalloutPlayerLateFrameSnapshot? _historicalLateFrame;
    private FalloutPlayerLateFrameCall? _lateFrameCall;
    private byte _cameraViewRequest;
    // The original shared inhibition clock has its own loader symbol. It is
    // independent of the POV camera scalar, even when both happen to be zero.
    private uint _lateInhibitionTimerBits;
    private bool _lateInhibitionTimerAvailable = true;
    private string? _lateInhibitionTimerWriterBoundary;

    internal void BindLateFramePrefix(FalloutPlayerLateFramePrefixSource source, FalloutCombatActorIdentity actor)
    {
        RequireIdle(); source.Validate();
        if (_lateFrameSource is not null || source.Following.Controls.Main != Source.Main)
            throw new InvalidOperationException("Late Player prefix replaced its actual source binding.");
        _lateFrameSource = source;
        _cameraViewRequest = FalloutPlayerLateFramePrefixSource.InitialCameraViewRequest;
        _lateInhibitionTimerBits = FalloutPlayerLateFramePrefixSource.InitialInhibitionTimerBits;
        if (_historicalLateFrame is { } saved)
        {
            if (saved.Source != source || saved.Stack != _stack || saved.Process == _process)
                throw new InvalidDataException("Cold late Player prefix changed source or reused its process.");
            _cameraViewRequest = saved.ViewRequest;
            _lateInhibitionTimerBits = saved.InhibitionTimerBits;
            _lateInhibitionTimerAvailable = saved.TimerAvailable;
            _lateInhibitionTimerWriterBoundary = saved.TimerWriterBoundary;
            _lateFrameCall = saved.LastCall;
        }
        _actorMover.BindPlayerFrame(actor, invocation =>
        {
            RequireLateFrameCaller(invocation.Caller);
            if (invocation.PlayerProcess != _process || invocation.Common != _actorProcessQuery?.Result ||
                invocation.ThreadId != Environment.CurrentManagedThreadId ||
                _lateFrameCall?.Step is not (FalloutPlayerLateFrameStep.MoverFlags or FalloutPlayerLateFrameStep.DefaultVector))
                throw new InvalidOperationException("PlayerMover store escaped its actual late frame, Common or thread.");
        });
    }

    private void RequireLateFrameCaller(FalloutPlayerSourceCaller caller)
    {
        RequireFollowingContinuationInvocation(caller);
        if (_lateFrameCall is not { Failure: null } call || call.Invocation.Caller != caller ||
            call.Invocation.PlayerProcess != _process || call.Invocation.PlayerConstructor != _constructor ||
            call.Invocation.Prelude != _prelude?.Invocation || call.Invocation.Common != _actorProcessQuery?.Result)
            throw new InvalidOperationException("Late Player child escaped its actual entered source frame.");
    }

    internal void UpdateLateFrame(FalloutPlayerSourceCaller caller, Action<FalloutPlayerLateFrameCall> observe)
    {
        RequireFollowingContinuationInvocation(caller); ArgumentNullException.ThrowIfNull(observe);
        var source = _lateFrameSource ?? throw new NotSupportedException(FalloutPlayerFollowingContinuation.LateTailBoundary);
        var common = _actorProcessQuery?.Result ?? throw new InvalidOperationException("Late Player lost its actual Common query.");
        var prelude = _prelude ?? throw new InvalidOperationException("Late Player lost its entered Prelude.");
        var argument = _clock?.ArgumentBits ?? throw new InvalidOperationException("Late Player lost its retained clock argument.");
        var invocation = new FalloutPlayerLateFrameInvocation(caller, _process, _constructor, prelude.Invocation,
            common, Environment.CurrentManagedThreadId, argument);
        _lateFrameCall = new(source.Identity, _stack, invocation, Next(), _sequence, FalloutPlayerLateFrameStep.ViewRequest);
        try
        {
            _lateFrameCall = _lateFrameCall with { ViewRequest = _cameraViewRequest }; Publish();
            if (_cameraViewRequest != 0)
            {
                Step(FalloutPlayerLateFrameStep.CurrentView);
                _lateFrameCall = _lateFrameCall with { CurrentView = _view }; Publish();
                if (_view == 0)
                {
                    Step(FalloutPlayerLateFrameStep.DisabledControls);
                    var disabled = _controlFields.ReadDisabledWord();
                    _lateFrameCall = _lateFrameCall with { DisabledControls = disabled }; Publish();
                    if ((disabled & FalloutPlayerLateFramePrefixSource.ViewDisabledMask) == 0) throw new NotSupportedException(LateViewBoundary);
                }
            }
            Step(FalloutPlayerLateFrameStep.MoverFlags);
            if (_movementCall?.Control?.Call is not { Returned: true, Failure: null, Inhibited: true } controls ||
                controls.Main != caller.Main || controls.MainOrdinal != caller.Ordinal || controls.Process != _process ||
                controls.Invocation != prelude.Invocation)
                throw new NotSupportedException("source-Player-late-control-output-word-writer-unbound");
            _lateFrameCall = _lateFrameCall with { MoverWord = controls.MoverWord }; Publish();
            var moverInvocation = new FalloutActorMoverFrameInvocation(caller, _process, common, Environment.CurrentManagedThreadId);
            try { _actorMover.StorePlayerFrameFlags(moverInvocation, controls.MoverWord); }
            finally
            {
                var flags = _actorMover.LastPlayerFrameFlags;
                _lateFrameCall = _lateFrameCall with { Flags = flags?.Invocation == moverInvocation ? flags : null }; Publish();
            }
            Step(FalloutPlayerLateFrameStep.SharedTimer);
            if (!_lateInhibitionTimerAvailable)
                throw new NotSupportedException(_lateInhibitionTimerWriterBoundary);
            _lateFrameCall = _lateFrameCall with { SharedTimerBits = _lateInhibitionTimerBits }; Publish();
            if (BitConverter.UInt32BitsToSingle(_lateInhibitionTimerBits) > BitConverter.UInt32BitsToSingle(FalloutPlayerLateFramePrefixSource.InhibitionThresholdBits))
                throw new NotSupportedException(LateTimerBoundary);
            Step(FalloutPlayerLateFrameStep.SecondInhibition);
            var second = _inhibition.ReadLateFrameLane(caller, FalloutPlayerInhibitionLane.Second);
            _lateFrameCall = _lateFrameCall with { InhibitionReads = [second] }; Publish();
            if (second.Value != 0) throw new NotSupportedException(LateInhibitionBoundary);
            Step(FalloutPlayerLateFrameStep.FirstInhibition);
            var first = _inhibition.ReadLateFrameLane(caller, FalloutPlayerInhibitionLane.First);
            _lateFrameCall = _lateFrameCall with { InhibitionReads = [second, first] }; Publish();
            if (first.Value != 0) throw new NotSupportedException(LateInhibitionBoundary);
            Step(FalloutPlayerLateFrameStep.ViewComparison);
            _lateFrameCall = _lateFrameCall with { DesiredView = _desiredCameraView, ComparedView = _view }; Publish();
            if (_desiredCameraView == _view)
            {
                Step(FalloutPlayerLateFrameStep.ActorTime);
                var actorTime = _lateActorTime ?? throw new NotSupportedException("source-Player-late-Actor-time-child-unbound");
                try { actorTime.Update(invocation); }
                finally
                {
                    var retained = actorTime.LastCall;
                    _lateFrameCall = _lateFrameCall with { ActorTime = retained?.Invocation == invocation ? retained : null }; Publish();
                }
            }
            Step(FalloutPlayerLateFrameStep.DefaultVector);
            try { _actorMover.StorePlayerFrameDefaultVector(moverInvocation); }
            finally
            {
                var vector = _actorMover.LastPlayerFrameDefaultVector;
                _lateFrameCall = _lateFrameCall with { DefaultVector = vector?.Invocation == moverInvocation ? vector : null }; Publish();
            }
            Step(FalloutPlayerLateFrameStep.MoverDelta);
            var updateMover = _updateLateMoverDelta ?? throw new NotSupportedException(LateMoverDeltaBoundary);
            try { updateMover(invocation); }
            finally
            {
                var delta = _lateMoverDelta?.LastCall;
                _lateFrameCall = _lateFrameCall with { MoverDelta = delta?.Invocation == invocation ? delta : null }; Publish();
            }
            Step(FalloutPlayerLateFrameStep.Tail);
            var updateTail = _updateReturnTail ?? throw new NotSupportedException(FalloutPlayerFollowingContinuation.LateTailBoundary);
            try { updateTail(invocation); }
            finally
            {
                var tail = _lateReturnTail?.LastCall;
                _lateFrameCall = _lateFrameCall with { ReturnTail = tail?.Invocation == invocation ? tail : null }; Publish();
            }
            Step(FalloutPlayerLateFrameStep.AnimationPreparation);
            UpdatePostAnimationPrefix(invocation, prefix =>
            { _lateFrameCall = _lateFrameCall with { PostAnimation = prefix }; Publish(); });
        }
        catch (Exception error)
        {
            _lateFrameCall = _lateFrameCall with
            {
                FailedAt = _lateFrameCall.Step,
                Step = FalloutPlayerLateFrameStep.Failed,
                Failure = error.Message,
                Changed = Next()
            };
            observe(_lateFrameCall); throw;
        }

        void Step(FalloutPlayerLateFrameStep step) { _lateFrameCall = _lateFrameCall with { Step = step }; Publish(); }
        void Publish()
        {
            RequireLateFrameCaller(caller);
            _lateFrameCall = _lateFrameCall! with { Changed = Next() };
            observe(_lateFrameCall);
        }
    }

    private FalloutPlayerLateFrameSnapshot? CaptureLateFrame() => _lateFrameSource is { } source
        ? new(source, _stack, _process, _constructor, _cameraViewRequest, _lateInhibitionTimerBits,
            _lateInhibitionTimerAvailable, _lateInhibitionTimerWriterBoundary, _lateFrameCall) : _historicalLateFrame;

    internal void ObserveUnownedLateInhibitionTimerWriter(string producer)
    {
        RequireThread(); ObjectDisposedException.ThrowIf(_retired, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(producer);
        _lateInhibitionTimerAvailable = false;
        _lateInhibitionTimerWriterBoundary ??= "source-Player-late-inhibition-timer-writer-unbound:" + producer;
        Next();
    }

    private static void ValidateLateFrame(FalloutStandalonePlayerSceneSnapshot saved)
    {
        if (saved.LateFrame is not { } late)
        {
            if (saved.FollowingContinuation?.LateFrame is not null || saved.LateActorTime?.LastCall is not null)
                throw new InvalidDataException("Late Player history omitted its actual prefix field owner.");
            return;
        }
        late.Source.Validate();
        if (late.Source.Following.Controls.Main != saved.Source.Main || late.Stack != saved.Stack ||
            !ChildBelongsToParentProcessLineage(saved, late.Process) ||
            saved.PointOfView?.Lifetimes.SingleOrDefault(value => value.Process == late.Process)?.Constructor != late.Constructor ||
            late.ViewRequest != 0 || late.InhibitionTimerBits != 0 ||
            late.TimerAvailable != (late.TimerWriterBoundary is null) || late.TimerWriterBoundary is { Length: 0 })
            throw new InvalidDataException("Late Player prefix lost its actual source constructor or retained request byte.");
        if (late.LastCall is not { } call)
        {
            if (saved.FollowingContinuation?.LateFrame is not null || saved.LateActorTime?.LastCall is not null)
                throw new InvalidDataException("Late Player field owner omitted its entered latest call.");
            return;
        }
        ValidateLateFrameCall(call, late.Source, saved.Stack);
        if (call.PostAnimation is { } post)
        {
            var tailOwner = saved.ReturnTail ?? throw new InvalidDataException("Post-animation prefix lost its actual updater source owner.");
            ValidatePostAnimationPrefix(post, call, tailOwner.Source, saved.Stack);
            if (post.FirstToggle is { } firstToggle && saved.ViewFields?.Tail?.First != firstToggle)
                throw new InvalidDataException("Post-animation toggle differs from the actual shared Scene field store.");
        }
        if (call.ViewRequest != late.ViewRequest ||
            call.SharedTimerBits is { } timerBits && timerBits != late.InhibitionTimerBits)
            throw new InvalidDataException("Late Player reads differ from their actual request or inhibition timer owner.");
        if (!ChildBelongsToParentProcessLineage(saved, call.Invocation.PlayerProcess) ||
            !SameBoundedLateChild(saved.FollowingContinuation?.LateFrame, call) ||
            !SameBoundedLateChild(saved.LateActorTime?.LastCall, call.ActorTime))
            throw new InvalidDataException("Late Player history lost its actual following or Actor-time child.");
        ValidateLateFrameMoverJoins(call, saved.ActorMover ??
            throw new InvalidDataException("Late Player omitted the actual mover's frame store owner."));
        ValidateLateFrameInhibitionJoins(call, late.Source.Following, saved.Inhibition ??
            throw new InvalidDataException("Late Player omitted its actual shared inhibition owner."));
    }

    internal static void ValidateLateFrameCall(FalloutPlayerLateFrameCall call,
        FalloutPlayerLateFramePrefixSource source, string stack)
    {
        source.Validate();
        if (call.Source != source.Identity || call.Stack != stack || call.Invocation.Caller.Main == Guid.Empty ||
            call.Invocation.Caller.Ordinal < 1 || call.Invocation.Caller.Invocation == Guid.Empty ||
            call.Invocation.PlayerProcess == Guid.Empty || call.Invocation.Prelude == Guid.Empty ||
            call.Invocation.PlayerConstructor == Guid.Empty || call.Entered < 1 || call.Changed <= call.Entered ||
            call.Step != FalloutPlayerLateFrameStep.Failed || call.FailedAt is null or FalloutPlayerLateFrameStep.Failed ||
            !Enum.IsDefined(call.FailedAt.Value) || call.Invocation.ThreadId < 1 ||
            call.Invocation.Common.ExecutableSha256 != source.Following.Controls.Main.EngineSha256 ||
            call.Invocation.Common.Stack != stack || !call.Invocation.Common.Actor.EnginePlayer ||
            call.Invocation.Common.Process == Guid.Empty || call.Invocation.Common.Epoch < 1 ||
            call.Invocation.Common.GameplayOwnership == Guid.Empty ||
            string.IsNullOrWhiteSpace(call.Failure) || call.InhibitionReads?.Count > 2)
            throw new InvalidDataException("Late Player prefix omitted its actual source call or failed boundary.");
        var at = call.FailedAt.Value;
        if (call.ReturnTail is { } tail && (at < FalloutPlayerLateFrameStep.Tail || tail.Invocation != call.Invocation) ||
            at == FalloutPlayerLateFrameStep.AnimationPreparation && call.ReturnTail is not { Returned: true, Failure: null })
            throw new InvalidDataException("Late Player prefix lost its reached animation updater or returned child.");
        if (call.ViewRequest == 0 && (call.CurrentView is not null || call.DisabledControls is not null) ||
            call.ViewRequest is > 0 && at > FalloutPlayerLateFrameStep.CurrentView && call.CurrentView is null ||
            call.ViewRequest is > 0 && call.CurrentView == 0 && at > FalloutPlayerLateFrameStep.DisabledControls &&
                (call.DisabledControls is not { } disabled || (disabled & FalloutPlayerLateFramePrefixSource.ViewDisabledMask) == 0) ||
            call.CurrentView is not 0 && call.DisabledControls is not null)
            throw new InvalidDataException("Late Player optional view/control branch omitted or fabricated a reached getter.");
        if (at > FalloutPlayerLateFrameStep.ViewRequest && call.ViewRequest is null ||
            at > FalloutPlayerLateFrameStep.MoverFlags && call.Flags is not { Returned: true, Failure: null } ||
            at > FalloutPlayerLateFrameStep.SharedTimer && call.SharedTimerBits is null ||
            at > FalloutPlayerLateFrameStep.SecondInhibition && call.InhibitionReads is not { Count: > 0 } ||
            at > FalloutPlayerLateFrameStep.FirstInhibition && call.InhibitionReads is not { Count: 2 } ||
            at > FalloutPlayerLateFrameStep.ViewComparison && (call.DesiredView is null || call.ComparedView is null) ||
            at > FalloutPlayerLateFrameStep.ActorTime && call.DesiredView == call.ComparedView &&
                call.ActorTime is not { Returned: true, Failure: null } ||
            at > FalloutPlayerLateFrameStep.DefaultVector && call.DefaultVector is not { Returned: true, Failure: null })
            throw new InvalidDataException("Late Player failure omitted a mandatory previously returned source child.");
        if (at < FalloutPlayerLateFrameStep.MoverFlags && (call.Flags is not null || call.MoverWord is not null) ||
            at < FalloutPlayerLateFrameStep.SharedTimer && call.SharedTimerBits is not null ||
            at < FalloutPlayerLateFrameStep.SecondInhibition && call.InhibitionReads is { Count: > 0 } ||
            at < FalloutPlayerLateFrameStep.ViewComparison && (call.DesiredView is not null || call.ComparedView is not null) ||
            at < FalloutPlayerLateFrameStep.ActorTime && call.ActorTime is not null ||
            at < FalloutPlayerLateFrameStep.DefaultVector && call.DefaultVector is not null)
            throw new InvalidDataException("Late Player failure acquired source children that it never reached.");
        var reads = call.InhibitionReads ?? [];
        for (var index = 0; index < reads.Count; index++)
            if (reads[index].Caller != call.Invocation.Caller || reads[index].Consumer != FalloutPlayerInhibitionConsumer.LateFrame ||
                reads[index].Lane != (index == 0 ? FalloutPlayerInhibitionLane.Second : FalloutPlayerInhibitionLane.First) ||
                index > 0 && reads[index - 1].Value != 0)
                throw new InvalidDataException("Late Player inhibition changed its actual second/first short circuit.");
        if (call.ActorTime is not null && (call.Flags is not { Returned: true, Failure: null } ||
            reads.Count != 2 || reads.Any(value => value.Value != 0) || call.SharedTimerBits is not { } timer ||
            BitConverter.UInt32BitsToSingle(timer) > BitConverter.UInt32BitsToSingle(FalloutPlayerLateFramePrefixSource.InhibitionThresholdBits)))
            throw new InvalidDataException("Late Player Actor-time omitted its mandatory mover, clock or inhibition prefix.");
        if (call.Flags is { } flags && (flags.Kind != FalloutActorMoverFrameStoreKind.Flags ||
            flags.Invocation.Caller != call.Invocation.Caller || flags.Invocation.PlayerProcess != call.Invocation.PlayerProcess ||
            flags.Invocation.Common != call.Invocation.Common || flags.RequestedWord != call.MoverWord || flags.Stores.Count > 1) ||
            call.DefaultVector is { } vector && (vector.Kind != FalloutActorMoverFrameStoreKind.DefaultVector ||
                vector.Invocation.Caller != call.Invocation.Caller || vector.Invocation.PlayerProcess != call.Invocation.PlayerProcess ||
                vector.Invocation.Common != call.Invocation.Common || vector.Stores.Count > 2))
            throw new InvalidDataException("Late Player stores borrowed another mover invocation or escaped their bounded field count.");
        if (call.ActorTime is { } actor && (actor.Invocation != call.Invocation || call.DesiredView != call.ComparedView) ||
            call.DefaultVector is not null && call.DesiredView == call.ComparedView && call.ActorTime is not { Returned: true, Failure: null })
            throw new InvalidDataException("Late Player skipped or exchanged its independently reached Actor-time child.");
    }

    // Each child contains only its bounded latest call. JSON cold restore makes
    // independent list objects, so record reference equality is insufficient.
    private static bool SameBoundedLateChild<T>(T? left, T? right) =>
        JsonSerializer.Serialize(left) == JsonSerializer.Serialize(right);

    internal static void ValidateLateFrameInhibitionJoins(FalloutPlayerLateFrameCall call,
        FalloutPlayerFollowingContinuationSource source, FalloutPlayerInhibitionSnapshot inhibition)
    {
        var history = new FalloutPlayerInhibitionReadHistory(inhibition, source, call.Stack);
        var reads = call.InhibitionReads ?? [];
        for (var index = 0; index < reads.Count; index++)
            history.Require(reads[index], FalloutPlayerInhibitionConsumer.LateFrame, call.Invocation.Caller,
                call.Invocation.PlayerProcess, index == 0 ? FalloutPlayerInhibitionLane.Second : FalloutPlayerInhibitionLane.First,
                call.Invocation.PlayerConstructor);
    }

    internal static void ValidateLateFrameMoverJoins(FalloutPlayerLateFrameCall call, FalloutActorMoverSnapshot mover)
    {
        FalloutActorMoverState.Validate(mover, mover.Source, mover.Stack);
        if (mover.Stack != call.Stack || mover.Source.Controls.Main.EngineSha256 != call.Invocation.Common.ExecutableSha256 ||
            mover.Actor != call.Invocation.Common.Actor)
            throw new InvalidDataException("Late Player borrowed another actual mover Actor or source domain.");
        var frame = mover.PlayerFrame ?? throw new InvalidDataException("Late Player omitted the mover's actual frame history.");
        if (call.Flags is not null && !SameBoundedLateChild(call.Flags, frame.Flags) ||
            call.DefaultVector is not null && !SameBoundedLateChild(call.DefaultVector, frame.Vector))
            throw new InvalidDataException("Late Player embedded stores differ from the actual mover's retained calls.");
    }
}
