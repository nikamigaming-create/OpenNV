using System.Buffers.Binary;
using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Formats.Gamebryo;

internal static class RuntimeNativeNifMeshBuilder
{
    private const ushort HiddenFlag = 0x0001;
    private const int TangentComponents = 4;
    private const uint SupportedShaderType = 1;
    private const uint ShaderFlagSpecular = 1U << 0;
    private const uint ShaderFlagSkinned = 1U << 1;
    private const uint ShaderFlagVertexAlpha = 1U << 3;
    private const uint ShaderFlagUseFalloff = 1U << 6;
    private const uint ShaderFlagEnvironmentMapping = 1U << 7;
    private const uint ShaderFlagAlphaTexture = 1U << 8;
    private const uint ShaderFlagEyeEnvironmentMapping = 1U << 17;
    private const uint ShaderFlagWindowEnvironmentMapping = 1U << 21;
    private const uint ShaderFlagRemappableTextures = 1U << 25;
    private const uint ShaderFlagDecal = 1U << 26;
    private const uint ShaderFlagDynamicDecal = 1U << 27;
    private const uint ShaderFlagExternalEmittance = 1U << 29;
    private const uint ShaderFlagZBufferTest = 1U << 31;
    private const uint ShaderFlagZBufferWrite = 1U << 0;
    private const uint ShaderFlagNoFade = 1U << 3;
    private const uint ShaderFlagEnvironmentMapLightFade = 1U << 15;
    private const uint SupportedShaderFlags = ShaderFlagSpecular |
        ShaderFlagVertexAlpha |
        ShaderFlagUseFalloff |
        ShaderFlagEnvironmentMapping | ShaderFlagAlphaTexture |
        ShaderFlagEyeEnvironmentMapping |
        ShaderFlagWindowEnvironmentMapping | ShaderFlagRemappableTextures |
        ShaderFlagExternalEmittance |
        ShaderFlagZBufferTest;
    private const uint SupportedShaderFlags2 = ShaderFlagZBufferWrite | (1U << 5) |
        ShaderFlagEnvironmentMapLightFade;
    private const uint SupportedNoLightingShaderType = 33;
    private const uint SupportedNoLightingShaderFlags = ShaderFlagVertexAlpha |
        ShaderFlagSpecular | ShaderFlagEnvironmentMapping |
        ShaderFlagUseFalloff |
        ShaderFlagRemappableTextures | ShaderFlagDecal | ShaderFlagDynamicDecal |
        ShaderFlagExternalEmittance | ShaderFlagZBufferTest;
    private const uint SupportedNoLightingShaderFlags2 = SupportedShaderFlags2 | ShaderFlagNoFade;
    private const ushort CollisionFlagActive = 1 << 0;
    private const ushort CollisionFlagNotify = 1 << 2;
    private const ushort CollisionFlagSetLocal = 1 << 3;
    private const ushort CollisionFlagAnimatedStatic = 1 << 5;
    private const ushort CollisionFlagReset = 1 << 6;
    private const ushort SupportedCollisionFlags = CollisionFlagActive | CollisionFlagNotify |
        CollisionFlagSetLocal | CollisionFlagAnimatedStatic | CollisionFlagReset;
    private const ushort AlphaBlendEnabled = 0x0001;
    private const ushort AlphaTestEnabled = 0x0200;
    private const ushort DisabledDoubleSidedStencilFlags = 0x4d80;
    private const ushort LegacyBaseTextureFlags = 0x3200;
    private const ushort LegacyTexturingFlags = 0x0004;
    private const uint LegacyTextureUvSet = 0;
    private const ushort MaterialColorSelfIllumination = 3;
    private const uint TextureTransformBaseSlot = 0;
    private const uint TextureTransformTranslateV = 1;
    private const uint QuadraticControllerKeyType = 2;
    private const byte ManagerControlledBlendFlags = 1;
    private const byte ManagerControlledBlendArraySize = 2;
    private const int LegacyTextureSlotCount = 9;
    private const uint LegacySourcePixelLayout = 6;
    private const uint LegacySourceMipmapMode = 1;
    private const float ByteMaximum = 255.0f;
    private const float HalfAngleScale = 0.5f;
    private const float ConstantBindTransformTolerance = 0.00001f;
    private const int DiffuseTextureSlot = 0;
    private const int NormalTextureSlot = 1;
    private const int EmissiveTextureSlot = 2;
    private const int EnvironmentTextureSlot = 4;
    private const int EnvironmentMaskTextureSlot = 5;
    private const int FalloutTextureSlots = 6;
    private const int DdsHeaderBytes = 128;
    private const ushort DormantManagerFlags = 0x004c;
    private const ushort DormantMultiTargetFlags = 0x006c;
    private const ushort DormantDirectTransformFlags = 0x0068;
    private const int DdsCaps2Offset = 112;
    private const uint DdsCubemapFlag = 0x00000200;
    private const uint DdsAllCubemapFaces = 0x0000fc00;
    private const int DdsPositiveXFace = 0;
    private const int DdsNegativeXFace = 1;
    private const int DdsPositiveYFace = 2;
    private const int DdsNegativeYFace = 3;
    private const int DdsPositiveZFace = 4;
    private const int DdsNegativeZFace = 5;
    private static readonly int[] DdsGodotFaceOrder =
    [
        DdsPositiveXFace,
        DdsNegativeXFace,
        DdsPositiveZFace,
        DdsNegativeZFace,
        DdsNegativeYFace,
        DdsPositiveYFace,
    ];
    private const string EnvironmentShader = """
        shader_type spatial;
        render_mode unshaded, blend_add, depth_draw_never, cull_back;

        uniform sampler2D normal_map : hint_normal;
        uniform samplerCube environment_cube;
        uniform sampler2D environment_mask;
        uniform bool use_environment_mask;
        uniform float environment_scale;

        void fragment() {
            vec3 tangent_normal = normalize(texture(normal_map, UV).xyz * 2.0 - 1.0);
            vec3 view_normal = normalize(
                TANGENT * tangent_normal.x + BINORMAL * tangent_normal.y + NORMAL * tangent_normal.z);
            vec3 reflected_view = reflect(-normalize(VIEW), view_normal);
            vec3 reflected_world = normalize((INV_VIEW_MATRIX * vec4(reflected_view, 0.0)).xyz);
            float mask = use_environment_mask
                ? texture(environment_mask, UV).r
                : texture(normal_map, UV).a;
            ALBEDO = texture(environment_cube, reflected_world).rgb * mask * environment_scale;
            ALPHA = 1.0;
        }
        """;
    private const string EnvironmentLightFadeShader = """
        shader_type spatial;
        render_mode blend_add, depth_draw_never, cull_back;

        uniform sampler2D normal_map : hint_normal;
        uniform samplerCube environment_cube;
        uniform sampler2D environment_mask;
        uniform bool use_environment_mask;
        uniform float environment_scale;

        void fragment() {
            vec3 tangent_normal = normalize(texture(normal_map, UV).xyz * 2.0 - 1.0);
            vec3 view_normal = normalize(
                TANGENT * tangent_normal.x + BINORMAL * tangent_normal.y + NORMAL * tangent_normal.z);
            vec3 reflected_view = reflect(-normalize(VIEW), view_normal);
            vec3 reflected_world = normalize((INV_VIEW_MATRIX * vec4(reflected_view, 0.0)).xyz);
            float mask = use_environment_mask
                ? texture(environment_mask, UV).r
                : texture(normal_map, UV).a;
            ALBEDO = texture(environment_cube, reflected_world).rgb * mask * environment_scale;
            ROUGHNESS = 0.0;
            METALLIC = 0.0;
            ALPHA = 1.0;
        }
        """;

    internal static RuntimeNativeNifScene Build(
        ReadOnlyMemory<byte> payload,
        float unitsToMetres,
        string? preferredTextureArchive = null) => Build(FalloutNifFile.Read(payload), unitsToMetres, preferredTextureArchive);

    internal static RuntimeNativeNifScene Build(
        FalloutNifFile source,
        float unitsToMetres,
        string? preferredTextureArchive = null)
    {
        if (!float.IsFinite(unitsToMetres) || unitsToMetres <= 0.0f)
            throw new ArgumentOutOfRangeException(
                nameof(unitsToMetres), "NIF-to-Godot scale must be finite and positive.");
        var root = new Node3D { Name = "NativeNif" };
        var state = new BuildState(source, unitsToMetres, preferredTextureArchive);
        try
        {
            foreach (var rootIndex in source.Roots)
                root.AddChild(state.Build(rootIndex));
            state.BuildControllerPlayers(root);
            return new RuntimeNativeNifScene(root, state.NodeCount, state.SurfaceCount,
                state.VertexCount, state.TriangleCount, state.CollisionBodyCount,
                state.CollisionShapeCount, state.CollisionTriangleCount);
        }
        catch
        {
            root.Free();
            throw;
        }
    }

    internal static RuntimeNativeNifSkeleton BuildActorSkeleton(
        ReadOnlyMemory<byte> payload, float unitsToMetres)
    {
        if (!float.IsFinite(unitsToMetres) || unitsToMetres <= 0.0f)
            throw new ArgumentOutOfRangeException(nameof(unitsToMetres));
        return new RuntimeNativeNifSkeleton(FalloutNifFile.Read(payload), unitsToMetres);
    }

    internal static MeshInstance3D BuildSkeletonAttachment(RuntimeNativeNifSkeleton skeleton, FalloutNifGeometry geometry,
        FalloutNifMorphGeometry? morph)
    {
        var state = new BuildState(skeleton.Source, skeleton.UnitsToMetres, null, externalSkeleton: true,
            geometryOwner: morph is null ? null : (_, _, data) => morph.BaseGeometry(data))
        {
            ExternalControllerBlocks = morph?.ControllerBlocks,
            MorphOwner = morph is null ? null : (_, _, _) => morph.RelativeDeltas(),
        };
        return (MeshInstance3D)state.Build(geometry.Block.Index);
    }

    internal static Material BuildMaterial(
        FalloutNifFile source, FalloutNifGeometry geometry, string? preferredTextureArchive, Color? hairColor) =>
        new BuildState(source, 1.0f, preferredTextureArchive, externalSkeleton: true).BuildMaterial(geometry, hairColor);

    internal static RuntimeNativeNifScene AddActorPart(
        ReadOnlyMemory<byte> payload,
        RuntimeNativeNifSkeleton skeleton,
        string? preferredTextureArchive,
        Func<FalloutNifFile, FalloutNifGeometry, Material?>? materialOverride,
        Func<FalloutNifFile, FalloutNifGeometry, FalloutNifMeshData, FalloutNifMeshData>? geometryOwner,
        IReadOnlySet<string>? externalTransformTargets,
        IReadOnlyDictionary<string, FalloutNifTransform>? rigidFaceBinds,
        string? selectedGeometryName,
        Func<FalloutNifFile, FalloutNifGeometry, FalloutNifMeshData, IReadOnlyDictionary<string, System.Numerics.Vector3[]>>? morphOwner)
    {
        var source = FalloutNifFile.Read(payload);
        var state = new BuildState(source, skeleton.UnitsToMetres, preferredTextureArchive,
            externalSkeleton: true, materialOverride, geometryOwner)
        { ExternalTransformTargets = externalTransformTargets, SelectedGeometryName = selectedGeometryName, MorphOwner = morphOwner };
        if (selectedGeometryName is not null && source.Blocks
            .Where(block => block.TypeName is "NiTriShape" or "NiTriStrips")
            .Count(block => source.ReadGeometry(block.Index).Name.Equals(selectedGeometryName, StringComparison.OrdinalIgnoreCase)) != 1)
            throw new InvalidDataException($"Source model has no unique equipped geometry named {selectedGeometryName}.");
        var result = new Node3D { Name = $"NativePart{skeleton.Node.GetChildCount()}" };
        try
        {
            foreach (var rootIndex in source.Roots)
            {
                var root = source.ReadNode(rootIndex);
                var parentNames = root.ExtraData.Where(reference => reference >= 0)
                    .Select(source.ReadObject).OfType<FalloutNifStringExtraData>()
                    .Where(extra => extra.Name == "Prn").Select(extra => extra.Value).ToArray();
                if (parentNames.Length > 1)
                    throw new InvalidDataException("Actor NIF has multiple source parent attachments.");
                if (parentNames.Length == 1)
                {
                    _ = skeleton.BoneIndex(parentNames[0]);
                    if (rigidFaceBinds is not null)
                    {
                        if (!rigidFaceBinds.TryGetValue(parentNames[0], out var faceBind))
                            throw new InvalidDataException($"Source head skin has no Prn inverse bind for {parentNames[0]}.");
                        state.RigidFaceBind = skeleton.Convert(faceBind);
                    }
                    var attachment = new BoneAttachment3D
                    {
                        Name = $"SourceAttachment{rootIndex}",
                        BoneName = parentNames[0],
                        UseExternalSkeleton = true,
                        ExternalSkeleton = new NodePath("../.."),
                    };
                    attachment.SetMeta("opennv_nif_parent_bone", parentNames[0]);
                    result.AddChild(attachment);
                    attachment.AddChild(state.Build(rootIndex));
                    if (rigidFaceBinds is not null)
                        attachment.SetMeta("opennv_rigid_face_basis", "source-head-skin-inverse-bind");
                }
                else
                {
                    if (rigidFaceBinds is not null)
                        throw new NotSupportedException("Rigid FaceGen component has no unique source Prn owner.");
                    state.BuildHardwareSkinTree(rootIndex, Transform3D.Identity, true, result, skeleton, []);
                }
            }
            if (state.SurfaceCount == 0)
                throw new InvalidDataException("Actor part contains no presented source surfaces.");
            skeleton.Node.AddChild(result);
            return new RuntimeNativeNifScene(result, state.NodeCount, state.SurfaceCount,
                state.VertexCount, state.TriangleCount, state.CollisionBodyCount,
                state.CollisionShapeCount, state.CollisionTriangleCount);
        }
        catch
        {
            result.Free();
            throw;
        }
    }

