using System.Buffers.Binary;
using System.Text;

namespace OpenNV.Runtime.Formats.Gamebryo;

internal sealed class FalloutNifFile
{
    internal const uint Version = 0x14020007;
    internal const uint UserVersion = 11;
    private const byte LittleEndian = 1;
    private const int MaximumTableEntries = 1_000_000;
    private const int MaximumShaderTextureCount = 32;
    private const int FalloutLegacyTextureSlotCount = 9;
    private const int FalloutLegacyTextureSlotCountWithSecondDecal = 10;
    private const uint LinearKeyType = 1;
    private const uint QuadraticKeyType = 2;
    private const uint TbcKeyType = 3;
    private const uint XyzRotationKeyType = 4;
    private const uint ConstantKeyType = 5;
    private const uint AnimationVersion2Legacy = 30;
    private const uint AnimationVersion2Minimum = 31;
    private const uint GeometryVersion2Minimum = 32;
    private const uint AnimationVersion2Alternate = 33;
    private const uint GeometryVersion2Current = 34;
    private const uint AnimationNotesVersion2Minimum = 24;
    private const uint AnimationNotesVersion2Maximum = 28;
    private const uint RagdollConstraintType = 7;
    private const int RotationMatrixValues = 9;
    private const int HavokUnknownPairBytes = 8;
    private const int HavokUnknownFloatPairBytes = 8;
    private const int HavokUnknownSixFloatBytes = 24;
    private const int HavokTransformMatrixValues = 16;
    private static readonly HashSet<uint> SupportedUserVersion2 =
        [AnimationVersion2Legacy, AnimationVersion2Minimum, GeometryVersion2Minimum,
            AnimationVersion2Alternate, GeometryVersion2Current];

    private readonly ReadOnlyMemory<byte> _payload;

    private FalloutNifFile(
        ReadOnlyMemory<byte> payload,
        uint userVersion2,
        IReadOnlyList<string> strings,
        IReadOnlyList<FalloutNifBlock> blocks,
        IReadOnlyList<int> roots)
    {
        _payload = payload;
        UserVersion2 = userVersion2;
        Strings = strings;
        Blocks = blocks;
        Roots = roots;
    }

    internal uint UserVersion2 { get; }
    internal IReadOnlyList<string> Strings { get; }
    internal IReadOnlyList<FalloutNifBlock> Blocks { get; }
    internal IReadOnlyList<int> Roots { get; }

    internal static FalloutNifFile Read(ReadOnlyMemory<byte> payload)
    {
        var cursor = new NifCursor(payload.Span, "NIF");
        var header = cursor.ReadLineAscii("header");
        if (!string.Equals(header, "Gamebryo File Format, Version 20.2.0.7", StringComparison.Ordinal))
            throw new InvalidDataException($"Unsupported NIF header: {header}");
        if (cursor.ReadUInt32("version") != Version)
            throw new InvalidDataException("NIF binary version differs from its 20.2.0.7 header.");
        if (cursor.ReadByte("endian") != LittleEndian)
            throw new InvalidDataException("Only little-endian Fallout NIF files are supported.");
        if (cursor.ReadUInt32("user version") != UserVersion)
            throw new InvalidDataException("Only Bethesda user version 11 NIF files are supported.");
        var blockCount = cursor.ReadCount32("block count", MaximumTableEntries);
        var userVersion2 = cursor.ReadUInt32("user version 2");
        if (!SupportedUserVersion2.Contains(userVersion2))
            throw new InvalidDataException($"Unsupported Fallout NIF user version 2: {userVersion2}");

        for (var index = 0; index < 3; ++index)
            _ = cursor.ReadShortString($"export info {index}");

        var typeCount = cursor.ReadCount16("block type count", ushort.MaxValue);
        if (typeCount == 0)
            throw new InvalidDataException("NIF block type table is empty.");
        var blockTypes = new string[typeCount];
        for (var index = 0; index < blockTypes.Length; ++index)
            blockTypes[index] = cursor.ReadSizedAscii($"block type {index}");

        var typeIndices = new ushort[blockCount];
        for (var index = 0; index < typeIndices.Length; ++index)
        {
            typeIndices[index] = cursor.ReadUInt16($"block type index {index}");
            if (typeIndices[index] >= typeCount)
                throw new InvalidDataException($"NIF block {index} has an invalid type index.");
        }
        var blockSizes = new uint[blockCount];
        for (var index = 0; index < blockSizes.Length; ++index)
            blockSizes[index] = cursor.ReadUInt32($"block size {index}");

        var stringCount = cursor.ReadCount32("string count", MaximumTableEntries);
        var maximumStringBytes = cursor.ReadUInt32("maximum string length");
        var strings = new string[stringCount];
        for (var index = 0; index < strings.Length; ++index)
        {
            strings[index] = cursor.ReadSizedUtf8($"string {index}", maximumStringBytes);
            if ((uint)Encoding.UTF8.GetByteCount(strings[index]) > maximumStringBytes)
                throw new InvalidDataException($"NIF string {index} exceeds the declared maximum length.");
        }

        var groupCount = cursor.ReadCount32("group count", MaximumTableEntries);
        for (var index = 0; index < groupCount; ++index)
            _ = cursor.ReadUInt32($"group size {index}");

        var blocks = new FalloutNifBlock[blockCount];
        for (var index = 0; index < blocks.Length; ++index)
        {
            var size = checked((int)blockSizes[index]);
            var offset = cursor.Offset;
            cursor.Skip(size, $"block {index}");
            blocks[index] = new FalloutNifBlock(index, blockTypes[typeIndices[index]], offset, size);
        }
        var rootCount = cursor.ReadCount32("root count", MaximumTableEntries);
        if (cursor.Remaining != checked(rootCount * sizeof(int)))
            throw new InvalidDataException("Fallout NIF footer root table size is invalid.");
        var roots = new int[rootCount];
        for (var index = 0; index < roots.Length; ++index)
        {
            roots[index] = cursor.ReadInt32($"root {index}");
            RequireReference(roots[index], blocks.Length, $"root {index}", allowNull: false);
        }
        cursor.RequireEnd();
        return new FalloutNifFile(payload, userVersion2, strings, blocks, roots);
    }

    internal FalloutNifObject ReadObject(int blockIndex)
    {
        RequireReference(blockIndex, Blocks.Count, "block", allowNull: false);
        var block = Blocks[blockIndex];
        var cursor = BlockCursor(block);
        FalloutNifObject result = block.TypeName switch
        {
            "NiNode" or "NiBone" or "BSFadeNode" => ReadNode(block, ref cursor),
            "NiAmbientLight" => ReadAmbientLight(block, ref cursor),
            "NiPointLight" => ReadPointLight(block, ref cursor),
            "NiTriShape" or "NiTriStrips" => ReadGeometry(block, ref cursor),
            "NiTriShapeData" => ReadTriShapeData(block, ref cursor),
            "NiTriStripsData" => ReadTriStripsData(block, ref cursor),
            "NiSkinInstance" or "BSDismemberSkinInstance" => ReadSkinInstance(block, ref cursor),
            "NiSkinData" => ReadSkinData(block, ref cursor),
            "NiSkinPartition" => ReadSkinPartition(block, ref cursor),
            "NiControllerSequence" => ReadControllerSequence(block, ref cursor),
            "NiTransformInterpolator" => ReadTransformInterpolator(block, ref cursor),
            "NiBSplineTransformInterpolator" or "NiBSplineCompTransformInterpolator" =>
                ReadSplineTransformInterpolator(block, ref cursor),
            "NiBSplineBasisData" => new FalloutNifSplineBasisData(block,
                cursor.ReadCount32("spline control point count", MaximumTableEntries)),
            "NiBSplineData" => ReadSplineData(block, ref cursor),
            "NiTransformData" => ReadTransformData(block, ref cursor),
            "NiFloatInterpolator" => ReadFloatInterpolator(block, ref cursor),
            "NiFloatData" => ReadFloatData(block, ref cursor),
            "NiBoolInterpolator" => new FalloutNifBoolInterpolator(block, cursor.ReadByte("bool value"), ReadReference(ref cursor, "bool data")),
            "NiBoolData" => ReadBoolData(block, ref cursor),
            "NiVisController" => new FalloutNifVisibilityController(block, ReadTimeController(ref cursor, "visibility"), ReadReference(ref cursor, "visibility interpolator")),
            "NiGeomMorpherController" => ReadMorphController(block, ref cursor),
            "NiMorphData" => ReadMorphData(block, ref cursor),
            "NiPoint3Interpolator" => ReadPoint3Interpolator(block, ref cursor),
            "NiPosData" => ReadPositionData(block, ref cursor),
            "NiBlendFloatInterpolator" => ReadBlendFloatInterpolator(block, ref cursor),
            "NiBlendPoint3Interpolator" => ReadBlendPoint3Interpolator(block, ref cursor),
            "NiTextureTransformController" => ReadTextureTransformController(block, ref cursor),
            "NiMaterialColorController" => ReadMaterialColorController(block, ref cursor),
            "NiTransformController" => ReadTransformController(block, ref cursor),
            "NiFloatExtraDataController" => new FalloutNifFloatExtraDataController(block,
                ReadTimeController(ref cursor, "float extra-data controller"),
                ReadReference(ref cursor, "float extra-data interpolator"),
                ReadStringReference(ref cursor, "float extra-data name")),
            "NiBSBoneLODController" => ReadBoneLodController(block, ref cursor),
            "NiControllerManager" => ReadControllerManager(block, ref cursor),
            "NiMultiTargetTransformController" => ReadMultiTargetTransformController(block, ref cursor),
            "NiTextKeyExtraData" => ReadTextKeyExtraData(block, ref cursor),
            "NiDefaultAVObjectPalette" => ReadDefaultAvObjectPalette(block, ref cursor),
            "BSShaderPPLightingProperty" => ReadShaderProperty(block, ref cursor),
            "BSShaderTextureSet" => ReadShaderTextureSet(block, ref cursor),
            "NiMaterialProperty" => ReadMaterialProperty(block, ref cursor),
            "BSXFlags" => ReadBsxFlags(block, ref cursor),
            "NiIntegerExtraData" => ReadIntegerExtraData(block, ref cursor),
            "NiFloatExtraData" => ReadFloatExtraData(block, ref cursor),
            "BSDecalPlacementVectorExtraData" => ReadDecalPlacementExtraData(block, ref cursor),
            "NiStringExtraData" => ReadStringExtraData(block, ref cursor),
            "BSBound" => ReadBound(block, ref cursor),
            "BSFurnitureMarker" => ReadFurnitureMarker(block, ref cursor),
            "bhkCollisionObject" => ReadCollisionObject(block, ref cursor),
            "bhkBlendCollisionObject" => ReadBlendCollisionObject(block, ref cursor),
            "bhkRigidBody" or "bhkRigidBodyT" => ReadRigidBody(block, ref cursor),
            "bhkMoppBvTreeShape" => ReadMoppShape(block, ref cursor),
            "bhkPackedNiTriStripsShape" => ReadPackedShape(block, ref cursor),
            "hkPackedNiTriStripsData" => ReadPackedData(block, ref cursor),
            "bhkBoxShape" => ReadBoxShape(block, ref cursor),
            "bhkSphereShape" => ReadSphereShape(block, ref cursor),
            "bhkCapsuleShape" => ReadCapsuleShape(block, ref cursor),
            "bhkConvexVerticesShape" => ReadConvexVerticesShape(block, ref cursor),
            "bhkListShape" => ReadListShape(block, ref cursor),
            "bhkConvexTransformShape" => ReadConvexTransformShape(block, ref cursor),
            "BSShaderNoLightingProperty" => ReadNoLightingProperty(block, ref cursor),
            "TileShaderProperty" => ReadTileShaderProperty(block, ref cursor),
            "NiAlphaProperty" => ReadAlphaProperty(block, ref cursor),
            "NiStencilProperty" => ReadStencilProperty(block, ref cursor),
            "NiTexturingProperty" => ReadTexturingProperty(block, ref cursor),
            "NiSourceTexture" => ReadSourceTexture(block, ref cursor),
            _ => throw new NotSupportedException(
                $"NIF block {blockIndex} type {block.TypeName} has no native runtime decoder."),
        };
        cursor.RequireEnd();
        return result;
    }

    internal FalloutNifNode ReadNode(int blockIndex) =>
        ReadObject(blockIndex) as FalloutNifNode ??
        throw new InvalidDataException($"NIF block {blockIndex} is not a supported node.");

    internal FalloutNifConstraintHeader ReadConstraintHeader(int blockIndex)
    {
        RequireReference(blockIndex, Blocks.Count, "constraint block", allowNull: false);
        var block = Blocks[blockIndex];
        if (block.TypeName is not ("bhkRagdollConstraint" or "bhkMalleableConstraint"))
            throw new NotSupportedException(
                $"NIF constraint block {blockIndex} type {block.TypeName} has no identity contract.");
        var cursor = BlockCursor(block);
        var entityCount = cursor.ReadUInt32("constraint entity count");
        if (entityCount != 2)
            throw new InvalidDataException(
                $"NIF constraint block {blockIndex} must join exactly two entities.");
        var entityA = ReadReference(ref cursor, "constraint entity A");
        var entityB = ReadReference(ref cursor, "constraint entity B");
        if (entityA == -1 || entityB == -1 ||
            Blocks[entityA].TypeName is not ("bhkRigidBody" or "bhkRigidBodyT") ||
            Blocks[entityB].TypeName is not ("bhkRigidBody" or "bhkRigidBodyT"))
            throw new InvalidDataException(
                $"NIF constraint block {blockIndex} does not join two rigid bodies.");
        var priority = cursor.ReadUInt32("constraint priority");
        if (priority is not (1U or 3U))
            throw new NotSupportedException(
                $"NIF constraint block {blockIndex} priority {priority} is unsupported.");
        var wrappedType = RagdollConstraintType;
        if (block.TypeName == "bhkMalleableConstraint")
        {
            wrappedType = cursor.ReadUInt32("malleable wrapped constraint type");
            var nestedCount = cursor.ReadUInt32("malleable nested constraint entity count");
            var nestedA = ReadReference(ref cursor, "malleable nested constraint entity A");
            var nestedB = ReadReference(ref cursor, "malleable nested constraint entity B");
            var nestedPriority = cursor.ReadUInt32("malleable nested constraint priority");
            var nestedUsesOuterIdentity = nestedA == -1 && nestedB == -1;
            if (nestedCount != entityCount ||
                !nestedUsesOuterIdentity && (nestedA != entityA || nestedB != entityB) ||
                nestedPriority != priority)
                throw new InvalidDataException(
                    $"NIF malleable constraint block {blockIndex} duplicates inconsistent entity identity: " +
                    $"outer={entityCount}/{entityA}/{entityB}/{priority} " +
                    $"inner={nestedCount}/{nestedA}/{nestedB}/{nestedPriority} type={wrappedType}.");
        }
        return new FalloutNifConstraintHeader(
            block, wrappedType, entityA, entityB, priority, cursor.Remaining);
    }

