using OpenNV.Runtime.Content;
using F = OpenNV.Runtime.World.Physics.FalloutPhysicalFloat32;
using M = OpenNV.Runtime.Gameplay.State.FalloutWorldProjectionCameraMath;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutWorldProjectionTargetRead(string Source, string Stack, Guid World, Guid Owner,
    bool RendererPresent, uint? Width, uint? Height, string Producer);

// The selected adapter owns renderer presence and current target dimensions.
// An absent renderer takes the source's retained-viewport arm. The callback
// cannot read a rendered camera's FOV, pose or projection into logical state.
internal sealed class FalloutWorldProjectionConsumers : IFalloutWorldProjectionConsumers
{
    private readonly FalloutWorldProjectionOwner _root;
    private readonly FalloutWorldProjectionInputs _inputs;
    private readonly Func<FalloutWorldProjectionTargetRead> _readTarget;
    internal FalloutWorldProjectionConsumers(FalloutWorldProjectionOwner root, FalloutWorldProjectionInputs inputs,
        Func<FalloutWorldProjectionTargetRead> readCurrentTarget)
    {
        ArgumentNullException.ThrowIfNull(root); ArgumentNullException.ThrowIfNull(readCurrentTarget); inputs.Validate();
        if (inputs.Source != root.Source || inputs.Stack != root.Stack)
            throw new InvalidOperationException("Projection consumers lost their actual source configuration/root.");
        _root = root; _inputs = inputs; _readTarget = readCurrentTarget;
    }
    public FalloutPlayerWorldOrientationSource Source => _root.Source;
    public string Stack => _root.Stack;
    public Guid World => _root.World;
    public Guid Factory { get; } = Guid.NewGuid();
    public void RequireCurrent() => _root.RequireCurrent();
    public void BuildFrustum(FalloutWorldProjectionCall call, FalloutWorldProjectionCamera camera)
    {
        RequireCurrent(); camera.RequireCurrent();
        if (!ReferenceEquals(camera.Root, _root) || call.Camera != camera.Factory)
            throw new InvalidOperationException("Logical frustum selected another actual current source camera.");
        // During the base constructor its BSSceneGraph virtual table is current.
        // After return, SceneGraph selects an independent world/process clip
        // getter. That getter cannot be replaced by the constructor formula.
        if (_root.ConstructorReturned) throw new NotSupportedException("source-current-SceneGraph-world-process-far-distance-unowned");
        var far = _inputs.ReadBaseFarBits(); var near = M.Bits((float)_inputs.NearDistance.Number!.Value);
        camera.MaximumFarNearRatio = M.Bits(F.Divide(M.Value(far), M.Value(near)));
        var target = _readTarget(); RequireCurrent();
        if (target.Source != Source.Identity || target.Stack != Stack || target.World != World ||
            target.Owner == Guid.Empty || string.IsNullOrWhiteSpace(target.Producer) ||
            target.RendererPresent && (target.Width is null || target.Height is null) ||
            !target.RendererPresent && (target.Width is not null || target.Height is not null))
            throw new InvalidDataException("Source frustum lost the actual renderer-presence/current-target query.");
        var viewport = camera.Viewport;
        var width = F.Subtract(M.Value(viewport.Right), M.Value(viewport.Left));
        var height = F.Subtract(M.Value(viewport.Top), M.Value(viewport.Bottom));
        if (target.RendererPresent)
        {
            height = F.Multiply(height, (float)(double)target.Height!.Value);
            width = F.Multiply(width, (float)(double)target.Width!.Value);
        }
        var aspect = F.Divide(height, width);
        var scale = F.Divide(M.Value(0x3f400000), aspect);
        var halfRadians = F.Multiply(F.Multiply(M.Value(call.RequestedBits), M.Value(0x3c8efa35)), M.Value(0x3f000000));
        if (!float.IsFinite(halfRadians) || !float.IsFinite(aspect) || !float.IsFinite(scale))
            throw new NotSupportedException("source-camera-nonfinite-frustum-machine-floating-state-unowned");
        // Source tangent consumes the exact Double promotion of the stored
        // Float32 angle and stores Float32 before any bound multiplication.
        // Original libm rounding/exception flags are an unverified evidence lane.
        var tangent = (float)Math.Tan((double)halfRadians);
        camera.StoreFrustum(new(M.Bits(F.Multiply(M.Negate(tangent), scale)), M.Bits(F.Multiply(tangent, scale)),
            M.Bits(F.Multiply(F.Multiply(tangent, aspect), scale)),
            M.Bits(F.Multiply(F.Multiply(M.Negate(tangent), aspect), scale)), near, far, false));
    }
    public void UpdateCamera(FalloutWorldProjectionCall call, FalloutWorldProjectionCamera camera)
    {
        RequireCurrent(); camera.RequireCurrent();
        if (!ReferenceEquals(camera.Root, _root) || call.Camera != camera.Factory)
            throw new InvalidOperationException("Source camera update selected another actual logical child.");
        camera.UpdateOrdinary(); RequireCurrent();
    }
}
