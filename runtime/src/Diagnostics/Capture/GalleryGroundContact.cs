using Godot;


using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Diagnostics.Capture;

internal static class GalleryGroundContact
{
    internal static Alignment Align(
        PhysicsDirectSpaceState3D space,
        CellActorLoader.PlacedActor actor,
        Aabb visualBounds,
        RuntimeConfiguration configuration,
        uint collisionMask,
        Vector3 cellOriginWorld)
    {
        var before = Measure(
            space,
            actor,
            visualBounds,
            configuration,
            collisionMask);
        if (!before.GroundFound)
        {
            var cellSupport = ResolveCellOriginSupport(
                space,
                actor.Placement.GlobalPosition,
                cellOriginWorld,
                configuration,
                collisionMask);
            if (cellSupport is Support support)
                before = Measure(
                    space,
                    actor,
                    visualBounds,
                    configuration,
                    collisionMask,
                    support);
        }
        if (!before.GroundFound || before.DeltaMeters is not float correctionMeters ||
            before.DeltaGameUnits is not float correctionGameUnits)
            throw new InvalidOperationException(
                "Actor has no authored collision support for floor alignment.");
        if (!float.IsFinite(correctionMeters) ||
            MathF.Abs(correctionMeters) > visualBounds.Size.Y)
            throw new InvalidOperationException(
                "Actor floor alignment exceeds its posed visual height.");
        var rootBefore = actor.Placement.GlobalPosition;
        actor.Placement.GlobalPosition = rootBefore - Vector3.Up * correctionMeters;
        return new Alignment(
            rootBefore,
            actor.Placement.GlobalPosition,
            correctionMeters,
            correctionGameUnits,
            before.GroundPosition,
            before.ColliderPath,
            before.Derivation,
            new Support(
                before.GroundPosition,
                before.ColliderPath,
                before.RayDirection,
                before.Derivation));
    }

