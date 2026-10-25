using System.Security.Cryptography;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Actors;

internal sealed partial class RuntimeNativeActorCombat
{
    private FalloutFurnitureIdleTree? _eatIdles;
    private NativeActorCombatAnimation? _eatAnimation;
    private FalloutFormKey? _eatIdle;
    internal int? ScriptEatProcedure => PackageOwnsPose && _state.PackageMotion?.Eat is { Consumed: true } ? 10 : null;

    private void AdvanceSandboxEat(FalloutPluginRecord package, FalloutSandboxPackage source,
        FalloutSandboxActivityProgress activity, double delta, NativeActorCombatContext context)
    {
        if (source.WeaponDrawn) throw new NotSupportedException("Eat requires its source holstered procedure.");
        var retained = _state.DeferredPackageContinuation?.RetainedAssignment is null &&
            _state.PackageMotion?.Package == package.FormKey ? _state.PackageMotion.Eat : null;
        var meal = retained ?? SelectMeal(activity, context);
        if (meal.WorldReference is { } food && !meal.Consumed && !_world.ActorMayEatWorldFood(_state.Reference, food))
            throw new InvalidOperationException("Selected food disappeared or changed ownership during approach.");

        if (meal.Idle is null)
        {
            if (meal.WorldReference is { } target)
            {
                var placement = _world.SpatialPlacement(target, null, _skeleton.UnitsToMetres);
                if (_world.TravelRegion(placement.Cell) != _world.TravelRegion(_world.Placement(_state.Reference).Cell))
                    throw new NotSupportedException("Eat target requires its other-region approach owner.");
                var position = placement.Position;
                var authored = _actor.GetParent<Node3D>().ToGlobal(new Vector3(position[0], position[2], -position[1]) * _skeleton.UnitsToMetres);
                var physicalFood = _world.Get(target);
                var bodies = physicalFood.CaptureObjectPhysics?.Invoke() ?? physicalFood.ObjectPhysics;
                if (bodies is { Count: > 0 })
                {
                    FalloutObjectPhysicsSnapshot.ValidateSet(bodies);
                    if (bodies.Count != 1) throw new NotSupportedException("Eat food has multiple physical body targets.");
                    // The physics owner publishes a global transform, including
                    // a dropped or displaced item. Never approach its old editor origin.
                    var pose = bodies[0].Transform;
                    authored = new(pose[9], pose[10], pose[11]);
                }
                var destination = ProjectPackageDestination(authored);
                var reach = PackageActivationRange;
                AdvancePackageMotion(package, destination, reach, source.Running, delta, requireArrivalHeight: true);
                _state.PackageMotion = _state.PackageMotion! with { Sandbox = activity, Wander = null, Eat = meal };
                if (_mover?.IsOnFloor() != true || _actor.GlobalPosition.DistanceTo(destination) > reach) return;
            }
            else
            {
                HoldSandboxActivity(package, source, activity, delta);
                _state.PackageMotion = _state.PackageMotion! with { Eat = meal };
            }

            var idle = (_eatIdles ??= new(_records, _skeletonPath)).SelectProcedure(10, condition =>
                condition.Function == 143 ? 10 : _actor switch
                {
                    RuntimeNativeNpc npc => npc.EvaluateAiCondition(condition),
                    RuntimeNativeCreature creature => creature.PackageCondition(condition),
                    _ => throw new NotSupportedException("Eat IDLE predicates have no actor owner.")
                });
            var idleHash = Convert.ToHexString(SHA256.HashData(idle.ReadData()));
            var clip = BindEatClip(idle.FormKey, idleHash);
            var use = _world.PrepareActorIngestible(_state.Reference, meal.Item, context.Level(), context.Globals,
                _world.PlayerHardcore, meal.WorldReference);
            if (use.Sound is { } sound) PackageSounds().PrepareSound(sound);

            var pending = meal with { Idle = idle.FormKey, IdleSha256 = idleHash };
            PublishEatMotion(package, activity, pending, clip, 0, true);
            _skeleton.Node.ResetBonePoses();
            var initialRoot = _skeleton.BoneIndex(clip.Animation.Sequence.TargetName);
            if (initialRoot >= 0) _skeleton.Node.SetBonePose(initialRoot, Transform3D.Identity);
            clip.Animation.ApplySourceTime(clip.Time(0));
            // The procedure's actual item-use transaction follows the admitted
            // arrival and published eating pose. Animation loops never consume
            // another item or reapply the effect transaction.
            use.Commit();
            meal = pending with { Consumed = true };
            PublishEatMotion(package, activity, meal, clip, 0, true);
            if (use.Sound is { } consumedSound) PackageSounds().DispatchSound(consumedSound);
            foreach (var key in clip.Events.Crossed(0, 0, true, true)) PackageSounds().Dispatch(key);
            _state.PackageMotion = _state.PackageMotion! with { StartPending = false };
            return;
        }

        var animation = BindEatClip(meal.Idle.Value, meal.IdleSha256!);
        if (!meal.Consumed) throw new NotSupportedException("Eat pose has no completed item-use receipt.");
        var motion = _state.PackageMotion!;
        if (motion.Animation != animation.Path || motion.AnimationSha256 != animation.Hash)
            throw new InvalidDataException("Retained Eat animation differs from its source.");
        var next = motion.Seconds + delta;
        if (!double.IsFinite(next)) throw new InvalidDataException("Eat animation clock overflowed.");
        PrepareMovement(); StopPackageMotion();
        Activity.SetMovement(false, false);
        MoveActor(animation.RootDisplacement(motion.Seconds, next), delta, null);
        foreach (var key in animation.Events.Crossed(motion.Seconds, next, motion.StartPending, true)) PackageSounds().Dispatch(key);
        _skeleton.Node.ResetBonePoses();
        var root = _skeleton.BoneIndex(animation.Animation.Sequence.TargetName);
        if (root >= 0) _skeleton.Node.SetBonePose(root, Transform3D.Identity);
        animation.Animation.ApplySourceTime(animation.Time(next));
        PublishEatMotion(package, activity, meal, animation, next, false);
    }