    private sealed class BuildState
    {
        internal IReadOnlySet<string>? ExternalTransformTargets { get; init; }
        internal IReadOnlySet<int>? ExternalControllerBlocks { get; init; }
        internal Transform3D? RigidFaceBind { get; set; }
        internal string? SelectedGeometryName { get; init; }
        private readonly FalloutNifFile _source;
        private readonly float _unitsToMetres;
        private readonly string? _preferredTextureArchive;
        private readonly HashSet<int> _active = [];
        private readonly HashSet<int> _owned = [];
        private readonly Dictionary<int, int> _oneBoneSkinInstances = [];
        private readonly Dictionary<int, Node3D> _nodes = [];
        private readonly Dictionary<int, List<Material>> _materials = [];
        private readonly List<FalloutNifControllerManager> _controllerManagers = [];
        private readonly List<RuntimeNifControllerSequence> _directControllerSequences = [];
        private readonly HashSet<int> _referencedDynamicEffects = [];
        private readonly Func<FalloutNifFile, FalloutNifGeometry, Material?>? _materialOverride;
        private readonly Func<FalloutNifFile, FalloutNifGeometry, FalloutNifMeshData, FalloutNifMeshData>? _geometryOwner;
        internal Func<FalloutNifFile, FalloutNifGeometry, FalloutNifMeshData, IReadOnlyDictionary<string, System.Numerics.Vector3[]>>? MorphOwner { get; init; }

        internal BuildState(
            FalloutNifFile source,
            float unitsToMetres,
            string? preferredTextureArchive,
            bool externalSkeleton = false,
            Func<FalloutNifFile, FalloutNifGeometry, Material?>? materialOverride = null,
            Func<FalloutNifFile, FalloutNifGeometry, FalloutNifMeshData, FalloutNifMeshData>? geometryOwner = null)
        {
            _source = source;
            _unitsToMetres = unitsToMetres;
            _preferredTextureArchive = preferredTextureArchive;
            _materialOverride = materialOverride;
            _geometryOwner = geometryOwner;
            foreach (var nodeBlock in source.Blocks.Where(block =>
                block.TypeName is "NiNode" or "NiBone" or "BSFadeNode"))
            {
                foreach (var effect in source.ReadNode(nodeBlock.Index).Effects.Where(reference => reference != -1))
                    _referencedDynamicEffects.Add(effect);
            }
            foreach (var block in source.Blocks.Where(block => !externalSkeleton &&
                (block.TypeName is "NiTriShape" or "NiTriStrips")))
            {
                var geometry = source.ReadGeometry(block.Index);
                if (geometry.SkinInstance == -1 ||
                    source.ReadObject(geometry.SkinInstance) is not FalloutNifSkinInstance instance ||
                    instance.Bones.Length != 1)
                    continue;
                if (!_oneBoneSkinInstances.TryAdd(instance.Bones[0], instance.Block.Index))
                    throw new NotSupportedException(
                        $"NIF bone {instance.Bones[0]} is shared by multiple skin instances outside the proven contract.");
            }
        }

        internal int NodeCount { get; private set; }
        internal int SurfaceCount { get; private set; }
        internal int VertexCount { get; private set; }
        internal int TriangleCount { get; private set; }
        internal int CollisionBodyCount { get; private set; }
        internal int CollisionShapeCount { get; private set; }
        internal int CollisionTriangleCount { get; private set; }

        internal void BuildHardwareSkinTree(
            int blockIndex,
            Transform3D parent,
            bool visible,
            Node3D output,
            RuntimeNativeNifSkeleton skeleton,
            HashSet<int> visited)
        {
            if (!visited.Add(blockIndex))
                throw new InvalidDataException("Actor part has a cycle or multiply owned source node.");
            if (_source.ReadObject(blockIndex) is FalloutNifNode node)
            {
                RequirePlainVisualState(node.Block, node.Controller, [], node.Properties, node.CollisionObject);
                if (node.Effects.Any(reference => reference >= 0))
                    throw new NotSupportedException($"Actor part source node {blockIndex} has unbound dynamic effects.");
                _ = ValidateExtraData(node.Block, node.ExtraData, collisionContract: false);
                var transform = parent * ConvertTransform(node.Transform);
                foreach (var child in node.Children.Where(child => child >= 0))
                    BuildHardwareSkinTree(child, transform,
                        visible && (node.Flags & HiddenFlag) == 0, output, skeleton, visited);
                return;
            }
            if (_source.ReadObject(blockIndex) is not FalloutNifGeometry geometry || geometry.SkinInstance < 0)
                throw new NotSupportedException(
                    $"Actor source block {blockIndex} requires a skinned mesh or an explicit Prn attachment.");
            RequirePlainVisualState(geometry.Block, geometry.Controller, [], [], geometry.CollisionObject);
            _ = ValidateExtraData(geometry.Block, geometry.ExtraData, collisionContract: false);
            if (geometry.MaterialNames.Length != 0 || geometry.MaterialExtraData.Length != 0)
                throw new NotSupportedException($"Actor geometry {blockIndex} uses an unsupported material table.");
            var data = ReadOwnedMeshData(geometry);
            var instance = (FalloutNifSkinInstance)_source.ReadObject(geometry.SkinInstance);
            var skinData = (FalloutNifSkinData)_source.ReadObject(instance.Data);
            var partitionData = (FalloutNifSkinPartition)_source.ReadObject(instance.SkinPartition);
            var sourceRoot = _source.ReadNode(instance.SkeletonRoot);
            var rootBone = skeleton.BoneIndex(sourceRoot.Name);
            if (skeleton.Node.GetBoneParent(rootBone) != -1)
                throw new NotSupportedException("Actor skin root is not a root of the external source skeleton.");
            var partitions = FalloutNifHardwareSkin.Read(instance, skinData, partitionData, data.Vertices.Length);
            var material = BuildMaterial(geometry);
            foreach (var partition in partitions)
            {
                var skin = new Skin();
                skin.SetBindCount(partition.BonePalette.Length);
                for (var bind = 0; bind < partition.BonePalette.Length; bind++)
                {
                    var sourceBone = partition.BonePalette[bind];
                    var boneName = _source.ReadNode(instance.Bones[sourceBone]).Name;
                    skin.SetBindBone(bind, skeleton.BoneIndex(boneName));
                    skin.SetBindName(bind, boneName);
                    skin.SetBindPose(bind, ConvertTransform(skinData.Bones[sourceBone].SkinTransform));
                }
                var mesh = new ArrayMesh();
                var arrays = BuildHardwareSkinArrays(data, partition);
                var morphs = BuildMorphArrays(mesh, geometry, data, arrays, partition.VertexMap.Select(value => (int)value).ToArray());
                var format = partition.InfluencesPerVertex == 8 ? Mesh.ArrayFormat.FlagUse8BoneWeights : 0;
                mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays, morphs, flags: format);
                mesh.SurfaceSetMaterial(0, material);
                var rendered = new MeshInstance3D
                {
                    Name = $"{SourceName(geometry.Name, blockIndex)}_Partition{partition.PartitionIndex}",
                    // NiSkinData maps the skeleton frame back into geometry space.
                    // Its product with the authored geometry placement is retained.
                    Transform = parent * ConvertTransform(geometry.Transform) * ConvertTransform(skinData.SkinTransform),
                    Visible = visible && (geometry.Flags & HiddenFlag) == 0 &&
                        FalloutNifHardwareSkin.VisibleOnIntactBody(partition.BodyPart),
                    Mesh = mesh,
                    Skin = skin,
                    Skeleton = new NodePath("../.."),
                };
                rendered.SetMeta("opennv_nif_geometry_block", blockIndex);
                PreserveExtraDataMetadata(rendered, geometry.ExtraData);
                rendered.SetMeta("opennv_nif_skin_instance", instance.Block.Index);
                rendered.SetMeta("opennv_nif_skin_partition", partition.PartitionIndex);
                rendered.SetMeta("opennv_nif_skin_vertex_map", partition.VertexMap.Select(value => (int)value).ToArray());
                if (partition.BodyPart is { } bodyPart)
                {
                    rendered.SetMeta("opennv_nif_body_part", bodyPart.BodyPart);
                    rendered.SetMeta("opennv_nif_body_part_flags", bodyPart.Flags);
                }
                output.AddChild(rendered);
                NodeCount++;
                SurfaceCount++;
                VertexCount += partition.VertexMap.Length;
                TriangleCount += arrays[(int)Mesh.ArrayType.Index].AsInt32Array().Length / 3;
            }
        }

        private Godot.Collections.Array BuildHardwareSkinArrays(
            FalloutNifMeshData data, FalloutNifHardwareSkinPartition partition)
        {
            if (data.AdditionalData != -1 || data.TextureCoordinates.Length > 1)
                throw new NotSupportedException("Actor source mesh has unsupported additional attributes.");
            RequireAttributeCount(data.Normals.Length, data.Vertices.Length, "normals", data.Block.Index);
            RequireAttributeCount(data.Tangents.Length, data.Vertices.Length, "tangents", data.Block.Index);
            RequireAttributeCount(data.Bitangents.Length, data.Vertices.Length, "bitangents", data.Block.Index);
            RequireAttributeCount(data.Colors.Length, data.Vertices.Length, "colors", data.Block.Index);
            var map = partition.VertexMap;
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = map.Select(index => ConvertVector(data.Vertices[index]) * _unitsToMetres).ToArray();
            if (data.Normals.Length > 0)
                arrays[(int)Mesh.ArrayType.Normal] = map.Select(index => ConvertVector(data.Normals[index])).ToArray();
            if (data.Tangents.Length > 0)
                arrays[(int)Mesh.ArrayType.Tangent] = BuildTangents(data with
                {
                    Normals = map.Select(index => data.Normals[index]).ToArray(),
                    Tangents = map.Select(index => data.Tangents[index]).ToArray(),
                    Bitangents = map.Select(index => data.Bitangents[index]).ToArray(),
                });
            if (data.Colors.Length > 0)
                arrays[(int)Mesh.ArrayType.Color] = map.Select(index => data.Colors[index])
                    .Select(color => new Color(color.R, color.G, color.B, color.A)).ToArray();
            if (data.TextureCoordinates.Length > 0)
            {
                RequireAttributeCount(data.TextureCoordinates[0].Length, data.Vertices.Length, "UVs", data.Block.Index);
                arrays[(int)Mesh.ArrayType.TexUV] = map.Select(index => data.TextureCoordinates[0][index])
                    .Select(uv => new Vector2(uv.U, uv.V)).ToArray();
            }
            arrays[(int)Mesh.ArrayType.Bones] = partition.BoneIndices;
            arrays[(int)Mesh.ArrayType.Weights] = partition.Weights;
            arrays[(int)Mesh.ArrayType.Index] = FalloutNifTriangleWinding.ToGodotIndices(partition.Triangles);
            return arrays;
        }

        internal Node3D Build(int blockIndex)
        {
            if (!_active.Add(blockIndex))
                throw new InvalidDataException($"NIF visual hierarchy contains a cycle at block {blockIndex}.");
            if (!_owned.Add(blockIndex))
                throw new InvalidDataException(
                    $"NIF visual block {blockIndex} has more than one owning parent.");
            try
            {
                var block = _source.Blocks[blockIndex];
                return block.TypeName switch
                {
                    "NiNode" or "NiBone" or "BSFadeNode" => BuildNode(_source.ReadNode(blockIndex)),
                    "NiTriShape" or "NiTriStrips" => BuildGeometry(_source.ReadGeometry(blockIndex)),
                    "NiAmbientLight" => BuildAmbientLight(
                        (FalloutNifAmbientLight)_source.ReadObject(blockIndex)),
                    "NiPointLight" => BuildPointLight(
                        (FalloutNifPointLight)_source.ReadObject(blockIndex)),
                    _ => throw new NotSupportedException(
                        $"Reachable NIF visual block {blockIndex} type {block.TypeName} is unsupported."),
                };
            }
            finally
            {
                _active.Remove(blockIndex);
            }
        }

        private Node3D BuildNode(FalloutNifNode source)
        {
            if (_oneBoneSkinInstances.TryGetValue(source.Block.Index, out var skinInstance))
                return BuildOneBoneSkeleton(source, skinInstance);
            RequirePlainVisualState(source.Block, -1, [], [], -1);
            var collision = ValidateVisualCollision(source.Block, source.CollisionObject);
            if (source.Effects.Any(reference => reference != -1))
                throw new NotSupportedException(
                    $"NIF node {source.Block.Index} has unsupported dynamic effects.");
            var result = CreateNode(source.Name, source.Block.Index, source.Transform, source.Flags);
            if (RigidFaceBind is not null)
            {
                if (source.Controller != -1 || source.CollisionObject != -1)
                    throw new NotSupportedException("Rigid FaceGen export hierarchy has an unbound transform or collision owner.");
                result.Transform = Transform3D.Identity;
            }
            _nodes.Add(source.Block.Index, result);
            PreserveCollisionMetadata(result, collision);
            AddCollision(result, collision);
            ValidateNodeController(source, result);
            var reachableCollision = HasReachableCollision(source.Block.Index, []);
            var omitEditorMarkers = ValidateExtraData(
                source.Block,
                source.ExtraData,
                reachableCollision);
            PreserveExtraDataMetadata(result, source.ExtraData);
            NodeCount++;
            foreach (var child in source.Children)
                if (child != -1)
                {
                    if (omitEditorMarkers && IsEditorMarkerRoot(child))
                        ValidateEditorMarkerTree(child);
                    else if (SelectedGeometryName is not null && _source.Blocks[child].TypeName is "NiTriShape" or "NiTriStrips" &&
                        !_source.ReadGeometry(child).Name.Equals(SelectedGeometryName, StringComparison.OrdinalIgnoreCase))
                    {
                        var omitted = CreateNode(_source.ReadGeometry(child).Name, child, _source.ReadGeometry(child).Transform, 0);
                        omitted.SetMeta("opennv_nif_geometry_disposition", "not-selected-by-equipment");
                        result.AddChild(omitted);
                    }
                    else
                        result.AddChild(Build(child));
                }
            return result;
        }

        private Node3D BuildAmbientLight(FalloutNifAmbientLight source)
        {
            RequirePlainVisualState(source.Block, source.Controller, source.ExtraData,
                source.Properties, source.CollisionObject);
            if (_referencedDynamicEffects.Contains(source.Block.Index))
                throw new NotSupportedException(
                    $"NIF ambient light {source.Block.Index} requires an exact subtree-lighting contract " +
                    $"(switch={source.SwitchState}, dimmer={source.Dimmer:R}, " +
                    $"ambient={source.Ambient.R:R},{source.Ambient.G:R},{source.Ambient.B:R}, " +
                    $"diffuse={source.Diffuse.R:R},{source.Diffuse.G:R},{source.Diffuse.B:R}, " +
                    $"specular={source.Specular.R:R},{source.Specular.G:R},{source.Specular.B:R}, " +
                    $"affected=[{string.Join(',', source.AffectedNodes)}]).");
            var result = CreateNode(source.Name, source.Block.Index, source.Transform, source.Flags);
            result.SetMeta("opennv_nif_unreferenced_ambient_light", true);
            result.SetMeta("opennv_nif_ambient_switch", source.SwitchState);
            result.SetMeta("opennv_nif_ambient_dimmer", source.Dimmer);
            NodeCount++;
            return result;
        }