    internal FalloutNifGeometry ReadGeometry(int blockIndex) =>
        ReadObject(blockIndex) as FalloutNifGeometry ??
        throw new InvalidDataException($"NIF block {blockIndex} is not supported geometry.");

    internal FalloutNifMeshData ReadMeshData(int blockIndex) =>
        ReadObject(blockIndex) as FalloutNifMeshData ??
        throw new InvalidDataException($"NIF block {blockIndex} is not supported mesh data.");

    internal FalloutNifControllerSequence ReadControllerSequence(int blockIndex) =>
        ReadObject(blockIndex) as FalloutNifControllerSequence ??
        throw new InvalidDataException($"NIF block {blockIndex} is not a controller sequence.");

    private FalloutNifSkinInstance ReadSkinInstance(FalloutNifBlock block, ref NifCursor cursor)
    {
        var data = ReadReference(ref cursor, "skin data");
        var partition = ReadReference(ref cursor, "skin partition");
        var skeletonRoot = ReadReference(ref cursor, "skeleton root");
        var bones = ReadReferences(ref cursor, "skin bones");
        var bodyPartitionCount = block.TypeName == "BSDismemberSkinInstance"
            ? cursor.ReadInt32("body partition count") : 0;
        if (bodyPartitionCount < 0 || bodyPartitionCount > MaximumTableEntries)
            throw new InvalidDataException(
                $"NIF skin {block.Index} has an invalid body-partition count.");
        var bodyPartitions = new FalloutNifBodyPartition[bodyPartitionCount];
        for (var index = 0; index < bodyPartitions.Length; ++index)
            bodyPartitions[index] = new FalloutNifBodyPartition(
                cursor.ReadUInt16($"body partition {index} flags"),
                cursor.ReadUInt16($"body partition {index} body part"));
        return new FalloutNifSkinInstance(
            block, data, partition, skeletonRoot, bones, bodyPartitions);
    }

    private FalloutNifSkinData ReadSkinData(FalloutNifBlock block, ref NifCursor cursor)
    {
        var transform = ReadSkinTransform(ref cursor, "skin transform");
        var boneCount = cursor.ReadCount32("skin-data bone count", MaximumTableEntries);
        var hasVertexWeights = cursor.ReadBoolean("skin-data has vertex weights");
        var bones = new FalloutNifSkinBoneData[boneCount];
        for (var boneIndex = 0; boneIndex < bones.Length; ++boneIndex)
        {
            var boneTransform = ReadSkinTransform(ref cursor, $"skin bone {boneIndex} transform");
            var center = ReadVector(ref cursor, $"skin bone {boneIndex} bounding sphere center");
            var radius = cursor.ReadFiniteSingle($"skin bone {boneIndex} bounding sphere radius");
            if (radius < 0.0f)
                throw new InvalidDataException(
                    $"NIF skin data {block.Index} has a negative bone bounding sphere.");
            var weightCount = cursor.ReadUInt16($"skin bone {boneIndex} vertex count");
            var weights = hasVertexWeights ? new FalloutNifSkinWeight[weightCount] : [];
            for (var weightIndex = 0; weightIndex < weights.Length; ++weightIndex)
                weights[weightIndex] = new FalloutNifSkinWeight(
                    cursor.ReadUInt16($"skin bone {boneIndex} weight {weightIndex} vertex"),
                    cursor.ReadFiniteSingle($"skin bone {boneIndex} weight {weightIndex} value"));
            bones[boneIndex] = new FalloutNifSkinBoneData(
                boneTransform, center, radius, weights);
        }
        return new FalloutNifSkinData(block, transform, hasVertexWeights, bones);
    }

    private FalloutNifSkinPartition ReadSkinPartition(FalloutNifBlock block, ref NifCursor cursor)
    {
        var count = cursor.ReadCount32("skin partition count", MaximumTableEntries);
        var partitions = new FalloutNifSkinPartitionBlock[count];
        for (var partitionIndex = 0; partitionIndex < partitions.Length; ++partitionIndex)
        {
            var vertexCount = cursor.ReadUInt16($"skin partition {partitionIndex} vertex count");
            var triangleCount = cursor.ReadUInt16($"skin partition {partitionIndex} triangle count");
            var boneCount = cursor.ReadUInt16($"skin partition {partitionIndex} bone count");
            var stripCount = cursor.ReadUInt16($"skin partition {partitionIndex} strip count");
            var weightsPerVertex = cursor.ReadUInt16(
                $"skin partition {partitionIndex} weights per vertex");
            var bones = new ushort[boneCount];
            for (var index = 0; index < bones.Length; ++index)
                bones[index] = cursor.ReadUInt16($"skin partition {partitionIndex} bone {index}");

            var hasVertexMap = cursor.ReadBoolean($"skin partition {partitionIndex} has vertex map");
            var vertexMap = hasVertexMap ? new ushort[vertexCount] : [];
            for (var index = 0; index < vertexMap.Length; ++index)
                vertexMap[index] = cursor.ReadUInt16(
                    $"skin partition {partitionIndex} vertex map {index}");

            var hasWeights = cursor.ReadBoolean($"skin partition {partitionIndex} has weights");
            var vertexWeights = hasWeights ? new float[vertexCount][] : [];
            for (var vertex = 0; vertex < vertexWeights.Length; ++vertex)
            {
                vertexWeights[vertex] = new float[weightsPerVertex];
                for (var influence = 0; influence < weightsPerVertex; ++influence)
                    vertexWeights[vertex][influence] = cursor.ReadFiniteSingle(
                        $"skin partition {partitionIndex} vertex {vertex} weight {influence}");
            }

            var stripLengths = new ushort[stripCount];
            for (var index = 0; index < stripLengths.Length; ++index)
                stripLengths[index] = cursor.ReadUInt16(
                    $"skin partition {partitionIndex} strip length {index}");
            var hasFaces = cursor.ReadBoolean($"skin partition {partitionIndex} has faces");
            var strips = new ushort[hasFaces ? stripCount : 0][];
            var expanded = new List<FalloutNifTriangle>();
            for (var strip = 0; strip < strips.Length; ++strip)
            {
                strips[strip] = new ushort[stripLengths[strip]];
                for (var point = 0; point < strips[strip].Length; ++point)
                {
                    var vertex = cursor.ReadUInt16($"skin partition {partitionIndex} strip {strip}/{point}");
                    if (vertex >= vertexCount)
                        throw new InvalidDataException($"NIF skin partition {partitionIndex} strip index is invalid.");
                    strips[strip][point] = vertex;
                    if (point >= 2)
                        expanded.Add((point & 1) == 0
                            ? new FalloutNifTriangle(strips[strip][point - 2], strips[strip][point - 1], vertex)
                            : new FalloutNifTriangle(strips[strip][point - 1], strips[strip][point - 2], vertex));
                }
            }
            var triangles = hasFaces && stripCount == 0 ? new FalloutNifTriangle[triangleCount]
                : expanded.ToArray();
            if (hasFaces && stripCount != 0 && triangles.Length != triangleCount)
                throw new InvalidDataException(
                    $"NIF skin partition {partitionIndex} strip triangle count differs from its declaration.");
            if (hasFaces && stripCount == 0)
                for (var index = 0; index < triangles.Length; ++index)
                    triangles[index] = ReadTriangle(
                        ref cursor, vertexCount, $"skin partition {partitionIndex} triangle {index}");

            var hasBoneIndices = cursor.ReadBoolean(
                $"skin partition {partitionIndex} has bone indices");
            var boneIndices = hasBoneIndices ? new byte[vertexCount][] : [];
            for (var vertex = 0; vertex < boneIndices.Length; ++vertex)
            {
                boneIndices[vertex] = new byte[weightsPerVertex];
                for (var influence = 0; influence < weightsPerVertex; ++influence)
                    boneIndices[vertex][influence] = cursor.ReadByte(
                        $"skin partition {partitionIndex} vertex {vertex} bone {influence}");
            }
            partitions[partitionIndex] = new FalloutNifSkinPartitionBlock(
                vertexCount, triangleCount, bones, weightsPerVertex, vertexMap,
                vertexWeights, stripLengths, triangles, boneIndices, strips);
        }
        return new FalloutNifSkinPartition(block, partitions);
    }

    private static FalloutNifTransform ReadSkinTransform(ref NifCursor cursor, string label)
    {
        var rotation = new float[RotationMatrixValues];
        for (var index = 0; index < rotation.Length; ++index)
            rotation[index] = cursor.ReadFiniteSingle($"{label} rotation {index}");
        var translation = ReadVector(ref cursor, $"{label} translation");
        var scale = cursor.ReadFiniteSingle($"{label} scale");
        if (scale <= 0.0f)
            throw new InvalidDataException($"NIF {label} scale must be positive.");
        return new FalloutNifTransform(translation, rotation, scale);
    }

    private FalloutNifNode ReadNode(FalloutNifBlock block, ref NifCursor cursor)
    {
        var av = ReadAvObject(ref cursor, block.TypeName);
        var children = ReadReferences(ref cursor, "children");
        var effects = ReadReferences(ref cursor, "effects");
        return new FalloutNifNode(block, av.Name, av.Transform, av.Flags, av.Controller,
            av.ExtraData, av.Properties, av.CollisionObject, children, effects);
    }

    private FalloutNifAmbientLight ReadAmbientLight(FalloutNifBlock block, ref NifCursor cursor)
    {
        var av = ReadAvObject(ref cursor, block.TypeName);
        var switchState = cursor.ReadBoolean("ambient light switch state");
        var affectedNodes = ReadReferences(ref cursor, "ambient light affected nodes");
        return new FalloutNifAmbientLight(
            block, av.Name, av.Transform, av.Flags, av.Controller, av.ExtraData,
            av.Properties, av.CollisionObject, switchState, affectedNodes,
            cursor.ReadFiniteSingle("ambient light dimmer"),
            ReadColor3(ref cursor, "ambient light ambient color"),
            ReadColor3(ref cursor, "ambient light diffuse color"),
            ReadColor3(ref cursor, "ambient light specular color"));
    }

    private FalloutNifPointLight ReadPointLight(FalloutNifBlock block, ref NifCursor cursor) => new(
        block, ReadAmbientLight(block, ref cursor),
        cursor.ReadFiniteSingle("point light constant attenuation"),
        cursor.ReadFiniteSingle("point light linear attenuation"),
        cursor.ReadFiniteSingle("point light quadratic attenuation"));

    private FalloutNifGeometry ReadGeometry(FalloutNifBlock block, ref NifCursor cursor)
    {
        var av = ReadAvObject(ref cursor, block.TypeName);
        var data = ReadReference(ref cursor, "geometry data");
        var skin = ReadReference(ref cursor, "skin instance");
        var materialCount = cursor.ReadCount32("material count", MaximumTableEntries);
        var materialNames = new string[materialCount];
        for (var index = 0; index < materialNames.Length; ++index)
            materialNames[index] = ReadStringReference(ref cursor, $"material name {index}");
        var materialExtraData = new int[materialCount];
        for (var index = 0; index < materialExtraData.Length; ++index)
            materialExtraData[index] = cursor.ReadInt32($"material extra data {index}");
        var activeMaterial = cursor.ReadInt32("active material");
        var dirty = cursor.ReadBoolean("dirty flag");
        return new FalloutNifGeometry(block, av.Name, av.Transform, av.Flags, av.Controller,
            av.ExtraData, av.Properties, av.CollisionObject, data, skin, materialNames, materialExtraData,
            activeMaterial, dirty);
    }

    private FalloutNifMeshData ReadTriShapeData(FalloutNifBlock block, ref NifCursor cursor) =>
        ReadMeshDataWithUvRecovery(block, ref cursor, strips: false);

    private FalloutNifMeshData ReadTriStripsData(FalloutNifBlock block, ref NifCursor cursor) =>
        ReadMeshDataWithUvRecovery(block, ref cursor, strips: true);

    private FalloutNifMeshData ReadMeshDataWithUvRecovery(
        FalloutNifBlock block,
        ref NifCursor cursor,
        bool strips)
    {
        var start = cursor;
        var prefix = ReadMeshPrefix(ref cursor);
        if (prefix.StoredUvSets <= 1)
            return ReadMeshTail(block, ref cursor, prefix, prefix.StoredUvSets, strips);
        FalloutNifMeshData? match = null;
        InvalidDataException? lastError = null;
        for (byte uvSets = 0; uvSets <= 1; ++uvSets)
        {
            try
            {
                var candidate = start;
                var candidatePrefix = ReadMeshPrefix(ref candidate);
                var parsed = ReadMeshTail(block, ref candidate, candidatePrefix, uvSets, strips);
                candidate.RequireEnd();
                if (match is not null)
                    throw new InvalidDataException(
                        $"NIF block {block.Index} UV-set recovery is ambiguous.");
                match = parsed;
                cursor = candidate;
            }
            catch (InvalidDataException error)
            {
                lastError = error;
            }
        }
        return match ?? throw new InvalidDataException(
            $"NIF block {block.Index} UV-set recovery has no exact layout: {lastError?.Message}", lastError);
    }

