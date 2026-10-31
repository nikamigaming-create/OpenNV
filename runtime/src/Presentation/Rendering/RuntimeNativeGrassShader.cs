namespace OpenNV.Runtime.Presentation.Rendering;

// Four source shader variants share this dynamic implementation.
internal static class RuntimeNativeGrassShader
{
    internal const string Code = """
        shader_type spatial;
        render_mode unshaded, blend_mix, depth_draw_always, cull_disabled, fog_disabled, skip_vertex_transform;
        // Native grass diffuse stage clamps both axes. Authored cards extend
        // outside the atlas; wrapping samples unrelated opaque atlas fragments.
        uniform sampler2D source_diffuse : source_color, filter_linear_mipmap, repeat_disable;
        uniform float source_alpha_cutoff;
        uniform float source_units;
        uniform bool source_fit_slope;
        uniform bool source_uniform_scale;
        uniform bool source_vertex_lighting;
        uniform vec4 source_wind; // direction.xy, magnitude, authoritative GameHour phase
        uniform vec3 source_ambient;
        uniform vec3 source_diffuse_direction;
        uniform vec3 source_diffuse_color;
        uniform float source_directional_scale;
        uniform vec3 source_fog_color;
        uniform vec3 source_fog; // far, far-near, power
        uniform vec2 source_fade; // start, range
        uniform vec2 source_clip; // native near/far, source game units
        varying vec3 grass_light;
        varying float grass_fog;
        varying float grass_alpha;

        vec3 to_scene(vec3 source) { return vec3(source.x, source.z, -source.y) * source_units; }
        vec4 source_clip_position(vec3 source, mat4 view_matrix, mat4 projection_matrix) {
            vec4 view = view_matrix * vec4(to_scene(source), 1.0);
            vec4 projected = projection_matrix * view;
            float depth = projected.w / source_units;
            float z = source_clip.y / (source_clip.y - source_clip.x) * depth
                - source_clip.y * source_clip.x / (source_clip.y - source_clip.x);
            return vec4(projected.xy / source_units, z, depth);
        }
        void vertex() {
            vec4 packed = INSTANCE_CUSTOM;
            vec3 normal = fract(packed.xyz) * 2.0 - 1.0;
            vec3 point = VERTEX;
            vec3 lighting_normal = source_vertex_lighting ? NORMAL : normal;
            vec3 scale_mask = source_uniform_scale ? vec3(1.0) : vec3(0.0, 0.0, 1.0);
            point *= vec3(1.0) + 0.01 * packed.w * scale_mask;
            if (source_fit_slope) {
                vec3 tangent = abs(normal.y) >= abs(normal.x) && abs(normal.z) >= abs(normal.x)
                    ? vec3(0.0, -normal.z, normal.y) : vec3(-normal.z, 0.0, normal.x);
                tangent = normalize(tangent);
                vec3 across = cross(tangent, normal);
                point = across * point.x + tangent * point.y + normal * point.z;
                if (source_vertex_lighting)
                    lighting_normal = across * NORMAL.x + tangent * NORMAL.y + normal * NORMAL.z;
            }
            float angle = fract(((packed.x + packed.y) * 0.0078125 + source_wind.w)
                * 0.159154937 + 0.5) * 6.28318548 - 3.14159274;
            point.xy += sin(angle) * source_wind.z * COLOR.a * COLOR.a * source_wind.xy;
            point += packed.xyz;
            float shade = fract(packed.w) * 0.75 + 0.25;
            grass_light = shade * source_ambient + shade * COLOR.rgb * source_diffuse_color
                * clamp(dot(source_diffuse_direction, lighting_normal), 0.0, 1.0) * source_directional_scale;
            grass_alpha = 1.0 - clamp((length(source_clip_position(packed.xyz, VIEW_MATRIX, PROJECTION_MATRIX)) - source_fade.x) / source_fade.y, 0.0, 1.0);
            grass_fog = pow(1.0 - clamp((source_fog.x - length(source_clip_position(point, VIEW_MATRIX, PROJECTION_MATRIX).xyz))
                / source_fog.y, 0.0, 1.0), source_fog.z);
            VERTEX = (VIEW_MATRIX * vec4(to_scene(point), 1.0)).xyz;
        }
        void fragment() {
            vec4 texel = texture(source_diffuse, UV);
            float alpha = texel.a <= source_alpha_cutoff ? 0.0 : grass_alpha;
            if (alpha <= 10.0 / 255.0) discard;
            // Godot's unshaded pass writes ALBEDO directly; its EMISSION input
            // belongs to the lit pass and is not added to unshaded output.
            ALBEDO = mix(texel.rgb * grass_light, source_fog_color, grass_fog);
            ALPHA = alpha;
        }
        """;
}