        private Node3D BuildPointLight(FalloutNifPointLight source)
        {
            // A NiDynamicEffect is applied through the node effect lists. Mere
            // presence in the visual tree does not light every mesh in a cell.
            // Keep the authored object and its parameters; an actual reference
            // still requires the subtree-lighting owner and fails closed above.
            var result = BuildAmbientLight(source.Light);
            result.RemoveMeta("opennv_nif_unreferenced_ambient_light");
            result.SetMeta("opennv_nif_unreferenced_point_light", true);
            result.SetMeta("opennv_nif_point_attenuation", new Vector3(source.ConstantAttenuation,
                source.LinearAttenuation, source.QuadraticAttenuation));
            return result;
        }

        private Skeleton3D BuildOneBoneSkeleton(FalloutNifNode source, int skinInstance)
        {
            RequirePlainVisualState(source.Block, source.Controller, source.ExtraData,
                source.Properties, source.CollisionObject);
            if (source.Children.Any(reference => reference != -1) ||
                source.Effects.Any(reference => reference != -1))
                throw new NotSupportedException(
                    $"NIF skin bone {source.Block.Index} has descendants outside the proven one-bone contract.");
            var boneName = SourceName(source.Name, source.Block.Index);
            var result = new Skeleton3D
            {
                Name = boneName,
                Visible = (source.Flags & HiddenFlag) == 0,
            };
            result.AddBone(boneName);
            result.SetBoneRest(0, ConvertTransform(source.Transform));
            result.SetMeta("opennv_nif_skin_instance", skinInstance);
            result.SetMeta("opennv_nif_bone_block", source.Block.Index);
            _nodes.Add(source.Block.Index, result);
            NodeCount++;
            return result;
        }

        private bool HasReachableCollision(int blockIndex, HashSet<int> visited)
        {
            if (!visited.Add(blockIndex))
                return false;
            var block = _source.Blocks[blockIndex];
            if (block.TypeName is "NiNode" or "NiBone" or "BSFadeNode")
            {
                var node = _source.ReadNode(blockIndex);
                if (node.CollisionObject != -1 &&
                    _source.ReadObject(node.CollisionObject) is FalloutNifCollisionObject)
                    return true;
                return node.Children.Where(reference => reference != -1)
                    .Any(reference => HasReachableCollision(reference, visited));
            }
            if (block.TypeName is "NiTriShape" or "NiTriStrips")
            {
                var geometry = _source.ReadGeometry(blockIndex);
                return geometry.CollisionObject != -1 &&
                    _source.ReadObject(geometry.CollisionObject) is FalloutNifCollisionObject;
            }
            return false;
        }

        private void ValidateNodeController(FalloutNifNode owner, Node3D node)
        {
            if (owner.Controller == -1)
                return;
            if (_source.Blocks[owner.Controller].TypeName == "bhkBlendController")
            {
                node.SetMeta("opennv_nif_blend_controller", owner.Controller);
                node.SetMeta("opennv_nif_blend_controller_pinned", true);
                return;
            }
            var controller = _source.ReadObject(owner.Controller);
            if (controller is FalloutNifBoneLodController boneLod)
            {
                ValidateBoneLodController(owner, node, boneLod);
                controller = _source.ReadObject(boneLod.Time.NextController);
            }
            if (controller is FalloutNifTransformController direct)
            {
                if (ExternalTransformTargets?.Contains(owner.Name) == true &&
                    (direct.Time.Flags & 0x0040) != 0 && direct.Time.NextController == -1 &&
                    direct.Time.Target == owner.Block.Index)
                {
                    // Manager-controlled transform: the selected external KF
                    // supplies this node's clock and values, not an auto-loop.
                    node.SetMeta("opennv_nif_external_transform_controller", direct.Block.Index);
                    return;
                }
                if (TryPreserveConstantBindTransform(owner, node, direct))
                    return;
                if (TryApplyStaticTransformController(owner, node, direct))
                    return;
                if (TryBuildDirectTransformController(owner, node, direct))
                    return;
                if (direct.Time.Flags is not (DormantDirectTransformFlags or DormantMultiTargetFlags or DormantManagerFlags) ||
                    direct.Time.Frequency != 1.0f || direct.Time.Phase != 0.0f ||
                    direct.Time.StartTime != float.MaxValue || direct.Time.StopTime != float.MinValue ||
                    direct.Time.Target != owner.Block.Index || direct.Interpolator != -1)
                    throw new NotSupportedException(
                        $"NIF node {owner.Block.Index} has an unsupported direct transform controller " +
                        $"block={direct.Block.Index} next={direct.Time.NextController} " +
                        $"flags=0x{direct.Time.Flags:x4} frequency={direct.Time.Frequency:R} " +
                        $"phase={direct.Time.Phase:R} start={direct.Time.StartTime:R} " +
                        $"stop={direct.Time.StopTime:R} target={direct.Time.Target} " +
                        $"interpolator={direct.Interpolator}.");
                node.SetMeta("opennv_nif_dormant_transform_controller", direct.Block.Index);
                node.SetMeta("opennv_nif_dormant_transform_next", direct.Time.NextController);
                return;
            }
            if (controller is not FalloutNifControllerManager manager ||
                manager.Time.Flags != DormantManagerFlags || manager.Time.Frequency != 1.0f ||
                manager.Time.Phase != 0.0f || manager.Time.StartTime != float.MaxValue ||
                manager.Time.StopTime != float.MinValue || manager.Time.Target != owner.Block.Index ||
                manager.Time.UnknownInteger != 0 || manager.Cumulative || manager.Sequences.Length == 0 ||
                manager.ObjectPalette == -1 || manager.Time.NextController == -1)
                throw new NotSupportedException(
                    $"NIF node {owner.Block.Index} has an unsupported active controller contract.");
            if (_source.ReadObject(manager.Time.NextController) is not
                FalloutNifMultiTargetTransformController multi ||
                multi.Time.NextController != -1 || multi.Time.Flags != DormantMultiTargetFlags ||
                multi.Time.Frequency != 1.0f || multi.Time.Phase != 0.0f ||
                multi.Time.StartTime != float.MaxValue || multi.Time.StopTime != float.MinValue ||
                multi.Time.Target != owner.Block.Index || multi.Time.UnknownInteger != 0 ||
                multi.ExtraTargets.Any(reference => reference != -1 &&
                    _source.Blocks[reference].TypeName is not ("NiNode" or "NiBone" or "BSFadeNode")))
                throw new NotSupportedException(
                    $"NIF controller manager {manager.Block.Index} has an unsupported target chain.");
            if (_source.ReadObject(manager.ObjectPalette) is not FalloutNifDefaultAvObjectPalette palette ||
                palette.UnknownInteger != 0 || palette.Objects.Length == 0)
                throw new NotSupportedException(
                    $"NIF controller manager {manager.Block.Index} has an unsupported object palette.");
            foreach (var sequenceReference in manager.Sequences)
            {
                if (_source.ReadObject(sequenceReference) is not FalloutNifControllerSequence sequence ||
                    sequence.Manager != manager.Block.Index || sequence.ControlledBlocks.Length == 0 ||
                    sequence.TextKeys == -1 ||
                    _source.ReadObject(sequence.TextKeys) is not FalloutNifTextKeyExtraData ||
                    sequence.CycleType is not (0U or 2U) ||
                    sequence.ControlledBlocks.Any(link => link.Interpolator == -1 ||
                        link.Controller == -1 || link.Priority != 0 ||
                        link.ControllerType is not ("NiTransformController" or
                            "NiTextureTransformController" or "NiMaterialColorController")))
                    throw new NotSupportedException(
                        $"NIF controller manager {manager.Block.Index} has an unsupported sequence chain.");
            }
            _controllerManagers.Add(manager);
            node.SetMeta("opennv_nif_dormant_controller_manager", manager.Block.Index);
        }

        private bool TryPreserveConstantBindTransform(
            FalloutNifNode owner,
            Node3D node,
            FalloutNifTransformController controller)
        {
            if (controller.Time.NextController != -1 || controller.Time.Flags != DormantManagerFlags ||
                controller.Time.Frequency != 1.0f || controller.Time.Phase != 0.0f ||
                controller.Time.StopTime <= controller.Time.StartTime ||
                controller.Time.Target != owner.Block.Index || controller.Time.UnknownInteger != 0 ||
                controller.Interpolator == -1 ||
                _source.ReadObject(controller.Interpolator) is not
                    FalloutNifTransformInterpolator interpolator ||
                interpolator.Data != -1 || interpolator.Scale != float.MinValue)
                return false;

            var source = owner.Transform;
            if (!NearlyEqual(interpolator.Translation.X, source.Translation.X) ||
                !NearlyEqual(interpolator.Translation.Y, source.Translation.Y) ||
                !NearlyEqual(interpolator.Translation.Z, source.Translation.Z))
                return false;
            var quaternionLengthSquared = interpolator.Rotation.W * interpolator.Rotation.W +
                interpolator.Rotation.X * interpolator.Rotation.X +
                interpolator.Rotation.Y * interpolator.Rotation.Y +
                interpolator.Rotation.Z * interpolator.Rotation.Z;
            if (!NearlyEqual(quaternionLengthSquared, 1.0f))
                return false;
            var rotation = QuaternionRowMajor(interpolator.Rotation);
            if (!rotation.Zip(source.RotationRowMajor, NearlyEqual).All(value => value))
                return false;

            node.SetMeta("opennv_nif_constant_bind_transform_controller", controller.Block.Index);
            node.SetMeta("opennv_nif_constant_bind_transform_interpolator", interpolator.Block.Index);
            node.SetMeta("opennv_nif_constant_bind_transform_start", controller.Time.StartTime);
            node.SetMeta("opennv_nif_constant_bind_transform_stop", controller.Time.StopTime);
            node.SetMeta("opennv_nif_constant_bind_transform_runtime_enabled", false);
            return true;
        }

        private bool TryBuildDirectTransformController(
            FalloutNifNode owner,
            Node3D node,
            FalloutNifTransformController controller)
        {
            if (controller.Time.NextController != -1 || controller.Time.Frequency <= 0.0f ||
                controller.Time.StopTime <= controller.Time.StartTime ||
                controller.Time.Target != owner.Block.Index || controller.Time.UnknownInteger != 0 ||
                controller.Interpolator == -1 ||
                _source.ReadObject(controller.Interpolator) is not FalloutNifTransformInterpolator interpolator ||
                interpolator.Data == -1 ||
                _source.ReadObject(interpolator.Data) is not FalloutNifTransformData data ||
                !ValidateDirectRotation(data, controller.Time.StartTime, controller.Time.StopTime) ||
                (data.Translations.Length != 0 && !ValidateQuadraticKeys(
                    data.Translations, controller.Time.StartTime, controller.Time.StopTime)) ||
                (data.Scales.Length != 0 && !ValidateQuadraticKeys(
                    data.Scales, controller.Time.StartTime, controller.Time.StopTime)))
                return false;

            var cycleType = (uint)(controller.Time.Flags >> 1) & 3U;
            if (cycleType is not (0U or 2U))
                return false;
            var sourceTransform = owner.Transform;
            var baseTranslation = interpolator.Translation.X == float.MinValue &&
                interpolator.Translation.Y == float.MinValue &&
                interpolator.Translation.Z == float.MinValue
                ? sourceTransform.Translation
                : interpolator.Translation;
            var baseScale = interpolator.Scale == float.MinValue
                ? sourceTransform.Scale
                : interpolator.Scale;
            var channel = new RuntimeNifControllerChannel(time =>
            {
                var rotation = data.QuaternionRotations.Length != 0
                    ? QuaternionRowMajor(SampleQuaternion(data.QuaternionRotations, time))
                    : EulerXyzRowMajor(
                        SampleScalar(data.XyzRotations[0], time),
                        SampleScalar(data.XyzRotations[1], time),
                        SampleScalar(data.XyzRotations[2], time));
                var translation = data.Translations.Length == 0
                    ? baseTranslation
                    : SampleVector(data.Translations, time);
                var scale = data.Scales.Length == 0
                    ? baseScale
                    : SampleScalar(data.Scales, time);
                node.Transform = ConvertTransform(new FalloutNifTransform(
                    translation, rotation, scale));
            });
            _directControllerSequences.Add(new RuntimeNifControllerSequence(
                $"DirectTransform{controller.Block.Index}",
                cycleType,
                controller.Time.Frequency,
                controller.Time.StartTime,
                controller.Time.StopTime,
                [channel]));
            node.SetMeta("opennv_nif_direct_transform_controller", controller.Block.Index);
            return true;
        }

        private bool TryApplyStaticTransformController(
            FalloutNifNode owner,
            Node3D node,
            FalloutNifTransformController controller)
        {
            if (controller.Time.NextController != -1 ||
                controller.Time.StopTime != controller.Time.StartTime ||
                controller.Time.Target != owner.Block.Index || controller.Interpolator == -1 ||
                _source.ReadObject(controller.Interpolator) is not FalloutNifTransformInterpolator interpolator)
                return false;
            var translation = interpolator.Translation.X == float.MinValue &&
                interpolator.Translation.Y == float.MinValue &&
                interpolator.Translation.Z == float.MinValue
                ? owner.Transform.Translation
                : interpolator.Translation;
            var scale = interpolator.Scale == float.MinValue
                ? owner.Transform.Scale
                : interpolator.Scale;
            var lengthSquared = interpolator.Rotation.W * interpolator.Rotation.W +
                interpolator.Rotation.X * interpolator.Rotation.X +
                interpolator.Rotation.Y * interpolator.Rotation.Y +
                interpolator.Rotation.Z * interpolator.Rotation.Z;
            var rotation = NearlyEqual(lengthSquared, 1.0f)
                ? QuaternionRowMajor(interpolator.Rotation)
                : owner.Transform.RotationRowMajor;
            if (interpolator.Data != -1)
            {
                if (_source.ReadObject(interpolator.Data) is not FalloutNifTransformData data)
                    return false;
                if (data.QuaternionRotations.Length != 0)
                    rotation = QuaternionRowMajor(data.QuaternionRotations[0].Value);
                else if (data.XyzRotations.Length == 3 &&
                    data.XyzRotations.All(keys => keys.Length != 0))
                    rotation = EulerXyzRowMajor(
                        data.XyzRotations[0][0].Value,
                        data.XyzRotations[1][0].Value,
                        data.XyzRotations[2][0].Value);
                if (data.Translations.Length != 0)
                    translation = data.Translations[0].Value;
                if (data.Scales.Length != 0)
                    scale = data.Scales[0].Value;
            }
            node.Transform = ConvertTransform(new FalloutNifTransform(translation, rotation, scale));
            node.SetMeta("opennv_nif_static_transform_controller", controller.Block.Index);
            return true;
        }