    private static MeshPrefix ReadMeshPrefix(ref NifCursor cursor)
    {
        _ = cursor.ReadInt32("geometry identifier");
        var vertexCount = cursor.ReadUInt16("vertex count");
        _ = cursor.ReadByte("keep flags");
        _ = cursor.ReadByte("compress flags");
        var hasVertices = cursor.ReadBoolean("has vertices");
        var vertices = hasVertices ? ReadVectors(ref cursor, vertexCount, "vertices") : [];
        var storedUvSets = cursor.ReadByte("UV set count");
        var extraVectorFlags = cursor.ReadByte("extra-vector flags");
        return new MeshPrefix(vertexCount, vertices, storedUvSets, extraVectorFlags);
    }

    private FalloutNifMeshData ReadMeshTail(
        FalloutNifBlock block,
        ref NifCursor cursor,
        MeshPrefix prefix,
        byte uvSets,
        bool strips)
    {
        var hasNormals = cursor.ReadBoolean("has normals");
        var normals = hasNormals ? ReadVectors(ref cursor, prefix.VertexCount, "normals") : [];
        var hasTangents = hasNormals && (prefix.ExtraVectorFlags & 0x10) != 0;
        var tangents = hasTangents ? ReadVectors(ref cursor, prefix.VertexCount, "tangents") : [];
        var bitangents = hasTangents ? ReadVectors(ref cursor, prefix.VertexCount, "bitangents") : [];
        var center = ReadVector(ref cursor, "center");
        var radius = cursor.ReadFiniteSingle("radius");
        if (radius < 0)
            throw new InvalidDataException("NIF geometry radius is negative.");
        var hasColors = cursor.ReadBoolean("has vertex colors");
        var colors = new FalloutNifColor[hasColors ? prefix.VertexCount : 0];
        for (var index = 0; index < colors.Length; ++index)
            colors[index] = new FalloutNifColor(
                cursor.ReadFiniteSingle($"color {index} r"), cursor.ReadFiniteSingle($"color {index} g"),
                cursor.ReadFiniteSingle($"color {index} b"), cursor.ReadFiniteSingle($"color {index} a"));
        var textureCoordinates = new FalloutNifTexCoord[uvSets][];
        for (var set = 0; set < textureCoordinates.Length; ++set)
        {
            textureCoordinates[set] = new FalloutNifTexCoord[prefix.VertexCount];
            for (var index = 0; index < prefix.VertexCount; ++index)
                textureCoordinates[set][index] = new FalloutNifTexCoord(
                    cursor.ReadFiniteSingle($"UV {set}/{index} u"),
                    cursor.ReadFiniteSingle($"UV {set}/{index} v"));
        }
        var consistency = cursor.ReadUInt16("consistency flags");
        var additionalData = ReadReference(ref cursor, "additional geometry data");
        var triangleCount = cursor.ReadUInt16("triangle count");
        var triangles = strips
            ? ReadStrips(ref cursor, triangleCount, prefix.VertexCount)
            : ReadTriangles(ref cursor, triangleCount, prefix.VertexCount);
        return new FalloutNifMeshData(block, prefix.Vertices, normals, tangents, bitangents,
            colors, textureCoordinates, triangles, center, radius, consistency, additionalData,
            prefix.StoredUvSets, uvSets);
    }

    private static FalloutNifTriangle[] ReadTriangles(
        ref NifCursor cursor,
        ushort triangleCount,
        ushort vertexCount)
    {
        var pointCount = cursor.ReadUInt32("triangle point count");
        if (pointCount != checked((uint)triangleCount * 3))
            throw new InvalidDataException("NIF triangle point count differs from triangle count.");
        var hasTriangles = cursor.ReadBoolean("has triangles");
        var triangles = new FalloutNifTriangle[hasTriangles ? triangleCount : 0];
        for (var index = 0; index < triangles.Length; ++index)
            triangles[index] = ReadTriangle(ref cursor, vertexCount, $"triangle {index}");
        var matchGroupCount = cursor.ReadUInt16("match group count");
        for (var group = 0; group < matchGroupCount; ++group)
        {
            var count = cursor.ReadUInt16($"match group {group} count");
            for (var index = 0; index < count; ++index)
            {
                var vertex = cursor.ReadUInt16($"match group {group}/{index}");
                if (vertex >= vertexCount)
                    throw new InvalidDataException("NIF match group references an invalid vertex.");
            }
        }
        return triangles;
    }

    private static FalloutNifTriangle[] ReadStrips(
        ref NifCursor cursor,
        ushort declaredTriangleCount,
        ushort vertexCount)
    {
        var stripCount = cursor.ReadUInt16("strip count");
        var lengths = new ushort[stripCount];
        for (var index = 0; index < lengths.Length; ++index)
            lengths[index] = cursor.ReadUInt16($"strip length {index}");
        var hasPoints = cursor.ReadBoolean("has strip points");
        var triangles = new List<FalloutNifTriangle>(declaredTriangleCount);
        for (var strip = 0; strip < lengths.Length; ++strip)
        {
            if (!hasPoints)
                continue;
            var points = new ushort[lengths[strip]];
            for (var index = 0; index < points.Length; ++index)
            {
                points[index] = cursor.ReadUInt16($"strip {strip} point {index}");
                if (points[index] >= vertexCount)
                    throw new InvalidDataException("NIF triangle strip references an invalid vertex.");
            }
            for (var index = 2; index < points.Length; ++index)
            {
                var a = points[index - 2];
                var b = points[index - 1];
                var c = points[index];
                if ((index & 1) != 0)
                    (a, b) = (b, a);
                triangles.Add(new FalloutNifTriangle(a, b, c));
            }
        }
        if (triangles.Count != declaredTriangleCount)
            throw new InvalidDataException(
                $"NIF decoded strip triangle count differs: expected={declaredTriangleCount} actual={triangles.Count}");
        return [.. triangles];
    }

    private FalloutNifControllerSequence ReadControllerSequence(
        FalloutNifBlock block,
        ref NifCursor cursor)
    {
        var name = ReadStringReference(ref cursor, "sequence name");
        var controlledCount = cursor.ReadCount32("controlled block count", MaximumTableEntries);
        _ = cursor.ReadUInt32("sequence unknown integer");
        var links = new FalloutNifControllerLink[controlledCount];
        for (var index = 0; index < links.Length; ++index)
        {
            var interpolator = ReadReference(ref cursor, $"controlled {index} interpolator");
            var controller = ReadReference(ref cursor, $"controlled {index} controller");
            var priority = cursor.ReadByte($"controlled {index} priority");
            var nodeName = ReadStringReference(ref cursor, $"controlled {index} node name");
            var propertyType = ReadStringReference(ref cursor, $"controlled {index} property type");
            var controllerType = ReadStringReference(ref cursor, $"controlled {index} controller type");
            var variable1 = ReadStringReference(ref cursor, $"controlled {index} variable 1");
            var variable2 = ReadStringReference(ref cursor, $"controlled {index} variable 2");
            links[index] = new FalloutNifControllerLink(
                nodeName, propertyType, controllerType, variable1, variable2,
                interpolator, controller, priority);
        }
        var weight = cursor.ReadFiniteSingle("sequence weight");
        var textKeys = ReadReference(ref cursor, "text keys");
        var cycle = cursor.ReadUInt32("cycle type");
        var frequency = cursor.ReadFiniteSingle("sequence frequency");
        var start = cursor.ReadFiniteSingle("sequence start");
        var stop = cursor.ReadFiniteSingle("sequence stop");
        if (frequency <= 0 || start > stop)
            throw new InvalidDataException("NIF controller sequence timing is invalid.");
        var manager = ReadReference(ref cursor, "sequence manager");
        var targetName = ReadStringReference(ref cursor, "sequence target name");
        var animNotes = -1;
        short? unknownShort = null;
        if (UserVersion2 is >= AnimationNotesVersion2Minimum and <= AnimationNotesVersion2Maximum)
            animNotes = ReadReference(ref cursor, "animation notes");
        else if (UserVersion2 > AnimationNotesVersion2Maximum)
            unknownShort = unchecked((short)cursor.ReadUInt16("sequence unknown short"));
        return new FalloutNifControllerSequence(block, name, links, weight, textKeys,
            cycle, frequency, start, stop, manager, targetName, animNotes, unknownShort);
    }

    private FalloutNifTransformInterpolator ReadTransformInterpolator(
        FalloutNifBlock block,
        ref NifCursor cursor)
    {
        var translation = ReadVector(ref cursor, "interpolator translation");
        var rotation = ReadQuaternion(ref cursor, "interpolator rotation");
        var scale = cursor.ReadFiniteSingle("interpolator scale");
        var data = ReadReference(ref cursor, "transform data");
        return new FalloutNifTransformInterpolator(block, translation, rotation, scale, data);
    }

    private FalloutNifSplineTransformInterpolator ReadSplineTransformInterpolator(
        FalloutNifBlock block,
        ref NifCursor cursor)
    {
        var start = cursor.ReadFiniteSingle("spline start time");
        var stop = cursor.ReadFiniteSingle("spline stop time");
        var data = ReadReference(ref cursor, "spline data");
        var basis = ReadReference(ref cursor, "spline basis");
        var translation = ReadVector(ref cursor, "spline translation");
        var rotation = ReadQuaternion(ref cursor, "spline rotation");
        var scale = cursor.ReadFiniteSingle("spline scale");
        var translationHandle = cursor.ReadUInt32("spline translation handle");
        var rotationHandle = cursor.ReadUInt32("spline rotation handle");
        var scaleHandle = cursor.ReadUInt32("spline scale handle");
        var compact = block.TypeName == "NiBSplineCompTransformInterpolator";
        var translationOffset = compact ? cursor.ReadFiniteSingle("spline translation offset") : 0.0f;
        var translationRange = compact ? cursor.ReadFiniteSingle("spline translation half range") : 1.0f;
        var rotationOffset = compact ? cursor.ReadFiniteSingle("spline rotation offset") : 0.0f;
        var rotationRange = compact ? cursor.ReadFiniteSingle("spline rotation half range") : 1.0f;
        var scaleOffset = compact ? cursor.ReadFiniteSingle("spline scale offset") : 0.0f;
        var scaleRange = compact ? cursor.ReadFiniteSingle("spline scale half range") : 1.0f;
        return new FalloutNifSplineTransformInterpolator(block, start, stop, data, basis,
            translation, rotation, scale, translationHandle, rotationHandle, scaleHandle, compact,
            translationOffset, translationRange, rotationOffset, rotationRange, scaleOffset, scaleRange);
    }

    private static FalloutNifSplineData ReadSplineData(FalloutNifBlock block, ref NifCursor cursor)
    {
        var floats = new float[cursor.ReadCount32("float control point count", MaximumTableEntries)];
        for (var index = 0; index < floats.Length; index++)
            floats[index] = cursor.ReadFiniteSingle($"float control point {index}");
        var compact = new short[cursor.ReadCount32("compact control point count", MaximumTableEntries)];
        for (var index = 0; index < compact.Length; index++)
            compact[index] = unchecked((short)cursor.ReadUInt16($"compact control point {index}"));
        return new FalloutNifSplineData(block, floats, compact);
    }

    private FalloutNifFloatInterpolator ReadFloatInterpolator(
        FalloutNifBlock block,
        ref NifCursor cursor) => new(
        block,
        cursor.ReadFiniteSingle("float interpolator value"),
        ReadReference(ref cursor, "float interpolator data"));

    private FalloutNifFloatData ReadFloatData(FalloutNifBlock block, ref NifCursor cursor) =>
        new(block, ReadScalarKeyGroup(ref cursor, "float data"));

    private static FalloutNifBoolData ReadBoolData(FalloutNifBlock block, ref NifCursor cursor)
    {
        var count = cursor.ReadCount32("boolean key count", MaximumTableEntries);
        var interpolation = count == 0 ? 0 : cursor.ReadUInt32("boolean interpolation");
        if (count != 0 && interpolation is not (1 or 5)) throw new NotSupportedException($"Boolean interpolation {interpolation} is unbound.");
        var keys = new FalloutNifBoolKey[count];
        for (var index = 0; index < count; index++)
            keys[index] = new(cursor.ReadFiniteSingle("boolean time"), cursor.ReadBoolean("boolean value"));
        RequireIncreasingTimes(keys.Select(key => key.Time), "boolean");
        return new(block, interpolation, keys);
    }

    private FalloutNifMorphController ReadMorphController(FalloutNifBlock block, ref NifCursor cursor)
    {
        var time = ReadTimeController(ref cursor, "morph");
        var flags = cursor.ReadUInt16("morph flags");
        var data = ReadReference(ref cursor, "morph data");
        var alwaysUpdate = cursor.ReadByte("morph always update");
        var count = cursor.ReadCount32("morph interpolator count", MaximumTableEntries);
        var weights = new FalloutNifMorphWeight[count];
        for (var index = 0; index < count; index++) weights[index] = new(ReadReference(ref cursor, "morph interpolator"), cursor.ReadFiniteSingle("morph weight"));
        return new(block, time, flags, data, alwaysUpdate, weights);
    }

    private FalloutNifMorphData ReadMorphData(FalloutNifBlock block, ref NifCursor cursor)
    {
        var count = cursor.ReadCount32("morph count", MaximumTableEntries);
        var vertices = cursor.ReadCount32("morph vertices", MaximumTableEntries);
        var relative = cursor.ReadByte("relative morph targets");
        if ((long)count * (4L + vertices * 12L) > cursor.Remaining) throw new InvalidDataException("Morph tables exceed the source block.");
        var morphs = new FalloutNifMorph[count];
        for (var index = 0; index < count; index++)
        {
            var name = ReadStringReference(ref cursor, "morph name");
            var values = new FalloutNifVector3[vertices];
            for (var vertex = 0; vertex < vertices; vertex++) values[vertex] = ReadVector(ref cursor, "morph vector");
            morphs[index] = new(name, values);
        }
        return new(block, relative, morphs);
    }

    private FalloutNifPoint3Interpolator ReadPoint3Interpolator(
        FalloutNifBlock block,
        ref NifCursor cursor) => new(
        block,
        ReadVector(ref cursor, "point3 interpolator value"),
        ReadReference(ref cursor, "point3 interpolator data"));

