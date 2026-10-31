using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Actors;

internal sealed record NativeActorNavigationRoute(Vector3[] Points,
    NativeNavigationProjectionRegion? Projection = null, Func<Vector3, bool>? Arrival = null,
    Func<Vector3, int, Func<Vector3, bool>>? PrefixArrival = null,
    string? SourceSha256 = null, Func<Vector3, bool>? TerminalFloor = null,
    Func<Vector3, bool>? CorridorFloor = null)
{
    internal static Func<Vector3, bool> ReadTerminalFloor(IReadOnlyList<Vector3> path,
        Func<Vector3, int, Func<Vector3, bool>> prefix)
    {
        if (path.Count == 0) throw new InvalidDataException("Source route has no terminal floor.");
        // FindPath emits one point per traversed triangle: preceding portals,
        // then the destination. PrefixRegion indexes the triangle after that
        // portal; a single-triangle route uses its endpoint as the cut plane.
        var index = path.Count - 1;
        return prefix(path[index == 0 ? 0 : index - 1], index);
    }
}

internal sealed record NativeActorCombatContext(Func<RuntimeNativePlayer?> Player, Func<GameplayVitals> Vitals,
    Action<FalloutWeaponDamage, byte> DamagePlayer, Func<Vector3, Vector3, Vector3[]> Route, Func<Vector3, bool> Resident,
    Func<int> Level, FalloutGlobalState Globals, float StepHeight, float Gravity,
    Action<FalloutFormKey, string>? DispatchEvent = null, Func<FalloutFormKey?>? PlayerCell = null,
    float MaximumWalkableSlopeDegrees = PlayerConfiguration.DefaultMaximumWalkableSlopeDegrees,
    Func<FalloutFormKey, ulong, bool, NativeRouteDoorStatus>? RouteDoor = null,
    Func<ulong, FalloutFormKey?>? CollisionReference = null,
    Func<FalloutFormKey, FalloutFormKey, bool>? RequestPortal = null,
    float? ActivationDistanceMeters = null,
    Func<Vector3, Vector3, float, NativeActorNavigationRoute>? RouteRegion = null,
    Func<FalloutFormKey, bool>? DialogueBusy = null,
    Func<FalloutFormKey, FalloutFormKey, bool>? TryAmbientConversation = null);

internal sealed partial class RuntimeNativeActorCombat
{
    private static readonly StringName CombatActorsGroup = "OpenNVNativeCombatActors";
    private NativeActorCombatContext? _context;
    private FalloutActorThreat? _threat;
    private FalloutActorThreat? _sourceThreat;
    private FalloutActorThreat CurrentThreat() => _threat = _world.ActorThreat(_state.Reference,
        _sourceThreat ??= FalloutActorThreat.Read(_records, _state.Base, _state.Templates));
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
    internal bool OwnsPose => _state.KnockedDown || _context is not null && (_state.Engagement is not null || _state.AggroWarning is not null || ReactingToHit);
    internal bool Restrained => _state.Restrained;
    internal int? ScriptCombatProcedure => _state.Engagement is null ? null : _state.Engagement.Action == "flee" ? 16 : 13;
    internal int? ScriptCombatPackage => _state.Engagement is null ? null : _state.Engagement.Action == "flee" ? 10 : 5;
    private bool PackageAllowsUnprovokedCombat(FalloutFormKey? attackedTarget = null) => _actor switch
    {
        RuntimeNativeCreature creature => creature.PackageAllowsUnprovokedCombat(attackedTarget),
        RuntimeNativeNpc npc => npc.PackageAllowsUnprovokedCombat,
        _ => true
    };
    private object EngagementObservation => new
    {
        state = _state.Engagement,
        ingestibles = _state.Ingestibles,
        threat = _threat,
        relation = _relation,
        attacks = _attacks,
        hits = _hits,
        assistsReceived = _assistsReceived,
        pendingHitscanImpacts = _pendingHitscanImpacts,
        enemySounds = _enemySounds?.CaptureDiagnostic,
        enemySoundLast = _enemySounds?.LastEvent,
        lastAttack = _lastAttack,
        lastHitscanImpact = _lastHitscanImpact,
        impactMaterialError = _impactMaterialError,
        lastExplosion = _lastExplosion,
        error = _engagementError,
        assistanceError = _assistanceError,
        acquisition = _lastAcquisition,
        warning = _state.AggroWarning,
        warningSounds = _warningSounds?.State,
        warningPresentationBoundary = "source-idle-and-turn;GuardTrespass-dialogue-and-special-warning-idles-unbound",
        motion = MotionObservation,
        boundary = "source-aggression-confidence-0-flee-and-faction-assistance;confidence-threat-ratios-stealth-avoidance-cover-and-retail-tactics-unmatched"
    };

