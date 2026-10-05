using Godot;

namespace OpenNV.Runtime.World.Cells;

// The optional OpenNV controller climbs a low obstruction only after sweeping
// its whole capsule up and forward toward a supported landing. These are the
// real physics shapes; a wall, ceiling or missing floor cannot be bypassed.
internal static class NativeCharacterStep
{
    internal static bool TryStep(CharacterBody3D body, Vector3 motion, float maximumHeight)
        => TryStep(body, motion, maximumHeight, out _);

    internal static bool TryStep(CharacterBody3D body, Vector3 motion, float maximumHeight, out string? blocked)
    {
        blocked = "upward-or-stationary";
        if (body.Velocity.Y > 0 || motion.LengthSquared() < 0.000001f) return false;
        var from = body.GlobalTransform;
        if (!body.IsOnFloor())
        {
            blocked = "floor-support";
            // A rounded capsule can report only the riser for a frame while
            // climbing onto a curb. Require nearby floor support instead of
            // losing stepping until the capsule somehow reaches the top.
            if (!TrySupport(body, from.Origin, maximumHeight, out _)) return false;
        }
        if (!TryQuery(body, from, motion, maximumHeight, out var destination, out blocked, out _, floorSupported: true)) return false;
        body.GlobalPosition = destination;
        return true;
    }

    // Planning uses the controller's supported-height probes and complete
    // sweeps at a supplied pose. This query never publishes that pose.
    internal static bool TryQuery(CharacterBody3D body, Transform3D from, Vector3 motion, float maximumHeight,
        out Vector3 destination, out string? blocked, out NativeNavigationContact? contact, bool floorSupported = false)
    {
        destination = default; contact = null;
        var floorCosine = MathF.Cos(body.FloorMaxAngle);
        blocked = "floor-support";
        if (!floorSupported)
        {
            if (!TrySupport(body, from.Origin, maximumHeight, out _)) return false;
        }
        using var parameters = new PhysicsTestMotionParameters3D
        { From = from, Motion = motion, Margin = body.SafeMargin, MaxCollisions = 4 };
        using var result = new PhysicsTestMotionResult3D();
        var rid = body.GetRid();
        blocked = "no-obstruction";
        if (!PhysicsServer3D.BodyTestMotion(rid, parameters, result)) return false;
        var wall = Enumerable.Range(0, result.GetCollisionCount())
            .Where(index => result.GetCollisionNormal(index).Dot(Vector3.Up) < floorCosine)
            .OrderBy(index => result.GetCollisionNormal(index).Dot(motion)).DefaultIfEmpty(-1).First();
        blocked = "no-riser";
        if (wall < 0) return false;
        // A bevel can be just steeper than the walkable slope. Probe beyond
        // the contacted riser for its top, bounded by the step height; every
        // accepted move still sweeps the whole capsule up and forward.
        var point = result.GetCollisionPoint(wall);
        contact = new(result.GetColliderId(wall), result.GetColliderShape(wall), point,
            result.GetCollisionNormal(wall), from.Origin, from.Origin + motion, Travel: result.GetTravel());
        var forward = motion.Normalized();
        using var ray = PhysicsRayQueryParameters3D.Create(Vector3.Zero, Vector3.Zero, body.CollisionMask, [rid]);
        blocked = "landing-support";
        for (var probe = 0; probe < 4; probe++)
        {
            var reach = probe == 0 ? body.SafeMargin * 4 : maximumHeight / (1 << (3 - probe));
            var ahead = point + forward * reach;
            ahead.Y = from.Origin.Y + maximumHeight + body.SafeMargin;
            ray.From = ahead; ray.To = ahead - Vector3.Up * maximumHeight;
            using var landing = body.GetWorld3D().DirectSpaceState.IntersectRay(ray);
            if (landing.Count == 0 || landing["normal"].AsVector3().Dot(Vector3.Up) < floorCosine) continue;
            var up = landing["position"].AsVector3().Y - from.Origin.Y + body.SafeMargin;
            blocked = "step-height";
            if (up <= body.SafeMargin * 2 || up > maximumHeight + body.SafeMargin) continue;
            parameters.From = from;
            parameters.Motion = Vector3.Up * up;
            blocked = "headroom";
            if (PhysicsServer3D.BodyTestMotion(rid, parameters, result)) continue;
            parameters.From = from.Translated(parameters.Motion);
            parameters.Motion = motion;
            blocked = "forward-clearance";
            // A sloped landing's first ray can be too low to clear the whole
            // capsule. Try the remaining supported heights within the same
            // step bound; each candidate still needs both complete sweeps.
            if (PhysicsServer3D.BodyTestMotion(rid, parameters, result)) continue;
            destination = parameters.From.Origin + motion;
            blocked = null;
            return true;
        }
        return false;
    }

    // The mover's root support can coexist with a steep contact on the
    // rounded bottom of its capsule. Planning reads this same bounded ray;
    // the receipt contains only the actual native floor and never moves it.
    internal static bool TrySupport(CharacterBody3D body, Vector3 from, float maximumHeight,
        out NativeNavigationContact? contact)
    {
        contact = null;
        var reach = Math.Min(maximumHeight, body.FloorSnapLength);
        if (!from.IsFinite() || !float.IsFinite(reach) || reach <= 0) return false;
        using var ray = PhysicsRayQueryParameters3D.Create(from + Vector3.Up * body.SafeMargin,
            from - Vector3.Up * reach, body.CollisionMask, [body.GetRid()]);
        using var support = body.GetWorld3D().DirectSpaceState.IntersectRay(ray);
        if (support.Count == 0) return false;
        var point = support["position"].AsVector3(); var normal = support["normal"].AsVector3();
        contact = new(checked((ulong)support["collider_id"].AsInt64()), support["shape"].AsInt32(),
            point, normal, ray.From, ray.To);
        return point.IsFinite() && normal.IsFinite() && normal.Dot(Vector3.Up) >= MathF.Cos(body.FloorMaxAngle);
    }
}
