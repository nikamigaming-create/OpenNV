namespace OpenNV.Runtime.World.Cells;

// Shared integration for the player's unsupported motion. Contact recovery,
// floor snapping and collision sliding remain owned by the native controller.
internal static class NativeCharacterGravity
{
    internal static float AirVelocity(float previous, float gravity, float delta)
    {
        if (!float.IsFinite(previous) || !float.IsFinite(gravity) || !float.IsFinite(delta) || gravity <= 0 || delta < 0)
            throw new InvalidDataException("Airborne motion requires finite velocity, positive gravity and a nonnegative time step.");
        var next = previous - gravity * delta;
        return float.IsFinite(next) ? next : throw new InvalidDataException("Airborne velocity overflowed.");
    }
}
