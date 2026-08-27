using Godot;

namespace OpenNV.Runtime;

internal static class GalleryFraming
{
    private const float FrustumCenterDivisor = 2.0f;

    internal static Frame Apply(
        Camera3D camera,
        CellActorLoader.PlacedActor actor,
        Aabb bounds,
        RuntimeConfiguration configuration,
        uint collisionMask,
        GalleryRetailEvidence.PresentationReference presentation,
        Vector3 matchedRetailActorRootWorld)
    {
        var capture = configuration.Capture;
        var policy = capture.Gallery;
        if (!bounds.Position.IsFinite() || !bounds.Size.IsFinite() ||
            bounds.Size.X <= 0.0f || bounds.Size.Y <= 0.0f || bounds.Size.Z <= 0.0f)
            throw new InvalidOperationException(
                $"Gallery actor has invalid bounds: position={bounds.Position} size={bounds.Size}");

        var pose = actor.Actor.PoseContract.Resolve();
        var target = pose.HeadWorldPosition;
        var viewportAspect = (float)capture.ExpectedWidthPixels /
            capture.ExpectedHeightPixels;
        var near = presentation.Frustum.Near * configuration.World.GameUnitsToMeters;
        var far = presentation.Frustum.Far * configuration.World.GameUnitsToMeters;
        var frustumSize =
            (presentation.Frustum.Top - presentation.Frustum.Bottom) * near;
        var frustumOffset = new Vector2(
            (presentation.Frustum.Left + presentation.Frustum.Right) * near /
                FrustumCenterDivisor,
            (presentation.Frustum.Top + presentation.Frustum.Bottom) * near /
                FrustumCenterDivisor);
        var matchedRetailCameraPosition =
            matchedRetailActorRootWorld +
            GamebryoCoordinate.ConvertVector(presentation.CameraOffsetGameUnits) *
            configuration.World.GameUnitsToMeters;
        // Camera and actor-root transforms come from the same retail frame. A
        // second animation-root orbit double-applies posed facing for rigs whose
        // non-accumulation root carries the model's authored half-turn.
        var poseAdjustedCameraPosition = matchedRetailCameraPosition;
        camera.GlobalTransform = new Transform3D(
            presentation.CameraBasis,
            poseAdjustedCameraPosition);
        camera.SetFrustum(frustumSize, frustumOffset, near, far);

        var space = camera.GetWorld3D().DirectSpaceState;
        var visualOcclusion = GalleryVisualOcclusionIndex.Build(
            camera.GetTree().CurrentScene,
            actor.Placement);
        var initialObstruction = CastVisibilityRays(
            space,
            visualOcclusion,
            camera.GlobalPosition,
            target,
            near,
            collisionMask);
        var occlusionResolved = false;
        if (initialObstruction.Hit)
        {
            var semanticFaceCenter = ActorModelSlice.PosedSemanticCenter(
                actor.Actor,
                "eye-left",
                "eye-right");
            var semanticFaceDirection = semanticFaceCenter is Vector3 faceCenter
                ? HorizontalDirection(faceCenter - target)
                : null;
            foreach (var candidate in AlternativeCameraPositions(
                         poseAdjustedCameraPosition,
                         target,
                         bounds,
                         near,
                         semanticFaceDirection))
            {
                var candidateObstruction = CastVisibilityRays(
                    space,
                    visualOcclusion,
                    candidate,
                    target,
                    near,
                    collisionMask);
                if (candidateObstruction.Hit)
                    continue;
                camera.GlobalPosition = candidate;
                camera.LookAt(target, Vector3.Up);
                occlusionResolved = true;
                break;
            }
        }

        var viewportSize = camera.GetViewport().GetVisibleRect().Size;
        var projectedHead = camera.UnprojectPosition(target);
        var headInViewport = IsInViewport(camera, target, projectedHead, viewportSize);
        var aimAdjusted = false;
        if (!headInViewport)
        {
            camera.LookAt(target, camera.GlobalBasis.Y.Normalized());
            projectedHead = camera.UnprojectPosition(target);
            headInViewport = IsInViewport(camera, target, projectedHead, viewportSize);
            aimAdjusted = true;
        }
        if (!headInViewport)
            throw new InvalidOperationException(
                $"Owned gallery head target is outside the viewport: {projectedHead} of {viewportSize}.");
        var cameraPosition = camera.GlobalPosition;
        var finalObstruction = CastVisibilityRays(
            space,
            visualOcclusion,
            cameraPosition,
            target,
            near,
            collisionMask);
        var front = -camera.GlobalBasis.Z.Normalized();
        var right = camera.GlobalBasis.X.Normalized();
        var projectedWidth = ProjectedSpan(bounds.Size, right);
        var projectedDepth = ProjectedSpan(bounds.Size, front);
        var cameraDistance = target.DistanceTo(cameraPosition);
        if (!float.IsFinite(cameraDistance) || cameraDistance <= 0.0f)
            throw new InvalidOperationException(
                $"Retail gallery camera produced an invalid distance: {cameraDistance}");
        return new Frame(
            bounds,
            target,
            matchedRetailCameraPosition,
            poseAdjustedCameraPosition,
            cameraPosition,
            front,
            right,
            pose.FacingRotation,
            0.0f,
            projectedWidth,
            projectedDepth,
            target.DistanceTo(matchedRetailCameraPosition),
            cameraDistance,
            viewportAspect,
            Mathf.RadToDeg(presentation.FovYRadians),
            policy.MaximumFrameOccupancy,
            policy.ModelFrontAxis,
            policy.TargetNodeRole,
            policy.FacingPoseSource,
            actor.Actor.PoseContract.FacingSource,
            actor.Actor.PoseContract.FacingNode,
            actor.Actor.PoseContract.FacingNodeIndex,
            actor.Actor.PoseContract.HeadSource,
            actor.Actor.PoseContract.HeadNode,
            actor.Actor.PoseContract.HeadNodeIndex,
            policy.OcclusionClearanceSource,
            near,
            occlusionResolved,
            initialObstruction.ColliderPath,
            initialObstruction.Position,
            projectedHead,
            viewportSize,
            headInViewport,
            !finalObstruction.Hit,
            aimAdjusted,
            occlusionResolved
                ? "owned-pose-facing-with-collision-derived-clear-orbit"
                : aimAdjusted
                ? "current-owned-head-retarget-from-matched-retail-camera-position-and-projection"
                : presentation.Derivation,
            presentation.ShotKind,
            presentation.Frame,
            presentation.CameraEventSha256,
            presentation.ActorSnapshotEventSha256,
            presentation.ActorPoseEventSha256);
    }