    internal static Measurement Measure(
        PhysicsDirectSpaceState3D space,
        CellActorLoader.PlacedActor actor,
        Aabb visualBounds,
        RuntimeConfiguration configuration,
        uint collisionMask,
        Support? retainedSupport = null)
    {
        var root = actor.Placement.GlobalPosition;
        var visualSupportY = visualBounds.Position.Y;
        var searchRange = visualBounds.Size.Y;
        var configuredToleranceGameUnits =
            configuration.ActorParity.PlacementToleranceGameUnits;
        if (!root.IsFinite() ||
            !visualBounds.Position.IsFinite() ||
            !visualBounds.Size.IsFinite() ||
            !float.IsFinite(searchRange) ||
            !float.IsFinite(visualSupportY) || searchRange <= 0.0f)
            throw new InvalidOperationException("Actor has no finite ground-contact search range.");
        var rayStartClearance = configuration.Player.CameraNearMeters;
        var candidates = new[]
            {
                new Vector3(root.X, visualSupportY, root.Z),
                new Vector3(
                    visualBounds.GetCenter().X,
                    visualSupportY,
                    visualBounds.GetCenter().Z),
                new Vector3(
                    visualBounds.Position.X,
                    visualSupportY,
                    visualBounds.Position.Z),
                new Vector3(
                    visualBounds.Position.X,
                    visualSupportY,
                    visualBounds.End.Z),
                new Vector3(
                    visualBounds.End.X,
                    visualSupportY,
                    visualBounds.Position.Z),
                new Vector3(
                    visualBounds.End.X,
                    visualSupportY,
                    visualBounds.End.Z),
            }
            .Distinct()
            .SelectMany(probe => new[]
            {
                Cast(
                    space,
                    probe + Vector3.Up * rayStartClearance,
                    probe - Vector3.Up * searchRange,
                    collisionMask,
                    configuration.Proof.WalkableSurfaceNormalYMinimum,
                    "down",
                    probe),
                Cast(
                    space,
                    probe - Vector3.Up * rayStartClearance,
                    probe + Vector3.Up * searchRange,
                    collisionMask,
                    configuration.Proof.WalkableSurfaceNormalYMinimum,
                    "up",
                    probe),
                Cast(
                    space,
                    new Vector3(probe.X, root.Y + rayStartClearance, probe.Z),
                    new Vector3(probe.X, root.Y - searchRange, probe.Z),
                    collisionMask,
                    configuration.Proof.WalkableSurfaceNormalYMinimum,
                    "retail-root-down",
                    probe),
                Cast(
                    space,
                    new Vector3(probe.X, root.Y - rayStartClearance, probe.Z),
                    new Vector3(probe.X, root.Y + searchRange, probe.Z),
                    collisionMask,
                    configuration.Proof.WalkableSurfaceNormalYMinimum,
                    "retail-root-up",
                    probe),
            })
            .Where(hit => hit.Hit)
            .OrderBy(hit => MathF.Abs(visualSupportY - hit.Position.Y))
            .ToArray();
        if (candidates.Length < 1)
        {
            if (retainedSupport is Support support)
                return BuildMeasurement(
                    root,
                    visualBounds,
                    visualSupportY,
                    support,
                    configuration,
                    configuredToleranceGameUnits);
            GD.Print(
                "OPENNV_GALLERY_GROUND_NO_HIT " +
                $"root={root} bounds={visualBounds} searchRange={searchRange} " +
                $"mask={collisionMask}");
            return new Measurement(
                false,
                false,
                root,
                Vector3.Zero,
                null,
                null,
                configuredToleranceGameUnits,
                configuredToleranceGameUnits *
                    configuration.World.GameUnitsToMeters,
                configuredToleranceGameUnits,
                0.0f,
                configuration.ActorParity.GroundContactMaximumUlp,
                visualBounds.Position.Y,
                visualBounds.End.Y,
                Vector3.Zero,
                "",
                "",
                "nearest-authored-collision-to-current-visual-support-plane-with-float-ulp-bound");
        }
        var ground = candidates[0];
        return BuildMeasurement(
            root,
            visualBounds,
            visualSupportY,
            new Support(
                ground.Position,
                ground.ColliderPath,
                ground.Direction,
                "nearest-authored-collision-to-current-visual-support-plane-with-float-ulp-bound"),
            configuration,
            configuredToleranceGameUnits);
    }

    private static Measurement BuildMeasurement(
        Vector3 root,
        Aabb visualBounds,
        float visualSupportY,
        Support support,
        RuntimeConfiguration configuration,
        float configuredToleranceGameUnits)
    {
        var ground = support.Position;
        var deltaMeters = visualSupportY - ground.Y;
        var deltaGameUnits = deltaMeters / configuration.World.GameUnitsToMeters;
        var numericPrecisionToleranceMeters = MathF.Max(
            Ulp(visualSupportY),
            Ulp(ground.Y)) * configuration.ActorParity.GroundContactMaximumUlp;
        var numericPrecisionToleranceGameUnits =
            numericPrecisionToleranceMeters / configuration.World.GameUnitsToMeters;
        var toleranceGameUnits = MathF.Max(
            configuredToleranceGameUnits,
            numericPrecisionToleranceGameUnits);
        var toleranceMeters = toleranceGameUnits * configuration.World.GameUnitsToMeters;
        var passed = MathF.Abs(deltaMeters) <= toleranceMeters;
        return new Measurement(
            true,
            passed,
            root,
            ground,
            deltaMeters,
            deltaGameUnits,
            toleranceGameUnits,
            toleranceMeters,
            configuredToleranceGameUnits,
            numericPrecisionToleranceGameUnits,
            configuration.ActorParity.GroundContactMaximumUlp,
            visualBounds.Position.Y,
            visualBounds.End.Y,
            support.Position,
            support.ColliderPath,
            support.Direction,
            support.Derivation);
    }

