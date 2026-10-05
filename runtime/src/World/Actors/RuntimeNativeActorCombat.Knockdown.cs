using System.Security.Cryptography;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Actors;

internal sealed partial class RuntimeNativeActorCombat
{
    private FalloutHitReactionTree? _recoveryTree;
    private readonly Dictionary<CollisionObject3D, (uint Layer, uint Mask)> _knockdownFilters = [];
    internal bool KnockedDown => _state.KnockedDown;

    internal void ApplyExplosionPhysics(FalloutExplosion explosion, FalloutActorHit hit, Vector3 center, bool push)
    {
        var source = FalloutActorTemplateOwner.Resolve(_records, _records.GetEffective(_state.Base), 2, _state.Templates);
        var stats = source.ReadSubrecords().Single(field => field.Signature == "DATA").Data.Span;
        var agility = source.Signature == "NPC_" && stats.Length == 11 ? stats[9] :
            source.Signature == "CREA" && stats.Length == 17 ? stats[15] :
            throw new NotSupportedException("Actor agility has no source stat layout.");
        if (!Dead && !_state.Unconscious && !_state.KnockedDown &&
            FalloutExplosionKnockdown.ShouldFall(_records, explosion, hit.HealthDamage, hit.HealthBefore,
                _world.Health(_state.Reference).Base, agility, _state.HitReactionRandom.NextUnitFloat))
        {
            // Bind both source recovery orientations before interrupting the actor.
            _ = RecoveryIdle(true); _ = RecoveryIdle(false);
            PrepareDeath(); _state.KnockedDown = true; BeginKnockdown();
            GD.Print($"OPENNV_ACTOR_KNOCKDOWN reference={_state.Reference} explosion={explosion.Form}");
        }
        if (push && _ragdoll is { Active: true })
            _ragdoll.ApplyBlast(center, explosion.Force * _skeleton.UnitsToMetres);
    }

    private FalloutPluginRecord RecoveryIdle(bool faceUp)
    {
        _recoveryTree ??= new(_records, _skeletonPath, 106);
        return _recoveryTree.Select(condition => condition.Function switch
        {
            106 => faceUp ? 1 : 0,
            107 => 2,
            _ => HitReactionCondition(condition, -1)
        }) ?? throw new NotSupportedException("Knocked-down actor has no source recovery IDLE.");
    }

    private void BeginKnockdown()
    {
        _state.HitReaction = null; _hitReactionClip = null;
        if (_state.Engagement is { } engagement) _state.Engagement = engagement.Transition("pursue");
        StopPackageMotion(); Activity.SetMovement(false, false);
        foreach (var collider in _actor.FindChildren("*", "CollisionObject3D", true, false).OfType<CollisionObject3D>()
            .Prepend((CollisionObject3D)_actor))
        {
            if (_ragdoll!.IsAncestorOf(collider)) continue;
            _knockdownFilters.TryAdd(collider, (collider.CollisionLayer, collider.CollisionMask));
            GamebryoReferenceEnableRuntime.SetCollisionFilter(collider, 0, 0);
        }
        _ragdoll!.Activate();
    }

    private bool AdvanceKnockdown()
    {
        if (!_state.KnockedDown || _state.HitReaction is not null) return false;
        if (_ragdoll is not { Settled: true }) return true;
        try
        {
            var torso = _ragdoll.TorsoTransform;
            var selected = RecoveryIdle((-torso.Basis.Z).Dot(Vector3.Up) > 0);
            var source = FalloutActorIdleSource.Resolve(_records, selected);
            var clip = new NativeActorCombatAnimation(source.AnimationPath, _content, _skeleton, _enemyObject, false);
            if (clip.Animation.Sequence.CycleType != 2) throw new NotSupportedException("Knockdown recovery must have a finite source KF.");
            var excluded = new Godot.Collections.Array<Rid>();
            foreach (var rid in CollisionRids) excluded.Add(rid);
            using var ray = PhysicsRayQueryParameters3D.Create(torso.Origin + Vector3.Up * .05f,
                torso.Origin + Vector3.Down * (256 * _skeleton.UnitsToMetres), _mask, excluded);
            using var floor = _actor.GetWorld3D().DirectSpaceState.IntersectRay(ray);
            if (!floor.TryGetValue("position", out var position)) return true;
            _ragdoll.EndLivingSimulation(); _ragdoll.Free(); _ragdoll = null;
            _actor.GlobalPosition = position.AsVector3();
            foreach (var (collider, filter) in _knockdownFilters)
                if (IsInstanceValid(collider)) GamebryoReferenceEnableRuntime.SetCollisionFilter(collider, filter.Layer, filter.Mask);
            _knockdownFilters.Clear();
            _hitReactionClip = clip;
            var p = _actor.GlobalPosition; var q = _actor.GlobalBasis.Orthonormalized().GetRotationQuaternion();
            _state.HitReaction = new(selected.FormKey, Convert.ToHexString(SHA256.HashData(selected.ReadData())), 0,
                new(clip.Path, clip.Hash, 0, true), [p.X, p.Y, p.Z], [q.X, q.Y, q.Z, q.W]);
            _hitReactionError = null;
            _state.HitReactionFaults.ClearCurrentError();
            GD.Print($"OPENNV_ACTOR_KNOCKDOWN_RECOVER reference={_state.Reference} idle={selected.FormKey}");
        }
        catch (Exception error) { ReportHitReactionError(error); }
        return true;
    }
}
