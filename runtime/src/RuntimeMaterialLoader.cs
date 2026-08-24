using System.Text.Json;
using Godot;

namespace OpenNV.Runtime;

internal static class RuntimeMaterialLoader
{
    private const string EnvironmentShaderPrefix = """
        shader_type spatial;
        render_mode unshaded, blend_add, depth_draw_never, CULL_MODE;

        uniform sampler2D normal_map : hint_normal;
        uniform samplerCube environment_cube : source_color;
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
        RendererConfiguration configuration)
    {
        var textures = new Dictionary<string, Texture2D>(StringComparer.Ordinal);
        var cubemaps = new Dictionary<string, Cubemap>(StringComparer.Ordinal);
        foreach (var texture in scene.GetProperty("textures").EnumerateArray())
        {
            var id = texture.GetProperty("id").GetString()!;
            var path = VerifiedGltfLoader.ResolvePath(texture.GetProperty("png").GetString()!);
            VerifiedGltfLoader.VerifyHash(path, texture.GetProperty("pngSha256").GetString()!);
            var image = Image.LoadFromFile(path);
            if (image is null || image.IsEmpty())
                throw new InvalidOperationException($"Godot could not load prepared texture: {path}");
            if (image.GetWidth() != texture.GetProperty("width").GetInt32() ||
                image.GetHeight() != texture.GetProperty("height").GetInt32())
                throw new InvalidOperationException($"Prepared texture dimensions do not match manifest: {path}");
            textures.Add(id, ImageTexture.CreateFromImage(image));
            if (texture.TryGetProperty("cubeFaces", out var cubeFaces))
            {
                var rows = cubeFaces.EnumerateArray().ToArray();
                if (rows.Length != configuration.CubemapFaceCount)
                    throw new InvalidOperationException($"Prepared cubemap must contain six faces: {id}");
                var images = new Godot.Collections.Array<Image>();
                foreach (var face in rows)
                {
                    var facePath = VerifiedGltfLoader.ResolvePath(face.GetProperty("png").GetString()!);
                    VerifiedGltfLoader.VerifyHash(facePath, face.GetProperty("pngSha256").GetString()!);
                    var faceImage = Image.LoadFromFile(facePath);
                    if (faceImage is null || faceImage.IsEmpty() ||
                        faceImage.GetWidth() != image.GetWidth() ||
                        faceImage.GetHeight() != image.GetHeight())
                        throw new InvalidOperationException($"Prepared cubemap face is invalid: {facePath}");
                    faceImage.Convert(Image.Format.Rgba8);
                    images.Add(faceImage);
                }
                var cubemap = new Cubemap();
                var error = cubemap.CreateFromImages(images);
                if (error != Error.Ok)
                    throw new InvalidOperationException($"Godot rejected prepared cubemap {id}: {error}");
                cubemaps.Add(id, cubemap);
            }
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
            ImageTexture.CreateFromImage(neutralNormalImage));
    }

    internal static int Apply(
        Node3D scene,
        JsonElement asset,
        LoadedTextures textures,
        RendererConfiguration configuration)
    {
        var surfaces = Descendants<MeshInstance3D>(scene)
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
        if (surfaces.Length != bindings.Length)
            throw new InvalidOperationException(
                $"Material/surface count mismatch for asset {asset.GetProperty("id").GetString()}: " +
                $"surfaces={surfaces.Length} bindings={bindings.Length}");
        var surfacesByName = surfaces.ToDictionary(
            surface => surface.Name,
            StringComparer.Ordinal);

        foreach (var binding in bindings)
        {
            var expectedName = binding.GetProperty("name").GetString()!;
            if (!surfacesByName.TryGetValue(expectedName, out var surface))
                throw new InvalidOperationException(
                    $"Imported glTF has no material surface named {expectedName} for asset " +
                    asset.GetProperty("id").GetString());
            var material = new StandardMaterial3D
            {
                Metallic = configuration.DefaultMetallic,
                Roughness = binding.GetProperty("roughness").GetSingle(),
                AlbedoColor = ReadColor(binding.GetProperty("baseColorFactor"), 4),
                VertexColorUseAsAlbedo =
                    binding.GetProperty("vertexColorMode").GetString() != "none",
            };
            material.AlbedoTexture = Texture(binding, "diffuseTextureId", textures.TwoDimensional);
            var normal = Texture(binding, "normalTextureId", textures.TwoDimensional);
            if (normal is not null)
            {
                material.NormalEnabled = true;
                material.NormalTexture = normal;
            }
            var emissive = Texture(binding, "emissiveTextureId", textures.TwoDimensional);
            var emissiveColor = ReadColor(binding.GetProperty("emissiveColor"));
            if (binding.GetProperty("emissiveReplace").GetBoolean())
            {
                material.AlbedoColor = emissiveColor;
                material.AlbedoTexture = null;
                material.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
            }
            else if (emissive is not null || emissiveColor != Colors.Black)
            {
                material.EmissionEnabled = true;
                material.Emission = emissiveColor == Colors.Black ? Colors.White : emissiveColor;
                material.EmissionTexture = emissive;
                material.EmissionOperator = BaseMaterial3D.EmissionOperatorEnum.Multiply;
                material.EmissionEnergyMultiplier = configuration.EmissionEnergyMultiplier;
            }
            var alpha = binding.GetProperty("alphaContract");
            var alphaMode = alpha.GetProperty("mode").GetString();
            if (alphaMode == "BLEND")
                material.Transparency = BaseMaterial3D.TransparencyEnum.AlphaDepthPrePass;
            else if (alphaMode == "MASK")
            {
                material.Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor;
                material.AlphaScissorThreshold = alpha.GetProperty("cutoff").GetSingle();
            }
            else if (alphaMode != "OPAQUE")
                throw new InvalidOperationException($"Unsupported material alpha mode: {alphaMode}");
            if (binding.GetProperty("doubleSided").GetBoolean())
                material.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
            if (binding.GetProperty("unshaded").GetBoolean())
                material.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
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
            surface.Mesh.SetSurfaceOverrideMaterial(surface.Surface, material);
        }
        return bindings.Length;
    }

    private static string NormalizeMaterialName(string value) =>
        value.EndsWith(" material", StringComparison.Ordinal)
            ? value[..^" material".Length]
            : value;

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

    private static IEnumerable<T> Descendants<T>(Node node)
        where T : Node
    {
        foreach (var child in node.GetChildren())
        {
            if (child is T match)
                yield return match;
            foreach (var descendant in Descendants<T>(child))
                yield return descendant;
        }
    }

    internal readonly record struct LoadedTextures(
        IReadOnlyDictionary<string, Texture2D> TwoDimensional,
        IReadOnlyDictionary<string, Cubemap> Cubemaps,
        Texture2D NeutralNormal);
}
