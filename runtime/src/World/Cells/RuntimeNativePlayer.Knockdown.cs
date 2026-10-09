using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Actors;

namespace OpenNV.Runtime.World.Cells;

internal partial class RuntimeNativePlayer
{
    private RuntimeNativeActorRagdoll? _playerRagdoll;
    private FalloutActorRagdollState? _playerRagdollState;
    private Func<FalloutActorRagdollState>? _capturePlayerRagdoll;
    private readonly Dictionary<CollisionObject3D, (uint Layer, uint Mask)> _playerKnockdownFilters = [];
    private FalloutHitReactionTree? _playerRecoveryTree;
    private readonly Dictionary<FalloutFormKey, PlayerPhysicalClip> _playerRecoveryClips = [];
    private PlayerPhysicalClip? _playerRecovery;
    private FalloutPlayerPhysicalAnimation? _playerRecoveryClock;
    private string? _playerKnockdownSkeleton, _playerKnockdownSkeletonHash;

    internal void ApplyPlayerExplosionDamage(FalloutExplosion explosion, FalloutWeaponDamage damage,
        byte part, Vector3 center, bool push, Action<FalloutWeaponDamage, byte> apply)
    {
        ArgumentNullException.ThrowIfNull(apply);
        PhysicalPlayer.Execute("player-explosion-damage:" + explosion.Form + ":part" + part, () =>
        {
            var before = _physicalVitals!().ExactHitPoints;
            apply(damage, part);
            var after = _physicalVitals!().ExactHitPoints;
            if (!float.IsFinite(before) || !float.IsFinite(after) || after > before)
                throw new NotSupportedException("Explosion damage callbacks require their actual mixed damage/heal prefix owner.");
            ApplyPlayerExplosionPhysics(explosion, before, before - after, center, push);
        });
    }

