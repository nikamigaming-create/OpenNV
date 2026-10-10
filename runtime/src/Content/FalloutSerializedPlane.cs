using System.Buffers.Binary;
using V3 = System.Numerics.Vector3;

namespace OpenNV.Runtime.Content;

// The source writes an axis and angle, not a quaternion. The two encoded
// dimensions are used directly as the plane's half extents by its constructor.
internal sealed record FalloutSerializedPlane(string Signature, IReadOnlyList<uint> ScalarBits)
{
    internal float HalfWidth => Scalar(0);
    internal float HalfHeight => Scalar(1);
    internal V3 Center => new(Scalar(2), Scalar(3), Scalar(4));
    internal V3 Axis => new(Scalar(5), Scalar(6), Scalar(7));
    internal float Angle => Scalar(8);
    private float Scalar(int ordinal) => BitConverter.UInt32BitsToSingle(ScalarBits[ordinal]);

    internal static FalloutSerializedPlane Decode(string signature, ReadOnlySpan<byte> bytes)
    {
        if (signature is not ("XOCP" or "XPTL") || bytes.Length != 9 * sizeof(float))
            throw new InvalidDataException("Serialized plane declaration has an unknown signature or incomplete extent.");
        var bits = new uint[9];
        for (var ordinal = 0; ordinal < bits.Length; ordinal++)
            bits[ordinal] = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(ordinal * sizeof(uint))..]);
        var result = new FalloutSerializedPlane(signature, Array.AsReadOnly(bits));
        result.Validate(); return result;
    }

    internal void Validate()
    {
        if (Signature is not ("XOCP" or "XPTL") || ScalarBits is null || ScalarBits.Count != 9)
            throw new InvalidDataException("Serialized plane lost its exact physical scalar declaration.");
        // XPTL is consumed and discarded by the selected source loader. Its
        // otherwise unused payload must not acquire a rendering admission gate.
        if (Signature == "XPTL") return;
        if (ScalarBits.Any(value => !float.IsFinite(BitConverter.UInt32BitsToSingle(value))) ||
            HalfWidth <= 0 || HalfHeight <= 0)
            throw new InvalidDataException("Serialized source occlusion rectangle has invalid dimensions or pose.");
    }

    internal IReadOnlyList<float> ClockwiseRotation()
    {
        Validate();
        if (Signature != "XOCP")
            throw new NotSupportedException("Source discarded portal declaration cannot produce an active plane pose.");
        var axis = Axis;
        var cosine = (float)Math.Cos(Angle); var sine = (float)Math.Sin(Angle); var rest = 1f - cosine;
        // The loader retains the declared axis. Normalizing it, normalizing a
        // quaternion, or using reference Euler angles changes its source pose.
        float[] rows = [
            axis.X * axis.X * rest + cosine, axis.X * axis.Y * rest + axis.Z * sine, axis.X * axis.Z * rest - axis.Y * sine,
            axis.Y * axis.X * rest - axis.Z * sine, axis.Y * axis.Y * rest + cosine, axis.Y * axis.Z * rest + axis.X * sine,
            axis.Z * axis.X * rest + axis.Y * sine, axis.Z * axis.Y * rest - axis.X * sine, axis.Z * axis.Z * rest + cosine];
        if (rows.Any(value => !float.IsFinite(value)))
            throw new InvalidDataException("Serialized source rotation cannot produce finite plane axes.");
        return Array.AsReadOnly(rows);
    }

    internal IReadOnlyList<V3> SourceCorners()
    {
        var rows = ClockwiseRotation(); var center = Center;
        var horizontal = new V3(rows[0], rows[3], rows[6]) * HalfWidth;
        var vertical = new V3(rows[1], rows[4], rows[7]) * HalfHeight;
        // Physical corner order is retained for the source side-plane masks.
        return Array.AsReadOnly(new[] { center + horizontal + vertical, center - horizontal + vertical,
            center - horizontal - vertical, center + horizontal - vertical });
    }
}
