using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutPlayerBonePose(int Index, int SourceBlock, string Name, int Parent,
    float[] Components)
{
    internal void Validate(int count)
    {
        if (Index < 0 || Index >= count || SourceBlock < 0 || string.IsNullOrWhiteSpace(Name) ||
            Parent < -1 || Parent >= Index || Components is not { Length: 10 } ||
            Components.Any(value => !float.IsFinite(value)) ||
            MathF.Abs(Components[3] * Components[3] + Components[4] * Components[4] +
                Components[5] * Components[5] + Components[6] * Components[6] - 1) > .001f ||
            Components[7] == 0 || Components[8] == 0 || Components[9] == 0)
            throw new InvalidDataException("Player physical bone pose has an invalid source identity or component extent.");
    }
}

// KF channels can cover only part of a bone. Preserve the actual local
// components; a cold constructor/rest pose cannot replace uncovered state.
internal sealed record FalloutPlayerPhysicalPose(string SkeletonSha256, float[] ActorTransform,
    float[] SkeletonTransform, IReadOnlyList<FalloutPlayerBonePose> Bones)
{
    internal void Validate()
    {
        if (!FalloutPlayerPhysicalSource.Digest(SkeletonSha256) || Bones is null || Bones.Count == 0 ||
            Bones.Any(bone => bone is null) || !Bones.Select(bone => bone.Index).SequenceEqual(Enumerable.Range(0, Bones.Count)) ||
            Bones.Select(bone => bone.SourceBlock).Distinct().Count() != Bones.Count ||
            Bones.Select(bone => bone.Name).Distinct(StringComparer.Ordinal).Count() != Bones.Count)
            throw new InvalidDataException("Player physical pose has no complete source skeleton denominator.");
        FalloutActorFurnitureContinuation.ValidatePose(ActorTransform);
        FalloutActorFurnitureContinuation.ValidatePose(SkeletonTransform);
        foreach (var bone in Bones) bone.Validate(Bones.Count);
    }
}