    private void RestoreEngagementPose()
    {
        // Stopped AI owns the complete captured matrix. Its combat quaternion
        // is derived history and must not round that independent pose again.
        if (RestoreStoppedAiPose()) return;
        if (_state.Engagement is not { Position: { } position, Rotation: { } rotation }) return;
        _actor.GlobalTransform = new(new Basis(new Quaternion(rotation[0], rotation[1], rotation[2], rotation[3]))
            .Scaled(_actor.Scale), new(position[0], position[1], position[2]));
    }

    private FalloutActorEngagement? CaptureEngagement()
    {
        if (!Dead && _hitKnockbackError is { } knockbackFailure)
            throw new NotSupportedException($"Living actor hit knockback continuation remains unowned: {knockbackFailure}");
        if (_state.Engagement is not { } state) return null;
        if (!Dead && (Error ?? _engagementError ?? _assistanceError) is { } failure)
            throw new NotSupportedException($"Living actor combat continuation remains unowned: {failure}");
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
        _state.AggroWarning = null;
        _state.Engagement ??= new(attacker);
        Activity.RecordAttack(); Activity.SetAlerted(true); Activity.SetCombat(true);
        NotifyAssistance(attacker, attacked: true);
    }

    private void NotifyAssistance(FalloutFormKey attacker, bool attacked = false)
    {
        if (_context is null) return;
        var player = _context.Player();
        var opponent = attacker == _records.RuntimeFormKey(0x14) ? null :
            CombatActors.SingleOrDefault(actor => actor._state.Reference == attacker);
        if (opponent is null && (attacker != _records.RuntimeFormKey(0x14) || player is not { CollisionResident: true })) return;
        var threat = CurrentThreat();
        var candidates = _actor.GetTree().GetNodesInGroup(CombatActorsGroup);
        using var candidatesLifetime = (Godot.Collections.Array)candidates;
        foreach (var candidate in candidates.OfType<RuntimeNativeActorCombat>())
        {
            if (candidate == this || !ReferenceEquals(candidate._world, _world) ||
                !ReferenceEquals(candidate._context, _context)) continue;
            try
            {
                candidate.TryAssist(_state, _actor.GlobalPosition, attacker, player, opponent,
                    attacked, threat.Aggression != 3);
            }
            catch (Exception error)
            {
                candidate._assistanceError = error.Message;
                GD.PushError($"OPENNV_COMBAT_ASSISTANCE_UNBOUND reference={candidate._state.Reference} {error.Message}");
            }
        }
    }