    private static IEnumerable<Vector3> AlternativeCameraPositions(
        Vector3 cameraPosition,
        Vector3 target,
        Aabb bounds,
        float cameraNear,
        Vector3? semanticFaceDirection)
    {
        var offset = cameraPosition - target;
        var vertical = new Vector3(0.0f, offset.Y, 0.0f);
        var horizontal = new Vector3(offset.X, 0.0f, offset.Z);
        if (horizontal.IsZeroApprox())
            yield break;
        var actorHorizontalRadius =
            new Vector2(bounds.Size.X, bounds.Size.Z).Length() / 2.0f;
        var initialRadius = horizontal.Length();
        var minimumRadius = actorHorizontalRadius + cameraNear;
        var radialSamples = Math.Max(
            1,
            Mathf.CeilToInt(
                (initialRadius - minimumRadius) / actorHorizontalRadius));
        var angularDiameter = 2.0f * MathF.Atan2(
            actorHorizontalRadius,
            initialRadius);
        var orbitSamples = Math.Max(
            1,
            Mathf.CeilToInt(Mathf.Tau / angularDiameter));
        var orbitStep = Mathf.Tau / orbitSamples;
        for (var radialSample = 0;
             radialSample <= radialSamples;
             radialSample++)
        {
            for (var angularSample = 0;
                 angularSample < orbitSamples;
                 angularSample++)
            {
                if (radialSample == 0 && angularSample == 0)
                    continue;
                foreach (var direction in angularSample == 0
                             ? new[] { 1 }
                             : new[] { 1, -1 })
                {
                    var yaw = direction * angularSample * orbitStep;
                    var angularDirection =
                        new Basis(Vector3.Up, yaw) * horizontal.Normalized();
                    if (semanticFaceDirection is Vector3 faceDirection &&
                        angularDirection.Dot(faceDirection) <= 0.0f)
                        continue;
                    var radius = Math.Max(
                        minimumRadius,
                        initialRadius - radialSample * actorHorizontalRadius);
                    var scale = radius / initialRadius;
                    yield return target + vertical * scale +
                        angularDirection * radius;
                }
            }
        }
    }

