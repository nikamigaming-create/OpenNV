using System.Text;
using System.Text.Json;
using Godot;

using OpenNV.Runtime.SceneGraph;

using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Presentation.Rendering;

internal static class RuntimeMaterialLoader
{
    private const int LandscapeSamplerBudget = 16;
    private const int LandscapeBaseSamplerCount = 2;
    private const int LandscapeSamplerCountPerLayer = 2;
    private const int LandscapeWeightsPerMap = 4;
    private const int LandscapeWeightVertexSide = 17;
    private const int LandscapeWeightLastVertex = LandscapeWeightVertexSide - 1;
    private const int NifBlendModeOne = 0;
    private const int NifBlendModeSourceAlpha = 6;
    private const string LandscapeWeightMapRole = "vertex-weight-rgba32f";
    private const string LandscapeMaterialContractSchema =
        "opennv-landscape-layer-material/v3";
    private const string RetailLandscapeLightingModel =
        "retail-sls-land-weighted-ambient-directional-lambert";
    private const string RetailLandscapeMaterialResourceName =
        "OpenNV_RetailLandscapeWeightedAmbientDirectionalLambert";
    private const string RetailLightingContractSchema = "opennv-retail-material-lighting/v1";
    private const string RetailAmbientDirectionalLambertModel = "ambient-plus-directional-lambert";
    internal const string RetailAmbientDirectionalLambertResourceName =
        "OpenNV_RetailAmbientDirectionalLambert";
    private const string RetailGrassMaterialResourceName =
        "OpenNV_RetailGrass23x002";
    internal const string RetailActorMaterialResourceName =
        "OpenNV_RetailActorAmbientDirectionalLambert";
    internal const string RetailActorUnshadedMaterialResourceName =
        "OpenNV_RetailActorUnshaded";
    private const string SourceSurfaceIdentityMetadataPrefix =
        "opennv_source_surface_identity_";

    internal static string? SourceSurfaceIdentity(MeshInstance3D mesh, int surface)
    {
        var key = SourceSurfaceIdentityMetadataPrefix + surface;
        if (!mesh.HasMeta(key))
            return null;
        var identity = mesh.GetMeta(key).AsString();
        return string.IsNullOrWhiteSpace(identity) ? null : identity;
    }

    internal static LoadedTextures LoadTextures(JsonElement scene)
    {
        var configuration = RuntimeConfiguration.Load();
        return LoadTextures(scene, configuration.Renderer);
    }

    private static int LandscapeWeightMapCount(int layerCount) =>
        layerCount == 0
            ? 0
            : (layerCount + 1 + LandscapeWeightsPerMap - 1) / LandscapeWeightsPerMap;

    private const string EnvironmentShaderPrefix = """
        shader_type spatial;
        render_mode unshaded, blend_add, depth_draw_never, CULL_MODE;

        uniform sampler2D normal_map : hint_normal;
        uniform samplerCube environment_cube;
        uniform sampler2D environment_mask;
        uniform bool use_custom_mask;
        uniform float environment_scale;
        uniform float normal_decode_scale;
        uniform float normal_decode_bias;
        uniform float reflection_homogeneous_w;
        uniform float opaque_alpha;

        void fragment() {
            vec4 normal_sample = texture(normal_map, UV);
            vec3 tangent_normal = normalize(
                normal_sample.xyz * normal_decode_scale + normal_decode_bias);
            vec3 view_normal = normalize(
                TANGENT * tangent_normal.x +
                BINORMAL * tangent_normal.y +
                NORMAL * tangent_normal.z);
            vec3 reflected_view = reflect(-normalize(VIEW), view_normal);
            vec3 reflected_world = normalize(
                (INV_VIEW_MATRIX * vec4(reflected_view, reflection_homogeneous_w)).xyz);
            float mask = use_custom_mask
                ? texture(environment_mask, UV).r
                : normal_sample.a;
            ALBEDO = texture(environment_cube, reflected_world).rgb * mask * environment_scale;
            ALPHA = opaque_alpha;
        }
        """;

    internal static LoadedTextures LoadTextures(
        JsonElement scene,
        RendererConfiguration configuration,
        TextureMemoryStore? memory = null) =>
        LoadTextures(
            scene.GetProperty("textures").EnumerateArray(),
            configuration,
            "id",
            null,
            memory);

