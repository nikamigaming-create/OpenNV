using Godot;
using V3 = System.Numerics.Vector3;

namespace OpenNV.Runtime.World.Cells;

internal static class RuntimeNativeRoomCamera
{
    internal static IReadOnlyList<FalloutRoomCamera> Observe(Viewport viewport)
    {
        if (!GodotObject.IsInstanceValid(viewport) || !viewport.IsInsideTree() || viewport.GetCamera3D() is not { } camera ||
            !GodotObject.IsInstanceValid(camera) || !camera.IsInsideTree() || camera.IsQueuedForDeletion() ||
            !float.IsFinite(camera.Near) || !float.IsFinite(camera.Far) || camera.Near <= 0 || camera.Far <= camera.Near)
            throw new NotSupportedException("Room graph has no actual current native camera/viewport publication.");
        if (camera is XRCamera3D xrCamera)
        {
            // This is the borrowed application interface. The XR owner, not
            // a per-draw camera observer, owns its managed/native retirement.
            var xr = XRServer.PrimaryInterface;
            if (xr is null || !xr.IsInitialized() || !viewport.UseXR)
                throw new NotSupportedException("Portal visibility has no active original XR view producer.");
            var origin = camera.GetParent() as XROrigin3D ??
                throw new NotSupportedException("Portal XR camera has no actual tracking-origin lifetime.");
            if (!origin.IsInsideTree()) throw new InvalidOperationException("Portal XR origin is detached.");
            var count = xr.GetViewCount();
            if (count is not (1 or 2))
                throw new NotSupportedException("Portal XR view topology has no admitted native visibility consumer.");
            var size = xr.GetRenderTargetSize();
            if (size.X <= 0 || size.Y <= 0)
                throw new NotSupportedException("Portal XR projection has no current source render-target extent.");
            // Read the same tracker projections/eye offsets published by the
            // 4.8 XRCamera3D. Offsets are relative to the tracked camera, whose
            // transform already includes the actual head pose and XR origin.
            var offsets = XRServer.GetCameraOffsets(xrCamera.Tracker);
            var projections = XRServer.GetCameraProjections(xrCamera.Tracker,
                (double)size.X / size.Y, camera.Near, camera.Far);
            if (offsets.Count != count || projections.Count != count)
                throw new NotSupportedException("Portal XR tracker has no complete current per-view offsets/projections.");
            var cameraTransform = camera.GetCameraTransform();
            var result = new List<FalloutRoomCamera>();
            for (var view = 0; view < offsets.Count; view++)
            {
                result.Add(Observe(cameraTransform * offsets[view], camera.Near, projections[view]));
            }
            return result.AsReadOnly();
        }
        if (viewport.UseXR)
            throw new NotSupportedException("XR viewport's active camera has no genuine per-view source projection.");
        var actual = camera.GetCameraTransform();
        var frustum = camera.GetFrustum();
        var planes = frustum.Select(Inward).ToArray();
        return [Create(actual, camera.Near, planes)];
    }

    private static FalloutRoomCamera Observe(Transform3D transform, float near, Projection projection)
    {
        var planes = new List<FalloutVisibilityPlane>();
        foreach (var kind in new[] { Projection.Planes.Near, Projection.Planes.Far, Projection.Planes.Left,
            Projection.Planes.Top, Projection.Planes.Right, Projection.Planes.Bottom })
        {
            var local = projection.GetProjectionPlane(kind);
            var normal = transform.Basis.Inverse().Transposed() * local.Normal;
            var point = transform * (local.Normal * (local.D / local.Normal.LengthSquared()));
            var world = new Plane(normal, normal.Dot(point));
            planes.Add(Inward(world));
        }
        return Create(transform, near, planes.AsReadOnly());
    }

    private static FalloutVisibilityPlane Inward(Plane plane)
    {
        // Godot's native camera planes point out of the visible volume; C#
        // compound windows retain inward halfspaces. No heuristic probe point
        // chooses the sign or changes a near/far boundary.
        var result = new FalloutVisibilityPlane(-N(plane.Normal), -plane.D);
        result.Validate(); return result;
    }

    private static FalloutRoomCamera Create(Transform3D transform, float near, IReadOnlyList<FalloutVisibilityPlane> planes)
    {
        if (!transform.Origin.IsFinite() || !transform.Basis.X.IsFinite() || !transform.Basis.Y.IsFinite() ||
            !transform.Basis.Z.IsFinite() || transform.Basis.Determinant() == 0 || planes.Count != 6)
            throw new InvalidDataException("Actual portal camera transform/frustum is invalid.");
        return new(N(transform.Origin), N(transform.Origin - transform.Basis.Z.Normalized() * near), planes);
    }
    private static V3 N(Vector3 value) => new(value.X, value.Y, value.Z);
}
