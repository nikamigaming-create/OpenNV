namespace OpenNV.Runtime.Formats.Gamebryo;

internal sealed record FalloutNifSkinInstance(
    FalloutNifBlock Block,
    int Data,
    int SkinPartition,
    int SkeletonRoot,
    int[] Bones,
    FalloutNifBodyPartition[] BodyPartitions) : FalloutNifObject(Block);

internal sealed record FalloutNifSkinData(
    FalloutNifBlock Block,
    FalloutNifTransform SkinTransform,
    bool HasVertexWeights,
    FalloutNifSkinBoneData[] Bones) : FalloutNifObject(Block);

internal sealed record FalloutNifSkinBoneData(
    FalloutNifTransform SkinTransform,
    FalloutNifVector3 BoundingSphereCenter,
    float BoundingSphereRadius,
    FalloutNifSkinWeight[] VertexWeights);

internal readonly record struct FalloutNifSkinWeight(ushort Vertex, float Weight);

internal sealed record FalloutNifSkinPartition(
    FalloutNifBlock Block,
    FalloutNifSkinPartitionBlock[] Partitions) : FalloutNifObject(Block);

internal sealed record FalloutNifSkinPartitionBlock(
    ushort VertexCount,
    ushort TriangleCount,
    ushort[] Bones,
    ushort WeightsPerVertex,
    ushort[] VertexMap,
    float[][] VertexWeights,
    ushort[] StripLengths,
    FalloutNifTriangle[] Triangles,
    byte[][] BoneIndices,
    ushort[][]? Strips = null);

internal readonly record struct FalloutNifBodyPartition(ushort Flags, ushort BodyPart);

internal sealed record FalloutNifOneBoneBinding(
    int SkeletonRoot,
    int Bone,
    FalloutNifTransform InverseBind,
    int[] BoneIndices,
    float[] Weights);

internal static class FalloutNifOneBoneSkin
{
    private const int InfluencesPerVertex = 4;
    private const int RotationMatrixElementCount = 9;
    private const int RotationMatrixLastDiagonalIndex = 8;
    private const float WeightTolerance = 0.0001f;
    private const ushort EditorVisibleStartBoneSetFlags = 0x0101;
    private const ushort HeadBodyPart = 1;

