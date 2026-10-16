using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

public partial class NativeNifInstanceAudit
{
    private void ExerciseLogicalPoseProjection()
    {
        ExerciseAllocationFreeBasisConversion();
        ExerciseLogicalVisualFields();
        var source = FalloutNifFile.Read(ActorSkinFixture("PoseRoot", false));
        var key = new FalloutFormKey("PoseProjectionFixture.esm", 7);
        var face = new FalloutNpcFaceGen(key, [], [], []);
        var appearance = new FalloutNpcAppearance(key, null, key, key, key, false,
            new("PoseProjectionFixture.esm", 8), "meshes/authored/pose-skeleton.nif",
            1, 1, [], [], [], [], face, face, [], [], [], [], [], [], null, null);
        var model = new FalloutActorSourceModel(appearance, FalloutNifPhysicalModel.Read(source), []);
        var scene = model.ConstructLogicalScene();
        var joint = scene.Objects.Single(node => node.Name == "Joint");
        var pose = FalloutNifSceneMath.Identity with
        {
            Translation = new(3, 4, 5),
            RotationRowMajor = [0, -1, 0, 1, 0, 0, 0, 0, 1],
            Scale = 1.5f
        };
        scene.PublishLocalPose(joint.Key, pose);
        scene.UpdateWorldAndBounds();
        var before = JsonSerializer.Serialize(scene.Capture());
        const float UnitsToMetres = .25f;
        var view = new RuntimeNativeNifSkeleton(model.Skeleton, UnitsToMetres);
        try
        {
            AddChild(view.Node);
            view.BindLogicalScene(scene);
            var bone = view.BoneIndex("Joint");
            for (var tick = 0; tick < 200; ++tick)
            {
                view.Node.SetBonePosePosition(bone, new(99, 98, 97));
                view.BorrowCurrentSourcePoses();
                if (view.Node.GetBonePosePosition(bone) != new Vector3(.75f, 1.25f, -1) ||
                    view.Node.GetBonePose(bone).Basis.X.DistanceTo(Vector3.Forward * pose.Scale) > .00001f)
                    throw new InvalidDataException("Native pose projection did not borrow the current C# transform.");
            }
            if (JsonSerializer.Serialize(scene.Capture()) != before)
                throw new InvalidDataException("Rendering advanced or rewrote the authoritative logical pose.");
            pose = pose with { Translation = new(7, 8, 9), Scale = 2 };
            scene.PublishLocalPose(joint.Key, pose);
            scene.UpdateWorldAndBounds();
            view.BorrowCurrentSourcePoses();
            if (view.Node.GetBonePosePosition(bone) != new Vector3(1.75f, 2.25f, -2) ||
                view.Node.GetBonePose(bone).Basis.X.DistanceTo(Vector3.Forward * pose.Scale) > .00001f)
                throw new InvalidDataException("Bound native skeleton borrowed stale copies of mutable source nodes.");
        }
        finally { view.Node.Free(); }

        var saved = JsonSerializer.Deserialize<FalloutActorLogicalSceneSnapshot>(JsonSerializer.Serialize(scene.Capture()))!;
        for (var cycle = 0; cycle < 3; ++cycle)
        {
            var cold = model.Instantiate().ConstructLogicalScene(saved);
            var freshView = new RuntimeNativeNifSkeleton(model.Skeleton, UnitsToMetres);
            try
            {
                AddChild(freshView.Node);
                freshView.BindLogicalScene(cold);
                freshView.BorrowRestoredSourcePoses();
                if (freshView.Node.GetBonePosePosition(freshView.BoneIndex("Joint")) != new Vector3(1.75f, 2.25f, -2))
                    throw new InvalidDataException("Fresh native pose binding lost restored C# state.");
                saved = JsonSerializer.Deserialize<FalloutActorLogicalSceneSnapshot>(JsonSerializer.Serialize(cold.Capture()))!;
                cold.Retire();
                try
                {
                    freshView.BorrowCurrentSourcePoses();
                    throw new InvalidDataException("Native pose projection accepted a retired source graph.");
                }
                catch (ObjectDisposedException) { }
            }
            finally { freshView.Node.Free(); }
        }
        using var material = new StandardMaterial3D();
        var borrower = new RuntimeNativeNifLogicalSceneMaterials(scene,
            [new(0, new(scene.SkeletonSha256, int.MaxValue, material))], () => true);
        try
        {
            AddChild(borrower);
            if (borrower.Failure is null)
                throw new InvalidDataException("Authored missing material did not retain its rendering failure.");
            borrower.RequireBinding(scene, this);
            RemoveChild(borrower);
            AddChild(borrower);
            try
            {
                borrower.RequireBinding(scene, this);
                throw new InvalidDataException("Reparenting revived a retired material borrower.");
            }
            catch (ObjectDisposedException) { }
        }
        finally { borrower.Free(); }
        scene.Retire();
        GD.Print("OPENNV_LOGICAL_PLAYER_POSE_PROJECTION_PASS actualSkeleton=true sourceUnchanged=true nativeOverwriteDiscarded=true liveMutation=true rotationScaleUnits=true cold3=true retiredRefused=true gameplay=UNEXECUTED");
    }

