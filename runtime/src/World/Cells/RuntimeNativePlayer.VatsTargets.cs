using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.Presentation.Rendering;

namespace OpenNV.Runtime.World.Cells;

internal partial class RuntimeNativePlayer
{
    private sealed record VatsActor(Node3D Node, FalloutFormKey Reference,
        RuntimeNativeNifSkeleton Skeleton, RuntimeNativeActorCombat Combat);
    private readonly Dictionary<FalloutFormKey, VatsActor> _vatsActors = [];
    private FalloutFormKey? _vatsConditionTarget;

    private void DiscoverVatsActors()
    {
        _vatsActors.Clear();
        foreach (var node in GetTree().Root.FindChildren("*", "", true, false).OfType<Node3D>())
        {
            VatsActor? actor = node switch
            {
                RuntimeNativeNpc { Combat: { } combat, Appearance.Reference: { } reference } npc => new(npc, reference, npc.Skeleton, combat),
                RuntimeNativeCreature { Combat: { } combat, Appearance.Reference: { } reference } creature => new(creature, reference, creature.Skeleton, combat),
                _ => null
            };
            if (actor is not null && actor.Node.IsInsideTree() && !actor.Node.IsQueuedForDeletion() && actor.Combat.EffectiveEnabled && !actor.Combat.Dead)
                _vatsActors.Add(actor.Reference, actor);
        }
    }

    private IReadOnlyList<FalloutVatsSession.Target> VatsTargets() => _vatsTargets ?? [];

    private async Task PrepareVatsTargets(FalloutVatsSession session, long generation)
    {
        NativeVatsVisibilitySamples? samples = null;
        try
        {
            samples = new(this, _xr?.Camera ?? _camera); _vatsVisibility = samples;
            var targets = await MeasureVatsTargets(samples);
            if (generation != _vatsGeneration || !ReferenceEquals(session, _vats) || !VatsMenuActive) return;
            _vatsTargets = targets; _vatsPreparing = false; _vatsMenu!.Refresh();
        }
        catch (Exception error)
        {
            if (generation != _vatsGeneration || !ReferenceEquals(session, _vats) || !VatsMenuActive) return;
            _vatsError = error.Message; _vatsPreparing = false;
            RetireVats("visibility-failed"); GD.PushError("OPENNV_NATIVE_VATS_VISIBILITY_DIVERGENCE: " + error.Message);
        }
        finally { samples?.Dispose(); if (ReferenceEquals(_vatsVisibility, samples)) _vatsVisibility = null; }
    }

    private async Task<IReadOnlyList<FalloutVatsSession.Target>> MeasureVatsTargets(NativeVatsVisibilitySamples samples)
    {
        var weapon = RequireVatsWeapon();
        var records = _presentationRecords!;
        var world = _hitReferences!;
        var content = RuntimeLiveContentSource.Current ?? throw new InvalidOperationException("VATS owned content source is absent.");
        var ammunition = _weaponHandling!.Ammunition(weapon);
        if (weapon.HasAmmunitionSource && ammunition is null) throw new InvalidOperationException("The equipped VATS weapon has no available ammunition.");
        var shot = FalloutWeaponShot.Read(records, weapon.Form, ammunition, weapon.HasAmmunitionSource);
        shot = FalloutWeaponModifications.Apply(records, shot, _presentationInventory!.WeaponModMask(weapon.Form));
        shot.RequireRuntimeAttackOwner();
        if (shot.Projectiles != 1) throw new NotSupportedException("Multiple-projectile VATS requires its separate spread owner.");
        var cost = FalloutVatsWeaponCost.Read(records, weapon.Form).ApplyPerks(_combatPerks?.Invoke() ?? [], VatsPerkCondition);
        var actorValue = _combatActorValue ?? throw new InvalidOperationException("VATS actor values are absent.");
        var arms = CrippledArms(_vatsVitals!.State);
        var spread = (_weaponSpread ??= new(records)).PlayerVatsWobbleDegrees(shot, actorValue(shot.SkillActorValue), actorValue(5),
            arms.Left, arms.Right, _combatPerks?.Invoke() ?? [], _combatCondition!);
        var maximumDistance = FalloutGameSettingFloats.Read(records, "fVATSMaxEngageDistance");
        if (!float.IsFinite(maximumDistance) || maximumDistance < 0) throw new InvalidDataException("VATS maximum engage distance is invalid.");
        var chanceOwner = new FalloutVatsRangedChance(records);
        var targets = new List<FalloutVatsSession.Target>();
        foreach (var actor in _vatsActors.Values.OrderBy(actor => GlobalPosition.DistanceSquaredTo(actor.Node.GlobalPosition)))
        {
            if (!ActorAvailable(actor)) continue;
            var distance = GlobalPosition.DistanceTo(actor.Node.GlobalPosition) / UnitsToMeters;
            if (distance > maximumDistance) continue;
            var bound = actor.Node switch
            {
                RuntimeNativeNpc npc => npc.CurrentWorldBound(content),
                RuntimeNativeCreature creature => creature.CurrentWorldBound(content),
                _ => throw new InvalidOperationException("VATS source actor presentation was replaced.")
            };
            var diameter = bound.Radius * 2 / UnitsToMeters;
            var source = records.GetEffective(world.Get(actor.Reference).Base);
            var fields = source.ReadSubrecords().ToArray();
            var full = fields.SingleOrDefault(field => field.Signature == "FULL").Data;
            var name = full.IsEmpty ? FalloutDialogueTopic.Text(fields.Single(field => field.Signature == "EDID").Data.Span) : FalloutDialogueTopic.Text(full.Span);
            foreach (var part in world.BodyParts(actor.Reference).Parts)
            {
                if (!TryVatsPoint(actor.Reference, part.Type, out _, out var visible)) continue;
                var fraction = await samples.MeasureAsync(actor.Node, part.Type);
                var chance = chanceOwner.Calculate(shot.ResolvedMinimumSpread, spread + _playerProjectileLaunch!.AimChaseRadians,
                    distance, diameter, fraction, part.HitChance);
                _vatsConditionTarget = actor.Reference;
                try
                {
                    chance = Math.Max(chance, FalloutPlayerPerkNumericEffects.ApplyContext(8, chance / 100,
                        _combatPerks?.Invoke() ?? [], VatsPerkCondition) * 100);
                }
                finally { _vatsConditionTarget = null; }
                targets.Add(new(actor.Reference, part.Type, name, part.Name.Length == 0 ? "Part " + part.Type : part.Name,
                    Math.Clamp(chance, 0, 100), cost, fraction > 0));
            }
        }
        return targets;
    }

