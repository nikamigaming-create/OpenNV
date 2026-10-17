namespace OpenNV.Runtime.World.Physics;

internal sealed record FalloutRagdollSimpleMapping(int Source, int Target, FalloutRagdollAnimationPose SourceFromTarget);
internal sealed record FalloutRagdollSkeletonMapping(Guid Identity, Guid Source, Guid Target,
    IReadOnlyList<FalloutRagdollSimpleMapping> Simple, IReadOnlyList<int> Unmapped);
internal sealed record FalloutRagdollSkeletonMappings(string Key, Guid Physical, Guid Animation,
    FalloutRagdollSkeletonMapping AnimationToPhysical, FalloutRagdollSkeletonMapping PhysicalToAnimation)
{
    internal void Validate(FalloutRagdollSkeletonDescriptor physical, FalloutRagdollAnimationDescriptor animation)
    {
        physical.Validate(); animation.Validate();
        if (Key != physical.Key || Key != animation.Key || Physical != physical.Identity || Animation != animation.Identity ||
            physical.RootTranslationLocked || animation.RootTranslationLocked)
            throw new InvalidDataException("Ragdoll mappings changed their actual shared skeletons or root translation stores.");
        Check(AnimationToPhysical, animation.Identity, physical.Identity, animation.Bones.Count, physical.Bones.Count);
        Check(PhysicalToAnimation, physical.Identity, animation.Identity, physical.Bones.Count, animation.Bones.Count);
        if (!AnimationToPhysical.Simple.Select(row => (row.Source, row.Target)).SequenceEqual(PhysicalToAnimation.Simple.Select(row => (row.Target, row.Source))))
            throw new InvalidDataException("Ragdoll bidirectional simple mappings changed their actual joined bone pairs.");
        static void Check(FalloutRagdollSkeletonMapping mapping, Guid source, Guid target, int sourceCount, int targetCount)
        {
            if (mapping is null || mapping.Identity == Guid.Empty || mapping.Source != source || mapping.Target != target || mapping.Simple is null || mapping.Unmapped is null ||
                mapping.Simple.Any(row => row is null || row.Source < 0 || row.Source >= sourceCount || row.Target < 0 || row.Target >= targetCount || row.SourceFromTarget is null) ||
                !mapping.Unmapped.SequenceEqual(Enumerable.Range(0, targetCount).Except(mapping.Simple.Select(row => row.Target))))
                throw new InvalidDataException("Ragdoll mapper lost its source/target bone joins or unmapped denominator.");
            foreach (var row in mapping.Simple) row.SourceFromTarget.Validate();
        }
    }
}

internal static class FalloutRagdollSkeletonMappingConstruction
{
    internal static FalloutRagdollSkeletonMappings Construct(FalloutRagdollSkeletonDescriptor physical, FalloutRagdollAnimationDescriptor animation)
    {
        physical.Validate(); animation.Validate();
        if (physical.Key != animation.Key || physical.Source != animation.Source)
            throw new InvalidDataException("Ragdoll mapping changed its shared physical/animation source entry.");
        // The actual caller requests automatic simple name mapping, supplies no
        // explicit names/chains, and disables automatic chain construction.
        var physicalNames = physical.Bones.Select(bone => Name(bone.Name)).ToArray();
        var animationNames = animation.Bones.Select(bone => Name(bone.Name)).ToArray();
        var physicalWorld = FalloutRagdollPoseMath.Worlds(physical.Bones.Select(bone => bone.Parent).ToArray(),
            physical.Bones.Select(bone => Pose(bone)).ToArray());
        var animationWorld = FalloutRagdollPoseMath.Worlds(animation.Bones.Select(bone => bone.Parent).ToArray(),
            animation.Bones.Select(bone => bone.ReferencePose).ToArray());
        var pairs = new List<(int Physical, int Animation)>();
        if (animationNames.Length > physicalNames.Length)
        {
            for (var p = 0; p < physicalNames.Length; ++p)
                for (var a = 0; a < animationNames.Length; ++a)
                    if (physicalNames[p] == animationNames[a]) { pairs.Add((p, a)); break; }
        }
        else
        {
            for (var a = 0; a < animationNames.Length; ++a)
                for (var p = 0; p < physicalNames.Length; ++p)
                    if (animationNames[a] == physicalNames[p]) { pairs.Add((p, a)); break; }
        }
        var forward = pairs.Select(pair => new FalloutRagdollSimpleMapping(pair.Animation, pair.Physical,
            FalloutRagdollPoseMath.Relative(animationWorld[pair.Animation], physicalWorld[pair.Physical]))).ToArray();
        var reverse = pairs.Select(pair => new FalloutRagdollSimpleMapping(pair.Physical, pair.Animation,
            FalloutRagdollPoseMath.Relative(physicalWorld[pair.Physical], animationWorld[pair.Animation]))).ToArray();
        return new(physical.Key, physical.Identity, animation.Identity,
            new(Guid.NewGuid(), animation.Identity, physical.Identity, forward,
                Enumerable.Range(0, physicalNames.Length).Except(pairs.Select(pair => pair.Physical)).ToArray()),
            new(Guid.NewGuid(), physical.Identity, animation.Identity, reverse,
                Enumerable.Range(0, animationNames.Length).Except(pairs.Select(pair => pair.Animation)).ToArray()));
    }
    internal static string Name(string name)
    {
        if (name.Any(value => value > 127)) throw new NotSupportedException("source-ragdoll-mapper-non-ASCII-name-comparison-unowned");
        return string.Create(name.Length, name, (span, text) =>
        { for (var index = 0; index < span.Length; ++index) span[index] = text[index] is >= 'A' and <= 'Z' ? (char)(text[index] + 32) : text[index]; });
    }
    private static FalloutRagdollAnimationPose Pose(FalloutRagdollPhysicalBone bone)
    {
        var r = bone.ReferencePose.Rotation;
        var rotation = FalloutRagdollAnimationPose.Read(new(default,
            [r.Column0.XValue, r.Column1.XValue, r.Column2.XValue, r.Column0.YValue, r.Column1.YValue, r.Column2.YValue,
             r.Column0.ZValue, r.Column1.ZValue, r.Column2.ZValue], BitConverter.UInt32BitsToSingle(bone.Scale)), 1);
        return rotation with { Translation = bone.ReferencePose.Translation };
    }
}