    private FalloutNifPositionData ReadPositionData(FalloutNifBlock block, ref NifCursor cursor) =>
        new(block, ReadVectorKeyGroup(ref cursor, "position data"));

    private FalloutNifBlendFloatInterpolator ReadBlendFloatInterpolator(
        FalloutNifBlock block,
        ref NifCursor cursor)
    {
        var blend = ReadManagerBlendInterpolator(ref cursor, "blend float interpolator");
        return new FalloutNifBlendFloatInterpolator(
            block, blend.Flags, blend.ArraySize, blend.WeightThreshold,
            cursor.ReadFiniteSingle("blend float interpolator value"));
    }

    private FalloutNifBlendPoint3Interpolator ReadBlendPoint3Interpolator(
        FalloutNifBlock block,
        ref NifCursor cursor)
    {
        var blend = ReadManagerBlendInterpolator(ref cursor, "blend point3 interpolator");
        return new FalloutNifBlendPoint3Interpolator(
            block, blend.Flags, blend.ArraySize, blend.WeightThreshold,
            ReadVector(ref cursor, "blend point3 interpolator value"));
    }

    private static (byte Flags, byte ArraySize, float WeightThreshold) ReadManagerBlendInterpolator(
        ref NifCursor cursor,
        string label)
    {
        const byte managerControlled = 1 << 0;
        var flags = cursor.ReadByte($"{label} flags");
        var arraySize = cursor.ReadByte($"{label} array size");
        var threshold = cursor.ReadFiniteSingle($"{label} weight threshold");
        if ((flags & managerControlled) == 0)
            throw new NotSupportedException($"{label} is not manager controlled.");
        return (flags, arraySize, threshold);
    }

    private FalloutNifTextureTransformController ReadTextureTransformController(
        FalloutNifBlock block,
        ref NifCursor cursor) => new(
        block,
        ReadTimeController(ref cursor, "texture transform controller"),
        ReadReference(ref cursor, "texture transform controller interpolator"),
        cursor.ReadBoolean("texture transform controller shader map"),
        cursor.ReadUInt32("texture transform controller texture slot"),
        cursor.ReadUInt32("texture transform controller operation"));

    private FalloutNifMaterialColorController ReadMaterialColorController(
        FalloutNifBlock block,
        ref NifCursor cursor) => new(
        block,
        ReadTimeController(ref cursor, "material color controller"),
        ReadReference(ref cursor, "material color controller interpolator"),
        cursor.ReadUInt16("material color controller target color"));

    private FalloutNifTransformController ReadTransformController(
        FalloutNifBlock block,
        ref NifCursor cursor) => new(
        block,
        ReadTimeController(ref cursor, "transform controller"),
        ReadReference(ref cursor, "transform controller interpolator"));

    private FalloutNifBoneLodController ReadBoneLodController(
        FalloutNifBlock block,
        ref NifCursor cursor)
    {
        var time = ReadTimeController(ref cursor, "bone LOD controller");
        var lod = cursor.ReadUInt32("bone LOD controller current LOD");
        var lodCount = cursor.ReadCount32("bone LOD controller LOD count", MaximumTableEntries);
        var declaredNodeGroupCount = cursor.ReadCount32(
            "bone LOD controller node-group count", MaximumTableEntries);
        var nodeGroups = new int[lodCount][];
        for (var groupIndex = 0; groupIndex < nodeGroups.Length; ++groupIndex)
            nodeGroups[groupIndex] = ReadReferences(
                ref cursor, $"bone LOD controller node group {groupIndex}");
        return new FalloutNifBoneLodController(
            block, time, lod, checked((uint)lodCount), checked((uint)declaredNodeGroupCount), nodeGroups);
    }

    private FalloutNifControllerManager ReadControllerManager(
        FalloutNifBlock block,
        ref NifCursor cursor)
    {
        var time = ReadTimeController(ref cursor, "controller manager");
        var cumulative = cursor.ReadBoolean("controller manager cumulative");
        var sequenceCount = cursor.ReadCount32("controller manager sequence count", MaximumTableEntries);
        var sequences = ReadReferences(ref cursor, sequenceCount, "controller manager sequence");
        var palette = ReadReference(ref cursor, "controller manager object palette");
        return new FalloutNifControllerManager(block, time, cumulative, sequences, palette);
    }

    private FalloutNifMultiTargetTransformController ReadMultiTargetTransformController(
        FalloutNifBlock block,
        ref NifCursor cursor)
    {
        var time = ReadTimeController(ref cursor, "multi-target transform controller");
        var targetCount = cursor.ReadUInt16("multi-target transform extra target count");
        var targets = ReadReferences(ref cursor, targetCount, "multi-target transform extra target");
        return new FalloutNifMultiTargetTransformController(block, time, targets);
    }

    private FalloutNifTextKeyExtraData ReadTextKeyExtraData(
        FalloutNifBlock block,
        ref NifCursor cursor)
    {
        var name = ReadStringReference(ref cursor, "text-key name");
        const uint unknown = 0;
        var count = cursor.ReadCount32("text-key count", MaximumTableEntries);
        var keys = new FalloutNifTextKey[count];
        for (var index = 0; index < keys.Length; ++index)
            keys[index] = new FalloutNifTextKey(
                cursor.ReadFiniteSingle($"text key {index} time"),
                ReadStringReference(ref cursor, $"text key {index} value"));
        RequireIncreasingTimes(keys.Select(key => key.Time), "text key");
        return new FalloutNifTextKeyExtraData(block, name, unknown, keys);
    }

    private FalloutNifDefaultAvObjectPalette ReadDefaultAvObjectPalette(
        FalloutNifBlock block,
        ref NifCursor cursor)
    {
        var unknown = cursor.ReadUInt32("object palette unknown integer");
        var count = cursor.ReadCount32("object palette count", MaximumTableEntries);
        var objects = new FalloutNifPaletteObject[count];
        for (var index = 0; index < objects.Length; ++index)
            objects[index] = new FalloutNifPaletteObject(
                cursor.ReadSizedUtf8($"object palette {index} name", checked((uint)cursor.Remaining)),
                ReadReference(ref cursor, $"object palette {index} object"));
        return new FalloutNifDefaultAvObjectPalette(block, unknown, objects);
    }

    private FalloutNifTimeController ReadTimeController(ref NifCursor cursor, string label) => new(
        ReadReference(ref cursor, $"{label} next controller"),
        cursor.ReadUInt16($"{label} flags"),
        cursor.ReadFiniteSingle($"{label} frequency"),
        cursor.ReadFiniteSingle($"{label} phase"),
        cursor.ReadFiniteSingle($"{label} start time"),
        cursor.ReadFiniteSingle($"{label} stop time"),
        ReadReference(ref cursor, $"{label} target"),
        0);

    private FalloutNifTransformData ReadTransformData(FalloutNifBlock block, ref NifCursor cursor)
    {
        var rotationCount = cursor.ReadCount32("rotation key count", MaximumTableEntries);
        uint rotationType = 0;
        var quaternionKeys = Array.Empty<FalloutNifQuaternionKey>();
        var xyz = Array.Empty<FalloutNifScalarKey[]>();
        if (rotationCount != 0)
        {
            rotationType = cursor.ReadUInt32("rotation key type");
            if (rotationType == XyzRotationKeyType)
            {
                if (rotationCount != 1)
                    throw new InvalidDataException("XYZ rotation requires its sentinel key count of one.");
                xyz = new FalloutNifScalarKey[3][];
                for (var axis = 0; axis < xyz.Length; ++axis)
                    xyz[axis] = ReadScalarKeyGroup(ref cursor, $"rotation axis {axis}");
            }
            else
            {
                ValidateKeyType(rotationType, "rotation");
                quaternionKeys = new FalloutNifQuaternionKey[rotationCount];
                for (var index = 0; index < quaternionKeys.Length; ++index)
                {
                    var time = cursor.ReadFiniteSingle($"rotation key {index} time");
                    var value = ReadQuaternion(ref cursor, $"rotation key {index}");
                    FalloutNifQuaternion? forward = rotationType == QuadraticKeyType
                        ? ReadQuaternion(ref cursor, $"rotation key {index} forward") : null;
                    FalloutNifQuaternion? backward = rotationType == QuadraticKeyType
                        ? ReadQuaternion(ref cursor, $"rotation key {index} backward") : null;
                    FalloutNifVector3? tbc = rotationType == TbcKeyType
                        ? ReadVector(ref cursor, $"rotation key {index} TBC") : null;
                    quaternionKeys[index] = new FalloutNifQuaternionKey(time, value, forward, backward, tbc);
                }
                RequireIncreasingTimes(quaternionKeys.Select(key => key.Time), "rotation");
            }
        }
        var translations = ReadVectorKeyGroup(ref cursor, "translation");
        var scales = ReadScalarKeyGroup(ref cursor, "scale");
        return new FalloutNifTransformData(block, rotationType, quaternionKeys, xyz, translations, scales);
    }

    private FalloutNifShaderProperty ReadShaderProperty(FalloutNifBlock block, ref NifCursor cursor)
    {
        var objectNet = ReadObjectNet(ref cursor, block.TypeName);
        var smooth = cursor.ReadUInt16("shader smooth flags");
        var shaderType = cursor.ReadUInt32("shader type");
        var shaderFlags = cursor.ReadUInt32("shader flags");
        var shaderFlags2 = cursor.ReadUInt32("shader flags 2");
        var environmentMapScale = cursor.ReadFiniteSingle("environment map scale");
        var textureClampMode = cursor.ReadUInt32("texture clamp mode");
        var textureSet = ReadReference(ref cursor, "shader texture set");
        var refractionStrength = cursor.ReadFiniteSingle("refraction strength");
        var refractionFirePeriod = cursor.ReadInt32("refraction fire period");
        var unknownFloat4 = cursor.ReadFiniteSingle("shader unknown float 4");
        var unknownFloat5 = cursor.ReadFiniteSingle("shader unknown float 5");
        return new FalloutNifShaderProperty(block, objectNet.Name, objectNet.ExtraData,
            objectNet.Controller, smooth, shaderType, shaderFlags, shaderFlags2,
            environmentMapScale, textureClampMode, textureSet, refractionStrength,
            refractionFirePeriod, unknownFloat4, unknownFloat5);
    }

    private static FalloutNifShaderTextureSet ReadShaderTextureSet(
        FalloutNifBlock block,
        ref NifCursor cursor)
    {
        var count = cursor.ReadInt32("shader texture count");
        if (count is < 0 or > MaximumShaderTextureCount)
            throw new InvalidDataException("NIF shader texture count is invalid.");
        var textures = new string[count];
        for (var index = 0; index < textures.Length; ++index)
            textures[index] = cursor.ReadSizedUtf8($"shader texture {index}", checked((uint)cursor.Remaining));
        return new FalloutNifShaderTextureSet(block, textures);
    }

    private FalloutNifMaterialProperty ReadMaterialProperty(
        FalloutNifBlock block,
        ref NifCursor cursor)
    {
        var objectNet = ReadObjectNet(ref cursor, block.TypeName);
        var specular = ReadColor3(ref cursor, "material specular");
        var emissive = ReadColor3(ref cursor, "material emissive");
        var glossiness = cursor.ReadFiniteSingle("material glossiness");
        var alpha = cursor.ReadFiniteSingle("material alpha");
        var emissiveMultiple = cursor.ReadFiniteSingle("material emissive multiple");
        return new FalloutNifMaterialProperty(block, objectNet.Name, objectNet.ExtraData,
            objectNet.Controller, specular, emissive, glossiness, alpha, emissiveMultiple);
    }

    private FalloutNifBsxFlags ReadBsxFlags(FalloutNifBlock block, ref NifCursor cursor) =>
        new(block, ReadStringReference(ref cursor, "BSX name"), cursor.ReadUInt32("BSX flags"));

    private FalloutNifIntegerExtraData ReadIntegerExtraData(
        FalloutNifBlock block,
        ref NifCursor cursor) =>
        new(block, ReadStringReference(ref cursor, "integer extra-data name"),
            cursor.ReadUInt32("integer extra-data value"));

    private FalloutNifFloatExtraData ReadFloatExtraData(
        FalloutNifBlock block,
        ref NifCursor cursor) =>
        new(block, ReadStringReference(ref cursor, "float extra-data name"),
            cursor.ReadFiniteSingle("float extra-data value"));

    private FalloutNifDecalPlacementExtraData ReadDecalPlacementExtraData(
        FalloutNifBlock block,
        ref NifCursor cursor)
    {
        var name = ReadStringReference(ref cursor, "decal-placement name");
        var value = cursor.ReadFiniteSingle("decal-placement float value");
        var blockCount = cursor.ReadUInt16("decal-placement vector block count");
        var vectorBlocks = new FalloutNifDecalVectorBlock[blockCount];
        for (var blockIndex = 0; blockIndex < vectorBlocks.Length; blockIndex++)
        {
            var vectorCount = cursor.ReadUInt16($"decal-placement block {blockIndex} vector count");
            var points = new FalloutNifVector3[vectorCount];
            var normals = new FalloutNifVector3[vectorCount];
            for (var index = 0; index < vectorCount; index++)
                points[index] = ReadVector(ref cursor, $"decal-placement block {blockIndex} point {index}");
            for (var index = 0; index < vectorCount; index++)
                normals[index] = ReadVector(ref cursor, $"decal-placement block {blockIndex} normal {index}");
            vectorBlocks[blockIndex] = new FalloutNifDecalVectorBlock(points, normals);
        }
        return new FalloutNifDecalPlacementExtraData(block, name, value, vectorBlocks);
    }

    private FalloutNifStringExtraData ReadStringExtraData(
        FalloutNifBlock block,
        ref NifCursor cursor) =>
        new(block, ReadStringReference(ref cursor, "string extra-data name"),
            ReadStringReference(ref cursor, "string extra-data value"));

    private FalloutNifBound ReadBound(FalloutNifBlock block, ref NifCursor cursor) =>
        new(block, ReadStringReference(ref cursor, "bound name"),
            ReadVector(ref cursor, "bound center"), ReadVector(ref cursor, "bound dimensions"));

