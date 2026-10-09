using Godot;

namespace OpenNV.Runtime.Formats.Gamebryo;

internal static class NativeNifRefractionMaterial
{
    internal const string ResourceIdentity = "Owned NIF refraction";
    internal const uint Flags = (1u << 15) | (1u << 16);

    internal static bool HasDirectController(FalloutNifFile file, FalloutNifShaderProperty property) =>
        (property.ShaderFlags & Flags) != 0 && property.Controller >= 0 &&
        file.ReadObject(property.Controller) is FalloutNifRefractionController controller &&
        controller.Time.Target == property.Block.Index && controller.Time.NextController == -1 &&
        controller.Time.UnknownInteger == 0 && (controller.Time.Flags & 0x60) == 0x40 &&
        file.ReadObject(controller.Interpolator) is FalloutNifFloatInterpolator or FalloutNifSplineFloatInterpolator;

    internal static RuntimeNifControllerSequence DirectChannel(FalloutNifFile file,
        FalloutNifShaderProperty property, IReadOnlyList<Material> materials)
    {
        if (!HasDirectController(file, property))
            throw new NotSupportedException("Direct refraction has no declared source shader controller.");
        var controller = (FalloutNifRefractionController)file.ReadObject(property.Controller);
        var clock = controller.Time;
        FalloutNifControllerClock.Validate(clock, requireActive: false);
        var sampler = new FalloutNifFloatAnimation(file, controller.Interpolator);
        void Sample(float time)
        {
            var strength = sampler.Sample(time);
            foreach (var material in materials) Apply(material, property, strength, time);
        }
        Sample((float)FalloutNifControllerClock.Resolve(clock, 0));
        return new RuntimeNifControllerSequence($"DirectRefraction{controller.Block.Index}",
            (uint)(clock.Flags >> 1) & 3, clock.Frequency, clock.StartTime, clock.StopTime,
            [new RuntimeNifControllerChannel(Sample)])
        { DirectClock = clock };
    }

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
        if (!float.IsFinite(strength) || !float.IsFinite(time)) throw new InvalidDataException("Refraction strength or clock is nonfinite.");
        material.SetMeta("opennv_nif_refraction_strength", strength);
        // A controller writes its property even when the selected source shader
        // does not consume refraction. It cannot select a different shader.
        if ((source.ShaderFlags & Flags) == 0) return;
        if (material is not ShaderMaterial { ResourceName: ResourceIdentity } output)
            throw new NotSupportedException("Refraction channel has no source material owner.");
        output.SetShaderParameter("source_strength", strength);
        output.SetShaderParameter("source_time", time);
    }

    internal static void ApplyPeriod(Material material, FalloutNifShaderProperty source, float period, float time)
    {
        if (!float.IsFinite(period) || !float.IsFinite(time)) throw new InvalidDataException("Refraction period or clock is nonfinite.");
        material.SetMeta("opennv_nif_refraction_fire_period", period);
        if ((source.ShaderFlags & (1u << 16)) == 0) return;
        if (period < 0 || material is not ShaderMaterial { ResourceName: ResourceIdentity } output)
            throw new NotSupportedException("Refraction period has no nonnegative source material owner.");
        output.SetShaderParameter("source_period", period);
        output.SetShaderParameter("source_time", time);
    }
}
