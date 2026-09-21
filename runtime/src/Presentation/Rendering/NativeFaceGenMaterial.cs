using System.Runtime.CompilerServices;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.Presentation.Rendering;

internal static class NativeFaceGenMaterial
{
    internal const string ResourceIdentity = "Owned NIF FaceGen skin";
    private const uint SkinShaderType = 14;
    private const uint SpecularFlag = 1U;
    private const uint SkinnedFlag = 1U << 1;
    private const uint FaceGenFlag = 1U << 10;
    private const uint RemappableTexturesFlag = 1U << 25;
    private const uint DepthTestFlag = 1U << 31;
    private const uint DepthWriteFlag = 1U;
    private const uint VertexColorsFlag = 1U << 5;
    private static readonly ConditionalWeakTable<RuntimeLiveContentSource, Dictionary<string, Texture2D>> Textures = new();
    private static readonly Dictionary<string, Shader> Shaders = new(StringComparer.Ordinal);

    internal static ShaderMaterial Create(FalloutNpcFaceMaterialInputs inputs,
        FalloutNifFile source, FalloutNifGeometry geometry, RuntimeLiveContentSource content,
        Color sourceAmbient)
    {
        if (!inputs.CanRender)
            throw new NotSupportedException(string.Join("; ", inputs.Blockers));
        FalloutNifShaderProperty? shader = null;
        FalloutNifMaterialProperty? material = null;
        FalloutNifStencilProperty? stencil = null;
        FalloutNifAlphaProperty? alpha = null;
        foreach (var property in geometry.Properties.Where(index => index >= 0).Select(source.ReadObject))
        {
            switch (property)
            {
                case FalloutNifShaderProperty value when shader is null:
                    shader = value;
                    break;
                case FalloutNifMaterialProperty value when material is null:
                    material = value;
                    break;
                case FalloutNifStencilProperty value when stencil is null:
                    stencil = value;
                    break;
                case FalloutNifAlphaProperty value when alpha is null:
                    alpha = value;
                    break;
                default:
                    throw new NotSupportedException($"FaceGen geometry {geometry.Block.Index} has an unbound property: {property.Block.TypeName}.");
            }
        }
        if (shader is null || shader.ShaderType != SkinShaderType || (shader.ShaderFlags & FaceGenFlag) == 0)
            throw new InvalidDataException("The native FaceGen material requires the source skin shader and FaceGen flag.");
        var allowedFlags = SpecularFlag | SkinnedFlag | FaceGenFlag | RemappableTexturesFlag | DepthTestFlag;
        if ((shader.ShaderFlags & ~allowedFlags) != 0 ||
            (shader.ShaderFlags2 & ~(DepthWriteFlag | VertexColorsFlag)) != 0 ||
            shader.Controller >= 0 || shader.ExtraData.Any(index => index >= 0) ||
            shader.RefractionStrength != 0.0f || shader.RefractionFirePeriod != 0)
            throw new NotSupportedException($"Source FaceGen shader {shader.Block.Index} has unbound flags, controllers or refraction.");
        if (material is not null &&
            (material.Controller >= 0 || material.ExtraData.Any(index => index >= 0) || material.Alpha != 1.0f ||
             material.Emissive != new FalloutNifColor3(0, 0, 0)))
            throw new NotSupportedException("The source FaceGen material requires an opacity or emissive constant owner.");
        var doubleSided = false;
        if (stencil is not null)
        {
            if (stencil.Controller >= 0 || stencil.ExtraData.Any(index => index >= 0) ||
                stencil.Flags != 0x4d80 || stencil.Reference != 0 || stencil.Mask != uint.MaxValue)
                throw new NotSupportedException("The source FaceGen stencil state has no render-state owner.");
            doubleSided = true;
        }
        var repeats = FalloutNifTextureAddressing.RepeatForGodot(shader.TextureClampMode);
        var modes = new List<string>
        {
            doubleSided ? "cull_disabled" : "cull_back", "ambient_light_disabled", "specular_disabled",
            (shader.ShaderFlags2 & DepthWriteFlag) != 0 ? "depth_draw_opaque" : "depth_draw_never",
        };
        if ((shader.ShaderFlags & DepthTestFlag) == 0)
            modes.Add("depth_test_disabled");
        var alphaTest = "";
        if (alpha is not null)
        {
            var state = FalloutNifAlphaState.Read(alpha.Flags, alpha.Threshold);
            if (alpha.Controller >= 0 || alpha.ExtraData.Any(index => index >= 0) || state.Blend != FalloutNifBlendMode.Opaque)
                throw new NotSupportedException("FaceGen alpha blending or animation is unbound.");
            if (state.TestEnabled)
            {
                var comparison = state.TestFunction switch
                {
                    0 => "false",
                    1 => "a < t",
                    2 => "a == t",
                    3 => "a <= t",
                    4 => "a > t",
                    5 => "a != t",
                    6 => "a >= t",
                    7 => "true",
                    _ => throw new InvalidDataException("FaceGen alpha comparison is invalid.")
                };
                alphaTest = "float a = texture(base_map, UV).a; float t = " +
                    (alpha.Threshold / 255f).ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "; if (!(" + comparison + ")) discard;";
            }
        }
        var code = BuildShader(string.Join(", ", modes), repeats, alphaTest);
        if (!Shaders.TryGetValue(code, out var compiled))
            Shaders.Add(code, compiled = new Shader { Code = code });
        var result = new ShaderMaterial { Shader = compiled, ResourceName = ResourceIdentity };
        result.SetShaderParameter("base_map", Load(content, inputs.BaseTexturePath));
        result.SetShaderParameter("normal_map", Load(content, inputs.NormalTexturePath));
        result.SetShaderParameter("base_mod_map", Load(content, inputs.BaseMod));
        result.SetShaderParameter("detail_mod_map", Load(content, inputs.DetailMod));
        result.SetShaderParameter("use_source_vertex_color", (shader.ShaderFlags2 & VertexColorsFlag) != 0);
        result.SetShaderParameter("source_specular", material is null || (shader.ShaderFlags & SpecularFlag) == 0 ? Vector3.Zero :
            new Vector3(material.Specular.R, material.Specular.G, material.Specular.B));
        result.SetShaderParameter("source_glossiness", material?.Glossiness ?? 1);
        SetSourceAmbient(result, sourceAmbient);
        result.SetMeta("opennv_nif_shader_block", shader.Block.Index);
        result.SetMeta("opennv_nif_shader_type", shader.ShaderType);
        result.SetMeta("opennv_facegen_base_texture", inputs.BaseTexturePath);
        result.SetMeta("opennv_facegen_normal_texture", inputs.NormalTexturePath);
        result.SetMeta("opennv_facegen_base_mod", inputs.BaseMod.SourceName);
        result.SetMeta("opennv_facegen_detail_mod", inputs.DetailMod.SourceName);
        result.SetMeta("opennv_facegen_settings_source", inputs.SourceSettings.SourcePath);
        if (inputs.ScatteringTexturePath is { } scattering)
            result.SetMeta("opennv_facegen_scattering_texture", scattering);
        // This recovered skin program consumes four maps. Its scattering texture
        // remains source-owned; it must not be substituted for either FaceGen map.
        result.SetMeta("opennv_facegen_scattering_sampler", "not-consumed-by-observed-four-map-program");
        result.SetMeta("opennv_facegen_parity", "unverified");
        result.SetMeta("opennv_facegen_unresolved_render_owners", new string[]
        {
            "native-shader-variant-and-constant-selection",
            "native-vertex-light-and-view-interpolation",
            "native-light-selection-attenuation-and-shadow-pass",
            "native-texture-filter-and-srgb-sampler-state",
            "native-fog-interpolation-and-toggle",
            "native-partial-precision-and-final-output-transfer",
        });
        return result;
    }

