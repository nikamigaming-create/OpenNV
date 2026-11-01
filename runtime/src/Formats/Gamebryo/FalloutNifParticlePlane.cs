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

    // Native planar contacts are two-sided; near-plane positions retain their position.
    // Particle radius belongs to drawing and does not enlarge its collider.
    internal static FalloutNifParticleCollisionHit? Sweep(Vector3 start, Vector3 velocity, float seconds,
        float width, float height, Vector3 xAxis, Vector3 yAxis, float distanceTolerance = .001f)
    {
        Validate(width, height, xAxis, yAxis);
        if (!float.IsFinite(seconds) || seconds < 0 || !float.IsFinite(start.LengthSquared()) || !float.IsFinite(velocity.LengthSquared()))
            throw new InvalidDataException("Particle plane sweep has nonfinite motion.");
        if (!float.IsFinite(distanceTolerance) || distanceTolerance <= 0)
            throw new InvalidDataException("Particle plane contact tolerance has no finite unit owner.");
        var normal = Vector3.Normalize(Vector3.Cross(xAxis, yAxis));
        var distance = Vector3.Dot(start, normal);
        var movement = Vector3.Dot(velocity, normal) * seconds;
        var fraction = 0f;
        var point = start;
        if (MathF.Abs(distance) >= distanceTolerance)
        {
            var endDistance = distance + movement;
            if (MathF.Abs(endDistance) < distanceTolerance) endDistance = 0;
            if (movement == 0 || (double)distance * endDistance > 0) return null;
            fraction = -distance / movement;
            point = start + velocity * (seconds * fraction);
        }
        if (MathF.Abs(Vector3.Dot(point, xAxis)) >= width / 2 || MathF.Abs(Vector3.Dot(point, yAxis)) >= height / 2) return null;
        return new(fraction, point, normal);
    }

    // Native far contacts which round across the plane are moved toward their
    // original side by 2^-21 times the largest absolute particle-space coordinate.
    internal static Vector3 CorrectPoint(Vector3 start, Vector3 point, Vector3 normal, Vector3 origin)
    {
        var constant = Vector3.Dot(normal, origin);
        var before = Vector3.Dot(normal, start) - constant;
        var after = Vector3.Dot(normal, point) - constant;
        if ((double)before * after >= 0) return point;
        var distance = MathF.Max(MathF.Max(MathF.Abs(point.X), MathF.Abs(point.Y)), MathF.Abs(point.Z)) / 2097152f;
        return before > 0 ? point + normal * distance : point - normal * distance;
    }

    // An ordinary response damps the entire reflected velocity. At frame-end
    // contact, native low normal speed removes only the normal component.
    internal static Vector3 Respond(Vector3 velocity, Vector3 normal, float bounce, bool frameEnd,
        float velocityTolerance = .001f)
    {
        if (!float.IsFinite(bounce) || bounce < 0 || !float.IsFinite(velocityTolerance) || velocityTolerance <= 0)
            throw new InvalidDataException("Particle plane response has an invalid bounce or unit owner.");
        var incoming = Vector3.Dot(velocity, normal);
        return frameEnd && incoming < velocityTolerance
            ? velocity - normal * incoming
            : (velocity - 2 * normal * incoming) * bounce;
    }
}
