using System.Numerics;

namespace OpenNV.Runtime.Formats.Gamebryo;

internal readonly record struct FalloutNifParticleCollisionHit(float Fraction, Vector3 Point, Vector3 Normal);

internal static class FalloutNifParticlePlane
{
    internal static void Validate(float width, float height, Vector3 xAxis, Vector3 yAxis)
    {
        if (!float.IsFinite(width) || !float.IsFinite(height) || width <= 0 || height <= 0 ||
            !float.IsFinite(xAxis.LengthSquared()) || !float.IsFinite(yAxis.LengthSquared()) ||
            MathF.Abs(xAxis.LengthSquared() - 1) > .001f || MathF.Abs(yAxis.LengthSquared() - 1) > .001f ||
            MathF.Abs(Vector3.Dot(xAxis, yAxis)) > .001f)
            throw new NotSupportedException("Particle collision plane requires a finite orthonormal rectangle.");
    }

    // Sweep the source point against the front of the declared local plane.
    // Particle radius belongs to drawing and does not enlarge its collider.
    internal static FalloutNifParticleCollisionHit? Sweep(Vector3 start, Vector3 velocity, float seconds,
        float width, float height, Vector3 xAxis, Vector3 yAxis)
    {
        Validate(width, height, xAxis, yAxis);
        if (!float.IsFinite(seconds) || seconds < 0 || !float.IsFinite(start.LengthSquared()) || !float.IsFinite(velocity.LengthSquared()))
            throw new InvalidDataException("Particle plane sweep has nonfinite motion.");
        var normal = Vector3.Normalize(Vector3.Cross(xAxis, yAxis));
        var distance = Vector3.Dot(start, normal);
        var movement = Vector3.Dot(velocity, normal) * seconds;
        if (distance < 0 || movement >= 0 || distance + movement > 0) return null;
        var fraction = -distance / movement;
        var point = start + velocity * (seconds * fraction);
        if (MathF.Abs(Vector3.Dot(point, xAxis)) > width / 2 || MathF.Abs(Vector3.Dot(point, yAxis)) > height / 2) return null;
        return new(fraction, point, normal);
    }
}