    private bool ActorAvailable(VatsActor actor) => IsInstanceValid(actor.Node) && actor.Node.IsInsideTree() &&
        !actor.Node.IsQueuedForDeletion() && actor.Combat.EffectiveEnabled && !actor.Combat.Dead &&
        _hitReferences!.IsResident(actor.Reference) && !_hitReferences.Get(actor.Reference).Deleted;

    private FalloutNifSphereBound? VatsMissBound(FalloutFormKey reference, byte partType)
    {
        if (!_vatsActors.TryGetValue(reference, out var actor) || !ActorAvailable(actor)) return null;
        var part = _hitReferences!.BodyParts(reference).Parts.Single(value => value.Type == partType);
        var bone = actor.Skeleton.BoneIndex(string.IsNullOrEmpty(part.Target) ? part.Node : part.Target);
        if (bone < 0) throw new NotSupportedException("VATS miss lost its selected source node.");
        var content = RuntimeLiveContentSource.Current!;
        return actor.Node switch
        {
            RuntimeNativeNpc npc => npc.CurrentBoneWorldBound(content, bone),
            RuntimeNativeCreature creature => creature.CurrentBoneWorldBound(content, bone),
            _ => throw new InvalidOperationException("VATS miss actor presentation changed.")
        };
    }

    private bool TryVatsPoint(FalloutFormKey reference, byte partType, out Vector3 point, out bool visible)
    {
        point = Vector3.Zero; visible = false;
        if (!_vatsActors.TryGetValue(reference, out var actor) || !ActorAvailable(actor)) return false;
        var part = _hitReferences!.BodyParts(reference).Parts.SingleOrDefault(part => part.Type == partType);
        if (part is null) return false;
        var bone = actor.Skeleton.BoneIndex(string.IsNullOrEmpty(part.Target) ? part.Node : part.Target);
        if (bone < 0) throw new NotSupportedException($"VATS part {reference}/{partType} lacks its authored target bone.");
        point = (actor.Skeleton.Node.GlobalTransform * actor.Skeleton.Node.GetBoneGlobalPose(bone)).Origin;
        var camera = _xr?.Camera ?? _camera;
        var screen = camera.UnprojectPosition(point);
        if (camera.IsPositionBehind(point) || !camera.GetViewport().GetVisibleRect().HasPoint(screen)) return true;
        var from = camera.GlobalPosition;
        using var hit = CastShotRay(from, point + (point - from).Normalized() * .01f);
        var collider = hit.TryGetValue("collider", out var value) ? value.AsGodotObject() as Node : null;
        visible = collider is null || collider == actor.Node || actor.Node.IsAncestorOf(collider);
        return true;
    }
}
