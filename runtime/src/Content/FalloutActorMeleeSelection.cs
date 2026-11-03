using System.Numerics;

namespace OpenNV.Runtime.Content;

internal readonly record struct FalloutMeleeActorGeometry(Vector3 Position, float SurfaceExtent,
    float MinimumHeight, float MaximumHeight, bool Swimming);

internal static class FalloutActorMeleeSelection
{
    internal static float Reach(bool intrinsicUnarmed, float weaponReach, float handReachMultiplier,
        float combatDistance, float actorScale, float units)
    {
        var multiplier = intrinsicUnarmed ? handReachMultiplier : weaponReach;
        if (!float.IsFinite(multiplier) || multiplier < 0 || !float.IsFinite(combatDistance) || combatDistance <= 0 ||
            !float.IsFinite(actorScale) || actorScale <= 0 || !float.IsFinite(units) || units <= 0)
            throw new InvalidDataException("Player melee source reach is invalid.");
        var reach = multiplier * combatDistance * actorScale * units;
        return float.IsFinite(reach) ? reach : throw new InvalidDataException("Player melee source reach overflowed.");
    }

    internal static float Distance(FalloutMeleeActorGeometry attacker, FalloutMeleeActorGeometry target)
    {
        Require(attacker); Require(target);
        var offset = target.Position - attacker.Position;
        var attackerMinimum = attacker.Position.Y + attacker.MinimumHeight;
        var attackerMaximum = attacker.Position.Y + attacker.MaximumHeight;
        var targetMinimum = target.Position.Y + target.MinimumHeight;
        var targetMaximum = target.Position.Y + target.MaximumHeight;
        // The source swimming test checks the attacker's vertical endpoints
        // inside the target interval; it is intentionally asymmetric.
        if (attacker.Swimming && target.Swimming &&
            (attackerMinimum >= targetMinimum && attackerMinimum <= targetMaximum ||
             attackerMaximum >= targetMinimum && attackerMaximum <= targetMaximum))
            offset.Y = 0;
        return offset.Length() - attacker.SurfaceExtent - target.SurfaceExtent;
    }

    internal static float Angle(Vector3 offset, Vector3 forward)
    {
        if (!Finite(offset) || !Finite(forward)) throw new InvalidDataException("Melee bearing is not finite.");
        var length = Math.Sqrt((double)offset.X * offset.X + (double)offset.Z * offset.Z);
        var facing = Math.Sqrt((double)forward.X * forward.X + (double)forward.Z * forward.Z);
        if (facing == 0) throw new InvalidDataException("Melee has no actual horizontal facing.");
        if (length == 0) return 0;
        var dot = ((double)offset.X * forward.X + (double)offset.Z * forward.Z) / (length * facing);
        return (float)(Math.Acos(Math.Clamp(dot, -1, 1)) * 180 / Math.PI);
    }

    internal static bool InCone(float angle, float referenceDistance, float degrees, float units, bool dead, float deadMultiplier)
    {
        if (!float.IsFinite(angle) || angle < 0 || !float.IsFinite(referenceDistance) || referenceDistance < 0 ||
            !float.IsFinite(degrees) || degrees < 0 || !float.IsFinite(units) || units <= 0 ||
            !float.IsFinite(deadMultiplier) || deadMultiplier < 0)
            throw new InvalidDataException("Melee source cone is invalid.");
        var allowed = referenceDistance < 128 * units ? degrees * 3 : degrees;
        return angle <= allowed || dead && angle <= allowed * deadMultiplier;
    }

    private static void Require(FalloutMeleeActorGeometry actor)
    {
        if (!Finite(actor.Position) || !float.IsFinite(actor.SurfaceExtent) ||
            !float.IsFinite(actor.MinimumHeight) || !float.IsFinite(actor.MaximumHeight) || actor.MinimumHeight > actor.MaximumHeight)
            throw new InvalidDataException("Melee actor source geometry is invalid.");
    }
    private static bool Finite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
