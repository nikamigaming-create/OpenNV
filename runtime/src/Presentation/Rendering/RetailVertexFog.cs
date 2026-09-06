namespace OpenNV.Runtime.Presentation.Rendering;

// Source vertex fog uses the length of the forward-depth clip XYZ, before
// perspective division. Preserve that operation and interpolate its result.
internal static class RetailVertexFog
{
    internal const string ShaderSource = """
        float owned_vertex_fog(vec4 view_position, mat4 projection, vec3 fog, float game_units_per_meter) {
            float extent = fog.y - fog.x;
            if (extent <= 0.0) return 0.0;
            vec4 clip = projection * view_position;
            float forward_z = (clip.w - clip.z) / (1.0 - CLIP_SPACE_FAR);
            float distance = length(vec3(clip.xy, forward_z));
            if (projection[3][3] == 0.0) distance *= game_units_per_meter;
            return pow(1.0 - clamp((fog.y - distance) / extent, 0.0, 1.0), fog.z);
        }
        """;
}