    internal static void SetSourceAmbient(ShaderMaterial material, Color sourceAmbient)
    {
        if (!float.IsFinite(sourceAmbient.R) || !float.IsFinite(sourceAmbient.G) || !float.IsFinite(sourceAmbient.B) ||
            sourceAmbient.R < 0 || sourceAmbient.G < 0 || sourceAmbient.B < 0)
            throw new InvalidDataException("Source CELL ambient RGB must be finite and nonnegative.");
        material.SetShaderParameter("source_ambient_rgb", new Vector3(sourceAmbient.R, sourceAmbient.G, sourceAmbient.B));
    }

    private static string BuildShader(string renderModes, bool repeats, string alphaTest) => $$"""
        shader_type spatial;
        render_mode {{renderModes}};

        uniform sampler2D base_map : filter_linear_mipmap_anisotropic, {{(repeats ? "repeat_enable" : "repeat_disable")}};
        uniform sampler2D normal_map : filter_linear_mipmap_anisotropic, {{(repeats ? "repeat_enable" : "repeat_disable")}};
        uniform sampler2D base_mod_map : filter_linear_mipmap_anisotropic, {{(repeats ? "repeat_enable" : "repeat_disable")}};
        uniform sampler2D detail_mod_map : filter_linear_mipmap_anisotropic, {{(repeats ? "repeat_enable" : "repeat_disable")}};
        uniform vec3 source_ambient_rgb;
        uniform bool use_source_vertex_color;
        uniform vec3 source_specular;
        uniform float source_glossiness;
        varying float source_specular_mask;
        {{NativeNifPointLighting.ShaderSource}}

        void fragment() {
            {{alphaTest}}
            vec3 base = texture(base_map, UV).rgb;
            vec3 base_mod = texture(base_mod_map, UV).rgb;
            vec3 detail_mod = texture(detail_mod_map, UV).rgb;
            vec3 face_color = (base + 2.0 * (base_mod - vec3(0.5))) * 4.0 * detail_mod;
            if (use_source_vertex_color) {
                face_color *= COLOR.rgb;
            }
            vec4 normal_sample = texture(normal_map, UV);
            source_specular_mask = normal_sample.a;
            vec3 tangent_normal = normalize(normal_sample.rgb * 2.0 - vec3(1.0));
            NORMAL = normalize(TANGENT * tangent_normal.x + BINORMAL * tangent_normal.y + NORMAL * tangent_normal.z);
            ALBEDO = face_color;
            EMISSION = face_color * (source_ambient_rgb + owned_point_irradiance(VERTEX, NORMAL, VIEW, true));
            EMISSION = owned_output_color(EMISSION);
            SPECULAR = 0.0;
        }

        void light() {
            float diffuse = max(dot(NORMAL, LIGHT), 0.0);
            float grazing = 1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0);
            float backscatter = 0.5 * max(dot(VIEW, -LIGHT), 0.0) * grazing * grazing;
            // Godot supplies LIGHT_COLOR multiplied by PI; the source light RGB
            // has no Lambertian 1/PI normalization.
            DIFFUSE_LIGHT += (diffuse + backscatter) * (LIGHT_COLOR / PI) * ATTENUATION;
            SPECULAR_LIGHT += source_specular * source_specular_mask * (LIGHT_COLOR / PI) * ATTENUATION *
                pow(max(dot(NORMAL, normalize(LIGHT + VIEW)), 0.0), source_glossiness);
        }
        """;

