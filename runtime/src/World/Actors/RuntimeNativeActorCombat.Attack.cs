using System.Buffers.Binary;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Actors;

internal sealed partial class RuntimeNativeActorCombat
{
    private readonly record struct AttackDecision(FalloutFormKey Target, string PriorAction,
        double PriorActionSeconds, float DistanceMeters, float PlanarDistanceSquared, float VerticalOffsetMeters,
        float ReachMeters, float AttackRangeMeters, float ActorRadiusMeters, float TargetRadiusUsedMeters,
        float ActorMeleeExtentMeters, float TargetMeleeExtentMeters, float ActorSafeMarginMeters, bool Visible, bool? Facing, float? FacingDot,
        string? RequestedAction, string SelectedAction, string? PursuitIdleReason);
    private AttackDecision? _attackDecision;

    private FalloutWeaponPresentation? _enemyWeapon;
    private FalloutWeaponHandling? _enemyWeaponHandling;
    private NativeActorWeaponAttachment? _enemyObject;
    private FalloutWeaponDamageResolver? _enemyDamage;
    private byte[]? _enemySkillValues;
    private float _naturalDamage, _attackRange, _baseWeaponDamage;
    private (float Base, float Multiplier)? _npcUnarmedDamageSettings;
    private NativeOwnedAnimationSoundPlayer? _enemySounds;
    private readonly Dictionary<string, NativeActorCombatAnimation> _combatClips = new(StringComparer.OrdinalIgnoreCase);
    private NativeActorCombatAnimation? _combatIdle, _combatAim, _combatGrip;
    private string _movementPath = "", _attackPath = "";
    private IReadOnlyList<string> _attackPaths = [];
    private int _attackHitCount;
    private bool Ranged => _enemyWeapon is not null && !_enemyWeapon.IsMeleeWeapon;
    private readonly (RuntimeNativeNifAnimation Animation, float SourceSeconds)[] _combatLayers = new (RuntimeNativeNifAnimation, float)[4];

    private void PrepareEngagement()
    {
        _creatureReachGameUnits = null;
        PrepareMovement();
        if (_enemySounds is null)
        {
            _enemySounds = new(_records, _content, _actor, _skeleton.UnitsToMetres, _state.SoundRandom, _state.AnimationSoundEvents);
            _actor.AddChild(_enemySounds);
        }
        var directory = _skeletonPath[.._skeletonPath.LastIndexOf('/')];
        var stats = FalloutActorTemplateOwner.Resolve(_records, _records.GetEffective(_state.Base), 2, _state.Templates);
        var statsData = stats.ReadSubrecords().Single(field => field.Signature == "DATA").Data;
        if (SelectCombatWeapon() is { } item)
            PrepareWeapon(item, stats, directory);
        else if (stats.Signature == "NPC_")
        {
            var skills = stats.ReadSubrecords().Single(field => field.Signature == "DNAM").Data;
            if (statsData.Length != 11 || skills.Length != 28)
                throw new NotSupportedException("NPC unarmed stat/skill extent is unbound.");
            _npcUnarmedDamageSettings ??= (
                FalloutGameSettingFloats.ReadRetained(_records, "fAVDUnarmedDamageBase", nameof(RuntimeNativeActorCombat)),
                FalloutGameSettingFloats.ReadRetained(_records, "fAVDUnarmedDamageMult", nameof(RuntimeNativeActorCombat)));
            _naturalDamage = NaturalAttackDamage();
            _attackRange = FalloutGameSettingFloats.ReadRetained(_records, "fCombatDistance", nameof(RuntimeNativeActorCombat)) * _skeleton.UnitsToMetres;
            var gender = _actor is RuntimeNativeNpc npc && npc.Appearance.Female ? "female" : "male";
            _movementPath = SelectPath(directory, "locomotion/h2hfastforward", "locomotion/h2hforward",
                $"locomotion/{gender}/mtfastforward", "locomotion/mtfastforward", "locomotion/mtforward");
            _attackPaths = SelectAttackPaths(directory, "h2hattackleft", "h2hattackright");
            _combatIdle = Clip(SelectPath(directory, "locomotion/mtidle", "mtidle"), true);
            _combatAim = Clip(SelectPath(directory, "h2haim"), true);
        }
        else
        {
            if (statsData.Length != 17) throw new NotSupportedException("Creature attack data extent is unbound.");
            _naturalDamage = BinaryPrimitives.ReadInt16LittleEndian(statsData.Span[8..]);
            var model = FalloutActorTemplateOwner.Resolve(_records, _records.GetEffective(_state.Base), 64, _state.Templates);
            var reach = model.ReadSubrecords().Single(field => field.Signature == "RNAM").Data.Span;
            if (reach.Length != 1) throw new InvalidDataException("Creature reach extent is invalid.");
            _creatureReachGameUnits = reach[0];
            _movementPath = SelectPath(directory, "locomotion/mtfastforward", "locomotion/mtforward", "mtforward");
            _attackPaths = SelectAttackPaths(directory, "h2hattackleft", "h2hattackright");
            _combatIdle = Clip(SelectPath(directory, "mtidle", "locomotion/mtidle"), true);
        }
        if (!float.IsFinite(CurrentAttackRange) || !float.IsFinite(_naturalDamage) || CurrentAttackRange <= 0 || _naturalDamage < 0)
            throw new InvalidDataException("Actor attack range/damage is invalid.");
        foreach (var path in _attackPaths)
            if (Clip(path, _enemyWeapon?.Automatic == true).Events.Discharges == 0)
                throw new NotSupportedException($"Actor attack {path} requires a source Hit, Fire or Release event.");
        _ = Clip(_movementPath, true);
    }

