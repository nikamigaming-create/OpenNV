using Godot;

namespace OpenNV.Runtime;

internal static class GalleryGroundContact
{
    internal static Measurement Measure(
        PhysicsDirectSpaceState3D space,
        CellActorLoader.PlacedActor actor,
        Aabb visualBounds,
        RuntimeConfiguration configuration,
        uint collisionMask)
    {
        var root = actor.Placement.GlobalPosition;
        var searchRange = MathF.Max(
            MathF.Abs(root.Y - visualBounds.Position.Y),
            MathF.Abs(root.Y - visualBounds.End.Y));
        var configuredToleranceGameUnits =
            configuration.ActorParity.PlacementToleranceGameUnits;
        if (!root.IsFinite() ||
            !visualBounds.Position.IsFinite() ||
            !visualBounds.Size.IsFinite() ||
            !float.IsFinite(searchRange) ||
            searchRange <= 0.0f)
            throw new InvalidOperationException("Gallery actor has no finite ground-contact search range.");
        var rayStartClearance = configuration.Player.CameraNearMeters;
        var candidates = new[]
            {
                root,
                new Vector3(visualBounds.GetCenter().X, root.Y, visualBounds.GetCenter().Z),
                new Vector3(visualBounds.Position.X, root.Y, visualBounds.Position.Z),
                new Vector3(visualBounds.Position.X, root.Y, visualBounds.End.Z),
                new Vector3(visualBounds.End.X, root.Y, visualBounds.Position.Z),
                new Vector3(visualBounds.End.X, root.Y, visualBounds.End.Z),
            }
            .Distinct()
            .SelectMany(probe => new[]
            {
                Cast(
                    space,
                    probe + Vector3.Up * rayStartClearance,
                    probe - Vector3.Up * searchRange,
                    collisionMask,
                    "down",
                    probe),
                Cast(
                    space,
                    probe - Vector3.Up * rayStartClearance,
                    probe + Vector3.Up * searchRange,
                    collisionMask,
                    "up",
                    probe),
            })
            .Where(hit => hit.Hit)
            .OrderBy(hit => MathF.Abs(root.Y - hit.Position.Y))
            .ToArray();
        if (candidates.Length < 1)
        {
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
                "nearest-authored-collision-within-owned-root-to-visual-bounds-extent-and-float-ulp-bound");
        }
        var ground = candidates[0];
        var deltaMeters = root.Y - ground.Position.Y;
        var deltaGameUnits = deltaMeters / configuration.World.GameUnitsToMeters;
        var numericPrecisionToleranceMeters = MathF.Max(
            Ulp(root.Y),
            Ulp(ground.Position.Y)) * configuration.ActorParity.GroundContactMaximumUlp;
        var numericPrecisionToleranceGameUnits =
            numericPrecisionToleranceMeters / configuration.World.GameUnitsToMeters;
        var toleranceGameUnits = MathF.Max(
            configuredToleranceGameUnits,
            numericPrecisionToleranceGameUnits);
        var toleranceMeters = toleranceGameUnits * configuration.World.GameUnitsToMeters;
        var passed = MathF.Abs(deltaMeters) <= searchRange + toleranceMeters;
        return new Measurement(
            true,
            passed,
            root,
            ground.Position,
            deltaMeters,
            deltaGameUnits,
            toleranceGameUnits,
            toleranceMeters,
            configuredToleranceGameUnits,
            numericPrecisionToleranceGameUnits,
            configuration.ActorParity.GroundContactMaximumUlp,
            visualBounds.Position.Y,
            visualBounds.End.Y,
            ground.ProbePosition,
            ground.ColliderPath,
            ground.Direction,
            "nearest-authored-collision-within-owned-root-to-visual-bounds-extent-and-float-ulp-bound");
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
        string direction,
        Vector3 probePosition)
    {
        var query = PhysicsRayQueryParameters3D.Create(from, to, collisionMask);
        query.HitFromInside = true;
        var result = space.IntersectRay(query);
        if (result.Count == 0)
            return new ContactRayHit(
                false,
                Vector3.Zero,
                "",
                direction,
                probePosition);
        var collider = result["collider"].AsGodotObject() as Node;
        return new ContactRayHit(
            true,
            result["position"].AsVector3(),
            collider?.GetPath().ToString() ?? "unknown",
            direction,
            probePosition);
    }

    private readonly record struct ContactRayHit(
        bool Hit,
        Vector3 Position,
        string ColliderPath,
        string Direction,
        Vector3 ProbePosition);

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
}
