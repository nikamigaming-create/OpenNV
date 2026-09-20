using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Actors;

internal sealed record NativeActorCombatContext(Func<RuntimeNativePlayer?> Player, Func<GameplayVitals> Vitals,
    Action<FalloutWeaponDamage, byte> DamagePlayer, Func<Vector3, Vector3, Vector3[]> Route, Func<Vector3, bool> Resident,
    Func<int> Level, FalloutGlobalState Globals, float StepHeight, float Gravity,
    Action<FalloutFormKey, string>? DispatchEvent = null, Func<FalloutFormKey?>? PlayerCell = null);

internal sealed partial class RuntimeNativeActorCombat
{
    private const string CombatActorsGroup = "OpenNVNativeCombatActors";
    private NativeActorCombatContext? _context;
    private FalloutActorThreat? _threat;
    private uint? _relation;
    private float _detectRange;
    private double _detectionClock;
    private string? _engagementError;
    internal string? EngagementError => _engagementError;
    private string? _assistanceError;
    private bool _engagementPrepared;
    private long _attacks, _hits, _assistsReceived;
    private int _pendingHitscanImpacts;
    private object? _lastAttack;
    private object? _lastHitscanImpact;
    private object? _lastExplosion;
    private FalloutActorActivityState Activity => _actor is RuntimeNativeNpc npc ? npc.Activity : ((RuntimeNativeCreature)_actor).Activity;
    internal bool OwnsPose => _context is not null && _state.Engagement is not null;
    internal bool Restrained => _state.Restrained;
    private object EngagementObservation => new
    {
        state = _state.Engagement,
        threat = _threat,
        relation = _relation,
        attacks = _attacks,
        hits = _hits,
        assistsReceived = _assistsReceived,
        pendingHitscanImpacts = _pendingHitscanImpacts,
        lastAttack = _lastAttack,
        lastHitscanImpact = _lastHitscanImpact,
        impactMaterialError = _impactMaterialError,
        lastExplosion = _lastExplosion,
        error = _engagementError,
        assistanceError = _assistanceError,
        motion = MotionObservation,
        boundary = "source-aggression-confidence-0-flee-and-faction-assistance;confidence-threat-ratios-stealth-avoidance-cover-and-retail-tactics-unmatched"
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
        return _state.Engagement = state with
        {
            Position = [p.X, p.Y, p.Z],
            Rotation = [q.X, q.Y, q.Z, q.W],
            WeaponHandling = _enemyWeaponHandling?.Capture() ?? state.WeaponHandling
        };
    }

    private void Provoke(FalloutFormKey attacker)
    {
        if (Dead || _state.Unconscious) return;
        if (_world.IgnoresFriendlyHits(_state.Reference) &&
            (attacker == _records.RuntimeFormKey(0x14) && _state.PlayerTeammate || _world.ActorRelation(_state.Reference, attacker) >= 2)) return;
        _state.Engagement ??= new(attacker);
        Activity.RecordAttack(); Activity.SetAlerted(true); Activity.SetCombat(true);
        NotifyAssistance(attacker);
    }

    private void NotifyAssistance(FalloutFormKey attacker)
    {
        if (attacker != _records.RuntimeFormKey(0x14) || _context?.Player() is not { } player) return;
        var threat = _threat ??= FalloutActorThreat.Read(_records, _state.Base, _state.Templates);
        if (threat.Aggression == 3) return;
        foreach (var candidate in _actor.GetTree().GetNodesInGroup(CombatActorsGroup).OfType<RuntimeNativeActorCombat>())
        {
            if (candidate == this || !ReferenceEquals(candidate._world, _world) ||
                !ReferenceEquals(candidate._context, _context)) continue;
            try
            {
                candidate.TryAssist(_state, _actor.GlobalPosition, attacker, player);
            }
            catch (Exception error)
            {
                candidate._assistanceError = error.Message;
                GD.PushError($"OPENNV_COMBAT_ASSISTANCE_UNBOUND reference={candidate._state.Reference} {error.Message}");
            }
        }
    }

