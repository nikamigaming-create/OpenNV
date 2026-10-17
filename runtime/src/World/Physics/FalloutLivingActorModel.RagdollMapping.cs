using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Physics;

internal sealed record FalloutLivingRagdollMappingSnapshot(FalloutReferenceModelDataInvocation Load,
    FalloutRagdollSkeletonMappings? Mappings, bool Cached, bool Returned, string? Failure);

internal sealed partial class FalloutLivingActorModel
{
    private FalloutLivingRagdollMappingSnapshot? _ragdollMapping;
    internal FalloutLivingRagdollMappingSnapshot? CaptureRagdollMapping() => _ragdollMapping;
    internal void InitializeRagdollMapping(FalloutReferenceModelDataInvocation load, FalloutRagdollDescriptorCache cache,
        Action<FalloutReferenceModelDataInvocation> requireActual)
    {
        RequireCurrent(); requireActual(load);
        if (_ragdollMapping is not null || _physicalSkeleton is not { } physical || physical.Load != load ||
            _animationSkeleton is not { Returned: true, Descriptor: { } animation, Failure: null } || _animationSkeleton.Load != load)
            throw new InvalidOperationException("Ragdoll mapper lost its same returned physical and animation source instances.");
        _ragdollMapping = new(load, null, false, false, null);
        try
        {
            var (mappings, cached) = cache.AcquireMappings(physical.Descriptor, animation, Check);
            _ragdollMapping = _ragdollMapping with { Mappings = mappings, Cached = cached, Returned = true };
        }
        catch (Exception error) { _ragdollMapping = _ragdollMapping with { Failure = error.Message }; throw; }
        void Check() { RequireCurrent(); requireActual(load); }
    }
}