        private static bool ValidateDirectRotation(
            FalloutNifTransformData data,
            float start,
            float stop)
        {
            if (data.RotationType == 4)
                return data.XyzRotations.Length == 3 &&
                    data.QuaternionRotations.Length == 0 &&
                    data.XyzRotations.All(keys => ValidateQuadraticKeys(keys, start, stop));
            return data.RotationType is 1U or 2U && data.XyzRotations.Length == 0 &&
                data.QuaternionRotations.Length >= 2 &&
                data.QuaternionRotations[0].Time == start &&
                data.QuaternionRotations[^1].Time == stop &&
                data.QuaternionRotations.Zip(data.QuaternionRotations.Skip(1),
                    (left, right) => right.Time > left.Time).All(value => value);
        }

        private static FalloutNifQuaternion SampleQuaternion(
            IReadOnlyList<FalloutNifQuaternionKey> keys,
            float time)
        {
            if (time <= keys[0].Time)
                return keys[0].Value;
            if (time >= keys[^1].Time)
                return keys[^1].Value;
            for (var index = 0; index + 1 < keys.Count; ++index)
            {
                var first = keys[index];
                var second = keys[index + 1];
                if (time > second.Time)
                    continue;
                var amount = (time - first.Time) / (second.Time - first.Time);
                var left = new Quaternion(
                    first.Value.X, first.Value.Y, first.Value.Z, first.Value.W).Normalized();
                var right = new Quaternion(
                    second.Value.X, second.Value.Y, second.Value.Z, second.Value.W).Normalized();
                var value = left.Slerp(right, amount).Normalized();
                return new FalloutNifQuaternion(value.W, value.X, value.Y, value.Z);
            }
            throw new InvalidDataException("NIF quaternion controller interval was not found.");
        }

        private static float[] QuaternionRowMajor(FalloutNifQuaternion value)
        {
            var xx = value.X * value.X;
            var yy = value.Y * value.Y;
            var zz = value.Z * value.Z;
            var xy = value.X * value.Y;
            var xz = value.X * value.Z;
            var yz = value.Y * value.Z;
            var wx = value.W * value.X;
            var wy = value.W * value.Y;
            var wz = value.W * value.Z;
            return
            [
                1.0f - 2.0f * (yy + zz), 2.0f * (xy - wz), 2.0f * (xz + wy),
                2.0f * (xy + wz), 1.0f - 2.0f * (xx + zz), 2.0f * (yz - wx),
                2.0f * (xz - wy), 2.0f * (yz + wx), 1.0f - 2.0f * (xx + yy),
            ];
        }

        private static bool NearlyEqual(float left, float right) =>
            MathF.Abs(left - right) <= ConstantBindTransformTolerance;

        private void ValidateBoneLodController(
            FalloutNifNode owner,
            Node3D node,
            FalloutNifBoneLodController controller)
        {
            if (controller.Time.Flags != DormantManagerFlags ||
                controller.Time.Frequency != 1.0f || controller.Time.Phase != 0.0f ||
                controller.Time.StartTime != float.MaxValue ||
                controller.Time.StopTime != float.MinValue ||
                controller.Time.Target != owner.Block.Index || controller.Time.UnknownInteger != 0 ||
                controller.LodCount == 0 || controller.Lod >= controller.LodCount ||
                controller.DeclaredNodeGroupCount != controller.LodCount ||
                controller.NodeGroups.Length != controller.LodCount ||
                controller.Time.NextController == -1 ||
                _source.Blocks[controller.Time.NextController].TypeName != "NiTransformController")
                throw new NotSupportedException(
                    $"NIF bone LOD controller {controller.Block.Index} is outside the admitted metadata contract.");

            var descendants = new HashSet<int>();
            CollectNodeDescendants(owner.Block.Index, descendants);
            descendants.Remove(owner.Block.Index);
            var groupedNodes = new HashSet<int>();
            foreach (var group in controller.NodeGroups)
            {
                foreach (var reference in group)
                {
                    if (reference == -1 || !descendants.Contains(reference) ||
                        _source.Blocks[reference].TypeName is not ("NiNode" or "NiBone" or "BSFadeNode") ||
                        !groupedNodes.Add(reference))
                        throw new NotSupportedException(
                            $"NIF bone LOD controller {controller.Block.Index} has an invalid or repeated bone.");
                }
            }

            node.SetMeta("opennv_nif_bone_lod_controller", controller.Block.Index);
            node.SetMeta("opennv_nif_bone_lod_current", controller.Lod);
            node.SetMeta("opennv_nif_bone_lod_count", controller.LodCount);
            node.SetMeta("opennv_nif_bone_lod_declared_node_group_count",
                controller.DeclaredNodeGroupCount);
            for (var groupIndex = 0; groupIndex < controller.NodeGroups.Length; ++groupIndex)
                node.SetMeta($"opennv_nif_bone_lod_group_{groupIndex}",
                    controller.NodeGroups[groupIndex]);
            node.SetMeta("opennv_nif_bone_lod_runtime_enabled", false);
        }

        private void CollectNodeDescendants(int blockIndex, HashSet<int> descendants)
        {
            if (!descendants.Add(blockIndex))
                return;
            var node = _source.ReadNode(blockIndex);
            foreach (var child in node.Children.Where(reference => reference != -1 &&
                _source.Blocks[reference].TypeName is "NiNode" or "NiBone" or "BSFadeNode"))
                CollectNodeDescendants(child, descendants);
        }

        internal void BuildControllerPlayers(Node3D root)
        {
            foreach (var manager in _controllerManagers)
            {
                var palette = (FalloutNifDefaultAvObjectPalette)_source.ReadObject(manager.ObjectPalette);
                var paletteByName = palette.Objects.ToDictionary(
                    value => value.Name, value => value.Object, StringComparer.Ordinal);
                var sequences = manager.Sequences.Select(reference => BuildControllerSequence(
                    manager,
                    (FalloutNifMultiTargetTransformController)_source.ReadObject(
                        manager.Time.NextController),
                    paletteByName,
                    _source.ReadControllerSequence(reference))).ToArray();
                var player = new RuntimeNifControllerPlayer
                {
                    Name = $"NifControllerManager{manager.Block.Index}",
                };
                player.Configure(sequences);
                player.SetMeta("opennv_nif_controller_manager", manager.Block.Index);
                player.SetMeta("opennv_nif_source_sequences", sequences.Select(value => value.Name).ToArray());
                root.AddChild(player);
            }
            foreach (var sequence in _directControllerSequences)
            {
                var player = new RuntimeNifControllerPlayer
                {
                    Name = sequence.Name,
                };
                player.Configure([sequence]);
                player.SetMeta("opennv_nif_direct_controller", true);
                root.AddChild(player);
            }
        }

        private RuntimeNifControllerSequence BuildControllerSequence(
            FalloutNifControllerManager manager,
            FalloutNifMultiTargetTransformController multi,
            IReadOnlyDictionary<string, int> palette,
            FalloutNifControllerSequence sequence)
        {
            if (sequence.Frequency != 1.0f || sequence.StartTime < 0.0f ||
                sequence.StopTime <= sequence.StartTime || sequence.Weight != 1.0f ||
                sequence.AnimationNotes != -1 || sequence.UnknownShort is not (null or 0))
                throw new NotSupportedException(
                    $"NIF sequence {sequence.Block.Index} has an unsupported timing contract.");
            var channels = new List<RuntimeNifControllerChannel>();
            foreach (var link in sequence.ControlledBlocks)
            {
                if (!palette.TryGetValue(link.NodeName, out var targetBlock) ||
                    !_nodes.TryGetValue(targetBlock, out var targetNode))
                    throw new NotSupportedException(
                        $"NIF sequence {sequence.Block.Index} target {link.NodeName} is not uniquely reachable.");
                channels.Add(link.ControllerType switch
                {
                    "NiTransformController" => BuildTransformChannel(
                        sequence, link, multi, targetBlock, targetNode),
                    "NiMaterialColorController" => BuildMaterialColorChannel(
                        sequence, link, targetBlock),
                    "NiTextureTransformController" => BuildTextureTransformChannel(
                        sequence, link, targetBlock),
                    _ => throw new NotSupportedException(
                        $"NIF sequence {sequence.Block.Index} controller {link.ControllerType} is unsupported."),
                });
            }
            return new RuntimeNifControllerSequence(
                sequence.Name, sequence.CycleType, sequence.Frequency,
                sequence.StartTime, sequence.StopTime, channels);
        }

        private RuntimeNifControllerChannel BuildTransformChannel(
            FalloutNifControllerSequence sequence,
            FalloutNifControllerLink link,
            FalloutNifMultiTargetTransformController multi,
            int targetBlock,
            Node3D target)
        {
            if (link.Controller != multi.Block.Index || !string.IsNullOrEmpty(link.PropertyType) ||
                !string.IsNullOrEmpty(link.Variable1) || !string.IsNullOrEmpty(link.Variable2) ||
                !multi.ExtraTargets.Contains(targetBlock))
                throw new NotSupportedException(
                    $"NIF sequence {sequence.Block.Index} transform target is outside its manager binding.");
            var sourceTransform = _source.ReadObject(targetBlock) switch
            {
                FalloutNifNode node => node.Transform,
                FalloutNifGeometry geometry => geometry.Transform,
                _ => throw new NotSupportedException("Managed transform target has no source-local transform."),
            };
            // Managed NIFs use the same keyed/constant/spline interpolator
            // contract as KF playback. A component without authored data keeps
            // the instance's source bind value; it is not a missing clip.
            var sampler = new FalloutNifAnimationSampler(_source, link.Interpolator);
            return new RuntimeNifControllerChannel(time =>
            {
                var sample = sampler.Sample(time);
                var rotation = sample.Rotation is { } value ? QuaternionRowMajor(value) : sourceTransform.RotationRowMajor;
                target.Transform = ConvertTransform(new FalloutNifTransform(
                    sample.Translation ?? sourceTransform.Translation, rotation, sample.Scale ?? sourceTransform.Scale));
            });
        }

        private RuntimeNifControllerChannel BuildMaterialColorChannel(
            FalloutNifControllerSequence sequence,
            FalloutNifControllerLink link,
            int targetBlock)
        {
            if (link.PropertyType != "NiMaterialProperty" || link.Variable1 != "SELF_ILLUM" ||
                !string.IsNullOrEmpty(link.Variable2) ||
                _source.ReadObject(link.Controller) is not FalloutNifMaterialColorController controller ||
                controller.TargetColor != MaterialColorSelfIllumination ||
                !ValidateManagedControllerTime(controller.Time, sequence) ||
                _source.ReadObject(controller.Interpolator) is not FalloutNifBlendPoint3Interpolator blend ||
                !ValidateBlend(blend.Flags, blend.ArraySize, blend.WeightThreshold, blend.Value) ||
                _source.ReadObject(link.Interpolator) is not FalloutNifPoint3Interpolator interpolator ||
                interpolator.Data == -1 ||
                _source.ReadObject(interpolator.Data) is not FalloutNifPositionData data ||
                !ValidateQuadraticKeys(data.Keys, sequence.StartTime, sequence.StopTime) ||
                !_source.ReadGeometry(targetBlock).Properties.Contains(controller.Time.Target) ||
                !_materials.TryGetValue(controller.Time.Target, out var materials))
                throw new NotSupportedException(
                    $"NIF sequence {sequence.Block.Index} material-color channel is incomplete.");
            return new RuntimeNifControllerChannel(time =>
            {
                var value = SampleVector(data.Keys, time);
                var color = new Color(value.X, value.Y, value.Z);
                foreach (var material in materials)
                {
                    if (material is ShaderMaterial effect && material.ResourceName == NativeNifEffectMaterial.ResourceIdentity)
                        NativeNifEffectMaterial.ApplyEmissiveColor(effect, new Vector3(color.R, color.G, color.B));
                    else if (material is ShaderMaterial shader)
                        shader.SetShaderParameter("emissive_color", new Vector3(color.R, color.G, color.B));
                    else if (material is StandardMaterial3D standard)
                    {
                        standard.EmissionEnabled = true;
                        standard.Emission = color;
                        if (standard.ShadingMode == BaseMaterial3D.ShadingModeEnum.Unshaded)
                            standard.AlbedoColor = new Color(color.R, color.G, color.B, standard.AlbedoColor.A);
                    }
                }
            });
        }

        private RuntimeNifControllerChannel BuildTextureTransformChannel(
            FalloutNifControllerSequence sequence,
            FalloutNifControllerLink link,
            int targetBlock)
        {
            if (link.PropertyType != "NiTexturingProperty" || link.Variable1 != "0-0-TT_TRANSLATE_V" ||
                !string.IsNullOrEmpty(link.Variable2) ||
                _source.ReadObject(link.Controller) is not FalloutNifTextureTransformController controller ||
                controller.ShaderMap || controller.TextureSlot != TextureTransformBaseSlot ||
                controller.Operation != TextureTransformTranslateV ||
                !ValidateManagedControllerTime(controller.Time, sequence) ||
                _source.ReadObject(controller.Interpolator) is not FalloutNifBlendFloatInterpolator blend ||
                !ValidateBlend(blend.Flags, blend.ArraySize, blend.WeightThreshold, blend.Value) ||
                _source.ReadObject(link.Interpolator) is not FalloutNifFloatInterpolator interpolator ||
                interpolator.Data == -1 ||
                _source.ReadObject(interpolator.Data) is not FalloutNifFloatData data ||
                !ValidateQuadraticKeys(data.Keys, sequence.StartTime, sequence.StopTime) ||
                !_source.ReadGeometry(targetBlock).Properties.Contains(controller.Time.Target) ||
                !_materials.TryGetValue(controller.Time.Target, out var materials))
                throw new NotSupportedException(
                    $"NIF sequence {sequence.Block.Index} texture-transform channel is incomplete.");
            return new RuntimeNifControllerChannel(time =>
            {
                var value = SampleScalar(data.Keys, time);
                foreach (var material in materials)
                {
                    if (material is ShaderMaterial effect && material.ResourceName == NativeNifEffectMaterial.ResourceIdentity)
                    {
                        var offset = effect.GetShaderParameter("source_uv_offset").AsVector2();
                        effect.SetShaderParameter("source_uv_offset", new Vector2(offset.X, value));
                    }
                    else if (material is StandardMaterial3D standard)
                        standard.Uv1Offset = new Vector3(standard.Uv1Offset.X, value, standard.Uv1Offset.Z);
                    else throw new NotSupportedException("Texture transform material has no parameter owner.");
                }
            });
        }