    private FalloutActorEatProgress SelectMeal(FalloutSandboxActivityProgress activity, NativeActorCombatContext context)
    {
        FalloutFormKey item;
        if (activity.Target is { } target) item = _world.Get(target).Base;
        else
        {
            var foods = _world.Inventory(_state.Reference, context.Level(), context.Globals).Contents.Items
                .Where(item => item.Count > 0 && FalloutActorFood.IsFood(_records, item.FormKey)).ToArray();
            if (foods.Length != 1) throw new NotSupportedException($"Eat inventory selection requires one qualified food kind; found {foods.Length}.");
            item = foods[0].FormKey;
        }
        var definition = FalloutIngestible.Read(_records, item);
        return new(item, definition.Hash, activity.Target);
    }

    private NativeActorCombatAnimation BindEatClip(FalloutFormKey idle, string hash)
    {
        var record = _records.GetEffective(idle);
        if (!Convert.ToHexString(SHA256.HashData(record.ReadData())).Equals(hash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Eat IDLE source changed.");
        if (_eatIdle == idle && _eatAnimation is not null) return _eatAnimation;
        var source = FalloutActorIdleSource.Resolve(_records, record);
        if (source.Objects.Count != 0) throw new NotSupportedException("Eat IDLE needs its source ANIO attachment owner.");
        var clip = new NativeActorCombatAnimation(source.AnimationPath, _content, _skeleton, null, true);
        _eatIdle = idle; return _eatAnimation = clip;
    }

    private void PublishEatMotion(FalloutPluginRecord package, FalloutSandboxActivityProgress activity,
        FalloutActorEatProgress meal, NativeActorCombatAnimation clip, double seconds, bool includeStart)
    {
        var position = _actor.GlobalPosition; var rotation = _actor.GlobalBasis.Orthonormalized().GetRotationQuaternion();
        var hash = Convert.ToHexString(SHA256.HashData(package.ReadData()));
        _state.PackageMotion = new(package.FormKey, hash, clip.Path, clip.Hash, seconds, includeStart,
            [position.X, position.Y, position.Z], [rotation.X, rotation.Y, rotation.Z, rotation.W], Sandbox: activity, Eat: meal);
        _packageDestinationActive = false; PackageMoving = false; PackageOwnsPose = true;
    }
}