    // The damage owner already committed this hit. Read its actual before/after
    // values and current pools; never apply damage or reroll a saved blast here.
    internal void ApplyPlayerExplosionPhysics(FalloutExplosion explosion, float healthBefore,
        float healthDamage, Vector3 center, bool push)
    {
        var state = PhysicalPlayer;
        state.Execute("player-explosion-physical-response", () =>
        {
            var vitals = _physicalVitals!(); vitals.Validate();
            if (vitals.ExactHitPoints <= 0)
                throw new NotSupportedException("Player death from an explosion requires its distinct death/ragdoll lifecycle.");
            if (state.FurniturePhase != FalloutPlayerFurniturePhase.None || state.Sleeping)
                throw new NotSupportedException("Explosion interruption requires the current player furniture/sleep retirement contract.");
            if (state.KnockdownPhase == FalloutPlayerKnockdownPhase.Recovering)
                throw new NotSupportedException("Explosion interruption of player recovery requires its actual animation/physics transfer owner.");
            var agility = (float)_physicalActorValue!("Agility");
            if (state.KnockdownPhase == FalloutPlayerKnockdownPhase.Upright &&
                FalloutExplosionKnockdown.ShouldFall(_physicalRecords!, explosion, healthDamage, healthBefore,
                    vitals.MaximumHitPoints, agility, _furnitureRandom.NextUnitFloat))
            {
                PreparePlayerKnockdown();
                state.CommitKnockdown(FalloutPlayerKnockdownPhase.Simulating);
                HoldPlayerPhysicalMotion();
                _playerRagdoll!.Activate();
            }
            if (push && _playerRagdoll is { Active: true })
                _playerRagdoll.ApplyBlast(center, explosion.Force * UnitsToMeters);
        });
    }
    private void PreparePlayerKnockdown()
    {
        var actor = _thirdPerson?.Actor ?? throw new NotSupportedException("Player knockdown has no actual source body.");
        if (!actor.IsInsideTree()) throw new NotSupportedException("Player knockdown body is detached.");
        var records = _physicalRecords!; var content = RuntimeLiveContentSource.Current!;
        _playerRecoveryTree = new(records, actor.Appearance.SkeletonPath, 106);
        // Preparation inspects all possible source leaves; it executes no CTDA,
        // consumes no random value and invents no selected recovery branch.
        foreach (var idle in _playerRecoveryTree.CandidateLeaves)
        {
            var source = FalloutActorIdleSource.Resolve(records, idle);
            if (source.Objects.Count != 0) throw new NotSupportedException("Player recovery ANIO requires its actual object owner.");
            _playerRecoveryClips[idle.FormKey] = ReadPlayerPhysicalClip(idle, false, actor.Skeleton,
                sample => ApplyPlayerRecoveryRoot(sample));
        }
        if (_playerRecoveryClips.Count == 0) throw new NotSupportedException("Player has no source recovery IDLE leaves.");
        _playerKnockdownSkeleton = actor.Appearance.SkeletonPath;
        _playerKnockdownSkeletonHash = actor.Skeleton.Source.Sha256;
        var owner = new FalloutNativeRagdollOwner(records.RuntimeFormKey(0x14),
            () => _physicalVitals!().ExactHitPoints <= 0,
            () => PhysicalPlayer.KnockdownPhase != FalloutPlayerKnockdownPhase.Upright,
            () => _playerRagdollState, value => _playerRagdollState = value,
            () => _capturePlayerRagdoll, value => _capturePlayerRagdoll = value, () => []);
        _playerRagdoll = RuntimeNativeActorRagdoll.Prepare(actor, actor.Skeleton, owner,
            content, _playerKnockdownSkeleton, CollisionLayer, CollisionMask, BodyParts().Parts);
    }
    private void HoldPlayerPhysicalMotion()
    {
        _physicalInput ??= AcquireModalInput();
        CancelWeaponAction(); Velocity = Vector3.Zero; Activity.SetMovement(false, false);
        foreach (var collider in _thirdPerson!.Actor.FindChildren("*", "CollisionObject3D", true, false)
            .OfType<CollisionObject3D>().Prepend(this))
        {
            if (_playerRagdoll!.IsAncestorOf(collider)) continue;
            _playerKnockdownFilters.TryAdd(collider, (collider.CollisionLayer, collider.CollisionMask));
            GamebryoReferenceEnableRuntime.SetCollisionFilter(collider, 0, 0);
        }
        _thirdPerson.Visible = true; _firstPersonPixels?.Hide(); PublishPlayerPhysicalView();
    }
    private bool AdvancePlayerKnockdown(double delta)
    {
        if (_playerPhysical is null || _playerPhysical.KnockdownPhase == FalloutPlayerKnockdownPhase.Upright) return false;
        if (_playerPhysical.Failure is not null) return true;
        if (GetTree().Paused || !CanProcess()) return true;
        try
        {
            _playerPhysical.Execute("advance-player-knockdown", () =>
            {
                if (_playerPhysical.KnockdownPhase == FalloutPlayerKnockdownPhase.Simulating)
                {
                    if (_playerRagdoll is not { Settled: true }) return;
                    var torso = _playerRagdoll.TorsoTransform;
                    var excluded = new Godot.Collections.Array<Rid>(CombatCollisionRids.Concat(_playerRagdoll.BodyRids));
                    using var ray = PhysicsRayQueryParameters3D.Create(torso.Origin + Vector3.Up * .05f,
                        torso.Origin + Vector3.Down * (256 * UnitsToMeters), _configuration.Player.CollisionMask, excluded);
                    using var floor = GetWorld3D().DirectSpaceState.IntersectRay(ray);
                    if (!floor.TryGetValue("position", out var position)) return;
                    // A missing floor must not re-evaluate source CTDA or roll
                    // a fresh recovery branch every waiting physics frame.
                    var selected = _playerRecoveryTree!.Select(condition => condition.Function == 106
                        ? (-torso.Basis.Z).Dot(Vector3.Up) > 0 ? 1 : 0 : EvaluatePlayerPhysicalCondition(condition)) ??
                        throw new NotSupportedException("Settled player knockdown has no eligible original recovery IDLE.");
                    var clip = _playerRecoveryClips.GetValueOrDefault(selected.FormKey) ??
                        throw new InvalidDataException("Selected player recovery leaves its prepared original IDLE graph.");
                    _playerRagdoll.EndLivingSimulation(); _playerRagdoll.Free(); _playerRagdoll = null;
                    GlobalPosition = position.AsVector3();
                    RestorePlayerKnockdownFilters();
                    _playerRecovery = clip; _playerRecoveryClock = new(clip.Identity, 0, true);
                    _playerPhysical.CommitKnockdown(FalloutPlayerKnockdownPhase.Recovering);
                }
                AdvancePlayerRecovery(delta);
            });
        }
        catch (Exception error) when (FalloutPlayerPhysicalActivity.Ordinary(error))
        { GD.PushError("OPENNV_PLAYER_KNOCKDOWN_FAILED " + error.Message); }
        return true;
    }
    private void AdvancePlayerRecovery(double delta)
    {
        var body = _thirdPerson ?? throw new NotSupportedException("Player recovery has no actual current source body.");
        var clip = _playerRecovery!; var clock = _playerRecoveryClock!;
        var next = Math.Min(clock.Seconds + delta, clip.Duration);
        foreach (var key in clip.Events.Crossed(clock.Seconds, next, clock.StartPending))
        {
            _playerRecoveryClock = clock with { AttemptedKeyOrdinal = key.SourceOrdinal, AttemptedKeyCycle = key.Cycle };
            DispatchPhysicalKey(key);
        }
        var motion = clip.RootDisplacement(clock.Seconds, next, Translation);
        if (!motion.IsZeroApprox()) MovePhysicalPlayer(GlobalBasis * motion);
        body.Skeleton.Node.SetBonePose(body.Skeleton.BoneIndex(clip.Animation.Sequence.TargetName), Transform3D.Identity);
        clip.Animation.ApplySourceTime(clip.SourceTime(next));
        _playerRecoveryClock = clock with { Seconds = next, StartPending = false };
        if (next < clip.Duration) return;
        PhysicalPlayer.CommitKnockdown(FalloutPlayerKnockdownPhase.Upright);
        _playerRecovery = null; _playerRecoveryClock = null;
        RetirePlayerPhysicalView();
        _physicalInput?.Invoke(); _physicalInput = null;
    }
    private void ApplyPlayerRecoveryRoot(FalloutNifAnimationSample sample)
    {
        // Source accumulation is extracted once by AdvancePlayerRecovery into
        // the real capsule. Pose-only publication validates this channel and
        // does not translate the player or replay a cold displacement.
        _ = Translation(sample);
    }
    private void RestorePlayerKnockdownFilters()
    {
        foreach (var (collider, filter) in _playerKnockdownFilters)
            if (GodotObject.IsInstanceValid(collider)) GamebryoReferenceEnableRuntime.SetCollisionFilter(collider, filter.Layer, filter.Mask);
        _playerKnockdownFilters.Clear();
    }
    private FalloutPlayerKnockdownSnapshot? CapturePlayerKnockdown()
    {
        if (_playerPhysical!.KnockdownPhase == FalloutPlayerKnockdownPhase.Upright) return null;
        return new(_playerPhysical.KnockdownPhase, _playerKnockdownSkeleton!, _playerKnockdownSkeletonHash!,
            PhysicalPose(GlobalTransform), _playerRagdoll?.Capture(), _playerRecoveryClock);
    }
    private void RestorePlayerKnockdown(FalloutPlayerKnockdownSnapshot saved)
    {
        saved.Validate();
        var actor = _thirdPerson!.Actor;
        if (actor.Appearance.SkeletonPath != saved.SkeletonPath ||
            !actor.Skeleton.Source.Sha256.Equals(saved.SkeletonSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Cold player knockdown has a different original skeleton.");
        GlobalTransform = PhysicalPose(saved.Pose); _playerRagdollState = saved.Ragdoll;
        PreparePlayerKnockdown(); HoldPlayerPhysicalMotion();
        if (saved.Phase == FalloutPlayerKnockdownPhase.Simulating) _playerRagdoll!.Activate();
        else
        {
            _playerRagdoll!.Free(); _playerRagdoll = null; RestorePlayerKnockdownFilters();
            var clock = saved.Recovery!;
            _playerRecovery = _playerRecoveryClips.GetValueOrDefault(clock.Source.Idle) ??
                throw new InvalidDataException("Saved player recovery IDLE leaves its source graph.");
            if (_playerRecovery.Identity != clock.Source || clock.Seconds > _playerRecovery.Duration ||
                clock.Seconds == _playerRecovery.Duration && PhysicalPlayer.Failure is null)
                throw new InvalidDataException("Saved player recovery resource or clock changed.");
            _playerRecoveryClock = clock;
            // Complete captured bone components already published above.
            // Seeking would replace uncovered or faulted source components.
            // Cold publication dispatches no source keys or root displacement.
        }
    }
}
