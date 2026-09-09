using Godot;
using OpenNV.Runtime.World.Actors;

namespace OpenNV.Runtime.Presentation.OpenXR;

internal static class NativeXrEyeFrame
{
    // Camera1st is the authored flat weapon camera, not the body's eyes.
    // Measure the actual FaceGen eye geometry in its source head attachment.
    internal static Transform3D InHead(RuntimeNativeNpc actor)
    {
        var centers = new Vector3[2];
        for (var side = 0; side < 2; side++)
        {
            var role = side == 0 ? "eye-left" : "eye-right";
            var index = actor.Appearance.Models.Select((part, index) => (part, index)).Single(value => value.part.Role == role).index;
            Aabb? bounds = null;
            foreach (var mesh in actor.Parts[index].Root.FindChildren("*", "", true, false).OfType<MeshInstance3D>())
            {
                if (mesh.Skin is not null) throw new NotSupportedException("Skinned eye geometry needs an anatomical eye-frame binding.");
                var pose = mesh.Transform;
                var parent = mesh.GetParent<Node3D>();
                while (parent is not BoneAttachment3D)
                {
                    pose = parent.Transform * pose;
                    parent = parent.GetParent<Node3D>() ?? throw new InvalidDataException("Eye has no source bone attachment.");
                }
                var attachment = (BoneAttachment3D)parent;
                var bone = actor.Skeleton.BoneIndex(attachment.BoneName);
                pose = actor.Skeleton.Node.GetBoneGlobalRest(bone) * pose;
                var bound = pose * mesh.GetAabb();
                bounds = bounds?.Merge(bound) ?? bound;
            }
            centers[side] = (bounds ?? throw new InvalidDataException("Source eye has no geometry.")).GetCenter();
        }
        var right = (centers[1] - centers[0]).Normalized();
        if (right.Dot(Vector3.Right) < .9f) throw new NotSupportedException("Source eye pair does not match the humanoid facing frame.");
        var up = Vector3.Up.Slide(right).Normalized();
        var eye = new Transform3D(new Basis(right, up, right.Cross(up)), (centers[0] + centers[1]) * .5f);
        return actor.Skeleton.Node.GetBoneGlobalRest(actor.Skeleton.BoneIndex("Bip01 Head")).AffineInverse() * eye;
    }
}