    private FalloutNifFurnitureMarker ReadFurnitureMarker(
        FalloutNifBlock block,
        ref NifCursor cursor)
    {
        var name = ReadStringReference(ref cursor, "furniture marker name");
        var count = cursor.ReadCount32("furniture position count", MaximumTableEntries);
        var positions = new FalloutNifFurniturePosition[count];
        for (var index = 0; index < positions.Length; ++index)
            positions[index] = new FalloutNifFurniturePosition(
                ReadVector(ref cursor, $"furniture position {index} offset"),
                cursor.ReadUInt16($"furniture position {index} orientation"),
                cursor.ReadByte($"furniture position {index} reference 1"),
                cursor.ReadByte($"furniture position {index} reference 2"));
        return new FalloutNifFurnitureMarker(block, name, positions);
    }

    private FalloutNifCollisionObject ReadCollisionObject(
        FalloutNifBlock block,
        ref NifCursor cursor) =>
        new(block, ReadReference(ref cursor, "collision target"),
            cursor.ReadUInt16("collision flags"), ReadReference(ref cursor, "collision body"),
            null, null);

    private FalloutNifCollisionObject ReadBlendCollisionObject(
        FalloutNifBlock block,
        ref NifCursor cursor) =>
        new(block, ReadReference(ref cursor, "blend collision target"),
            cursor.ReadUInt16("blend collision flags"),
            ReadReference(ref cursor, "blend collision body"),
            cursor.ReadFiniteSingle("blend collision heir gain"),
            cursor.ReadFiniteSingle("blend collision velocity gain"));

    private FalloutNifRigidBody ReadRigidBody(FalloutNifBlock block, ref NifCursor cursor)
    {
        var shape = ReadReference(ref cursor, "rigid-body shape");
        var filter = ReadCollisionFilter(ref cursor, "rigid-body world filter");
        var worldUnused = cursor.ReadUInt32("rigid-body world unused bytes");
        var broadPhaseType = cursor.ReadByte("rigid-body broad phase type");
        var broadPhaseUnused = new byte[3];
        for (var index = 0; index < broadPhaseUnused.Length; ++index)
            broadPhaseUnused[index] = cursor.ReadByte("rigid-body broad phase unused byte");
        var property = new FalloutNifHavokProperty(
            cursor.ReadUInt32("rigid-body property data"),
            cursor.ReadUInt32("rigid-body property size"),
            cursor.ReadUInt32("rigid-body property capacity and flags"));
        var entityResponse = ReadCollisionResponse(ref cursor, "rigid-body entity");
        var infoUnused1 = cursor.ReadUInt32("rigid-body info unused bytes 1");
        var infoFilter = ReadCollisionFilter(ref cursor, "rigid-body info filter");
        var infoUnused2 = cursor.ReadUInt32("rigid-body info unused bytes 2");
        var infoResponse = ReadCollisionResponse(ref cursor, "rigid-body info");
        var infoUnused3 = cursor.ReadUInt32("rigid-body info unused bytes 3");
        var translation = ReadHavokVector(ref cursor, "rigid-body translation");
        var rotation = ReadQuaternionXyzw(ref cursor, "rigid-body rotation");
        var linearVelocity = ReadHavokVector(ref cursor, "rigid-body linear velocity");
        var angularVelocity = ReadHavokVector(ref cursor, "rigid-body angular velocity");
        var inertia = new FalloutNifHavokMatrix3(
            ReadVector(ref cursor, "rigid-body inertia row 0"),
            cursor.ReadUInt32("rigid-body inertia row 0 padding"),
            ReadVector(ref cursor, "rigid-body inertia row 1"),
            cursor.ReadUInt32("rigid-body inertia row 1 padding"),
            ReadVector(ref cursor, "rigid-body inertia row 2"),
            cursor.ReadUInt32("rigid-body inertia row 2 padding"));
        var center = ReadHavokVector(ref cursor, "rigid-body center");
        var mass = cursor.ReadFiniteSingle("rigid-body mass");
        var linearDamping = cursor.ReadFiniteSingle("rigid-body linear damping");
        var angularDamping = cursor.ReadFiniteSingle("rigid-body angular damping");
        var friction = cursor.ReadFiniteSingle("rigid-body friction");
        var restitution = cursor.ReadFiniteSingle("rigid-body restitution");
        var maxLinearVelocity = cursor.ReadFiniteSingle("rigid-body maximum linear velocity");
        var maxAngularVelocity = cursor.ReadFiniteSingle("rigid-body maximum angular velocity");
        var penetrationDepth = cursor.ReadFiniteSingle("rigid-body penetration depth");
        var motionSystem = cursor.ReadByte("rigid-body motion system");
        var deactivatorType = cursor.ReadByte("rigid-body deactivator type");
        var solverDeactivation = cursor.ReadByte("rigid-body solver deactivation");
        var qualityType = cursor.ReadByte("rigid-body quality type");
        var infoUnused4 = new uint[3];
        for (var index = 0; index < infoUnused4.Length; ++index)
            infoUnused4[index] = cursor.ReadUInt32("rigid-body info unused trailing bytes");
        var constraints = ReadReferences(ref cursor, "rigid-body constraints");
        var bodyFlags = cursor.ReadUInt32("rigid-body flags");
        return new FalloutNifRigidBody(block, shape, translation, rotation, center, mass,
            linearDamping, angularDamping, friction, restitution, motionSystem, constraints)
        {
            Filter = filter,
            WorldUnused = worldUnused,
            BroadPhaseType = broadPhaseType,
            BroadPhaseUnused = broadPhaseUnused,
            Property = property,
            EntityResponse = entityResponse,
            InfoUnused1 = infoUnused1,
            InfoFilter = infoFilter,
            InfoUnused2 = infoUnused2,
            InfoResponse = infoResponse,
            InfoUnused3 = infoUnused3,
            LinearVelocity = linearVelocity,
            AngularVelocity = angularVelocity,
            Inertia = inertia,
            MaxLinearVelocity = maxLinearVelocity,
            MaxAngularVelocity = maxAngularVelocity,
            PenetrationDepth = penetrationDepth,
            DeactivatorType = deactivatorType,
            SolverDeactivation = solverDeactivation,
            QualityType = qualityType,
            InfoUnused4 = infoUnused4,
            BodyFlags = bodyFlags,
            SourceBytes = _payload.Slice(block.Offset, block.Size),
        };
    }

    private static FalloutNifCollisionResponse ReadCollisionResponse(ref NifCursor cursor, string label) =>
        new(cursor.ReadByte($"{label} collision response"),
            cursor.ReadByte($"{label} unused byte"),
            cursor.ReadUInt16($"{label} contact callback delay"));

    private static FalloutNifVector4 ReadHavokVector(ref NifCursor cursor, string label) =>
        new(cursor.ReadFiniteSingle($"{label} x"),
            cursor.ReadFiniteSingle($"{label} y"),
            cursor.ReadFiniteSingle($"{label} z"),
            // Havok's fourth SIMD lane is not a spatial coordinate. Owned bodies
            // contain non-finite padding here; retain those bits without arithmetic.
            BitConverter.UInt32BitsToSingle(cursor.ReadUInt32($"{label} fourth lane")));

    private FalloutNifMoppShape ReadMoppShape(FalloutNifBlock block, ref NifCursor cursor)
    {
        var child = ReadReference(ref cursor, "MOPP child");
        _ = cursor.ReadUInt32("MOPP unknown integer");
        cursor.Skip(HavokUnknownPairBytes, "MOPP unknown bytes");
        _ = cursor.ReadFiniteSingle("MOPP unknown float");
        var size = cursor.ReadCount32("MOPP byte count", MaximumTableEntries);
        var origin = ReadVector(ref cursor, "MOPP origin");
        var scale = cursor.ReadFiniteSingle("MOPP scale");
        cursor.Skip(size, "MOPP bytecode");
        return new FalloutNifMoppShape(block, child, origin, scale);
    }

    private FalloutNifPackedShape ReadPackedShape(FalloutNifBlock block, ref NifCursor cursor)
    {
        // The shape-level sub-shape table ended at 20.0.0.5. Fallout 20.2.0.7
        // carries it only in hkPackedNiTriStripsData.
        FalloutNifSubShape[] subShapes = [];
        cursor.Skip(HavokUnknownPairBytes, "packed shape unknown integers");
        _ = cursor.ReadFiniteSingle("packed shape unknown float 1");
        _ = cursor.ReadUInt32("packed shape unknown integer 3");
        var scaleCopy = ReadVector(ref cursor, "packed shape scale copy");
        cursor.Skip(HavokUnknownFloatPairBytes, "packed shape unknown floats 2 and 3");
        var scale = ReadVector(ref cursor, "packed shape scale");
        _ = cursor.ReadFiniteSingle("packed shape unknown float 4");
        var data = ReadReference(ref cursor, "packed shape data");
        return new FalloutNifPackedShape(block, subShapes, scaleCopy, scale, data);
    }

    private static FalloutNifPackedData ReadPackedData(FalloutNifBlock block, ref NifCursor cursor)
    {
        var triangleCount = cursor.ReadCount32("packed triangle count", MaximumTableEntries);
        var triangles = new FalloutNifPackedTriangle[triangleCount];
        for (var index = 0; index < triangles.Length; ++index)
        {
            var a = cursor.ReadUInt16($"packed triangle {index} a");
            var b = cursor.ReadUInt16($"packed triangle {index} b");
            var c = cursor.ReadUInt16($"packed triangle {index} c");
            var welding = cursor.ReadUInt16($"packed triangle {index} welding");
            // hkTriangle normals ended at 20.0.0.5; Fallout's 20.2.0.7 stores indices and welding only.
            var normal = new FalloutNifVector3(0.0f, 0.0f, 0.0f);
            triangles[index] = new FalloutNifPackedTriangle(a, b, c, welding, normal);
        }
        var vertexCount = cursor.ReadCount32("packed vertex count", ushort.MaxValue);
        _ = cursor.ReadByte("packed vertex unknown byte");
        var vertices = ReadVectors(ref cursor, vertexCount, "packed vertices");
        var subShapes = ReadSubShapes(ref cursor, "packed data");
        foreach (var triangle in triangles)
            if (triangle.A >= vertexCount || triangle.B >= vertexCount || triangle.C >= vertexCount)
                throw new InvalidDataException($"NIF packed data {block.Index} has an invalid triangle index.");
        return new FalloutNifPackedData(block, vertices, triangles, subShapes);
    }

    private static FalloutNifBoxShape ReadBoxShape(FalloutNifBlock block, ref NifCursor cursor)
    {
        var material = cursor.ReadUInt32("box material");
        var radius = cursor.ReadFiniteSingle("box radius");
        cursor.Skip(HavokUnknownPairBytes, "box unknown bytes");
        var dimensions = ReadVector(ref cursor, "box dimensions");
        var minimumSize = cursor.ReadFiniteSingle("box minimum size");
        return new FalloutNifBoxShape(block, material, radius, dimensions, minimumSize);
    }

    private static FalloutNifSphereShape ReadSphereShape(FalloutNifBlock block, ref NifCursor cursor) =>
        new(block, cursor.ReadUInt32("sphere material"), cursor.ReadFiniteSingle("sphere radius"));

    private static FalloutNifCapsuleShape ReadCapsuleShape(FalloutNifBlock block, ref NifCursor cursor)
    {
        var material = cursor.ReadUInt32("capsule material");
        var radius = cursor.ReadFiniteSingle("capsule radius");
        cursor.Skip(HavokUnknownPairBytes, "capsule unknown bytes");
        var first = ReadVector(ref cursor, "capsule first point");
        var firstRadius = cursor.ReadFiniteSingle("capsule first radius");
        var second = ReadVector(ref cursor, "capsule second point");
        var secondRadius = cursor.ReadFiniteSingle("capsule second radius");
        return new FalloutNifCapsuleShape(block, material, radius, first, firstRadius, second, secondRadius);
    }

    private static FalloutNifConvexVerticesShape ReadConvexVerticesShape(
        FalloutNifBlock block, ref NifCursor cursor)
    {
        var material = cursor.ReadUInt32("convex material");
        var radius = cursor.ReadFiniteSingle("convex radius");
        cursor.Skip(HavokUnknownSixFloatBytes, "convex unknown floats");
        var vertexCount = cursor.ReadCount32("convex vertex count", ushort.MaxValue);
        var vertices = ReadVector4s(ref cursor, vertexCount, "convex vertices");
        var normalCount = cursor.ReadCount32("convex normal count", MaximumTableEntries);
        var normals = ReadVector4s(ref cursor, normalCount, "convex normals");
        return new FalloutNifConvexVerticesShape(block, material, radius, vertices, normals);
    }

    private FalloutNifListShape ReadListShape(FalloutNifBlock block, ref NifCursor cursor)
    {
        var children = ReadReferences(ref cursor, "list shape children");
        var material = cursor.ReadUInt32("list material");
        cursor.Skip(HavokUnknownSixFloatBytes, "list unknown floats");
        var unknownCount = cursor.ReadCount32("list unknown integer count", MaximumTableEntries);
        cursor.Skip(checked(unknownCount * sizeof(uint)), "list unknown integers");
        return new FalloutNifListShape(block, children, material);
    }

    private FalloutNifConvexTransformShape ReadConvexTransformShape(
        FalloutNifBlock block, ref NifCursor cursor)
    {
        var child = ReadReference(ref cursor, "convex transform child");
        var material = cursor.ReadUInt32("convex transform material");
        _ = cursor.ReadFiniteSingle("convex transform unknown float");
        cursor.Skip(HavokUnknownPairBytes, "convex transform unknown bytes");
        var matrix = new float[HavokTransformMatrixValues];
        for (var index = 0; index < matrix.Length; ++index)
            matrix[index] = cursor.ReadFiniteSingle($"convex transform matrix {index}");
        return new FalloutNifConvexTransformShape(block, child, material, matrix);
    }

    private static FalloutNifCollisionFilter ReadCollisionFilter(ref NifCursor cursor, string label) =>
        new(cursor.ReadByte($"{label} layer"), cursor.ReadByte($"{label} flags"),
            cursor.ReadUInt16($"{label} group"));

