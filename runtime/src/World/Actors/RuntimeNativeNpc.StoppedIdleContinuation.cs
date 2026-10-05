using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Actors;

internal partial class RuntimeNativeNpc
{
    private bool CanCaptureStoppedIndependentIdle(bool allowFiniteSoundWait = false) =>
        _sitting == 0 && !_baseLocomotionMoving && !HasIndependentStoppedPose && Combat?.OwnsPose != true &&
        _idleOwner is "script" or "dialogue-response" or "package-event" && !_responseIdleActive &&
        _idleForm is not null && _animation is not null && _baseAnimation is not null &&
        _idlePlayback is { Complete: false } && _idleData is not null &&
        _idleRevision is > 0 and < long.MaxValue && _idleAnimationResource is not null && _idleAnimationSha256 is not null &&
        _animationObjects.Count == 0 && Combat?.AnimationWeapon is null &&
        (_animationSounds?.CanCaptureSilent != false || allowFiniteSoundWait && _animationSounds?.PendingFiniteVoices is { Count: > 0 });

    private FalloutActorStoppedIdleAnimation CaptureStoppedIndependentIdle()
    {
        if (!CanCaptureStoppedIndependentIdle())
            throw new NotSupportedException("Stopped source IDLE still has an unowned response, object, attachment or sound.");
        var animation = new FalloutActorPackageIdleAnimation(_idleForm!.Value,
            FalloutActorFurnitureContinuation.RecordHash(_aiStack!.GetEffective(_idleForm.Value)),
            _idleAnimationResource!, _idleAnimationSha256!, _idlePlayback!.Capture(), _idleRevision, CaptureFurnitureResidualPose());
        var saved = new FalloutActorStoppedIdleAnimation(_idleOwner!, animation);
        saved.Validate(_aiStack);
        return saved;
    }

    private void RestoreStoppedIndependentIdle(FalloutActorStoppedIdleAnimation saved)
    {
        saved.Validate(_aiStack!);
        if (_animation is not null || _responseIdleActive || HasIndependentStoppedPose || _baseLocomotionMoving)
            throw new NotSupportedException("Cold stopped source IDLE already has another pose owner.");
        PlayIdle(_aiStack!, saved.Animation.Idle, saved.Owner, saved.Animation);
    }
}
