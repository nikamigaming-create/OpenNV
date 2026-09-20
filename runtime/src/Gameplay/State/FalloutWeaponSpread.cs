using Godot;

namespace OpenNV.Runtime.Gameplay.State;

internal static class FalloutWeaponSpread
{
    internal static Vector3 Deviate(Vector3 center, float minimumSpreadDegrees, Func<float> nextUnit)
    {
        ArgumentNullException.ThrowIfNull(nextUnit);
        if (!IsFinite(center) || center.LengthSquared() <= float.Epsilon ||
            !float.IsFinite(minimumSpreadDegrees) || minimumSpreadDegrees < 0)
            throw new InvalidDataException("Weapon spread input is invalid.");
        var forward = center.Normalized();
        if (minimumSpreadDegrees == 0) return forward;

        var angleUnit = nextUnit();
        var rotationUnit = nextUnit();
        if (!float.IsFinite(angleUnit) || angleUnit is < 0 or > 1 ||
            !float.IsFinite(rotationUnit) || rotationUnit is < 0 or > 1)
            throw new InvalidDataException("Weapon spread random sample is invalid.");

        var helper = Mathf.Abs(forward.Dot(Vector3.Up)) < .99f ? Vector3.Up : Vector3.Right;
        var right = forward.Cross(helper).Normalized();
        var up = right.Cross(forward).Normalized();
        var angle = Mathf.DegToRad(minimumSpreadDegrees * 2 * angleUnit);
        var rotation = MathF.Tau * rotationUnit;
        var sine = Mathf.Sin(angle);
        var result = forward * Mathf.Cos(angle) +
            right * (Mathf.Cos(rotation) * sine) +
            up * (Mathf.Sin(rotation) * sine);
        if (!IsFinite(result) || result.LengthSquared() <= float.Epsilon)
            throw new InvalidDataException("Weapon spread produced an invalid direction.");
        return result.Normalized();
    }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
