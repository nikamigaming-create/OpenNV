using System.Text;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

public partial class NativeNifInstanceAudit
{
    private void ExerciseActorSkinRoot()
    {
        var skeleton = NativeNifMeshBuilder.BuildActorSkeleton(ActorSkinFixture("ActorRoot", false), 1);
        using var material = new StandardMaterial3D();
        try
        {
            AddChild(skeleton.Node);
            var source = FalloutNifFile.Read(ActorSkinFixture("EquipmentRoot", true));
            var scene = NativeNifMeshBuilder.AddActorPart(source, skeleton, materialOverride: (_, _) => material);
            var mesh = scene.Root.FindChildren("*", "MeshInstance3D", true, false).Cast<MeshInstance3D>().Single();
            var vertices = mesh.Mesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            if (scene.Vertices != 3 || scene.Triangles != 1 || mesh.Skin.GetBindCount() != 1 ||
                mesh.Skin.GetBindBone(0) != skeleton.BoneIndex("Joint"))
                throw new InvalidDataException("Model-local skin root prevented the actual influence from binding.");
            skeleton.Node.SetBonePoseRotation(skeleton.BoneIndex("Joint"), new Quaternion(Vector3.Up, Mathf.Pi / 2));
            var transformed = skeleton.Node.GetBoneGlobalPose(mesh.Skin.GetBindBone(0)) * mesh.Skin.GetBindPose(0) * vertices[1];
            if (transformed.DistanceTo(new Vector3(0, 0, -1)) > .0001f)
                throw new InvalidDataException("Equipment root naming changed animated skin placement.");
            var nestedSource = FalloutNifFile.Read(ActorSkinFixture("ActorRoot", true, nestedRoot: true));
            var nestedScene = NativeNifMeshBuilder.AddActorPart(nestedSource, skeleton, materialOverride: (_, _) => material);
            var nestedMesh = nestedScene.Root.FindChildren("*", "MeshInstance3D", true, false).Cast<MeshInstance3D>().Single();
            var nestedVertices = nestedMesh.Mesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var nestedTransformed = skeleton.Node.GetBoneGlobalPose(nestedMesh.Skin.GetBindBone(0)) *
                nestedMesh.Skin.GetBindPose(0) * nestedVertices[1];
            if (nestedScene.Vertices != 3 || nestedScene.Triangles != 1 ||
                nestedMesh.Skin.GetBindBone(0) != skeleton.BoneIndex("Joint") ||
                nestedTransformed.DistanceTo(transformed) > .0001f)
                throw new InvalidDataException("Nested source skin root lost its external actor-root binding.");
            try
            {
                NativeNifMeshBuilder.AddActorPart(FalloutNifFile.Read(ActorSkinFixture("EquipmentRoot", true, "MissingJoint")),
                    skeleton, materialOverride: (_, _) => material);
                throw new InvalidOperationException("Missing actual influence bone was silently accepted.");
            }
            catch (InvalidDataException) { }
            GD.Print("OPENNV_ACTOR_SKIN_ROOT_PASS model-root-independent=true nested-actor-root=true palette-bound=true animated-placement=true missing-influence-rejected=true");
        }
        finally { skeleton.Node.Free(); }
    }

    private void ExerciseOwnedActorSkins(string game, string mod, string root, string skeletonPath, string[] arguments)
    {
        var split = Array.IndexOf(arguments, "--dependencies");
        if (split < 1) throw new ArgumentException("Owned skin audit needs model paths then --dependencies.");
        var setup = new FalloutModStackSelection([new(mod, root, arguments[(split + 1)..])]).Resolve(game);
        using var content = setup.OpenSource();
        if (!content.TryRead(skeletonPath, null, out var skeletonBytes, out _)) throw new FileNotFoundException(skeletonPath);
        foreach (var model in arguments[..split])
        {
            var skeleton = NativeNifMeshBuilder.BuildActorSkeleton(skeletonBytes, .0142875f);
            try
            {
                AddChild(skeleton.Node);
                if (!content.TryRead(model, null, out var bytes, out _)) throw new FileNotFoundException(model);
                var source = FalloutNifFile.Read(bytes);
                var result = NativeNifMeshBuilder.AddActorPart(source, skeleton, contentSource: content);
                if (result.Vertices == 0 || result.Surfaces == 0) throw new InvalidDataException("Owned skin omitted its mesh.");
                foreach (var mesh in result.Root.FindChildren("*", "MeshInstance3D", true, false).Cast<MeshInstance3D>())
                    if (mesh.Skin is null || Enumerable.Range(0, mesh.Skin.GetBindCount()).Any(index => mesh.Skin.GetBindBone(index) < 0))
                        throw new InvalidDataException("Owned influence palette was not attached.");
                GD.Print($"OPENNV_OWNED_ACTOR_SKIN_PASS model={model} vertices={result.Vertices} triangles={result.Triangles} surfaces={result.Surfaces} sourceMaterials=true finalPixels=unverified");
            }
            finally { skeleton.Node.Free(); }
        }
    }

