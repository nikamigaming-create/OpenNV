using OpenNV.Runtime.World.Physics;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutWorldProjectionPlane(FalloutPhysicalVector3 Normal, uint Distance);
internal sealed record FalloutWorldProjectionCullingSnapshot(bool Accumulate, Guid? Camera, Guid? VisibleSet,
    FalloutWorldProjectionFrustum Frustum, IReadOnlyList<FalloutWorldProjectionPlane> Planes, uint ActivePlaneMask,
    Guid? CompoundFrustum, Guid? Scene, Guid? Callback);

// The original world constructor allocates this real logical culling state
// independently of graphics. A later traversal or compound-frustum operation
// still needs its reached source implementation; allocation grants no draw.
internal sealed class FalloutWorldProjectionCullingOwner
{
    private readonly FalloutWorldProjectionOwner _root;
    private FalloutWorldProjectionCullingSnapshot _state;
    internal Guid Factory { get; } = Guid.NewGuid();
    internal FalloutWorldProjectionCullingOwner(FalloutWorldProjectionOwner root, FalloutWorldProjectionCullingSnapshot? saved = null)
    {
        _root = root;
        _state = saved is not null ? saved with { Planes = Array.AsReadOnly(saved.Planes.ToArray()) } : new(false, null, null, new(0, 0, 0, 0, 0, 0, false),
            Array.AsReadOnly(Enumerable.Range(0, 6).Select(_ => new FalloutWorldProjectionPlane(new(0, 0, 0), 0)).ToArray()),
            0x3f, null, null, null);
        Validate(_state);
    }
    internal FalloutWorldProjectionCullingSnapshot Capture() { _root.RequireCurrent(); return _state; }
    internal FalloutWorldProjectionCullingSnapshot State => _state;
    internal static void Validate(FalloutWorldProjectionCullingSnapshot saved)
    {
        if (saved.Accumulate || saved.Camera is not null || saved.VisibleSet is not null ||
            saved.Frustum != new FalloutWorldProjectionFrustum(0, 0, 0, 0, 0, 0, false) || saved.Planes.Count != 6 ||
            saved.Planes.Any(plane => plane != new FalloutWorldProjectionPlane(new(0, 0, 0), 0)) || saved.ActivePlaneMask != 0x3f ||
            saved.CompoundFrustum is not null || saved.Scene is not null || saved.Callback is not null)
            throw new NotSupportedException("source-world-current-culling-traversal-mutation-unowned");
    }
}
