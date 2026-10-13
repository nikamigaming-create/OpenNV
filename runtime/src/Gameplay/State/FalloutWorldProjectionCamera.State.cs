using OpenNV.Runtime.World.Physics;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutWorldProjectionFrustum(uint Left, uint Right, uint Top, uint Bottom, uint Near, uint Far, bool Orthographic);
internal sealed record FalloutWorldProjectionViewport(uint Left, uint Right, uint Top, uint Bottom);
internal sealed record FalloutWorldProjectionBounds(FalloutPhysicalVector3 Center, uint Radius);
internal sealed record FalloutWorldProjectionPose(IReadOnlyList<uint> Rotation, FalloutPhysicalVector3 Translation, uint Scale)
{
    internal static FalloutWorldProjectionPose Identity() => new(Array.AsReadOnly(new uint[]
        { 0x3f800000, 0, 0, 0, 0x3f800000, 0, 0, 0, 0x3f800000 }), new(0, 0, 0), 0x3f800000);
    internal static FalloutWorldProjectionPose OrdinaryCamera() => new(Array.AsReadOnly(new uint[]
        { 0, 0, 0x3f800000, 0x3f800000, 0, 0, 0, 0x3f800000, 0 }), new(0, 0, 0), 0x3f800000);
    internal void Validate()
    {
        if (Rotation.Count != 9 || Rotation.Any(bits => !float.IsFinite(BitConverter.UInt32BitsToSingle(bits))) ||
            !float.IsFinite(BitConverter.UInt32BitsToSingle(Scale)) ||
            !float.IsFinite(Translation.XValue) || !float.IsFinite(Translation.YValue) || !float.IsFinite(Translation.ZValue))
            throw new InvalidDataException("Source camera pose lost its actual Float32 transform fields.");
    }
    internal bool Same(FalloutWorldProjectionPose other) => Rotation.SequenceEqual(other.Rotation) && Translation == other.Translation && Scale == other.Scale;
}
internal sealed record FalloutWorldProjectionCameraSnapshot(FalloutWorldProjectionFrustum Frustum,
    FalloutWorldProjectionViewport Viewport, uint MinimumNear, uint MaximumFarNearRatio, uint Ratio,
    FalloutWorldProjectionPose Local, FalloutWorldProjectionPose World, IReadOnlyList<uint> Matrix,
    FalloutWorldProjectionBounds Bounds, long FrustumStores, long Updates, bool Attached,
    FalloutWorldProjectionPose MatrixPose, FalloutWorldProjectionFrustum MatrixFrustum);