    internal static LoadedTextures LoadTextures(
        IEnumerable<JsonElement> textureRows,
        RendererConfiguration configuration,
        string idProperty,
        string? baseDirectory,
        TextureMemoryStore? memory = null)
    {
        var textures = new Dictionary<string, Texture2D>(StringComparer.Ordinal);
        var cubemaps = new Dictionary<string, Cubemap>(StringComparer.Ordinal);
        var authoredDdsTextures = 0;
        var authoredDdsMipChainTextures = 0;
        var decodedAuthoredBc1AlphaMipChainTextures = 0;
        var runtimeGeneratedMipTextures = 0;
        foreach (var texture in textureRows)
        {
            var id = texture.GetProperty(idProperty).GetString()!;
            var contract = texture.GetRawText();
            if (memory?.TryGet(id, contract, out var stored) == true)
            {
                textures.Add(id, stored.Texture);
                if (stored.Cubemap is not null)
                    cubemaps.Add(id, stored.Cubemap);
                authoredDdsTextures += stored.AuthoredDdsTextures;
                authoredDdsMipChainTextures += stored.AuthoredDdsMipChainTextures;
                decodedAuthoredBc1AlphaMipChainTextures +=
                    stored.DecodedAuthoredBc1AlphaMipChainTextures;
                runtimeGeneratedMipTextures += stored.RuntimeGeneratedMipTextures;
                continue;
            }
            var authoredDdsBefore = authoredDdsTextures;
            var authoredDdsMipBefore = authoredDdsMipChainTextures;
            var decodedMipBefore = decodedAuthoredBc1AlphaMipChainTextures;
            var generatedMipBefore = runtimeGeneratedMipTextures;
            var isLandscapeWeightMap = texture.TryGetProperty(
                    "landscapeRole",
                    out var landscapeRole) &&
                landscapeRole.GetString() == LandscapeWeightMapRole;
            var hasCubeFaces = texture.TryGetProperty("cubeFaces", out var cubeFaceRows) &&
                cubeFaceRows.GetArrayLength() > 0;
            var normalGreenInverted =
                texture.GetProperty("normalGreenInverted").GetBoolean();
            var path = ResolveContentPath(
                texture.GetProperty("png").GetString()!,
                baseDirectory);
            var loadedTexturePath = path;
            Image? image = null;
            var loadedDirectly = false;
            if (!isLandscapeWeightMap && !hasCubeFaces &&
                RuntimeLiveContentSource.Current is { } ownedSource &&
                texture.TryGetProperty("archivePath", out var archivePathProperty) &&
                archivePathProperty.ValueKind == JsonValueKind.String)
            {
                var logicalPath = archivePathProperty.GetString()!;
                var preferredArchive = texture.TryGetProperty("sourceArchive", out var sourceArchiveProperty)
                    ? sourceArchiveProperty.GetString()
                    : null;
                if (!ownedSource.TryRead(logicalPath, preferredArchive, out var sourceBytes, out var source))
                    throw new FileNotFoundException($"The effective owned resource is missing: {logicalPath}");
                image = new Image();
                var directLoadResult = image.LoadDdsFromBuffer(sourceBytes);
                if (directLoadResult != Error.Ok || image.IsEmpty())
                    throw new InvalidOperationException(
                        $"Godot could not decode effective DDS resource: {source} ({directLoadResult})");
                if (normalGreenInverted)
                    image = InvertNormalGreen(image, source);
                loadedTexturePath = source;
                loadedDirectly = true;
                authoredDdsTextures++;
                if (image.HasMipmaps())
                    authoredDdsMipChainTextures++;
            }
            if (!loadedDirectly)
                VerifiedGltfLoader.VerifyHash(path, texture.GetProperty("pngSha256").GetString()!);
            JsonElement rgba8MipProperty = default;
            var useDecodedAuthoredMipChain = !isLandscapeWeightMap &&
                !hasCubeFaces &&
                texture.TryGetProperty("rgba8MipChain", out rgba8MipProperty) &&
                rgba8MipProperty.ValueKind == JsonValueKind.String;
            JsonElement ddsProperty = default;
            var useAuthoredDds = !useDecodedAuthoredMipChain &&
                !isLandscapeWeightMap &&
                !normalGreenInverted &&
                !hasCubeFaces &&
                texture.TryGetProperty("dds", out ddsProperty) &&
                ddsProperty.ValueKind == JsonValueKind.String;
            if (loadedDirectly)
            {
                // The effective source can be a legitimate higher-priority mod replacement,
                // so prepared-source dimensions and hashes do not apply to this payload.
            }
            else if (isLandscapeWeightMap)
            {
                var width = texture.GetProperty("width").GetInt32();
                var height = texture.GetProperty("height").GetInt32();
                var data = File.ReadAllBytes(path);
                var expectedBytes = checked(width * height * 4 * sizeof(float));
                if (data.Length != expectedBytes)
                    throw new InvalidOperationException(
                        $"LAND float32 weight map has invalid byte length: {path}");
                image = Image.CreateFromData(
                    width,
                    height,
                    false,
                    Image.Format.Rgbaf,
                    data);
            }
            else if (useDecodedAuthoredMipChain)
            {
                var mipPath = ResolveContentPath(
                    rgba8MipProperty.GetString()!,
                    baseDirectory);
                loadedTexturePath = mipPath;
                VerifiedGltfLoader.VerifyHash(
                    mipPath,
                    texture.GetProperty("rgba8MipChainSha256").GetString()!);
                var data = File.ReadAllBytes(mipPath);
                if (data.Length != texture.GetProperty("rgba8MipChainBytes").GetInt64())
                    throw new InvalidOperationException(
                        $"Decoded authored mip byte length does not match manifest: {mipPath}");
                if (texture.GetProperty("rgba8MipChainFormat").GetString() !=
                        "RGBA8-authored-levels-base-to-1x1" ||
                    texture.GetProperty("rgba8MipChainReason").GetString() !=
                        "BC1-one-bit-alpha-preservation")
                    throw new InvalidOperationException(
                        $"Decoded authored mip contract differs: {mipPath}");
                var width = texture.GetProperty("width").GetInt32();
                var height = texture.GetProperty("height").GetInt32();
                var authoredMipCount = texture.GetProperty("authoredMipCount").GetInt32();
                var expectedBytes = Rgba8MipChainBytes(width, height, authoredMipCount);
                if (data.Length != expectedBytes)
                    throw new InvalidOperationException(
                        $"Decoded authored mip payload differs: {mipPath} " +
                        $"(expected={expectedBytes}, actual={data.Length})");
                image = Image.CreateFromData(
                    width,
                    height,
                    authoredMipCount > 1,
                    Image.Format.Rgba8,
                    data);
                if (image is null || image.IsEmpty() ||
                    image.GetMipmapCount() + 1 != authoredMipCount)
                    throw new InvalidOperationException(
                        $"Godot did not retain the decoded authored BC1-alpha mip chain: " +
                        $"{mipPath}");
                authoredDdsTextures++;
                if (authoredMipCount > 1)
                    authoredDdsMipChainTextures++;
                decodedAuthoredBc1AlphaMipChainTextures++;
            }
            else if (useAuthoredDds)
            {
                var ddsPath = ResolveContentPath(
                    ddsProperty.GetString()!,
                    baseDirectory);
                loadedTexturePath = ddsPath;
                VerifiedGltfLoader.VerifyHash(
                    ddsPath,
                    texture.GetProperty("ddsSha256").GetString()!);
                if (new FileInfo(ddsPath).Length != texture.GetProperty("ddsBytes").GetInt64())
                    throw new InvalidOperationException(
                        $"Authored DDS byte length does not match manifest: {ddsPath}");
                image = new Image();
                var ddsLoadResult = image.LoadDdsFromBuffer(File.ReadAllBytes(ddsPath));
                if (ddsLoadResult != Error.Ok || image.IsEmpty())
                    throw new InvalidOperationException(
                        $"Godot could not load authored DDS texture: {ddsPath} " +
                        $"({ddsLoadResult})");
                var authoredMipCount = texture.GetProperty("authoredMipCount").GetInt32();
                if (authoredMipCount < 1 ||
                    (authoredMipCount > 1 &&
                        (!image.HasMipmaps() ||
                            image.GetMipmapCount() + 1 != authoredMipCount)))
                    throw new InvalidOperationException(
                        $"Godot did not retain the authored DDS mip chain: {ddsPath} " +
                        $"(expected={authoredMipCount}, actual={image.GetMipmapCount() + 1})");
                authoredDdsTextures++;
                if (authoredMipCount > 1)
                    authoredDdsMipChainTextures++;
            }
            else
                image = Image.LoadFromFile(path);
            if (image is null || image.IsEmpty())
                throw new InvalidOperationException($"Godot could not load prepared texture: {path}");
            if (!loadedDirectly &&
                (image.GetWidth() != texture.GetProperty("width").GetInt32() ||
                    image.GetHeight() != texture.GetProperty("height").GetInt32()))
                throw new InvalidOperationException($"Prepared texture dimensions do not match manifest: {path}");
            if (!isLandscapeWeightMap)
                runtimeGeneratedMipTextures += GenerateRuntimeMipmaps(
                    image,
                    normalGreenInverted,
                    loadedTexturePath)
                    ? 1
                    : 0;
            var loadedTexture = ImageTexture.CreateFromImage(image);
            textures.Add(id, loadedTexture);
            Cubemap? loadedCubemap = null;
            if (texture.TryGetProperty("cubeFaces", out var cubeFaces))
            {
                var rows = cubeFaces.EnumerateArray().ToArray();
                if (rows.Length > 0)
                {
                    if (rows.Length != configuration.CubemapFaceCount)
                        throw new InvalidOperationException($"Prepared cubemap must contain six faces: {id}");
                    var images = new Godot.Collections.Array<Image>();
                    foreach (var face in rows)
                    {
                        var facePath = ResolveContentPath(
                            face.GetProperty("png").GetString()!,
                            baseDirectory);
                        VerifiedGltfLoader.VerifyHash(facePath, face.GetProperty("pngSha256").GetString()!);
                        var faceImage = Image.LoadFromFile(facePath);
                        if (faceImage is null || faceImage.IsEmpty() ||
                            faceImage.GetWidth() != image.GetWidth() ||
                            faceImage.GetHeight() != image.GetHeight())
                            throw new InvalidOperationException($"Prepared cubemap face is invalid: {facePath}");
                        faceImage.Convert(Image.Format.Rgba8);
                        GenerateRuntimeMipmaps(faceImage, false, facePath);
                        images.Add(faceImage);
                    }
                    loadedCubemap = new Cubemap();
                    var error = loadedCubemap.CreateFromImages(images);
                    if (error != Error.Ok)
                        throw new InvalidOperationException($"Godot rejected prepared cubemap {id}: {error}");
                    cubemaps.Add(id, loadedCubemap);
                }
            }
            memory?.Add(
                id,
                contract,
                new StoredTexture(
                    loadedTexture,
                    loadedCubemap,
                    authoredDdsTextures - authoredDdsBefore,
                    authoredDdsMipChainTextures - authoredDdsMipBefore,
                    decodedAuthoredBc1AlphaMipChainTextures - decodedMipBefore,
                    runtimeGeneratedMipTextures - generatedMipBefore));
        }
        var neutralNormalImage = Image.CreateEmpty(
            configuration.NeutralNormalTextureSizePixels[0],
            configuration.NeutralNormalTextureSizePixels[1],
            false,
            Image.Format.Rgba8);
        neutralNormalImage.Fill(configuration.NeutralNormalColorRgba.Color());
        return new LoadedTextures(
            textures,
            cubemaps,
            ImageTexture.CreateFromImage(neutralNormalImage),
            authoredDdsTextures,
            authoredDdsMipChainTextures,
            decodedAuthoredBc1AlphaMipChainTextures,
            runtimeGeneratedMipTextures);
    }

    private static int Rgba8MipChainBytes(int width, int height, int mipCount)
    {
        if (width < 1 || height < 1 || mipCount < 1)
            throw new InvalidOperationException("Decoded authored mip dimensions are invalid.");
        var bytes = 0;
        for (var level = 0; level < mipCount; ++level)
        {
            bytes = checked(bytes + width * height * 4);
            width = Math.Max(1, width / 2);
            height = Math.Max(1, height / 2);
        }
        return bytes;
    }

