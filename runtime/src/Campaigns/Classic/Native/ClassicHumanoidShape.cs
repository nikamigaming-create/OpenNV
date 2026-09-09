using Godot;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.Campaigns.Classic.Native;

/// <summary>Small source-likeness adjustments on the complete dressed rig, including its hand socket.</summary>
internal sealed class ClassicHumanoidShape(RuntimeNativeNifSkeleton skeleton, float shoulders, float waist)
{
    private readonly Dictionary<int, Transform3D> _before = [];
    internal void Restore()
    {
        foreach (var (bone, pose) in _before) skeleton.Node.SetBonePose(bone, pose);
        _before.Clear();
    }

    internal void Apply()
    {
        if (shoulders == 1 && waist == 1) return;
        if (shoulders is < 0.8f or > 1.3f || waist is < 0.8f or > 1.2f)
            throw new InvalidDataException("Classic body proportions exceed the supported source-likeness range.");
        var node = skeleton.Node;
        var poses = new Transform3D[node.GetBoneCount()];
        var changed = new bool[poses.Length];
        var waistBone = skeleton.BoneIndex("Bip01 Spine");
        var left = skeleton.BoneIndex("Bip01 L Clavicle");
        var right = skeleton.BoneIndex("Bip01 R Clavicle");
        for (var bone = 0; bone < poses.Length; bone++)
        {
            var parent = node.GetBoneParent(bone);
            poses[bone] = (parent < 0 ? Transform3D.Identity : poses[parent]) * node.GetBonePose(bone);
        }
        var shifts = new Vector3[poses.Length];
        for (var bone = 0; bone < poses.Length; bone++)
        {
            var parent = node.GetBoneParent(bone);
            shifts[bone] = bone == left || bone == right ? Vector3.Right * poses[bone].Origin.X * (shoulders - 1) :
                parent < 0 ? Vector3.Zero : shifts[parent];
            if (shifts[bone] != Vector3.Zero) { poses[bone].Origin += shifts[bone]; changed[bone] = true; }
        }
        poses[waistBone].Basis = Basis.FromScale(new Vector3(waist, 1, 1)) * poses[waistBone].Basis;
        changed[waistBone] = true;
        for (var bone = 0; bone < poses.Length; bone++)
        {
            var parent = node.GetBoneParent(bone);
            if (!changed[bone] && (parent < 0 || !changed[parent])) continue;
            _before.Add(bone, node.GetBonePose(bone));
            node.SetBonePose(bone, parent < 0 ? poses[bone] : poses[parent].AffineInverse() * poses[bone]);
        }
    }
}
