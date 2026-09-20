using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Actors;

internal sealed record NativeActorCombatContext(Func<RuntimeNativePlayer?> Player, Func<GameplayVitals> Vitals,
    Action<float, byte, float> DamagePlayer, Func<Vector3, Vector3, Vector3[]> Route, Func<Vector3, bool> Resident,
    Func<int> Level, FalloutGlobalState Globals, float StepHeight, float Gravity);

internal sealed partial class RuntimeNativeActorCombat
{
    private NativeActorCombatContext? _context;
    private FalloutActorThreat? _threat;
    private uint? _relation;
    private float _detectRange;
    private double _detectionClock;
    private string? _engagementError;
    private bool _engagementPrepared;
    private long _attacks, _hits;
    private object? _lastAttack;
    private FalloutActorActivityState Activity => _actor is RuntimeNativeNpc npc ? npc.Activity : ((RuntimeNativeCreature)_actor).Activity;
    internal bool OwnsPose => _context is not null && _state.Engagement is not null;
    private object EngagementObservation => new
    {
        state = _state.Engagement, threat = _threat, relation = _relation, attacks = _attacks, hits = _hits,
        lastAttack = _lastAttack, error = _engagementError, motion = MotionObservation,
        boundary = "source-aggression-and-provoked-combat;confidence-threat-ratios-stealth-avoidance-cover-assistance-and-retail-tactics-unmatched"
    };

    private void RestoreEngagementPose()
    {
        if (_state.Engagement is not { Position: { } position, Rotation: { } rotation }) return;
        _actor.GlobalTransform = new(new Basis(new Quaternion(rotation[0], rotation[1], rotation[2], rotation[3]))
            .Scaled(_actor.Scale), new(position[0], position[1], position[2]));
    }

    private FalloutActorEngagement? CaptureEngagement()
    {
        if (_state.Engagement is not { } state) return null;
        var p = _actor.GlobalPosition; var q = _actor.GlobalBasis.Orthonormalized().GetRotationQuaternion();
        return _state.Engagement = state with { Position = [p.X, p.Y, p.Z], Rotation = [q.X, q.Y, q.Z, q.W],
            WeaponHandling = _enemyWeaponHandling?.Capture() ?? state.WeaponHandling };
    }

    private void Provoke(FalloutFormKey attacker)
    {
        if (Dead || _state.Unconscious) return;
        _state.Engagement ??= new(attacker);
        Activity.RecordAttack(); Activity.SetAlerted(true); Activity.SetCombat(true);
    }

    public override void _PhysicsProcess(double delta)
    {
        _enemyMuzzle?.Advance(delta);
        if (_context is null || Dead || !_state.Enabled || _state.Unconscious || _engagementError is not null) return;
        var player = _context.Player();
        if (player is null || !player.CollisionResident || !_context.Resident(_actor.GlobalPosition)) return;
        try
        {
            if (_state.Engagement is null)
            {
                _detectionClock -= delta;
                if (_detectionClock > 0) return;
                _detectionClock = .2; // broad-phase scheduling; source rules own eligibility
                _threat ??= FalloutActorThreat.Read(_records, _state.Base);
                if (_threat.Aggression == 0 || _threat.Confidence == 0 || _context.Vitals().HitPoints == 0) return;
                _relation ??= FalloutActorThreat.Relation(_records, _state.Base, _records.RuntimeFormKey(7));
                if (!_threat.Initiates(_relation.Value)) return;
                _detectRange = (_threat.RadiusBehavior ? _threat.Radius : FalloutGameSettingFloats.Read(_records, "fSneakMaxDistance")) * _skeleton.UnitsToMetres;
                if (_actor.GlobalPosition.DistanceTo(player.GlobalPosition) > _detectRange || !CanSee(player)) return;
                _state.Engagement = new(_records.RuntimeFormKey(0x14));
            }
            if (_state.Engagement.Target != _records.RuntimeFormKey(0x14))
                throw new NotSupportedException("This engagement requires a resident non-player target adapter.");
            _threat ??= FalloutActorThreat.Read(_records, _state.Base);
            _detectRange = (_threat.RadiusBehavior ? _threat.Radius : FalloutGameSettingFloats.Read(_records, "fSneakMaxDistance")) * _skeleton.UnitsToMetres;
            if (!float.IsFinite(_detectRange) || _detectRange <= 0) throw new InvalidDataException("Actor threat radius is invalid.");
            if (_threat.Confidence == 0)
            {
                if (!_engagementPrepared)
                {
                    PrepareFlee(); _engagementPrepared = true;
                    _state.CaptureEngagement = CaptureEngagement;
                    Activity.SetAlerted(true); Activity.SetCombat(true); Activity.SetWeaponDrawn(false);
                }
                AdvanceFlee(player, delta);
                return;
            }
            if (!_engagementPrepared)
            {
                PrepareEngagement(); _engagementPrepared = true;
                _state.CaptureEngagement = CaptureEngagement;
                Activity.SetAlerted(true); Activity.SetCombat(true); Activity.SetWeaponDrawn(_enemyWeapon is not null);
            }
            AdvanceEngagement(player, delta);
        }
        catch (Exception error)
        {
            _engagementError = error.Message;
            if (_actor is CharacterBody3D body) body.Velocity = Vector3.Zero;
            GD.PushError($"OPENNV_ACTOR_COMBAT_UNBOUND reference={_state.Reference} {error.Message}");
        }
    }

