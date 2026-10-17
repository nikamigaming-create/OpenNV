using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Physics;

internal sealed record FalloutRagdollArmBoneRead(string Name, int Index);
internal sealed record FalloutRagdollArmTargetFields(FalloutPhysicalVector3 Position, bool Valid);
internal sealed record FalloutRagdollArmInitialization(FalloutReferenceModelDataInvocation Load, Guid? ExistingAnimation,
    IReadOnlyList<FalloutRagdollArmBoneRead> Reads, IReadOnlyList<int?> BoneIndices, byte Availability,
    FalloutRagdollArmTargetFields Left, FalloutRagdollArmTargetFields Right)
{
    internal static IReadOnlyList<string> BoneNames { get; } = Array.AsReadOnly(new[]
    { "Bip01 L UpperArm", "Bip01 L ForeArm", "Bip01 L Hand", "Bip01 R UpperArm", "Bip01 R ForeArm", "Bip01 R Hand" });

    internal static FalloutRagdollArmInitialization Read(FalloutReferenceModelDataInvocation load, FalloutRagdollAnimationDescriptor? existing)
    {
        if (existing is null)
            return new(load, null, [], new int?[6], 1, new(default, false), new(default, false));
        existing.Validate();
        var reads = BoneNames.Select(name => new FalloutRagdollArmBoneRead(name,
            existing.Bones.Select((bone, index) => (bone, index)).FirstOrDefault(row =>
                FalloutRagdollSkeletonMappingConstruction.Name(row.bone.Name) == FalloutRagdollSkeletonMappingConstruction.Name(name), (null!, -1)).index)).ToArray();
        return new(load, existing.Identity, reads, reads.Select(read => (int?)read.Index).ToArray(),
            reads.All(read => read.Index != -1) ? (byte)1 : (byte)0, new(default, false), new(default, false));
    }
    internal void Validate()
    {
        if (Load is null || Load.Call == Guid.Empty || Reads is null || BoneIndices is not { Count: 6 } || Availability > 1 ||
            Left is null || Right is null || Left.Position != default || Right.Position != default || Left.Valid || Right.Valid)
            throw new InvalidDataException("Ragdoll arm initialization lost its current source load, targets or six independent bone fields.");
        if (ExistingAnimation is null)
        {
            if (Reads.Count != 0 || BoneIndices.Any(index => index is not null) || Availability != 1)
                throw new InvalidDataException("Ragdoll arm cache miss invented source index stores or changed its retained availability byte.");
        }
        else if (ExistingAnimation == Guid.Empty || Reads.Count != 6 || Reads.Any(read => read is null) || !Reads.Select(read => read.Name).SequenceEqual(BoneNames) ||
            !Reads.Select(read => (int?)read.Index).SequenceEqual(BoneIndices) || Reads.Any(read => read is null || read.Index < -1 || read.Index > short.MaxValue) ||
            Availability != (Reads.All(read => read.Index != -1) ? 1 : 0))
            throw new InvalidDataException("Ragdoll arm cache read changed its actual name/index order or missing-bone branch.");
    }
    internal int RequireBoneIndex(int index)
    {
        Validate();
        if (index < 0 || index >= BoneIndices.Count) throw new ArgumentOutOfRangeException(nameof(index));
        return BoneIndices[index] ?? throw new NotSupportedException("source-ragdoll-arm-index-has-no-actual-writer");
    }
}

internal sealed partial class FalloutLivingActorModel
{
    private FalloutRagdollArmInitialization? _ragdollArms;
    internal FalloutRagdollArmInitialization? CaptureRagdollArms() => _ragdollArms;
    internal void InitializeRagdollArms(FalloutReferenceModelDataInvocation load, FalloutRagdollDescriptorCache cache,
        Action<FalloutReferenceModelDataInvocation> requireActual)
    {
        RequireCurrent(); requireActual(load);
        if (_ragdollArms is not null || _animationSkeleton is not null || _physicalSkeleton is not { } physical || physical.Load != load ||
            _ragdollDrive?.Capture() is not { Returned: true, Failure: null })
            throw new InvalidOperationException("Ragdoll arm initialization lost its same returned physical drive or replayed after animation construction.");
        _ragdollArms = FalloutRagdollArmInitialization.Read(load, cache.ReadExistingAnimation(physical.Descriptor));
        _ragdollArms.Validate(); requireActual(load);
    }
}
