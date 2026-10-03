using System.Security.Cryptography;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Presentation.Rendering;

public partial class NativeNifInstanceAudit
{
    private async Task ExerciseHeadEquipmentAttachment()
    {
        const string head = FalloutNpcFaceAttachment.HeadBone;
        if (!FalloutNpcFaceAttachment.IsRigidHeadEquipment(0x0800) || !FalloutNpcFaceAttachment.IsRigidHeadEquipment(0x4800) ||
            FalloutNpcFaceAttachment.IsRigidHeadEquipment(0) || FalloutNpcFaceAttachment.IsRigidHeadEquipment(4))
            throw new InvalidDataException("Biped slot classification omitted eyeglasses or admitted body armor.");
        var skeleton = NativeNifMeshBuilder.BuildActorSkeleton(ActorSkinFixture("ActorRoot", false, head), 1);
        using var material = new StandardMaterial3D();
        try
        {
            AddChild(skeleton.Node);
            var scenes = new[] { FaceAttachmentFixture(null), FaceAttachmentFixture(head) }
                .Select(bytes => NativeNifMeshBuilder.AddActorPart(FalloutNifFile.Read(bytes), skeleton,
                    materialOverride: (_, _) => material, bipedSlots: 0x4800)).ToArray();
            var meshes = scenes.Select(scene => scene.Root.FindChildren("*", "MeshInstance3D", true, false)
                .Cast<MeshInstance3D>().Single()).ToArray();
            var expected = skeleton.Convert(FalloutNpcFaceAttachment.RigidHeadEquipmentBind) *
                new Transform3D(Basis.Identity, new Vector3(2, 0, 0));
            foreach (var scene in scenes)
            {
                var attachment = scene.Root.GetChildren().OfType<BoneAttachment3D>().Single();
                if (attachment.BoneName != head || !attachment.HasMeta("opennv_biped_attachment"))
                    throw new InvalidDataException("Head equipment lost its source head-bone basis.");
            }
            if (meshes.Any(mesh => !mesh.Transform.IsEqualApprox(expected)))
                throw new InvalidDataException("Explicit Prn equipment retained the wrong export/bone frame.");
            skeleton.Node.SetBonePoseRotation(skeleton.BoneIndex(head), new Quaternion(Vector3.Up, Mathf.Pi / 2));
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (!meshes[0].GlobalTransform.IsEqualApprox(meshes[1].GlobalTransform) ||
                meshes[0].GlobalTransform.IsEqualApprox(expected))
                throw new InvalidDataException("Explicit and implicit head equipment diverged during animation.");
            var skinned = NativeNifMeshBuilder.AddActorPart(FalloutNifFile.Read(ActorSkinFixture("SkinRoot", true, head)),
                skeleton, materialOverride: (_, _) => material, bipedSlots: 0x4800);
            if (skinned.Root.GetChildren().OfType<BoneAttachment3D>().Any() ||
                skinned.Root.FindChildren("*", "MeshInstance3D", true, false).Cast<MeshInstance3D>().Single().Skin is null)
                throw new InvalidDataException("Skinned head equipment acquired a rigid attachment.");
            var children = skeleton.Node.GetChildCount();
            try
            {
                NativeNifMeshBuilder.AddActorPart(FalloutNifFile.Read(FaceAttachmentFixture("MissingBone")), skeleton,
                    materialOverride: (_, _) => material, bipedSlots: 0x4800);
                throw new InvalidOperationException("An explicit missing Prn was replaced with an inferred head.");
            }
            catch (InvalidDataException) { }
            if (skeleton.Node.GetChildCount() != children)
                throw new InvalidDataException("Rejected head equipment retained partial nodes.");
            GD.Print("OPENNV_HEAD_EQUIPMENT_ATTACHMENT_PASS eyeGlassesSlot=true explicitPrn=true implicitPrn=true " +
                "headBasis=true exportTransformReplaced=true animatedHead=true skinnedPreserved=true missingPrnRefused=true cleanup=true");
        }
        finally { skeleton.Node.Free(); }
    }