        private static bool ValidateManagedControllerTime(
            FalloutNifTimeController time,
            FalloutNifControllerSequence sequence) =>
            time.NextController == -1 && time.Flags == DormantMultiTargetFlags &&
            time.Frequency == 1.0f && time.Phase == 0.0f &&
            time.StartTime == sequence.StartTime && time.StopTime == sequence.StopTime &&
            time.Target != -1;

        private static bool ValidateBlend(
            byte flags,
            byte arraySize,
            float threshold,
            float value) =>
            flags == ManagerControlledBlendFlags &&
            arraySize == ManagerControlledBlendArraySize && threshold == 0.0f &&
            value == float.MinValue;

        private static bool ValidateBlend(
            byte flags,
            byte arraySize,
            float threshold,
            FalloutNifVector3 value) =>
            flags == ManagerControlledBlendFlags &&
            arraySize == ManagerControlledBlendArraySize && threshold == 0.0f &&
            value.X == float.MinValue && value.Y == float.MinValue && value.Z == float.MinValue;

        private static bool ValidateQuadraticKeys(
            IReadOnlyList<FalloutNifScalarKey> keys,
            float start,
            float stop) =>
            keys.Count >= 2 && keys[0].Time == start && keys[^1].Time == stop &&
            keys.All(key => key.Interpolation == QuadraticControllerKeyType &&
                key.Forward is not null && key.Backward is not null);

        private static bool ValidateQuadraticKeys(
            IReadOnlyList<FalloutNifVectorKey> keys,
            float start,
            float stop) =>
            keys.Count >= 2 && keys[0].Time == start && keys[^1].Time == stop &&
            keys.All(key => key.Interpolation == QuadraticControllerKeyType &&
                key.Forward is not null && key.Backward is not null);

        private static float SampleScalar(IReadOnlyList<FalloutNifScalarKey> keys, float time)
        {
            if (time <= keys[0].Time)
                return keys[0].Value;
            if (time >= keys[^1].Time)
                return keys[^1].Value;
            for (var index = 0; index + 1 < keys.Count; ++index)
            {
                var first = keys[index];
                var second = keys[index + 1];
                if (time > second.Time)
                    continue;
                var amount = (time - first.Time) / (second.Time - first.Time);
                var squared = amount * amount;
                var cubed = squared * amount;
                return first.Value * (2.0f * cubed - 3.0f * squared + 1.0f) +
                    second.Value * (-2.0f * cubed + 3.0f * squared) +
                    first.Backward!.Value * (cubed - 2.0f * squared + amount) +
                    second.Forward!.Value * (cubed - squared);
            }
            throw new InvalidDataException("NIF scalar controller interval was not found.");
        }

        private static FalloutNifVector3 SampleVector(
            IReadOnlyList<FalloutNifVectorKey> keys,
            float time)
        {
            if (time <= keys[0].Time)
                return keys[0].Value;
            if (time >= keys[^1].Time)
                return keys[^1].Value;
            for (var index = 0; index + 1 < keys.Count; ++index)
            {
                var first = keys[index];
                var second = keys[index + 1];
                if (time > second.Time)
                    continue;
                var amount = (time - first.Time) / (second.Time - first.Time);
                var squared = amount * amount;
                var cubed = squared * amount;
                return HermiteVector(
                    first.Value,
                    second.Value,
                    first.Backward!.Value,
                    second.Forward!.Value,
                    amount,
                    squared,
                    cubed);
            }
            throw new InvalidDataException("NIF vector controller interval was not found.");
        }

        private static FalloutNifVector3 HermiteVector(
            FalloutNifVector3 first,
            FalloutNifVector3 second,
            FalloutNifVector3 firstTangent,
            FalloutNifVector3 secondTangent,
            float amount,
            float squared,
            float cubed) => new(
            Hermite(first.X, second.X, firstTangent.X, secondTangent.X, amount, squared, cubed),
            Hermite(first.Y, second.Y, firstTangent.Y, secondTangent.Y, amount, squared, cubed),
            Hermite(first.Z, second.Z, firstTangent.Z, secondTangent.Z, amount, squared, cubed));

        private static float Hermite(
            float first,
            float second,
            float firstTangent,
            float secondTangent,
            float amount,
            float squared,
            float cubed) =>
            first * (2.0f * cubed - 3.0f * squared + 1.0f) +
            second * (-2.0f * cubed + 3.0f * squared) +
            firstTangent * (cubed - 2.0f * squared + amount) +
            secondTangent * (cubed - squared);

        private static float[] EulerXyzRowMajor(float x, float y, float z)
        {
            var halfX = x * HalfAngleScale;
            var halfY = y * HalfAngleScale;
            var halfZ = z * HalfAngleScale;
            var cx = MathF.Cos(halfX);
            var cy = MathF.Cos(halfY);
            var cz = MathF.Cos(halfZ);
            var sx = MathF.Sin(halfX);
            var sy = MathF.Sin(halfY);
            var sz = MathF.Sin(halfZ);
            var w = cx * cy * cz - sx * sy * sz;
            var qx = sx * cy * cz + cx * sy * sz;
            var qy = cx * sy * cz - sx * cy * sz;
            var qz = cx * cy * sz + sx * sy * cz;
            return
            [
                1.0f - 2.0f * (qy * qy + qz * qz), 2.0f * (qx * qy + qz * w),
                2.0f * (qx * qz - qy * w), 2.0f * (qx * qy - qz * w),
                1.0f - 2.0f * (qx * qx + qz * qz), 2.0f * (qy * qz + qx * w),
                2.0f * (qx * qz + qy * w), 2.0f * (qy * qz - qx * w),
                1.0f - 2.0f * (qx * qx + qy * qy),
            ];
        }