    private static bool GenerateRuntimeMipmaps(Image image, bool renormalize, string path)
    {
        if (image.HasMipmaps() || image.GetWidth() <= 1 || image.GetHeight() <= 1)
            return false;
        var result = image.GenerateMipmaps(renormalize);
        if (result != Error.Ok || !image.HasMipmaps())
            throw new InvalidOperationException(
                $"Godot could not generate the required runtime mip chain: {path} ({result})");
        return true;
    }

    private static Image InvertNormalGreen(Image image, string source)
    {
        var width = image.GetWidth();
        var height = image.GetHeight();
        var hasMipmaps = image.HasMipmaps();
        image.Convert(Image.Format.Rgba8);
        var data = image.GetData();
        if (data.Length % 4 != 0)
            throw new InvalidOperationException($"Decoded normal DDS is not RGBA8 aligned: {source}");
        for (var index = 1; index < data.Length; index += 4)
            data[index] = (byte)(byte.MaxValue - data[index]);
        var inverted = Image.CreateFromData(width, height, hasMipmaps, Image.Format.Rgba8, data);
        if (inverted is null || inverted.IsEmpty())
            throw new InvalidOperationException($"Godot could not invert the normal-map green channel: {source}");
        return inverted;
    }

    internal static int Apply(
        Node3D scene,
        JsonElement asset,
        LoadedTextures textures,
        RendererConfiguration configuration,
        RetailGrassCompilerConfiguration retailGrass)
    {
        var surfaces = NodeTraversal.Descendants<MeshInstance3D>(scene)
            .SelectMany(mesh => Enumerable.Range(0, mesh.Mesh?.GetSurfaceCount() ?? 0)
                .Select(index =>
                {
                    var name = mesh.Mesh!.SurfaceGetMaterial(index)?.ResourceName;
                    if (string.IsNullOrWhiteSpace(name))
                        throw new InvalidOperationException(
                            $"Imported glTF surface has no material identity: {mesh.Name}[{index}]");
                    return (Name: NormalizeMaterialName(name), Mesh: mesh, Surface: index);
                }))
            .ToArray();
        var bindings = asset.GetProperty("materials").EnumerateArray().ToArray();
        var assetId = AssetId(asset);
        if (surfaces.Length != bindings.Length)
            throw new InvalidOperationException(
                $"Material/surface count mismatch for asset {assetId}: " +
                $"surfaces={surfaces.Length} bindings={bindings.Length}");
        var surfacesByName = surfaces
            .GroupBy(surface => surface.Name, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => new Queue<(string Name, MeshInstance3D Mesh, int Surface)>(group),
                StringComparer.Ordinal);

        foreach (var binding in bindings)
        {
            var expectedName = binding.GetProperty("name").GetString()!;
            if (!surfacesByName.TryGetValue(expectedName, out var matches) ||
                matches.Count < 1)
                throw new InvalidOperationException(
                    $"Imported glTF has no material surface named {expectedName} for asset " +
                    assetId);
            var surface = matches.Dequeue();
            var identityKey = SourceSurfaceIdentityMetadataPrefix + surface.Surface;
            if (surface.Mesh.HasMeta(identityKey) &&
                !surface.Mesh.GetMeta(identityKey).AsString().Equals(
                    expectedName,
                    StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"Imported glTF source surface identity changed for asset {assetId}: " +
                    $"{surface.Mesh.Name}[{surface.Surface}]");
            surface.Mesh.SetMeta(identityKey, expectedName);
            Material material;
            if (binding.TryGetProperty("retailGrassContract", out var retailGrassContract) &&
                retailGrassContract.ValueKind != JsonValueKind.Null)
            {
                material = RetailGrassPass(
                    binding,
                    retailGrassContract,
                    textures,
                    retailGrass);
            }
            else if (binding.TryGetProperty("landscapeContract", out var landscapeContract) &&
                landscapeContract.ValueKind != JsonValueKind.Null)
            {
                material = LandscapePass(
                    binding,
                    landscapeContract,
                    textures);
            }
            else if (binding.TryGetProperty(
                    "retailLightingContract",
                    out var retailLightingContract) &&
                retailLightingContract.ValueKind != JsonValueKind.Null)
            {
                material = RetailAmbientDirectionalLambertPass(
                    binding,
                    retailLightingContract,
                    textures,
                    configuration,
                    surface.Mesh.Mesh is ArrayMesh arrayMesh &&
                        (arrayMesh.SurfaceGetFormat(surface.Surface) &
                            Mesh.ArrayFormat.FormatColor) != 0);
            }
            else
            {
                var decal = binding.TryGetProperty("decal", out var decalProperty) &&
                    decalProperty.GetBoolean();
                var lodObjectAtlas = binding.TryGetProperty("lodObjectAtlas", out var lodProperty) &&
                    lodProperty.GetBoolean();
                var standard = new StandardMaterial3D
                {
                    Metallic = configuration.DefaultMetallic,
                    Roughness = binding.GetProperty("roughness").GetSingle(),
                    AlbedoColor = ReadColor(binding.GetProperty("baseColorFactor"), 4),
                    VertexColorUseAsAlbedo =
                        binding.GetProperty("vertexColorMode").GetString() != "none",
                };
                var textureClampMode = binding.TryGetProperty("textureClampMode", out var clampProperty)
                    ? clampProperty.GetInt32()
                    : 0;
                // NiTexturingProperty uses 0 for wrap-both and 3 for
                // clamp-both. Godot's material API has one repeat switch;
                // preserve those exact modes and conservatively clamp mixed
                // axis modes instead of allowing atlas UVs to wrap.
                standard.TextureRepeat = textureClampMode == 3;
                standard.AlbedoTexture = Texture(binding, "diffuseTextureId", textures.TwoDimensional);
                var diffuseSampleSrgb = !binding.TryGetProperty(
                        "diffuseSampleSrgb",
                        out var diffuseSampleSrgbProperty) ||
                    diffuseSampleSrgbProperty.GetBoolean();
                standard.AlbedoTextureForceSrgb =
                    standard.AlbedoTexture is not null && diffuseSampleSrgb;
                var normal = Texture(binding, "normalTextureId", textures.TwoDimensional);
                if (normal is not null)
                {
                    standard.NormalEnabled = true;
                    standard.NormalTexture = normal;
                }
                var emissive = Texture(binding, "emissiveTextureId", textures.TwoDimensional);
                var emissiveColor = ReadColor(binding.GetProperty("emissiveColor"));
                if (binding.GetProperty("emissiveReplace").GetBoolean())
                {
                    standard.AlbedoColor = emissiveColor;
                    standard.AlbedoTexture = null;
                    standard.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
                }
                else if (emissive is not null || emissiveColor != Colors.Black)
                {
                    standard.EmissionEnabled = true;
                    standard.Emission = emissiveColor == Colors.Black ? Colors.White : emissiveColor;
                    standard.EmissionTexture = emissive;
                    standard.EmissionOperator = BaseMaterial3D.EmissionOperatorEnum.Multiply;
                    standard.EmissionEnergyMultiplier = configuration.EmissionEnergyMultiplier;
                }
                var alpha = binding.GetProperty("alphaContract");
                var alphaMode = alpha.GetProperty("mode").GetString();
                if (alphaMode == "BLEND")
                {
                    var authoredAdditive =
                        alpha.TryGetProperty("sourceBlendMode", out var sourceBlendMode) &&
                        sourceBlendMode.ValueKind == JsonValueKind.Number &&
                        sourceBlendMode.GetInt32() == NifBlendModeSourceAlpha &&
                        alpha.TryGetProperty(
                            "destinationBlendMode",
                            out var destinationBlendMode) &&
                        destinationBlendMode.ValueKind == JsonValueKind.Number &&
                        destinationBlendMode.GetInt32() == NifBlendModeOne;
                    if (authoredAdditive)
                    {
                        // NiAlphaProperty SRC_ALPHA + ONE is an authored
                        // additive light/glare pass. AlphaDepthPrePass turns
                        // its opaque black texels into an occluder instead.
                        standard.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
                        standard.BlendMode = BaseMaterial3D.BlendModeEnum.Add;
                        standard.DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled;
                    }
                    else
                        standard.Transparency = BaseMaterial3D.TransparencyEnum.AlphaDepthPrePass;
                }
                else if (alphaMode == "MASK")
                {
                    standard.Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor;
                    standard.AlphaScissorThreshold = alpha.GetProperty("cutoff").GetSingle();
                }
                else if (alphaMode != "OPAQUE")
                    throw new InvalidOperationException($"Unsupported material alpha mode: {alphaMode}");
                if (lodObjectAtlas)
                {
                    // LOD object NIFs rely on the authored atlas alpha for
                    // silhouette cutout; their source shader flag is the
                    // alpha contract when NiAlphaProperty is absent.
                    standard.Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor;
                    // The authored atlas contains exact alpha-zero holes and
                    // non-zero 8-bit edge levels. A zero threshold keeps the
                    // holes; 1/255 is the smallest cutoff that removes only
                    // alpha-zero texels.
                    standard.AlphaScissorThreshold = 1.0f / byte.MaxValue;
                }
                if (binding.GetProperty("doubleSided").GetBoolean())
                    standard.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
                if (binding.GetProperty("unshaded").GetBoolean())
                    standard.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
                if (decal)
                {
                    // Retail decal surfaces are submitted in a second pass over the
                    // depth-tested road and still write the tested pixels to depth.
                    // Preserve that authored pass boundary instead of letting Godot
                    // sort the pitted/gravel surface as an ordinary road mesh.
                    standard.RenderPriority = 1;
                    standard.DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Always;
                }
                var environmentId = binding.GetProperty("environmentTextureId");
                if (environmentId.ValueKind == JsonValueKind.String)
                {
                    if (!textures.Cubemaps.TryGetValue(environmentId.GetString()!, out var environment))
                        throw new InvalidOperationException(
                            $"Material environment texture is not a complete cubemap: {environmentId.GetString()}");
                    standard.NextPass = EnvironmentPass(
                        environment,
                        normal ?? textures.NeutralNormal,
                        Texture(binding, "environmentMaskTextureId", textures.TwoDimensional),
                        binding.GetProperty("environmentMapScale").GetSingle(),
                        binding.GetProperty("doubleSided").GetBoolean(),
                        configuration);
                }
                material = standard;
            }
            if (string.IsNullOrWhiteSpace(material.ResourceName))
                material.ResourceName = expectedName;
            surface.Mesh.SetSurfaceOverrideMaterial(surface.Surface, material);
        }
        return bindings.Length;
    }

    internal static int Apply(
        Node3D scene,
        JsonElement asset,
        LoadedTextures textures)
    {
        var configuration = RuntimeConfiguration.Load();
        return Apply(
            scene,
            asset,
            textures,
            configuration.Renderer,
            configuration.ContentCompiler.RetailGrass);
    }

    internal static int ApplyRetailAmbientDirectionalLighting(
        Node root,
        Color ambient,
        Color fog,
        float fogNearGameUnits,
        float fogFarGameUnits,
        float fogPower,
        float gameUnitsToMeters) =>
        ApplyRetailLighting(
            root,
            ambient,
            fog,
            fogNearGameUnits,
            fogFarGameUnits,
            fogPower,
            gameUnitsToMeters,
            RetailAmbientDirectionalLambertResourceName);

    internal static int ApplyRetailAmbientDirectionalMenuLighting(
        Node root,
        Color ambient)
    {
        var configured = new HashSet<ulong>();
        foreach (var mesh in NodeTraversal.Descendants<MeshInstance3D>(root))
        {
            for (var surface = 0; surface < (mesh.Mesh?.GetSurfaceCount() ?? 0); surface++)
            {
                if (mesh.GetSurfaceOverrideMaterial(surface) is not ShaderMaterial material ||
                    !material.ResourceName.Equals(
                        RetailAmbientDirectionalLambertResourceName,
                        StringComparison.Ordinal) ||
                    !configured.Add(material.GetInstanceId()))
                    continue;
                material.SetShaderParameter("retail_ambient_color", Rgb(ambient));
                material.SetShaderParameter("retail_fog_enabled", false);
            }
        }
        return configured.Count;
    }

    internal static int ApplyRetailLandscapeLighting(
        Node root,
        Color ambient,
        Color fog,
        float fogNearGameUnits,
        float fogFarGameUnits,
        float fogPower,
        float gameUnitsToMeters) =>
        ApplyRetailLighting(
            root,
            ambient,
            fog,
            fogNearGameUnits,
            fogFarGameUnits,
            fogPower,
            gameUnitsToMeters,
            RetailLandscapeMaterialResourceName);

    internal static int ApplyRetailActorLighting(
        Node root,
        Color ambient,
        Color fog,
        float fogNearGameUnits,
        float fogFarGameUnits,
        float fogPower,
        float gameUnitsToMeters) =>
        ApplyRetailLighting(
            root,
            ambient,
            fog,
            fogNearGameUnits,
            fogFarGameUnits,
            fogPower,
            gameUnitsToMeters,
            RetailActorMaterialResourceName);

    internal static int ApplyRetailGrassLighting(
        Node root,
        Color ambient,
        Color fog,
        float fogNearGameUnits,
        float fogFarGameUnits,
        float fogPower,
        float gameUnitsToMeters) =>
        ApplyRetailLighting(
            root,
            ambient,
            fog,
            fogNearGameUnits,
            fogFarGameUnits,
            fogPower,
            gameUnitsToMeters,
            RetailGrassMaterialResourceName);

    internal static int ApplyRetailGrassDistanceScale(
        Node root,
        float gameUnitsToMeters)
    {
        if (gameUnitsToMeters <= 0.0f)
            throw new InvalidOperationException("Retail grass unit scale is invalid.");
        var configured = new HashSet<ulong>();
        foreach (var mesh in NodeTraversal.Descendants<MeshInstance3D>(root))
        {
            for (var surface = 0; surface < (mesh.Mesh?.GetSurfaceCount() ?? 0); surface++)
            {
                if (mesh.GetSurfaceOverrideMaterial(surface) is not ShaderMaterial material ||
                    !material.ResourceName.Equals(
                        RetailGrassMaterialResourceName,
                        StringComparison.Ordinal) ||
                    !configured.Add(material.GetInstanceId()))
                    continue;
                material.SetShaderParameter(
                    "retail_game_units_per_meter",
                    1.0f / gameUnitsToMeters);
            }
        }
        return configured.Count;
    }

    private static int ApplyRetailLighting(
        Node root,
        Color ambient,
        Color fog,
        float fogNearGameUnits,
        float fogFarGameUnits,
        float fogPower,
        float gameUnitsToMeters,
        string resourceName)
    {
        if (!float.IsFinite(fogNearGameUnits) ||
            !float.IsFinite(fogFarGameUnits) ||
            !float.IsFinite(fogPower) ||
            !float.IsFinite(gameUnitsToMeters) ||
            fogFarGameUnits <= fogNearGameUnits || fogPower <= 0.0f ||
            gameUnitsToMeters <= 0.0f)
            throw new InvalidOperationException("Retail SLS fog inputs are invalid.");
        var configured = new HashSet<ulong>();
        foreach (var mesh in NodeTraversal.Descendants<MeshInstance3D>(root))
        {
            for (var surface = 0; surface < (mesh.Mesh?.GetSurfaceCount() ?? 0); surface++)
            {
                if (mesh.GetSurfaceOverrideMaterial(surface) is not ShaderMaterial material ||
                    !material.ResourceName.Equals(resourceName, StringComparison.Ordinal) ||
                    !configured.Add(material.GetInstanceId()))
                    continue;
                material.SetShaderParameter("retail_ambient_color", Rgb(ambient));
                material.SetShaderParameter("retail_fog_enabled", true);
                material.SetShaderParameter("retail_fog_color", Rgb(fog));
                material.SetShaderParameter("retail_fog_near_game_units", fogNearGameUnits);
                material.SetShaderParameter("retail_fog_far_game_units", fogFarGameUnits);
                material.SetShaderParameter("retail_fog_power", fogPower);
                material.SetShaderParameter(
                    "retail_game_units_per_meter",
                    1.0f / gameUnitsToMeters);
            }
        }
        return configured.Count;
    }

    private static string NormalizeMaterialName(string value) =>
        value.EndsWith(" material", StringComparison.Ordinal)
            ? value[..^" material".Length]
            : value;

    private static string AssetId(JsonElement asset) =>
        asset.TryGetProperty("id", out var id)
            ? id.GetString()!
            : asset.GetProperty("assetId").GetString()!;

    private static string ResolveContentPath(string path, string? baseDirectory)
    {
        if (baseDirectory is null || Path.IsPathRooted(path) ||
            path.StartsWith("res://", StringComparison.Ordinal) ||
            path.StartsWith("user://", StringComparison.Ordinal))
            return VerifiedGltfLoader.ResolvePath(path);
        return Path.GetFullPath(Path.Combine(baseDirectory, path));
    }

    private static Texture2D? Texture(
        JsonElement binding,
        string property,
        IReadOnlyDictionary<string, Texture2D> textures)
    {
        var value = binding.GetProperty(property);
        return value.ValueKind == JsonValueKind.String ? textures[value.GetString()!] : null;
    }

    private static ShaderMaterial EnvironmentPass(
        Cubemap environment,
        Texture2D normal,
        Texture2D? mask,
        float scale,
        bool doubleSided,
        RendererConfiguration configuration)
    {
        var shader = new Shader
        {
            Code = EnvironmentShaderPrefix.Replace(
                "CULL_MODE",
                doubleSided ? "cull_disabled" : "cull_back",
                StringComparison.Ordinal),
        };
        var material = new ShaderMaterial { Shader = shader };
        material.SetShaderParameter("normal_map", normal);
        material.SetShaderParameter("environment_cube", environment);
        material.SetShaderParameter("environment_mask", mask ?? normal);
        material.SetShaderParameter("use_custom_mask", mask is not null);
        material.SetShaderParameter("environment_scale", scale);
        material.SetShaderParameter("normal_decode_scale", configuration.EnvironmentNormalDecodeScale);
        material.SetShaderParameter("normal_decode_bias", configuration.EnvironmentNormalDecodeBias);
        material.SetShaderParameter(
            "reflection_homogeneous_w",
            configuration.EnvironmentReflectionHomogeneousW);
        material.SetShaderParameter("opaque_alpha", configuration.EnvironmentOpaqueAlpha);
        return material;
    }

    private static ShaderMaterial RetailAmbientDirectionalLambertPass(
        JsonElement binding,
        JsonElement contract,
        LoadedTextures textures,
        RendererConfiguration configuration,
        bool hasVertexColor)
    {
        var source = contract.GetProperty("source").GetString();
        if (contract.GetProperty("schema").GetString() != RetailLightingContractSchema ||
            contract.GetProperty("model").GetString() != RetailAmbientDirectionalLambertModel ||
            contract.GetProperty("diffuseDomain").GetString() != "encoded" ||
            contract.GetProperty("normalDecode").GetString() != "signed-rgb" ||
            contract.GetProperty("vertexColorOperation").GetString() != "multiply" ||
            (source != "matched-live-road-shader-package" &&
                source != "recovered-sls-ordinary-lighting-family"))
            throw new InvalidOperationException("Unsupported retail material lighting contract.");
        if (!binding.TryGetProperty("diffuseSampleSrgb", out var diffuseSampleSrgb) ||
            diffuseSampleSrgb.GetBoolean())
            throw new InvalidOperationException(
                "Retail encoded diffuse lighting requires an encoded-domain sampler.");
        if (binding.GetProperty("unshaded").GetBoolean())
            throw new InvalidOperationException(
                "Retail SLS lighting cannot be attached to an unshaded material.");
        var alpha = binding.GetProperty("alphaContract");
        var alphaMode = alpha.GetProperty("mode").GetString() ?? "";
        if (alphaMode is not ("OPAQUE" or "MASK" or "BLEND"))
            throw new InvalidOperationException(
                $"Unsupported retail SLS alpha mode: {alphaMode}");
        var lodObjectAtlas = binding.TryGetProperty("lodObjectAtlas", out var lodProperty) &&
            lodProperty.GetBoolean();
        var runtimeAlphaMode = lodObjectAtlas ? "MASK" : alphaMode;

        var normal = Texture(binding, "normalTextureId", textures.TwoDimensional);
        var emissive = Texture(binding, "emissiveTextureId", textures.TwoDimensional);
        var decal = binding.TryGetProperty("decal", out var decalProperty) &&
            decalProperty.GetBoolean();
        var material = new ShaderMaterial
        {
            ResourceName = RetailAmbientDirectionalLambertResourceName,
            Shader = new Shader
            {
                Code = BuildRetailAmbientDirectionalLambertShader(
                    binding,
                    runtimeAlphaMode,
                    decal),
            },
            RenderPriority = decal ? 1 : 0,
        };
        material.SetShaderParameter(
            "base_map",
            RequiredTexture(binding, "diffuseTextureId", textures.TwoDimensional));
        material.SetShaderParameter("normal_map", normal ?? textures.NeutralNormal);
        material.SetShaderParameter("use_normal_map", normal is not null);
        material.SetShaderParameter("emissive_map", emissive ?? textures.NeutralNormal);
        material.SetShaderParameter("use_emissive_map", emissive is not null);
        material.SetShaderParameter(
            "base_color_factor",
            ReadColor(binding.GetProperty("baseColorFactor"), 4));
        // The matched SLS2001 and SLS2017 programs consume vertex RGB even
        // when the BSShaderPPLightingProperty flag-derived legacy mode says
        // "none".  The actual vertex stream is the authority: road meshes
        // carry COLOR_0 paint/dust values down to 13/255, and omitting them
        // is the dominant washed-terrain error.
        material.SetShaderParameter("use_vertex_color", hasVertexColor);
        material.SetShaderParameter("retail_ambient_color", Vector3.Zero);
        material.SetShaderParameter(
            "emissive_color",
            Rgb(ReadColor(binding.GetProperty("emissiveColor"))));
        material.SetShaderParameter(
            "emissive_replace",
            binding.GetProperty("emissiveReplace").GetBoolean());
        material.SetShaderParameter(
            "emission_energy",
            configuration.EmissionEnergyMultiplier);
        material.SetShaderParameter(
            "alpha_cutoff",
            lodObjectAtlas
                ? 1.0f / byte.MaxValue
                : alphaMode == "MASK"
                    ? alpha.GetProperty("cutoff").GetSingle()
                    : 0.0f);

        var environmentId = binding.GetProperty("environmentTextureId");
        if (environmentId.ValueKind == JsonValueKind.String)
        {
            if (!textures.Cubemaps.TryGetValue(environmentId.GetString()!, out var environment))
                throw new InvalidOperationException(
                    $"Material environment texture is not a complete cubemap: {environmentId.GetString()}");
            material.NextPass = EnvironmentPass(
                environment,
                normal ?? textures.NeutralNormal,
                Texture(binding, "environmentMaskTextureId", textures.TwoDimensional),
                binding.GetProperty("environmentMapScale").GetSingle(),
                binding.GetProperty("doubleSided").GetBoolean(),
                configuration);
        }
        return material;
    }

    private static ShaderMaterial RetailGrassPass(
        JsonElement binding,
        JsonElement contract,
        LoadedTextures textures,
        RetailGrassCompilerConfiguration configuration)
    {
        if (!RetailGrassHash.TryParse(configuration.Shader.VertexFnv1a32, out var vertexHash) ||
            !RetailGrassHash.TryParse(configuration.Shader.PixelFnv1a32, out var pixelHash) ||
            contract.GetProperty("schema").GetString() != configuration.MaterialSchema ||
            contract.GetProperty("model").GetString() != configuration.MaterialModel ||
            contract.GetProperty("vertexFnv1a32").GetUInt32() != vertexHash ||
            contract.GetProperty("pixelFnv1a32").GetUInt32() != pixelHash ||
            contract.GetProperty("diffuseDomain").GetString() !=
                configuration.Material.DiffuseDomain ||
            contract.GetProperty("sampler").GetString() != configuration.Material.Sampler ||
            contract.GetProperty("vertexLightingBake").GetString() !=
                configuration.Material.VertexLightingBake ||
            contract.GetProperty("windBake").GetString() !=
                configuration.Material.WindBake ||
            !binding.TryGetProperty("diffuseSampleSrgb", out var diffuseSampleSrgb) ||
            diffuseSampleSrgb.GetBoolean() != (configuration.Draw.Sampler.SrgbTexture != 0) ||
            binding.GetProperty("textureClampMode").GetInt32() !=
                configuration.Material.TextureClampMode ||
            binding.GetProperty("unshaded").GetBoolean() !=
                configuration.Material.Unshaded ||
            binding.GetProperty("doubleSided").GetBoolean() !=
                configuration.Material.DoubleSided)
            throw new InvalidOperationException("Unsupported retail grass material contract.");
        var renderState = contract.GetProperty("renderState");
        var expectedRenderState = configuration.Draw.RenderState.Values;
        var actualRenderState = renderState.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.GetInt32(),
                StringComparer.Ordinal);
        if (actualRenderState.Count != expectedRenderState.Count ||
            expectedRenderState.Any(expected =>
                !actualRenderState.TryGetValue(expected.Key, out var actual) ||
                actual != expected.Value))
            throw new InvalidOperationException("Retail grass D3D9 render state changed.");
        var alpha = binding.GetProperty("alphaContract");
        if (alpha.GetProperty("mode").GetString() != configuration.Material.AlphaMode ||
            alpha.GetProperty("testEnabled").GetBoolean() !=
                (configuration.Draw.RenderState.AlphaTestEnable != 0))
            throw new InvalidOperationException("Retail grass alpha contract changed.");
        // alphaContract is the owned NIF material state.  Retail's GRASS23x000
        // program overrides its blend state at draw time, so that state is
        // validated independently above against the captured renderState map.
        _ = alpha.GetProperty("blendEnabled").GetBoolean();
        var fogNear = contract.GetProperty("fogNearGameUnits").GetSingle();
        var fogFar = contract.GetProperty("fogFarGameUnits").GetSingle();
        var fogPower = contract.GetProperty("fogPower").GetSingle();
        var fadeStart = contract.GetProperty("fadeStartGameUnits").GetSingle();
        var fadeRange = contract.GetProperty("fadeRangeGameUnits").GetSingle();
        if (!float.IsFinite(fogNear) || !float.IsFinite(fogFar) ||
            !float.IsFinite(fogPower) || fogFar <= fogNear || fogPower <= 0.0f ||
            fadeStart < 0.0f || fadeRange <= 0.0f)
            throw new InvalidOperationException("Retail grass distance contract is invalid.");

        var material = new ShaderMaterial
        {
            ResourceName = RetailGrassMaterialResourceName,
            Shader = new Shader { Code = BuildRetailGrassShader() },
        };
        material.SetShaderParameter(
            "base_map",
            RequiredTexture(binding, "diffuseTextureId", textures.TwoDimensional));
        material.SetShaderParameter(
            "retail_ambient_color",
            Rgb(ReadColor(contract.GetProperty("ambientColor"))));
        material.SetShaderParameter(
            "retail_diffuse_color",
            Rgb(ReadColor(contract.GetProperty("diffuseColor"))));
        material.SetShaderParameter(
            "retail_directional_scale",
            contract.GetProperty("directionalScale").GetSingle());
        material.SetShaderParameter(
            "retail_fog_color",
            Rgb(ReadColor(contract.GetProperty("fogColor"))));
        material.SetShaderParameter("retail_fog_near_game_units", fogNear);
        material.SetShaderParameter("retail_fog_far_game_units", fogFar);
        material.SetShaderParameter("retail_fog_power", fogPower);
        material.SetShaderParameter("retail_fade_start_game_units", fadeStart);
        material.SetShaderParameter("retail_fade_range_game_units", fadeRange);
        material.SetShaderParameter(
            "retail_alpha_cutoff",
            contract.GetProperty("alphaCutoff").GetSingle());
        material.SetShaderParameter(
            "retail_fixed_alpha_reference",
            renderState.GetProperty("alphaReference").GetSingle() / byte.MaxValue);
        // CellContentLoader applies the prepared root scale after materials
        // are constructed. The owning scene loader replaces this sentinel
        // with the verified world-unit reciprocal before the first frame.
        material.SetShaderParameter("retail_game_units_per_meter", 1.0f);
        return material;
    }

