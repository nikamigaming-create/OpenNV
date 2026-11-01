using System.Security.Cryptography;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;
using Function = OpenNV.Runtime.Content.Definitions.FalloutConditionFunction;
using Procedure = OpenNV.Runtime.Content.Definitions.FalloutAiProcedure;

namespace OpenNV.Runtime.World.Actors;

internal sealed partial class RuntimeNativeActorCombat
{
    private FalloutFurnitureIdleTree? _eatIdles;
    private NativeActorCombatAnimation? _eatAnimation;
    private FalloutFormKey? _eatIdle;
    private NativeActorIdleObjects? _eatObjects;
    internal int? ScriptEatProcedure => PackageOwnsPose && (_state.PackageMotion?.Sandbox?.Activity == FalloutSandboxActivity.Eat ||
        _state.PackageMotion?.EatPackage is not null && _state.PackageMotion.Eat is not null) ? 10 : null;
    internal bool OwnsEatIdle(FalloutFormKey idle, OpenNV.Runtime.Formats.Gamebryo.FalloutNifControllerSequence? sequence) =>
        PackageOwnsPose && _state.PackageMotion?.Eat?.Idle == idle && ReferenceEquals(_eatAnimation?.Animation.Sequence, sequence);

    internal RuntimeNativeNifAnimation? RestoreEatIdleRequest(FalloutFormKey idle) =>
        _state.PackageMotion?.Eat is { } meal && meal.Idle == idle
            ? BindEatClip(idle, meal.IdleSha256!, meal.Objects, true).Animation : null;

    private void AdvanceSandboxEat(FalloutPluginRecord package, FalloutSandboxPackage source,
        FalloutSandboxActivityProgress activity, double delta, NativeActorCombatContext context)
        => AdvanceEating(package, source.Running, source.WeaponDrawn, activity, null, delta, context, null);

    internal bool HasInventoryFood(FalloutEatPackage source)
    {
        var context = _context ?? throw new NotSupportedException("Eat has no gameplay context.");
        return _world.Inventory(_state.Reference, context.Level(), context.Globals).Contents.Items
            .Any(item => item.Count > 0 && source.AcceptsFood(_records, _world, item.FormKey));
    }

    internal void AdvanceEatPackage(FalloutPluginRecord package, FalloutEatPackage source,
        FalloutActorEatPackageProgress progress, double delta)
    {
        if (OwnsPose || !PackageMovementReady) return;
        var context = _context ?? throw new NotSupportedException("Eat has no gameplay context.");
        if ((context.DialogueBusy ?? throw new NotSupportedException("Eat has no dialogue participant owner."))(_state.Reference)) return;
        var clock = progress.AdvanceIdle(delta);
        var retryIdle = clock.Retry;
        progress = clock.Progress;
        if (_actor is not RuntimeNativeNpc npc) throw new NotSupportedException("Standalone Eat has no NPC procedure owner.");
        npc.UpdateEatIntent(source, progress);
        AdvanceEating(package, source.Running, source.WeaponDrawn, null, progress, delta, context, source, retryIdle);
    }

    internal void HoldEatPackage(FalloutPluginRecord package, FalloutEatPackage source,
        FalloutActorEatPackageProgress progress, double delta) =>
        HoldEating(package, source.WeaponDrawn, null, progress, delta);

