using System.Buffers.Binary;

namespace OpenNV.Runtime.Formats.FaceGen;

internal sealed record FalloutEgtMode(float Scale, ReadOnlyMemory<byte> PlanarSignedRgb);
internal sealed record FalloutFaceGenTextureDelta(int Width, int Height, float[] Rgb);

/// <summary>
/// FaceGen SDK FREGT003. Rows precede columns. Each mode is a float scale and
/// three signed-byte image planes, ordered left-to-right and top-to-bottom.
/// Output is an additive statistical color delta, not a fabricated mean image.
/// </summary>
internal sealed class FalloutEgtFile
{
    private const int HeaderBytes = 64;
    internal int Width { get; }
    internal int Height { get; }
    internal uint BasisVersion { get; }
    internal ReadOnlyMemory<byte> SourceBytes { get; }
    internal IReadOnlyList<FalloutEgtMode> SymmetricModes { get; }
    internal IReadOnlyList<FalloutEgtMode> AsymmetricModes { get; }

    private FalloutEgtFile(ReadOnlyMemory<byte> source, int width, int height, uint basis,
        IReadOnlyList<FalloutEgtMode> symmetric, IReadOnlyList<FalloutEgtMode> asymmetric)
    {
        SourceBytes = source;
        Width = width;
        Height = height;
        BasisVersion = basis;
        SymmetricModes = symmetric;
        AsymmetricModes = asymmetric;
    }

    internal static bool HasSupportedSignature(ReadOnlySpan<byte> source) =>
        source.Length >= 8 && source[..8].SequenceEqual("FREGT003"u8);

    internal static FalloutEgtFile Read(ReadOnlyMemory<byte> source)
    {
        var bytes = source.Span;
        if (bytes.Length < HeaderBytes || !HasSupportedSignature(bytes))
            throw new InvalidDataException("EGT requires a complete FREGT003 header.");
        var rows = BinaryPrimitives.ReadUInt32LittleEndian(bytes[8..]);
        var columns = BinaryPrimitives.ReadUInt32LittleEndian(bytes[12..]);
        var symmetric = BinaryPrimitives.ReadUInt32LittleEndian(bytes[16..]);
        var asymmetric = BinaryPrimitives.ReadUInt32LittleEndian(bytes[20..]);
        var basis = BinaryPrimitives.ReadUInt32LittleEndian(bytes[24..]);
        if (rows == 0 || columns == 0 || rows > int.MaxValue || columns > int.MaxValue ||
            (ulong)rows * columns > int.MaxValue / 3 || symmetric > int.MaxValue || asymmetric > int.MaxValue)
            throw new InvalidDataException("EGT dimensions or mode counts exceed addressable source data.");
        var pixels = checked((int)(rows * columns));
        var modeBytes = 4L + pixels * 3L;
        var count = (long)symmetric + asymmetric;
        if (count > (source.Length - HeaderBytes) / modeBytes || HeaderBytes + count * modeBytes != source.Length)
            throw new InvalidDataException("EGT mode counts do not cover its exact source extent.");
        var modes = new FalloutEgtMode[(int)count];
        var offset = HeaderBytes;
        for (var index = 0; index < modes.Length; ++index)
        {
            var scale = BinaryPrimitives.ReadSingleLittleEndian(bytes[offset..]);
            if (!float.IsFinite(scale)) throw new InvalidDataException($"EGT mode {index} has a non-finite scale.");
            modes[index] = new FalloutEgtMode(scale, source.Slice(offset + 4, pixels * 3));
            offset += checked((int)modeBytes);
        }
        return new FalloutEgtFile(source, (int)columns, (int)rows, basis, modes[..(int)symmetric], modes[(int)symmetric..]);
    }

    internal FalloutFaceGenTextureDelta EvaluateDelta(IReadOnlyList<float> symmetric, IReadOnlyList<float> asymmetric)
    {
        ValidateWeights(symmetric, SymmetricModes.Count);
        ValidateWeights(asymmetric, AsymmetricModes.Count);
        var output = new float[checked(Width * Height * 3)];
        Accumulate(output, SymmetricModes, symmetric);
        Accumulate(output, AsymmetricModes, asymmetric);
        return new FalloutFaceGenTextureDelta(Width, Height, output);
    }

    private static void Accumulate(float[] output, IReadOnlyList<FalloutEgtMode> modes, IReadOnlyList<float> weights)
    {
        var pixels = output.Length / 3;
        for (var modeIndex = 0; modeIndex < modes.Count; ++modeIndex)
        {
            var mode = modes[modeIndex];
            var bytes = mode.PlanarSignedRgb.Span;
            for (var channel = 0; channel < 3; ++channel)
                for (var pixel = 0; pixel < pixels; ++pixel)
                {
                    var target = pixel * 3 + channel;
                    output[target] += unchecked((sbyte)bytes[channel * pixels + pixel]) * mode.Scale * weights[modeIndex];
                    if (!float.IsFinite(output[target])) throw new InvalidDataException("EGT produced a non-finite texture delta.");
                }
        }
    }

    private static void ValidateWeights(IReadOnlyList<float> weights, int count)
    {
        if (weights.Count != count || weights.Any(weight => !float.IsFinite(weight)))
            throw new InvalidDataException("EGT requires one finite coefficient for every source mode.");
    }
}
