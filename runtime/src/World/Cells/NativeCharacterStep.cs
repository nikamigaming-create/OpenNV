using Godot;

namespace OpenNV.Runtime.World.Cells;

// The optional OpenNV controller climbs a low obstruction only after sweeping
// its whole capsule up and forward toward a supported landing. These are the
// real physics shapes; a wall, ceiling or missing floor cannot be bypassed.
internal static partial class NativeCharacterStep
{
    internal static bool TestMotion(Rid body, PhysicsTestMotionParameters3D parameters, PhysicsTestMotionResult3D result)
    {
        using var scope = OpenNV.Runtime.Diagnostics.RuntimeFrameWork.Measure(OpenNV.Runtime.Diagnostics.RuntimeFrameWork.Owner.NativeCapsuleSweep);
        return PhysicsServer3D.BodyTestMotion(body, parameters, result);
    }

    internal static Godot.Collections.Dictionary QuerySupport(CharacterBody3D body, PhysicsRayQueryParameters3D ray)
    {
        using var scope = OpenNV.Runtime.Diagnostics.RuntimeFrameWork.Measure(OpenNV.Runtime.Diagnostics.RuntimeFrameWork.Owner.NativeSupportRay);
        return body.GetWorld3D().DirectSpaceState.IntersectRay(ray);
    }

    internal static bool TryStep(CharacterBody3D body, Vector3 motion, float maximumHeight)
        => TryStep(body, motion, maximumHeight, out _);

    internal static bool TryStep(CharacterBody3D body, Vector3 motion, float maximumHeight, out string? blocked,
        QueryResources? resources = null)
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
            if (!TrySupport(body, from.Origin, maximumHeight, out _, resources)) return false;
        }
        if (!TryQuery(body, from, motion, maximumHeight, out var destination, out blocked, out _, floorSupported: true,
                resources: resources)) return false;
        body.GlobalPosition = destination;
        return true;
    }

    // Planning uses the controller's supported-height probes and complete
    // sweeps at a supplied pose. This query never publishes that pose.
    internal static bool TryQuery(CharacterBody3D body, Transform3D from, Vector3 motion, float maximumHeight,
        out Vector3 destination, out string? blocked, out NativeNavigationContact? contact, bool floorSupported = false,
        QueryResources? resources = null)
    {
        destination = default; contact = null;
        var floorCosine = MathF.Cos(body.FloorMaxAngle);
        blocked = "floor-support";
        if (!floorSupported)
        {
            if (!TrySupport(body, from.Origin, maximumHeight, out _, resources)) return false;
        }
        using var ownedParameters = resources is null ? new PhysicsTestMotionParameters3D
        { Margin = body.SafeMargin, MaxCollisions = 4 } : null;
        var parameters = resources?.MotionParameters(body) ?? ownedParameters!;
        parameters.From = from; parameters.Motion = motion;
        using var ownedResult = resources is null ? new PhysicsTestMotionResult3D() : null;
        var result = resources?.MotionResult(body) ?? ownedResult!;
        var rid = body.GetRid();
        blocked = "no-obstruction";
        if (!TestMotion(rid, parameters, result)) return false;
        var wall = -1;
        var wallScore = 0f;
        var collisionCount = result.GetCollisionCount();
        for (var index = 0; index < collisionCount; index++)
        {
            var normal = result.GetCollisionNormal(index);
            if (!(normal.Dot(Vector3.Up) < floorCosine)) continue;
            var score = normal.Dot(motion);
            if (wall < 0 || score.CompareTo(wallScore) < 0) { wall = index; wallScore = score; }
        }
        blocked = "no-riser";
        if (wall < 0)
        {
            return TrySlope(body, from, motion, maximumHeight, parameters, result,
                out destination, out blocked, out contact);
        }
        // A bevel can be just steeper than the walkable slope. Probe beyond
        // the contacted riser for its top, bounded by the step height; every
        // accepted move still sweeps the whole capsule up and forward.
        var point = result.GetCollisionPoint(wall);
        contact = new(result.GetColliderId(wall), result.GetColliderShape(wall), point,
            result.GetCollisionNormal(wall), from.Origin, from.Origin + motion, Travel: result.GetTravel());
        var forward = motion.Normalized();
        using var ownedRay = resources is null ? CreateSupportRay(Vector3.Zero, Vector3.Zero, body.CollisionMask, rid) : null;
        var ray = resources?.SupportRay(body) ?? ownedRay!;
        using var normalKey = Variant.From("normal");
        using var positionKey = Variant.From("position");
        blocked = "landing-support";
        for (var probe = 0; probe < 4; probe++)
        {
            var reach = probe == 0 ? body.SafeMargin * 4 : maximumHeight / (1 << (3 - probe));
            var ahead = point + forward * reach;
            ahead.Y = from.Origin.Y + maximumHeight + body.SafeMargin;
            ray.From = ahead; ray.To = ahead - Vector3.Up * maximumHeight;
            using var landing = QuerySupport(body, ray);
            if (landing.Count == 0 || landing[normalKey].AsVector3().Dot(Vector3.Up) < floorCosine) continue;
            var up = landing[positionKey].AsVector3().Y - from.Origin.Y + body.SafeMargin;
            blocked = "step-height";
            if (up <= body.SafeMargin * 2 || up > maximumHeight + body.SafeMargin) continue;
            parameters.From = from;
            parameters.Motion = Vector3.Up * up;
            blocked = "headroom";
            if (TestMotion(rid, parameters, result)) continue;
            parameters.From = from.Translated(parameters.Motion);
            parameters.Motion = motion;
            blocked = "forward-clearance";
            // A sloped landing's first ray can be too low to clear the whole
            // capsule. Try the remaining supported heights within the same
            // step bound; each candidate still needs both complete sweeps.
            if (TestMotion(rid, parameters, result))
            {
                if (!TrySlope(body, parameters.From, motion, maximumHeight - up, parameters, result,
                    out destination, out blocked, out var slopeContact))
                { contact = slopeContact ?? contact; continue; }
                return true;
            }
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
        out NativeNavigationContact? contact, QueryResources? resources = null)
    {
        contact = null;
        var reach = Math.Min(maximumHeight, body.FloorSnapLength);
        if (!from.IsFinite() || !float.IsFinite(reach) || reach <= 0) return false;
        var rayFrom = from + Vector3.Up * body.SafeMargin; var rayTo = from - Vector3.Up * reach;
        using var ownedRay = resources is null ? CreateSupportRay(rayFrom, rayTo, body.CollisionMask, body.GetRid()) : null;
        var ray = resources?.SupportRay(body) ?? ownedRay!;
        if (resources is not null) { ray.From = rayFrom; ray.To = rayTo; }
        using var support = QuerySupport(body, ray);
        if (support.Count == 0) return false;
        using var positionKey = Variant.From("position");
        using var normalKey = Variant.From("normal");
        using var colliderKey = Variant.From("collider_id");
        using var shapeKey = Variant.From("shape");
        var point = support[positionKey].AsVector3(); var normal = support[normalKey].AsVector3();
        contact = new(checked((ulong)support[colliderKey].AsInt64()), support[shapeKey].AsInt32(),
            point, normal, ray.From, ray.To);
        return point.IsFinite() && normal.IsFinite() && normal.Dot(Vector3.Up) >= MathF.Cos(body.FloorMaxAngle);
    }
}