    private static FalloutNifSubShape[] ReadSubShapes(ref NifCursor cursor, string label)
    {
        var count = cursor.ReadCount16($"{label} sub-shape count", ushort.MaxValue);
        var result = new FalloutNifSubShape[count];
        for (var index = 0; index < result.Length; ++index)
            result[index] = new FalloutNifSubShape(ReadCollisionFilter(ref cursor, $"{label} sub-shape {index}"),
                cursor.ReadUInt32($"{label} sub-shape {index} vertex count"),
                cursor.ReadUInt32($"{label} sub-shape {index} material"));
        return result;
    }

    private static FalloutNifVector4 ReadVector4(ref NifCursor cursor, string label) =>
        new(cursor.ReadFiniteSingle($"{label} x"), cursor.ReadFiniteSingle($"{label} y"),
            cursor.ReadFiniteSingle($"{label} z"), cursor.ReadFiniteSingle($"{label} w"));

    private static FalloutNifQuaternion ReadQuaternionXyzw(ref NifCursor cursor, string label)
    {
        var x = cursor.ReadFiniteSingle($"{label} x");
        var y = cursor.ReadFiniteSingle($"{label} y");
        var z = cursor.ReadFiniteSingle($"{label} z");
        var w = cursor.ReadFiniteSingle($"{label} w");
        return new FalloutNifQuaternion(w, x, y, z);
    }

    private static FalloutNifVector4[] ReadVector4s(ref NifCursor cursor, int count, string label)
    {
        var result = new FalloutNifVector4[count];
        for (var index = 0; index < result.Length; ++index)
            result[index] = ReadVector4(ref cursor, $"{label} {index}");
        return result;
    }

    private FalloutNifNoLightingProperty ReadNoLightingProperty(
        FalloutNifBlock block,
        ref NifCursor cursor)
    {
        var objectNet = ReadObjectNet(ref cursor, block.TypeName);
        var smooth = cursor.ReadUInt16("no-lighting smooth flags");
        var shaderType = cursor.ReadUInt32("no-lighting shader type");
        var shaderFlags = cursor.ReadUInt32("no-lighting shader flags");
        var shaderFlags2 = cursor.ReadUInt32("no-lighting shader flags 2");
        var environmentMapScale = cursor.ReadFiniteSingle("no-lighting environment map scale");
        var textureClampMode = cursor.ReadUInt32("no-lighting texture clamp mode");
        var fileName = cursor.ReadSizedUtf8("no-lighting texture", checked((uint)cursor.Remaining));
        var falloffStartAngle = cursor.ReadFiniteSingle("no-lighting falloff start angle");
        var falloffStopAngle = cursor.ReadFiniteSingle("no-lighting falloff stop angle");
        var falloffStartOpacity = cursor.ReadFiniteSingle("no-lighting falloff start opacity");
        var falloffStopOpacity = cursor.ReadFiniteSingle("no-lighting falloff stop opacity");
        return new FalloutNifNoLightingProperty(block, objectNet.Name, objectNet.ExtraData,
            objectNet.Controller, smooth, shaderType, shaderFlags, shaderFlags2,
            environmentMapScale, textureClampMode, fileName, falloffStartAngle,
            falloffStopAngle, falloffStartOpacity, falloffStopOpacity);
    }

    private FalloutNifTileShaderProperty ReadTileShaderProperty(FalloutNifBlock block, ref NifCursor cursor)
    {
        var net = ReadObjectNet(ref cursor, block.TypeName);
        return new FalloutNifTileShaderProperty(block, net.Name, net.ExtraData, net.Controller,
            cursor.ReadUInt16("tile smooth flags"), cursor.ReadUInt32("tile shader type"),
            cursor.ReadUInt32("tile shader flags"), cursor.ReadUInt32("tile shader flags 2"),
            cursor.ReadFiniteSingle("tile environment map scale"), cursor.ReadUInt32("tile texture clamp mode"),
            cursor.ReadSizedUtf8("tile texture", checked((uint)cursor.Remaining)));
    }

    private FalloutNifAlphaProperty ReadAlphaProperty(
        FalloutNifBlock block,
        ref NifCursor cursor)
    {
        var objectNet = ReadObjectNet(ref cursor, block.TypeName);
        return new FalloutNifAlphaProperty(block, objectNet.Name, objectNet.ExtraData,
            objectNet.Controller, cursor.ReadUInt16("alpha flags"), cursor.ReadByte("alpha threshold"));
    }

    private FalloutNifStencilProperty ReadStencilProperty(
        FalloutNifBlock block,
        ref NifCursor cursor)
    {
        var objectNet = ReadObjectNet(ref cursor, block.TypeName);
        return new FalloutNifStencilProperty(
            block,
            objectNet.Name,
            objectNet.ExtraData,
            objectNet.Controller,
            cursor.ReadUInt16("stencil flags"),
            cursor.ReadUInt32("stencil reference"),
            cursor.ReadUInt32("stencil mask"));
    }

    private FalloutNifTexturingProperty ReadTexturingProperty(
        FalloutNifBlock block,
        ref NifCursor cursor)
    {
        var objectNet = ReadObjectNet(ref cursor, block.TypeName);
        var flags = cursor.ReadUInt16("texturing flags");
        var textureCount = cursor.ReadCount32(
            "legacy texture count", FalloutLegacyTextureSlotCountWithSecondDecal);
        if (textureCount is not (FalloutLegacyTextureSlotCount or
            FalloutLegacyTextureSlotCountWithSecondDecal))
            throw new NotSupportedException(
                $"NIF legacy texturing property {block.Index} has unsupported slot count {textureCount}.");
        var baseTexture = ReadOptionalTextureDescriptor(ref cursor, "base texture");
        var darkTexture = ReadOptionalTextureDescriptor(ref cursor, "dark texture");
        var detailTexture = ReadOptionalTextureDescriptor(ref cursor, "detail texture");
        var glossTexture = ReadOptionalTextureDescriptor(ref cursor, "gloss texture");
        var glowTexture = ReadOptionalTextureDescriptor(ref cursor, "glow texture");
        var bumpTexture = ReadOptionalTextureDescriptor(ref cursor, "bump texture");
        FalloutNifBumpMapParameters? bump = null;
        if (bumpTexture is not null)
            bump = new FalloutNifBumpMapParameters(
                cursor.ReadFiniteSingle("bump luma scale"),
                cursor.ReadFiniteSingle("bump luma offset"),
                cursor.ReadFiniteSingle("bump matrix 00"),
                cursor.ReadFiniteSingle("bump matrix 01"),
                cursor.ReadFiniteSingle("bump matrix 10"),
                cursor.ReadFiniteSingle("bump matrix 11"));
        var normalTexture = ReadOptionalTextureDescriptor(ref cursor, "normal texture");
        var parallaxTexture = ReadOptionalTextureDescriptor(ref cursor, "parallax texture");
        float? parallaxOffset = parallaxTexture is null
            ? null : cursor.ReadFiniteSingle("parallax offset");
        var decal0Texture = ReadOptionalTextureDescriptor(ref cursor, "decal 0 texture");
        var decal1Texture = textureCount == FalloutLegacyTextureSlotCountWithSecondDecal
            ? ReadOptionalTextureDescriptor(ref cursor, "decal 1 texture") : null;
        var shaderTextureCount = cursor.ReadCount32(
            "shader texture count", MaximumShaderTextureCount);
        var shaderTextures = new FalloutNifShaderTextureDescriptor[shaderTextureCount];
        for (var index = 0; index < shaderTextures.Length; index++)
        {
            var texture = ReadOptionalTextureDescriptor(
                ref cursor, $"shader texture {index}");
            shaderTextures[index] = new FalloutNifShaderTextureDescriptor(
                texture,
                texture is null ? null : cursor.ReadUInt32($"shader texture {index} map ID"));
        }
        return new FalloutNifTexturingProperty(
            block, objectNet.Name, objectNet.ExtraData, objectNet.Controller, flags,
            textureCount, baseTexture, darkTexture, detailTexture, glossTexture,
            glowTexture, bumpTexture, bump, normalTexture, parallaxTexture,
            parallaxOffset, decal0Texture, decal1Texture, shaderTextures);
    }

    private FalloutNifTextureDescriptor? ReadOptionalTextureDescriptor(
        ref NifCursor cursor,
        string label)
    {
        if (!cursor.ReadBoolean($"has {label}"))
            return null;
        var source = ReadReference(ref cursor, $"{label} source");
        var flags = cursor.ReadUInt16($"{label} flags");
        var uvSet = (uint)(flags & 0x00ff);
        FalloutNifTextureTransform? transform = null;
        if (cursor.ReadBoolean($"{label} has transform"))
            transform = new FalloutNifTextureTransform(
                new FalloutNifTexCoord(
                    cursor.ReadFiniteSingle($"{label} translation U"),
                    cursor.ReadFiniteSingle($"{label} translation V")),
                new FalloutNifTexCoord(
                    cursor.ReadFiniteSingle($"{label} tiling U"),
                    cursor.ReadFiniteSingle($"{label} tiling V")),
                cursor.ReadFiniteSingle($"{label} rotation"),
                cursor.ReadUInt32($"{label} transform type"),
                new FalloutNifTexCoord(
                    cursor.ReadFiniteSingle($"{label} center U"),
                    cursor.ReadFiniteSingle($"{label} center V")));
        return new FalloutNifTextureDescriptor(source, flags, uvSet, transform);
    }

    private FalloutNifSourceTexture ReadSourceTexture(
        FalloutNifBlock block,
        ref NifCursor cursor)
    {
        var objectNet = ReadObjectNet(ref cursor, block.TypeName);
        var external = cursor.ReadByte("source texture external flag");
        if (external != 1)
            throw new NotSupportedException(
                $"NIF source texture {block.Index} is embedded; only external owned textures are supported.");
        var fileName = ReadStringReference(ref cursor, "source texture file name");
        var unknownLink = ReadReference(ref cursor, "source texture unknown link");
        return new FalloutNifSourceTexture(
            block, objectNet.Name, objectNet.ExtraData, objectNet.Controller,
            fileName, unknownLink,
            cursor.ReadUInt32("source texture pixel layout"),
            cursor.ReadUInt32("source texture mipmap mode"),
            cursor.ReadUInt32("source texture alpha format"),
            cursor.ReadByte("source texture static flag"),
            cursor.ReadBoolean("source texture direct-render flag"),
            cursor.ReadBoolean("source texture persistent-render-data flag"));
    }

    private static FalloutNifVectorKey[] ReadVectorKeyGroup(ref NifCursor cursor, string label)
    {
        var count = cursor.ReadCount32($"{label} key count", MaximumTableEntries);
        if (count == 0)
            return [];
        var type = cursor.ReadUInt32($"{label} key type");
        ValidateKeyType(type, label);
        var keys = new FalloutNifVectorKey[count];
        for (var index = 0; index < keys.Length; ++index)
        {
            var time = cursor.ReadFiniteSingle($"{label} key {index} time");
            var value = ReadVector(ref cursor, $"{label} key {index}");
            FalloutNifVector3? forward = type == QuadraticKeyType
                ? ReadVector(ref cursor, $"{label} key {index} forward") : null;
            FalloutNifVector3? backward = type == QuadraticKeyType
                ? ReadVector(ref cursor, $"{label} key {index} backward") : null;
            FalloutNifVector3? tbc = type == TbcKeyType
                ? ReadVector(ref cursor, $"{label} key {index} TBC") : null;
            keys[index] = new FalloutNifVectorKey(time, value, forward, backward, tbc, type);
        }
        RequireIncreasingTimes(keys.Select(key => key.Time), label);
        return keys;
    }

    private static FalloutNifScalarKey[] ReadScalarKeyGroup(ref NifCursor cursor, string label)
    {
        var count = cursor.ReadCount32($"{label} key count", MaximumTableEntries);
        if (count == 0)
            return [];
        var type = cursor.ReadUInt32($"{label} key type");
        ValidateKeyType(type, label);
        var keys = new FalloutNifScalarKey[count];
        for (var index = 0; index < keys.Length; ++index)
        {
            var time = cursor.ReadFiniteSingle($"{label} key {index} time");
            var value = cursor.ReadFiniteSingle($"{label} key {index} value");
            float? forward = type == QuadraticKeyType ? cursor.ReadFiniteSingle($"{label} key {index} forward") : null;
            float? backward = type == QuadraticKeyType ? cursor.ReadFiniteSingle($"{label} key {index} backward") : null;
            FalloutNifVector3? tbc = type == TbcKeyType
                ? ReadVector(ref cursor, $"{label} key {index} TBC") : null;
            keys[index] = new FalloutNifScalarKey(time, value, forward, backward, tbc, type);
        }
        RequireIncreasingTimes(keys.Select(key => key.Time), label);
        return keys;
    }

    private AvObjectFields ReadAvObject(ref NifCursor cursor, string label)
    {
        var objectNet = ReadObjectNet(ref cursor, label);
        var flags = cursor.ReadUInt16($"{label} flags");
        _ = cursor.ReadUInt16($"{label} Bethesda flags");
        var translation = ReadVector(ref cursor, $"{label} translation");
        var rotation = new float[RotationMatrixValues];
        for (var index = 0; index < rotation.Length; ++index)
            rotation[index] = cursor.ReadFiniteSingle($"{label} rotation {index}");
        var scale = cursor.ReadFiniteSingle($"{label} scale");
        if (scale <= 0)
            throw new InvalidDataException($"NIF {label} scale is not positive.");
        var properties = ReadReferences(ref cursor, $"{label} properties");
        var collision = ReadReference(ref cursor, $"{label} collision");
        return new AvObjectFields(objectNet.Name, new FalloutNifTransform(translation, rotation, scale),
            flags, objectNet.ExtraData, objectNet.Controller, properties, collision);
    }

    private ObjectNetFields ReadObjectNet(ref NifCursor cursor, string label) => new(
        ReadStringReference(ref cursor, $"{label} name"),
        ReadReferences(ref cursor, $"{label} extra data"),
        ReadReference(ref cursor, $"{label} controller"));

