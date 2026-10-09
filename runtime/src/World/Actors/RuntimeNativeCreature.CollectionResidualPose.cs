using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Actors;

internal sealed partial class RuntimeNativeCreature
{
    private FalloutActorResidualPose CaptureCollectionResidualPose(RuntimeNativeNifAnimation overlay)
    {
        var coverage = RuntimeNativeNifAnimation.TransformCoverage(CollectionBaseLayer().Animation, overlay);
        var node = Skeleton.Node;
        var bones = new List<FalloutActorResidualBonePose>();
        for (var index = 0; index < coverage.Length; ++index)
        {
            var missing = FalloutNifTransformComponents.All & ~coverage[index];
            if (missing == FalloutNifTransformComponents.None) continue;
            var p = node.GetBonePosePosition(index); var q = node.GetBonePoseRotation(index); var s = node.GetBonePoseScale(index);
            bones.Add(new(index, node.GetBoneName(index).ToString(),
                (missing & FalloutNifTransformComponents.Translation) == 0 ? null : [p.X, p.Y, p.Z],
                (missing & FalloutNifTransformComponents.Rotation) == 0 ? null : [q.X, q.Y, q.Z, q.W],
                (missing & FalloutNifTransformComponents.Scale) == 0 ? null : [s.X, s.Y, s.Z]));
        }
        return new(Appearance.SkeletonPath, Skeleton.Source.Sha256, bones);
    }

    private void RestoreCollectionResidualPose(FalloutActorResidualPose saved, RuntimeNativeNifAnimation overlay,
        RuntimeNativeNifAnimation basis)
    {
        var coverage = RuntimeNativeNifAnimation.TransformCoverage(basis, overlay);
        var node = Skeleton.Node;
        saved.ValidateBinding(Appearance.SkeletonPath, Skeleton.Source.Sha256,
            coverage.Select((mask, index) => (node.GetBoneName(index).ToString(), mask)).ToArray());
        foreach (var bone in saved.Bones)
        {
            if (bone.Position is { } p) node.SetBonePosePosition(bone.Index, new(p[0], p[1], p[2]));
            if (bone.Rotation is { } q) node.SetBonePoseRotation(bone.Index, new(q[0], q[1], q[2], q[3]));
            if (bone.Scale is { } s) node.SetBonePoseScale(bone.Index, new(s[0], s[1], s[2]));
        }
    }
}
