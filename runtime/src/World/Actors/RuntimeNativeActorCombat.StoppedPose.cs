using Godot;
using OpenNV.Runtime.Content;

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
        if (_state.PendingPackageSelection is { } pending)
        {
            pending.Validate();
            var pose = pending.Pose;
            _actor.Transform = new(new Basis(new Vector3(pose[0], pose[1], pose[2]),
                new Vector3(pose[3], pose[4], pose[5]), new Vector3(pose[6], pose[7], pose[8])),
                new(pose[9], pose[10], pose[11]));
            return true;
        }
        return false;
    }

    private bool? _retiredStoppedPoseReady;
    private FalloutActorStoppedPoseCaptureDiagnostic? _retiredStoppedPoseDiagnostic;
    internal FalloutActorStoppedPoseCaptureDiagnostic StoppedAiPoseCaptureDiagnostic =>
        _retiredStoppedPoseDiagnostic ?? ReadStoppedPoseDiagnostic(LiveStoppedAiPoseCaptureReady, false);

    private FalloutActorStoppedPoseCaptureDiagnostic ReadStoppedPoseDiagnostic(bool ready, bool retired)
    {
        var blockers = new List<FalloutActorCaptureBlocker>();
        void Refuse(bool condition, string predicate, string? detail = null)
        {
            if (condition) blockers.Add(new("combat-pose", predicate, detail));
        }
        Refuse(PackageOwnsPose, "package-owns-pose");
        Refuse(PackageMoving, "package-moving");
        Refuse(_state.KnockedDown, "knocked-down");
        Refuse(_state.Unconscious, "unconscious");
        Refuse(ReactingToHit, "active-hit-reaction");
        Refuse(Error is not null, "combat-error", Error);
        Refuse(_engagementError is not null, "engagement-error", _engagementError);
        Refuse(_assistanceError is not null, "assistance-error", _assistanceError);
        Refuse(_pendingHitscanImpacts != 0, "pending-hitscan-impacts", _pendingHitscanImpacts.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Refuse(_routeSearch is not null, "active-route-search");
        Refuse(_routeDoor is not null, "active-route-door");
        Refuse(_enemySounds?.CanCaptureSilent == false, "enemy-sounds");
        if (Dead)
        {
            Refuse(_ragdoll?.CaptureReady != true, "corpse-capture-not-ready");
            Refuse(_state.HitReaction is not null, "corpse-hit-reaction");
            Refuse(_enemyObject is not null, "corpse-enemy-object");
            Refuse(_packageWeapon is not null, "corpse-package-weapon");
            Refuse(_enemyWeaponHandling is not null, "corpse-native-weapon-handling");
            Refuse(_state.Engagement?.WeaponHandling is not null, "corpse-retained-weapon-handling");
        }
        else if (OwnsPose)
        {
            Refuse(!_engagementPrepared, "engagement-unprepared");
            Refuse(_state.CaptureEngagement != CaptureEngagement, "engagement-capture-binding");
            Refuse(_state.Engagement?.Action != "idle", "engagement-not-idle", _state.Engagement?.Action);
            Refuse(_state.Engagement?.StartPending != false, "engagement-start-pending");
            if (_state.Engagement is not { Animation: { } path, AnimationHash: { } hash })
                Refuse(true, "engagement-source-animation-missing");
            else
            {
                var found = _combatClips.TryGetValue(path, out var clip);
                Refuse(!found, "engagement-clip-missing", path);
                Refuse(found && !clip!.Hash.Equals(hash, StringComparison.OrdinalIgnoreCase), "engagement-clip-hash", path);
            }
            Refuse(_mover is null, "mover-missing");
            Refuse(_mover is { } mover && !mover.Velocity.IsZeroApprox(), "mover-not-stationary");
        }
        if (retired && _state.CaptureEngagement != CaptureEngagement && _state.Engagement is not null)
            Refuse(true, "retired-engagement-capture-binding");
        // A failed selection before HitReaction was created is an independent
        // historical fault, not one of the current readiness predicates.
        return new(ready, retired, blockers.AsReadOnly(), _enemySounds?.CaptureDiagnostic,
            _hitReactionError, _hitReactionSounds?.CaptureDiagnostic);
    }

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
        _retiredStoppedPoseDiagnostic ??= ReadStoppedPoseDiagnostic(_retiredStoppedPoseReady.Value, true);
    }
}