    private static string BuildRetailGrassShader() => """
        shader_type spatial;
        render_mode unshaded, cull_disabled, blend_mix, depth_draw_always;

        uniform sampler2D base_map : filter_linear_mipmap, repeat_enable;
        uniform vec3 retail_ambient_color;
        uniform vec3 retail_diffuse_color;
        uniform float retail_directional_scale;
        uniform vec3 retail_fog_color;
        uniform float retail_fog_near_game_units;
        uniform float retail_fog_far_game_units;
        uniform float retail_fog_power;
        uniform float retail_fade_start_game_units;
        uniform float retail_fade_range_game_units;
        uniform float retail_alpha_cutoff;
        uniform float retail_fixed_alpha_reference;
        uniform float retail_game_units_per_meter;
        varying float retail_distance_game_units;

        void vertex() {
            vec3 retail_view = (MODELVIEW_MATRIX * vec4(VERTEX, 1.0)).xyz;
            retail_distance_game_units = length(retail_view) * retail_game_units_per_meter;
        }

        void fragment() {
            vec4 base = texture(base_map, UV);
            // D3D9 GRASS23x000 uses CMP(cutoff - alpha): equality fails.
            if (base.a <= retail_alpha_cutoff) {
                discard;
            }
            float distance_fade = 1.0 - clamp(
                (retail_distance_game_units - retail_fade_start_game_units) /
                    retail_fade_range_game_units,
                0.0,
                1.0);
            // The shader emits fade as alpha, then fixed-function D3DCMP_GREATER
            // applies the live 10/255 alpha reference before blending.
            if (distance_fade <= retail_fixed_alpha_reference) {
                discard;
            }
            vec3 lighting = COLOR.a * retail_ambient_color +
                COLOR.rgb * retail_diffuse_color * retail_directional_scale;
            ALBEDO = base.rgb * lighting;
            ALPHA = distance_fade;
            float fog_range = retail_fog_far_game_units - retail_fog_near_game_units;
            float fog_base = clamp(
                (retail_distance_game_units - retail_fog_near_game_units) / fog_range,
                0.0,
                1.0);
            FOG = vec4(retail_fog_color, pow(fog_base, retail_fog_power));
        }
        """;

