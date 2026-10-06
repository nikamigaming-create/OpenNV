using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Actors;

internal sealed partial class NativeActorWeaponAttachment
{
    private readonly string _modelResource, _modelSha256;
    private readonly Node[] _sourceNodes;

    internal FalloutCorpseWeaponAttachment CapturePersistence(string owner, FalloutPluginStack records)
    {
        RequirePersistenceBoundary();
        var nodes = new[] { Root }.Concat(Nodes).ToArray();
        var poses = nodes.Select((node, index) =>
        {
            var parent = node.GetParent();
            var bone = parent is BoneAttachment3D attachment &&
                (attachment == Attachment || _bodyAttachments.Contains(attachment)) ? attachment.BoneName.ToString() : null;
            var parentIndex = bone is null ? Array.IndexOf(nodes, parent) : -1;
            if (bone is null && parentIndex < 0)
                throw new NotSupportedException("Corpse weapon source node left its original attachment graph.");
            int? sourceBlock = node.HasMeta("opennv_nif_block") ? node.GetMeta("opennv_nif_block").AsInt32() :
                node.HasMeta("opennv_nif_geometry_block") ? node.GetMeta("opennv_nif_geometry_block").AsInt32() : null;
            return new FalloutCorpseWeaponNode(index, parentIndex, bone, sourceBlock,
                node.GetMeta("opennv_nif_source_name", "").AsString(), node.GetType().FullName!,
                WritePose(node.Transform), node.Visible, node is GeometryInstance3D geometry ? geometry.Layers : null);
        }).ToArray();
        var result = new FalloutCorpseWeaponAttachment(owner,
            new(Weapon.Form, FalloutActorFurnitureContinuation.RecordHash(records.GetEffective(Weapon.Form))),
            _modelResource, _modelSha256, Attachment.Visible, poses,
            _bodyAttachments.Select((part, index) => new FalloutCorpseWeaponBodyAttachment(index, part.BoneName.ToString(), part.Visible)).ToArray());
        result.Validate();
        return result;
    }

    internal void ValidatePersistence(FalloutCorpseWeaponAttachment saved, FalloutPluginStack records) =>
        saved.ValidateBinding(CapturePersistence(saved.Owner, records));

    internal void RestorePersistence(FalloutCorpseWeaponAttachment saved, FalloutPluginStack records)
    {
        ValidatePersistence(saved, records);
        var nodes = new[] { Root }.Concat(Nodes).ToArray();
        for (var index = 0; index < nodes.Length; index++)
        {
            var pose = saved.Nodes[index];
            nodes[index].Transform = ReadPose(pose.Transform);
            nodes[index].Visible = pose.Visible;
            if (nodes[index] is GeometryInstance3D geometry) geometry.Layers = pose.RenderLayers!.Value;
        }
        Attachment.Visible = saved.PrimaryVisible;
        // Root visibility changes also update anatomical packs. Restore their
        // independent values last, without changing the pre-death presentation.
        for (var index = 0; index < _bodyAttachments.Count; index++)
            _bodyAttachments[index].Visible = saved.BodyAttachments[index].Visible;
    }

    private void RequirePersistenceBoundary()
    {
        if (!GodotObject.IsInstanceValid(Attachment) || !GodotObject.IsInstanceValid(Root) ||
            _sourceNodes.Any(node => !GodotObject.IsInstanceValid(node)))
            throw new NotSupportedException("Corpse weapon lost its original native attachment graph.");
        if (_sourceNodes.OfType<RuntimeNifControllerPlayer>().Any(player => player.ActiveSequence is not null ||
            player.CaptureScriptState() is not null))
            throw new NotSupportedException("Corpse weapon model has an unowned internal controller clock.");
        if (_sourceNodes.Any(node => node is RuntimeNifParticleSystem or NativeNifBillboard))
            throw new NotSupportedException("Corpse weapon model has an unowned particle or billboard continuation.");
        if (_sourceNodes.Any(node => node is Skeleton3D))
            throw new NotSupportedException("Corpse weapon model has an unowned independent skeleton continuation.");
        if (_sourceNodes.OfType<RigidBody3D>().Any(body => !body.Freeze || body.TopLevel))
            throw new NotSupportedException("Corpse weapon has an independent dynamic body; drop/physics continuation is unowned.");
        if (_sourceNodes.OfType<MeshInstance3D>().Any(mesh => mesh.Mesh is ArrayMesh array && array.GetBlendShapeCount() != 0))
            throw new NotSupportedException("Corpse weapon model has an unowned deformation continuation.");
        if (_sourceNodes.Any(node => node is not RuntimeNifControllerPlayer &&
            (node.IsProcessing() || node.IsPhysicsProcessing())))
            throw new NotSupportedException("Corpse weapon model has an unowned autonomous native owner.");
    }

    internal void FreeFailedRestore()
    {
        foreach (var body in _bodyAttachments)
            if (GodotObject.IsInstanceValid(body)) body.Free();
        if (GodotObject.IsInstanceValid(Attachment)) Attachment.Free();
    }

    private static float[] WritePose(Transform3D pose) =>
        [pose.Basis.X.X, pose.Basis.X.Y, pose.Basis.X.Z, pose.Basis.Y.X, pose.Basis.Y.Y, pose.Basis.Y.Z,
            pose.Basis.Z.X, pose.Basis.Z.Y, pose.Basis.Z.Z, pose.Origin.X, pose.Origin.Y, pose.Origin.Z];

    private static Transform3D ReadPose(IReadOnlyList<float> pose) => new(
        new Vector3(pose[0], pose[1], pose[2]), new Vector3(pose[3], pose[4], pose[5]),
        new Vector3(pose[6], pose[7], pose[8]), new Vector3(pose[9], pose[10], pose[11]));
}