        private MeshInstance3D BuildGeometry(FalloutNifGeometry source)
        {
            RequirePlainVisualState(source.Block, source.Controller, [], [], -1);
            var collision = ValidateVisualCollision(source.Block, source.CollisionObject);
            _ = ValidateExtraData(
                source.Block,
                source.ExtraData,
                collisionContract: collision is not null);
            if (source.MaterialNames.Length != 0 || source.MaterialExtraData.Length != 0)
                throw new NotSupportedException(
                    $"NIF geometry {source.Block.Index} uses an unsupported material table.");
            if (source.Data == -1)
                throw new InvalidDataException($"NIF geometry {source.Block.Index} has no mesh data.");
            var data = ReadOwnedMeshData(source);
            if (data.AdditionalData != -1)
                throw new NotSupportedException(
                    $"NIF geometry data {data.Block.Index} has unsupported additional vertex data.");
            if (data.Vertices.Length == 0 || data.Triangles.Length == 0)
                throw new InvalidDataException($"NIF geometry data {data.Block.Index} is empty.");
            RequireAttributeCount(data.Normals.Length, data.Vertices.Length, "normals", data.Block.Index);
            RequireAttributeCount(data.Tangents.Length, data.Vertices.Length, "tangents", data.Block.Index);
            RequireAttributeCount(data.Bitangents.Length, data.Vertices.Length, "bitangents", data.Block.Index);
            RequireAttributeCount(data.Colors.Length, data.Vertices.Length, "colors", data.Block.Index);
            if (data.TextureCoordinates.Length > 1)
                throw new NotSupportedException(
                    $"NIF geometry data {data.Block.Index} has more than one UV set.");
            if (data.TextureCoordinates.Length == 1)
                RequireAttributeCount(data.TextureCoordinates[0].Length, data.Vertices.Length,
                    "UVs", data.Block.Index);

            FalloutNifOneBoneBinding? skinBinding = null;
            FalloutNifNode? skinBone = null;
            if (source.SkinInstance != -1)
            {
                var instance = _source.ReadObject(source.SkinInstance) as FalloutNifSkinInstance ??
                    throw new InvalidDataException(
                        $"NIF geometry {source.Block.Index} has an invalid skin-instance block.");
                var skinData = _source.ReadObject(instance.Data) as FalloutNifSkinData ??
                    throw new InvalidDataException(
                        $"NIF skin {instance.Block.Index} has an invalid skin-data block.");
                var partition = _source.ReadObject(instance.SkinPartition) as FalloutNifSkinPartition ??
                    throw new InvalidDataException(
                        $"NIF skin {instance.Block.Index} has an invalid skin-partition block.");
                skinBinding = FalloutNifOneBoneSkin.Validate(
                    instance, skinData, partition, data.Vertices.Length);
                var skeletonRoot = _source.ReadNode(skinBinding.SkeletonRoot);
                skinBone = _source.ReadNode(skinBinding.Bone);
                if (!skeletonRoot.Children.Contains(source.Block.Index) ||
                    !skeletonRoot.Children.Contains(skinBinding.Bone))
                    throw new NotSupportedException(
                        $"NIF skin {instance.Block.Index} does not use the proven sibling-root hierarchy.");
            }

            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            var vertices = new Vector3[data.Vertices.Length];
            for (var index = 0; index < vertices.Length; ++index)
                vertices[index] = ConvertVector(data.Vertices[index]) * _unitsToMetres;
            arrays[(int)Mesh.ArrayType.Vertex] = vertices;
            if (data.Normals.Length != 0)
            {
                var normals = new Vector3[data.Normals.Length];
                for (var index = 0; index < normals.Length; ++index)
                    normals[index] = ConvertVector(data.Normals[index]).Normalized();
                arrays[(int)Mesh.ArrayType.Normal] = normals;
            }
            if (data.Tangents.Length != 0)
                arrays[(int)Mesh.ArrayType.Tangent] = BuildTangents(data);
            if (data.Colors.Length != 0)
                arrays[(int)Mesh.ArrayType.Color] =
                    data.Colors.Select(color => new Color(color.R, color.G, color.B, color.A)).ToArray();
            if (data.TextureCoordinates.Length == 1)
                arrays[(int)Mesh.ArrayType.TexUV] =
                    data.TextureCoordinates[0].Select(uv => new Vector2(uv.U, uv.V)).ToArray();
            if (skinBinding is not null)
            {
                arrays[(int)Mesh.ArrayType.Bones] = skinBinding.BoneIndices;
                arrays[(int)Mesh.ArrayType.Weights] = skinBinding.Weights;
            }

            var indices = FalloutNifTriangleWinding.ToGodotIndices(data.Triangles);
            if (indices.Length == 0)
                throw new InvalidDataException(
                    $"NIF geometry data {data.Block.Index} contains no non-degenerate triangles.");
            arrays[(int)Mesh.ArrayType.Index] = indices;
            var mesh = new ArrayMesh();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays,
                BuildMorphArrays(mesh, source, data, arrays, Enumerable.Range(0, data.Vertices.Length).ToArray()));
            mesh.SurfaceSetMaterial(0, BuildMaterial(source));
            var result = new MeshInstance3D
            {
                Name = SourceName(source.Name, source.Block.Index),
                Transform = ConvertTransform(source.Transform),
                Visible = (source.Flags & HiddenFlag) == 0,
                Mesh = mesh,
            };
            result.SetMeta("opennv_nif_geometry_block", source.Block.Index);
            result.SetMeta("opennv_nif_source_name", source.Name);
            if (RigidFaceBind is { } faceBind)
            {
                if (skinBinding is not null)
                    throw new NotSupportedException("Rigid FaceGen shape must be unskinned.");
                // FaceGen uses the head's model basis, including its scale.
                // Component export rotation/scale are not applied again; owned
                // mouth/eye exports also contain non-unit rounding residues.
                result.Transform = faceBind * new Transform3D(Basis.Identity,
                    ConvertVector(source.Transform.Translation) * _unitsToMetres);
            }
            _nodes.Add(source.Block.Index, result);
            if (skinBinding is not null && skinBone is not null)
            {
                var boneName = SourceName(skinBone.Name, skinBone.Block.Index);
                var skin = new Skin();
                skin.SetBindCount(1);
                skin.SetBindBone(0, 0);
                skin.SetBindName(0, boneName);
                skin.SetBindPose(0, ConvertTransform(skinBinding.InverseBind));
                result.Skin = skin;
                result.Skeleton = new NodePath($"../{boneName}");
                result.SetMeta("opennv_nif_skin_instance", source.SkinInstance);
            }
            PreserveCollisionMetadata(result, collision);
            AddCollision(result, collision);
            NodeCount++;
            SurfaceCount++;
            VertexCount += vertices.Length;
            TriangleCount += indices.Length / 3;
            return result;
        }

        private Godot.Collections.Array<Godot.Collections.Array> BuildMorphArrays(ArrayMesh mesh, FalloutNifGeometry geometry,
            FalloutNifMeshData data, Godot.Collections.Array sourceArrays, int[] vertexMap)
        {
            var result = new Godot.Collections.Array<Godot.Collections.Array>();
            if (MorphOwner is null) return result;
            // Godot packs every blend-shape normal/tangent as a unit direction;
            // a zero relative delta cannot survive that representation. Use
            // absolute targets with normalized blending so the source basis
            // cancels independently of simultaneous expression weights.
            mesh.BlendShapeMode = Mesh.BlendShapeMode.Normalized;
            var baseVertices = sourceArrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            foreach (var (name, values) in MorphOwner(_source, geometry, data))
            {
                if (values.Length != data.Vertices.Length) throw new InvalidDataException("Source morph vertex order differs from geometry.");
                mesh.AddBlendShape(name);
                var arrays = new Godot.Collections.Array();
                arrays.Resize((int)Mesh.ArrayType.Max);
                arrays[(int)Mesh.ArrayType.Vertex] = vertexMap.Select((index, row) => baseVertices[row] +
                    GamebryoCoordinate.ConvertVector(new(values[index].X, values[index].Y, values[index].Z)) * _unitsToMetres).ToArray();
                // Dynamic expression normal recomputation is a separate lane;
                // blinking must not add an unrelated packed unit direction.
                if (sourceArrays[(int)Mesh.ArrayType.Normal].VariantType != Variant.Type.Nil)
                    arrays[(int)Mesh.ArrayType.Normal] = sourceArrays[(int)Mesh.ArrayType.Normal];
                if (sourceArrays[(int)Mesh.ArrayType.Tangent].VariantType != Variant.Type.Nil)
                    arrays[(int)Mesh.ArrayType.Tangent] = sourceArrays[(int)Mesh.ArrayType.Tangent];
                result.Add(arrays);
            }
            return result;
        }

        private FalloutNifMeshData ReadOwnedMeshData(FalloutNifGeometry geometry)
        {
            var source = _source.ReadMeshData(geometry.Data);
            if (_geometryOwner is null)
                return source;
            var result = _geometryOwner(_source, geometry, source);
            if (result.Block != source.Block || result.Vertices.Length != source.Vertices.Length ||
                !result.Triangles.SequenceEqual(source.Triangles))
                throw new InvalidDataException("Actor geometry owner changed source mesh identity, vertex count or topology.");
            return result;
        }

        private bool ValidateExtraData(
            FalloutNifBlock owner,
            IReadOnlyList<int> references,
            bool collisionContract)
        {
            var omitEditorMarkers = false;
            foreach (var reference in references.Where(reference => reference != -1))
            {
                switch (_source.ReadObject(reference))
                {
                    case FalloutNifBsxFlags flags:
                        var hasEditorMarker = HasDirectEditorMarker(owner);
                        try
                        {
                            FalloutNifBsxContract.Validate(flags.Flags, new FalloutNifBsxEvidence(
                                collisionContract,
                                HasReachableBlendCollision(owner.Index, []),
                                HasReachableConstrainedCollision(owner.Index, []),
                                hasEditorMarker,
                                HasExternalEmittanceShader()));
                        }
                        catch (NotSupportedException error)
                        {
                            throw new NotSupportedException(
                                $"NIF visual block {owner.Index} has unsupported BSX flags " +
                                $"0x{flags.Flags:x8}: {error.Message}", error);
                        }
                        omitEditorMarkers |=
                            (flags.Flags & FalloutNifBsxContract.EditorMarkers) != 0;
                        break;
                    case FalloutNifStringExtraData:
                    case FalloutNifIntegerExtraData:
                    case FalloutNifFloatExtraData:
                    case FalloutNifDecalPlacementExtraData:
                    case FalloutNifTextKeyExtraData:
                    case FalloutNifBound:
                    case FalloutNifFurnitureMarker:
                        break;
                    default:
                        throw new NotSupportedException(
                            $"NIF visual block {owner.Index} has unsupported extra-data block {reference} " +
                            $"({_source.ReadObject(reference).Block.TypeName}).");
                }
            }
            return omitEditorMarkers;
        }

        private bool HasDirectEditorMarker(FalloutNifBlock owner) =>
            owner.TypeName is "NiNode" or "NiBone" or "BSFadeNode" &&
            _source.ReadNode(owner.Index).Children
                .Any(reference => reference != -1 && IsEditorMarkerRoot(reference));

        private bool HasReachableBlendCollision(int blockIndex, HashSet<int> visited) =>
            HasReachableCollisionMatching(blockIndex, visited, collision => collision.IsBlend);

        private bool HasReachableConstrainedCollision(int blockIndex, HashSet<int> visited) =>
            HasReachableCollisionMatching(blockIndex, visited, collision =>
                collision.Body != -1 &&
                _source.ReadObject(collision.Body) is FalloutNifRigidBody body &&
                body.Constraints.Length != 0);

        private bool HasReachableCollisionMatching(
            int blockIndex,
            HashSet<int> visited,
            Func<FalloutNifCollisionObject, bool> predicate)
        {
            if (!visited.Add(blockIndex))
                return false;
            var block = _source.Blocks[blockIndex];
            if (block.TypeName is "NiNode" or "NiBone" or "BSFadeNode")
            {
                var node = _source.ReadNode(blockIndex);
                if (node.CollisionObject != -1 &&
                    _source.ReadObject(node.CollisionObject) is FalloutNifCollisionObject collision &&
                    predicate(collision))
                    return true;
                return node.Children.Where(reference => reference != -1)
                    .Any(reference => HasReachableCollisionMatching(reference, visited, predicate));
            }
            if (block.TypeName is "NiTriShape" or "NiTriStrips")
            {
                var geometry = _source.ReadGeometry(blockIndex);
                return geometry.CollisionObject != -1 &&
                    _source.ReadObject(geometry.CollisionObject) is FalloutNifCollisionObject collision &&
                    predicate(collision);
            }
            return false;
        }

        private bool IsEditorMarkerRoot(int blockIndex) =>
            _source.Blocks[blockIndex].TypeName is "NiNode" or "NiBone" or "BSFadeNode" &&
            _source.ReadNode(blockIndex).Name.Equals("EditorMarker", StringComparison.Ordinal);

        private void ValidateEditorMarkerTree(int blockIndex)
        {
            var marker = _source.ReadNode(blockIndex);
            if (!marker.Name.Equals("EditorMarker", StringComparison.Ordinal) ||
                marker.Controller != -1 || marker.CollisionObject != -1 ||
                marker.ExtraData.Any(reference => reference != -1) ||
                marker.Properties.Any(reference => reference != -1) ||
                marker.Effects.Any(reference => reference != -1) || marker.Children.Length == 0)
                throw new NotSupportedException(
                    $"NIF editor-marker node {blockIndex} is outside the admitted non-runtime contract.");
            foreach (var childReference in marker.Children)
            {
                if (childReference == -1)
                    continue;
                if (_source.ReadObject(childReference) is not FalloutNifGeometry geometry ||
                    !geometry.Name.StartsWith("EditorMarker:", StringComparison.Ordinal) ||
                    geometry.Controller != -1 || geometry.CollisionObject != -1 ||
                    geometry.SkinInstance != -1 || geometry.Data == -1 ||
                    geometry.MaterialNames.Length != 0 || geometry.MaterialExtraData.Length != 0 ||
                    geometry.ExtraData.Any(reference => reference != -1) ||
                    geometry.Properties.Any(reference => reference != -1 &&
                        _source.ReadObject(reference) is not (FalloutNifNoLightingProperty or
                            FalloutNifMaterialProperty or FalloutNifAlphaProperty)))
                    throw new NotSupportedException(
                        $"NIF editor-marker child {childReference} is outside the admitted geometry contract.");
                var mesh = _source.ReadMeshData(geometry.Data);
                if (mesh.Vertices.Length == 0 || mesh.Triangles.Length == 0)
                    throw new InvalidDataException(
                        $"NIF editor-marker child {childReference} has empty geometry.");
            }
        }

        private void PreserveExtraDataMetadata(Node3D node, IReadOnlyList<int> references)
        {
            var integerIndex = 0;
            foreach (var reference in references.Where(reference => reference != -1))
            {
                switch (_source.ReadObject(reference))
                {
                    case FalloutNifBsxFlags flags:
                        node.SetMeta("opennv_nif_bsx_flags", flags.Flags);
                        break;
                    case FalloutNifFurnitureMarker furniture:
                        node.SetMeta("opennv_nif_furniture_positions", furniture.Positions.Length);
                        break;
                    case FalloutNifIntegerExtraData integer:
                        node.SetMeta($"opennv_nif_integer_extra_data_{integerIndex}_name", integer.Name);
                        node.SetMeta($"opennv_nif_integer_extra_data_{integerIndex}_value", integer.Value);
                        integerIndex++;
                        break;
                }
            }
            if (integerIndex != 0)
                node.SetMeta("opennv_nif_integer_extra_data_count", integerIndex);
        }

        private bool HasExternalEmittanceShader() => _source.Blocks
            .Where(block => block.TypeName is "BSShaderPPLightingProperty" or
                "BSShaderNoLightingProperty")
            .Select(block => _source.ReadObject(block.Index))
            .Any(shader => shader switch
            {
                FalloutNifShaderProperty lighting =>
                    (lighting.ShaderFlags & ShaderFlagExternalEmittance) != 0,
                FalloutNifNoLightingProperty unlit =>
                    (unlit.ShaderFlags & ShaderFlagExternalEmittance) != 0,
                _ => false,
            });

        private FalloutNifCollisionObject? ValidateVisualCollision(
            FalloutNifBlock owner,
            int reference)
        {
            if (reference == -1)
                return null;
            if (_source.ReadObject(reference) is not FalloutNifCollisionObject collision ||
                collision.Target != owner.Index ||
                (collision.Flags & ~SupportedCollisionFlags) != 0 ||
                collision.Body == -1 ||
                _source.Blocks[collision.Body].TypeName is not ("bhkRigidBody" or "bhkRigidBodyT"))
                throw new NotSupportedException(
                    $"NIF visual block {owner.Index} has an unsupported collision attachment.");
            return collision;
        }

        private static void PreserveCollisionMetadata(
            Node3D node,
            FalloutNifCollisionObject? collision)
        {
            if (collision is null)
                return;
            node.SetMeta("opennv_nif_collision_object", collision.Block.Index);
            node.SetMeta("opennv_nif_collision_body", collision.Body);
            node.SetMeta("opennv_nif_blend_collision", collision.IsBlend);
            node.SetMeta("opennv_nif_collision_flags", collision.Flags);
        }

        private void AddCollision(Node3D owner, FalloutNifCollisionObject? attachment)
        {
            if (attachment is null)
                return;
            var built = NativeNifCollisionBuilder.Build(_source, attachment, _unitsToMetres);
            owner.AddChild(built.Body);
            CollisionBodyCount++;
            CollisionShapeCount += built.Shapes;
            CollisionTriangleCount += built.Triangles;
        }

        internal Material BuildMaterial(FalloutNifGeometry geometry, Color? hairColor = null)
        {
            var overridden = _materialOverride?.Invoke(_source, geometry);
            if (overridden is not null)
                return overridden;
            var result = BuildMaterialCore(geometry, hairColor);
            if (result is not StandardMaterial3D && result.ResourceName is not
                (NativeNifLightingMaterial.ResourceIdentity or NativeNifEffectMaterial.ResourceIdentity))
                return result;
            foreach (var reference in geometry.Properties.Where(reference => reference != -1))
            {
                if (_source.ReadObject(reference) is not (FalloutNifMaterialProperty or
                    FalloutNifTexturingProperty))
                    continue;
                if (!_materials.TryGetValue(reference, out var values))
                {
                    values = [];
                    _materials.Add(reference, values);
                }
                values.Add(result);
            }
            return result;
        }

        private Material BuildMaterialCore(FalloutNifGeometry geometry, Color? hairColor)
        {
            FalloutNifShaderProperty? shader = null;
            FalloutNifNoLightingProperty? noLighting = null;
            FalloutNifMaterialProperty? material = null;
            FalloutNifAlphaProperty? alpha = null;
            FalloutNifTexturingProperty? texturing = null;
            FalloutNifStencilProperty? stencil = null;
            foreach (var reference in geometry.Properties.Where(reference => reference != -1))
            {
                switch (_source.ReadObject(reference))
                {
                    case FalloutNifShaderProperty value when shader is null:
                        shader = value;
                        break;
                    case FalloutNifMaterialProperty value when material is null:
                        material = value;
                        break;
                    case FalloutNifNoLightingProperty value when noLighting is null:
                        noLighting = value;
                        break;
                    case FalloutNifAlphaProperty value when alpha is null:
                        alpha = value;
                        break;
                    case FalloutNifTexturingProperty value when texturing is null:
                        texturing = value;
                        break;
                    case FalloutNifStencilProperty value when stencil is null:
                        stencil = value;
                        break;
                    case FalloutNifShaderProperty:
                        throw new NotSupportedException(
                            $"NIF geometry {geometry.Block.Index} has multiple lighting shaders.");
                    case FalloutNifMaterialProperty:
                        throw new NotSupportedException(
                            $"NIF geometry {geometry.Block.Index} has multiple material properties.");
                    case FalloutNifNoLightingProperty:
                        throw new NotSupportedException(
                            $"NIF geometry {geometry.Block.Index} has multiple no-lighting shaders.");
                    case FalloutNifAlphaProperty:
                        throw new NotSupportedException(
                            $"NIF geometry {geometry.Block.Index} has multiple alpha properties.");
                    case FalloutNifTexturingProperty:
                        throw new NotSupportedException(
                            $"NIF geometry {geometry.Block.Index} has multiple legacy texturing properties.");
                    case FalloutNifStencilProperty:
                        throw new NotSupportedException(
                            $"NIF geometry {geometry.Block.Index} has multiple stencil properties.");
                    default:
                        throw new NotSupportedException(
                            $"NIF geometry {geometry.Block.Index} has unsupported property block {reference}.");
                }
            }
            if (shader is null && noLighting is null)
                return BuildVertexMaterialOnly(geometry, material, alpha, texturing, stencil);
            if (shader is not null && noLighting is not null)
                throw new NotSupportedException(
                    $"NIF geometry {geometry.Block.Index} must have exactly one supported shader.");
            if (noLighting is not null)
                return BuildNoLightingMaterial(noLighting, material, alpha, texturing, stencil);
            if (texturing is not null)
                throw new NotSupportedException(
                    $"NIF geometry {geometry.Block.Index} combines legacy texturing with a lighting shader.");
            if (shader is null)
                throw new InvalidDataException(
                    $"NIF geometry {geometry.Block.Index} lost its validated lighting shader.");
            var supportedLightingFlags = SupportedShaderFlags |
                (geometry.SkinInstance == -1 ? 0U : ShaderFlagSkinned) |
                (hairColor.HasValue ? FalloutNpcAppearanceHairColor.ShaderFlag : 0U);
            if (shader.Controller != -1 || shader.ExtraData.Any(reference => reference != -1) ||
                shader.ShaderType != SupportedShaderType ||
                (shader.ShaderFlags & ~supportedLightingFlags) != 0 ||
                (shader.ShaderFlags2 & ~SupportedShaderFlags2) != 0 ||
                shader.RefractionStrength != 0.0f || shader.RefractionFirePeriod != 0)
                throw new NotSupportedException(
                    $"NIF shader {shader.Block.Index} uses unsupported lighting semantics: " +
                    $"type={shader.ShaderType} flags1=0x{shader.ShaderFlags:x8} " +
                    $"flags2=0x{shader.ShaderFlags2:x8} clamp={shader.TextureClampMode} " +
                    $"refraction={shader.RefractionStrength}/{shader.RefractionFirePeriod}.");
            var windowEnvironment = (shader.ShaderFlags & ShaderFlagWindowEnvironmentMapping) != 0;
            var eyeEnvironment = (shader.ShaderFlags & ShaderFlagEyeEnvironmentMapping) != 0;
            var environment = (shader.ShaderFlags & ShaderFlagEnvironmentMapping) != 0 ||
                windowEnvironment || eyeEnvironment;
            if (shader.TextureSet == -1 ||
                _source.ReadObject(shader.TextureSet) is not FalloutNifShaderTextureSet textures)
                throw new NotSupportedException(
                    $"NIF shader {shader.Block.Index} has no decoded texture set: " +
                    $"flags1=0x{shader.ShaderFlags:x8} textureSet={shader.TextureSet}.");
            if (textures.Textures.Length == FalloutTextureSlots &&
                textures.Textures[EnvironmentTextureSlot].Length != 0)
                environment = true;
            if (textures.Textures.Length != FalloutTextureSlots ||
                (!environment && textures.Textures.Skip(EmissiveTextureSlot + 1)
                    .Any(path => !string.IsNullOrEmpty(path))) ||
                (environment && textures.Textures[3].Length != 0))
                throw new NotSupportedException(
                    $"NIF shader {shader.Block.Index} uses an unsupported texture set: " +
                    $"flags1=0x{shader.ShaderFlags:x8} slots=" +
                    $"[{string.Join(',', textures.Textures.Select((path, index) => $"{index}:{path}"))}].");

            var result = new StandardMaterial3D
            {
                TextureRepeat = FalloutNifTextureAddressing.RepeatForGodot(shader.TextureClampMode),
                Metallic = 0.0f,
                SpecularMode = (shader.ShaderFlags & ShaderFlagSpecular) == 0
                    ? BaseMaterial3D.SpecularModeEnum.Disabled
                    : BaseMaterial3D.SpecularModeEnum.SchlickGgx,
                Roughness = material is null ? 1.0f : GlossToRoughness(material.Glossiness),
                VertexColorUseAsAlbedo =
                    (shader.ShaderFlags & ShaderFlagVertexAlpha) != 0,
            };
            if (material is not null)
            {
                if ((material.Controller != -1 &&
                        !IsManagedMaterialController(material.Controller, material.Block.Index)) ||
                    material.ExtraData.Any(reference => reference != -1))
                    throw new NotSupportedException(
                        $"NIF material {material.Block.Index} uses unsupported controllers or extra data.");
                result.AlbedoColor = new Color(1.0f, 1.0f, 1.0f, material.Alpha);
                if (material.Controller != -1 || (material.EmissiveMultiple > 0.0f &&
                    (material.Emissive.R != 0.0f || material.Emissive.G != 0.0f || material.Emissive.B != 0.0f))
                    )
                {
                    result.EmissionEnabled = true;
                    result.Emission = new Color(material.Emissive.R, material.Emissive.G, material.Emissive.B);
                    result.EmissionEnergyMultiplier = material.EmissiveMultiple;
                }
            }
            result.AlbedoTexture = LoadTexture(textures.Textures[DiffuseTextureSlot], normal: false);
            var normal = LoadTexture(textures.Textures[NormalTextureSlot], normal: true);
            if (normal is not null)
            {
                result.NormalEnabled = true;
                result.NormalTexture = normal;
            }
            var emissive = LoadTexture(textures.Textures[EmissiveTextureSlot], normal: false);
            if (emissive is not null)
            {
                result.EmissionEnabled = true;
                result.Emission = Colors.White;
                result.EmissionTexture = emissive;
                result.EmissionOperator = BaseMaterial3D.EmissionOperatorEnum.Multiply;
            }
            if (environment)
            {
                if (normal is null)
                    throw new NotSupportedException(
                        $"NIF environment shader {shader.Block.Index} has no tangent-space normal map.");
                var environmentMask = LoadTexture(
                    textures.Textures[EnvironmentMaskTextureSlot], normal: false);
                var environmentScale = eyeEnvironment ? 1.0f : shader.EnvironmentMapScale;
                if (textures.Textures[EnvironmentTextureSlot].Length == 0)
                {
                    result.Metallic = Math.Clamp(environmentScale, 0.0f, 1.0f);
                    result.MetallicTexture = environmentMask;
                }
                else
                {
                    result.NextPass = BuildEnvironmentPass(
                        LoadCubemap(textures.Textures[EnvironmentTextureSlot]),
                        normal,
                        environmentMask,
                        environmentScale,
                        (shader.ShaderFlags2 & ShaderFlagEnvironmentMapLightFade) != 0);
                }
            }
            if ((shader.ShaderFlags & FalloutNpcAppearanceHairColor.ShaderFlag) != 0)
            {
                var tint = hairColor ?? throw new InvalidDataException("Hair shader has no actor colour owner.");
                result.AlbedoColor = new Color(tint.R, tint.G, tint.B, result.AlbedoColor.A);
                result.SetMeta("opennv_hair_rgb", new Vector3(tint.R, tint.G, tint.B));
                result.SetMeta("opennv_hair_lighting_parity", "unverified");
            }
            ApplyAlpha(result, alpha);
            ApplyStencil(result, stencil, environment);
            var meshData = _source.ReadMeshData(geometry.Data);
            var vertexColors = FalloutNifVertexColorState.Resolve(shader.ShaderFlags2,
                meshData.Vertices.Length, meshData.Colors.Length);
            return NativeNifLightingMaterial.Build(result, shader, material, alpha, vertexColors);
        }

        private StandardMaterial3D BuildVertexMaterialOnly(
            FalloutNifGeometry geometry,
            FalloutNifMaterialProperty? material,
            FalloutNifAlphaProperty? alpha,
            FalloutNifTexturingProperty? texturing,
            FalloutNifStencilProperty? stencil)
        {
            if (material is null || alpha is not null || texturing is not null || stencil is not null ||
                geometry.Properties.Count(reference => reference != -1) != 1 ||
                material.Controller != -1 || material.ExtraData.Any(reference => reference != -1))
                throw new NotSupportedException(
                    $"NIF geometry {geometry.Block.Index} has an unsupported shaderless material contract.");
            var mesh = _source.ReadMeshData(geometry.Data);
            if (mesh.Colors.Length != mesh.Vertices.Length || mesh.Colors.Length == 0 ||
                mesh.Normals.Length != mesh.Vertices.Length ||
                mesh.TextureCoordinates.Length != 0)
                throw new NotSupportedException(
                    $"NIF geometry {geometry.Block.Index} lacks complete source vertex material inputs.");
            return new StandardMaterial3D
            {
                VertexColorUseAsAlbedo = true,
                AlbedoColor = new Color(1.0f, 1.0f, 1.0f, material.Alpha),
                Metallic = 0.0f,
                Roughness = GlossToRoughness(material.Glossiness),
                ResourceName = $"NIF vertex material {material.Block.Index}",
            };
        }

        private Material BuildNoLightingMaterial(
            FalloutNifNoLightingProperty shader,
            FalloutNifMaterialProperty? material,
            FalloutNifAlphaProperty? alpha,
            FalloutNifTexturingProperty? texturing,
            FalloutNifStencilProperty? stencil)
        {
            // NOLIGHT programs consume the property-owned 2D diffuse texture.
            // The common specular/environment bits do not introduce lighting
            // passes or another texture slot in this shader family. The owned
            // NOLIGHT vertex/pixel program inventory confirms this contract.
            var supportedShaderFlags = SupportedNoLightingShaderFlags |
                (texturing is null ? 0U : ShaderFlagAlphaTexture);
            if (shader.Controller != -1 || shader.ExtraData.Any(reference => reference != -1) ||
                shader.ShaderType != SupportedNoLightingShaderType ||
                (shader.ShaderFlags & ~supportedShaderFlags) != 0 ||
                (shader.ShaderFlags2 & ~SupportedNoLightingShaderFlags2) != 0)
                throw new NotSupportedException(
                    $"NIF no-lighting shader {shader.Block.Index} uses unsupported semantics: " +
                    $"type={shader.ShaderType} flags1=0x{shader.ShaderFlags:x8} " +
                    $"flags2=0x{shader.ShaderFlags2:x8} clamp={shader.TextureClampMode} " +
                    $"environmentScale={shader.EnvironmentMapScale:R} " +
                    $"falloff={shader.FalloffStartAngle:R}/{shader.FalloffStopAngle:R}/" +
                    $"{shader.FalloffStartOpacity:R}/{shader.FalloffStopOpacity:R}.");
            if (material is not null &&
                ((material.Controller != -1 &&
                    !IsManagedMaterialController(material.Controller, material.Block.Index)) ||
                 material.ExtraData.Any(reference => reference != -1)))
                throw new NotSupportedException(
                    $"NIF material {material.Block.Index} uses unsupported controllers or extra data.");
            var texturePath = shader.FileName;
            if (texturing is not null)
            {
                texturePath = ValidateLegacyTexturing(texturing);
                if (!texturePath.Equals(shader.FileName, StringComparison.OrdinalIgnoreCase))
                    throw new NotSupportedException(
                        $"NIF legacy texturing property {texturing.Block.Index} differs from its no-lighting shader texture.");
            }
            using var result = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                VertexColorUseAsAlbedo = true,
                TextureRepeat = FalloutNifTextureAddressing.RepeatForGodot(shader.TextureClampMode),
                AlbedoTexture = LoadTexture(texturePath, normal: false),
                AlbedoColor = new Color(1.0f, 1.0f, 1.0f, material?.Alpha ?? 1.0f),
            };
            ApplyAlpha(result, alpha);
            if ((shader.ShaderFlags2 & ShaderFlagZBufferWrite) == 0)
                result.DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled;
            ApplyStencil(result, stencil, environmentPass: false);
            return NativeNifEffectMaterial.Build(shader, material, alpha,
                result.AlbedoTexture,
                result.CullMode == BaseMaterial3D.CullModeEnum.Disabled);
        }

        private string ValidateLegacyTexturing(FalloutNifTexturingProperty texturing)
        {
            if ((texturing.Controller != -1 &&
                    !IsManagedTextureController(texturing.Controller, texturing.Block.Index)) ||
                texturing.ExtraData.Any(reference => reference != -1) ||
                texturing.Flags != LegacyTexturingFlags ||
                texturing.TextureCount != LegacyTextureSlotCount ||
                texturing.BaseTexture is not { } descriptor ||
                descriptor.Flags != LegacyBaseTextureFlags ||
                descriptor.UvSet != LegacyTextureUvSet ||
                (descriptor.Transform is not null && !IsIdentityTextureTransform(descriptor.Transform)) ||
                texturing.DarkTexture is not null || texturing.DetailTexture is not null ||
                texturing.GlossTexture is not null || texturing.GlowTexture is not null ||
                texturing.BumpTexture is not null || texturing.BumpParameters is not null ||
                texturing.NormalTexture is not null || texturing.ParallaxTexture is not null ||
                texturing.ParallaxOffset is not null || texturing.Decal0Texture is not null ||
                texturing.Decal1Texture is not null || texturing.ShaderTextures.Length != 0)
                throw new NotSupportedException(
                    $"NIF legacy texturing property {texturing.Block.Index} is outside the supported base-only contract " +
                    $"(flags=0x{texturing.Flags:x4}, slots={texturing.TextureCount}, base={texturing.BaseTexture is not null}, " +
                    $"dark={texturing.DarkTexture is not null}, detail={texturing.DetailTexture is not null}, " +
                    $"gloss={texturing.GlossTexture is not null}, glow={texturing.GlowTexture is not null}, " +
                    $"bump={texturing.BumpTexture is not null}, normal={texturing.NormalTexture is not null}, " +
                    $"parallax={texturing.ParallaxTexture is not null}, decal0={texturing.Decal0Texture is not null}, " +
                    $"decal1={texturing.Decal1Texture is not null}, shaderTextures={texturing.ShaderTextures.Length}, " +
                    $"controller={texturing.Controller}, extra=[{string.Join(',', texturing.ExtraData)}], " +
                    $"baseFlags=0x{texturing.BaseTexture?.Flags:x4}, baseUv={texturing.BaseTexture?.UvSet}, " +
                    $"baseTransform={FormatTextureTransform(texturing.BaseTexture?.Transform)}).");
            if (_source.ReadObject(descriptor.Source) is not FalloutNifSourceTexture source ||
                source.Controller != -1 || source.ExtraData.Any(reference => reference != -1) ||
                source.UnknownLink != -1 || source.PixelLayout != LegacySourcePixelLayout ||
                source.MipmapMode != LegacySourceMipmapMode ||
                source.AlphaFormat > 3 || source.StaticFlag != 1 ||
                !source.DirectRender || source.PersistentRenderData ||
                string.IsNullOrWhiteSpace(source.FileName))
                throw new NotSupportedException(
                    $"NIF legacy texturing property {texturing.Block.Index} has an unsupported source texture contract.");
            // NiTexture::FormatPrefs requests a storage format; it is not draw
            // state. External DDS pixels already carry that format. Preserve
            // their alpha and let NiAlphaProperty own blending and testing.
            return source.FileName;
        }

        private static string FormatTextureTransform(FalloutNifTextureTransform? transform) =>
            transform is null ? "none" :
            $"offset={transform.Translation.U:R},{transform.Translation.V:R};" +
            $"scale={transform.Tiling.U:R},{transform.Tiling.V:R};" +
            $"rotation={transform.Rotation:R};method={transform.TransformType};" +
            $"center={transform.Center.U:R},{transform.Center.V:R}";

        private static bool IsIdentityTextureTransform(FalloutNifTextureTransform transform) =>
            transform.Translation.U == 0.0f && transform.Translation.V == 0.0f &&
            transform.Tiling.U == 1.0f && transform.Tiling.V == 1.0f &&
            transform.Rotation == 0.0f;

        private bool IsManagedMaterialController(int reference, int target) =>
            _source.ReadObject(reference) is FalloutNifMaterialColorController controller &&
            controller.Time.Target == target && controller.TargetColor == MaterialColorSelfIllumination;

        private bool IsManagedTextureController(int reference, int target) =>
            _source.ReadObject(reference) is FalloutNifTextureTransformController controller &&
            controller.Time.Target == target && !controller.ShaderMap &&
            controller.TextureSlot == TextureTransformBaseSlot &&
            controller.Operation == TextureTransformTranslateV;

        private static void ApplyStencil(
            StandardMaterial3D material,
            FalloutNifStencilProperty? stencil,
            bool environmentPass)
        {
            if (stencil is null)
                return;
            if (stencil.Controller != -1 || stencil.ExtraData.Any(reference => reference != -1) ||
                stencil.Flags != DisabledDoubleSidedStencilFlags || stencil.Reference != 0 ||
                stencil.Mask != uint.MaxValue)
                throw new NotSupportedException(
                    $"NIF stencil property {stencil.Block.Index} uses unsupported stencil semantics: " +
                    $"flags=0x{stencil.Flags:x4} reference={stencil.Reference} mask=0x{stencil.Mask:x8}.");
            if (environmentPass)
                throw new NotSupportedException(
                    $"NIF stencil property {stencil.Block.Index} requires a double-sided environment pass.");
            material.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
        }

        private static void ApplyAlpha(StandardMaterial3D material, FalloutNifAlphaProperty? alpha)
        {
            if (alpha is null)
                return;
            if (alpha.Controller != -1 || alpha.ExtraData.Any(reference => reference != -1))
                throw new NotSupportedException(
                    $"NIF alpha property {alpha.Block.Index} uses unsupported controllers or extra data.");
            var state = FalloutNifAlphaState.Read(alpha.Flags, alpha.Threshold);
            material.BlendMode = state.Blend switch
            {
                FalloutNifBlendMode.Add => BaseMaterial3D.BlendModeEnum.Add,
                FalloutNifBlendMode.Premultiplied => BaseMaterial3D.BlendModeEnum.PremultAlpha,
                FalloutNifBlendMode.Multiply => BaseMaterial3D.BlendModeEnum.Mul,
                _ => BaseMaterial3D.BlendModeEnum.Mix,
            };
            if ((alpha.Flags & AlphaTestEnabled) != 0)
            {
                material.Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor;
                material.AlphaScissorThreshold = alpha.Threshold / ByteMaximum;
            }
            else if ((alpha.Flags & AlphaBlendEnabled) != 0)
                material.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        }

        private Texture2D? LoadTexture(string logicalPath, bool normal)
        {
            if (string.IsNullOrEmpty(logicalPath))
                return null;
            var (payload, source) = ReadTexture(logicalPath);
            var image = new Image();
            var error = image.LoadDdsFromBuffer(payload);
            if (error != Error.Ok || image.IsEmpty())
                throw new InvalidDataException(
                    $"Godot could not decode native NIF DDS texture {source}: {error}");
            if (normal && image.GetFormat() == Image.Format.L8)
                throw new NotSupportedException(
                    $"Native NIF normal texture has an unsupported single-channel format: {source}");
            var texture = ImageTexture.CreateFromImage(image);
            texture.SetMeta("opennv_source_texture", source);
            texture.SetMeta("opennv_logical_texture", logicalPath);
            return texture;
        }

        private Cubemap LoadCubemap(string logicalPath)
        {
            var (payload, source) = ReadTexture(logicalPath);
            if (payload.Length < DdsHeaderBytes ||
                !payload.AsSpan(0, 4).SequenceEqual("DDS "u8))
                throw new InvalidDataException($"Native NIF cubemap is not DDS: {source}");
            var caps2 = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(DdsCaps2Offset));
            if ((caps2 & DdsCubemapFlag) == 0 ||
                (caps2 & DdsAllCubemapFaces) != DdsAllCubemapFaces ||
                (payload.Length - DdsHeaderBytes) % DdsGodotFaceOrder.Length != 0)
                throw new InvalidDataException(
                    $"Native NIF environment texture is not a complete six-face DDS cubemap: {source}");
            var faceBytes = (payload.Length - DdsHeaderBytes) / DdsGodotFaceOrder.Length;
            var images = new Godot.Collections.Array<Image>();
            foreach (var sourceFace in DdsGodotFaceOrder)
            {
                var facePayload = new byte[checked(DdsHeaderBytes + faceBytes)];
                payload.AsSpan(0, DdsHeaderBytes).CopyTo(facePayload);
                BinaryPrimitives.WriteUInt32LittleEndian(facePayload.AsSpan(DdsCaps2Offset), 0);
                payload.AsSpan(DdsHeaderBytes + sourceFace * faceBytes, faceBytes)
                    .CopyTo(facePayload.AsSpan(DdsHeaderBytes));
                var image = new Image();
                var error = image.LoadDdsFromBuffer(facePayload);
                if (error != Error.Ok || image.IsEmpty())
                    throw new InvalidDataException(
                        $"Godot could not decode native NIF cubemap face {sourceFace} from {source}: {error}");
                images.Add(image);
            }
            var result = new Cubemap();
            var createError = result.CreateFromImages(images);
            if (createError != Error.Ok)
                throw new InvalidDataException(
                    $"Godot could not create native NIF cubemap {source}: {createError}");
            return result;
        }

        private static ShaderMaterial BuildEnvironmentPass(
            Cubemap environment,
            Texture2D normal,
            Texture2D? mask,
            float scale,
            bool lightFade)
        {
            var result = new ShaderMaterial
            {
                Shader = new Shader
                {
                    Code = lightFade ? EnvironmentLightFadeShader : EnvironmentShader,
                },
            };
            result.SetShaderParameter("normal_map", normal);
            result.SetShaderParameter("environment_cube", environment);
            result.SetShaderParameter("environment_mask", mask ?? normal);
            result.SetShaderParameter("use_environment_mask", mask is not null);
            result.SetShaderParameter("environment_scale", scale);
            result.SetMeta("opennv_environment_light_fade", lightFade);
            return result;
        }

        private (byte[] Payload, string Source) ReadTexture(string logicalPath)
        {
            var owned = RuntimeLiveContentSource.Current ??
                throw new InvalidOperationException(
                    $"Native NIF texture resolution is not configured: {logicalPath}");
            if (!owned.TryRead(logicalPath, _preferredTextureArchive, out var payload, out var source))
                throw new FileNotFoundException($"Native NIF texture is missing: {logicalPath}");
            return (payload, source);
        }

        private static float GlossToRoughness(float glossiness)
        {
            if (!float.IsFinite(glossiness) || glossiness < 0.0f)
                throw new InvalidDataException("NIF material glossiness is invalid.");
            return 1.0f / MathF.Sqrt(glossiness + 1.0f);
        }

        private Node3D CreateNode(
            string sourceName,
            int blockIndex,
            FalloutNifTransform transform,
            ushort flags)
        {
            var node = new Node3D
            {
                Name = SourceName(sourceName, blockIndex),
                Transform = ConvertTransform(transform),
                Visible = (flags & HiddenFlag) == 0,
            };
            node.SetMeta("opennv_nif_block", blockIndex);
            node.SetMeta("opennv_nif_source_name", sourceName);
            return node;
        }

        private Transform3D ConvertTransform(FalloutNifTransform source) => new(
            GamebryoCoordinate.ConvertBasis(source.RotationRowMajor, source.Scale, "NIF local transform"),
            GamebryoCoordinate.ConvertVector(new Vector3(
                source.Translation.X, source.Translation.Y, source.Translation.Z)) * _unitsToMetres);

        private static float[] BuildTangents(FalloutNifMeshData source)
        {
            var values = new float[checked(source.Tangents.Length * TangentComponents)];
            for (var index = 0; index < source.Tangents.Length; ++index)
            {
                var normal = ConvertVector(source.Normals[index]).Normalized();
                var tangent = ConvertVector(source.Tangents[index]).Normalized();
                var bitangent = ConvertVector(source.Bitangents[index]).Normalized();
                values[index * TangentComponents] = tangent.X;
                values[index * TangentComponents + 1] = tangent.Y;
                values[index * TangentComponents + 2] = tangent.Z;
                values[index * TangentComponents + 3] = normal.Cross(tangent).Dot(bitangent) < 0.0f
                    ? -1.0f : 1.0f;
            }
            return values;
        }

        private static Vector3 ConvertVector(FalloutNifVector3 value) =>
            GamebryoCoordinate.ConvertVector(new Vector3(value.X, value.Y, value.Z));

        private static string SourceName(string value, int blockIndex) =>
            string.IsNullOrWhiteSpace(value) ? $"NifBlock{blockIndex}" : value;

        private void RequirePlainVisualState(
            FalloutNifBlock block,
            int controller,
            IReadOnlyList<int> extraData,
            IReadOnlyList<int> properties,
            int collision)
        {
            if (controller != -1 && ExternalControllerBlocks?.Contains(controller) != true)
            {
                var dormant = _source.Blocks[controller].TypeName == "NiTransformController" &&
                    _source.ReadObject(controller) is FalloutNifTransformController direct &&
                    direct.Time.Flags is (DormantDirectTransformFlags or DormantMultiTargetFlags or DormantManagerFlags) &&
                    direct.Time.Frequency == 1.0f && direct.Time.Phase == 0.0f &&
                    direct.Time.StartTime == float.MaxValue && direct.Time.StopTime == float.MinValue &&
                    direct.Time.Target == block.Index && direct.Interpolator == -1;
                if (!dormant)
                    throw new NotSupportedException(
                        $"NIF visual block {block.Index} has unsupported controller {controller} " +
                        $"type {_source.Blocks[controller].TypeName}.");
            }
            if (extraData.Any(reference => reference != -1))
                throw new NotSupportedException(
                    $"NIF visual block {block.Index} has unsupported extra data.");
            if (properties.Any(reference => reference != -1))
                throw new NotSupportedException(
                    $"NIF visual block {block.Index} has unsupported material properties.");
            if (collision != -1)
                throw new NotSupportedException(
                    $"NIF visual block {block.Index} has unsupported collision.");
        }

        private static void RequireAttributeCount(
            int actual,
            int expected,
            string label,
            int blockIndex)
        {
            if (actual != 0 && actual != expected)
                throw new InvalidDataException(
                    $"NIF geometry data {blockIndex} {label} count differs from its vertices.");
        }
    }
}

