using Godot;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal partial class RuntimeNativePlayer
{
    private FalloutPlayerPhysicalPose CapturePhysicalBones()
    {
        var actor = _thirdPerson!.Actor; var skeleton = actor.Skeleton; var node = skeleton.Node;
        var result = new FalloutPlayerPhysicalPose(skeleton.Source.Sha256, PhysicalPose(actor.Transform),
            PhysicalPose(node.Transform), Enumerable.Range(0, node.GetBoneCount()).Select(index =>
            {
                var position = node.GetBonePosePosition(index); var rotation = node.GetBonePoseRotation(index);
                var scale = node.GetBonePoseScale(index);
                return new FalloutPlayerBonePose(index, node.GetBoneMeta(index, "opennv_nif_block").AsInt32(),
                    node.GetBoneName(index).ToString(), node.GetBoneParent(index),
                    [position.X, position.Y, position.Z, rotation.X, rotation.Y, rotation.Z, rotation.W, scale.X, scale.Y, scale.Z]);
            }).ToArray());
        result.Validate(); return result;
    }

    private void RestorePhysicalBones(FalloutPlayerPhysicalPose saved)
    {
        saved.Validate(); var actor = _thirdPerson!.Actor; var skeleton = actor.Skeleton; var node = skeleton.Node;
        if (!saved.SkeletonSha256.Equals(skeleton.Source.Sha256, StringComparison.OrdinalIgnoreCase) ||
            saved.Bones.Count != node.GetBoneCount() || saved.Bones.Any(bone =>
                bone.SourceBlock != node.GetBoneMeta(bone.Index, "opennv_nif_block").AsInt32() ||
                bone.Name != node.GetBoneName(bone.Index).ToString() || bone.Parent != node.GetBoneParent(bone.Index)))
            throw new InvalidDataException("Cold physical player pose differs from its actual source bone graph.");
        actor.Transform = PhysicalPose(saved.ActorTransform); node.Transform = PhysicalPose(saved.SkeletonTransform);
        foreach (var bone in saved.Bones)
        {
            var p = bone.Components;
            node.SetBonePosePosition(bone.Index, new(p[0], p[1], p[2]));
            node.SetBonePoseRotation(bone.Index, new(p[3], p[4], p[5], p[6]));
            node.SetBonePoseScale(bone.Index, new(p[7], p[8], p[9]));
        }
    }
}