    private static Vector3? HorizontalDirection(Vector3 value)
    {
        value.Y = 0.0f;
        return value.IsZeroApprox() ? null : value.Normalized();
    }

    private static RayHit CastVisibilityRays(
        PhysicsDirectSpaceState3D space,
        GalleryVisualOcclusionIndex visualOcclusion,
        Vector3 cameraPosition,
        Vector3 semanticTarget,
        float occlusionClearance,
        uint collisionMask)
    {
        var forward = (semanticTarget - cameraPosition).Normalized();
        var right = Vector3.Up.Cross(forward).Normalized();
        var up = forward.Cross(right).Normalized();
        foreach (var target in new[]
                 {
                     semanticTarget,
                     semanticTarget + right * occlusionClearance,
                     semanticTarget - right * occlusionClearance,
                     semanticTarget + up * occlusionClearance,
                     semanticTarget - up * occlusionClearance,
                 })
        {
            var hit = CastRay(space, target, cameraPosition, collisionMask);
            if (hit.Hit)
                return hit;
            var visualHit = visualOcclusion.CastSegment(target, cameraPosition);
            if (visualHit.HitSurface)
                return new RayHit(
                    true,
                    visualHit.Position,
                    visualHit.SurfacePath);
        }
        return new RayHit(false, Vector3.Zero, "");
    }

    private static bool IsInViewport(
        Camera3D camera,
        Vector3 target,
        Vector2 projected,
        Vector2 viewportSize) =>
        !camera.IsPositionBehind(target) &&
        projected.X >= 0.0f &&
        projected.Y >= 0.0f &&
        projected.X <= viewportSize.X &&
        projected.Y <= viewportSize.Y;

    private static RayHit CastRay(
        PhysicsDirectSpaceState3D space,
        Vector3 from,
        Vector3 to,
        uint collisionMask)
    {
        var query = PhysicsRayQueryParameters3D.Create(from, to, collisionMask);
        query.HitFromInside = true;
        var result = space.IntersectRay(query);
        if (result.Count == 0)
            return new RayHit(false, Vector3.Zero, "");
        var collider = result["collider"].AsGodotObject() as Node;
        return new RayHit(
            true,
            result["position"].AsVector3(),
            collider?.GetPath().ToString() ?? "unknown");
    }

    private static float ProjectedSpan(Vector3 size, Vector3 direction) =>
        MathF.Abs(direction.X) * size.X +
        MathF.Abs(direction.Y) * size.Y +
        MathF.Abs(direction.Z) * size.Z;

    private readonly record struct RayHit(
        bool Hit,
        Vector3 Position,
        string ColliderPath);

    internal readonly record struct Frame(
        Aabb Bounds,
        Vector3 Target,
        Vector3 DesiredCameraPosition,
        Vector3 PoseAdjustedCameraPosition,
        Vector3 CameraPosition,
        Vector3 Front,
        Vector3 Right,
        Quaternion FacingPoseRotation,
        float FacingPoseCorrectionRadians,
        float ProjectedWidthMeters,
        float ProjectedDepthMeters,
        float DesiredCameraDistanceMeters,
        float CameraDistanceMeters,
        float ViewportAspect,
        float VerticalFovDegrees,
        float MaximumFrameOccupancy,
        string ModelFrontAxis,
        string TargetNodeRole,
        string FacingPoseSource,
        string FacingDerivation,
        string? FacingNode,
        int? FacingNodeIndex,
        string HeadDerivation,
        string HeadNode,
        int HeadNodeIndex,
        string OcclusionClearanceSource,
        float OcclusionClearanceMeters,
        bool OcclusionResolved,
        string OccludingColliderPath,
        Vector3 OcclusionHitPosition,
        Vector2 ProjectedHeadPixels,
        Vector2 ViewportSizePixels,
        bool HeadInViewport,
        bool HeadVisibilityClear,
        bool AimAdjusted,
        string Derivation,
        string RetailShotKind,
        int RetailFrame,
        string CameraEventSha256,
        string ActorSnapshotEventSha256,
        string ActorPoseEventSha256);
}
