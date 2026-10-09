using System.Numerics;

namespace OpenNV.Runtime.Formats.Gamebryo;

internal static class FalloutNifParticleSphere
{
    internal static void Validate(float radius)
    {
        if (!float.IsFinite(radius) || radius <= 0)
            throw new InvalidDataException("Particle collision sphere requires a finite positive radius.");
    }

    // Swept source points contact the outside of the declared local sphere.
    // Drawing radius is independent. Interior/moving-collider retail response
    // remains an explicit parity boundary, as does existing plane sidedness.
    internal static FalloutNifParticleCollisionHit? Sweep(Vector3 start, Vector3 velocity, float seconds, float radius)
    {
        Validate(radius);
        if (!float.IsFinite(seconds) || seconds < 0 || !Finite(start) || !Finite(velocity))
            throw new InvalidDataException("Particle sphere sweep has nonfinite motion.");
        var a = Dot(velocity, velocity);
        var b = Dot(start, velocity);
        var c = Dot(start, start) - (double)radius * radius;
        if (seconds == 0 || a == 0 || c < 0 || b >= 0) return null;
        var discriminant = b * b - a * c;
        if (discriminant <= 0) return null;
        // Stable quadratic roots avoid losing a nearby contact when the other
        // intersection is many source units away. Work in double until output.
        var q = -b - Math.CopySign(Math.Sqrt(discriminant), b);
        var first = q / a;
        var second = c / q;
        var time = Math.Min(first, second);
        if (time < 0 || time > seconds) return null;
        var point = new Vector3((float)(start.X + velocity.X * time),
            (float)(start.Y + velocity.Y * time), (float)(start.Z + velocity.Z * time));
        var normal = Vector3.Normalize(point);
        if (!Finite(point) || !Finite(normal)) throw new InvalidDataException("Particle sphere contact is not finite.");
        return new((float)(time / seconds), point, normal);
    }

    private static double Dot(Vector3 first, Vector3 second) =>
        (double)first.X * second.X + (double)first.Y * second.Y + (double)first.Z * second.Z;
    private static bool Finite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