    private bool TryAssist(FalloutReferenceInstance victim, Vector3 origin, FalloutFormKey attacker,
        RuntimeNativePlayer? player, RuntimeNativeActorCombat? opponent, bool attacked, bool factionAssistance)
    {
        if (attacker == _state.Reference || Dead || !EffectiveEnabled || _state.Unconscious || _state.Restrained || _state.Engagement is not null ||
            _engagementError is not null || !_context!.Resident(_actor.GlobalPosition)) return false;
        var passiveCombat = PackageAllowsUnprovokedCombat();
        if (!PackageAllowsUnprovokedCombat(attacked ? victim.Reference : null)) return false;
        var defendingPackageTarget = attacked && !passiveCombat;
        var threat = CurrentThreat();
        if (!defendingPackageTarget)
        {
            if (!factionAssistance || threat.Aggression == 3 || threat.Assistance == 0) return false;
            var relation = _world.ActorRelation(_state.Reference, victim.Reference);
            if (relation != 2 && !(threat.Assistance == 2 && relation == 3)) return false;
        }
        var radius = ThreatRadius(threat);
        if (!float.IsFinite(radius) || radius <= 0 || _actor.GlobalPosition.DistanceTo(origin) > radius ||
            !CanSeePoint(opponent?.BodyTargetPoint() ?? player!.CombatTargetPoint, opponent)) return false;
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
        try { AdvanceCombatPhysics(delta); AdvanceRetiringPackageAnimationLayers(delta); }
        finally { FinishHitKnockbackPhysics(delta); }
    }

    private void AdvanceCombatPhysics(double delta)
    {
        using var frameWork = OpenNV.Runtime.Diagnostics.RuntimeFrameWork.Measure(OpenNV.Runtime.Diagnostics.RuntimeFrameWork.Owner.Combat);
        _enemyMuzzle?.Advance(delta);
        if (_state.ProjectileLaunch is { } launch && EffectiveEnabled)
        {
            try
            {
                _state.ProjectileLaunch = launch.Advance(delta,
                    _records.IniSettings.Float("fAimChaseLookingMult:Combat"),
                    _records.IniSettings.Float("fAnimationMult:General"));
            }
            catch (Exception error)
            {
                _engagementError = error.Message;
                _launchRefusalOwnsError = false;
                GD.PushError($"OPENNV_ACTOR_AIM_CHASE_UNBOUND reference={_state.Reference} {error.Message}");
                return;
            }
        }
        if (Dead) { RestoreDeath(); return; }
        if (_context is null || !EffectiveEnabled || _state.Unconscious) return;
        var player = _context.Player();
        if (!_context.Resident(_actor.GlobalPosition)) return;
        if (_state.ConversationSuppressionSeconds is { } suppression)
            _state.ConversationSuppressionSeconds = FalloutConversationSuppression.Advance(suppression, delta);
        if (ReactingToHit && !_engagementPrepared && _state.Engagement is not null)
        {
            try
            {
                if (_actor is RuntimeNativeNpc npc) npc.RequireTravelCombatContinuation();
                PrepareCombatPresentation();
            }
            catch (Exception error)
            {
                if (_engagementError != error.Message) GD.PushError($"OPENNV_ACTOR_COMBAT_UNBOUND reference={_state.Reference} {error.Message}");
                _engagementError = error.Message;
                _launchRefusalOwnsError = false;
            }
        }
        if (AdvanceEssentialKnockdown(delta) || AdvanceKnockdown() || AdvanceHitReaction(delta) || _state.Restrained || _engagementError is not null) return;
        try
        {
            if (_state.Engagement is null && !TryCompanionCombat())
            {
                _detectionClock -= delta;
                if (_detectionClock <= 0)
                {
                    _detectionClock = .2; // broad-phase scheduling; source rules own eligibility
                    _threat = CurrentThreat();
                    if (!PackageAllowsUnprovokedCombat() || _state.PlayerTeammate || _threat.Confidence == 0 ||
                        _threat.Aggression == 0) ClearRadiusWarning();
                    else TryAcquireThreat(player);
                }
                if (_state.Engagement is null)
                {
                    AdvanceRadiusWarning(player, delta);
                    if (_state.Engagement is null) return;
                }
                NotifyAssistance(_state.Engagement!.Target);
            }
            // The package's destination cannot be silently replaced by enemy
            // pursuit. Continue During Combat needs simultaneous route and
            // firing ownership; refuse that phase before either advances.
            _state.AggroWarning = null;
            if (_actor is RuntimeNativeNpc npc) npc.RequireTravelCombatContinuation();
            if (!ResolveTarget(player)) return;
            StopPackageMotion(); _enemyObject?.Root.Show();
            _threat = CurrentThreat();
            _detectRange = ThreatRadius(_threat);
            if (!float.IsFinite(_detectRange) || _detectRange <= 0) throw new InvalidDataException("Actor threat radius is invalid.");
            if (_threat.Confidence == 0)
            {
                PrepareCombatPresentation();
                AdvanceFlee(player, delta);
                return;
            }
            PrepareCombatPresentation();
            AdvanceEngagement(player, delta);
        }
        catch (Exception error)
        {
            _engagementError = error.Message;
            _launchRefusalOwnsError = error is LaunchAdmissionException admission && ReferenceEquals(admission.Refusal, _launchRefusal);
            if (_actor is CharacterBody3D body) body.Velocity = Vector3.Zero;
            _previousKnockbackVertical = 0;
            GD.PushError($"OPENNV_ACTOR_COMBAT_UNBOUND reference={_state.Reference} {error}");
        }
    }

