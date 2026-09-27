using Godot;

namespace OpenNV.Runtime.Formats.Gamebryo;

internal static class NativeNifRefractionMaterial
{
    internal const string ResourceIdentity = "Owned NIF refraction";
    internal const uint Flags = (1u << 15) | (1u << 16);

    internal static Material Build(StandardMaterial3D result, FalloutNifShaderProperty source,
        FalloutNifMaterialProperty? material, FalloutNifAlphaProperty? alpha)
    {
        if (result.NormalTexture is null || source.RefractionFirePeriod < 0)
            throw new NotSupportedException("Refraction requires its source normal map and nonnegative period.");
        if (alpha is not null && (alpha.Flags & 1) != 0)
            throw new NotSupportedException("Source refraction with additional alpha blending has no compositor owner.");
        var output = new ShaderMaterial { ResourceName = ResourceIdentity, Shader = new Shader { Code = $$"""
            shader_type spatial;
            render_mode unshaded, blend_mix, {{(result.CullMode == BaseMaterial3D.CullModeEnum.Disabled ? "cull_disabled" : "cull_back")}},
                {{((source.ShaderFlags2 & 1) != 0 ? "depth_draw_always" : "depth_draw_never")}};
            uniform sampler2D scene_color : hint_screen_texture, repeat_disable, filter_linear;
            uniform sampler2D source_normal : repeat_enable, filter_linear_mipmap;
            uniform float source_strength;
            uniform float source_time;
            uniform float source_period;
            uniform float source_alpha;
            {{NativeNifBillboard.ShaderSource}}
            void vertex() {
                if (source_billboard_mode >= 0)
                    MODELVIEW_MATRIX = VIEW_MATRIX * owned_billboard(MODEL_MATRIX, INV_VIEW_MATRIX);
            }
            void fragment() {
                vec2 uv = UV + vec2(0.0, source_period > 0.0 ? source_time / source_period : 0.0);
                vec3 bend = texture(source_normal, uv).xyz * 2.0 - 1.0;
                vec3 direction = normalize(TANGENT * bend.x + BINORMAL * bend.y + NORMAL * bend.z);
                ALBEDO = texture(scene_color, SCREEN_UV + direction.xy * source_strength).rgb;
                ALPHA = source_alpha;
            }
            """ } };
        output.SetShaderParameter("source_normal", result.NormalTexture);
        output.SetShaderParameter("source_alpha", material?.Alpha ?? 1);
        output.SetShaderParameter("source_period", (source.ShaderFlags & (1u << 16)) != 0 ? source.RefractionFirePeriod : 0);
        output.SetMeta("opennv_refraction_parity", "source-strength-and-period;screen-distortion-kernel-unmatched");
        Apply(output, source, source.RefractionStrength, 0);
        return output;
    }

    internal static void Apply(Material material, FalloutNifShaderProperty source, float strength, float time)
    {
        if (material is not ShaderMaterial { ResourceName: ResourceIdentity } output)
            throw new NotSupportedException("Refraction channel has no source material owner.");
        output.SetShaderParameter("source_strength", strength);
        output.SetShaderParameter("source_time", time);
    }
}