    private bool TryAssist(FalloutReferenceInstance victim, Vector3 origin, FalloutFormKey attacker, RuntimeNativePlayer player)
    {
        if (Dead || !_state.Enabled || _state.Unconscious || _state.Engagement is not null ||
            _engagementError is not null || !player.CollisionResident || !_context!.Resident(_actor.GlobalPosition)) return false;
        var threat = _threat ??= FalloutActorThreat.Read(_records, _state.Base, _state.Templates);
        if (threat.Aggression == 3 || threat.Assistance == 0) return false;
        var relation = _world.ActorRelation(_state.Reference, victim.Reference);
        if (relation != 2 && !(threat.Assistance == 2 && relation == 3)) return false;
        var radius = ThreatRadius(threat);
        if (!float.IsFinite(radius) || radius <= 0 || _actor.GlobalPosition.DistanceTo(origin) > radius || !CanSee(player)) return false;
        _state.Engagement = new(attacker);
        _assistsReceived++;
        Activity.RecordAttack(); Activity.SetAlerted(true); Activity.SetCombat(true);
        _assistanceError = null;
        return true;
    }

    private float ThreatRadius(FalloutActorThreat threat) =>
        (threat.RadiusBehavior ? threat.Radius : FalloutGameSettingFloats.Read(_records, "fSneakMaxDistance")) * _skeleton.UnitsToMetres;

    public override void _PhysicsProcess(double delta)
    {
        _enemyMuzzle?.Advance(delta);
        if (_context is null || Dead || !_state.Enabled || _state.Unconscious || _state.Restrained || _engagementError is not null) return;
        var player = _context.Player();
        if (player is null || !player.CollisionResident || !_context.Resident(_actor.GlobalPosition)) return;
        try
        {
            if (_state.Engagement is null && !TryCompanionCombat(player))
            {
                _detectionClock -= delta;
                if (_detectionClock > 0) return;
                _detectionClock = .2; // broad-phase scheduling; source rules own eligibility
                _threat ??= FalloutActorThreat.Read(_records, _state.Base, _state.Templates);
                var aggression = _world.ActorValue(_state.Reference, "aggression");
                if (aggression != Math.Truncate(aggression) || aggression is < 0 or > 3) throw new InvalidDataException("Actor aggression is invalid.");
                _threat = _threat with { Aggression = (byte)aggression };
                if (_state.PlayerTeammate || _threat.Aggression == 0 || _threat.Confidence == 0 || _context.Vitals().HitPoints == 0) return;
                _relation = _world.ActorRelation(_state.Reference, _records.RuntimeFormKey(0x14));
                if (!_threat.Initiates(_relation.Value)) return;
                _detectRange = ThreatRadius(_threat);
                if (_actor.GlobalPosition.DistanceTo(player.GlobalPosition) > _detectRange || !CanSee(player)) return;
                _state.Engagement = new(_records.RuntimeFormKey(0x14));
            }
            if (!ResolveTarget(player)) return;
            _threat ??= FalloutActorThreat.Read(_records, _state.Base, _state.Templates);
            _detectRange = ThreatRadius(_threat);
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
            GD.PushError($"OPENNV_ACTOR_COMBAT_UNBOUND reference={_state.Reference} {error}");
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
        var offset = _actor.GlobalPosition - TargetPosition(player);
        offset.Y = 0;
        var distance = offset.Length();
        if (distance >= _detectRange)
        {
            EndEngagement();
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
        var head = SightBone();
        if (head < 0) throw new NotSupportedException("Actor sight requires its source head bone.");
        var eye = (_skeleton.Node.GlobalTransform * _skeleton.Node.GetBoneGlobalPose(head)).Origin;
        using var query = PhysicsRayQueryParameters3D.Create(eye, player.Camera.GlobalPosition, player.CollisionMask);
        return _actor.GetWorld3D().DirectSpaceState.IntersectRay(query).Count == 0;
    }

    public override void _ExitTree()
    {
        // A door can materialize the same reference in its destination before
        // the previous presentation leaves the tree. Only the current binding
        // may capture a pose or release the shared save callback.
        if (_state.CaptureEngagement == CaptureEngagement)
        {
            CaptureEngagement(); _state.CaptureEngagement = null;
        }
    }
}
