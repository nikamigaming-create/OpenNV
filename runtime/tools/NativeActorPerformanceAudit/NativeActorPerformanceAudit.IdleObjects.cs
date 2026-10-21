using System.Diagnostics;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

public partial class NativeActorPerformanceAudit
{
    private async Task OwnedIdleObjects(string dataRoot, string actorId, string idleId)
    {
        RuntimeLiveContentSource.Configure(dataRoot, RuntimeLiveContentSource.FalloutNewVegasGame);
        using var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        var caller = records.RuntimeFormKey(Convert.ToUInt32(actorId, 16));
        var cell = FalloutCellSceneReader.Read(records, FalloutCellSceneReader.ParentCell(records.GetEffective(caller))!.Value);
        var placed = cell.References.Single(value => value.FormKey == caller);
        var idle = FalloutActorIdleSource.Resolve(records, idleId);
        if (idle.Objects.Count == 0) throw new InvalidDataException("Selected source idle has no authored object.");
        foreach (var item in idle.Objects)
        {
            if (!content.TryRead(item.ModelPath, null, out var bytes, out _))
                throw new FileNotFoundException(item.ModelPath);
            var model = FalloutNifFile.Read(bytes);
            var binding = new FalloutActorIdleObjectBinding(item.Form,
                FalloutActorFurnitureContinuation.RecordHash(records.GetEffective(item.Form)), item.ModelPath, model.Sha256);
            binding.ValidateModel(model);
            Reject(() => (binding with { Sha256 = new string('0', 64) }).ValidateModel(model));
            if (!content.TryRead(idle.AnimationPath, null, out var animationBytes, out _))
                throw new FileNotFoundException(idle.AnimationPath);
            var animated = FalloutNifFile.Read(animationBytes);
            Reject(() => (binding with { Sha256 = animated.Sha256 }).ValidateModel(animated));
        }
        static void Reject(Action action)
        {
            try { action(); }
            catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
            throw new InvalidDataException("Changed or independently animated idle object was admitted.");
        }
        var globals = FalloutGlobalState.Read(records);
        var clock = new FalloutGameTime(globals, FalloutGameTimeBindings.Read(records),
            FalloutCalendar.Read(Path.Combine(Path.GetDirectoryName(dataRoot)!, "FalloutNV.exe")));
        var quests = new FalloutQuestState(records);
        var fixture = new Node3D(); AddChild(fixture);
        try
        {
            using var world = new FalloutReferenceWorld(records); world.LoadCell(cell);
            RuntimeNativeNpc Assemble(FalloutReferenceWorld owner)
            {
                var templates = owner.InitializeActorTemplates(caller, 1, globals);
                var actor = RuntimeNativeNpc.Create(records, content, placed, .0142875f,
                    (_, _, _, _) => new StandardMaterial3D(), owner.EquippedArmor(caller, 1, globals), templates,
                    owner.ActorAppearanceOverride(caller));
                try
                {
                    Transform3D Placement(FalloutPlacedReference reference) => new(
                        GamebryoCoordinate.ConvertReferenceEuler(new(reference.RotationRadians[0], reference.RotationRadians[1],
                            reference.RotationRadians[2]), reference.Scale),
                        GamebryoCoordinate.ConvertVector(new(reference.Position[0], reference.Position[1], reference.Position[2])) * .0142875f);
                    actor.Transform = Placement(placed);
                    actor.Combat = RuntimeNativeActorCombat.Attach(actor, actor.Skeleton, actor.Appearance.SkeletonPath,
                        owner, owner.Get(caller), records, content, 1, 1);
                    actor.ConfigureAi(records, quests, cell, Placement, clock: clock, globals: globals, world: owner);
                    fixture.AddChild(actor); actor.SetProcess(false); actor.SetPhysicsProcess(false);
                    actor.Combat.SetPhysicsProcess(false);
                    if (actor.AiError is null) throw new InvalidDataException("Selected fixture must retain its actual stopped source package.");
                    return actor;
                }
                catch { actor.Free(); throw; }
            }
            async Task Settle(FalloutReferenceInstance state)
            {
                var timer = Stopwatch.StartNew();
                while (!state.AnimationSoundEvents.CanCapture && timer.Elapsed.TotalSeconds < 12)
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                if (!state.AnimationSoundEvents.CanCapture) throw new InvalidDataException("Source idle voices did not finish naturally.");
            }
            var warm = Assemble(world);
            using var warmLifetime = new PackageFixtureLifetime(warm);
            warm.PlayIdle(records, idleId); warm._Process(14.21);
            await Settle(world.Get(caller));
            var saved = JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))!;
            var animation = saved.Single(value => value.Reference == caller).PackageBindingFailure?.IndependentIdle?.Animation ??
                throw new InvalidDataException("Source object idle has no retained independent continuation.");
            if (animation.Objects?.Count != idle.Objects.Count || animation.Clock.Complete)
                throw new InvalidDataException("Source idle lost its live objects or selected clock.");
            using var cold = new FalloutReferenceWorld(records); cold.Restore(saved); cold.LoadCell(cell);
            var resumed = Assemble(cold);
            using var resumedLifetime = new PackageFixtureLifetime(resumed);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            void SamePose()
            {
                if (warm.AnimationError is not null || resumed.AnimationError is not null)
                    throw new InvalidDataException(warm.AnimationError ?? resumed.AnimationError);
                var first = warm.FindChildren("*", "", true, false).OfType<Node3D>()
                    .Where(value => value.HasMeta("opennv_animation_object_form")).ToArray();
                var second = resumed.FindChildren("*", "", true, false).OfType<Node3D>()
                    .Where(value => value.HasMeta("opennv_animation_object_form")).ToArray();
                if (first.Length != idle.Objects.Count || second.Length != first.Length)
                    throw new InvalidDataException("Cold native actor did not rebuild its actual authored objects.");
                for (var index = 0; index < first.Length; ++index)
                    if (first[index].GetMeta("opennv_animation_object_form").AsString() != second[index].GetMeta("opennv_animation_object_form").AsString() ||
                        !first[index].GlobalTransform.IsEqualApprox(second[index].GlobalTransform) || first[index].Visible != second[index].Visible)
                        throw new InvalidDataException("Cold source object binding, pose or visibility differs.");
                for (var bone = 0; bone < warm.Skeleton.Node.GetBoneCount(); ++bone)
                    if (!warm.Skeleton.Node.GetBonePose(bone).IsEqualApprox(resumed.Skeleton.Node.GetBonePose(bone)))
                        throw new InvalidDataException("Cold source idle changed a native skeleton bone.");
            }
            SamePose();
            warm._Process(.39); resumed._Process(.39);
            await Settle(world.Get(caller)); await Settle(cold.Get(caller));
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            SamePose();
            var warmState = world.Get(caller).Capture(); var coldState = cold.Get(caller).Capture();
            if (JsonSerializer.Serialize(warmState.PackageBindingFailure) != JsonSerializer.Serialize(coldState.PackageBindingFailure) ||
                JsonSerializer.Serialize(warmState.AnimationSoundEvents) != JsonSerializer.Serialize(coldState.AnimationSoundEvents))
                throw new InvalidDataException("Cold source object idle changed its phase, RNG, fault or voice history.");
            Console.WriteLine("OPENNV_NATIVE_OWNED_IDLE_OBJECTS_PASS sourceObjects=true changedNifRefused=true independentClockRefused=true nativeAttachment=true coldBones=true coldObjects=true suffix=true sourceClock=true stoppedSourcePackageExpected=true recording=false ordinarySandbox=unimplemented parity=unverified");
        }
        finally { fixture.Free(); RuntimeLiveContentSource.Clear(); }
    }
}
