using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutActorPerception
{
    // This is the original mode-one pair consumer. GetDetected never enters
    // this operation. The actual source scheduler must issue the pair in its
    // own selected order; nearest distance is not an admitted scheduler.
    internal FalloutPerceptionPairReceipt ComputeSourcePair(FalloutFormKey receiver,
        FalloutFormKey target, bool callerStagesNonplayer)
    {
        FalloutPerceptionPairReceipt? receipt = null;
        Mutate(() =>
        {
            RequireIdle(); var from = RequireActor(receiver); var to = RequireActor(target);
            if (receiver == target) throw new InvalidDataException("Source detection pair aliases its receiver and target.");
            RequireProcess(from); RequireProcess(to);
            if (ProducerBlocker is { } blocker) throw new NotSupportedException(blocker);
            _computingPair = new(receiver, target, from.ProcessEpoch, to.ProcessEpoch, callerStagesNonplayer);
            _phase = FalloutPerceptionFramePhase.Computing;
            var fromPose = Observation(receiver); var toPose = Observation(target);
            var request = _inputs.Pair(receiver, target, from.ProcessEpoch, to.ProcessEpoch);
            if (request.Receiver != receiver || request.Target != target || request.ReceiverProcessEpoch != from.ProcessEpoch ||
                request.TargetProcessEpoch != to.ProcessEpoch || string.IsNullOrWhiteSpace(request.Owner))
                throw new InvalidDataException("Detection producer belongs to a foreign or retired actor/process pair.");
            if (request.Failure is not null || request.Scalar is null)
                throw new NotSupportedException(request.Failure ?? "Source detection scalar inputs are incomplete: " + request.Owner);
            request.Visibility.Require(); var input = request.Scalar;
            if (input.VisualLineOfSight != request.Visibility.LineOfSight || input.VisualCone != request.Visibility.Cone ||
                input.SourceDistance != Distance(fromPose.SourcePosition, toPose.SourcePosition) ||
                input.TargetActionSound != to.Action.Level)
                throw new InvalidDataException("Detection scalar substituted visibility, distance or the actual action-noise owner.");
            var result = FalloutDetectionScalar.Calculate(_source.Scalar, input, _inputs.Settings());
            // Source mode one stages Detected only when explicitly requested
            // or when the receiver is the engine player. Positive scores also
            // stage the target's reverse Detecting list; nonpositive scores do
            // not manufacture a reverse clearing entry.
            if (callerStagesNonplayer || receiver == _player)
            {
                var entry = from.Cache?.Stage(target, result.Score > 0 ? 3 : 0, result.Score,
                    input.VisualCone, input.VisualLineOfSight, toPose.RawInCombat,
                    FalloutDetectionDirection.Detected, toPose.SourcePosition, _seconds);
                if (result.Score > 0)
                    _ = to.Cache?.Stage(receiver, 3, result.Score, input.VisualCone, input.VisualLineOfSight,
                        fromPose.RawInCombat, FalloutDetectionDirection.Detecting, fromPose.SourcePosition, _seconds);
                if (entry is not null && from.Cache!.Read(target, FalloutDetectionDirection.Detected)?.ProducerWrite == entry.ProducerWrite)
                    throw new InvalidDataException("Detection staging published a committed entry before the source commit phase.");
            }
            receipt = new(receiver, target, from.ProcessEpoch, to.ProcessEpoch, _seconds, result, request.Owner, Next());
            _pairs.Add(receipt); _computingPair = null; _phase = FalloutPerceptionFramePhase.Idle;
        });
        return receipt!;
    }

    // Invoke at the original commit call, after all admitted staging for this
    // receiver. A failure retains the committed Detected prefix, pending
    // Detecting list and controller-notification cursor for cold continuation.
    internal void CommitSourceCache(FalloutFormKey actor)
    {
        Mutate(() =>
        {
            RequireIdle(); var state = RequireActor(actor); RequireProcess(state);
            if (state.Process!.Level != FalloutDetectionProcessLevel.High) return;
            _frameReceivers = [actor]; _frameCursor = 0; _pairCursor = 0;
            _phase = FalloutPerceptionFramePhase.Committing;
            ContinueCommit();
        });
    }

    internal void ContinueSourceCommit()
    {
        Mutate(() =>
        {
            if (_phase is not (FalloutPerceptionFramePhase.Committing or FalloutPerceptionFramePhase.NotifyingController))
                throw new InvalidOperationException("No original detection commit is pending.");
            ContinueCommit();
        });
    }

    private void ContinueCommit()
    {
        if (_frameReceivers is not { Length: 1 } || _frameCursor != 0)
            throw new InvalidDataException("Detection commit lost its exact source receiver cursor.");
        var actor = RequireActor(_frameReceivers[0]); RequireProcess(actor);
        var cache = actor.Cache ?? throw new InvalidDataException("Detection HighProcess has no retained cache.");
        if (_phase == FalloutPerceptionFramePhase.Committing)
        {
            if (cache.BeginCommit()) _phase = FalloutPerceptionFramePhase.NotifyingController;
            else CompleteCommit();
        }
        if (_phase == FalloutPerceptionFramePhase.NotifyingController)
        {
            var actual = _inputs.Notify(actor.Source.Reference);
            if (string.IsNullOrWhiteSpace(actual.Owner) || actual.Present is null || actual.DetectionUpdatedApplied is null ||
                actual.Present != actual.DetectionUpdatedApplied)
                throw new NotSupportedException("Detection commit lacks its actual controller/updated-flag producer: " + actual.Owner);
            cache.CompleteCombatNotification(actual.Present.Value, actual.DetectionUpdatedApplied.Value);
            CompleteCommit();
        }
    }

    private void CompleteCommit()
    {
        _phase = FalloutPerceptionFramePhase.Idle; _frameReceivers = []; _frameCursor = _pairCursor = 0;
        _frames = checked(_frames + 1); Next();
    }

    internal void AdvanceSourceLight(FalloutFormKey actor, float actorElapsedSeconds)
    {
        Mutate(() =>
        {
            RequireIdle(); var state = RequireActor(actor); RequireProcess(state);
            if (state.Process!.Level != FalloutDetectionProcessLevel.High) return;
            state.Light!.Advance(actorElapsedSeconds, () => state.Source3D, () => _inputs.Light(actor)); Next();
        });
    }

    // Producer failures remain source faults. Retrying a successfully admitted
    // native phase does not erase the original failure or publish a clean save.
    internal void RetainSourceBoundary(FalloutFormKey? actor, FalloutFormKey? target, string owner, string message)
    {
        Mutate(() =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(owner); ArgumentException.ThrowIfNullOrWhiteSpace(message);
            if (actor is { } from) _ = RequireActor(from);
            if (target is { } to) _ = RequireActor(to);
            if (!_failures.Any(failure => failure.Actor == actor && failure.Target == target && failure.Owner == owner && failure.Error == message))
                _failures.Add(new(Next(), actor, target, owner, message));
        });
    }

    private static void RequireProcess(ActorState actor)
    {
        if (actor.ProcessBoundary is { } boundary) throw new NotSupportedException(boundary);
        var process = actor.Process ?? throw new NotSupportedException("Actor process class is unowned.");
        process.Validate();
        if (!process.HasProcess) throw new InvalidOperationException("Detection producer has no source process.");
    }
}
