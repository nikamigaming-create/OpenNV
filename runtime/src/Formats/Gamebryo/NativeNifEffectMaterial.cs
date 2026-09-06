using Godot;
using OpenNV.Runtime.Presentation.Rendering;

namespace OpenNV.Runtime.Formats.Gamebryo;

internal static class NativeNifEffectMaterial
{
    internal const string ResourceIdentity = "Owned NIF no-lighting";
    // The no-lighting material owner substitutes white only when the complete
    // resolved emissive colour is zero, preserving texture and vertex colour.
    internal const string ColorFallbackShader = """
        vec3 owned_no_light_color(vec3 color) {
            return all(equal(color, vec3(0.0))) ? vec3(1.0) : color;
        }
        """;

    private const string Fragment = $$"""
        shader_type spatial;
        render_mode unshaded, fog_disabled, __BLEND__, __DEPTH__, __CULL__;
        uniform sampler2D source_texture : filter_linear_mipmap, __REPEAT__;
        uniform bool source_has_texture;
        uniform vec4 source_color_multiplier;
        uniform float source_emissive_multiple;
        uniform vec2 source_uv_offset;
        uniform vec4 source_falloff;
        uniform bool falloff_enabled;
        uniform bool vertex_alpha_enabled;
        uniform bool alpha_test_enabled;
        uniform int alpha_test_function;
        uniform float alpha_threshold;
        uniform vec2 source_fog_blend;
        instance uniform vec3 source_fog_color;
        instance uniform vec3 source_fog_range;
        instance uniform float source_fog_game_units_per_meter;
        varying float source_view_opacity;
        varying float source_fog_factor;
        {{RetailVertexFog.ShaderSource}}
        {{FalloutNifFogBlend.ShaderSource}}
        {{FalloutNifAngleFalloff.ShaderSource}}
        {{NativeNifEmittanceMaterial.ShaderSource}}
        {{ColorFallbackShader}}
        void vertex() {
            UV += source_uv_offset;
            vec4 view_position = MODELVIEW_MATRIX * vec4(VERTEX, 1.0);
            source_fog_factor = owned_vertex_fog(view_position,
                PROJECTION_MATRIX, source_fog_range, source_fog_game_units_per_meter);
            source_view_opacity = 1.0;
            if (falloff_enabled) {
                vec3 view_normal = mat3(MODELVIEW_MATRIX) * NORMAL;
                source_view_opacity = owned_angle_opacity(dot(normalize(view_position.xyz), normalize(view_normal)), source_falloff);
            }
        }
        bool accepted(float alpha) {
            if (alpha_test_function == 0) return true;
            if (alpha_test_function == 1) return alpha < alpha_threshold;
            if (alpha_test_function == 2) return alpha == alpha_threshold;
            if (alpha_test_function == 3) return alpha <= alpha_threshold;
            if (alpha_test_function == 4) return alpha > alpha_threshold;
            if (alpha_test_function == 5) return alpha != alpha_threshold;
            if (alpha_test_function == 6) return alpha >= alpha_threshold;
            return false;
        }
        void fragment() {
            vec4 sampled = source_has_texture ? texture(source_texture, UV) : vec4(1.0);
            float alpha = sampled.a * source_color_multiplier.a;
            if (vertex_alpha_enabled) alpha *= COLOR.a;
            alpha *= source_view_opacity;
            if (alpha_test_enabled && !accepted(alpha)) discard;
            vec3 material_color = owned_emissive_color(source_color_multiplier.rgb, source_emissive_multiple);
            vec3 color = sampled.rgb * COLOR.rgb * owned_no_light_color(material_color);
            ALBEDO = owned_no_light_fog(color, source_fog_color, source_fog_factor, source_fog_blend);
            __ALPHA_WRITE__
        }
        """;

