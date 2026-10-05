using System.Buffers.Binary;
using System.Text;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Actors;

public partial class NativeAuthoredRagdollAudit
{
    private void ExerciseSynthetic()
    {
        var bytes = SyntheticSkeleton();
        var source = FalloutNifFile.Read(bytes);
        Require(FalloutNifAuthoredRagdoll.BindAccumulationRoot(source).Block.Index == 1,
            "Accumulation root used a bone name instead of source child order.");
        var skeleton = new RuntimeNativeNifSkeleton(source, .01f);
        var actor = new Node3D { Transform = new(new Basis(Vector3.Up, .6f).Scaled(Vector3.One * 1.7f), new(3, 4, 5)) };
        actor.AddChild(skeleton.Node); AddChild(actor);
        try
        {
            var root = skeleton.BoneIndex("ArbitraryAccum");
            var bone = skeleton.BoneIndex("AuthoredBody");
            var misleading = skeleton.BoneIndex("Bip01");
            skeleton.Node.SetBonePoseScale(bone, Vector3.One * 2.3f);
            var placed = actor.Transform;
            var scene = skeleton.Node.GetBonePose(0);
            var rootPosition = skeleton.Node.GetBonePosePosition(root);
            var rootScale = skeleton.Node.GetBonePoseScale(root);
            var unrelated = skeleton.Node.GetBonePose(misleading);
            var authored = new FalloutAuthoredRagdoll([new(6, [12, 23, 34], [.2f, -.1f, .4f])], [.3f, -.4f, .7f]);
            RuntimeNativeActorRagdoll.ApplyAuthoredPose(skeleton, authored);
            // Evaluate the clockwise XYZ matrix independently in source axes,
            // then apply the source-to-native axis permutation to each column.
            var expected = ExpectedRotation(authored.BipedRotation!);
            var actual = new Basis(skeleton.Node.GetBonePoseRotation(root));
            Require(BasisNear(actual, expected) && skeleton.Node.GetBonePosePosition(root) == rootPosition &&
                skeleton.Node.GetBonePoseScale(root) == rootScale && skeleton.Node.GetBonePoseScale(bone) == Vector3.One * 2.3f &&
                skeleton.Node.GetBonePosePosition(bone).DistanceTo(new(.12f, .34f, -.23f)) < .000001f &&
                actor.Transform == placed && skeleton.Node.GetBonePose(0) == scene && skeleton.Node.GetBonePose(misleading) == unrelated,
                "Authored pose lost root/body components, source XYZ signs/order or reference placement.");
            var retainedRotation = skeleton.Node.GetBonePoseRotation(root);
            foreach (var angles in new float[]?[] { null, [0, -0.0f, 0] })
            {
                RuntimeNativeActorRagdoll.ApplyAuthoredPose(skeleton, authored with { BipedRotation = angles });
                Require(skeleton.Node.GetBonePoseRotation(root) == retainedRotation, "Absent/exact-zero XRGB reset the existing accumulation rotation.");
            }
            var physicalPose = skeleton.Node.GetBonePose(bone);
            skeleton.Node.SetBonePoseRotation(root, Quaternion.Identity);
            RuntimeNativeActorRagdoll.ApplyAuthoredPose(skeleton, authored with { Bones = [new(6, [100, 200, 300], [0, 0, 0])] }, false);
            Require(BasisNear(new(skeleton.Node.GetBonePoseRotation(root)), expected) && skeleton.Node.GetBonePose(bone) == physicalPose,
                "Cold accumulation restoration reapplied initial XRGD over retained physical body pose.");
            var retained = Enumerable.Range(0, skeleton.Node.GetBoneCount()).Select(skeleton.Node.GetBonePose).ToArray();
            foreach (var rejected in new[]
            {
                authored with { BipedRotation = [float.NaN, 0, 0] },
                authored with { BipedRotation = [1, 2] },
                authored with { Bones = [new(7, [12, 23, 34], [0, 0, 0])] },
                authored with { Bones = [new(6, [float.PositiveInfinity, 23, 34], [0, 0, 0])] }
            })
            {
                Reject(() => RuntimeNativeActorRagdoll.ApplyAuthoredPose(skeleton, rejected));
                Require(retained.SequenceEqual(Enumerable.Range(0, skeleton.Node.GetBoneCount()).Select(skeleton.Node.GetBonePose)),
                    "Rejected source pose partially changed the live skeleton.");
            }
            Require(bytes.SequenceEqual(SyntheticSkeleton()), "Source synthetic NIF bytes changed.");
        }
        finally { actor.Free(); }
        foreach (var malformed in new[] { SyntheticSkeleton(firstChild: -1), SyntheticSkeleton(emptyRoot: true),
            SyntheticSkeleton(firstChild: 6), SyntheticSkeleton(multipleRoots: true) })
            Reject(() => FalloutNifAuthoredRagdoll.BindAccumulationRoot(FalloutNifFile.Read(malformed)));
        GD.Print("OPENNV_AUTHORED_RAGDOLL_SYNTHETIC_PASS orderedChild=true mixedXYZ=true exactZero=true components=true atomicRefusal=true malformedRoot=true");
    }