    private void PrepareCombatPresentation()
    {
        if (_engagementError is not null) return;
        if (!_engagementPrepared)
        {
            _threat = CurrentThreat();
            if (_threat.Confidence == 0) PrepareFlee();
            else PrepareEngagement();
            _engagementPrepared = true;
            _state.CaptureEngagement = CaptureEngagement;
        }
        // Models remain prepared after an engagement ends. Each new
        // engagement binds the actor's existing stream without drawing from it.
        _state.Engagement = FalloutAttackAnimationSelection.BindRandomOwner(_state.Engagement!, _state.AttackRandom.State);
        Activity.SetAlerted(true); Activity.SetCombat(true);
        Activity.SetWeaponDrawn(_enemyWeapon is not null);
    }

    private void PrepareFlee()
    {
        PrepareMovement();
        _enemySounds = new(_records, _content, _actor, _skeleton.UnitsToMetres, _state.SoundRandom, _state.AnimationSoundEvents);
        _actor.AddChild(_enemySounds);
        var directory = _skeletonPath[.._skeletonPath.LastIndexOf('/')];
        var gender = _actor is RuntimeNativeNpc npc && npc.Appearance.Female ? "female/" : "";
        _movementPath = SelectPath(directory, $"locomotion/{gender}mtfastforward", $"locomotion/{gender}mtforward",
            "locomotion/mtfastforward", "locomotion/mtforward", "mtforward");
        _combatIdle = Clip(SelectPath(directory, "locomotion/mtidle", "mtidle"), true);
        _ = Clip(_movementPath, true);
    }

