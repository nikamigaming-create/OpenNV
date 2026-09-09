using System.Numerics;

namespace OpenNV.Runtime.Gameplay.Bots;

// Input policy only. The caller owns observation, navigation and authoritative
// movement. No transforms, quest state or collision response are written here.
internal sealed class ReactiveSteering
{
    private float _yawVelocity, _pitchVelocity;

    internal SteeringIntent Step(Vector3 camera, Vector3 forward, Vector3 lookAt,
        bool advance, float seconds)
    {
        if (!Finite(camera) || !Finite(forward) || !Finite(lookAt) || !float.IsFinite(seconds) || seconds <= 0)
            throw new ArgumentException("Steering requires a finite observation and positive frame duration.");
        // A delayed frame must not produce a large catch-up camera jump.
        var dt = Math.Min(seconds, .05f);
        var direction = lookAt - camera;
        var horizontal = MathF.Sqrt(direction.X * direction.X + direction.Z * direction.Z);
        if (horizontal < .001f) { Reset(); return default; }
        var yawError = Wrap(MathF.Atan2(direction.X, direction.Z) - MathF.Atan2(forward.X, forward.Z));
        var pitchError = MathF.Atan2(direction.Y, horizontal) - MathF.Asin(Math.Clamp(forward.Y, -1, 1));
        var yaw = Turn(yawError, dt, ref _yawVelocity, 2.1f);
        var pitch = Turn(pitchError, dt, ref _pitchVelocity, 1.2f);
        return new(advance && MathF.Abs(yawError) < .6f, yaw, pitch);
    }

    internal void Reset() => _yawVelocity = _pitchVelocity = 0;
    private static float Turn(float error, float dt, ref float velocity, float maximum)
    {
        var requested = Math.Clamp(error * 6, -maximum, maximum);
        velocity += Math.Clamp(requested - velocity, -8 * dt, 8 * dt);
        var delta = velocity * dt;
        if (delta * error < 0) return 0;
        return MathF.CopySign(Math.Min(MathF.Abs(delta), MathF.Abs(error)), error);
    }
    private static float Wrap(float angle) => MathF.IEEERemainder(angle, MathF.Tau);
    private static bool Finite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}

internal readonly record struct SteeringIntent(bool Forward, float YawRadians, float PitchRadians)
{
    internal Vector3? AimAt { get; init; }
}
