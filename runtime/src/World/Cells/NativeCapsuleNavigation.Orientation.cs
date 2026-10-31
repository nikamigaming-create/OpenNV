using Godot;

namespace OpenNV.Runtime.World.Cells;

internal static partial class NativeCapsuleNavigation
{
    private static readonly StringName DirectionalEnvelope = "source_directional_character_envelope";
    internal static void SetDirectionalEnvelope(CharacterBody3D body, bool directional)
        => body.SetMeta(DirectionalEnvelope, directional);

    internal static Basis PlanningBasis(Basis original, Vector3 from, Vector3 desired)
    {
        var direction = desired - from; direction.Y = 0;
        if (direction.LengthSquared() <= .000000000001f) return original;
        return new Basis(Vector3.Up, MathF.Atan2(-direction.X, -direction.Z)).Scaled(original.Scale);
    }
}
