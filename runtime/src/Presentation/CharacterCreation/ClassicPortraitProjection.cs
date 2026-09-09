using Godot;

namespace OpenNV.Runtime.Presentation.CharacterCreation;

internal enum ClassicPortraitMode
{
    Live3D,
    Illustrated,
    WireMesh,
}

/// <summary>
/// Presents the current character render without generating a second face or
/// changing its appearance state. Illustration groups filled tones and dark contours.
/// </summary>
internal static class ClassicPortraitProjection
{
    internal static ShaderMaterial? Create(ClassicPortraitMode mode)
    {
        if (mode == ClassicPortraitMode.Live3D) return null;
        if (mode == ClassicPortraitMode.WireMesh)
            return ClassicGreenWireframeShader.Create("Reflectron 2.0 contour projection");
        if (mode != ClassicPortraitMode.Illustrated) throw new ArgumentOutOfRangeException(nameof(mode));
        return new ShaderMaterial
        {
            ResourceName = "Reflectron 2.0 illustrated Fallout portrait",
            Shader = new Shader
            {
                Code = """
                    shader_type canvas_item;
                    render_mode unshaded;

                    uniform vec2 portrait_size = vec2(848.0, 748.0);
                    uniform float shadow_floor : hint_range(0.0, 0.5) = 0.12;
                    uniform float highlight_ceiling : hint_range(0.1, 1.0) = 0.68;
                    uniform float contour_weight : hint_range(0.0, 1.0) = 0.24;

                    float luminance(vec3 rgb) {
                        return dot(rgb, vec3(0.299, 0.587, 0.114));
                    }

                    float ink_light(sampler2D source, vec2 uv, vec2 pixel) {
                        float value = luminance(texture(source, uv).rgb) * 4.0;
                        value += luminance(texture(source, uv + vec2(pixel.x, 0.0)).rgb);
                        value += luminance(texture(source, uv - vec2(pixel.x, 0.0)).rgb);
                        value += luminance(texture(source, uv + vec2(0.0, pixel.y)).rgb);
                        value += luminance(texture(source, uv - vec2(0.0, pixel.y)).rgb);
                        return sqrt(max(value / 8.0, 0.0));
                    }

                    void fragment() {
                        vec2 pixel = floor(UV * portrait_size);
                        vec2 step_uv = 1.0 / portrait_size;
                        vec2 uv = (pixel + 0.5) * step_uv;
                        vec4 source = texture(TEXTURE, uv);
                        float light = ink_light(TEXTURE, uv, step_uv);
                        float dx = ink_light(TEXTURE, uv + vec2(step_uv.x, 0.0), step_uv)
                            - ink_light(TEXTURE, uv - vec2(step_uv.x, 0.0), step_uv);
                        float dy = ink_light(TEXTURE, uv + vec2(0.0, step_uv.y), step_uv)
                            - ink_light(TEXTURE, uv - vec2(0.0, step_uv.y), step_uv);
                        float contour = smoothstep(0.035, 0.16, length(vec2(dx, dy)));
                        // Illustrated faces keep filled highlights and dark interior
                        // contours. Wire projection has the opposite contour polarity.
                        float tone = smoothstep(shadow_floor, highlight_ceiling, light);
                        tone = max(0.0, tone - contour * contour_weight);
                        float dither = (mod(pixel.x, 2.0) + mod(pixel.y, 2.0) * 2.0) / 4.0 - 0.375;
                        tone = floor(clamp(tone + dither / 14.0, 0.0, 1.0) * 5.0 + 0.5) / 5.0;
                        vec3 shadow = vec3(0.004, 0.018, 0.006);
                        vec3 midtone = vec3(0.10, 0.85, 0.12);
                        vec3 highlight = vec3(0.68, 1.00, 0.48);
                        vec3 color = tone < 0.70
                            ? mix(shadow, midtone, tone / 0.70)
                            : mix(midtone, highlight, (tone - 0.70) / 0.30);
                        COLOR = vec4(color, source.a);
                    }
                    """,
            },
        };
    }
}
