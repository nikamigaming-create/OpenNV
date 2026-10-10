namespace OpenNV.Runtime.Formats.Gamebryo;

/// <summary>Publish the admitted pixel-program output on an opaque menu surface.</summary>
internal static class FalloutD3D9MenuSurface
{
    internal static string SpatialShader(FalloutD3D9PixelProgram program)
    {
        ArgumentNullException.ThrowIfNull(program);
        // The programmable pixel stage supplies the colour sent to the output
        // merger. A geometry colour is an input only when the original program
        // reads it; the current decoder refuses that register bank. Multiplying
        // by an unrelated NIF colour after execution changes its final output.
        return """
            shader_type spatial;
            render_mode unshaded, cull_back, depth_draw_opaque;
            """ + program.GodotSource + """
            void fragment() {
                vec3 encoded = owned_pixel_program(UV).rgb;
                ALBEDO = mix(encoded / 12.92, pow((max(encoded, vec3(0.0)) + 0.055) / 1.055,
                    vec3(2.4)), step(vec3(0.04045), encoded));
            }
            """;
    }
}
