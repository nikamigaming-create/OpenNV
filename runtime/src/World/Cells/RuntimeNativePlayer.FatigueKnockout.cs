using System.Security.Cryptography;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Content.Definitions;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Actors;

namespace OpenNV.Runtime.World.Cells;

internal partial class RuntimeNativePlayer
{
    private Func<float>? _currentFatigue;
    private Action? _requireFatigueEffects;
    private FalloutFormKey _fatigueActor;
    private FalloutPlayerFatigueKnockout? _fatigueKnockout;
    private RuntimeNativeActorRagdoll? _fatigueBody;
    private PlayerRagdollState? _fatigueBodyState;
    private NativeActorCombatAnimation? _fatigueRecovery;
    private FalloutHitReactionTree? _fatigueRecoveryTree;
    private readonly Dictionary<CollisionObject3D, (uint Layer, uint Mask)> _fatigueFilters = [];
    private string? _fatigueKnockoutError;
    private string? _fatigueRecoveryNotice;
    internal bool FatigueKnockoutActive => _fatigueKnockout is not null || _fatigueKnockoutError is not null;
    internal int FatigueKnockedState => _fatigueKnockout?.State.Unconscious == true ? 1 : 0;
    private bool FatigueOwnsPose => _fatigueBody is not null || _fatigueRecovery is not null;
    internal string? FatigueKnockoutSaveBlocker => _fatigueKnockoutError is not null ? "player-fatigue-physical-continuation-fault" :
        _fatigueKnockout is not null && !FatigueOwnsPose ? "player-fatigue-source-binding-pending" : null;
    internal object FatigueKnockoutObservation => new
    {
        phase = _fatigueKnockout?.State.Phase.ToString(),
        error = _fatigueKnockoutError,
        ragdoll = _fatigueBody?.Observation,
        recovery = _fatigueKnockout?.Recovery,
        recoveryNotice = _fatigueRecoveryNotice,
        boundary = "source-ragdoll-settlement-and-finite-getup;paralysis-and-special-life-state-producers-unadmitted"
    };

    private sealed class PlayerRagdollState(RuntimeNativePlayer player, FalloutFormKey reference) : RuntimeNativeRagdollStateOwner
    {
        internal override FalloutFormKey Reference => reference;
        internal override bool Dead => player.IsDefeated?.Invoke() == true;
        internal override bool KnockedDown => player._fatigueKnockout is not null;
        internal override IReadOnlyList<byte> SeveredParts => [];
        internal override FalloutActorRagdollState? Ragdoll { get; set; }
        internal override Func<FalloutActorRagdollState>? CaptureRagdoll { get; set; }
    }

    internal void ConfigureFatigueKnockout(FalloutPluginStack records, FalloutPluginRecord actor,
        Func<float> currentFatigue, Action requireEffects, FalloutPlayerFatigueKnockout? restore)
    {
        if (_currentFatigue is not null) throw new InvalidOperationException("Player fatigue knockout already has a pool owner.");
        restore?.Validate();
        if (restore is not null && !FalloutActorKnockdownSource.Allows(records, actor.FormKey))
            throw new InvalidDataException("Player fatigue knockout conflicts with its source NoKnockdowns policy.");
        _fatigueActor = actor.FormKey; _currentFatigue = currentFatigue; _requireFatigueEffects = requireEffects; _fatigueKnockout = restore;
    }

    internal FalloutPlayerFatigueKnockout? CaptureFatigueKnockout()
    {
        if (FatigueKnockoutSaveBlocker is { } blocker) throw new InvalidOperationException(blocker);
        var state = _fatigueKnockout;
        if (state is null) return null;
        if (_fatigueBody is { Active: true } body) state = state with { Ragdoll = body.Capture() };
        state.Validate();
        return state;
    }

    private FalloutPluginRecord? PlayerRecoveryIdle((bool FacingUp, bool LeftUp) orientation)
    {
        return PlayerRecoveryTree().Select(condition => condition.Function switch
        {
            (ushort)FalloutConditionFunction.IsFacingUp => orientation.FacingUp ? 1 : 0,
            (ushort)FalloutConditionFunction.IsLeftUp => orientation.LeftUp ? 1 : 0,
            // Source selection observes the unconscious hold before the get-up
            // request changes phase; both preflight orientations use that phase.
            (ushort)FalloutConditionFunction.GetKnockedState => 1,
            391 => -1, // Recovery has no hit location; authored humanoid/ant roots require this sentinel.
            392 => 0, // Recovery binds the complete third-person body in both flat and XR.
            _ => (_combatCondition ?? throw new InvalidOperationException("Player get-up has no source condition owner."))(condition)
        });
    }