    private void AdvanceEating(FalloutPluginRecord package, bool running, bool weaponDrawn,
        FalloutSandboxActivityProgress? activity, FalloutActorEatPackageProgress? standalone,
        double delta, NativeActorCombatContext context, FalloutEatPackage? source, bool retryIdle = true)
    {
        if ((activity is null) == (standalone is null)) throw new InvalidOperationException("Eating needs exactly one parent procedure.");
        if (weaponDrawn) throw new NotSupportedException("Eat requires its source holstered procedure.");
        var retained = _state.DeferredPackageContinuation?.RetainedAssignment is null &&
            _state.PackageMotion?.Package == package.FormKey ? _state.PackageMotion.Eat : null;
        var meal = retained ?? SelectMeal(activity?.Target, context, source);
        if (source is not null && !source.AcceptsFood(_records, _world, meal.Item))
            throw new InvalidDataException("Retained meal differs from its authored Eat food restriction.");
        if (meal.WorldReference is { } food && !meal.Consumed && !_world.ActorMayEatWorldFood(_state.Reference, food))
            throw new InvalidOperationException("Selected food disappeared or changed ownership during approach.");

        if (meal.NoIdle)
        {
            activity = HoldEating(package, weaponDrawn, activity, standalone, delta);
            _state.PackageMotion = _state.PackageMotion! with { Eat = meal };
            if (source is null || !retryIdle) return;
        }

        if (meal.Idle is null && source is not null && !retryIdle)
        {
            activity = HoldEating(package, weaponDrawn, activity, standalone, delta);
            _state.PackageMotion = _state.PackageMotion! with { Eat = meal };
            return;
        }
        if (meal.Idle is null || source is not null && retryIdle)
        {
            var hadIdle = meal.Idle is not null;
            if (!meal.Consumed && meal.WorldReference is { } target)
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
                AdvancePackageMotion(package, destination, reach, running, delta, requireArrivalHeight: true);
                _state.PackageMotion = _state.PackageMotion! with { Sandbox = CurrentSandboxActivity ?? activity, EatPackage = standalone, Wander = null, Eat = meal };
                ObserveEatingMotion(package, activity);
                if (_mover?.IsOnFloor() != true || _actor.GlobalPosition.DistanceTo(destination) > reach) return;
            }
            else if (!hadIdle)
            {
                activity = HoldEating(package, weaponDrawn, activity, standalone, delta);
                _state.PackageMotion = _state.PackageMotion! with { Eat = meal };
            }

            FalloutPluginRecord? idle;
            using (_world.EnterUsedItem(meal.Item))
                idle = (_eatIdles ??= new(_records, _skeletonPath)).SelectProcedure((int)Procedure.Eat, condition =>
                    condition.Function switch
                    {
                        // The admitted Eat procedure has published and owns this
                        // actor's awake approach/idle pose. This does not provide
                        // a general answer for an unbound Sleep continuation.
                        (ushort)Function.GetSleeping when condition.RunOn == 0 && condition.Argument1 == 0 && condition.Argument2 == 0 &&
                            _state.PackageMotion?.Package == package.FormKey => 0,
                        (ushort)Function.GetCurrentAIProcedure when condition.RunOn == 0 && condition.Argument1 == 0 && condition.Argument2 == 0 => (int)Procedure.Eat,
                        _ => _actor switch
                        {
                            RuntimeNativeNpc npc => npc.EvaluateAiCondition(condition),
                            RuntimeNativeCreature creature => creature.PackageCondition(condition),
                            _ => throw new NotSupportedException("Eat IDLE predicates have no actor owner.")
                        }
                    });
            if (idle is null)
            {
                var use = meal.Consumed ? null : _world.PrepareActorIngestible(_state.Reference, meal.Item, context.Level(), context.Globals,
                    _world.PlayerHardcore, meal.WorldReference);
                if (use?.Sound is { } sound) PackageSounds().PrepareSound(sound);
                // No file is a valid IDLE-tree result. Keep the actually
                // published base pose; this is not a substitute eating clip.
                use?.Commit();
                if (!hadIdle)
                {
                    meal = meal with { Consumed = true, NoIdle = true };
                    _state.PackageMotion = _state.PackageMotion! with { Eat = meal, Sandbox = CurrentSandboxActivity ?? activity, EatPackage = standalone, Wander = null };
                }
                if (use?.Sound is { } noIdleSound) PackageSounds().DispatchSound(noIdleSound);
                ObserveEatingMotion(package, activity);
                if (source is not null && _actor is RuntimeNativeNpc noIdleOwner) noIdleOwner.EatIdleAttempted(source);
                if (!hadIdle) return;
            }
            else
            {
                var idleHash = Convert.ToHexString(SHA256.HashData(idle.ReadData()));
                bool OwnsSelection() => _state.PackageMotion?.Package == package.FormKey &&
                    (activity is not null ? ReferenceEquals(_state.PackageMotion.Sandbox, activity) :
                        _actor is RuntimeNativeNpc owner && source is not null && owner.OwnsEatIntent(source, standalone!)) &&
                    ReferenceEquals(_state.PackageMotion.Eat, meal);
                var published = false;
                void Activate(Action<RuntimeNativeNifAnimation, string>? bind = null)
                {
                    if (!OwnsSelection())
                        throw new InvalidOperationException("Queued Eat idle lost its selected meal procedure.");
                    // Preparation captures current transaction inputs. Do this at
                    // promotion, never before an arbitrary queued animation delay.
                    var use = meal.Consumed ? null : _world.PrepareActorIngestible(_state.Reference, meal.Item, context.Level(), context.Globals,
                        _world.PlayerHardcore, meal.WorldReference);
                    if (use?.Sound is { } sound) PackageSounds().PrepareSound(sound);
                    if (source is not null && _actor is RuntimeNativeNpc currentOwner) standalone = currentOwner.CurrentEatIntent(source);
                    var clip = BindEatClip(idle.FormKey, idleHash);
                    var pending = meal with { Idle = idle.FormKey, IdleSha256 = idleHash, Objects = _eatObjects?.Bindings, NoIdle = false };
                    PublishEatMotion(package, activity, standalone, pending, clip, 0, true);
                    _skeleton.ResetSourceBonePoses();
                    var initialRoot = _skeleton.BoneIndex(clip.Animation.Sequence.TargetName);
                    if (initialRoot >= 0) _skeleton.Node.SetBonePose(initialRoot, Transform3D.Identity);
                    _eatObjects?.SetVisible(true);
                    clip.Animation.ApplySourceTime(clip.Time(0));
                    bind?.Invoke(clip.Animation, clip.Path);
                    // Only actual publication admits ingestion. A refused producer
                    // or a queued request does not consume another item.
                    use?.Commit();
                    meal = pending with { Consumed = true };
                    PublishEatMotion(package, activity, standalone, meal, clip, 0, true);
                    if (use?.Sound is { } consumedSound) PackageSounds().DispatchSound(consumedSound);
                    foreach (var key in clip.Events.Crossed(0, 0, true, true)) PackageSounds().Dispatch(key);
                    _state.PackageMotion = _state.PackageMotion! with { StartPending = false };
                    ObserveEatingMotion(package, activity);
                    published = true;
                }
                if (_actor is RuntimeNativeNpc npc) npc.QueueProcedureIdle(_records, idle.FormKey, "eat", OwnsSelection, bind => Activate(bind));
                else
                {
                    _state.RecordIdleRequest(_records, idle.FormKey);
                    Activate();
                }
                if (source is not null && _actor is RuntimeNativeNpc retryOwner) retryOwner.EatIdleAttempted(source);
                if (published || !hadIdle) return;
            }
        }

