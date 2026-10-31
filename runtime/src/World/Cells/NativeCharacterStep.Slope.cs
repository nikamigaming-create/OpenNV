using Godot;

namespace OpenNV.Runtime.World.Cells;

internal static partial class NativeCharacterStep
{
    // Consume the actual failed horizontal sweep, then validate the entire
    // capsule along its floor tangent. Planning and live movement use this
    // same clearance query. Its raised endpoint is not a settled floor pose:
    // the planner validates its downward sweep/root floor, while live movement
    // retains its initial support and ordinary gravity/floor-snap resolution.
    internal static bool TrySlope(CharacterBody3D body, Transform3D from, Vector3 motion, float maximumRise,
        PhysicsTestMotionParameters3D parameters, PhysicsTestMotionResult3D result,
        out Vector3 destination, out string? blocked, out NativeNavigationContact? contact)
    {
        destination = default; contact = null;
        blocked = "walkable-contact-without-forward-tangent";
        var floorCosine = MathF.Cos(body.FloorMaxAngle);
        var bestDot = 0f;
        var bestNormal = Vector3.Zero;
        for (var index = 0; index < result.GetCollisionCount(); index++)
        {
            var normal = result.GetCollisionNormal(index);
            if (!normal.IsFinite() || normal.Y < floorCosine)
            {
                contact = new(result.GetColliderId(index), result.GetColliderShape(index), result.GetCollisionPoint(index),
                    normal, from.Origin, from.Origin + motion, Travel: result.GetTravel());
                blocked = "slope-obstruction";
                return false;
            }
            var dot = normal.Dot(motion);
            if (dot < bestDot) { bestDot = dot; bestNormal = normal; }
        }
        if (!(bestDot < -.000001f)) return false;
        var rise = -bestDot / bestNormal.Y + body.SafeMargin * 4;
        blocked = "slope-height";
        if (rise > maximumRise + body.SafeMargin * 8) return false;
        var tangent = motion + Vector3.Up * rise;
        parameters.From = from; parameters.Motion = tangent;
        blocked = "slope-clearance";
        if (TestMotion(body.GetRid(), parameters, result))
        {
            if (result.GetCollisionCount() > 0)
                contact = new(result.GetColliderId(0), result.GetColliderShape(0), result.GetCollisionPoint(0),
                    result.GetCollisionNormal(0), from.Origin, from.Origin + tangent, Travel: result.GetTravel());
            return false;
        }
        destination = from.Origin + tangent;
        blocked = null;
        return true;
    }
}
