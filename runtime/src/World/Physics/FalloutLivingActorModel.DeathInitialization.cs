using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Physics;

internal sealed record FalloutRagdollDeathBoneRead(string Name, int Index);
internal sealed record FalloutRagdollDeathJointRead(int Constraint, Guid Wrapper, ulong NativeJoint, int Type);
internal sealed record FalloutRagdollDeathInitialization(FalloutReferenceModelDataInvocation Load, string Path,
    Guid Animation, Guid Physical, IReadOnlyList<FalloutRagdollDeathBoneRead> Bones,
    IReadOnlyList<FalloutRagdollDeathJointRead> Joints, bool? MotorsAdmitted, string Step, bool Returned, string? Failure)
{
    internal void Validate(FalloutLivingModelSnapshot model)
    {
        if (Load is null || Path is null || Bones is null || Joints is null || string.IsNullOrWhiteSpace(Step) ||
            model.Controller is not { Returned: true } controller || controller.Load != Load ||
            (controller.DeathPathStores == 0 ? Step != "path-publication" || Returned : controller.DeathAssetPath != Path) ||
            model.AnimationSkeleton?.Descriptor is not { } animation || animation.Identity != Animation ||
            model.PhysicalSkeleton is not { } physical || physical.Instance != Physical || physical.Load != Load ||
            model.RagdollBinding is not { } binding || !Bones.SequenceEqual(FalloutRagdollDeathBones.Read(animation).Take(Bones.Count)) ||
            Bones.Count > 3 || Returned && (Failure is not null || MotorsAdmitted != false || Bones.Count != 3 || Bones.Any(bone => bone.Index == -1)) ||
            Failure is not null && string.IsNullOrWhiteSpace(Failure))
            throw new InvalidDataException("Death initializer lost its actual fields, default bones or retained source prefix.");
        if (!Joints.Select(joint => joint.Constraint).SequenceEqual(physical.Descriptor.Constraints.Take(Joints.Count)) ||
            Joints.Any(joint => joint.Wrapper == Guid.Empty || !binding.Joints.Any(current =>
                current.Constraint == joint.Constraint && current.NativeJoint == joint.NativeJoint)) ||
            MotorsAdmitted == false && (Joints.Count == 0 || Joints[^1].Type is not (12 or 13)))
            throw new InvalidDataException("Death initializer borrowed another actual current physical constraint array.");
    }
}

internal static class FalloutRagdollDeathBones
{
    internal static IReadOnlyList<string> Names { get; } = Array.AsReadOnly(new[] { "Bip01 NonAccum", "Bip01 Head", "Bip01 R UpperArm" });
    internal static IReadOnlyList<FalloutRagdollDeathBoneRead> Read(FalloutRagdollAnimationDescriptor animation)
    {
        animation.Validate();
        return Names.Select(name => new FalloutRagdollDeathBoneRead(name, animation.Bones.Select((bone, index) => (bone, index))
            .FirstOrDefault(row => FalloutRagdollSkeletonMappingConstruction.Name(row.bone.Name) ==
                FalloutRagdollSkeletonMappingConstruction.Name(name), (null!, -1)).index)).ToArray();
    }
}

internal sealed partial class FalloutLivingActorModel
{
    private FalloutRagdollDeathInitialization? _deathInitialization;
    private bool _deathEntered;
    private long _deathReentry;
    internal FalloutRagdollDeathInitialization? CaptureDeathInitialization() => _deathInitialization;

    internal void InitializeRagdollDeathAsset(FalloutReferenceModelDataInvocation load, string path,
        Action<FalloutReferenceModelDataInvocation> requireActual)
    {
        if (_deathEntered) { ++_deathReentry; throw new InvalidOperationException("Death initializer reentered its actual current owner."); }
        var fault = _deathReentry;
        Check();
        if (_deathInitialization is not null || _physicalSkeleton is not { } physical || physical.Load != load ||
            _animationSkeleton is not { Returned: true, Descriptor: { } animation } || _animationSkeleton.Load != load)
            throw new InvalidOperationException("Ragdoll death initializer lost its same current physical and animation objects or replayed.");
        _deathInitialization = new(load, path, animation.Identity, physical.Instance, [], [], null, "path-publication", false, null);
        _deathEntered = true;
        try
        {
            _ragdollController!.PublishDeathAssetPath(load, path);
            Step("current-default-bone-indices");
            _deathInitialization = _deathInitialization with { Bones = FalloutRagdollDeathBones.Read(animation) };
            Step("current-constraint-motor-admission");
            var admitted = true; var reads = new List<FalloutRagdollDeathJointRead>();
            foreach (var index in physical.Descriptor.Constraints)
            {
                Check(); var joint = _constraints[index]; joint.RequireCurrent();
                var data = joint.Wrapper.CurrentData ?? throw new InvalidDataException("Death initializer lost its current constraint data object.");
                if (!joint.Backend.HasPhysicalObject) throw new InvalidDataException("Death initialization lost a real current joint.");
                reads.Add(new(index, joint.Wrapper.Instance, joint.Backend.PhysicalObjectId, data.Type));
                _deathInitialization = _deathInitialization with { Joints = reads.ToArray() };
                if (data.Type is 12 or 13) { admitted = false; break; }
                if (data.Type is 2 or 7)
                    throw new NotSupportedException("source-ragdoll-current-constraint-motor-pointer-getters-unowned");
            }
            _deathInitialization = _deathInitialization with { MotorsAdmitted = admitted };
            if (!admitted && _deathInitialization.Bones.All(bone => bone.Index != -1))
            {
                Step("source-motor-admission-return");
                _deathInitialization = _deathInitialization with { Returned = true };
                return;
            }
            Step("source-powered-controller-construction");
            throw new NotSupportedException("source-ragdoll-powered-controller-and-death-asset-reader-unowned");
        }
        catch (Exception error) { _deathInitialization = _deathInitialization with { Failure = error.Message }; throw; }
        finally { _deathEntered = false; }

        void Check()
        {
            RequireCurrent(); requireActual(load); _ragdollController!.RequireReturned();
            if (_deathEntered && _deathReentry != fault) throw new InvalidOperationException("Death initializer swallowed source reentry.");
        }
        void Step(string step) { Check(); _deathInitialization = _deathInitialization! with { Step = step }; }
    }
}
