using System.Text;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Presentation.Rendering;

namespace OpenNV.Runtime.Formats.Gamebryo;

// SLS samples diffuse bytes in the encoded domain. StandardMaterial3D applies
// an sRGB decode and a PBR BRDF, so it cannot feed the source HDR/cinematic pass.
internal static class NativeNifLightingMaterial
{
    internal const string ResourceIdentity = "Owned NIF SLS lighting";
    private static readonly Dictionary<string, Shader> Shaders = new(StringComparer.Ordinal);

    internal static ShaderMaterial Build(StandardMaterial3D textures, FalloutNifShaderProperty source,
        FalloutNifMaterialProperty? material, FalloutNifAlphaProperty? alpha, FalloutNifVertexColorState vertexColors)
    {
        var state = alpha is null ? new FalloutNifAlphaState(FalloutNifBlendMode.Opaque, false, 0, 0, true)
            : FalloutNifAlphaState.Read(alpha.Flags, alpha.Threshold);
        var modes = new List<string> { "ambient_light_disabled", "specular_disabled",
            textures.CullMode == BaseMaterial3D.CullModeEnum.Disabled ? "cull_disabled" :
                textures.CullMode == BaseMaterial3D.CullModeEnum.Front ? "cull_front" : "cull_back",
            (source.ShaderFlags2 & 1) != 0 ? "depth_draw_always" : "depth_draw_never" };
        if ((source.ShaderFlags & (1u << 31)) == 0) modes.Add("depth_test_disabled");
        modes.Add(state.Blend switch
        {
            FalloutNifBlendMode.Add => "blend_add",
            FalloutNifBlendMode.Multiply => "blend_mul",
            FalloutNifBlendMode.Premultiplied => "blend_premul_alpha",
            _ => "blend_mix",
        });
        var repeat = textures.TextureRepeat ? "repeat_enable" : "repeat_disable";
        var code = new StringBuilder($$"""
            shader_type spatial;
            render_mode {{string.Join(", ", modes)}};
            uniform sampler2D base_map : filter_linear_mipmap_anisotropic, {{repeat}};
            uniform sampler2D normal_map : filter_linear_mipmap_anisotropic, {{repeat}};
            uniform sampler2D emissive_map : filter_linear_mipmap_anisotropic, {{repeat}};
            uniform bool use_base_map;
            uniform bool use_normal_map;
            uniform bool use_emissive_map;
            uniform bool use_vertex_alpha;
            uniform bool use_vertex_color;
            uniform bool use_hair;
            uniform vec3 hair_tint;
            uniform vec4 base_factor;
            uniform vec3 emissive_color;
            uniform float emissive_multiple;
            uniform vec3 source_specular;
            uniform float source_glossiness;
            uniform samplerCube environment_cube;
            uniform sampler2D environment_mask : filter_linear_mipmap_anisotropic, {{repeat}};
            uniform bool use_environment;
            uniform bool use_environment_mask;
            uniform bool environment_light_fade;
            uniform float environment_scale;
            instance uniform vec3 source_ambient;
            instance uniform vec3 source_fog_color;
            instance uniform vec3 source_fog_range;
            varying float source_fog_factor;
            varying float source_specular_mask;
            {{NativeNifPointLighting.ShaderSource}}
            {{FalloutNifHairShading.ShaderSource}}
            void vertex() {
                float distance_to_eye = length((MODELVIEW_MATRIX * vec4(VERTEX, 1.0)).xyz);
                float extent = source_fog_range.y - source_fog_range.x;
                source_fog_factor = extent > 0.0 ? pow(clamp(
                    (distance_to_eye - source_fog_range.x) / extent, 0.0, 1.0), source_fog_range.z) : 0.0;
            }
            void fragment() {
                vec4 base = use_base_map ? texture(base_map, UV) : vec4(1.0);
                if (use_hair) {
                    vec4 layer = use_emissive_map ? texture(emissive_map, UV) : vec4(0.0);
                    base.rgb = owned_hair_base(base.rgb, layer, hair_tint, use_vertex_color ? COLOR.g : 1.0);
                } else if (use_vertex_color) {
                    base.rgb *= COLOR.rgb;
                }
                base *= base_factor;
                if (use_vertex_alpha) base.a *= COLOR.a;
                {{(state.TestEnabled ? $"if (!({TestExpression(state.TestFunction, state.Threshold)})) discard;" : "")}}
                source_specular_mask = 1.0;
                if (use_normal_map) {
                    vec4 normal_sample = texture(normal_map, UV);
                    vec3 tangent_normal = normalize(normal_sample.rgb * 2.0 - 1.0);
                    NORMAL = normalize(TANGENT * tangent_normal.x + BINORMAL * tangent_normal.y + NORMAL * tangent_normal.z);
                    source_specular_mask = normal_sample.a;
                }
                vec3 reflection = vec3(0.0);
                if (use_environment) {
                    vec3 reflected_view = reflect(-normalize(VIEW), NORMAL);
                    vec3 reflected_world = normalize((INV_VIEW_MATRIX * vec4(reflected_view, 0.0)).xyz);
                    float mask = use_environment_mask ? texture(environment_mask, UV).r : source_specular_mask;
                    reflection = texture(environment_cube, reflected_world).rgb * mask * environment_scale;
                }
                vec3 lit = base.rgb + (environment_light_fade ? reflection : vec3(0.0));
                ALBEDO = lit;
                vec3 glow = use_emissive_map ? texture(emissive_map, UV).rgb : vec3(1.0);
                EMISSION = lit * (source_ambient + owned_point_irradiance(VERTEX, NORMAL, VIEW, false));
                if (!use_hair) EMISSION += glow * emissive_color * emissive_multiple;
                if (!environment_light_fade) EMISSION += reflection;
                EMISSION = owned_output_color(EMISSION);
                FOG = vec4(source_fog_color, source_fog_factor);
                {{(state.Blend == FalloutNifBlendMode.Opaque ? "" : "ALPHA = base.a;")}}
            }
            """);
        RetailLighting.AppendDiffuseLightFunction(code);
        // Specular remains a separate source lane. This is the NIF gloss core;
        // native variant/constant and per-light selection still require matching.
        code.Replace("    DIFFUSE_LIGHT +=", "    SPECULAR_LIGHT += source_specular * source_specular_mask * (LIGHT_COLOR / PI) * retail_attenuation * pow(max(dot(NORMAL, normalize(LIGHT + VIEW)), 0.0), source_glossiness);\n    DIFFUSE_LIGHT +=");
        var shaderCode = code.ToString();
        if (!Shaders.TryGetValue(shaderCode, out var compiled)) Shaders.Add(shaderCode, compiled = new Shader { Code = shaderCode });
        var result = new ShaderMaterial { Shader = compiled, ResourceName = ResourceIdentity };
        if (textures.NextPass is ShaderMaterial environment && environment.HasMeta("opennv_environment_light_fade"))
        {
            // Compose the source cube contribution with source lighting before
            // output transfer. A separate Godot additive pass added encoded
            // cube samples to an already decoded target and brightened shadows.
            result.SetShaderParameter("use_environment", true);
            result.SetShaderParameter("environment_light_fade", environment.GetMeta("opennv_environment_light_fade"));
            foreach (var name in new[] { "environment_cube", "environment_mask", "use_environment_mask", "environment_scale" })
                result.SetShaderParameter(name, environment.GetShaderParameter(name));
        }
        else if (textures.NextPass is not null)
            throw new NotSupportedException("Source SLS next pass has no composition owner.");
        SetTexture(result, "base", textures.AlbedoTexture);
        SetTexture(result, "normal", textures.NormalTexture);
        SetTexture(result, "emissive", textures.EmissionTexture);
        var color = textures.AlbedoColor;
        var hair = (source.ShaderFlags & FalloutNpcAppearanceHairColor.ShaderFlag) != 0;
        result.SetShaderParameter("use_hair", hair);
        result.SetShaderParameter("hair_tint", new Vector3(color.R, color.G, color.B));
        result.SetShaderParameter("base_factor", hair ? new Vector4(1, 1, 1, color.A) : new Vector4(color.R, color.G, color.B, color.A));
        result.SetShaderParameter("use_vertex_color", vertexColors.Enabled);
        result.SetShaderParameter("use_vertex_alpha", (source.ShaderFlags & 8) != 0);
        result.SetShaderParameter("emissive_color", material is null ? Vector3.Zero :
            new Vector3(material.Emissive.R, material.Emissive.G, material.Emissive.B));
        result.SetShaderParameter("emissive_multiple", material?.EmissiveMultiple ?? 0);
        result.SetShaderParameter("source_specular", (source.ShaderFlags & 1) == 0 || material is null ? Vector3.Zero :
            new Vector3(material.Specular.R, material.Specular.G, material.Specular.B));
        result.SetShaderParameter("source_glossiness", material?.Glossiness ?? 1);
        result.SetMeta("opennv_nif_shader_flags", source.ShaderFlags);
        result.SetMeta("opennv_nif_shader_flags2", source.ShaderFlags2);
        result.SetMeta("opennv_nif_effective_shader_flags2", vertexColors.EffectiveFlags2);
        result.SetMeta("opennv_nif_vertex_color_owner", "bound-geometry-colour-buffer");
        result.SetMeta("opennv_nif_alpha_flags", alpha?.Flags ?? 0);
        result.SetMeta("opennv_source_lighting_domain", "encoded");
        if (hair)
        {
            result.SetMeta("opennv_hair_rgb", new Vector3(color.R, color.G, color.B));
            result.SetMeta("opennv_hair_layer_slot", 2);
            result.SetMeta("opennv_hair_vertex_semantics", "green-tint-mask;not-diffuse-rgb");
            result.SetMeta("opennv_hair_unbound", "native-variant-selection,anisotropic-specular,partial-precision");
        }
        result.SetMeta("opennv_source_lighting_parity", "unverified-light-selection-shadows-specular");
        return result;
    }

