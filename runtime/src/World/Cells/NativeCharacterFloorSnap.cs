using Godot;

namespace OpenNV.Runtime.World.Cells;

// A read-only prediction of the controller's floor-snap query after a clear
// motion. It does not model prior sliding, moving platforms or root support.
internal readonly record struct NativeFloorSnap(bool? OnFloor, Vector3 Position);

internal static class NativeCharacterFloorSnap
{
    internal static NativeFloorSnap Query(CharacterBody3D body, Vector3 position)
    {
        if (!GodotObject.IsInstanceValid(body) || !body.IsInsideTree() || body.IsQueuedForDeletion() || !position.IsFinite())
            throw new InvalidOperationException("Floor snap requires a finite pose and a resident native body.");
        var up = body.UpDirection;
        if (!up.IsFinite() || !up.IsNormalized() || !float.IsFinite(body.FloorSnapLength) ||
            !float.IsFinite(body.SafeMargin) || body.FloorSnapLength < 0 || body.SafeMargin < 0)
            throw new InvalidDataException("Floor snap has invalid native controller settings.");
        using var parameters = new PhysicsTestMotionParameters3D
        {
            From = new(body.GlobalBasis, position),
            Motion = -up * Math.Max(body.FloorSnapLength, body.SafeMargin),
            Margin = body.SafeMargin,
            MaxCollisions = 4,
            RecoveryAsCollision = true,
            CollideSeparationRay = true
        };
        using var result = new PhysicsTestMotionResult3D();
        if (!PhysicsServer3D.BodyTestMotion(body.GetRid(), parameters, result)) return new(false, position);
        var normals = Enumerable.Range(0, result.GetCollisionCount()).Select(result.GetCollisionNormal).ToArray();
        var floor = Classify(normals, up, body.FloorMaxAngle);
        if (floor is null) return new(null, position);
        if (floor == false) return new(false, position);
        var travel = result.GetTravel();
        if (!travel.IsFinite()) throw new InvalidDataException("Floor snap returned nonfinite travel.");
        return new(true, position + (travel.Length() > body.SafeMargin ? up * travel.Dot(up) : Vector3.Zero));
    }

    internal static bool? Classify(IReadOnlyList<Vector3> normals, Vector3 up, float maximumAngle)
    {
        if (normals.Count == 0 || normals.Any(normal => !normal.IsFinite()) || !up.IsFinite() || !up.IsNormalized() ||
            !float.IsFinite(maximumAngle) || maximumAngle < 0 || maximumAngle > MathF.PI / 2)
            return null;
        // Classify floor and ceiling separately; multiple wall contacts can
        // jointly form support even when no one normal is walkable.
        var cosine = MathF.Cos(maximumAngle + .01f);
        var floor = normals.Any(normal => normal.Dot(up) >= cosine);
        if (!floor)
        {
            var walls = new List<Vector3>();
            foreach (var normal in normals.Reverse())
            {
                if (normal.Dot(-up) >= cosine) continue;
                if (walls.Count == 0 || !walls[^1].IsEqualApprox(normal)) walls.Add(normal);
            }
            if (walls.Count > 1)
            {
                var combined = walls.Aggregate(Vector3.Zero, (sum, normal) => sum + normal);
                floor = combined.LengthSquared() > 0 && combined.Normalized().Dot(up) >= cosine;
            }
        }
        return floor;
    }
}