    private FalloutHitReactionTree PlayerRecoveryTree()
    {
        var actor = _thirdPerson ?? throw new InvalidOperationException("Player get-up requires its actual source body.");
        return _fatigueRecoveryTree ??= new(_presentationRecords!, actor.Actor.Appearance.SkeletonPath,
            (ushort)FalloutConditionFunction.GetKnockedState);
    }

    private NativeActorCombatAnimation PlayerRecoveryClip(FalloutPluginRecord idle)
    {
        var source = FalloutActorIdleSource.Resolve(_presentationRecords!, idle);
        return PlayerRecoveryClip(source.AnimationPath);
    }

    private NativeActorCombatAnimation PlayerRecoveryClip(string animationPath)
    {
        var clip = new NativeActorCombatAnimation(animationPath, RuntimeLiveContentSource.Current!,
            _thirdPerson!.Skeleton, _thirdPerson.AnimationWeapon, false);
        if (clip.Animation.Sequence.CycleType != 2 || !double.IsFinite(clip.Duration) || clip.Duration <= 0)
            throw new NotSupportedException("Player get-up requires a finite source animation.");
        return clip;
    }

    private void DisableFatigueCollision()
    {
        foreach (var collider in FindChildren("*", "CollisionObject3D", true, false).OfType<CollisionObject3D>().Prepend(this))
        {
            if (_fatigueBody?.IsAncestorOf(collider) == true) continue;
            // These query-only volumes follow the same posed source bones.
            // Player melee damage queries require them while the physical rig
            // replaces the locomotion hull; they are not extra rigid bodies.
            if (collider is Area3D area && _thirdPerson!.BodyContacts.Contains(area)) continue;
            _fatigueFilters.TryAdd(collider, (collider.CollisionLayer, collider.CollisionMask));
            GamebryoReferenceEnableRuntime.SetCollisionFilter(collider, 0, 0);
        }
    }

    private void RestoreFatigueCollision()
    {
        foreach (var (collider, filter) in _fatigueFilters)
            if (IsInstanceValid(collider)) GamebryoReferenceEnableRuntime.SetCollisionFilter(collider, filter.Layer, filter.Mask);
        _fatigueFilters.Clear();
    }

    private void PreparePlayerFatigueBody()
    {
        var actor = _thirdPerson!;
        // Validate the actual authored family, without inventing a requirement
        // for branches in orientations that the source does not declare.
        var paths = PlayerRecoveryTree().AnimationPaths().Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        // No IDLE family is valid: the source reset path restores living
        // physics once fatigue/effect eligibility passes, without a guessed KF.
        foreach (var path in paths) _ = PlayerRecoveryClip(path);
        _fatigueBodyState = new(this, _presentationRecords!.RuntimeFormKey(0x14)) { Ragdoll = _fatigueKnockout!.Ragdoll };
        _fatigueBody = RuntimeNativeActorRagdoll.Prepare(actor, actor.Skeleton, _fatigueBodyState,
            RuntimeLiveContentSource.Current!, actor.Actor.Appearance.SkeletonPath, CollisionLayer, CollisionMask, BodyParts().Parts);
        DisableFatigueCollision();
        _fatigueBody.Activate();
    }

