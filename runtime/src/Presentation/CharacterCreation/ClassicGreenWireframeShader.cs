using Godot;

namespace OpenNV.Runtime.Presentation.CharacterCreation;

/// <summary>
/// Shared phosphor edge-wireframe presentation used by classic character monitors.
/// </summary>
internal static class ClassicGreenWireframeShader
{
    internal const string ProjectionRole = "classic-character-green-wireframe";

    internal static ShaderMaterial Create(string resourceName)
    {
        var material = new ShaderMaterial
        {
            ResourceName = resourceName,
            Shader = new Shader
            {
                Code = """
                    shader_type canvas_item;
                    render_mode unshaded;

                    void fragment() {
                        vec4 source = texture(TEXTURE, UV);
                        vec3 left = texture(TEXTURE, UV - vec2(TEXTURE_PIXEL_SIZE.x, 0.0)).rgb;
                        vec3 right = texture(TEXTURE, UV + vec2(TEXTURE_PIXEL_SIZE.x, 0.0)).rgb;
                        vec3 up = texture(TEXTURE, UV - vec2(0.0, TEXTURE_PIXEL_SIZE.y)).rgb;
                        vec3 down = texture(TEXTURE, UV + vec2(0.0, TEXTURE_PIXEL_SIZE.y)).rgb;
                        float edge = length(right - left) + length(down - up);
                        float luma = dot(source.rgb, vec3(0.299, 0.587, 0.114));
                        float line = smoothstep(0.055, 0.24, edge);
                        float body = smoothstep(0.035, 0.16, luma);
                        float scan = 0.82 + 0.18 * step(0.5, fract(FRAGCOORD.y * 0.25));
                        vec3 dark_green = vec3(0.001, 0.025, 0.006);
                        vec3 glow_green = vec3(0.10, 1.00, 0.24);
                        vec3 color = mix(dark_green, glow_green * scan, max(line, body * 0.12));
                        COLOR = vec4(color, source.a);
                    }
                    """,
            },
        };
        material.SetMeta("opennv_projection_role", ProjectionRole);
        return material;
    }
}
