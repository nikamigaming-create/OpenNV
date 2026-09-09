using System.Numerics;

namespace OpenNV.Runtime.Formats.Gamebryo;

/// <summary>Spherical cubic interpolation of authored tension/bias/continuity rotation keys.</summary>
internal static class FalloutNifQuaternionCurve
{
    // Quaternion log/exp and SQUAD endpoint derivatives:
    // David Eberly, Quaternion Algebra and Calculus, sections 5-6.
    // https://www.geometrictools.com/Documentation/Quaternions.pdf
    // These are mathematical interpolation contracts, not a retail timing claim.
    internal static Quaternion Sample(FalloutNifQuaternionKey[] keys, int index, float amount)
    {
        var first = Value(keys[index]); var last = Align(first, Value(keys[index + 1]));
        var delta = Log(Quaternion.Conjugate(first) * last);
        var interval = keys[index + 1].Time - keys[index].Time;
        var outgoing = Tangent(keys, index, interval, true);
        var incoming = Tangent(keys, index + 1, interval, false);
        var a = Quaternion.Normalize(first * Exp((outgoing - delta) * 0.5f));
        var b = Quaternion.Normalize(last * Exp((delta - incoming) * 0.5f));
        return Quaternion.Normalize(Quaternion.Slerp(Quaternion.Slerp(first, last, amount),
            Quaternion.Slerp(a, Align(a, b), amount), 2 * amount * (1 - amount)));
    }

    private static Vector3 Tangent(FalloutNifQuaternionKey[] keys, int index, float interval, bool outgoing)
    {
        var current = Value(keys[index]);
        var previousTime = index == 0 ? keys[1].Time - keys[0].Time : keys[index].Time - keys[index - 1].Time;
        var nextTime = index == keys.Length - 1 ? previousTime : keys[index + 1].Time - keys[index].Time;
        var previous = index == 0 ? Vector3.Zero : -Log(Quaternion.Conjugate(current) * Align(current, Value(keys[index - 1])));
        var next = index == keys.Length - 1 ? Vector3.Zero : Log(Quaternion.Conjugate(current) * Align(current, Value(keys[index + 1])));
        // One-sided continuation at the two endpoints; interior tangent weights
        // retain source time spacing and all three authored T/B/C parameters.
        if (index == 0) previous = next;
        if (index == keys.Length - 1) next = previous;
        var tbc = keys[index].Tbc ?? throw new InvalidDataException("TCB quaternion key has no parameters.");
        var continuity = outgoing ? tbc.Z : -tbc.Z;
        return interval * (1 - tbc.X) / (previousTime + nextTime) *
            ((1 + continuity) * (1 + tbc.Y) * previous + (1 - continuity) * (1 - tbc.Y) * next);
    }

    private static Quaternion Value(FalloutNifQuaternionKey key) => Quaternion.Normalize(
        new(key.Value.X, key.Value.Y, key.Value.Z, key.Value.W));
    private static Quaternion Align(Quaternion reference, Quaternion value) => Quaternion.Dot(reference, value) < 0 ? -value : value;
    private static Vector3 Log(Quaternion value)
    {
        value = Quaternion.Normalize(value);
        var vector = new Vector3(value.X, value.Y, value.Z); var length = vector.Length();
        return length < 1e-7f ? vector : vector * (MathF.Atan2(length, value.W) / length);
    }
    private static Quaternion Exp(Vector3 value)
    {
        var length = value.Length();
        return new Quaternion(value * (length < 1e-7f ? 1 : MathF.Sin(length) / length), MathF.Cos(length));
    }
}