    private bool AdvanceFatigueKnockout(double delta)
    {
        if (_currentFatigue is null) return false;
        if (_presentationRecords is null) return _fatigueKnockout is not null;
        if (IsDefeated?.Invoke() == true)
        {
            // Death owns the subsequent lifecycle. No inventory/drop/death event
            // is synthesized by retiring this nonlethal presentation.
            _fatigueBody?.Free(); _fatigueBody = null; _fatigueRecovery = null;
            _fatigueKnockout = null; _fatigueBodyState = null; _fatigueKnockoutError = null;
            RestoreFatigueCollision();
            return false;
        }
        if (_fatigueKnockoutError is not null) return true;
        try
        {
            var fatigue = _currentFatigue();
            if (!float.IsFinite(fatigue)) throw new InvalidDataException("Player current fatigue is nonfinite.");
            if (_fatigueKnockout is not null && !FalloutActorKnockdownSource.Allows(_presentationRecords!, _fatigueActor))
            {
                ClearPlayerFatigueKnockout();
                return false;
            }
            if (_fatigueKnockout is null)
            {
                if (fatigue >= 0 || _thirdPerson is null || _presentationError is not null || _sourceCamera is not null ||
                    _furniturePhase != 0 || _modalInput ||
                    _weaponAction?.StartsWith("reload", StringComparison.Ordinal) == true || _automaticReloadAfterDischarge is not null ||
                    !FalloutActorKnockdownSource.Allows(_presentationRecords!, _fatigueActor)) return false;
                _requireFatigueEffects!();
                if (VatsMenuActive || VatsExecuting) RetireVats("fatigue-knockout");
                _fatigueKnockout = new(new(FalloutFatigueKnockoutPhase.Falling));
                _fatigueRecoveryNotice = null;
                ReleaseGrab(); CancelWeaponAction(); _weaponTriggerHeld = false; _pendingShotCount = 0;
                _reloadPressed = false; _aiming = false; _hitKnockback = null; _previousKnockbackVertical = 0;
            }
            if (_thirdPerson is null) return true; // Cold binding waits for the actual source body, never a proxy.
            if (_fatigueKnockout.State.Phase != FalloutFatigueKnockoutPhase.GettingUp)
            {
                if (_fatigueBody is null) PreparePlayerFatigueBody();
                if (_fatigueBody!.Settled)
                    _fatigueKnockout = _fatigueKnockout with { State = new(FalloutFatigueKnockoutPhase.Unconscious) };
                if (_fatigueKnockout.State.Phase != FalloutFatigueKnockoutPhase.Unconscious || fatigue < 0) return true;
                BeginPlayerFatigueRecovery();
                return true;
            }
            AdvancePlayerFatigueRecovery(delta);
            return true;
        }
        catch (Exception error)
        {
            _fatigueKnockoutError = error.Message;
            GD.PushError("OPENNV_PLAYER_FATIGUE_KNOCKOUT_UNBOUND " + error.Message);
            return true;
        }
    }

    private void BeginPlayerFatigueRecovery()
    {
        _requireFatigueEffects!();
        var torso = _fatigueBody!.TorsoTransform;
        var idle = PlayerRecoveryIdle(_fatigueBody.RecoveryOrientation);
        var clip = idle is null ? null : PlayerRecoveryClip(idle);
        var excluded = new Godot.Collections.Array<Rid>(SelfQueryBodies);
        foreach (var body in _fatigueBody.FindChildren("*", "CollisionObject3D", true, false).OfType<CollisionObject3D>()) excluded.Add(body.GetRid());
        using var ray = PhysicsRayQueryParameters3D.Create(torso.Origin + Vector3.Up * .05f,
            torso.Origin + Vector3.Down * (256 * UnitsToMeters), _fatigueFilters[this].Mask, excluded);
        using var floor = GetWorld3D().DirectSpaceState.IntersectRay(ray);
        if (!floor.TryGetValue("position", out var point)) return;
        _fatigueBody.EndLivingSimulation(); _fatigueBody.Free(); _fatigueBody = null; _fatigueBodyState = null;
        GlobalPosition = point.AsVector3();
        RestoreFatigueCollision();
        if (idle is null || clip is null)
        {
            // Native null-IDLE selection uses the explicit reset helper. The
            // caller already qualified fatigue and actual effect producers;
            // this handoff changes neither health, fatigue nor inventory.
            ClearPlayerFatigueKnockout();
            _fatigueRecoveryNotice = "source-no-idle;living-physics-reset";
            return;
        }
        _fatigueRecoveryNotice = null;
        _fatigueRecovery = clip;
        var p = GlobalPosition; var q = GlobalBasis.Orthonormalized().GetRotationQuaternion();
        _fatigueKnockout = new(new(FalloutFatigueKnockoutPhase.GettingUp), Recovery:
            new(idle.FormKey, Convert.ToHexString(SHA256.HashData(idle.ReadData())), 0,
                new(clip.Path, clip.Hash, 0, true), [p.X, p.Y, p.Z], [q.X, q.Y, q.Z, q.W]));
    }

