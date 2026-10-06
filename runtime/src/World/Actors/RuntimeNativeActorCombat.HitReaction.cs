using System.Security.Cryptography;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Actors;

internal sealed partial class RuntimeNativeActorCombat
{
    private FalloutHitReactionTree? _hitReactionTree;
    private NativeActorCombatAnimation? _hitReactionClip;
    private NativeOwnedAnimationSoundPlayer? _hitReactionSounds;
    private string? _hitReactionError;
    private object? _lastHitReaction;
    private long _hitReactionsStarted, _hitReactionsCompleted;
    internal bool ReactingToHit => _state.HitReaction is not null;
    internal object HitReactionObservation => new
    {
        state = _state.HitReaction,
        started = _hitReactionsStarted,
        completed = _hitReactionsCompleted,
        last = _lastHitReaction,
        error = _hitReactionError,
        sounds = _hitReactionSounds?.State,
        boundary = "source-IDLE-conditions-and-KF;blend-in-out-and-forced-power-attack-triggers-unmatched"
    };

    private void RequestHitReaction(FalloutActorHit hit, int hitLocation)
    {
        if (hit.Dead || hit.HealthDamage <= 0 || _state.Unconscious || _state.KnockedDown) return;
        // Repeated pellets retain the active response and its random state.
        // They cannot rewind text keys or postpone combat forever. Retail
        // interruption priorities remain an explicit boundary.
        if (_state.HitReaction is { } active)
        {
            _lastHitReaction = new { hit.Part, hitLocation, selected = active.Idle, disposition = "retained-active-reaction" };
            return;
        }
        var attempt = _state.HitReactionFaults.BeginAttempt();
        var randomBefore = _state.HitReactionRandom.State;
        var consumed = new List<FalloutHitReactionPredicateRead>();
        FalloutCondition? failedCondition = null;
        try
        {
            _hitReactionTree ??= new(_records, _skeletonPath);
            var selected = _hitReactionTree.Select(condition =>
            {
                var before = _state.HitReactionRandom.State;
                try
                {
                    var actual = HitReactionCondition(condition, hitLocation);
                    consumed.Add(new(FalloutHitReactionFaults.Source(condition.Owner), FalloutHitReactionFaults.Ordinal(condition),
                        actual, before, _state.HitReactionRandom.State));
                    return actual;
                }
                catch { failedCondition = condition; throw; }
            });
            _lastHitReaction = new
            {
                hit.Part,
                hitLocation,
                selected = selected?.FormKey,
                visited = _hitReactionTree.LastVisited,
                disposition = selected is null ? "source-conditions-reject" : "source-selected"
            };
            if (selected is null) return;
            var source = FalloutActorIdleSource.Resolve(_records, selected);
            var timing = FalloutIdleAnimationData.Read(selected);
            if (source.Objects.Count != 0 || timing.ReplayDelaySeconds != 0 || timing.SelectAdditionalLoops(_state.HitReactionRandom.NextBounded) != 0)
                throw new NotSupportedException("Hit reaction requires animated-object, replay-delay or repeated-idle ownership.");
            _hitReactionClip = new(source.AnimationPath, _content, _skeleton, _enemyObject, false);
            if (_hitReactionClip.Animation.Sequence.CycleType != 2)
                throw new NotSupportedException("Hit reaction requires a finite source KF.");
            var p = _actor.GlobalPosition; var q = _actor.GlobalBasis.Orthonormalized().GetRotationQuaternion();
            _state.HitReaction = new(selected.FormKey, Convert.ToHexString(SHA256.HashData(selected.ReadData())), hit.Part,
                new(_hitReactionClip.Path, _hitReactionClip.Hash, 0, true), [p.X, p.Y, p.Z], [q.X, q.Y, q.Z, q.W]);
            if (_state.Engagement is { } engagement) _state.Engagement = engagement.Transition("pursue");
            StopPackageMotion();
            Activity.SetMovement(false, false);
            _hitReactionsStarted++;
            _hitReactionError = null;
            _state.HitReactionFaults.ClearCurrentError();
            GD.Print($"OPENNV_ACTOR_HIT_REACTION reference={_state.Reference} part={hit.Part} idle={selected.FormKey} animation={source.AnimationPath}");
        }
        catch (Exception error)
        {
            var captured = false;
            if (failedCondition is { } site && site.Function != 77 && _state.HitReaction is null)
            {
                try
                {
                    var visited = _hitReactionTree!.LastVisited.Select(key => FalloutHitReactionFaults.Source(_records.GetEffective(key))).ToArray();
                    _state.HitReactionFaults.Record(new(attempt, hit.Part, hitLocation,
                        FalloutHitReactionFaults.Source(site.Owner), FalloutHitReactionFaults.Ordinal(site), site.Function,
                        error.Message, randomBefore, _state.HitReactionRandom.State, visited, consumed.ToArray()), _records);
                    captured = true;
                }
                catch (Exception receiptError)
                {
                    _state.HitReactionFaultCaptureBlocker ??= "Hit-reaction read receipt is unbound: " + receiptError.Message;
                }
            }
            ReportHitReactionError(error, captured);
        }
    }