    private static Basis ExpectedRotation(float[] angles)
    {
        var sx = MathF.Sin(-angles[0]); var cx = MathF.Cos(angles[0]);
        var sy = MathF.Sin(-angles[1]); var cy = MathF.Cos(angles[1]);
        var sz = MathF.Sin(-angles[2]); var cz = MathF.Cos(angles[2]);
        float[,] m = { { cy * cz, -cy * sz, sy }, { cx * sz + sx * sy * cz, cx * cz - sx * sy * sz, -sx * cy },
            { sx * sz - cx * sy * cz, sx * cz + cx * sy * sz, cx * cy } };
        static Vector3 Convert(float x, float y, float z) => new(x, z, -y);
        return new(Convert(m[0, 0], m[1, 0], m[2, 0]), Convert(m[0, 2], m[1, 2], m[2, 2]),
            -Convert(m[0, 1], m[1, 1], m[2, 1]));
    }

    private static bool BasisNear(Basis first, Basis second) => first.X.DistanceTo(second.X) < .00001f &&
        first.Y.DistanceTo(second.Y) < .00001f && first.Z.DistanceTo(second.Z) < .00001f;

    private static byte[] SyntheticSkeleton(int firstChild = 1, bool emptyRoot = false, bool multipleRoots = false)
    {
        static byte[] Bytes(Action<BinaryWriter> write)
        {
            using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
            write(writer); return stream.ToArray();
        }
        static void Object(BinaryWriter writer, int name, int collision, float scale, Vector3 position)
        {
            writer.Write(name); writer.Write(0U); writer.Write(-1); writer.Write(14U);
            writer.Write(position.X); writer.Write(position.Y); writer.Write(position.Z);
            for (var i = 0; i < 9; i++) writer.Write(i % 4 == 0 ? 1f : 0f);
            writer.Write(scale); writer.Write(0U); writer.Write(collision);
        }
        byte[] Node(int name, int collision, float scale, Vector3 position, int[] children) => Bytes(writer =>
        {
            Object(writer, name, collision, scale, position);
            writer.Write((uint)children.Length); foreach (var child in children) writer.Write(child); writer.Write(0U);
        });
        var body = new byte[236]; BinaryPrimitives.WriteInt32LittleEndian(body, -1); body[5] = 6;
        var blocks = new[]
        {
            Node(0, -1, 1, Vector3.Zero, emptyRoot ? [] : firstChild == 1 ? [1, 3] : [firstChild, 2, 3]),
            Node(1, -1, 1.25f, new(10, 20, 30), [2]),
            Node(2, 4, 2, new(1, 2, 3), []), Node(3, -1, 1, Vector3.Zero, []),
            Bytes(writer => { writer.Write(2); writer.Write((ushort)1); writer.Write(5); }), body,
            Bytes(writer => { Object(writer, 4, -1, 1, Vector3.Zero); writer.Write(-1); writer.Write(-1);
                writer.Write(0U); writer.Write(0); writer.Write(false); })
        };
        return Bytes(writer =>
        {
            writer.Write(Encoding.ASCII.GetBytes("Gamebryo File Format, Version 20.2.0.7\n"));
            writer.Write(FalloutNifFile.Version); writer.Write((byte)1); writer.Write(FalloutNifFile.UserVersion);
            writer.Write((uint)blocks.Length); writer.Write(34U); writer.Write(new byte[] { 1, 0, 1, 0, 1, 0 });
            string[] types = ["NiNode", "bhkCollisionObject", "bhkRigidBody", "NiTriShape"];
            writer.Write((ushort)types.Length);
            foreach (var type in types) { writer.Write(type.Length); writer.Write(Encoding.ASCII.GetBytes(type)); }
            foreach (var type in new ushort[] { 0, 0, 0, 0, 1, 2, 3 }) writer.Write(type);
            foreach (var block in blocks) writer.Write(block.Length);
            string[] names = ["SceneRoot", "ArbitraryAccum", "AuthoredBody", "Bip01", "NotABone"];
            writer.Write((uint)names.Length); writer.Write((uint)names.Max(value => value.Length));
            foreach (var name in names) { writer.Write(name.Length); writer.Write(Encoding.ASCII.GetBytes(name)); }
            writer.Write(0U); foreach (var block in blocks) writer.Write(block);
            writer.Write(multipleRoots ? 2U : 1U); writer.Write(0); if (multipleRoots) writer.Write(3);
        });
    }
}