    private void AdvanceFlee(RuntimeNativePlayer? player, double delta)
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
        var destination = PursuitTarget(target, delta, 0);
        if (state.Action != "flee") state = state.Transition("flee");
        FalloutActorPathTurn? pathTurn = null;
        var turnLeft = false;
        var headingTarget = destination;
        if (headingTarget is null && state.PathTurn is { } pendingTurn)
            headingTarget = new Vector3(pendingTurn.TargetX, pendingTurn.TargetY, pendingTurn.TargetZ);
        var turning = headingTarget is { } facingTarget && FacePath(facingTarget, delta, state.PathTurn, out pathTurn, out turnLeft);
        if (headingTarget is null) _routeTurning = false;
        var clip = turning ? PathTurnClip(turnLeft) : destination.HasValue ? Clip(_movementPath, true) : _combatIdle!;
        if (state.Animation is { } previous && !previous.Equals(clip.Path, StringComparison.OrdinalIgnoreCase))
        {
            if (!IsPathClip(previous) ||
                !state.AnimationHash!.Equals(Clip(previous, true).Hash, StringComparison.OrdinalIgnoreCase))
                throw new NotSupportedException("Saved flee animation differs from the winning source.");
            state = state.Transition("flee");
        }
        if (state.Animation is not null && (!state.Animation.Equals(clip.Path, StringComparison.OrdinalIgnoreCase) ||
            !state.AnimationHash!.Equals(clip.Hash, StringComparison.OrdinalIgnoreCase)))
            throw new NotSupportedException("Saved flee animation differs from the winning source.");
        state = state with { Animation = clip.Path, AnimationHash = clip.Hash, PathTurn = pathTurn };
        var next = state.Seconds + delta;
        Activity.SetMovement(running: destination.HasValue && !turning, sneaking: false);
        MoveActor(destination.HasValue && !turning ? clip.RootDisplacement(state.Seconds, next) : Vector3.Zero, delta, destination);
        foreach (var key in clip.Events.Crossed(state.Seconds, next, state.StartPending))
            _enemySounds!.Dispatch(key);
        PublishCombatPose(clip, clip.Time(next), next);
        _state.Engagement = state with { Seconds = next, StartPending = false };
    }

    private bool _sourceBindingsRetired;
    public override void _ExitTree() => RetireSourceBindings();

    internal void RetireSourceBindings()
    {
        if (_sourceBindingsRetired) return;
        _sourceBindingsRetired = true;
        try
        {
            // Ready may have queued corpse construction earlier in this frame.
            // A streamed candidate can retire before that deferred call runs.
            // Complete its actual native body while it still owns the binding.
            if (Dead && !_deathPresentationAttempted && _state.CaptureCorpseEquipment == CaptureCorpseEquipment)
                RestoreDeath();
            RetainCorpseEquipment();
            RetainStoppedPoseReadiness();
            // A destination can already own the same reference. Only the
            // current binding may retain its pose during retirement.
            if (_state.CaptureEngagement == CaptureEngagement) CaptureEngagement();
        }
        finally
        {
            if (_state.PrepareNativeInventoryRemoval == PrepareNativeInventoryRemoval) _state.PrepareNativeInventoryRemoval = null;
            if (_state.StopCombat == StopCombat) _state.StopCombat = null;
            if (_state.QuerySpatialPlacement == CaptureSpatialPlacement) _state.QuerySpatialPlacement = null;
            if (_state.CaptureEngagement == CaptureEngagement) _state.CaptureEngagement = null;
            if (_state.CanCaptureCorpseEquipment == CorpseEquipmentCaptureReady) _state.CanCaptureCorpseEquipment = null;
            if (_state.CaptureCorpseEquipment == CaptureCorpseEquipment) _state.CaptureCorpseEquipment = null;
            if (_state.CaptureCorpseEquipmentForFiniteWait == CaptureCorpseEquipmentForFiniteWait) _state.CaptureCorpseEquipmentForFiniteWait = null;
            DisposeRouteSearch();
            _stepQueries?.Dispose(); _stepQueries = null;
            _movementEdge?.Dispose(); _movementEdge = null;
            _eatObjects?.Dispose(); _eatObjects = null;
        }
    }

    internal void RestoreSourceBindings()
    {
        if (!IsInsideTree() || _sourceBindingsRetired ||
            _state.QuerySpatialPlacement is not null && _state.QuerySpatialPlacement != CaptureSpatialPlacement) return;
        _state.QuerySpatialPlacement ??= CaptureSpatialPlacement;
        _state.StopCombat ??= StopCombat;
        _state.CaptureEngagement ??= CaptureEngagement;
        _state.CanCaptureCorpseEquipment ??= CorpseEquipmentCaptureReady;
        _state.CaptureCorpseEquipment ??= CaptureCorpseEquipment;
        _state.CaptureCorpseEquipmentForFiniteWait ??= CaptureCorpseEquipmentForFiniteWait;
        _state.PrepareNativeInventoryRemoval ??= PrepareNativeInventoryRemoval;
    }
}