    internal static ShaderMaterial Build(FalloutNifNoLightingProperty source,
        FalloutNifMaterialProperty? material, FalloutNifAlphaProperty? alpha, Texture2D? texture,
        bool doubleSided)
    {
        var state = FalloutNifAlphaState.ForNoLighting(source, alpha);
        var fog = FalloutNifFogBlend.Read(alpha?.Flags);
        var useFalloff = (source.ShaderFlags & (1u << 6)) != 0;
        var falloff = useFalloff ? FalloutNifAngleFalloff.Read(source) : new(1, 0, 1, 1);
        var blend = state.Blend switch
        {
            FalloutNifBlendMode.Add => "blend_add",
            FalloutNifBlendMode.Premultiplied => "blend_premul_alpha",
            FalloutNifBlendMode.Multiply => "blend_mul",
            _ => "blend_mix",
        };
        var result = new ShaderMaterial
        {
            ResourceName = ResourceIdentity,
            Shader = new Shader
            {
                Code = Fragment
                .Replace("__BLEND__", blend)
                .Replace("__DEPTH__", (source.ShaderFlags2 & 1) != 0 ? "depth_draw_always" : "depth_draw_never")
                .Replace("__CULL__", doubleSided ? "cull_disabled" : "cull_back")
                .Replace("__ALPHA_WRITE__", state.Blend == FalloutNifBlendMode.Opaque ? "" : "ALPHA = alpha;")
                .Replace("__REPEAT__", FalloutNifTextureAddressing.RepeatForGodot(source.TextureClampMode) ? "repeat_enable" : "repeat_disable")
            },
        };
        if (texture is not null) result.SetShaderParameter("source_texture", texture);
        result.SetShaderParameter("source_has_texture", texture is not null);
        result.SetShaderParameter("source_emissive_multiple", material?.EmissiveMultiple ?? 1);
        NativeNifEmittanceMaterial.Configure(result, source.ShaderFlags);
        result.SetShaderParameter("source_color_multiplier", material is null ? Vector4.One :
            new Vector4(material.Emissive.R * material.EmissiveMultiple, material.Emissive.G * material.EmissiveMultiple,
                material.Emissive.B * material.EmissiveMultiple, material.Alpha));
        result.SetShaderParameter("source_falloff", new Vector4(falloff.StartCosine, falloff.StopCosine, falloff.StartOpacity, falloff.StopOpacity));
        result.SetShaderParameter("falloff_enabled", useFalloff);
        result.SetShaderParameter("vertex_alpha_enabled", (source.ShaderFlags & 8) != 0);
        result.SetShaderParameter("alpha_test_enabled", state.TestEnabled);
        result.SetShaderParameter("alpha_test_function", (int)state.TestFunction);
        result.SetShaderParameter("alpha_threshold", state.Threshold / 255.0f);
        result.SetShaderParameter("source_fog_blend", new Vector2(fog.Additive ? 1 : 0, fog.DestinationColor ? 1 : 0));
        result.SetMeta("opennv_nif_fog_owner", "projected-vertex;source-destination-blend-factor");
        result.SetMeta("opennv_nif_fog_unbound", "native-pass-admission-and-GPU-draw-association");
        result.SetMeta("opennv_nif_alpha_flags", alpha?.Flags ?? 0);
        result.SetMeta("opennv_nif_effective_blend", state.Blend.ToString());
        result.SetMeta("opennv_nif_alpha_owner", state.Blend == FalloutNifBlendMode.SourceAlpha &&
            (alpha is null || (alpha.Flags & 1) == 0) ? "no-lighting-falloff-pass" : "source-alpha-property");
        result.SetMeta("opennv_nif_angle_falloff", useFalloff);
        if (useFalloff) result.SetMeta("opennv_nif_falloff_owner", "vertex-view-normal-and-position;smooth-cosine-opacity");
        return result;
    }

    internal static void ApplyEmissiveColor(ShaderMaterial material, Vector3 color)
    {
        var previous = material.GetShaderParameter("source_color_multiplier").AsVector4();
        var multiple = material.GetShaderParameter("source_emissive_multiple").AsSingle();
        material.SetShaderParameter("source_color_multiplier", new Vector4(
            color.X * multiple, color.Y * multiple, color.Z * multiple, previous.W));
    }
}