    internal static void SetTexture(ShaderMaterial material, string slot, Texture2D? texture)
    {
        if (texture is not null) material.SetShaderParameter(slot + "_map", texture);
        material.SetShaderParameter("use_" + slot + "_map", texture is not null);
    }

    internal static void ApplyEnvironment(Node root, FalloutCellLighting lighting, float unitsToMeters)
    {
        foreach (var child in root.GetChildren()) ApplyEnvironment(child, lighting, unitsToMeters);
        if (root is not MeshInstance3D mesh || mesh.Mesh is null) return;
        if (!Enumerable.Range(0, mesh.Mesh.GetSurfaceCount()).Any(index => mesh.GetActiveMaterial(index)?.ResourceName == ResourceIdentity)) return;
        mesh.SetInstanceShaderParameter("source_ambient", Rgb(lighting.AmbientRgb));
        mesh.SetInstanceShaderParameter("source_fog_color", Rgb(lighting.FogRgb));
        mesh.SetInstanceShaderParameter("source_fog_range", new Vector3(lighting.FogNear * unitsToMeters,
            lighting.FogFar * unitsToMeters, lighting.FogPower));
    }

    private static Vector3 Rgb(byte[] value) => new(value[0] / 255.0f, value[1] / 255.0f, value[2] / 255.0f);
    private static string TestExpression(byte function, byte threshold)
    {
        var value = (threshold / 255.0f).ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        if (!value.Contains('.') && !value.Contains('E')) value += ".0";
        return function switch
        {
            0 => "true",
            1 => $"base.a < {value}",
            2 => $"base.a == {value}",
            3 => $"base.a <= {value}",
            4 => $"base.a > {value}",
            5 => $"base.a != {value}",
            6 => $"base.a >= {value}",
            7 => "false",
            _ => throw new InvalidDataException("Invalid NIF alpha test function."),
        };
    }
}