    private void AdvancePlayerFatigueRecovery(double delta)
    {
        var saved = _fatigueKnockout!.Recovery!;
        if (_fatigueRecovery is null)
        {
            var idle = _presentationRecords!.GetEffective(saved.Idle);
            if (!saved.IdleSha256.Equals(Convert.ToHexString(SHA256.HashData(idle.ReadData())), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Saved player get-up IDLE changed.");
            _fatigueRecovery = PlayerRecoveryClip(idle);
            if (!_fatigueRecovery.Path.Equals(saved.Animation.Resource, StringComparison.OrdinalIgnoreCase) ||
                !_fatigueRecovery.Hash.Equals(saved.Animation.Sha256, StringComparison.OrdinalIgnoreCase) ||
                saved.Animation.ElapsedSeconds >= _fatigueRecovery.Duration)
                throw new InvalidDataException("Saved player get-up animation or clock changed.");
        }
        var clip = _fatigueRecovery;
        var from = saved.Animation.ElapsedSeconds; var next = Math.Min(from + delta, clip.Duration);
        Velocity = GlobalBasis * clip.RootDisplacement(from, next) * UnitsToMeters / (float)Math.Max(delta, .000001);
        MoveAndSlide(); Velocity = Vector3.Zero;
        EnsureWeaponSounds();
        foreach (var key in clip.Events.Crossed(from, next, saved.Animation.StartPending)) _weaponSounds!.Dispatch(key);
        var skeleton = _thirdPerson!.Skeleton;
        skeleton.ResetSourceBonePoses();
        skeleton.Node.SetBonePose(skeleton.BoneIndex(clip.Animation.Sequence.TargetName), Transform3D.Identity);
        clip.Animation.ApplySourceTime(clip.Time(next));
        var p = GlobalPosition; var q = GlobalBasis.Orthonormalized().GetRotationQuaternion();
        _fatigueKnockout = _fatigueKnockout with
        {
            Recovery = saved with
            {
                Animation = saved.Animation with { ElapsedSeconds = next, StartPending = false },
                Position = [p.X, p.Y, p.Z],
                Rotation = [q.X, q.Y, q.Z, q.W]
            }
        };
        if (next >= clip.Duration)
        {
            _requireFatigueEffects!();
            if (_currentFatigue!() < 0)
            {
                // A completed old get-up cannot erase newly negative fatigue.
                // Retain input suppression and enter physics from the actual
                // completed source pose rather than publishing a recovered tick.
                _fatigueKnockout = new(new(FalloutFatigueKnockoutPhase.Falling));
                _fatigueRecovery = null;
                PreparePlayerFatigueBody();
            }
            else ClearPlayerFatigueKnockout();
        }
    }

    private void ClearPlayerFatigueKnockout()
    {
        _fatigueBody?.EndLivingSimulation(); _fatigueBody?.Free();
        _fatigueBody = null; _fatigueBodyState = null; _fatigueRecovery = null; _fatigueKnockout = null;
        _fatigueRecoveryNotice = null;
        RestoreFatigueCollision();
        if (_firstPerson is not null) _firstPerson.Visible = true;
    }

    private void PublishPlayerFatiguePose()
    {
        if (_firstPersonPixels is not null) _firstPersonPixels.Visible = false;
        if (_firstPerson is not null) _firstPerson.Visible = false;
        _thirdPerson!.Visible = true; _thirdPerson.SetViewPolicy(true, true);
        _fatigueBody?.PublishCurrentPose();
        if (_xr is not null) return; // Tracking remains the sole headset pose owner.
        var head = BodyParts().Parts.Single(part => part.Type == 1);
        var bone = _thirdPerson.Skeleton.BoneIndex(string.IsNullOrEmpty(head.Target) ? head.Node : head.Target);
        if (bone < 0) throw new NotSupportedException("Player fatigue camera has no source head node.");
        var frame = _thirdPerson.Skeleton.Node.GlobalTransform * _thirdPerson.Skeleton.Node.GetBoneGlobalPose(bone);
        _camera.GlobalTransform = new(frame.Basis.Orthonormalized(), frame.Origin);
    }
}