internal sealed partial class FalloutWorldProjectionCamera
{
    private FalloutWorldProjectionFrustum _frustum = new(0xbf000000, 0x3f000000, 0x3f000000, 0xbf000000, 0x3f800000, 0x40000000, false);
    private FalloutWorldProjectionViewport _viewport = new(0, 0x3f800000, 0x3f800000, 0);
    private FalloutWorldProjectionPose _local = FalloutWorldProjectionPose.Identity(), _world = FalloutWorldProjectionPose.Identity();
    private IReadOnlyList<uint> _matrix = Array.Empty<uint>();
    private FalloutWorldProjectionPose _matrixPose = FalloutWorldProjectionPose.Identity();
    private FalloutWorldProjectionFrustum _matrixFrustum = new(0xbf000000, 0x3f000000, 0x3f000000, 0xbf000000, 0x3f800000, 0x40000000, false);
    private FalloutWorldProjectionBounds _bounds = new(new(0, 0, 0), 0);
    private uint _minimumNear = 0x3dcccccd, _maximumFarNearRatio = 0x461c4000;
    private long _frustumStores, _updates;
    internal uint MaximumFarNearRatio { get => _maximumFarNearRatio; set { RequireCurrent(); _maximumFarNearRatio = value; } }
    internal FalloutWorldProjectionViewport Viewport => _viewport;
    internal FalloutWorldProjectionFrustum Frustum => _frustum;
    internal FalloutWorldProjectionCameraSnapshot Capture()
    {
        RequireCurrent(); return ReadState();
    }
    internal FalloutWorldProjectionCameraSnapshot ReadState() => new(_frustum, _viewport, _minimumNear, _maximumFarNearRatio,
            RatioBits ?? 0x3f800000, _local, _world, _matrix.ToArray(), _bounds, _frustumStores, _updates, Parent is not null,
            _matrixPose, _matrixFrustum);
    internal void InitializeOrdinary()
    {
        RequireCurrent();
        _viewport = new(0, 0x3f800000, 0x3f800000, 0); _local = FalloutWorldProjectionPose.OrdinaryCamera(); _minimumNear = 0x3f800000;
    }
    internal void StoreFrustum(FalloutWorldProjectionFrustum requested)
    {
        RequireCurrent();
        var near = BitConverter.UInt32BitsToSingle(requested.Near);
        _frustum = _frustum with { Near = requested.Near };
        var ratioNear = FalloutPhysicalFloat32.Divide(BitConverter.UInt32BitsToSingle(requested.Far), BitConverter.UInt32BitsToSingle(_maximumFarNearRatio));
        if (ratioNear > near) { near = ratioNear; _frustum = _frustum with { Near = BitConverter.SingleToUInt32Bits(near) }; }
        var minimum = BitConverter.UInt32BitsToSingle(_minimumNear);
        if (minimum > near) near = minimum;
        _frustum = requested with { Near = BitConverter.SingleToUInt32Bits(near) }; _frustumStores = checked(_frustumStores + 1);
    }
    internal void UpdateOrdinary()
    {
        RequireCurrent();
        _local.Validate();
        if (Parent is not null && !Parent.WorldPose.Same(FalloutWorldProjectionPose.Identity()))
            throw new NotSupportedException("source-camera-current-parent-transform-composition-unowned");
        // This is the source constructor arm: an unparented camera copies its
        // local fields; the later identity scene parent preserves those fields.
        // Nonconstructor parent transform writers retain their separate owner.
        _world = _local;
        _matrix = FalloutWorldProjectionCameraMath.Perspective(_world, _frustum);
        _matrixPose = _world; _matrixFrustum = _frustum;
        _bounds = new(_world.Translation, 0); _updates = checked(_updates + 1);
        Parent?.UpdateBoundsFromChildren();
    }
    internal void Restore(FalloutWorldProjectionCameraSnapshot saved)
    {
        Validate(saved); _frustum = saved.Frustum; _viewport = saved.Viewport; _minimumNear = saved.MinimumNear;
        _maximumFarNearRatio = saved.MaximumFarNearRatio; RatioBits = saved.Ratio; _local = CopyPose(saved.Local); _world = CopyPose(saved.World);
        _matrix = Array.AsReadOnly(saved.Matrix.ToArray()); _bounds = saved.Bounds; _frustumStores = saved.FrustumStores; _updates = saved.Updates;
        _matrixPose = CopyPose(saved.MatrixPose); _matrixFrustum = saved.MatrixFrustum;
    }
    private static FalloutWorldProjectionPose CopyPose(FalloutWorldProjectionPose pose) =>
        new(Array.AsReadOnly(pose.Rotation.ToArray()), pose.Translation, pose.Scale);
    internal static void Validate(FalloutWorldProjectionCameraSnapshot saved)
    {
        saved.Local.Validate(); saved.World.Validate(); saved.MatrixPose.Validate();
        var identity = FalloutWorldProjectionPose.Identity(); var ordinary = FalloutWorldProjectionPose.OrdinaryCamera();
        if (saved.FrustumStores < 0 || saved.Updates < 1 || saved.Matrix.Count != 16 ||
            saved.Bounds.Center != saved.World.Translation || saved.Bounds.Radius != 0 ||
            !saved.Local.Same(identity) && !saved.Local.Same(ordinary) ||
            !saved.World.Same(identity) && !saved.World.Same(ordinary) ||
            !saved.MatrixPose.Same(identity) && !saved.MatrixPose.Same(ordinary) ||
            saved.Viewport != new FalloutWorldProjectionViewport(0, 0x3f800000, 0x3f800000, 0) ||
            saved.MinimumNear != (saved.Local.Same(identity) ? 0x3dcccccd : 0x3f800000))
            throw new InvalidDataException("Cold logical camera changed its actual constructor/update state.");
        if (saved.Updates > 0 && !saved.Matrix.SequenceEqual(FalloutWorldProjectionCameraMath.Perspective(saved.MatrixPose, saved.MatrixFrustum)))
            throw new InvalidDataException("Cold logical camera matrix disagrees with its retained source pose/frustum.");
    }
}
