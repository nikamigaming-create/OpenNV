using System.Text;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

public partial class NativeNifInstanceAudit
{
    private async Task ExerciseFaceGenAttachment()
    {
        const string head = FalloutNpcFaceAttachment.HeadBone;
        var skeleton = NativeNifMeshBuilder.BuildActorSkeleton(ActorSkinFixture("ActorRoot", false, head), 1);
        using var material = new StandardMaterial3D();
        IReadOnlyDictionary<string, FalloutNifTransform> binds = new Dictionary<string, FalloutNifTransform>
        {
            [head] = new(new(3, 5, 7), [0, -1, 0, 1, 0, 0, 0, 0, 1], 2),
        };
        try
        {
            AddChild(skeleton.Node);
            var scenes = new[] { FaceAttachmentFixture(null), FaceAttachmentFixture(head) }
                .Select(bytes => NativeNifMeshBuilder.AddActorPart(FalloutNifFile.Read(bytes), skeleton,
                    materialOverride: (_, _) => material, rigidFaceBinds: binds)).ToArray();
            var meshes = scenes.Select(scene => scene.Root.FindChildren("*", "MeshInstance3D", true, false)
                .Cast<MeshInstance3D>().Single()).ToArray();
            foreach (var scene in scenes)
            {
                var attachment = scene.Root.GetChildren().OfType<BoneAttachment3D>().Single();
                if (attachment.BoneName != head || scene.Vertices != 3 || scene.Triangles != 1 ||
                    !attachment.HasMeta("opennv_rigid_face_basis"))
                    throw new InvalidDataException("Rigid FaceGen omitted its source head binding or geometry.");
            }
            var expected = skeleton.Convert(binds[head]) * new Transform3D(Basis.Identity, new Vector3(2, 0, 0));
            if (meshes.Any(mesh => !mesh.Transform.IsEqualApprox(expected)))
                throw new InvalidDataException("Rigid FaceGen composed its export transform over the source head bind.");
            skeleton.Node.SetBonePoseRotation(skeleton.BoneIndex(head), new Quaternion(Vector3.Up, Mathf.Pi / 2));
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (!meshes[0].GlobalTransform.IsEqualApprox(meshes[1].GlobalTransform) ||
                meshes[0].GlobalTransform.IsEqualApprox(expected))
                throw new InvalidDataException("Implicit FaceGen attachment did not follow the same animated head as explicit Prn.");
            void Refuse(byte[] bytes, IReadOnlyDictionary<string, FalloutNifTransform>? owner)
            {
                var children = skeleton.Node.GetChildCount();
                try
                {
                    NativeNifMeshBuilder.AddActorPart(FalloutNifFile.Read(bytes), skeleton,
                        materialOverride: (_, _) => material, rigidFaceBinds: owner);
                    throw new InvalidOperationException("Unowned rigid actor geometry was accepted.");
                }
                catch (Exception error) when (error is InvalidDataException or NotSupportedException) { }
                if (skeleton.Node.GetChildCount() != children)
                    throw new InvalidDataException("Rejected FaceGen assembly retained a partial actor part.");
            }
            Refuse(FaceAttachmentFixture(null), null);
            Refuse(FaceAttachmentFixture(null), new Dictionary<string, FalloutNifTransform>());
            Refuse(FaceAttachmentFixture("MissingBone"), binds);
            Refuse(ActorSkinFixture("EquipmentRoot", true, head), binds);
            GD.Print("OPENNV_FACEGEN_ATTACHMENT_PASS implicit=true explicit=true source-inverse-bind=true animated-head=true export-transform-replaced=true unowned-rejected=true cleanup=true");
        }
        finally { skeleton.Node.Free(); }
    }

    private static byte[] FaceAttachmentFixture(string? parent)
    {
        void Object(BinaryWriter writer, int name, bool root)
        {
            writer.Write(name); writer.Write(root && parent is not null ? 1 : 0);
            if (root && parent is not null) writer.Write(3);
            writer.Write(-1); writer.Write(14U);
            foreach (var value in root ? new float[] { 19, 23, 29, 0, -1, 0, 1, 0, 0, 0, 0, 1, 3 }
                : [2, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1, .99999976f]) writer.Write(value);
            writer.Write(0); writer.Write(-1);
        }
        var blocks = new List<(string Type, byte[] Bytes)>
        {
            ("NiNode", Bytes(writer => { Object(writer, 0, true); writer.Write(1); writer.Write(1); writer.Write(0); })),
            ("NiTriShape", Bytes(writer => { Object(writer, 1, false); writer.Write(2); writer.Write(-1); writer.Write(0); writer.Write(-1); writer.Write((byte)0); })),
            ("NiTriShapeData", Bytes(writer =>
            {
                writer.Write(0); writer.Write((ushort)3); writer.Write((ushort)0); writer.Write((byte)1);
                foreach (var value in new float[] { 0, 0, 0, 1, 0, 0, 0, 1, 0 }) writer.Write(value);
                writer.Write((ushort)0); writer.Write((byte)0);
                writer.Write(0f); writer.Write(0f); writer.Write(0f); writer.Write(2f); writer.Write((byte)0);
                writer.Write((ushort)0); writer.Write(-1); writer.Write((ushort)1); writer.Write(3U); writer.Write((byte)1);
                writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)2); writer.Write((ushort)0);
            })),
        };
        if (parent is not null) blocks.Add(("NiStringExtraData", Bytes(writer => { writer.Write(2); writer.Write(3); })));
        string[] names = ["FaceExport", "FaceGeometry", "Prn", parent ?? "unused"];
        return Bytes(writer =>
        {
            writer.Write("Gamebryo File Format, Version 20.2.0.7\n"u8); writer.Write(FalloutNifFile.Version);
            writer.Write((byte)1); writer.Write(FalloutNifFile.UserVersion); writer.Write(blocks.Count); writer.Write(34U);
            writer.Write(new byte[] { 1, 0, 1, 0, 1, 0 }); writer.Write((ushort)blocks.Count);
            foreach (var block in blocks) { writer.Write(block.Type.Length); writer.Write(Encoding.ASCII.GetBytes(block.Type)); }
            for (var index = 0; index < blocks.Count; index++) writer.Write((ushort)index);
            foreach (var block in blocks) writer.Write(block.Bytes.Length);
            writer.Write(names.Length); writer.Write(names.Max(name => name.Length));
            foreach (var name in names) { writer.Write(name.Length); writer.Write(Encoding.ASCII.GetBytes(name)); }
            writer.Write(0U); foreach (var block in blocks) writer.Write(block.Bytes);
            writer.Write(1U); writer.Write(0);
        });
    }
}
