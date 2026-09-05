using Godot;

namespace OpenNV.Runtime.Formats.Gamebryo;

internal static class NativeNifEffectMaterial
{
    private const string Fragment = """
        shader_type spatial;
        render_mode unshaded, __BLEND__, __DEPTH__, __CULL__;
        uniform sampler2D source_texture : filter_linear_mipmap, __REPEAT__;
        uniform bool source_has_texture;
        uniform vec4 source_color_multiplier;
        uniform vec4 source_falloff;
        uniform bool falloff_enabled;
        uniform bool vertex_alpha_enabled;
        uniform bool alpha_test_enabled;
        uniform int alpha_test_function;
        uniform float alpha_threshold;
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
            if (falloff_enabled) {
                float span = source_falloff.x - source_falloff.y;
                float fraction = span == 0.0 ? 1.0 : clamp(
                    (abs(dot(normalize(NORMAL), normalize(VIEW))) - source_falloff.y) / span, 0.0, 1.0);
                alpha *= mix(source_falloff.w, source_falloff.z, fraction);
            }
            if (alpha_test_enabled && !accepted(alpha)) discard;
            ALBEDO = sampled.rgb * COLOR.rgb * source_color_multiplier.rgb;
            __ALPHA_WRITE__
        }
        """;

    internal static ShaderMaterial Build(FalloutNifNoLightingProperty source,
        FalloutNifMaterialProperty? material, FalloutNifAlphaProperty? alpha, Texture2D? texture,
        bool doubleSided)
    {
        var state = FalloutNifAlphaState.ForNoLighting(source, alpha);
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
        result.SetShaderParameter("source_color_multiplier", material is null ? Vector4.One :
            new Vector4(material.Emissive.R * material.EmissiveMultiple, material.Emissive.G * material.EmissiveMultiple,
                material.Emissive.B * material.EmissiveMultiple, material.Alpha));
        result.SetShaderParameter("source_falloff", new Vector4(falloff.StartCosine, falloff.StopCosine, falloff.StartOpacity, falloff.StopOpacity));
        result.SetShaderParameter("falloff_enabled", useFalloff);
        result.SetShaderParameter("vertex_alpha_enabled", (source.ShaderFlags & 8) != 0);
        result.SetShaderParameter("alpha_test_enabled", state.TestEnabled);
        result.SetShaderParameter("alpha_test_function", (int)state.TestFunction);
        result.SetShaderParameter("alpha_threshold", state.Threshold / 255.0f);
        result.SetMeta("opennv_nif_alpha_flags", alpha?.Flags ?? 0);
        result.SetMeta("opennv_nif_effective_blend", state.Blend.ToString());
        result.SetMeta("opennv_nif_alpha_owner", state.Blend == FalloutNifBlendMode.SourceAlpha &&
            (alpha is null || (alpha.Flags & 1) == 0) ? "no-lighting-falloff-pass" : "source-alpha-property");
        result.SetMeta("opennv_nif_angle_falloff", useFalloff);
        return result;
    }
}