    private string ReadStringReference(ref NifCursor cursor, string label)
    {
        var reference = cursor.ReadInt32(label);
        if (reference == -1)
            return string.Empty;
        if ((uint)reference >= Strings.Count)
            throw new InvalidDataException($"NIF {label} has an invalid string-table index.");
        return Strings[reference];
    }

    private int ReadReference(ref NifCursor cursor, string label)
    {
        var reference = cursor.ReadInt32(label);
        RequireReference(reference, Blocks.Count, label, allowNull: true);
        return reference;
    }

    private int[] ReadReferences(ref NifCursor cursor, string label)
    {
        var count = cursor.ReadCount32($"{label} count", MaximumTableEntries);
        return ReadReferences(ref cursor, count, label);
    }

    private int[] ReadReferences(ref NifCursor cursor, int count, string label)
    {
        var values = new int[count];
        for (var index = 0; index < values.Length; ++index)
            values[index] = ReadReference(ref cursor, $"{label} {index}");
        return values;
    }

    private NifCursor BlockCursor(FalloutNifBlock block) =>
        new(_payload.Span.Slice(block.Offset, block.Size), $"NIF block {block.Index} ({block.TypeName})");

    private static FalloutNifVector3 ReadVector(ref NifCursor cursor, string label) =>
        new(cursor.ReadFiniteSingle($"{label} x"), cursor.ReadFiniteSingle($"{label} y"),
            cursor.ReadFiniteSingle($"{label} z"));

    private static FalloutNifQuaternion ReadQuaternion(ref NifCursor cursor, string label) =>
        new(cursor.ReadFiniteSingle($"{label} w"), cursor.ReadFiniteSingle($"{label} x"),
            cursor.ReadFiniteSingle($"{label} y"), cursor.ReadFiniteSingle($"{label} z"));

    private static FalloutNifColor3 ReadColor3(ref NifCursor cursor, string label) =>
        new(cursor.ReadFiniteSingle($"{label} r"), cursor.ReadFiniteSingle($"{label} g"),
            cursor.ReadFiniteSingle($"{label} b"));

    private static FalloutNifVector3[] ReadVectors(ref NifCursor cursor, int count, string label)
    {
        var values = new FalloutNifVector3[count];
        for (var index = 0; index < values.Length; ++index)
            values[index] = ReadVector(ref cursor, $"{label} {index}");
        return values;
    }

    private static FalloutNifTriangle ReadTriangle(ref NifCursor cursor, ushort vertexCount, string label)
    {
        var result = new FalloutNifTriangle(cursor.ReadUInt16($"{label} a"),
            cursor.ReadUInt16($"{label} b"), cursor.ReadUInt16($"{label} c"));
        if (result.A >= vertexCount || result.B >= vertexCount || result.C >= vertexCount)
            throw new InvalidDataException($"NIF {label} references an invalid vertex.");
        return result;
    }

    private static void ValidateKeyType(uint type, string label)
    {
        if (type is not (LinearKeyType or QuadraticKeyType or TbcKeyType or ConstantKeyType))
            throw new NotSupportedException($"NIF {label} interpolation type {type} is unsupported.");
    }

    private static void RequireIncreasingTimes(IEnumerable<float> times, string label)
    {
        var first = true;
        var previous = 0.0f;
        foreach (var time in times)
        {
            if (!first && time < previous)
                throw new InvalidDataException($"NIF {label} key times are not ordered.");
            first = false;
            previous = time;
        }
    }

    private static void RequireReference(int value, int blockCount, string label, bool allowNull)
    {
        if ((allowNull && value == -1) || (value >= 0 && value < blockCount))
            return;
        throw new InvalidDataException($"NIF {label} has an invalid block reference: {value}");
    }

    private readonly record struct MeshPrefix(
        ushort VertexCount,
        FalloutNifVector3[] Vertices,
        byte StoredUvSets,
        byte ExtraVectorFlags);

    private readonly record struct AvObjectFields(
        string Name,
        FalloutNifTransform Transform,
        ushort Flags,
        int[] ExtraData,
        int Controller,
        int[] Properties,
        int CollisionObject);

    private readonly record struct ObjectNetFields(string Name, int[] ExtraData, int Controller);
}

internal readonly record struct FalloutNifBlock(int Index, string TypeName, int Offset, int Size);
internal readonly record struct FalloutNifVector3(float X, float Y, float Z);
internal readonly record struct FalloutNifVector4(float X, float Y, float Z, float W);
internal readonly record struct FalloutNifQuaternion(float W, float X, float Y, float Z);
internal readonly record struct FalloutNifColor(float R, float G, float B, float A);
internal readonly record struct FalloutNifColor3(float R, float G, float B);
internal readonly record struct FalloutNifTexCoord(float U, float V);
internal readonly record struct FalloutNifTriangle(ushort A, ushort B, ushort C);
internal sealed record FalloutNifTransform(FalloutNifVector3 Translation, float[] RotationRowMajor, float Scale);

internal abstract record FalloutNifObject(FalloutNifBlock Block);

internal sealed record FalloutNifNode(
    FalloutNifBlock Block,
    string Name,
    FalloutNifTransform Transform,
    ushort Flags,
    int Controller,
    int[] ExtraData,
    int[] Properties,
    int CollisionObject,
    int[] Children,
    int[] Effects) : FalloutNifObject(Block);

internal sealed record FalloutNifAmbientLight(
    FalloutNifBlock Block,
    string Name,
    FalloutNifTransform Transform,
    ushort Flags,
    int Controller,
    int[] ExtraData,
    int[] Properties,
    int CollisionObject,
    bool SwitchState,
    int[] AffectedNodes,
    float Dimmer,
    FalloutNifColor3 Ambient,
    FalloutNifColor3 Diffuse,
    FalloutNifColor3 Specular) : FalloutNifObject(Block);

internal sealed record FalloutNifPointLight(
    FalloutNifBlock Block,
    FalloutNifAmbientLight Light,
    float ConstantAttenuation,
    float LinearAttenuation,
    float QuadraticAttenuation) : FalloutNifObject(Block);

internal sealed record FalloutNifGeometry(
    FalloutNifBlock Block,
    string Name,
    FalloutNifTransform Transform,
    ushort Flags,
    int Controller,
    int[] ExtraData,
    int[] Properties,
    int CollisionObject,
    int Data,
    int SkinInstance,
    string[] MaterialNames,
    int[] MaterialExtraData,
    int ActiveMaterial,
    bool Dirty) : FalloutNifObject(Block);

internal sealed record FalloutNifMeshData(
    FalloutNifBlock Block,
    FalloutNifVector3[] Vertices,
    FalloutNifVector3[] Normals,
    FalloutNifVector3[] Tangents,
    FalloutNifVector3[] Bitangents,
    FalloutNifColor[] Colors,
    FalloutNifTexCoord[][] TextureCoordinates,
    FalloutNifTriangle[] Triangles,
    FalloutNifVector3 Center,
    float Radius,
    uint Consistency,
    int AdditionalData,
    byte StoredUvSets,
    byte DecodedUvSets) : FalloutNifObject(Block);

internal sealed record FalloutNifControllerLink(
    string NodeName,
    string PropertyType,
    string ControllerType,
    string Variable1,
    string Variable2,
    int Interpolator,
    int Controller,
    byte Priority);

internal sealed record FalloutNifControllerSequence(
    FalloutNifBlock Block,
    string Name,
    FalloutNifControllerLink[] ControlledBlocks,
    float Weight,
    int TextKeys,
    uint CycleType,
    float Frequency,
    float StartTime,
    float StopTime,
    int Manager,
    string TargetName,
    int AnimationNotes,
    short? UnknownShort) : FalloutNifObject(Block);

internal sealed record FalloutNifTransformInterpolator(
    FalloutNifBlock Block,
    FalloutNifVector3 Translation,
    FalloutNifQuaternion Rotation,
    float Scale,
    int Data) : FalloutNifObject(Block);

internal sealed record FalloutNifSplineBasisData(
    FalloutNifBlock Block,
    int ControlPointCount) : FalloutNifObject(Block);

internal sealed record FalloutNifSplineData(
    FalloutNifBlock Block,
    float[] FloatControlPoints,
    short[] CompactControlPoints) : FalloutNifObject(Block);

internal sealed record FalloutNifSplineTransformInterpolator(
    FalloutNifBlock Block,
    float StartTime,
    float StopTime,
    int Data,
    int BasisData,
    FalloutNifVector3 Translation,
    FalloutNifQuaternion Rotation,
    float Scale,
    uint TranslationHandle,
    uint RotationHandle,
    uint ScaleHandle,
    bool Compact,
    float TranslationOffset,
    float TranslationHalfRange,
    float RotationOffset,
    float RotationHalfRange,
    float ScaleOffset,
    float ScaleHalfRange) : FalloutNifObject(Block);

internal sealed record FalloutNifFloatInterpolator(
    FalloutNifBlock Block,
    float Value,
    int Data) : FalloutNifObject(Block);

internal sealed record FalloutNifFloatData(
    FalloutNifBlock Block,
    FalloutNifScalarKey[] Keys) : FalloutNifObject(Block);

internal sealed record FalloutNifPoint3Interpolator(
    FalloutNifBlock Block,
    FalloutNifVector3 Value,
    int Data) : FalloutNifObject(Block);

internal sealed record FalloutNifPositionData(
    FalloutNifBlock Block,
    FalloutNifVectorKey[] Keys) : FalloutNifObject(Block);

internal sealed record FalloutNifBlendFloatInterpolator(
    FalloutNifBlock Block,
    byte Flags,
    byte ArraySize,
    float WeightThreshold,
    float Value) : FalloutNifObject(Block);

internal sealed record FalloutNifBlendPoint3Interpolator(
    FalloutNifBlock Block,
    byte Flags,
    byte ArraySize,
    float WeightThreshold,
    FalloutNifVector3 Value) : FalloutNifObject(Block);

internal sealed record FalloutNifTimeController(
    int NextController,
    ushort Flags,
    float Frequency,
    float Phase,
    float StartTime,
    float StopTime,
    int Target,
    uint UnknownInteger);

internal sealed record FalloutNifControllerManager(
    FalloutNifBlock Block,
    FalloutNifTimeController Time,
    bool Cumulative,
    int[] Sequences,
    int ObjectPalette) : FalloutNifObject(Block);

internal sealed record FalloutNifMultiTargetTransformController(
    FalloutNifBlock Block,
    FalloutNifTimeController Time,
    int[] ExtraTargets) : FalloutNifObject(Block);

internal sealed record FalloutNifTextureTransformController(
    FalloutNifBlock Block,
    FalloutNifTimeController Time,
    int Interpolator,
    bool ShaderMap,
    uint TextureSlot,
    uint Operation) : FalloutNifObject(Block);

internal sealed record FalloutNifMaterialColorController(
    FalloutNifBlock Block,
    FalloutNifTimeController Time,
    int Interpolator,
    ushort TargetColor) : FalloutNifObject(Block);

internal sealed record FalloutNifTransformController(
    FalloutNifBlock Block,
    FalloutNifTimeController Time,
    int Interpolator) : FalloutNifObject(Block);

internal sealed record FalloutNifFloatExtraDataController(
    FalloutNifBlock Block,
    FalloutNifTimeController Time,
    int Interpolator,
    string ExtraDataName) : FalloutNifObject(Block);

internal sealed record FalloutNifBoneLodController(
    FalloutNifBlock Block,
    FalloutNifTimeController Time,
    uint Lod,
    uint LodCount,
    uint DeclaredNodeGroupCount,
    int[][] NodeGroups) : FalloutNifObject(Block);

internal sealed record FalloutNifTextKey(float Time, string Value);

internal sealed record FalloutNifTextKeyExtraData(
    FalloutNifBlock Block,
    string Name,
    uint UnknownInteger,
    FalloutNifTextKey[] Keys) : FalloutNifObject(Block);

internal sealed record FalloutNifPaletteObject(string Name, int Object);

internal sealed record FalloutNifDefaultAvObjectPalette(
    FalloutNifBlock Block,
    uint UnknownInteger,
    FalloutNifPaletteObject[] Objects) : FalloutNifObject(Block);

internal sealed record FalloutNifQuaternionKey(
    float Time,
    FalloutNifQuaternion Value,
    FalloutNifQuaternion? Forward,
    FalloutNifQuaternion? Backward,
    FalloutNifVector3? Tbc);

internal sealed record FalloutNifVectorKey(
    float Time,
    FalloutNifVector3 Value,
    FalloutNifVector3? Forward,
    FalloutNifVector3? Backward,
    FalloutNifVector3? Tbc,
    uint Interpolation);

internal sealed record FalloutNifScalarKey(
    float Time,
    float Value,
    float? Forward,
    float? Backward,
    FalloutNifVector3? Tbc,
    uint Interpolation);

internal sealed record FalloutNifTransformData(
    FalloutNifBlock Block,
    uint RotationType,
    FalloutNifQuaternionKey[] QuaternionRotations,
    FalloutNifScalarKey[][] XyzRotations,
    FalloutNifVectorKey[] Translations,
    FalloutNifScalarKey[] Scales) : FalloutNifObject(Block);

internal sealed record FalloutNifShaderProperty(
    FalloutNifBlock Block,
    string Name,
    int[] ExtraData,
    int Controller,
    ushort Smooth,
    uint ShaderType,
    uint ShaderFlags,
    uint ShaderFlags2,
    float EnvironmentMapScale,
    uint TextureClampMode,
    int TextureSet,
    float RefractionStrength,
    int RefractionFirePeriod,
    float UnknownFloat4,
    float UnknownFloat5) : FalloutNifObject(Block);

internal sealed record FalloutNifShaderTextureSet(
    FalloutNifBlock Block,
    string[] Textures) : FalloutNifObject(Block);

internal sealed record FalloutNifMaterialProperty(
    FalloutNifBlock Block,
    string Name,
    int[] ExtraData,
    int Controller,
    FalloutNifColor3 Specular,
    FalloutNifColor3 Emissive,
    float Glossiness,
    float Alpha,
    float EmissiveMultiple) : FalloutNifObject(Block);

internal sealed record FalloutNifBsxFlags(
    FalloutNifBlock Block,
    string Name,
    uint Flags) : FalloutNifObject(Block);