    private string SelectPath(string directory, params string[] groups)
    {
        foreach (var group in groups)
        {
            if (_content.ActorAnimations.Find(directory, group) is { } path) return path;
        }
        throw new FileNotFoundException("Source actor group is absent: " + string.Join(',', groups));
    }

    private IReadOnlyList<string> SelectAttackPaths(string directory, params string[] groups)
    {
        foreach (var group in groups)
        {
            var paths = _actor is RuntimeNativeCreature creature
                ? _content.ActorAnimations.Variants(directory, group, creature.Appearance.AnimationFiles)
                : _content.ActorAnimations.Variants(directory, group);
            if (paths.Count != 0) return paths;
        }
        throw new FileNotFoundException("Source actor attack group is absent: " + string.Join(',', groups));
    }

    private NativeActorCombatAnimation Clip(string path, bool loop)
    {
        if (!_combatClips.TryGetValue(path, out var clip))
            _combatClips.Add(path, clip = new(path, _content, _skeleton, _enemyObject, loop,
                _attackPaths.Contains(path, StringComparer.OrdinalIgnoreCase) && _enemyWeapon is { Automatic: true, AttackAnimation: 74 }));
        return clip;
    }

    private void AdvanceEngagement(RuntimeNativePlayer? player, double delta)
    {
        var state = _state.Engagement!;
        // A committed finite strike finishes its source animation. Other
        // decision boundaries may replace a broken, consumed or exhausted
        // weapon through the same inventory selection used at combat entry.
        if (_enemyWeapon is { } current &&
            (state.Action != "attack" || state.StartPending || current.Automatic) &&
            (!_enemyWeaponHandling!.CanUse(current) ||
             Ranged && current.AmmoRegenerationSeconds == 0 && !_enemyWeaponHandling.CanFire(current) && !_enemyWeaponHandling.CanReload(current)))
            state = ReselectCombatWeapon(state);
        var offset = TargetPosition(player) - _actor.GlobalPosition;
        var distance = offset.Length();
        var targetRadius = Ranged ? 0 : TargetRadius(player);
        var meleeExtent = Ranged ? 0 : MeleeSurfaceExtent;
        var targetMeleeExtent = Ranged ? 0 : TargetMeleeExtent(player);
        var reach = CurrentAttackRange + meleeExtent + targetMeleeExtent;
        var priorAction = state.Action;
        var priorSeconds = state.Seconds;
        bool? decisionFacing = null;
        float? decisionFacingDot = null;
        string? requestedAction = null, pursuitIdleReason = null;
        var visible = CanSeeTarget(player);
        Vector3? destination = null;
        var requestedPursuit = false;
        // Finite swings and reloads retain their committed animation. An
        // automatic attack has no end boundary, so it must reconsider the
        // target or it can remain firing/holding behind cover forever instead
        // of returning control to pursuit.
        if (state.Action is "pursue" or "idle" || state.Action == "attack" && _enemyWeapon?.Automatic == true)
        {
            var direction = offset; direction.Y = 0;
            var facingDot = direction.IsZeroApprox() ? 1 : (-_actor.GlobalBasis.Z).Normalized().Dot(direction.Normalized());
            var facing = facingDot > .95f;
            decisionFacing = facing; decisionFacingDot = facingDot;
            var action = distance > reach || !visible ? "pursue" : facing ? "attack" : "idle";
            requestedAction = action;
            if (action == "pursue")
            {
                requestedPursuit = true;
                destination = PursuitTarget(TargetPosition(player), delta, Ranged ? _radius * 2 + TargetRadius(player) : reach);
                if (destination is null && state.PathTurn is null)
                {
                    action = "idle";
                    pursuitIdleReason = _routeSearch is not null ? "route-query-pending" :
                        _routeError is not null ? "route-source-refusal" :
                        _coarseRouteError is not null ? "route-physical-refusal" : "no-unconsumed-waypoint";
                }
            }
            if (action != state.Action) state = state.Transition(action);
        }
        if (!requestedPursuit) RetireUnusedPursuitSearch();
        if (state.Action == "attack" && state.StartPending && Ranged && !_enemyWeaponHandling!.CanFire(_enemyWeapon!))
        {
            state = _enemyWeapon!.AmmoRegenerationSeconds > 0 ? state.Transition("idle") : BeginWeaponReload(state);
        }
        // Retain only values from this actual decision; no diagnostic raycast
        // or navigation query, and no per-frame reference object allocation.
        _attackDecision = new(state.Target, priorAction, priorSeconds, distance,
            offset.X * offset.X + offset.Z * offset.Z, offset.Y, reach, CurrentAttackRange,
            _radius, targetRadius, meleeExtent, targetMeleeExtent, _mover!.SafeMargin, visible, decisionFacing, decisionFacingDot,
            requestedAction, state.Action, pursuitIdleReason);
        if (state.Action == "attack")
        {
            state = FalloutAttackAnimationSelection.Bind(state, _attackPaths,
                path => Clip(path, _enemyWeapon?.Automatic == true).Hash);
            _attackPath = state.Animation!;
            _state.AttackRandom.Restore(state.AttackRandomState!.Value);
            _state.Engagement = state;
        }
        FalloutActorPathTurn? pathTurn = null;
        var turnLeft = false;
        var headingTarget = destination;
        if (headingTarget is null && state.Action == "pursue" && state.PathTurn is { } pendingTurn)
            headingTarget = new Vector3(pendingTurn.TargetX, pendingTurn.TargetY, pendingTurn.TargetZ);
        var turning = state.Action == "pursue" && headingTarget is { } facingTarget &&
            FacePath(facingTarget, delta, state.PathTurn, out pathTurn, out turnLeft);
        if (state.Action != "pursue" || headingTarget is null)
        { _routeTurning = false; TurnToward(TargetPosition(player), delta); }
        var path = turning ? PathTurnClip(turnLeft).Path : state.Action switch
        {
            "pursue" => _movementPath,
            "attack" => _attackPath,
            "idle" => _combatIdle!.Path,
            "reload" => SelectPath(_skeletonPath[.._skeletonPath.LastIndexOf('/')], _enemyWeapon!.AnimationGroup + _enemyWeapon.ReloadGroup),
            _ => throw new InvalidDataException("Actor engagement action is invalid.")
        };
        var clip = Clip(path, state.Action is "pursue" or "idle");
        if (state.Action == "pursue" && state.Animation is { } previous &&
            !previous.Equals(path, StringComparison.OrdinalIgnoreCase))
        {
            if (!IsPathClip(previous) || !state.AnimationHash!.Equals(Clip(previous, true).Hash, StringComparison.OrdinalIgnoreCase))
                throw new NotSupportedException("Saved pursuit animation differs from the winning source.");
            state = state.Transition("pursue");
        }
        if (state.Animation is not null && (!state.Animation.Equals(path, StringComparison.OrdinalIgnoreCase) ||
            !state.AnimationHash!.Equals(clip.Hash, StringComparison.OrdinalIgnoreCase)))
            throw new NotSupportedException("Saved combat animation differs from the winning source.");
        state = state with { Animation = path, AnimationHash = clip.Hash, PathTurn = pathTurn };
        if (state.Action == "attack") _attackHitCount = clip.Events.Discharges;
        double factor = state.Action is "attack" or "reload" ? _enemyWeapon?.AnimationMultiplier ?? 1 : 1;
        if (state.Action == "attack" && _enemyWeapon?.Automatic == true)
        {
            var sequence = clip.Animation.Sequence;
            var duration = sequence.StopTime - sequence.StartTime;
            var rate = _enemyWeapon.AttackShotsPerSecond;
            if (!float.IsFinite(rate) || rate <= 0 || !float.IsFinite(sequence.Frequency) || sequence.Frequency <= 0 ||
                duration <= 0 || _attackHitCount <= 0)
                throw new InvalidDataException("Actor automatic attack cadence is invalid.");
            factor = clip.Events.Period * rate / clip.Events.Discharges;
        }
        else if (state.Action == "attack") factor *= _enemyWeapon?.AttackMultiplier ?? 1;
        if (!double.IsFinite(factor) || factor <= 0) throw new InvalidDataException("Actor attack animation rate is invalid.");
        var next = state.Seconds + delta * factor;
        if (state.Action == "reload" || state.Action == "attack" && _enemyWeapon?.Automatic != true)
            next = Math.Min(next, clip.Duration);
        // Inside weapon range is not arrival when the target is occluded.
        // Waiting to face a visible target uses the idle, never a running clip.
        var moving = state.Action == "pursue" && destination.HasValue && !turning;
        Activity.SetMovement(running: moving, sneaking: false);
        MoveActor(moving ? clip.RootDisplacement(state.Seconds, next) : Vector3.Zero, delta, destination);
        foreach (var key in clip.Events.Crossed(state.Seconds, next, state.StartPending))
        {
            PublishCombatPose(clip, key.SourceSeconds, next);
            foreach (var text in key.Text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Select(value => value.Trim()))
            {
                if (state.Action == "attack" && FalloutWeaponAnimationTimeline.DischargesWeapon(text)) AttackTarget(player);
                else _enemySounds!.Dispatch(key with { Text = text });
            }
        }
        PublishCombatPose(clip, clip.Time(next), next);
        state = state with { Seconds = next, StartPending = false };
        var finiteAction = state.Action == "reload" || state.Action == "attack" && _enemyWeapon?.Automatic != true;
        if (finiteAction && next >= clip.Duration)
        {
            if (state.Action == "reload") _enemyWeaponHandling!.CompleteReload(_enemyWeapon!);
            state = state.Transition("pursue");
        }
        if (state.Action == "attack" && _enemyWeapon?.Automatic == true && Ranged &&
            !_enemyWeaponHandling!.CanFire(_enemyWeapon))
        {
            state = _enemyWeapon.AmmoRegenerationSeconds > 0 ? state.Transition("idle") : _enemyWeaponHandling.CanReload(_enemyWeapon)
                ? BeginWeaponReload(state) : ReselectCombatWeapon(state);
        }
        _state.Engagement = state;
    }