    private static void ExerciseAllocationFreeBasisConversion()
    {
        float[][] fixtures =
        [
            [1, 0, 0, 0, 1, 0, 0, 0, 1],
            [0, -1, 0, 1, 0, 0, 0, 0, 1],
            [.3f, -.7f, .9f, -.8f, .25f, -.5f, .6f, -.2f, .4f],
            [-0.0f, 0.0f, -1, 0, 1, -0.0f, 1, 0, -0.0f]
        ];
        foreach (var matrix in fixtures)
        {
            var actual = GamebryoCoordinate.ConvertBasis(matrix, 1.5f, "authored projection");
            var expected = Legacy(matrix, 1.5f);
            if (!Same(actual.X, expected.X) || !Same(actual.Y, expected.Y) || !Same(actual.Z, expected.Z))
                throw new InvalidDataException("Allocation-free projection changed prior Float32 matrix arithmetic.");
        }
        for (var warm = 0; warm < 64; ++warm)
            _ = GamebryoCoordinate.ConvertBasis(fixtures[2], 1.5f, "authored projection");
        var last = Basis.Identity;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var sample = 0; sample < 5000; ++sample)
            last = GamebryoCoordinate.ConvertBasis(fixtures[2], 1.5f, "authored projection");
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        if (allocated != 0 || !last.X.IsFinite())
            throw new InvalidDataException($"Basis projection allocated {allocated} managed bytes across repeated samples.");
        GD.Print("OPENNV_BASIS_PROJECTION_ALLOCATION_PASS samples=5000 managedBytes=0 priorFloat32Bits=true");

        static bool Same(Vector3 left, Vector3 right) =>
            BitConverter.SingleToInt32Bits(left.X) == BitConverter.SingleToInt32Bits(right.X) &&
            BitConverter.SingleToInt32Bits(left.Y) == BitConverter.SingleToInt32Bits(right.Y) &&
            BitConverter.SingleToInt32Bits(left.Z) == BitConverter.SingleToInt32Bits(right.Z);
        static Basis Legacy(IReadOnlyList<float> source, float scale)
        {
            var axes = new float[,] { { 1, 0, 0 }, { 0, 0, 1 }, { 0, -1, 0 } };
            var matrix = new float[3, 3]; var transpose = new float[3, 3];
            for (var row = 0; row < 3; ++row)
                for (var column = 0; column < 3; ++column)
                { matrix[row, column] = source[row * 3 + column]; transpose[row, column] = axes[column, row]; }
            var converted = Multiply(axes, Multiply(matrix, transpose));
            return new Basis(new(converted[0, 0], converted[1, 0], converted[2, 0]),
                new(converted[0, 1], converted[1, 1], converted[2, 1]),
                new(converted[0, 2], converted[1, 2], converted[2, 2])).Scaled(Vector3.One * scale);
        }
        static float[,] Multiply(float[,] left, float[,] right)
        {
            var result = new float[3, 3];
            for (var row = 0; row < 3; ++row)
                for (var column = 0; column < 3; ++column)
                    for (var axis = 0; axis < 3; ++axis)
                        result[row, column] += left[row, axis] * right[axis, column];
            return result;
        }
    }
}
