using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Actors;

internal sealed partial class RuntimeNativeActorCombat : Node
{
    private Node3D _actor = null!;
    private RuntimeNativeNifSkeleton _skeleton = null!;
    private FalloutReferenceWorld _world = null!;
    private FalloutReferenceInstance _state = null!;
    private FalloutPluginStack _records = null!;
    private RuntimeLiveContentSource _content = null!;
    private string _skeletonPath = "";
    private uint _layer, _mask;
    private RuntimeNativeActorRagdoll? _ragdoll;
    private readonly Dictionary<string, byte> _parts = new(StringComparer.Ordinal);
    private object? _lastLimbEffect;
    internal bool Dead => _state.Injury?.Dead == true;
    internal bool EngagedWith(FalloutFormKey target) => !Dead && _state.Enabled &&
        _state.Engagement is { Action: not "idle" } engagement && engagement.Target == target;
    internal bool CanReceiveExplosionDamage => _state.Enabled && !Dead;
    internal IEnumerable<Vector3> CorpseAimPoints => _ragdoll?.Active == true ? _ragdoll.AimPoints : [];
    internal IEnumerable<Rid> CollisionRids => _actor.FindChildren("*", "", true, false)
        .OfType<CollisionObject3D>().Prepend(_actor as CollisionObject3D).Where(value => value is not null)
        .Select(value => value!.GetRid());
    internal string? Error { get; private set; }
    internal object Observation => new
    {
        dead = Dead,
        health = _state.ActorValues.GetValueOrDefault("health")?.Current,
        injury = _state.Injury,
        ragdoll = _ragdoll?.Observation,
        gore = _goreEffects?.State,
        lastSeverEffect = _lastSeverEffect,
        goreError = _goreError,
        lastLimbEffect = _lastLimbEffect,
        error = Error,
        muzzlePresentationError = _muzzlePresentationError,
        casingPresentationError = _casingPresentationError,
        engagement = EngagementObservation,
        unbound = "critical,sneak,conditional-resistance-modifiers,armor-wear,damage-reactions,combat-AI-tactics,confidence-threat-ratios,hit-and-death-script-events,death-XP,exploded-limbs"
    };

    internal static RuntimeNativeActorCombat Attach(Node3D actor, RuntimeNativeNifSkeleton skeleton, string skeletonPath,
        FalloutReferenceWorld world, FalloutReferenceInstance state, FalloutPluginStack records, RuntimeLiveContentSource content,
        uint layer, uint mask, NativeActorCombatContext? context = null)
    {
        world.InitializeSourceCorpse(state.Reference);
        var owner = new RuntimeNativeActorCombat
        {
            Name = "ActorCombat",
            _actor = actor,
            _skeleton = skeleton,
            _skeletonPath = skeletonPath,
            _world = world,
            _state = state,
            _records = records,
            _content = content,
            _layer = layer,
            _mask = mask,
            _context = context
        };
        actor.AddChild(owner);
        return owner;
    }

    public override void _Ready()
    {
        AddToGroup(CombatActorsGroup);
        RestorePackageMotion();
        RestoreEngagementPose();
        // A cold cell enters the tree with this owner already attached. Its
        // parent is still visiting children during Ready, so adding the death
        // rig there would be rejected by Godot.
        if (Dead) Callable.From(RestoreDeath).CallDeferred();
    }

    private void RestoreDeath()
    {
        if (!IsInsideTree() || IsQueuedForDeletion()) return;
        try { PrepareDeath(); BeginDeath(); }
        catch (Exception error) { Error = error.Message; GD.PushError($"OPENNV_ACTOR_DEATH_UNBOUND reference={_state.Reference} {Error}"); }
    }

    internal byte HitPart(Node collider)
    {
        if (!collider.HasMeta("opennv_nif_collision_bone")) throw new NotSupportedException("Actor hit lacks a source collision bone.");
        var name = collider.GetMeta("opennv_nif_collision_bone").AsString();
        if (_parts.TryGetValue(name, out var cached)) return cached;
        var parts = _world.BodyParts(_state.Reference).Parts;
        for (var bone = _skeleton.BoneIndex(name); bone >= 0; bone = _skeleton.Node.GetBoneParent(bone))
        {
            var current = _skeleton.Node.GetBoneName(bone).ToString();
            var part = parts.SingleOrDefault(part => part.Node == current);
            if (part is not null) { _parts.Add(name, part.Type); return part.Type; }
        }
        // The source torso is the residual body region; named limb roots carve
        // their descendants out of it. No synthetic capsule selects anatomy.
        var torso = parts.Single(part => part.Type == 0);
        _parts.Add(name, torso.Type); return torso.Type;
    }