    private FalloutActorEngagement BeginWeaponReload(FalloutActorEngagement state)
    {
        var weapon = _enemyWeapon!;
        var path = _skeletonPath[.._skeletonPath.LastIndexOf('/')] + "/" + weapon.AnimationGroup + weapon.ReloadGroup + ".kf";
        if (_content.TryResolve(path, null, out _)) return state.Transition("reload");
        if (_actor is not RuntimeNativeCreature || weapon.Model is not null || weapon.NpcsUseAmmo)
            throw new FileNotFoundException("Source actor reload group is absent: " + path);
        // Embedded, ammo-free creature weapons have no magazine manipulation
        // clip. Refill their virtual magazine at the attack boundary; never
        // borrow a humanoid animation or manufacture carried ammunition.
        // This recovery policy is explicit; retail recharge cadence is unmeasured.
        _enemyWeaponHandling!.CompleteReload(weapon);
        GD.Print($"OPENNV_ACTOR_EMBEDDED_RECHARGE reference={_state.Reference} weapon={weapon.Form} loaded={_enemyWeaponHandling.Loaded(weapon.Form)} boundary=attack-end retailCadence=unmeasured");
        return state.Transition("pursue");
    }

    private void PublishCombatPose(NativeActorCombatAnimation clip, float seconds, double elapsed)
    {
        _skeleton.ResetSourceBonePoses();
        _skeleton.Node.SetBonePose(_skeleton.BoneIndex(clip.Animation.Sequence.TargetName), Transform3D.Identity);
        var count = 0;
        _combatLayers[count++] = (_combatIdle!.Animation, _combatIdle.Time(elapsed));
        if (_combatAim is not null) _combatLayers[count++] = (_combatAim.Animation, _combatAim.Time(elapsed));
        if (_combatGrip is not null) _combatLayers[count++] = (_combatGrip.Animation, _combatGrip.Time(elapsed));
        _combatLayers[count++] = (clip.Animation, seconds);
        RuntimeNativeNifAnimation.ApplyLayers(_combatLayers.AsSpan(0, count));
    }

