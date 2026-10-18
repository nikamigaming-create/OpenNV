namespace OpenNV.Runtime.Presentation.Rendering;

// Perspective fog is a world-distance presentation policy. Clip-space XY
// stretches distance with FOV/aspect and asymmetric eye projections, making a
// stationary horizon breathe when the player turns their head. View-space
// length is rotation invariant and uses the same authored range in both modes.
// Orthographic device previews retain their normalized projection convention.
internal static class RetailVertexFog
{
    internal const string ShaderSource = """
        float owned_vertex_fog(vec4 view_position, mat4 projection, vec3 fog, float game_units_per_meter) {
            float extent = fog.y - fog.x;
            if (extent <= 0.0) return 0.0;
            vec4 clip = projection * view_position;
            float forward_z = (clip.w - clip.z) / (1.0 - CLIP_SPACE_FAR);
            float distance = projection[3][3] == 0.0
                ? length(view_position.xyz) * game_units_per_meter
                : length(vec3(clip.xy, forward_z));
            return pow(1.0 - clamp((fog.y - distance) / extent, 0.0, 1.0), fog.z);
        }
        """;
}
