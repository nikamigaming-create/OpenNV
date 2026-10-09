using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.World.Actors;

internal sealed record NativeDetectionSightPoints(FalloutDetectionSightBindings Binding,
    Vector3? HeadWorldPosition, Vector3? TorsoWorldPosition);

// Adapts actual published source bones. This helper does not create a process,
// choose a detection target, fit bounds or perform a visibility test.
internal static class NativeDetectionSight
{
    internal static NativeDetectionSightPoints Read(FalloutBodyPartData parts,
        RuntimeNativeNifSkeleton skeleton)
    {
        ArgumentNullException.ThrowIfNull(skeleton);
        var binding = FalloutDetectionSightBindings.Read(parts);
        Vector3? Sample(string? name)
        {
            if (name is null) return null; // The source table has no declared target.
            var bone = skeleton.BoneIndex(name);
            var point = (skeleton.Node.GlobalTransform * skeleton.Node.GetBoneGlobalPose(bone)).Origin;
            return point.IsFinite() ? point : throw new InvalidDataException("Detection sight bone has a non-finite published world pose.");
        }
        return new(binding, Sample(binding.HeadTarget), Sample(binding.TorsoTarget));
    }
}