    private float HitReactionCondition(FalloutCondition condition, int hitLocation) => condition.Function switch
    {
        14 when condition.Argument1 is >= 25 and <= 31 => _world.BodyParts(_state.Reference)
            .LimbCondition((int)condition.Argument1, _world.Health(_state.Reference).Base, _state.Injury?.LimbDamage),
        25 => Activity.Running || _actor is RuntimeNativeNpc { Traveling: true } ? 1 : 0,
        49 => 0, // No sleeping procedure is currently owned by a resident actor.
        72 => _state.Base == condition.FormArgument1 ? 1 : 0,
        77 => _state.HitReactionRandom.NextBounded(100),
        107 => _state.KnockedDown ? 2 : 0,
        159 => _actor is RuntimeNativeNpc npc ? npc.SittingState : 0,
        286 => Activity.Sneaking ? 1 : 0,
        391 => hitLocation,
        392 => 0, // NPC and creature bodies are third-person actors.
        601 => 0, // Ordinary damage does not fabricate a forced power-attack reaction.
        _ => _actor is RuntimeNativeNpc npc ? npc.EvaluateAiCondition(condition) :
            ((RuntimeNativeCreature)_actor).PackageCondition(condition)
    };

    private bool AdvanceHitReaction(double delta)
    {
        if (_state.HitReaction is not { } state) return false;
        if (_hitReactionError is not null) return true;
        try
        {
            if (_hitReactionClip is null)
            {
                var source = FalloutActorIdleSource.Resolve(_records, _records.GetEffective(state.Idle));
                if (source.AnimationPath != state.Animation.Resource) throw new InvalidDataException("Saved hit-reaction resource changed.");
                _hitReactionClip = new(source.AnimationPath, _content, _skeleton, _enemyObject, false);
                if (!_hitReactionClip.Hash.Equals(state.Animation.Sha256, StringComparison.OrdinalIgnoreCase) ||
                    _hitReactionClip.Animation.Sequence.CycleType != 2 || state.Animation.ElapsedSeconds >= _hitReactionClip.Duration)
                    throw new InvalidDataException("Saved hit-reaction KF or clock differs from source.");
                _actor.GlobalTransform = new(new Basis(new Quaternion(state.Rotation[0], state.Rotation[1], state.Rotation[2], state.Rotation[3]))
                    .Scaled(_actor.Scale), new(state.Position[0], state.Position[1], state.Position[2]));
                GD.Print($"OPENNV_ACTOR_HIT_REACTION_RESUMED reference={_state.Reference} idle={state.Idle} seconds={state.Animation.ElapsedSeconds:R}");
            }
            var clip = _hitReactionClip;
            if (_hitReactionSounds is null)
            {
                _hitReactionSounds = new(_records, _content, _actor, _skeleton.UnitsToMetres, _state.SoundRandom, _state.AnimationSoundEvents);
                _actor.AddChild(_hitReactionSounds);
            }
            PrepareMovement();
            var from = state.Animation.ElapsedSeconds;
            var next = Math.Min(from + delta, clip.Duration);
            MoveActor(clip.RootDisplacement(from, next), delta);
            foreach (var key in clip.Events.Crossed(from, next, state.Animation.StartPending))
                _hitReactionSounds.Dispatch(key);
            _skeleton.Node.ResetBonePoses();
            _skeleton.Node.SetBonePose(_skeleton.BoneIndex(clip.Animation.Sequence.TargetName), Transform3D.Identity);
            clip.Animation.ApplySourceTime(clip.Time(next));
            var p = _actor.GlobalPosition; var q = _actor.GlobalBasis.Orthonormalized().GetRotationQuaternion();
            _state.HitReaction = state with
            {
                Animation = state.Animation with { ElapsedSeconds = next, StartPending = false },
                Position = [p.X, p.Y, p.Z],
                Rotation = [q.X, q.Y, q.Z, q.W]
            };
            if (next >= clip.Duration)
            {
                _state.HitReaction = null; _hitReactionClip = null; _hitReactionsCompleted++;
                _state.KnockedDown = false;
                GD.Print($"OPENNV_ACTOR_HIT_REACTION_END reference={_state.Reference} idle={state.Idle}");
            }
        }
        catch (Exception error) { ReportHitReactionError(error); }
        return true;
    }

    private void ReportHitReactionError(Exception error, bool capturedRead = false)
    {
        if (_hitReactionError != error.Message)
            GD.PushError($"OPENNV_ACTOR_HIT_REACTION_UNBOUND reference={_state.Reference} {error.Message}");
        _hitReactionError = error.Message;
        if (!capturedRead) _state.HitReactionFaultCaptureBlocker ??= "Hit reaction has no retained source/pose continuation: " + error.Message;
    }
}