    private async Task ExerciseOwnedHeadEquipment(string game, string mod, string root, string npcId, string[] dependencies)
    {
        var setup = new FalloutModStackSelection([new(mod, root, dependencies)]).Resolve(game);
        using var content = setup.OpenSource();
        using var records = FalloutPluginStack.Load(content.PluginSources);
        var npc = FalloutDialogueTopic.Find(records, "NPC_", npcId).FormKey;
        var appearance = FalloutNpcAppearanceResolver.Resolve(records, npc) with { RuntimeFace = true };
        if (!content.TryRead(appearance.SkeletonPath, null, out var skeletonBytes, out _))
            throw new FileNotFoundException(appearance.SkeletonPath);
        var selected = appearance.Models.Where(part => (part.Role is "armor" or "armor-addon") &&
            FalloutNpcFaceAttachment.IsRigidHeadEquipment(part.BipedSlots)).ToArray();
        var bound = 0;
        foreach (var part in selected)
        {
            if (!content.TryRead(part.ModelPath!, null, out var bytes, out _)) throw new FileNotFoundException(part.ModelPath);
            var model = FalloutNifFile.Read(bytes);
            if (model.Blocks.Where(block => block.TypeName is "NiTriShape" or "NiTriStrips" or "BSSegmentedTriShape")
                .Any(block => model.ReadGeometry(block.Index).SkinInstance >= 0)) continue;
            var skeleton = NativeNifMeshBuilder.BuildActorSkeleton(skeletonBytes, .0142875f);
            try
            {
                AddChild(skeleton.Node); skeleton.Node.Scale = Vector3.One * appearance.Height;
                var scene = NativeNifMeshBuilder.AddActorPart(model, skeleton,
                    materialOverride: (source, geometry) => NativeNpcMaterial.Resolve(appearance, part, source, geometry, records, Colors.Black, content),
                    contentSource: content, bipedSlots: part.BipedSlots);
                var meshes = scene.Root.FindChildren("*", "MeshInstance3D", true, false).Cast<MeshInstance3D>().ToArray();
                var attachments = scene.Root.GetChildren().OfType<BoneAttachment3D>().ToArray();
                if (scene.Vertices == 0 || meshes.Length == 0 || attachments.Length == 0 ||
                    attachments.Any(attachment => attachment.BoneName != FalloutNpcFaceAttachment.HeadBone || !attachment.HasMeta("opennv_biped_attachment")))
                    throw new InvalidDataException("Owned equipment lacks the actual head attachment or source geometry.");
                foreach (var mesh in meshes)
                {
                    var geometry = model.ReadGeometry(mesh.GetMeta("opennv_nif_geometry_block").AsInt32());
                    var p = geometry.Transform.Translation;
                    var expected = skeleton.Convert(FalloutNpcFaceAttachment.RigidHeadEquipmentBind) *
                        new Transform3D(Basis.Identity, GamebryoCoordinate.ConvertVector(new Vector3(p.X, p.Y, p.Z)) * .0142875f);
                    if (!mesh.Transform.IsEqualApprox(expected)) throw new InvalidDataException("Owned Prn head equipment has the wrong local basis.");
                }
                var before = meshes.Select(mesh => mesh.GlobalTransform).ToArray();
                skeleton.Node.SetBonePoseRotation(skeleton.BoneIndex(FalloutNpcFaceAttachment.HeadBone),
                    new Quaternion(Vector3.Up, Mathf.Pi / 3));
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                if (meshes.Select((mesh, index) => mesh.GlobalTransform.IsEqualApprox(before[index])).All(value => value))
                    throw new InvalidDataException("Owned head equipment did not follow the native head pose.");
                if (!content.TryRead(part.ModelPath!, null, out var after, out _) ||
                    !SHA256.HashData(bytes).SequenceEqual(SHA256.HashData(after)))
                    throw new InvalidDataException("Owned head equipment bytes changed.");
                GD.Print($"OPENNV_OWNED_HEAD_EQUIPMENT_PASS npc={npc} equipment={part.Source} slots={part.BipedSlots:x8} " +
                    $"vertices={scene.Vertices} triangles={scene.Triangles} headBasis=true animated=true sourceMaterials=true sourceReadOnly=true finalPixels=unverified");
                bound++;
            }
            finally { skeleton.Node.Free(); }
        }
        if (bound == 0) throw new InvalidDataException("The owned selection has no rigid source head equipment.");
    }
}