    private static string BuildRetailAmbientDirectionalLambertShader(
        JsonElement binding,
        string alphaMode,
        bool decal)
    {
        var modes = new List<string>
        {
            binding.GetProperty("doubleSided").GetBoolean()
                ? "cull_disabled"
                : "cull_back",
            "ambient_light_disabled",
            "specular_disabled",
        };
        if (alphaMode == "BLEND")
            modes.Add("blend_mix");
        if (decal)
            modes.Add("depth_draw_always");
        else if (alphaMode == "BLEND")
            modes.Add("depth_prepass_alpha");

        var textureClampMode = binding.GetProperty("textureClampMode").GetInt32();
        if (textureClampMode is < 0 or > 3)
            throw new InvalidOperationException(
                $"Unsupported Gamebryo texture clamp mode: {textureClampMode}");
        // NIF TexClampMode is 0=clamp/clamp, 1=clamp/wrap,
        // 2=wrap/clamp, 3=wrap/wrap.  Godot exposes one sampler repeat flag,
        // so mixed modes wrap the required axis explicitly with a clamped
        // sampler.
        var repeat = textureClampMode == 3 ? "repeat_enable" : "repeat_disable";
        var materialUv = textureClampMode switch
        {
            0 => "UV",
            1 => "vec2(UV.x, fract(UV.y))",
            2 => "vec2(fract(UV.x), UV.y)",
            _ => "UV",
        };
        var source = new StringBuilder();
        source.AppendLine("shader_type spatial;");
        source.AppendLine($"render_mode {string.Join(", ", modes)};");
        source.AppendLine(
            $"uniform sampler2D base_map : filter_linear_mipmap_anisotropic, {repeat};");
        source.AppendLine(
            $"uniform sampler2D normal_map : hint_normal, filter_linear_mipmap_anisotropic, {repeat};");
        source.AppendLine(
            $"uniform sampler2D emissive_map : filter_linear_mipmap_anisotropic, {repeat};");
        source.AppendLine("uniform bool use_normal_map;");
        source.AppendLine("uniform bool use_emissive_map;");
        source.AppendLine("uniform vec4 base_color_factor;");
        source.AppendLine("uniform bool use_vertex_color;");
        source.AppendLine("uniform vec3 retail_ambient_color;");
        source.AppendLine("uniform bool retail_fog_enabled;");
        source.AppendLine("uniform vec3 retail_fog_color;");
        source.AppendLine("uniform float retail_fog_near_game_units;");
        source.AppendLine("uniform float retail_fog_far_game_units;");
        source.AppendLine("uniform float retail_fog_power;");
        source.AppendLine("uniform float retail_game_units_per_meter;");
        source.AppendLine("uniform vec3 emissive_color;");
        source.AppendLine("uniform bool emissive_replace;");
        source.AppendLine("uniform float emission_energy;");
        source.AppendLine("uniform float alpha_cutoff;");
        source.AppendLine("varying float retail_fog_factor;");
        source.AppendLine("void vertex() {");
        source.AppendLine(
            "    vec3 retail_view = (MODELVIEW_MATRIX * vec4(VERTEX, 1.0)).xyz;");
        source.AppendLine(
            "    float retail_distance = length(retail_view) * retail_game_units_per_meter;");
        source.AppendLine("    retail_fog_factor = 0.0;");
        source.AppendLine("    if (retail_fog_enabled) {");
        source.AppendLine(
            "        float retail_fog_range = retail_fog_far_game_units - retail_fog_near_game_units;");
        source.AppendLine(
            "        float retail_fog_base = clamp((retail_distance - retail_fog_near_game_units) / retail_fog_range, 0.0, 1.0);");
        source.AppendLine(
            "        retail_fog_factor = pow(retail_fog_base, retail_fog_power);");
        source.AppendLine("    }");
        source.AppendLine("}");
        source.AppendLine("void fragment() {");
        source.AppendLine($"    vec2 material_uv = {materialUv};");
        source.AppendLine(
            "    vec3 vertex_tint = use_vertex_color ? COLOR.rgb : vec3(1.0);");
        source.AppendLine(
            "    vec4 base = texture(base_map, material_uv) * base_color_factor;");
        source.AppendLine("    base.rgb *= vertex_tint;");
        source.AppendLine("    if (use_normal_map) {");
        source.AppendLine(
            "        vec3 tangent_normal = normalize(texture(normal_map, material_uv).rgb * 2.0 - 1.0);");
        source.AppendLine("        NORMAL = normalize(");
        source.AppendLine("            TANGENT * tangent_normal.x +");
        source.AppendLine("            BINORMAL * tangent_normal.y +");
        source.AppendLine("            NORMAL * tangent_normal.z);");
        source.AppendLine("    }");
        source.AppendLine(
            "    vec3 self_illumination = use_emissive_map ? texture(emissive_map, material_uv).rgb * emissive_color : emissive_color;");
        source.AppendLine("    ALBEDO = emissive_replace ? vec3(0.0) : base.rgb;");
        source.AppendLine(
            "    EMISSION = emissive_replace ? emissive_color * emission_energy : base.rgb * retail_ambient_color + self_illumination * emission_energy;");
        source.AppendLine("    FOG = vec4(retail_fog_color, retail_fog_factor);");
        if (alphaMode == "MASK")
        {
            source.AppendLine("    ALPHA = base.a;");
            source.AppendLine("    ALPHA_SCISSOR_THRESHOLD = alpha_cutoff;");
        }
        else if (alphaMode == "BLEND")
            source.AppendLine("    ALPHA = base.a;");
        source.AppendLine("}");
        RetailLighting.AppendDiffuseLightFunction(source);
        return source.ToString();
    }