internal sealed record RuntimeNativeNifScene(
    Node3D Root,
    int Nodes,
    int Surfaces,
    int Vertices,
    int Triangles,
    int CollisionBodies,
    int CollisionShapes,
    int CollisionTriangles);

internal sealed class RuntimeNativeNifPrototype
{
    private readonly FalloutNifFile _source;
    private readonly float _unitsToMetres;
    private readonly bool _hasControllers;
    internal RuntimeNativeNifScene Scene { get; }

    internal RuntimeNativeNifPrototype(ReadOnlyMemory<byte> payload, float unitsToMetres)
    {
        _source = FalloutNifFile.Read(payload);
        _unitsToMetres = unitsToMetres;
        Scene = RuntimeNativeNifMeshBuilder.Build(_source, unitsToMetres);
        _hasControllers = Scene.Root.FindChildren("*", "", true, false).OfType<RuntimeNifControllerPlayer>().Any();
    }

    internal Node3D Instantiate()
    {
        // Godot Duplicate copies engine properties, not configured C# delegates
        // and their target objects. Reuse the decoded source, but create fresh
        // controllers/material owners for each animated reference.
        if (_hasControllers) return RuntimeNativeNifMeshBuilder.Build(_source, _unitsToMetres).Root;
        return Scene.Root.Duplicate((int)Node.DuplicateFlags.UseInstantiation) as Node3D ??
            throw new InvalidOperationException("Could not instantiate a static native NIF.");
    }

