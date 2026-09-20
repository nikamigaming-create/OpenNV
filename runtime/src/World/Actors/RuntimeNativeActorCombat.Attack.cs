using System.Buffers.Binary;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Actors;

internal sealed partial class RuntimeNativeActorCombat
{
    private FalloutWeaponPresentation? _enemyWeapon;
    private FalloutWeaponHandling? _enemyWeaponHandling;
    private NativeActorWeaponAttachment? _enemyObject;
    private FalloutWeaponDamageResolver? _enemyDamage;
    private float _naturalDamage, _attackRange, _baseWeaponDamage;
    private NativeOwnedAnimationSoundPlayer? _enemySounds;
    private readonly Dictionary<string, NativeActorCombatAnimation> _combatClips = new(StringComparer.OrdinalIgnoreCase);
    private NativeActorCombatAnimation? _combatIdle, _combatAim, _combatGrip;
    private string _movementPath = "", _attackPath = "";
    private int _attackHitCount;
    private bool Ranged => _enemyWeapon is not null && !_enemyWeapon.IsMeleeWeapon;
    private readonly (RuntimeNativeNifAnimation Animation, float SourceSeconds)[] _combatLayers = new (RuntimeNativeNifAnimation, float)[4];

    private void PrepareEngagement()
    {
        PrepareMovement();
        _enemySounds = new(_records, _content, _actor, _skeleton.UnitsToMetres, _state.SoundRandom); _actor.AddChild(_enemySounds);
        var directory = _skeletonPath[.._skeletonPath.LastIndexOf('/')];
        var stats = FalloutActorTemplateOwner.Resolve(_records, _records.GetEffective(_state.Base), 2);
        var statsData = stats.ReadSubrecords().Single(field => field.Signature == "DATA").Data;
        var group = "h2h";
        if (_actor is RuntimeNativeNpc npc)
        {
            var owned = _world.Inventory(_state.Reference, _context!.Level(), _context.Globals);
            var equipped = owned.Contents.Items.Where(item => item.RecordType == "WEAP" && !owned.Unequipped.Contains(item.FormKey)).ToArray();
            var selected = equipped.Where(item => owned.Contents.Equipped.Contains(item.RuntimeFormId)).ToArray();
            var item = selected.Length == 1 ? selected[0] : equipped.Length == 1 ? equipped[0] :
                throw new NotSupportedException("Actor requires unarmed damage or competing-weapon selection.");
            _enemyWeapon = FalloutWeaponPresentation.Read(_records, item.FormKey, firstPerson: false);
            if (_enemyWeapon.IsMine)
                throw new NotSupportedException("NPC mine placement and proximity detonation are unbound.");
            if (_enemyWeapon.Automatic && (!float.IsFinite(_enemyWeapon.AttackShotsPerSecond) || _enemyWeapon.AttackShotsPerSecond <= 0))
                throw new NotSupportedException("Actor automatic weapon has no valid source attack-shot rate.");
            var supportedAnimation = _enemyWeapon.IsMeleeWeapon
                ? _enemyWeapon.AnimationGroup is "h2h" or "1hm" or "2hm" or "2hh"
                : _enemyWeapon.AnimationGroup is "1hp" or "2hr" or "2ha" or "2hl" or "1gt";
            if (!supportedAnimation)
                throw new NotSupportedException("This actor weapon requires its specialized attack owner.");
            owned.Contents.Equip(_records, item.FormKey);
            _enemyObject = new(_enemyWeapon, _skeleton, _content);
            _enemyWeaponHandling = new(owned.Contents, nativeNpc: true);
            if (_state.Engagement!.WeaponHandling is { } restored)
                _enemyWeaponHandling.Restore(restored, key => FalloutWeaponPresentation.Read(_records, key, false));
            else if (Ranged) _enemyWeaponHandling.CompleteReload(_enemyWeapon);
            var data = _records.GetEffective(item.FormKey).ReadSubrecords().Single(field => field.Signature == "DATA").Data.Span;
            _baseWeaponDamage = BinaryPrimitives.ReadInt16LittleEndian(data[12..]);
            var skills = stats.ReadSubrecords().Single(field => field.Signature == "DNAM").Data.ToArray();
            if (skills.Length != 28) throw new NotSupportedException("NPC attack skill extent is unbound.");
            _enemyDamage = new(_records, owned.Contents, value => value is >= 32 and <= 45 ? skills[value - 32] :
                throw new NotSupportedException("NPC weapon skill is outside its source skill block."), () => []);
            group = _enemyWeapon.AnimationGroup;
            _attackRange = Ranged ? _enemyWeapon.MaximumRange * _skeleton.UnitsToMetres :
                _enemyWeapon.Reach * FalloutGameSettingFloats.Read(_records, "fCombatDistance") * _skeleton.UnitsToMetres;
            _movementPath = SelectPath(directory, $"locomotion/{group}fastforward", $"locomotion/{group}forward",
                $"locomotion/{(npc.Appearance.Female ? "female" : "male")}/mtfastforward", $"locomotion/{(npc.Appearance.Female ? "female" : "male")}/mtforward");
            _combatIdle = Clip(SelectPath(directory, "locomotion/mtidle"), true);
            _combatAim = Clip(SelectPath(directory, group + "aim"), true);
            if (_enemyWeapon.Grip != 255)
            {
                if (_enemyWeapon.Grip is < 230 or > 235) throw new NotSupportedException("Source weapon grip index is unbound.");
                _combatGrip = Clip(SelectPath(directory, group + "handgrip" + (_enemyWeapon.Grip - 229)), true);
            }
            var attack = group + _enemyWeapon.AttackGroup;
            _attackPath = SelectPath(directory, attack, attack + "_a", attack + "_b");
        }
        else
        {
            if (statsData.Length != 17) throw new NotSupportedException("Creature attack data extent is unbound.");
            _naturalDamage = BinaryPrimitives.ReadInt16LittleEndian(statsData.Span[8..]);
            var model = FalloutActorTemplateOwner.Resolve(_records, _records.GetEffective(_state.Base), 64);
            var reach = model.ReadSubrecords().Single(field => field.Signature == "RNAM").Data.Span;
            if (reach.Length != 1) throw new InvalidDataException("Creature reach extent is invalid.");
            _attackRange = reach[0] * _skeleton.UnitsToMetres * _skeleton.Node.Scale.X;
            _movementPath = SelectPath(directory, "locomotion/mtfastforward", "locomotion/mtforward", "mtforward");
            _attackPath = SelectPath(directory, "h2hattackleft", "h2hattackright");
            _combatIdle = Clip(SelectPath(directory, "mtidle", "locomotion/mtidle"), true);
        }
        if (_attackRange <= 0 || _naturalDamage < 0) throw new InvalidDataException("Actor attack range/damage is invalid.");
        var attackClip = Clip(_attackPath, _enemyWeapon?.Automatic == true);
        _attackHitCount = attackClip.Animation.TextKeys
            .SelectMany(key => key.Value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            .Count(text => text.Trim().Equals("Hit", StringComparison.OrdinalIgnoreCase));
        if (_attackHitCount == 0) throw new NotSupportedException("Actor attack requires a source Hit event.");
        _ = Clip(_movementPath, true);
    }

    private string SelectPath(string directory, params string[] groups)
    {
        foreach (var group in groups)
        {
            var path = directory + "/" + group + ".kf";
            if (_content.TryResolve(path, null, out _)) return path;
        }
        throw new FileNotFoundException("Source actor group is absent: " + string.Join(',', groups));
    }

    private NativeActorCombatAnimation Clip(string path, bool loop)
    {
        if (!_combatClips.TryGetValue(path, out var clip))
            _combatClips.Add(path, clip = new(path, _content, _skeleton, _enemyObject, loop));
        return clip;
    }

    private void AdvanceEngagement(RuntimeNativePlayer player, double delta)
    {
        var state = _state.Engagement!;
        if (_context!.Vitals().HitPoints == 0 && state.Action != "idle") state = state.Transition("idle");
        var offset = player.GlobalPosition - _actor.GlobalPosition;
        var distance = new Vector2(offset.X, offset.Z).Length();
        var reach = _attackRange + (Ranged ? 0 : _radius + player.CombatRadius);
        var visible = CanSee(player);
        if (state.Action == "pursue" && distance <= reach && visible)
        {
            if (_enemyWeapon?.IsMeleeWeapon == true && !_enemyWeaponHandling!.CanUse(_enemyWeapon))
                throw new NotSupportedException("Actor's broken melee weapon requires its unarmed attack owner.");
            var direction = offset; direction.Y = 0;
            if (direction.IsZeroApprox() || (-_actor.GlobalBasis.Z).Normalized().Dot(direction.Normalized()) > .95f)
                state = state.Transition("attack");
        }
        if (state.Action == "attack" && state.StartPending && Ranged && !_enemyWeaponHandling!.CanFire(_enemyWeapon!))
        {
            if (!_enemyWeaponHandling.CanReload(_enemyWeapon!)) throw new NotSupportedException("Actor exhausted usable ammunition and needs weapon reselection.");
            state = state.Transition("reload");
        }
        var path = state.Action switch
        {
            "pursue" => _movementPath,
            "attack" => _attackPath,
            "idle" => _combatIdle!.Path,
            "reload" => SelectPath(_skeletonPath[.._skeletonPath.LastIndexOf('/')], _enemyWeapon!.AnimationGroup + _enemyWeapon.ReloadGroup),
            _ => throw new InvalidDataException("Actor engagement action is invalid.")
        };
        var clip = Clip(path, state.Action is "pursue" or "idle");
        if (state.Animation is not null && (!state.Animation.Equals(path, StringComparison.OrdinalIgnoreCase) ||
            !state.AnimationHash!.Equals(clip.Hash, StringComparison.OrdinalIgnoreCase)))
            throw new NotSupportedException("Saved combat animation differs from the winning source.");
        state = state with { Animation = path, AnimationHash = clip.Hash };
        double factor = state.Action is "attack" or "reload" ? _enemyWeapon?.AnimationMultiplier ?? 1 : 1;
        if (state.Action == "attack" && _enemyWeapon?.Automatic == true)
        {
            var sequence = clip.Animation.Sequence;
            var duration = sequence.StopTime - sequence.StartTime;
            var rate = _enemyWeapon.AttackShotsPerSecond;
            if (!float.IsFinite(rate) || rate <= 0 || !float.IsFinite(sequence.Frequency) || sequence.Frequency <= 0 ||
                duration <= 0 || _attackHitCount <= 0)
                throw new InvalidDataException("Actor automatic attack cadence is invalid.");
            factor = duration * rate / (sequence.Frequency * _attackHitCount);
        }
        else if (state.Action == "attack") factor *= _enemyWeapon?.AttackMultiplier ?? 1;
        if (!double.IsFinite(factor) || factor <= 0) throw new InvalidDataException("Actor attack animation rate is invalid.");
        var next = state.Seconds + delta * factor;
        if (state.Action == "reload" || state.Action == "attack" && _enemyWeapon?.Automatic != true)
            next = Math.Min(next, clip.Duration);
        var moving = state.Action == "pursue" && distance > reach;
        TurnToward(moving ? PursuitTarget(player.GlobalPosition, delta) : player.GlobalPosition, delta);
        Activity.SetMovement(running: moving, sneaking: false);
        MoveActor(moving ? clip.RootDisplacement(state.Seconds, next) : Vector3.Zero, delta);
        foreach (var key in clip.Events.Crossed(state.Seconds, next, state.StartPending))
        {
            PublishCombatPose(clip, key.SourceSeconds, next);
            foreach (var text in key.Text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Select(value => value.Trim()))
            {
                if (state.Action == "attack" && text.Equals("Hit", StringComparison.OrdinalIgnoreCase)) AttackPlayer(player);
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
            if (!_enemyWeaponHandling.CanReload(_enemyWeapon))
                throw new NotSupportedException("Actor exhausted usable ammunition and cannot reload.");
            state = state.Transition("reload");
        }
        _state.Engagement = state;
    }

    private void PublishCombatPose(NativeActorCombatAnimation clip, float seconds, double elapsed)
    {
        _skeleton.Node.ResetBonePoses();
        _skeleton.Node.SetBonePose(_skeleton.BoneIndex(clip.Animation.Sequence.TargetName), Transform3D.Identity);
        var count = 0;
        _combatLayers[count++] = (_combatIdle!.Animation, _combatIdle.Time(elapsed));
        if (_combatAim is not null) _combatLayers[count++] = (_combatAim.Animation, _combatAim.Time(elapsed));
        if (_combatGrip is not null) _combatLayers[count++] = (_combatGrip.Animation, _combatGrip.Time(elapsed));
        _combatLayers[count++] = (clip.Animation, seconds);
        RuntimeNativeNifAnimation.ApplyLayers(_combatLayers.AsSpan(0, count));
    }

    private void AttackPlayer(RuntimeNativePlayer player)
    {
        ++_attacks;
        if (_context!.Vitals().HitPoints <= 0) return;
        if (Ranged) { ShootPlayer(player); return; }
        var distance = _actor.GlobalPosition.DistanceTo(player.GlobalPosition);
        var allowed = _attackRange + _radius + player.CombatRadius;
        var before = _context.Vitals().ExactHitPoints;
        var resolvedDamage = _enemyWeapon is null ? new FalloutWeaponDamage(_naturalDamage, 1, 0, 1) :
            _enemyDamage!.Resolve(_enemyWeapon.Form, _baseWeaponDamage);
        var strikeBone = _enemyWeapon is null
            ? _world.BodyParts(_state.Reference).Parts.Single(part => part.Type == 1).Node
            : "Bip01 R Hand";
        var bone = _skeleton.BoneIndex(strikeBone);
        var origin = bone >= 0
            ? (_skeleton.Node.GlobalTransform * _skeleton.Node.GetBoneGlobalPose(bone)).Origin
            : _actor.GlobalPosition + Vector3.Up * _radius;
        byte? part = null;
        if (distance <= allowed && CanSee(player) && player.CombatHitPartFrom(origin, _actor) is { } selectedPart)
        {
            part = selectedPart;
            _context.DamagePlayer(resolvedDamage, selectedPart);
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
            healthAfter = _context.Vitals().ExactHitPoints,
            damage = resolvedDamage.Amount,
            limbDamage = part is null ? (float?)null : resolvedDamage.Amount * resolvedDamage.LimbMultiplier
        };
        GD.Print($"OPENNV_ACTOR_ATTACK reference={_state.Reference} kind=melee part={part} health={before:R}->{_context.Vitals().ExactHitPoints:R}");
    }
}