    private static ShaderMaterial LandscapePass(
        JsonElement binding,
        JsonElement contract,
        LoadedTextures textures)
    {
        if (contract.GetProperty("schema").GetString() != LandscapeMaterialContractSchema ||
            contract.GetProperty("model").GetString() != RetailLandscapeLightingModel ||
            contract.GetProperty("diffuseDomain").GetString() != "encoded" ||
            contract.GetProperty("normalDecode").GetString() !=
                "weighted-signed-rgb-normalize-once" ||
            contract.GetProperty("layerWeightOperation").GetString() !=
                "float32-descending-atxt-sum-base-one-minus-sum-normalize-per-vertex" ||
            contract.GetProperty("weightInterpolation").GetString() !=
                "per-vertex-linear" ||
            contract.GetProperty("weightStorage").GetString() !=
                "generated-17x17-rgba32f-vertex-lookup" ||
            contract.GetProperty("retailWeightType").GetString() != "float4" ||
            contract.GetProperty("source").GetString() !=
                "matched-live-land-shader-package")
            throw new InvalidOperationException("Unsupported LAND material contract schema.");
        var weightSemantics = contract.GetProperty("retailWeightSemantics")
            .EnumerateArray()
            .Select(value => value.GetString())
            .ToArray();
        if (!weightSemantics.SequenceEqual(
                new[] { "TEXCOORD1", "TEXCOORD2" },
                StringComparer.Ordinal) ||
            contract.GetProperty("weightVertexSide").GetInt32() != LandscapeWeightVertexSide ||
            contract.GetProperty("weightLastVertex").GetInt32() != LandscapeWeightLastVertex ||
            !binding.TryGetProperty("diffuseSampleSrgb", out var diffuseSampleSrgb) ||
            diffuseSampleSrgb.GetBoolean())
            throw new InvalidOperationException("LAND material differs from its retail vertex contract.");
        if (binding.GetProperty("doubleSided").GetBoolean() ||
            binding.GetProperty("unshaded").GetBoolean() ||
            binding.GetProperty("emissiveReplace").GetBoolean() ||
            binding.GetProperty("emissiveTextureId").ValueKind != JsonValueKind.Null ||
            binding.GetProperty("environmentTextureId").ValueKind != JsonValueKind.Null ||
            binding.GetProperty("alphaContract").GetProperty("mode").GetString() != "OPAQUE")
            throw new InvalidOperationException("LAND material encountered an unsupported authored pass.");
        var layers = contract.GetProperty("layers").EnumerateArray().ToArray();
        var weightMaps = contract.GetProperty("weightMapTextureIds")
            .EnumerateArray()
            .Select(value => value.GetString()!)
            .ToArray();
        var expectedWeightMapCount = LandscapeWeightMapCount(layers.Length);
        if (weightMaps.Length != expectedWeightMapCount ||
            (weightMaps.Length == 0
                ? contract.GetProperty("baseWeightMapIndex").ValueKind != JsonValueKind.Null ||
                    contract.GetProperty("baseWeightChannel").ValueKind != JsonValueKind.Null
                : contract.GetProperty("baseWeightMapIndex").GetInt32() != 0 ||
                    contract.GetProperty("baseWeightChannel").GetInt32() != 0))
            throw new InvalidOperationException("LAND base weight mapping differs from retail.");
        var expectedSamplerCount = LandscapeBaseSamplerCount +
            layers.Length * LandscapeSamplerCountPerLayer + weightMaps.Length;
        if (expectedSamplerCount > LandscapeSamplerBudget ||
            contract.GetProperty("samplersUsed").GetInt32() != expectedSamplerCount)
            throw new InvalidOperationException("LAND material exceeds its sampler contract.");

        var material = new ShaderMaterial
        {
            ResourceName = RetailLandscapeMaterialResourceName,
            Shader = new Shader
            {
                Code = BuildLandscapeShader(
                    layers.Length,
                    binding.GetProperty("doubleSided").GetBoolean()),
            },
        };
        material.SetShaderParameter(
            "base_diffuse",
            RequiredTexture(binding, "diffuseTextureId", textures.TwoDimensional));
        material.SetShaderParameter(
            "base_normal",
            Texture(binding, "normalTextureId", textures.TwoDimensional) ?? textures.NeutralNormal);
        material.SetShaderParameter(
            "tile_repeats",
            contract.GetProperty("tileRepeats").GetSingle());
        material.SetShaderParameter(
            "albedo_tint",
            ReadColor(binding.GetProperty("baseColorFactor"), 4));
        material.SetShaderParameter("retail_ambient_color", Vector3.Zero);
        material.SetShaderParameter("weight_vertex_side", (float)LandscapeWeightVertexSide);
        material.SetShaderParameter("weight_last_vertex", (float)LandscapeWeightLastVertex);

        for (var index = 0; index < weightMaps.Length; index++)
        {
            if (!textures.TwoDimensional.TryGetValue(weightMaps[index], out var weightMap))
                throw new InvalidOperationException(
                    $"LAND material requires float32 weight map: {weightMaps[index]}");
            material.SetShaderParameter($"weight_map_{index}", weightMap);
        }
        for (var index = 0; index < layers.Length; index++)
        {
            var layer = layers[index];
            var weightOrdinal = index + 1;
            var expectedMapIndex = weightOrdinal / LandscapeWeightsPerMap;
            var expectedChannel = weightOrdinal % LandscapeWeightsPerMap;
            if (layer.GetProperty("weightMapIndex").GetInt32() != expectedMapIndex ||
                layer.GetProperty("weightChannel").GetInt32() != expectedChannel ||
                layer.GetProperty("weightMapTextureId").GetString() !=
                    weightMaps[expectedMapIndex])
                throw new InvalidOperationException(
                    "LAND alpha weight mapping differs from retail TEXCOORD packing.");
            material.SetShaderParameter(
                $"layer_diffuse_{index}",
                RequiredTexture(layer, "diffuseTextureId", textures.TwoDimensional));
            material.SetShaderParameter(
                $"layer_normal_{index}",
                Texture(layer, "normalTextureId", textures.TwoDimensional) ?? textures.NeutralNormal);
        }
        return material;
    }