internal sealed record FalloutNifIntegerExtraData(
    FalloutNifBlock Block,
    string Name,
    uint Value) : FalloutNifObject(Block);

internal sealed record FalloutNifFloatExtraData(
    FalloutNifBlock Block,
    string Name,
    float Value) : FalloutNifObject(Block);

internal sealed record FalloutNifDecalVectorBlock(
    FalloutNifVector3[] Points,
    FalloutNifVector3[] Normals);

internal sealed record FalloutNifDecalPlacementExtraData(
    FalloutNifBlock Block,
    string Name,
    float Value,
    FalloutNifDecalVectorBlock[] VectorBlocks) : FalloutNifObject(Block);

internal sealed record FalloutNifStringExtraData(
    FalloutNifBlock Block,
    string Name,
    string Value) : FalloutNifObject(Block);

internal sealed record FalloutNifBound(
    FalloutNifBlock Block,
    string Name,
    FalloutNifVector3 Center,
    FalloutNifVector3 Dimensions) : FalloutNifObject(Block);

internal sealed record FalloutNifFurniturePosition(
    FalloutNifVector3 Offset,
    ushort Orientation,
    byte PositionReference1,
    byte PositionReference2);

internal sealed record FalloutNifFurnitureMarker(
    FalloutNifBlock Block,
    string Name,
    FalloutNifFurniturePosition[] Positions) : FalloutNifObject(Block);

internal sealed record FalloutNifCollisionObject(
    FalloutNifBlock Block,
    int Target,
    ushort Flags,
    int Body,
    float? HeirGain,
    float? VelocityGain) : FalloutNifObject(Block)
{
    internal bool IsBlend => HeirGain.HasValue;
}

internal readonly record struct FalloutNifCollisionFilter(byte Layer, byte Flags, ushort Group);
internal readonly record struct FalloutNifCollisionResponse(byte Type, byte Unused, ushort CallbackDelay);
internal readonly record struct FalloutNifHavokProperty(uint Data, uint Size, uint CapacityAndFlags);
internal readonly record struct FalloutNifHavokMatrix3(
    FalloutNifVector3 Row0, uint Padding0,
    FalloutNifVector3 Row1, uint Padding1,
    FalloutNifVector3 Row2, uint Padding2);
internal readonly record struct FalloutNifConstraintHeader(
    FalloutNifBlock Block,
    uint WrappedType,
    int EntityA,
    int EntityB,
    uint Priority,
    int UndecodedPayloadBytes);
internal readonly record struct FalloutNifSubShape(
    FalloutNifCollisionFilter Filter, uint VertexCount, uint Material);
internal readonly record struct FalloutNifPackedTriangle(
    ushort A, ushort B, ushort C, ushort Welding, FalloutNifVector3 Normal);

internal sealed record FalloutNifRigidBody(
    FalloutNifBlock Block, int Shape, FalloutNifVector4 Translation,
    FalloutNifQuaternion Rotation, FalloutNifVector4 Center, float Mass,
    float LinearDamping, float AngularDamping, float Friction, float Restitution,
    byte MotionSystem, int[] Constraints) : FalloutNifObject(Block)
{
    internal required FalloutNifCollisionFilter Filter { get; init; }
    internal required uint WorldUnused { get; init; }
    internal required byte BroadPhaseType { get; init; }
    internal required byte[] BroadPhaseUnused { get; init; }
    internal required FalloutNifHavokProperty Property { get; init; }
    internal required FalloutNifCollisionResponse EntityResponse { get; init; }
    internal required uint InfoUnused1 { get; init; }
    internal required FalloutNifCollisionFilter InfoFilter { get; init; }
    internal required uint InfoUnused2 { get; init; }
    internal required FalloutNifCollisionResponse InfoResponse { get; init; }
    internal required uint InfoUnused3 { get; init; }
    internal required FalloutNifVector4 LinearVelocity { get; init; }
    internal required FalloutNifVector4 AngularVelocity { get; init; }
    internal required FalloutNifHavokMatrix3 Inertia { get; init; }
    internal required float MaxLinearVelocity { get; init; }
    internal required float MaxAngularVelocity { get; init; }
    internal required float PenetrationDepth { get; init; }
    internal required byte DeactivatorType { get; init; }
    internal required byte SolverDeactivation { get; init; }
    internal required byte QualityType { get; init; }
    internal required uint[] InfoUnused4 { get; init; }
    internal required uint BodyFlags { get; init; }
    internal required ReadOnlyMemory<byte> SourceBytes { get; init; }
}
internal sealed record FalloutNifMoppShape(
    FalloutNifBlock Block, int Child, FalloutNifVector3 Origin, float Scale) : FalloutNifObject(Block);
internal sealed record FalloutNifPackedShape(
    FalloutNifBlock Block, FalloutNifSubShape[] SubShapes, FalloutNifVector3 ScaleCopy,
    FalloutNifVector3 Scale, int Data) : FalloutNifObject(Block);
internal sealed record FalloutNifPackedData(
    FalloutNifBlock Block, FalloutNifVector3[] Vertices, FalloutNifPackedTriangle[] Triangles,
    FalloutNifSubShape[] SubShapes) : FalloutNifObject(Block);
internal sealed record FalloutNifBoxShape(
    FalloutNifBlock Block, uint Material, float Radius, FalloutNifVector3 Dimensions,
    float MinimumSize) : FalloutNifObject(Block);
internal sealed record FalloutNifSphereShape(
    FalloutNifBlock Block, uint Material, float Radius) : FalloutNifObject(Block);
internal sealed record FalloutNifCapsuleShape(
    FalloutNifBlock Block, uint Material, float Radius, FalloutNifVector3 First,
    float FirstRadius, FalloutNifVector3 Second, float SecondRadius) : FalloutNifObject(Block);
internal sealed record FalloutNifConvexVerticesShape(
    FalloutNifBlock Block, uint Material, float Radius, FalloutNifVector4[] Vertices,
    FalloutNifVector4[] Normals) : FalloutNifObject(Block);
internal sealed record FalloutNifListShape(
    FalloutNifBlock Block, int[] Children, uint Material) : FalloutNifObject(Block);
internal sealed record FalloutNifConvexTransformShape(
    FalloutNifBlock Block, int Child, uint Material, float[] MatrixRowMajor) : FalloutNifObject(Block);

internal sealed record FalloutNifNoLightingProperty(
    FalloutNifBlock Block,
    string Name,
    int[] ExtraData,
    int Controller,
    ushort Smooth,
    uint ShaderType,
    uint ShaderFlags,
    uint ShaderFlags2,
    float EnvironmentMapScale,
    uint TextureClampMode,
    string FileName,
    float FalloffStartAngle,
    float FalloffStopAngle,
    float FalloffStartOpacity,
    float FalloffStopOpacity) : FalloutNifObject(Block);

internal sealed record FalloutNifTileShaderProperty(
    FalloutNifBlock Block, string Name, int[] ExtraData, int Controller,
    ushort Smooth, uint ShaderType, uint ShaderFlags, uint ShaderFlags2,
    float EnvironmentMapScale, uint TextureClampMode, string FileName) : FalloutNifObject(Block);

internal sealed record FalloutNifAlphaProperty(
    FalloutNifBlock Block,
    string Name,
    int[] ExtraData,
    int Controller,
    ushort Flags,
    byte Threshold) : FalloutNifObject(Block);

internal sealed record FalloutNifStencilProperty(
    FalloutNifBlock Block,
    string Name,
    int[] ExtraData,
    int Controller,
    ushort Flags,
    uint Reference,
    uint Mask) : FalloutNifObject(Block);

internal sealed record FalloutNifTextureTransform(
    FalloutNifTexCoord Translation,
    FalloutNifTexCoord Tiling,
    float Rotation,
    uint TransformType,
    FalloutNifTexCoord Center);

internal sealed record FalloutNifTextureDescriptor(
    int Source,
    ushort Flags,
    uint UvSet,
    FalloutNifTextureTransform? Transform);

internal sealed record FalloutNifBumpMapParameters(
    float LumaScale,
    float LumaOffset,
    float Matrix00,
    float Matrix01,
    float Matrix10,
    float Matrix11);

internal sealed record FalloutNifShaderTextureDescriptor(
    FalloutNifTextureDescriptor? Texture,
    uint? MapId);

internal sealed record FalloutNifTexturingProperty(
    FalloutNifBlock Block,
    string Name,
    int[] ExtraData,
    int Controller,
    ushort Flags,
    int TextureCount,
    FalloutNifTextureDescriptor? BaseTexture,
    FalloutNifTextureDescriptor? DarkTexture,
    FalloutNifTextureDescriptor? DetailTexture,
    FalloutNifTextureDescriptor? GlossTexture,
    FalloutNifTextureDescriptor? GlowTexture,
    FalloutNifTextureDescriptor? BumpTexture,
    FalloutNifBumpMapParameters? BumpParameters,
    FalloutNifTextureDescriptor? NormalTexture,
    FalloutNifTextureDescriptor? ParallaxTexture,
    float? ParallaxOffset,
    FalloutNifTextureDescriptor? Decal0Texture,
    FalloutNifTextureDescriptor? Decal1Texture,
    FalloutNifShaderTextureDescriptor[] ShaderTextures) : FalloutNifObject(Block);

internal sealed record FalloutNifSourceTexture(
    FalloutNifBlock Block,
    string Name,
    int[] ExtraData,
    int Controller,
    string FileName,
    int UnknownLink,
    uint PixelLayout,
    uint MipmapMode,
    uint AlphaFormat,
    byte StaticFlag,
    bool DirectRender,
    bool PersistentRenderData) : FalloutNifObject(Block);

internal ref struct NifCursor
{
    private readonly ReadOnlySpan<byte> _data;
    private readonly string _owner;

    internal NifCursor(ReadOnlySpan<byte> data, string owner)
    {
        _data = data;
        _owner = owner;
        Offset = 0;
    }

    internal int Offset { get; private set; }
    internal int Remaining => _data.Length - Offset;

    internal byte ReadByte(string label)
    {
        Require(1, label);
        return _data[Offset++];
    }

    internal bool ReadBoolean(string label)
    {
        var value = ReadByte(label);
        if (value > 1)
        {
            var start = Math.Max(0, Offset - 9);
            var count = Math.Min(24, _data.Length - start);
            throw new InvalidDataException(
                $"{_owner} {label} at byte {Offset - 1} has value {value} and is not a canonical boolean; " +
                $"near={Convert.ToHexString(_data.Slice(start, count))}.");
        }
        return value != 0;
    }

    internal ushort ReadUInt16(string label)
    {
        Require(sizeof(ushort), label);
        var value = BinaryPrimitives.ReadUInt16LittleEndian(_data[Offset..]);
        Offset += sizeof(ushort);
        return value;
    }

    internal uint ReadUInt32(string label)
    {
        Require(sizeof(uint), label);
        var value = BinaryPrimitives.ReadUInt32LittleEndian(_data[Offset..]);
        Offset += sizeof(uint);
        return value;
    }

    internal int ReadInt32(string label) => unchecked((int)ReadUInt32(label));

    internal float ReadFiniteSingle(string label)
    {
        var value = BitConverter.Int32BitsToSingle(ReadInt32(label));
        if (!float.IsFinite(value))
            throw new InvalidDataException($"{_owner} {label} is not finite.");
        return value;
    }

    internal int ReadCount16(string label, int maximum)
    {
        var count = ReadUInt16(label);
        if (count > maximum)
            throw new InvalidDataException($"{_owner} {label} exceeds its supported limit.");
        return count;
    }

    internal int ReadCount32(string label, int maximum)
    {
        var count = ReadUInt32(label);
        if (count > maximum)
            throw new InvalidDataException($"{_owner} {label} exceeds its supported limit.");
        return checked((int)count);
    }

    internal string ReadLineAscii(string label)
    {
        var tail = _data[Offset..];
        var newline = tail.IndexOf((byte)'\n');
        if (newline < 0)
            throw new InvalidDataException($"{_owner} {label} is unterminated.");
        var result = DecodeAscii(tail[..newline], label);
        Offset += newline + 1;
        return result;
    }

    internal string ReadShortString(string label)
    {
        var length = ReadByte($"{label} length");
        var bytes = ReadBytes(length, label);
        if (bytes.Length == 0 || bytes[^1] != 0)
            throw new InvalidDataException($"{_owner} {label} is not null terminated.");
        return DecodeUtf8(bytes[..^1], label);
    }

    internal string ReadSizedAscii(string label)
    {
        var length = ReadCount32($"{label} length", Remaining);
        if (length == 0)
            throw new InvalidDataException($"{_owner} {label} is empty.");
        return DecodeAscii(ReadBytes(length, label), label);
    }

    internal string ReadSizedUtf8(string label, uint maximumBytes)
    {
        var length = ReadUInt32($"{label} length");
        if (length > maximumBytes || length > int.MaxValue)
            throw new InvalidDataException($"{_owner} {label} exceeds the declared maximum.");
        return DecodeUtf8(ReadBytes((int)length, label), label);
    }

    internal void Skip(int count, string label)
    {
        Require(count, label);
        Offset += count;
    }

    internal void RequireEnd()
    {
        if (Remaining != 0)
            throw new InvalidDataException($"{_owner} has {Remaining} unconsumed bytes.");
    }

    private ReadOnlySpan<byte> ReadBytes(int count, string label)
    {
        Require(count, label);
        var result = _data.Slice(Offset, count);
        Offset += count;
        return result;
    }

    private void Require(int count, string label)
    {
        if (count < 0 || count > Remaining)
            throw new InvalidDataException($"{_owner} {label} exceeds its binary boundary.");
    }

    private static string DecodeAscii(ReadOnlySpan<byte> value, string label)
    {
        if (value.ContainsAnyExceptInRange((byte)0x20, (byte)0x7e))
            throw new InvalidDataException($"NIF {label} is not printable ASCII.");
        return Encoding.ASCII.GetString(value);
    }

    private static string DecodeUtf8(ReadOnlySpan<byte> value, string label)
    {
        return new UTF8Encoding(false, true).GetString(value);
    }
}
