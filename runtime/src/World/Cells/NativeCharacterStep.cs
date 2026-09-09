using Godot;

namespace OpenNV.Runtime.World.Cells;

// The optional OpenNV controller climbs a low obstruction only after sweeping
// its whole capsule up and forward toward a supported landing. These are the
// real physics shapes; a wall, ceiling or missing floor cannot be bypassed.
internal static class NativeCharacterStep
{
    internal static bool TryStep(CharacterBody3D body, Vector3 motion, float maximumHeight)
    {
        if (!body.IsOnFloor() || body.Velocity.Y > 0 || motion.LengthSquared() < 0.000001f) return false;
        var from = body.GlobalTransform;
        using var parameters = new PhysicsTestMotionParameters3D
        { From = from, Motion = motion, Margin = body.SafeMargin, MaxCollisions = 4 };
        using var result = new PhysicsTestMotionResult3D();
        var rid = body.GetRid();
        var floorCosine = MathF.Cos(body.FloorMaxAngle);
        if (!PhysicsServer3D.BodyTestMotion(rid, parameters, result)) return false;
        var wall = Enumerable.Range(0, result.GetCollisionCount())
            .Where(index => result.GetCollisionNormal(index).Dot(Vector3.Up) < floorCosine)
            .OrderBy(index => result.GetCollisionNormal(index).Dot(motion)).DefaultIfEmpty(-1).First();
        if (wall < 0) return false;
        // Probe immediately beyond the contacted riser, not under the rounded
        // capsule's trailing edge (which would reject every ordinary curb).
        var ahead = result.GetCollisionPoint(wall) + motion.Normalized() * body.SafeMargin * 4;
        ahead.Y = from.Origin.Y + maximumHeight + body.SafeMargin;
        using var ray = PhysicsRayQueryParameters3D.Create(ahead, ahead - Vector3.Up * maximumHeight,
            body.CollisionMask, [rid]);
        using var landing = body.GetWorld3D().DirectSpaceState.IntersectRay(ray);
        if (landing.Count == 0 || landing["normal"].AsVector3().Dot(Vector3.Up) < floorCosine) return false;
        var up = landing["position"].AsVector3().Y - from.Origin.Y + body.SafeMargin;
        if (up <= body.SafeMargin * 2 || up > maximumHeight + body.SafeMargin) return false;
        parameters.Motion = Vector3.Up * up;
        if (PhysicsServer3D.BodyTestMotion(rid, parameters, result)) return false;
        parameters.From = from.Translated(parameters.Motion);
        parameters.Motion = motion;
        if (PhysicsServer3D.BodyTestMotion(rid, parameters, result)) return false;
        body.GlobalPosition = parameters.From.Origin + motion;
        return true;
    }
}
