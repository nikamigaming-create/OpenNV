using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

public partial class NativeActorPerformanceAudit
{
    private static void FurnitureResidualBindingRefusals(FalloutPluginStack records,
        IReadOnlyList<FalloutReferenceSnapshot> saved, FalloutFormKey actor,
        Func<FalloutReferenceWorld, RuntimeNativeNpc> assemble)
    {
        var occupied = saved.Single(value => value.Reference == actor).FurnitureContinuation!;
        var animation = occupied.IdleState!.ActiveAnimation!;
        var residual = animation.ResidualPose!;
        var partial = residual.Bones.FirstOrDefault(bone =>
            (bone.Position is null ? 0 : 1) + (bone.Rotation is null ? 0 : 1) + (bone.Scale is null ? 0 : 1) > 1) ??
            throw new InvalidDataException("Selected native residual fixture has no multiple uncovered components.");
        var incomplete = partial.Scale is not null ? partial with { Scale = null } : partial with { Position = null };
        foreach (var (invalid, reason) in new[]
        {
            (residual with { SkeletonSha256 = new string('0', 64) }, "Saved residual pose differs from its owned skeleton."),
            (residual with { Bones = residual.Bones.Select(bone => bone.Index == partial.Index ? incomplete : bone).ToArray() },
                "Saved residual pose differs from its source layer coverage."),
        })
        {
            using var rejected = new FalloutReferenceWorld(records);
            rejected.Restore(saved.Select(value => value.Reference == actor ? value with
            {
                FurnitureContinuation = occupied with
                { IdleState = occupied.IdleState with { ActiveAnimation = animation with { ResidualPose = invalid } } }
            } : value).ToArray());
            rejected.LoadCell(FalloutCellSceneReader.Read(records, rejected.Placement(actor).Cell));
            RuntimeNativeNpc? admitted = null;
            var refused = false;
            try { admitted = assemble(rejected); }
            catch (InvalidDataException error) when (error.Message == reason) { refused = true; }
            finally { if (admitted is not null) admitted.Free(); }
            if (!refused) throw new InvalidDataException("Native residual source drift reached bone publication.");
        }
    }

    private static void FurnitureIdleSuffix(RuntimeNativeNpc warm, RuntimeNativeNpc resumed,
        FalloutReferenceWorld world, FalloutReferenceWorld cold, FalloutFormKey actor, FalloutReferenceSnapshot saved)
    {
        var idle = saved.FurnitureContinuation?.IdleState?.ActiveAnimation ??
            throw new InvalidDataException("Native occupied fixture has no selected collection clock.");
        if (idle.Clock.CompletedRepeats < 2 || warm.ActiveIdle != idle.Idle || resumed.ActiveIdle != idle.Idle ||
            warm.ActiveIdleOwner != "package-idle" || resumed.ActiveIdleOwner != "package-idle")
            throw new InvalidDataException("Native fixture did not capture the selected source IDLE mid-repeat.");
        var crossedRepeat = false;
        var crossedKeys = 0;
        for (var frame = 0; frame < 1200; ++frame)
        {
            warm._Process(.125); resumed._Process(.125);
            if (warm.ActiveIdle != idle.Idle || resumed.ActiveIdle != idle.Idle ||
                JsonSerializer.Serialize(world.Get(actor).Capture()) != JsonSerializer.Serialize(cold.Get(actor).Capture()) ||
                warm.Skeleton.Node.GetBoneCount() != resumed.Skeleton.Node.GetBoneCount())
                throw new InvalidDataException("Native restored collection pose changed its selected loops, base clock, replay delay or seat.");
            for (var bone = 0; bone < warm.Skeleton.Node.GetBoneCount(); ++bone)
                if (warm.Skeleton.Node.GetBonePose(bone) != resumed.Skeleton.Node.GetBonePose(bone))
                {
                    GD.Print("OPENNV_NATIVE_OCCUPIED_IDLE_BONE_DIFF " + JsonSerializer.Serialize(new
                    {
                        bone,
                        name = warm.Skeleton.Node.GetBoneName(bone).ToString(),
                        warm = Matrix(warm.Skeleton.Node.GetBonePose(bone)),
                        cold = Matrix(resumed.Skeleton.Node.GetBonePose(bone)),
                        warmBaseClock = world.Get(actor).Animation.Capture(),
                        coldBaseClock = cold.Get(actor).Animation.Capture(),
                        warmOverlay = world.Get(actor).Capture().FurnitureContinuation!.IdleState!.ActiveAnimation!.Clock,
                        coldOverlay = cold.Get(actor).Capture().FurnitureContinuation!.IdleState!.ActiveAnimation!.Clock,
                    }));
                    throw new InvalidDataException("Native occupied cold animation changed its actual source bone pose.");
                }
            var warmKeys = Crossings(warm).EnumerateArray().Select(key => key.GetRawText()).ToArray();
            var coldKeys = Crossings(resumed).EnumerateArray().Select(key => key.GetRawText()).ToArray();
            if (!warmKeys.SequenceEqual(coldKeys))
                throw new InvalidDataException("Native collection clock lost or repeated a remaining authored text key.");
            crossedKeys += warmKeys.Length;
            if (world.Get(actor).Capture().FurnitureContinuation!.IdleState!.ActiveAnimation!.Clock.CompletedRepeats > idle.Clock.CompletedRepeats)
            {
                crossedRepeat = true; break;
            }
        }
        if (!crossedRepeat || crossedKeys == 0)
            throw new InvalidDataException("Native collection clock lost or repeated its remaining authored text-key suffix.");
        static JsonElement Crossings(RuntimeNativeNpc actor) => JsonSerializer.SerializeToElement(actor.AnimationState)
            .GetProperty("textKeyCrossings");
        static float[] Matrix(Transform3D pose) => [pose.Basis.X.X, pose.Basis.X.Y, pose.Basis.X.Z,
            pose.Basis.Y.X, pose.Basis.Y.Y, pose.Basis.Y.Z, pose.Basis.Z.X, pose.Basis.Z.Y, pose.Basis.Z.Z,
            pose.Origin.X, pose.Origin.Y, pose.Origin.Z];
    }
}
