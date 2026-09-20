using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

public partial class NativeActorCombatAudit
{
    private async Task ExerciseAuthoredCorpse(FalloutPluginStack records, RuntimeLiveContentSource content,
        FalloutReferenceWorld world, FalloutCellScene cell, FalloutPlacedReference placed)
    {
        var authored = FalloutAuthoredRagdoll.Read(records.GetEffective(placed.FormKey)) ??
            throw new InvalidDataException("Selected corpse has no authored pose.");
        var actor = RuntimeNativeCreature.Create(records, content, placed, world.Get(placed.FormKey), .0142875f);
        actor.Transform = new(GamebryoCoordinate.ConvertReferenceEuler(new(placed.RotationRadians[0], placed.RotationRadians[1], placed.RotationRadians[2]), placed.Scale),
            GamebryoCoordinate.ConvertVector(new(placed.Position[0], placed.Position[1], placed.Position[2])) * .0142875f);
        AddChild(actor);
        // The authored corpse can extend below its reference origin. Keep this
        // synthetic drop-test floor clear of the complete initial pose.
        var floor = new StaticBody3D { Position = actor.GlobalPosition - Vector3.Up };
        floor.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(20, .1f, 20) } }); AddChild(floor);
        try
        {
            var bound = FalloutNifAuthoredRagdoll.Bind(actor.Skeleton.Source, authored);
            var firstBone = actor.Skeleton.BoneIndex(bound[0].Bone);
            var flyingLocal = actor.Skeleton.Node.GetBonePosePosition(firstBone);
            RuntimeNativeActorContacts.Configure(actor, actor.Skeleton, 2);
            actor.Combat = RuntimeNativeActorCombat.Attach(actor, actor.Skeleton, actor.Appearance.SkeletonPath,
                world, world.Get(placed.FormKey), records, content, 2, 3);
            var initialized = new TaskCompletionSource();
            Callable.From(() => initialized.SetResult()).CallDeferred(); await initialized.Task;
            if (actor.Combat.Error is not null) throw new InvalidDataException(actor.Combat.Error);
            var initial = world.Get(placed.FormKey).Capture().Ragdoll ?? throw new InvalidDataException("Authored corpse has no bodies.");
            var local = actor.Skeleton.Node.GetBonePosePosition(firstBone);
            var expected = GamebryoCoordinate.ConvertVector(new(authored.Bones[0].Position[0], authored.Bones[0].Position[1], authored.Bones[0].Position[2])) * .0142875f;
            if (local.DistanceTo(expected) > .0002f || initial.Bodies.Count != authored.Bones.Count)
                throw new InvalidDataException("Initial corpse retained its living animation instead of XRGD.");
            var deliberatelyWrong = authored with { Bones = authored.Bones.Skip(1).ToArray() };
            try { _ = FalloutNifAuthoredRagdoll.Bind(actor.Skeleton.Source, deliberatelyWrong); throw new Exception("Mismatched pose was accepted."); }
            catch (InvalidDataException) { }
            await Frames(180);
            var saved = JsonSerializer.Deserialize<FalloutReferenceSnapshot>(JsonSerializer.Serialize(world.Get(placed.FormKey).Capture()))!;
            if (saved.Ragdoll!.Bodies.Any(body => body.Transform.Any(value => !float.IsFinite(value)) ||
                body.Transform[10] < floor.GlobalPosition.Y - .3f || body.Transform[10] > floor.GlobalPosition.Y + 1.5f))
                throw new InvalidDataException("Authored corpse escaped its floor or constraints: " +
                    JsonSerializer.Serialize(new
                    {
                        floor = floor.GlobalPosition.Y,
                        initial = initial.Bodies.Select(b => new { b.SourceBody, y = b.Transform[10] }),
                        final = saved.Ragdoll.Bodies.Select(b => new { b.SourceBody, y = b.Transform[10], b.LinearVelocity })
                    }));
            var transform = actor.Transform; actor.Free();
            using var restored = new FalloutReferenceWorld(records); restored.Restore([saved]); restored.LoadCell(cell);
            var cold = RuntimeNativeCreature.Create(records, content, placed, restored.Get(placed.FormKey), .0142875f);
            cold.Transform = transform; AddChild(cold);
            try
            {
                cold.Combat = RuntimeNativeActorCombat.Attach(cold, cold.Skeleton, cold.Appearance.SkeletonPath,
                    restored, restored.Get(placed.FormKey), records, content, 2, 3);
                var ready = new TaskCompletionSource(); Callable.From(() => ready.SetResult()).CallDeferred(); await ready.Task;
                if (cold.Combat.Error is not null) throw new InvalidDataException(cold.Combat.Error);
                var after = restored.Get(placed.FormKey).Capture().Ragdoll!;
                if (!after.Bodies.Zip(saved.Ragdoll.Bodies).All(pair => pair.First.Transform.Zip(pair.Second.Transform).All(v => MathF.Abs(v.First - v.Second) < .0002f)))
                    throw new InvalidDataException("Cold restoration reapplied the original pose over persistent physics.");
                GD.Print($"OPENNV_AUTHORED_CORPSE_PASS reference={placed.FormKey} bodies={initial.Bodies.Count} initialOffset={local} livingOffset={flyingLocal} cold=preserved");
            }
            finally { cold.Free(); }
        }
        finally { if (IsInstanceValid(actor)) actor.Free(); floor.Free(); }
    }
}
