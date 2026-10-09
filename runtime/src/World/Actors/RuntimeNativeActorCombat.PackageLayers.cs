using System.Security.Cryptography;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.World.Actors;

internal sealed partial class RuntimeNativeActorCombat
{
    internal (RuntimeNativeNifAnimation Animation, float Seconds)? PackageBaseLayer
    {
        get
        {
            if (!PackageOwnsPose || _state.PackageMotion is not { } motion) return null;
            var clip = new[] { _packageIdle, _packageWalk, _packageRun }.Where(value => value is not null &&
                value.Path.Equals(motion.Animation, StringComparison.OrdinalIgnoreCase) &&
                value.Hash.Equals(motion.AnimationSha256, StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
            return clip is null ? null : (clip.Animation, clip.Time(motion.Seconds));
        }
    }

    internal (RuntimeNativeNifAnimation Animation, float Seconds) RestoredCollectionBase(FalloutFormKey package)
    {
        if (_state.PackageMotion is not { } motion || motion.Package != package ||
            _state.PackageAssignment?.Package != package)
            throw new InvalidDataException("Cold collection has no exact retained package base clock.");
        motion.Validate();
        var original = _records.GetEffective(package);
        if (!Convert.ToHexString(SHA256.HashData(original.ReadData())).Equals(motion.PackageSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Cold collection package base differs from its winning source.");
        var directory = _skeletonPath[.._skeletonPath.LastIndexOf('/')];
        var gender = _actor is RuntimeNativeNpc npc && npc.Appearance.Female ? "female" : "male";
        var roles = new[]
        {
            SelectPath(directory, "mtidle", "locomotion/mtidle"),
            SelectPath(directory, "mtforward", $"locomotion/{gender}/mtforward", "locomotion/mtforward"),
            SelectPath(directory, "mtfastforward", $"locomotion/{gender}/mtfastforward", "locomotion/mtfastforward",
                "mtforward", $"locomotion/{gender}/mtforward", "locomotion/mtforward")
        };
        if (!roles.Contains(motion.Animation, StringComparer.OrdinalIgnoreCase))
            throw new NotSupportedException("Cold collection retained an unowned package base role.");
        // Reconstruct only the retained clip. This binds its original channels
        // without moving the actor, advancing a clock or dispatching text keys.
        var clip = new NativeActorCombatAnimation(motion.Animation, _content, _skeleton, null, true);
        if (!clip.Hash.Equals(motion.AnimationSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Cold collection package base KF differs from its winning resource.");
        var root = _skeleton.BoneIndex(clip.Animation.Sequence.TargetName);
        if (root >= 0) _skeleton.Node.SetBonePose(root, Transform3D.Identity);
        return (clip.Animation, clip.Time(motion.Seconds));
    }
}