    internal static FalloutNifOneBoneBinding Validate(
        FalloutNifSkinInstance instance,
        FalloutNifSkinData data,
        FalloutNifSkinPartition partition,
        int vertexCount)
    {
        if (vertexCount <= 0)
            throw new InvalidDataException("NIF skinned geometry has no vertices.");
        if (instance.Data != data.Block.Index || instance.SkinPartition != partition.Block.Index)
            throw new InvalidDataException("NIF skin-instance references do not match decoded skin data.");
        if (instance.SkeletonRoot < 0 || instance.Bones.Length != 1 || data.Bones.Length != 1)
            throw new NotSupportedException(
                $"NIF skin {instance.Block.Index} is outside the proven one-bone runtime contract.");
        if (!data.HasVertexWeights)
            throw new NotSupportedException(
                $"NIF skin data {data.Block.Index} does not carry explicit vertex weights.");
        if (instance.BodyPartitions.Length != 1 || partition.Partitions.Length != 1)
            throw new NotSupportedException(
                $"NIF skin {instance.Block.Index} is outside the proven one-partition runtime contract.");
        if (instance.BodyPartitions[0] != new FalloutNifBodyPartition(
                EditorVisibleStartBoneSetFlags, HeadBodyPart) || !IsIdentity(data.SkinTransform))
            throw new NotSupportedException(
                $"NIF skin {instance.Block.Index} is outside the proven head bind contract.");

        var source = partition.Partitions[0];
        if (source.VertexCount != vertexCount || source.Bones.Length != 1 || source.Bones[0] != 0 ||
            source.WeightsPerVertex != InfluencesPerVertex ||
            source.VertexMap.Length != vertexCount || source.VertexWeights.Length != vertexCount ||
            source.BoneIndices.Length != vertexCount || source.StripLengths.Length != 0 ||
            source.Triangles.Length != source.TriangleCount)
            throw new NotSupportedException(
                $"NIF skin partition {partition.Block.Index} is outside the proven one-bone layout.");

        var mapped = new bool[vertexCount];
        var boneIndices = new int[checked(vertexCount * InfluencesPerVertex)];
        var weights = new float[boneIndices.Length];
        for (var row = 0; row < vertexCount; ++row)
        {
            var vertex = source.VertexMap[row];
            if (vertex >= vertexCount || mapped[vertex])
                throw new InvalidDataException(
                    $"NIF skin partition {partition.Block.Index} has an invalid or duplicate vertex map.");
            mapped[vertex] = true;
            if (source.VertexWeights[row].Length != InfluencesPerVertex ||
                source.BoneIndices[row].Length != InfluencesPerVertex)
                throw new InvalidDataException(
                    $"NIF skin partition {partition.Block.Index} has a ragged influence table.");
            var total = 0.0f;
            for (var influence = 0; influence < InfluencesPerVertex; ++influence)
            {
                var weight = source.VertexWeights[row][influence];
                var bone = source.BoneIndices[row][influence];
                if (!float.IsFinite(weight) || weight < 0.0f || bone != 0)
                    throw new InvalidDataException(
                        $"NIF skin partition {partition.Block.Index} has an invalid one-bone influence.");
                var output = vertex * InfluencesPerVertex + influence;
                boneIndices[output] = 0;
                weights[output] = weight;
                total += weight;
            }
            if (MathF.Abs(total - 1.0f) > WeightTolerance)
                throw new InvalidDataException(
                    $"NIF skin partition {partition.Block.Index} has non-normalized vertex weights.");
        }
        if (mapped.Any(value => !value))
            throw new InvalidDataException(
                $"NIF skin partition {partition.Block.Index} does not cover every mesh vertex.");

        var boneWeights = data.Bones[0].VertexWeights;
        if (boneWeights.Length != vertexCount)
            throw new InvalidDataException(
                $"NIF skin data {data.Block.Index} does not cover every mesh vertex.");
        Array.Clear(mapped);
        foreach (var sourceWeight in boneWeights)
        {
            if (sourceWeight.Vertex >= vertexCount || mapped[sourceWeight.Vertex] ||
                !float.IsFinite(sourceWeight.Weight) ||
                MathF.Abs(sourceWeight.Weight - 1.0f) > WeightTolerance)
                throw new InvalidDataException(
                    $"NIF skin data {data.Block.Index} has invalid one-bone vertex weights.");
            mapped[sourceWeight.Vertex] = true;
        }
        if (mapped.Any(value => !value))
            throw new InvalidDataException(
                $"NIF skin data {data.Block.Index} does not cover every mesh vertex.");

        return new FalloutNifOneBoneBinding(
            instance.SkeletonRoot, instance.Bones[0], data.Bones[0].SkinTransform,
            boneIndices, weights);
    }

    private static bool IsIdentity(FalloutNifTransform transform)
    {
        if (transform.RotationRowMajor.Length != RotationMatrixElementCount ||
            MathF.Abs(transform.Translation.X) > WeightTolerance ||
            MathF.Abs(transform.Translation.Y) > WeightTolerance ||
            MathF.Abs(transform.Translation.Z) > WeightTolerance ||
            MathF.Abs(transform.Scale - 1.0f) > WeightTolerance)
            return false;
        for (var index = 0; index < transform.RotationRowMajor.Length; ++index)
        {
            var expected = index is 0 or 4 or RotationMatrixLastDiagonalIndex ? 1.0f : 0.0f;
            if (MathF.Abs(transform.RotationRowMajor[index] - expected) > WeightTolerance)
                return false;
        }
        return true;
    }
}
