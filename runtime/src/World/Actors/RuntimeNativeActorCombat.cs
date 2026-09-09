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
    internal bool Dead => _state.Injury?.Dead == true;
    internal string? Error { get; private set; }
    internal object Observation => new
    {
        dead = Dead,
        health = _state.ActorValues.GetValueOrDefault("health")?.Current,
        injury = _state.Injury,
        ragdoll = _ragdoll?.Observation,
        error = Error,
        unbound = "critical,sneak,armor-and-resistance-modifiers,damage-reactions,combat-AI,hit-and-death-script-events,death-XP,limb-destruction"
    };

    internal static RuntimeNativeActorCombat Attach(Node3D actor, RuntimeNativeNifSkeleton skeleton, string skeletonPath,
        FalloutReferenceWorld world, FalloutReferenceInstance state, FalloutPluginStack records, RuntimeLiveContentSource content,
        uint layer, uint mask)
    {
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
            _mask = mask
        };
        actor.AddChild(owner);
        return owner;
    }

    public override void _Ready()
    {
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
        int level, FalloutGlobalState globals)
    {
        try
        {
            var part = HitPart(collider);
            var defense = _world.Defense(_state.Reference, level, globals);
            var amount = defense.Absorb(damage.Amount, FalloutGameSettingFloats.Read(_records, "fMinDamMultiplier"));
            var health = _world.Health(_state.Reference);
            var bodyPart = _world.BodyParts(_state.Reference).Parts.Single(value => value.Type == part);
            if (!Dead && amount * bodyPart.DamageMultiplier >= health.Current) PrepareDeath();
            var hit = _world.DamageActor(_state.Reference, attacker, part, amount, damage.LimbMultiplier, level, globals);
            if (hit.Died) BeginDeath();
            Error = null;
            GD.Print($"OPENNV_ACTOR_HIT reference={hit.Reference} part={hit.Part} health={hit.HealthBefore:R}->{hit.HealthAfter:R} died={hit.Died}");
            return hit;
        }
        catch (Exception error) { Error = error.Message; throw; }
    }

    private void PrepareDeath() => _ragdoll ??= RuntimeNativeActorRagdoll.Prepare(_actor, _skeleton, _state, _content, _skeletonPath, _layer, _mask);

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
