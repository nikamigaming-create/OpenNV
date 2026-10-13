using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal enum FalloutWorldFovIngress { WorldConstructor, PlayerConstructor, PlayerFov, ConsoleFov, WorldRefresh }
internal enum FalloutWorldProjectionStep { Allocated, CameraSelected, Stored, Ratio, Frustum, Camera, Returned }
internal enum FalloutWorldProjectionConstructorStep { Allocated, OrdinaryCamera, ForcedFov, AttachedCamera, CameraUpdate, Bounds, Returned, SuppliedCamera }
internal sealed record FalloutWorldProjectionCall(Guid Invocation, string Producer, FalloutWorldFovIngress Ingress,
    long Entered, long Changed, uint RequestedBits, bool Force, bool SuppliedCamera, bool UpdateRatio,
    uint? BeforeBits, bool Stored, Guid Camera, FalloutWorldProjectionStep Step, Guid? Consumers,
    bool Returned, string? Failure);
internal sealed record FalloutWorldProjectionSnapshot(string Schema, FalloutPlayerWorldOrientationSource Source, string Stack,
    Guid CapturedWorld, Guid Factory, long Sequence, uint? CurrentFovBits, long Stores,
    uint? CameraRatioBits, bool OrdinaryConstructor, bool ConstructorEntered, bool ConstructorReturned,
    FalloutIniValue? ConstructorSetting, FalloutWorldProjectionCall? Call, string? Failure, bool Retired,
    FalloutWorldProjectionConstructorStep ConstructorStep, FalloutWorldProjectionCameraSnapshot Camera,
    uint SceneFlags, FalloutWorldProjectionBounds? Bounds, FalloutWorldProjectionCullingSnapshot Culling,
    FalloutWorldProjectionInputs? ConstructorInputs);

// These services belong to the source projection/camera path. Presentation
// may borrow their results; a native Camera3D is not a service producer.
internal interface IFalloutWorldProjectionConsumers
{
    FalloutPlayerWorldOrientationSource Source { get; }
    string Stack { get; }
    Guid World { get; }
    Guid Factory { get; }
    void RequireCurrent();
    void BuildFrustum(FalloutWorldProjectionCall call, FalloutWorldProjectionCamera camera);
    void UpdateCamera(FalloutWorldProjectionCall call, FalloutWorldProjectionCamera camera);
}

internal sealed partial class FalloutWorldProjectionCamera
{
    private readonly FalloutWorldProjectionOwner _root;
    internal FalloutWorldProjectionCamera(FalloutWorldProjectionOwner root, bool construct = true)
    {
        _root = root;
        // Ordinary NiCamera construction publishes its local/world identity,
        // default frustum and actual logical matrix before the World overrides.
        if (construct) UpdateOrdinary();
    }
    internal Guid Factory { get; } = Guid.NewGuid();
    internal uint? RatioBits { get; set; } = 0x3f800000;
    internal FalloutWorldProjectionSceneChild? Parent { get; set; }
    internal FalloutWorldProjectionOwner Root => _root;
    internal void RequireCurrent() => _root.RequireCurrent();
}

// Actual C# child owners are allocated before the constructor's FOV store.
// Their identity does not certify unimplemented projection or draw work.
internal sealed class FalloutWorldProjectionSceneChild(FalloutWorldProjectionOwner root)
{
    private FalloutWorldProjectionCamera? _camera;
    internal Guid Factory { get; } = Guid.NewGuid();
    internal FalloutWorldProjectionOwner Root { get; } = root;
    internal FalloutWorldProjectionPose WorldPose { get; } = FalloutWorldProjectionPose.Identity();
    internal FalloutWorldProjectionBounds? Bounds { get; private set; }
    internal FalloutWorldProjectionCamera Camera => _camera ?? throw new InvalidOperationException("World scene has no actual attached camera child.");
    internal void AttachCamera(FalloutWorldProjectionCamera camera)
    {
        if (_camera is not null || camera.Parent is not null || !ReferenceEquals(camera.Root, Root))
            throw new InvalidOperationException("World scene/camera child ownership changed.");
        _camera = camera; camera.Parent = this;
    }
    internal void RequireCurrent() => Root.RequireCurrent();
    internal void UpdateBoundsFromChildren()
    {
        // The constructor scene contains only its real camera child. That child
        // owns radius zero, so the source bound scan has no eligible sphere.
        if (_camera?.Capture().Bounds.Radius is not (null or 0))
            throw new NotSupportedException("source-current-scene-nonzero-child-bound-unowned");
        Bounds = null;
    }
}