    private static string BuildLandscapeShader(int layerCount, bool doubleSided)
    {
        var source = new StringBuilder();
        source.AppendLine("shader_type spatial;");
        source.AppendLine(doubleSided
            ? "render_mode cull_disabled, ambient_light_disabled, specular_disabled;"
            : "render_mode cull_back, ambient_light_disabled, specular_disabled;");
        source.AppendLine(
            "uniform sampler2D base_diffuse : filter_linear_mipmap_anisotropic, repeat_enable;");
        source.AppendLine(
            "uniform sampler2D base_normal : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;");
        source.AppendLine("uniform float tile_repeats;");
        source.AppendLine("uniform vec4 albedo_tint;");
        source.AppendLine("uniform vec3 retail_ambient_color;");
        source.AppendLine("uniform vec3 retail_fog_color;");
        source.AppendLine("uniform float retail_fog_near_game_units;");
        source.AppendLine("uniform float retail_fog_far_game_units;");
        source.AppendLine("uniform float retail_fog_power;");
        source.AppendLine("uniform float retail_game_units_per_meter;");
        source.AppendLine("uniform float weight_vertex_side;");
        source.AppendLine("uniform float weight_last_vertex;");
        source.AppendLine("varying float retail_fog_factor;");
        for (var index = 0; index < layerCount; index++)
        {
            source.AppendLine(
                $"uniform sampler2D layer_diffuse_{index} : filter_linear_mipmap_anisotropic, repeat_enable;");
            source.AppendLine(
                $"uniform sampler2D layer_normal_{index} : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;");
        }
        var weightMapCount = LandscapeWeightMapCount(layerCount);
        for (var index = 0; index < weightMapCount; index++)
        {
            source.AppendLine(
                $"uniform sampler2D weight_map_{index} : filter_nearest, repeat_disable;");
            source.AppendLine($"varying vec4 land_weights_{index};");
        }
        source.AppendLine("void vertex() {");
        source.AppendLine(
            "    vec3 retail_view = (MODELVIEW_MATRIX * vec4(VERTEX, 1.0)).xyz;");
        source.AppendLine(
            "    float retail_distance = length(retail_view) * retail_game_units_per_meter;");
        source.AppendLine(
            "    float retail_fog_range = retail_fog_far_game_units - retail_fog_near_game_units;");
        source.AppendLine(
            "    float retail_fog_base = clamp((retail_distance - retail_fog_near_game_units) / retail_fog_range, 0.0, 1.0);");
        source.AppendLine(
            "    retail_fog_factor = pow(retail_fog_base, retail_fog_power);");
        if (weightMapCount > 0)
        {
            source.AppendLine(
                "    vec2 weight_uv = (clamp(UV, vec2(0.0), vec2(1.0)) * weight_last_vertex + vec2(0.5)) / weight_vertex_side;");
            for (var index = 0; index < weightMapCount; index++)
                source.AppendLine(
                    $"    land_weights_{index} = textureLod(weight_map_{index}, weight_uv, 0.0);");
        }
        source.AppendLine("}");
        source.AppendLine("void fragment() {");
        source.AppendLine("    vec2 repeated_uv = UV * tile_repeats;");
        for (var index = 0; index < layerCount; index++)
        {
            var weightOrdinal = index + 1;
            var channel = (weightOrdinal % LandscapeWeightsPerMap) switch
            {
                0 => "r",
                1 => "g",
                2 => "b",
                _ => "a",
            };
            source.AppendLine(
                $"    float layer_weight_{index} = land_weights_{weightOrdinal / LandscapeWeightsPerMap}.{channel};");
        }
        source.AppendLine(weightMapCount == 0
            ? "    float base_weight = 1.0;"
            : "    float base_weight = land_weights_0.r;");
        source.AppendLine(
            "    vec3 terrain = texture(base_diffuse, repeated_uv).rgb * base_weight;");
        source.AppendLine(
            "    vec3 terrain_normal = (texture(base_normal, repeated_uv).rgb * 2.0 - 1.0) * base_weight;");
        for (var index = 0; index < layerCount; index++)
        {
            source.AppendLine(
                $"    terrain += texture(layer_diffuse_{index}, repeated_uv).rgb * layer_weight_{index};");
            source.AppendLine(
                $"    terrain_normal += (texture(layer_normal_{index}, repeated_uv).rgb * 2.0 - 1.0) * layer_weight_{index};");
        }
        source.AppendLine("    terrain_normal = normalize(terrain_normal);");
        source.AppendLine("    NORMAL = normalize(");
        source.AppendLine("        TANGENT * terrain_normal.x +");
        source.AppendLine("        BINORMAL * terrain_normal.y +");
        source.AppendLine("        NORMAL * terrain_normal.z);");
        source.AppendLine("    vec3 diffuse = terrain * albedo_tint.rgb * COLOR.rgb;");
        source.AppendLine("    ALBEDO = diffuse;");
        source.AppendLine("    EMISSION = diffuse * retail_ambient_color;");
        source.AppendLine("    FOG = vec4(retail_fog_color, retail_fog_factor);");
        source.AppendLine("}");
        RetailLighting.AppendDiffuseLightFunction(source);
        return source.ToString();
    }