    private static Support? ResolveCellOriginSupport(
        PhysicsDirectSpaceState3D space,
        Vector3 actorRoot,
        Vector3 cellOriginWorld,
        RuntimeConfiguration configuration,
        uint collisionMask)
    {
        var probe = new Vector3(cellOriginWorld.X, actorRoot.Y, cellOriginWorld.Z);
        var hit = Cast(
            space,
            probe + Vector3.Up * configuration.Proof.SpawnFloorRayStartMeters,
            probe + Vector3.Up * configuration.Proof.SpawnFloorRayEndMeters,
            collisionMask,
            configuration.Proof.WalkableSurfaceNormalYMinimum,
            "authored-cell-origin-down",
            probe);
        if (!hit.Hit)
            return null;
        GD.Print(
            "OPENNV_GALLERY_GROUND_CELL_SUPPORT " +
            $"actorRoot={actorRoot} probe={probe} hit={hit.Position} " +
            $"collider={hit.ColliderPath}");
        return new Support(
            hit.Position,
            hit.ColliderPath,
            hit.Direction,
            "current-posed-owned-vertex-support-aligned-to-authored-cell-origin-floor-collision");
    }

    private static float Ulp(float value)
    {
        var magnitude = MathF.Abs(value);
        return MathF.BitIncrement(magnitude) - magnitude;
    }

    private static ContactRayHit Cast(
        PhysicsDirectSpaceState3D space,
        Vector3 from,
        Vector3 to,
        uint collisionMask,
        float walkableSurfaceNormalYMinimum,
        string direction,
        Vector3 probePosition)
    {
        var query = PhysicsRayQueryParameters3D.Create(from, to, collisionMask);
        query.HitFromInside = true;
        var excluded = new Godot.Collections.Array<Rid>();
        while (true)
        {
            query.Exclude = excluded;
            var result = space.IntersectRay(query);
            if (result.Count == 0)
                return new ContactRayHit(
                    false,
                    Vector3.Zero,
                    Vector3.Zero,
                    "",
                    direction,
                    probePosition);
            var collider = result["collider"].AsGodotObject() as Node;
            var normal = result["normal"].AsVector3();
            if (IsWalkableWorldSupport(collider, normal, walkableSurfaceNormalYMinimum))
                return new ContactRayHit(
                    true,
                    result["position"].AsVector3(),
                    normal,
                    collider!.GetPath().ToString(),
                    direction,
                    probePosition);
            if (collider is not CollisionObject3D rejected)
                return new ContactRayHit(
                    false,
                    Vector3.Zero,
                    Vector3.Zero,
                    "",
                    direction,
                    probePosition);
            excluded.Add(rejected.GetRid());
        }
    }

    internal static bool IsWalkableWorldSupport(
        Node? collider,
        Vector3 normal,
        float walkableSurfaceNormalYMinimum) =>
        collider is StaticBody3D &&
        normal.IsFinite() &&
        normal.Y >= walkableSurfaceNormalYMinimum;

    private readonly record struct ContactRayHit(
        bool Hit,
        Vector3 Position,
        Vector3 Normal,
        string ColliderPath,
        string Direction,
        Vector3 ProbePosition);

    internal readonly record struct Support(
        Vector3 Position,
        string ColliderPath,
        string Direction,
        string Derivation);

    internal readonly record struct Measurement(
        bool GroundFound,
        bool Passed,
        Vector3 ActorRootPosition,
        Vector3 GroundPosition,
        float? DeltaMeters,
        float? DeltaGameUnits,
        float ToleranceGameUnits,
        float ToleranceMeters,
        float ConfiguredToleranceGameUnits,
        float NumericPrecisionToleranceGameUnits,
        int GroundContactMaximumUlp,
        float VisualBoundsMinimumY,
        float VisualBoundsMaximumY,
        Vector3 ProbePosition,
        string ColliderPath,
        string RayDirection,
        string Derivation);

    internal readonly record struct Alignment(
        Vector3 RootBefore,
        Vector3 RootAfter,
        float CorrectionMeters,
        float CorrectionGameUnits,
        Vector3 GroundPosition,
        string ColliderPath,
        string Derivation,
        Support Support);
}
