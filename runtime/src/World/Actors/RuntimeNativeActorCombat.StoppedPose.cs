using Godot;

namespace OpenNV.Runtime.World.Actors;

internal sealed partial class RuntimeNativeActorCombat
{
    private bool RestoreStoppedAiPose()
    {
        if (_state.PackageBindingFailure is { } binding)
        {
            binding.Validate();
            _actor.GlobalTransform = new(new Basis(new Vector3(binding.Basis[0], binding.Basis[1], binding.Basis[2]),
                new Vector3(binding.Basis[3], binding.Basis[4], binding.Basis[5]),
                new Vector3(binding.Basis[6], binding.Basis[7], binding.Basis[8])),
                new(binding.Position[0], binding.Position[1], binding.Position[2]));
            return true;
        }
        if (_state.SelectionFailure is { } selection)
        {
            selection.Validate();
            var pose = selection.Pose;
            _actor.Transform = new(new Basis(new Vector3(pose[0], pose[1], pose[2]),
                new Vector3(pose[3], pose[4], pose[5]), new Vector3(pose[6], pose[7], pose[8])),
                new(pose[9], pose[10], pose[11]));
            return true;
        }
        return false;
    }

    private bool? _retiredStoppedPoseReady;

    // A stopped AI owner cannot waive a second pose owner. Only a completed
    // corpse capture or a stationary, source-bound combat idle composes here.
    // Preserve a refusal before ExitTree disposes searches and sound voices.
    internal bool StoppedAiPoseCaptureReady => _retiredStoppedPoseReady ?? LiveStoppedAiPoseCaptureReady;

    private bool LiveStoppedAiPoseCaptureReady
    {
        get
        {
            if (PackageOwnsPose || PackageMoving || _state.KnockedDown || _state.Unconscious ||
                ReactingToHit || Error is not null || _engagementError is not null || _assistanceError is not null ||
                _pendingHitscanImpacts != 0 || _routeSearch is not null || _routeDoor is not null ||
                _enemySounds?.CanCaptureSilent == false) return false;
            if (Dead)
                return _ragdoll?.CaptureReady == true && _state.HitReaction is null &&
                    _enemyObject is null && _packageWeapon is null && _enemyWeaponHandling is null &&
                    _state.Engagement?.WeaponHandling is null;
            if (!OwnsPose) return true;
            return _engagementPrepared && _state.CaptureEngagement == CaptureEngagement &&
                _state.Engagement is { Action: "idle", Animation: { } path, AnimationHash: { } hash, StartPending: false } &&
                _combatClips.TryGetValue(path, out var clip) && clip.Hash.Equals(hash, StringComparison.OrdinalIgnoreCase) &&
                _mover is { } mover && mover.Velocity.IsZeroApprox();
        }
    }

    private void RetainStoppedPoseReadiness()
    {
        _retiredStoppedPoseReady = LiveStoppedAiPoseCaptureReady;
        if (_state.CaptureEngagement != CaptureEngagement && _state.Engagement is not null)
            _retiredStoppedPoseReady = false;
    }
}