        if (meal.Idle is not { } activeIdle) throw new InvalidDataException("Eat continuation has no selected source clip.");
        var animation = BindEatClip(activeIdle, meal.IdleSha256!, meal.Objects, true);
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
        _skeleton.ResetSourceBonePoses();
        var root = _skeleton.BoneIndex(animation.Animation.Sequence.TargetName);
        if (root >= 0) _skeleton.Node.SetBonePose(root, Transform3D.Identity);
        _eatObjects?.SetVisible(true);
        animation.Animation.ApplySourceTime(animation.Time(next));
        if (source is not null && _actor is RuntimeNativeNpc currentOwner) standalone = currentOwner.CurrentEatIntent(source);
        PublishEatMotion(package, activity, standalone, meal, animation, next, false);
        ObserveEatingMotion(package, activity);
    }

    private void ObserveEatingMotion(FalloutPluginRecord package, FalloutSandboxActivityProgress? activity)
    {
        if (activity is not null) _state.ObserveInitialSandboxMotion(package.FormKey);
    }

    private FalloutSandboxActivityProgress? HoldEating(FalloutPluginRecord package, bool weaponDrawn, FalloutSandboxActivityProgress? activity,
        FalloutActorEatPackageProgress? standalone, double delta)
    {
        if (activity is not null) activity = ObserveSandboxPhase(3);
        if (standalone is { Furniture: true } && _actor is RuntimeNativeNpc npc)
        {
            _state.PackageMotion = npc.CaptureEatFurnitureMotion(package, standalone);
            _packageDestinationActive = false; PackageMoving = false; PackageOwnsPose = false;
        }
        else
        {
            AdvancePackageMotion(package, _actor.GlobalPosition, 0, false, delta, weaponDrawn);
            _state.PackageMotion = _state.PackageMotion! with { Sandbox = activity, EatPackage = standalone, Wander = null };
        }
        if (activity is not null && _state.ProcedureCaptureBlocker?.StartsWith(FalloutSandboxPackage.CaptureBlockerPrefix, StringComparison.Ordinal) == true)
            _state.ProcedureCaptureBlocker = null;
        // The phase publication can create a new activity record. The queued
        // physical request must retain this actual owner, not its old phase.
        return activity;
    }

    private FalloutActorEatProgress SelectMeal(FalloutFormKey? worldTarget, NativeActorCombatContext context, FalloutEatPackage? source)
    {
        FalloutFormKey item;
        if (worldTarget is { } target) item = _world.Get(target).Base;
        else
        {
            var inventory = _world.Inventory(_state.Reference, context.Level(), context.Globals).Contents;
            bool Allowed(FalloutFormKey form) => source is null ? _world.IsActorFood(form) : source.AcceptsFood(_records, _world, form);
            var foods = inventory.Items.Where(item => item.Count > 0 && Allowed(item.FormKey)).ToArray();
            var orderedFoods = inventory.TraversalItems?.Where(food => food.Count > 0 && Allowed(food.FormKey)).ToArray();
            if (foods.Length > 1 && orderedFoods is null) throw new FalloutSandboxMealSelectionException(foods.Select(food =>
                new FalloutSandboxFoodInput(food.FormKey,
                    Convert.ToHexString(SHA256.HashData(_records.GetEffective(food.FormKey).ReadData())), food.Count)).ToArray());
            if (foods.Length == 0) throw new NotSupportedException(
                $"Eat inventory selection requires one qualified food kind; found {foods.Length}: " +
                string.Join(", ", foods.Select(food => $"{food.FormKey} x{food.Count}")) + ".");
            item = (orderedFoods ?? foods)[0].FormKey;
        }
        var definition = FalloutIngestible.Read(_records, item);
        return new(item, definition.Hash, worldTarget);
    }

    private NativeActorCombatAnimation BindEatClip(FalloutFormKey idle, string hash,
        IReadOnlyList<FalloutActorIdleObjectBinding>? saved = null, bool restoring = false)
    {
        var record = _records.GetEffective(idle);
        if (!Convert.ToHexString(SHA256.HashData(record.ReadData())).Equals(hash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Eat IDLE source changed.");
        var source = FalloutActorIdleSource.Resolve(_records, record);
        if (restoring) FalloutActorIdleObjectBinding.ValidateSources(_records, source, saved);
        if (_eatIdle == idle && _eatAnimation is not null) return _eatAnimation;
        var objects = source.Objects.Count == 0 ? null : new NativeActorIdleObjects(_records, _content, _skeleton, source, saved);
        try
        {
            var clip = new NativeActorCombatAnimation(source.AnimationPath, _content, _skeleton, null, true, objects: objects);
            _eatObjects?.Dispose(); _eatObjects = objects;
            _eatIdle = idle; return _eatAnimation = clip;
        }
        catch { objects?.Dispose(); throw; }
    }

    private void PublishEatMotion(FalloutPluginRecord package, FalloutSandboxActivityProgress? activity,
        FalloutActorEatPackageProgress? standalone, FalloutActorEatProgress meal, NativeActorCombatAnimation clip, double seconds, bool includeStart)
    {
        var position = _actor.GlobalPosition; var rotation = _actor.GlobalBasis.Orthonormalized().GetRotationQuaternion();
        var hash = Convert.ToHexString(SHA256.HashData(package.ReadData()));
        _state.PackageMotion = new(package.FormKey, hash, clip.Path, clip.Hash, seconds, includeStart,
            [position.X, position.Y, position.Z], [rotation.X, rotation.Y, rotation.Z, rotation.W], Sandbox: activity, Eat: meal, EatPackage: standalone);
        _packageDestinationActive = false; PackageMoving = false; PackageOwnsPose = true;
    }
}
