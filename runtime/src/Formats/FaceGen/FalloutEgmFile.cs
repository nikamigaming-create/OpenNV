using System.Buffers.Binary;
using System.Numerics;

namespace OpenNV.Runtime.Formats.FaceGen;

internal sealed record FalloutEgmMode(float Scale, ReadOnlyMemory<byte> PackedDeltas);

/// <summary>FaceGen SDK FREGM002: float scale followed by signed XYZ int16 deltas.</summary>
internal sealed class FalloutEgmFile
{
    private const int HeaderBytes = 64;
    internal int VertexCount { get; }
    internal uint BasisVersion { get; }
    internal ReadOnlyMemory<byte> SourceBytes { get; }
    internal IReadOnlyList<FalloutEgmMode> SymmetricModes { get; }
    internal IReadOnlyList<FalloutEgmMode> AsymmetricModes { get; }

    private FalloutEgmFile(ReadOnlyMemory<byte> source, int vertices, uint basis,
        IReadOnlyList<FalloutEgmMode> symmetric, IReadOnlyList<FalloutEgmMode> asymmetric)
    {
        SourceBytes = source;
        VertexCount = vertices;
        BasisVersion = basis;
        SymmetricModes = symmetric;
        AsymmetricModes = asymmetric;
    }

    internal static FalloutEgmFile Read(ReadOnlyMemory<byte> source)
    {
        var bytes = source.Span;
        if (bytes.Length < HeaderBytes || !bytes[..8].SequenceEqual("FREGM002"u8))
            throw new InvalidDataException("EGM requires a complete FREGM002 header.");
        var vertices = BinaryPrimitives.ReadUInt32LittleEndian(bytes[8..]);
        var symmetric = BinaryPrimitives.ReadUInt32LittleEndian(bytes[12..]);
        var asymmetric = BinaryPrimitives.ReadUInt32LittleEndian(bytes[16..]);
        var basis = BinaryPrimitives.ReadUInt32LittleEndian(bytes[20..]);
        var modeBytes = 4L + vertices * 6L;
        var count = (long)symmetric + asymmetric;
        if (vertices == 0 || vertices > int.MaxValue || symmetric > int.MaxValue || asymmetric > int.MaxValue ||
            count > (source.Length - HeaderBytes) / modeBytes || HeaderBytes + count * modeBytes != source.Length)
            throw new InvalidDataException("EGM vertex/mode counts do not cover its exact source extent.");
        var offset = HeaderBytes;
        var modes = new FalloutEgmMode[(int)count];
        for (var index = 0; index < modes.Length; ++index)
        {
            var scale = BinaryPrimitives.ReadSingleLittleEndian(bytes[offset..]);
            if (!float.IsFinite(scale)) throw new InvalidDataException($"EGM mode {index} has a non-finite scale.");
            modes[index] = new FalloutEgmMode(scale, source.Slice(offset + 4, checked((int)vertices * 6)));
            offset += checked((int)modeBytes);
        }
        return new FalloutEgmFile(source, (int)vertices, basis, modes[..(int)symmetric], modes[(int)symmetric..]);
    }

    internal Vector3[] EvaluateDeltas(IReadOnlyList<float> symmetric, IReadOnlyList<float> asymmetric)
    {
        ValidateWeights(symmetric, SymmetricModes.Count);
        ValidateWeights(asymmetric, AsymmetricModes.Count);
        var output = new Vector3[VertexCount];
        Accumulate(output, SymmetricModes, symmetric);
        Accumulate(output, AsymmetricModes, asymmetric);
        return output;
    }

    internal Vector3[] EvaluatePositions(IReadOnlyList<Vector3> sourceVertices,
        IReadOnlyList<float> symmetric, IReadOnlyList<float> asymmetric)
    {
        if (sourceVertices.Count != VertexCount)
            throw new InvalidDataException("EGM vertex count/order must match the source mesh including any TRI stat-morph vertices.");
        return EvaluateSourcePrefixPositions(sourceVertices, symmetric, asymmetric);
    }

    // NIF base vertices precede the additional TRI static-morph vertices in an
    // EGM. Apply only the explicit source prefix, retaining the complete EGM.
    internal Vector3[] EvaluateSourcePrefixPositions(IReadOnlyList<Vector3> sourceVertices,
        IReadOnlyList<float> symmetric, IReadOnlyList<float> asymmetric)
    {
        if (sourceVertices.Count == 0 || sourceVertices.Count > VertexCount)
            throw new InvalidDataException("EGM source vertex prefix is empty or exceeds its vertex table.");
        ValidateWeights(symmetric, SymmetricModes.Count);
        ValidateWeights(asymmetric, AsymmetricModes.Count);
        var output = sourceVertices.ToArray();
        if (output.Any(value => !Finite(value)))
            throw new InvalidDataException("EGM source vertex prefix contains a non-finite position.");
        // Native observation distinguishes base-first accumulation from adding
        // a separately summed delta: their float rounding is not identical.
        Accumulate(output, SymmetricModes, symmetric);
        Accumulate(output, AsymmetricModes, asymmetric);
        return output;
    }

    private static void Accumulate(Vector3[] output, IReadOnlyList<FalloutEgmMode> modes, IReadOnlyList<float> weights)
    {
        for (var modeIndex = 0; modeIndex < modes.Count; ++modeIndex)
        {
            var mode = modes[modeIndex];
            var bytes = mode.PackedDeltas.Span;
            var weight = weights[modeIndex];
            for (var vertex = 0; vertex < output.Length; ++vertex)
            {
                var offset = vertex * 6;
                var delta = new Vector3(BinaryPrimitives.ReadInt16LittleEndian(bytes[offset..]) * mode.Scale,
                    BinaryPrimitives.ReadInt16LittleEndian(bytes[(offset + 2)..]) * mode.Scale,
                    BinaryPrimitives.ReadInt16LittleEndian(bytes[(offset + 4)..]) * mode.Scale);
                output[vertex] += delta * weight;
                if (!Finite(output[vertex])) throw new InvalidDataException("EGM produced a non-finite vertex delta.");
            }
        }
    }

    private static void ValidateWeights(IReadOnlyList<float> weights, int count)
    {
        if (weights.Count != count || weights.Any(weight => !float.IsFinite(weight)))
            throw new InvalidDataException("EGM requires one finite coefficient for every source mode.");
    }
    private static bool Finite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