    internal Node3D InstantiatePlaced(Transform3D placement)
    {
        // A placed TES reference owns the loaded scene root's complete local
        // transform. Native placement replaces the exported root transform;
        // composing both rotates/translates whole room modules a second time.
        // Descendant transforms and standalone/menu model assembly are intact.
        if (_source.Roots.Count != 1)
            throw new NotSupportedException("Placed multi-root NIF ownership has not been established.");
        var instance = Instantiate();
        var modelRoot = instance.GetChild<Node3D>(0);
        modelRoot.SetMeta("opennv_nif_authored_root_transform", modelRoot.Transform);
        modelRoot.SetMeta("opennv_nif_root_transform_owner", "placed-reference");
        modelRoot.Transform = Transform3D.Identity;
        instance.Transform = placement;
        return instance;
    }
}

internal static class NativeNifMeshBuilder
{
    internal static Material BuildMaterial(
        FalloutNifFile source, FalloutNifGeometry geometry, string? preferredTextureArchive = null, Color? hairColor = null) =>
        RuntimeNativeNifMeshBuilder.BuildMaterial(source, geometry, preferredTextureArchive, hairColor);

    internal static RuntimeNativeNifSkeleton BuildActorSkeleton(
        ReadOnlyMemory<byte> payload, float unitsToMetres) =>
        RuntimeNativeNifMeshBuilder.BuildActorSkeleton(payload, unitsToMetres);

    internal static RuntimeNativeNifScene AddActorPart(
        ReadOnlyMemory<byte> payload,
        RuntimeNativeNifSkeleton skeleton,
        string? preferredTextureArchive = null,
        Func<FalloutNifFile, FalloutNifGeometry, Material?>? materialOverride = null,
        Func<FalloutNifFile, FalloutNifGeometry, FalloutNifMeshData, FalloutNifMeshData>? geometryOwner = null,
        IReadOnlySet<string>? externalTransformTargets = null,
        IReadOnlyDictionary<string, FalloutNifTransform>? rigidFaceBinds = null,
        string? selectedGeometryName = null,
        Func<FalloutNifFile, FalloutNifGeometry, FalloutNifMeshData, IReadOnlyDictionary<string, System.Numerics.Vector3[]>>? morphOwner = null) =>
        RuntimeNativeNifMeshBuilder.AddActorPart(payload, skeleton, preferredTextureArchive, materialOverride, geometryOwner, externalTransformTargets, rigidFaceBinds, selectedGeometryName, morphOwner);

    internal static RuntimeNativeNifScene Build(
        ReadOnlyMemory<byte> payload,
        float unitsToMetres,
        string? preferredTextureArchive = null) =>
        RuntimeNativeNifMeshBuilder.Build(payload, unitsToMetres, preferredTextureArchive);
}