    internal FalloutActorHit Hit(Node collider, FalloutWeaponDamage damage, FalloutFormKey attacker,
        int level, FalloutGlobalState globals, uint? weaponOnHitBehavior = null,
        Func<float>? nextWeaponRandomUnit = null)
    {
        try
        {
            var part = HitPart(collider);
            var defense = _world.Defense(_state.Reference, level, globals);
            var amount = defense.Absorb(damage.Amount, FalloutGameSettingFloats.Read(_records, "fMinDamMultiplier"), damage.AmmoEffects);
            var health = _world.Health(_state.Reference);
            var bodyPart = _world.BodyParts(_state.Reference).Parts.Single(value => value.Type == part);
            if (!Dead && amount * bodyPart.DamageMultiplier >= health.Current) PrepareDeath();
            var hit = _world.DamageActor(_state.Reference, attacker, part, amount, damage.LimbMultiplier, level, globals);
            if (hit.Died)
            {
                BeginDeath();
                ApplyLethalWeaponLimbEffect(bodyPart, weaponOnHitBehavior, nextWeaponRandomUnit);
            }
            else if (hit.HealthDamage > 0) Provoke(attacker);
            Error = null;
            GD.Print($"OPENNV_ACTOR_HIT reference={hit.Reference} part={hit.Part} health={hit.HealthBefore:R}->{hit.HealthAfter:R} died={hit.Died}");
            return hit;
        }
        catch (Exception error) { Error = error.Message; throw; }
    }

    private void PrepareDeath()
    {
        if (_ragdoll is not null) return;
        var authored = Dead && _state.Ragdoll is null && FalloutActorHealthSource.StartsDead(_records, _state.Base, _state.Templates)
            ? FalloutAuthoredRagdoll.Read(_records.GetEffective(_state.Reference)) : null;
        _ragdoll = RuntimeNativeActorRagdoll.Prepare(_actor, _skeleton, _state, _content, _skeletonPath, _layer, _mask,
            _world.BodyParts(_state.Reference).Parts, authored);
    }

    private void ApplyLethalWeaponLimbEffect(FalloutBodyPart part, uint? behavior, Func<float>? nextRandomUnit)
    {
        if (behavior is not { } onHit) return;
        if (onHit is < FalloutWeaponOnHitRules.Normal or > FalloutWeaponOnHitRules.NoDismemberOrExplode)
            throw new NotSupportedException($"Weapon On Hit behavior {onHit} is unbound.");
        if (onHit == FalloutWeaponOnHitRules.ExplodeOnly)
        {
            _lastLimbEffect = new { part = part.Type, behavior = onHit, state = (part.Flags & 0x08) != 0 ? "explode-unbound" : "not-explodable" };
            if ((part.Flags & 0x08) != 0)
                GD.PushError($"OPENNV_ACTOR_LIMB_EXPLOSION_UNBOUND reference={_state.Reference} part={part.Type} weaponOnHit={onHit}");
            return;
        }
        if (!FalloutWeaponOnHitRules.AllowsDismember(onHit, part))
        {
            _lastLimbEffect = new { part = part.Type, behavior = onHit, state = "not-severable" };
            return;
        }

        var chance = checked((int)FalloutGameSettingIntegers.Read(_records, "iCombatDismemberPartChance"));
        var roll = (nextRandomUnit ?? throw new NotSupportedException("Lethal weapon dismemberment has no persistent random owner."))();
        if (!FalloutWeaponOnHitRules.ShouldDismember(onHit, part, chance, roll))
        {
            var explosionUnbound = onHit == FalloutWeaponOnHitRules.Normal && (part.Flags & 0x08) != 0 && part.ExplosionChance > 0;
            _lastLimbEffect = new { part = part.Type, behavior = onHit, chance, roll, state = explosionUnbound ? "explode-unbound" : "chance-failed" };
            if (explosionUnbound)
                GD.PushError($"OPENNV_ACTOR_LIMB_EXPLOSION_UNBOUND reference={_state.Reference} part={part.Type} weaponOnHit={onHit}");
            return;
        }

        try
        {
            SeverLimb(part.Type);
            _lastLimbEffect = new { part = part.Type, behavior = onHit, chance, roll, state = "severed" };
        }
        catch (Exception error)
        {
            _lastLimbEffect = new { part = part.Type, behavior = onHit, chance, roll, state = "sever-unbound", error = error.Message };
            GD.PushError($"OPENNV_ACTOR_LIMB_SEVER_UNBOUND reference={_state.Reference} part={part.Type} {error.Message}");
        }
    }

    internal void SeverLimb(byte type)
    {
        if (!Dead) throw new InvalidOperationException("Source limb separation requires a dead actor.");
        if (_state.Injury!.SeveredParts?.Contains(type) == true) return;
        PrepareDeath();
        _ragdoll!.RequireSeverable(type);
        _ragdoll.Sever(type);
        _world.SeverLimb(_state.Reference, type);
        _ragdoll.AddSeparationVelocity(type, FalloutGameSettingFloats.Read(_records, "fCombatDismemberedLimbVelocity"));
        EmitSeverEffect(_world.BodyParts(_state.Reference).Parts.Single(part => part.Type == type));
    }

    private void BeginDeath()
    {
        foreach (var area in _actor.FindChildren("*", "Area3D", true, false).OfType<Area3D>())
            if (area.HasMeta("opennv_nif_collision_bone")) GamebryoReferenceEnableRuntime.SetCollisionFilter(area, 0, 0);
        _ragdoll!.Activate();
    }

    internal static RuntimeNativeActorCombat? Find(Node? collider)
    {
        for (var node = collider; node is not null; node = node.GetParent())
        {
            if (node is RuntimeNativeNpc npc) return npc.Combat;
            if (node is RuntimeNativeCreature creature) return creature.Combat;
        }
        return null;
    }
}