    private void AttackTarget(RuntimeNativePlayer? player)
    {
        ++_attacks;
        if (TargetHealth(player) <= 0) return;
        if (Ranged) { ShootTarget(player); return; }
        var distance = _actor.GlobalPosition.DistanceTo(TargetPosition(player));
        var allowed = CurrentAttackRange + MeleeSurfaceExtent + TargetMeleeExtent(player);
        var before = TargetHealth(player);
        var resolvedDamage = _enemyWeapon is null ? new FalloutWeaponDamage(NaturalAttackDamage(), 1, 0, 1) :
            _enemyDamage!.Resolve(_enemyWeapon.Form, _baseWeaponDamage);
        var strikeBone = _actor is RuntimeNativeNpc
            ? _enemyWeapon is null && _attackPath.Contains("attackleft", StringComparison.OrdinalIgnoreCase) ? "Bip01 L Hand" : "Bip01 R Hand"
            : (_world.BodyParts(_state.Reference).Parts.SingleOrDefault(part => part.Type == 1) ??
                _world.BodyParts(_state.Reference).Parts.Single(part => part.Type == 0)).Node;
        var bone = _skeleton.BoneIndex(strikeBone);
        var origin = bone >= 0
            ? (_skeleton.Node.GlobalTransform * _skeleton.Node.GetBoneGlobalPose(bone)).Origin
            : _actor.GlobalPosition + Vector3.Up * _radius;
        byte? part = null;
        if (distance <= allowed && CanSeeTarget(player) && _opponent is null && player!.CombatHitPartFrom(origin, _actor) is { } selectedPart)
        {
            part = selectedPart;
            _world.RecordActorWeaponContact(_records.RuntimeFormKey(0x14), _state.Reference, _enemyWeapon?.Form, resolvedDamage.WeaponScriptInstance);
            _context!.DamagePlayer(resolvedDamage, selectedPart);
            if (_enemyWeapon is { } weapon) player!.ReceiveWeaponTouchEffects(_state.Reference, weapon.Form);
            player!.ReceiveMeleeKnockback(_actor.GlobalPosition, resolvedDamage.Amount);
            ++_hits;
        }
        else if (distance <= allowed && _opponent is not null && TargetContact(origin) is { } collider)
        {
            part = _opponent.Hit(collider, resolvedDamage, _state.Reference, _context!.Level(), _context.Globals,
                hitWeapon: _enemyWeapon?.Form).Part;
            _opponent.ReceiveMeleeKnockback(_actor.GlobalPosition, resolvedDamage.Amount);
            ++_hits;
        }
        if (_enemyWeapon?.IsMeleeWeapon == true)
            _enemyWeaponHandling!.ApplyMeleeConditionWear(_enemyWeapon, _records);
        if (_enemyWeapon?.Sounds.TryGetValue("empty", out var swing) == true) _enemySounds!.DispatchSound(swing);
        _lastAttack = new
        {
            kind = "source-melee-Hit",
            source = _state.Base.ToString(),
            range = allowed,
            distance,
            part,
            healthBefore = before,
            healthAfter = TargetHealth(player),
            damage = resolvedDamage.Amount,
            limbDamage = part is null ? (float?)null : resolvedDamage.Amount * resolvedDamage.LimbMultiplier
        };
        GD.Print($"OPENNV_ACTOR_ATTACK reference={_state.Reference} kind=melee target={_state.Engagement?.Target} part={part} health={before:R}->{TargetHealth(player):R}");
    }

    private float NaturalAttackDamage()
    {
        var damage = _actor is RuntimeNativeNpc && _npcUnarmedDamageSettings is { } settings
            ? settings.Base + settings.Multiplier * _world.ActorValue(_state.Reference, "Unarmed")
            : _naturalDamage;
        if (!float.IsFinite(damage) || damage < 0)
            throw new InvalidDataException("Actor natural attack damage is invalid.");
        return damage;
    }
}
