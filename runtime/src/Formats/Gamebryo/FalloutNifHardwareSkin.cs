namespace OpenNV.Runtime.Formats.Gamebryo;

internal sealed record FalloutNifHardwareSkinPartition(
    int PartitionIndex,
    ushort[] VertexMap,
    ushort[] BonePalette,
    int[] BoneIndices,
    float[] Weights,
    int InfluencesPerVertex,
    FalloutNifTriangle[] Triangles,
    FalloutNifBodyPartition? BodyPart);

internal static class FalloutNifHardwareSkin
{
    // Fallout BSDismemberBodyPartType: intact bodies show ordinary parts and
    // torso sections. Section/torso caps belong to severed-body presentation.
    // https://www.niftools.org/nifxml/BSDismemberBodyPartType.html
    internal static bool VisibleOnIntactBody(FalloutNifBodyPartition? partition) => partition?.BodyPart switch
    {
        null => true,
        <= 13 => true,
        >= 101 and <= 113 or >= 201 and <= 213 => false,
        >= 1000 and <= 13000 when partition.Value.BodyPart % 1000 == 0 => true,
        _ => throw new NotSupportedException($"Unknown Fallout body partition {partition.Value.BodyPart}."),
    };

    internal static IReadOnlyList<FalloutNifHardwareSkinPartition> Read(
        FalloutNifSkinInstance instance,
        FalloutNifSkinData data,
        FalloutNifSkinPartition partition,
        int vertexCount)
    {
        if (instance.Data != data.Block.Index || instance.SkinPartition != partition.Block.Index ||
            instance.Bones.Length == 0 || instance.Bones.Length != data.Bones.Length || vertexCount <= 0)
            throw new InvalidDataException("NIF hardware skin references or bone count differ.");
        if (instance.BodyPartitions.Length != 0 && instance.BodyPartitions.Length != partition.Partitions.Length)
            throw new InvalidDataException("NIF dismember partition metadata does not cover its hardware partitions.");
        var covered = new bool[vertexCount];
        var output = new List<FalloutNifHardwareSkinPartition>();
        for (var index = 0; index < partition.Partitions.Length; index++)
        {
            var source = partition.Partitions[index];
            if (source.WeightsPerVertex == 0 || source.WeightsPerVertex > 8)
                throw new NotSupportedException(
                    $"NIF skin partition {index} has {source.WeightsPerVertex} influences; Godot supports at most eight.");
            if (source.VertexMap.Length != source.VertexCount ||
                source.VertexWeights.Length != source.VertexCount ||
                source.BoneIndices.Length != source.VertexCount || source.Bones.Length == 0 ||
                source.Bones.Any(bone => bone >= instance.Bones.Length) ||
                source.Triangles.Length != source.TriangleCount)
                throw new InvalidDataException($"NIF hardware skin partition {index} has incomplete tables.");
            var influences = source.WeightsPerVertex <= 4 ? 4 : 8;
            var bones = new int[checked(source.VertexCount * influences)];
            var weights = new float[bones.Length];
            var localVertices = new HashSet<ushort>();
            for (var row = 0; row < source.VertexCount; row++)
            {
                var vertex = source.VertexMap[row];
                if (vertex >= vertexCount || !localVertices.Add(vertex) ||
                    source.VertexWeights[row].Length != source.WeightsPerVertex ||
                    source.BoneIndices[row].Length != source.WeightsPerVertex)
                    throw new InvalidDataException($"NIF hardware skin partition {index} has invalid vertex mapping.");
                covered[vertex] = true;
                var sum = 0.0f;
                for (var influence = 0; influence < source.WeightsPerVertex; influence++)
                {
                    var bone = source.BoneIndices[row][influence];
                    var weight = source.VertexWeights[row][influence];
                    if (bone >= source.Bones.Length || !float.IsFinite(weight) || weight < 0.0f)
                        throw new InvalidDataException($"NIF hardware skin partition {index} has invalid influence data.");
                    bones[row * influences + influence] = bone;
                    weights[row * influences + influence] = weight;
                    sum += weight;
                }
                // Validate exported normalization without changing a source weight.
                if (MathF.Abs(sum - 1.0f) > 0.001f)
                    throw new InvalidDataException($"NIF hardware skin partition {index} has non-normalized weights.");
            }
            output.Add(new FalloutNifHardwareSkinPartition(index, source.VertexMap, source.Bones,
                bones, weights, influences, source.Triangles,
                instance.BodyPartitions.Length == 0 ? null : instance.BodyPartitions[index]));
        }
        if (covered.Any(value => !value))
            throw new InvalidDataException("NIF hardware skin partitions omit source geometry vertices.");
        return output;
    }
}