    private static Texture2D RequiredTexture(
        JsonElement binding,
        string property,
        IReadOnlyDictionary<string, Texture2D> textures)
    {
        var texture = Texture(binding, property, textures);
        return texture ?? throw new InvalidOperationException(
            $"LAND material requires texture binding: {property}");
    }

    private static Color ReadColor(JsonElement values, int expectedComponents = 3)
    {
        var components = values.EnumerateArray().Select(value => value.GetSingle()).ToArray();
        if (components.Length != expectedComponents)
            throw new InvalidOperationException(
                $"Material color must contain {expectedComponents} values.");
        return expectedComponents == 4
            ? new Color(components[0], components[1], components[2], components[3])
            : new Color(components[0], components[1], components[2]);
    }

    private static Vector3 Rgb(Color color) => new(color.R, color.G, color.B);

    internal readonly record struct LoadedTextures(
        IReadOnlyDictionary<string, Texture2D> TwoDimensional,
        IReadOnlyDictionary<string, Cubemap> Cubemaps,
        Texture2D NeutralNormal,
        int AuthoredDdsTextures,
        int AuthoredDdsMipChainTextures,
        int DecodedAuthoredBc1AlphaMipChainTextures,
        int RuntimeGeneratedMipTextures);

    internal sealed class TextureMemoryStore
    {
        private readonly Dictionary<string, (string Contract, StoredTexture Texture)> _entries =
            new(StringComparer.Ordinal);

        internal int UniqueTextures => _entries.Count;
        internal int ReusedTextures { get; private set; }

        internal bool TryGet(string id, string contract, out StoredTexture texture)
        {
            if (!_entries.TryGetValue(id, out var entry))
            {
                texture = default;
                return false;
            }
            if (!entry.Contract.Equals(contract, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"Texture identity {id} has conflicting in-memory contracts.");
            ReusedTextures++;
            texture = entry.Texture;
            return true;
        }

        internal void Add(string id, string contract, StoredTexture texture)
        {
            if (!_entries.TryAdd(id, (contract, texture)))
                throw new InvalidOperationException(
                    $"Texture identity {id} was stored in memory twice.");
        }
    }

    internal readonly record struct StoredTexture(
        Texture2D Texture,
        Cubemap? Cubemap,
        int AuthoredDdsTextures,
        int AuthoredDdsMipChainTextures,
        int DecodedAuthoredBc1AlphaMipChainTextures,
        int RuntimeGeneratedMipTextures);

}