    private static byte[] ActorSkinFixture(string rootName, bool mesh, string jointName = "Joint", bool nestedRoot = false)
    {
        static void Transform(BinaryWriter writer)
        { foreach (var value in new float[] { 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1, 1 }) writer.Write(value); }
        static void SkinTransform(BinaryWriter writer)
        { foreach (var value in new float[] { 1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1 }) writer.Write(value); }
        static void Object(BinaryWriter writer, int name)
        { writer.Write(name); writer.Write(0); writer.Write(-1); writer.Write(14U); Transform(writer); writer.Write(0); writer.Write(-1); }
        var blocks = new List<(string Type, byte[] Bytes)>
        {
            ("NiNode", Bytes(writer => { Object(writer, 0); writer.Write(mesh ? 2 : 1); writer.Write(1); if (mesh) writer.Write(2); writer.Write(0); })),
            ("NiNode", Bytes(writer => { Object(writer, 1); writer.Write(0); writer.Write(0); })),
        };
        if (mesh) blocks.AddRange([
            ("NiTriShape", Bytes(writer => { Object(writer, 2); writer.Write(3); writer.Write(4); writer.Write(0); writer.Write(-1); writer.Write((byte)0); })),
            ("NiTriShapeData", Bytes(writer =>
            {
                writer.Write(0); writer.Write((ushort)3); writer.Write((ushort)0); writer.Write((byte)1);
                foreach (var value in new float[] { 0, 0, 0, 1, 0, 0, 0, 1, 0 }) writer.Write(value);
                writer.Write((ushort)0); writer.Write((byte)0);
                writer.Write(0f); writer.Write(0f); writer.Write(0f); writer.Write(2f); writer.Write((byte)0);
                writer.Write((ushort)0); writer.Write(-1); writer.Write((ushort)1); writer.Write(3U); writer.Write((byte)1);
                writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)2); writer.Write((ushort)0);
            })),
            ("NiSkinInstance", Bytes(writer => { writer.Write(5); writer.Write(6); writer.Write(0); writer.Write(1); writer.Write(1); })),
            ("NiSkinData", Bytes(writer =>
            {
                SkinTransform(writer); writer.Write(1); writer.Write((byte)0); SkinTransform(writer);
                writer.Write(0f); writer.Write(0f); writer.Write(0f); writer.Write(2f); writer.Write((ushort)0);
            })),
            ("NiSkinPartition", Bytes(writer =>
            {
                writer.Write(1); foreach (var value in new ushort[] { 3, 1, 1, 0, 4, 0 }) writer.Write(value);
                writer.Write((byte)1); writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)2);
                writer.Write((byte)1); for (var row = 0; row < 3; row++) foreach (var value in new float[] { 1, 0, 0, 0 }) writer.Write(value);
                writer.Write((byte)1); writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)2);
                writer.Write((byte)1); writer.Write(new byte[12]);
            })),
        ]);
        var root = 0;
        if (nestedRoot)
        {
            root = blocks.Count;
            blocks.Add(("NiNode", Bytes(writer => { Object(writer, 3); writer.Write(1); writer.Write(0); writer.Write(0); })));
        }
        string[] names = [rootName, jointName, "Surface", "ModelWrapper"];
        return Bytes(writer =>
        {
            writer.Write("Gamebryo File Format, Version 20.2.0.7\n"u8); writer.Write(FalloutNifFile.Version); writer.Write((byte)1); writer.Write(FalloutNifFile.UserVersion);
            writer.Write(blocks.Count); writer.Write(34U); writer.Write(new byte[] { 1, 0, 1, 0, 1, 0 }); writer.Write((ushort)blocks.Count);
            foreach (var block in blocks) { writer.Write(block.Type.Length); writer.Write(Encoding.ASCII.GetBytes(block.Type)); }
            for (var index = 0; index < blocks.Count; index++) writer.Write((ushort)index);
            foreach (var block in blocks) writer.Write(block.Bytes.Length);
            writer.Write(names.Length); writer.Write(names.Max(name => name.Length));
            foreach (var name in names) { writer.Write(name.Length); writer.Write(Encoding.ASCII.GetBytes(name)); }
            writer.Write(0U); foreach (var block in blocks) writer.Write(block.Bytes);
            writer.Write(1U); writer.Write(root);
        });
    }
}