    private void PrepareFlee()
    {
        PrepareMovement();
        _enemySounds = new(_records, _content, _actor, _skeleton.UnitsToMetres, _state.SoundRandom);
        _actor.AddChild(_enemySounds);
        var directory = _skeletonPath[.._skeletonPath.LastIndexOf('/')];
        var gender = _actor is RuntimeNativeNpc npc && npc.Appearance.Female ? "female/" : "";
        _movementPath = SelectPath(directory, $"locomotion/{gender}mtfastforward", $"locomotion/{gender}mtforward",
            "locomotion/mtfastforward", "locomotion/mtforward", "mtforward");
        _combatIdle = Clip(SelectPath(directory, "locomotion/mtidle", "mtidle"), true);
        _ = Clip(_movementPath, true);
    }

    private void AdvanceFlee(RuntimeNativePlayer player, double delta)
    {
        var state = _state.Engagement!;
        var offset = _actor.GlobalPosition - player.GlobalPosition;
        offset.Y = 0;
        var distance = offset.Length();
        if (distance >= _detectRange)
        {
            state = state.Transition("idle");
            Activity.SetMovement(running: false, sneaking: false);
            Activity.SetCombat(false);
            _state.Engagement = state;
            return;
        }
        if (offset.IsZeroApprox()) offset = _actor.GlobalBasis.Z;
        var target = _actor.GlobalPosition + offset.Normalized() * _detectRange;
        var clip = Clip(_movementPath, true);
        if (state.Action != "flee") state = state.Transition("flee");
        if (state.Animation is not null && (!state.Animation.Equals(clip.Path, StringComparison.OrdinalIgnoreCase) ||
            !state.AnimationHash!.Equals(clip.Hash, StringComparison.OrdinalIgnoreCase)))
            throw new NotSupportedException("Saved flee animation differs from the winning source.");
        state = state with { Animation = clip.Path, AnimationHash = clip.Hash };
        var next = state.Seconds + delta;
        var destination = PursuitTarget(target, delta);
        TurnToward(destination, delta);
        Activity.SetMovement(running: true, sneaking: false);
        MoveActor(clip.RootDisplacement(state.Seconds, next), delta);
        foreach (var key in clip.Events.Crossed(state.Seconds, next, state.StartPending))
            _enemySounds!.Dispatch(key);
        PublishCombatPose(clip, clip.Time(next), next);
        _state.Engagement = state with { Seconds = next, StartPending = false };
    }

    private bool CanSee(RuntimeNativePlayer player)
    {
        var head = _skeleton.BoneIndex(_actor is RuntimeNativeNpc ? "Bip01 Head" :
            _world.BodyParts(_state.Reference).Parts.Single(part => part.Type == 1).Node);
        if (head < 0) throw new NotSupportedException("Actor sight requires its source head bone.");
        var eye = (_skeleton.Node.GlobalTransform * _skeleton.Node.GetBoneGlobalPose(head)).Origin;
        using var query = PhysicsRayQueryParameters3D.Create(eye, player.Camera.GlobalPosition, player.CollisionMask);
        return _actor.GetWorld3D().DirectSpaceState.IntersectRay(query).Count == 0;
    }

    public override void _ExitTree()
    {
        CaptureEngagement(); _state.CaptureEngagement = null;
    }
}