    private static Texture2D Load(RuntimeLiveContentSource content, string path)
    {
        var cache = Textures.GetValue(content, _ => new(StringComparer.OrdinalIgnoreCase));
        if (cache.TryGetValue(path, out var texture))
            return texture;
        if (!content.TryRead(path, null, out var bytes, out var identity))
            throw new FileNotFoundException($"Source FaceGen texture is missing: {path}");
        texture = NativeDdsTexture.Load(bytes, identity);
        texture.SetMeta("opennv_source_texture", identity);
        cache.Add(path, texture);
        return texture;
    }

    private static Texture2D Load(RuntimeLiveContentSource content, FalloutFaceGenTextureInput input)
    {
        if (input.LogicalPath is { } path)
        {
            var texture = Load(content, path);
            if (texture.GetWidth() != input.Width || texture.GetHeight() != input.Height)
                throw new InvalidDataException($"Source FaceGen texture dimensions differ: {input.SourceName}.");
            return texture;
        }
        if (input.Width <= 0 || input.Height <= 0 || (long)input.Width * input.Height * 4 != input.Rgba8.Length)
            throw new InvalidDataException($"Source engine texture has invalid RGBA8 extent: {input.SourceName}.");
        using var image = Image.CreateFromData(input.Width, input.Height, false, Image.Format.Rgba8, input.Rgba8);
        var result = ImageTexture.CreateFromImage(image);
        result.SetMeta("opennv_source_engine_texture", input.SourceName);
        return result;
    }
}
