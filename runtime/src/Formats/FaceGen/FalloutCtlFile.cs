using System.Buffers.Binary;
using System.Text;

namespace OpenNV.Runtime.Formats.FaceGen;

internal sealed record FalloutCtlControl(string Label, float[] Axis);
internal sealed record FalloutCtlAffineAxis(float[] Axis, float Offset);
internal sealed record FalloutCtlSeparation(int From, int To, float[] Geometry, float[] Texture, float Offset);
internal sealed record FalloutCtlDistribution(float[] GeometryMean, float[] TextureMean,
    float[] JointMatrix, float[] GeometryMatrix, float[] TextureMatrix);

/// <summary>FRCTL001 control axes and the complete statistical model, decoded in source order.</summary>
internal sealed record FalloutCtlFile(uint GeometryBasisVersion, uint TextureBasisVersion, int[] BasisCounts,
    IReadOnlyList<FalloutCtlControl>[] Controls, FalloutCtlAffineAxis[][][] AffineAxes,
    IReadOnlyList<FalloutCtlSeparation> Separations, IReadOnlyList<FalloutCtlDistribution> Distributions)
{
    // The FRCTL001 model has five population slots. This is an implicit file
    // layout, independent of the game's playable RACE records and menu choices.
    private const int PopulationSlots = 5;

    internal static FalloutCtlFile Read(ReadOnlyMemory<byte> source)
    {
        var input = new Cursor(source.Span);
        if (!input.Bytes(8).SequenceEqual("FRCTL001"u8)) throw new InvalidDataException("CTL signature is unsupported.");
        var geometryVersion = input.UInt32(); var textureVersion = input.UInt32();
        var dimensions = new int[4];
        for (var index = 0; index < dimensions.Length; index++) dimensions[index] = input.Count();
        var controls = new IReadOnlyList<FalloutCtlControl>[4];
        for (var group = 0; group < controls.Length; group++)
        {
            var count = input.Count();
            if ((long)count * (4L * dimensions[group] + 4) > input.Remaining)
                throw new InvalidDataException("CTL control count exceeds the remaining source extent.");
            var rows = new FalloutCtlControl[count];
            for (var index = 0; index < count; index++)
            {
                var axis = input.Floats(dimensions[group]);
                var text = input.Bytes(input.Count());
                if (text.Length == 0 || text.Contains((byte)0)) throw new InvalidDataException("CTL control label is empty or contains a terminator.");
                rows[index] = new(Encoding.Latin1.GetString(text), axis);
            }
            controls[group] = rows;
        }
        var symmetric = new[] { dimensions[0], dimensions[2] };
        var affine = new FalloutCtlAffineAxis[PopulationSlots][][];
        for (var population = 0; population < affine.Length; population++)
        {
            affine[population] = new FalloutCtlAffineAxis[2][];
            for (var attribute = 0; attribute < 2; attribute++)
            {
                affine[population][attribute] = new FalloutCtlAffineAxis[2];
                for (var domain = 0; domain < 2; domain++)
                    affine[population][attribute][domain] = new(input.Floats(symmetric[domain]), input.Float());
            }
        }
        var separations = new List<FalloutCtlSeparation>();
        for (var from = 0; from < PopulationSlots; from++)
            for (var to = 0; to < PopulationSlots; to++)
                if (from != to) separations.Add(new(from, to, input.Floats(symmetric[0]), input.Floats(symmetric[1]), input.Float()));
        var distributions = new FalloutCtlDistribution[PopulationSlots];
        var jointDimension = checked((long)symmetric[0] + symmetric[1]);
        for (var population = 0; population < distributions.Length; population++)
            distributions[population] = new(input.Floats(symmetric[0]), input.Floats(symmetric[1]),
                input.Floats(jointDimension * jointDimension), input.Floats((long)symmetric[0] * symmetric[0]),
                input.Floats((long)symmetric[1] * symmetric[1]));
        if (input.Remaining != 0) throw new InvalidDataException("CTL has unaccounted source bytes.");
        return new(geometryVersion, textureVersion, dimensions, controls, affine, separations, distributions);
    }

    private ref struct Cursor(ReadOnlySpan<byte> source)
    {
        private ReadOnlySpan<byte> _remaining = source;
        internal int Remaining => _remaining.Length;
        internal ReadOnlySpan<byte> Bytes(int count)
        {
            if (count < 0 || count > Remaining) throw new InvalidDataException("CTL source is truncated.");
            var result = _remaining[..count]; _remaining = _remaining[count..]; return result;
        }
        internal uint UInt32() => BinaryPrimitives.ReadUInt32LittleEndian(Bytes(4));
        internal int Count()
        {
            var value = UInt32();
            if (value > int.MaxValue) throw new InvalidDataException("CTL count is outside the supported address space.");
            return (int)value;
        }
        internal float Float()
        {
            var value = BinaryPrimitives.ReadSingleLittleEndian(Bytes(4));
            if (!float.IsFinite(value)) throw new InvalidDataException("CTL contains a non-finite model coordinate.");
            return value;
        }
        internal float[] Floats(long count)
        {
            if (count < 0 || count > Remaining / 4) throw new InvalidDataException("CTL vector or matrix exceeds the source extent.");
            var result = new float[(int)count];
            for (var index = 0; index < result.Length; index++) result[index] = Float();
            return result;
        }
    }
}
