using System.Buffers.Binary;

namespace OpenNV.Runtime.Formats.FaceGen;

/// <summary>Control-space editing of source NPC and race coefficient vectors.</summary>
internal static class FalloutFaceGenControls
{
    internal static float Project(ReadOnlySpan<byte> npc, ReadOnlySpan<byte> race, IReadOnlyList<float> axis)
        => Dot(Combined(npc, race, axis.Count), axis);

    internal static byte[] SetControl(ReadOnlySpan<byte> npc, ReadOnlySpan<byte> race, IReadOnlyList<float> axis, float target)
    {
        if (!float.IsFinite(target)) throw new InvalidDataException("Face control target is non-finite.");
        var combined = Combined(npc, race, axis.Count);
        var delta = target - Dot(combined, axis);
        for (var index = 0; index < combined.Length; index++) combined[index] += delta * axis[index];
        return RelativeToRace(combined, race);
    }

    internal static float Attribute(ReadOnlySpan<byte> npc, ReadOnlySpan<byte> race, FalloutCtlAffineAxis axis)
        => Project(npc, race, axis.Axis) + axis.Offset;

    // The two statistical attributes share a domain. Solve their Gram system
    // together, so changing age does not unintentionally change the other
    // authored attribute. Control axes and affine axes have different setters.
    internal static byte[] SetAttribute(ReadOnlySpan<byte> npc, ReadOnlySpan<byte> race,
        IReadOnlyList<FalloutCtlAffineAxis> axes, int changed, float target)
    {
        if (axes.Count != 2 || changed is < 0 or > 1 || !float.IsFinite(target))
            throw new InvalidDataException("Affine face edit has an invalid extent, index or target.");
        var combined = Combined(npc, race, axes[0].Axis.Length);
        var a = Dot(axes[0].Axis, axes[0].Axis); var b = Dot(axes[0].Axis, axes[1].Axis);
        var c = Dot(axes[1].Axis, axes[1].Axis); var determinant = a * c - b * b;
        if (!float.IsFinite(determinant) || determinant <= 0) throw new InvalidDataException("Face attribute axes are singular.");
        var delta = target - (Dot(combined, axes[changed].Axis) + axes[changed].Offset);
        var first = (changed == 0 ? c : -b) * delta / determinant;
        var second = (changed == 0 ? -b : a) * delta / determinant;
        for (var index = 0; index < combined.Length; index++)
        {
            combined[index] += first * axes[0].Axis[index];
            combined[index] += second * axes[1].Axis[index];
        }
        return RelativeToRace(combined, race);
    }

    private static float Dot(IReadOnlyList<float> left, IReadOnlyList<float> right)
    {
        if (left.Count != right.Count || left.Count == 0) throw new InvalidDataException("Face control axis extent differs.");
        double value = 0;
        for (var index = 0; index < left.Count; index++) value += (double)left[index] * right[index];
        if (!double.IsFinite(value) || !float.IsFinite((float)value)) throw new InvalidDataException("Face projection is non-finite.");
        return (float)value;
    }

    private static float[] Combined(ReadOnlySpan<byte> npc, ReadOnlySpan<byte> race, int count)
    {
        if (npc.Length != (long)count * 4 || race.Length != npc.Length || count == 0)
            throw new InvalidDataException("Face coefficients and control axes have incompatible extents.");
        var result = new float[count];
        for (var index = 0; index < count; index++)
        {
            result[index] = BinaryPrimitives.ReadSingleLittleEndian(npc[(index * 4)..]) + BinaryPrimitives.ReadSingleLittleEndian(race[(index * 4)..]);
            if (!float.IsFinite(result[index])) throw new InvalidDataException("Face coefficient is non-finite.");
        }
        return result;
    }

    private static byte[] RelativeToRace(IReadOnlyList<float> combined, ReadOnlySpan<byte> race)
    {
        var result = new byte[race.Length];
        for (var index = 0; index < combined.Count; index++)
        {
            var value = combined[index] - BinaryPrimitives.ReadSingleLittleEndian(race[(index * 4)..]);
            if (!float.IsFinite(value)) throw new InvalidDataException("Edited face coefficient is non-finite.");
            BinaryPrimitives.WriteSingleLittleEndian(result.AsSpan(index * 4), value);
        }
        return result;
    }
}
