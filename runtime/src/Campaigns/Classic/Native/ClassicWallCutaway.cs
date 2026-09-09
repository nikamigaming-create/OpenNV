using Godot;

namespace OpenNV.Runtime.Campaigns.Classic.Native;

internal static class ClassicWallCutaway
{
    // All walls use world projection before and during the cutaway. The view
    // opening changes fragments only, never the source collision/hex field.
    private static readonly Shader Surface = new() { Code = """
        shader_type spatial;
        render_mode diffuse_burley;
        uniform sampler2D albedo_map : source_color, hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
        uniform sampler2D normal_map : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
        uniform vec4 tint : source_color = vec4(1.0);
        uniform vec3 texture_scale = vec3(1.0);
        uniform float roughness_value = 0.95;
        uniform float normal_strength = 0.0;
        uniform bool source_uv = false;
        uniform bool reveal_player = false;
        uniform vec3 player_world;
        uniform vec3 camera_world;
        uniform float reveal_radius = 1.25;
        varying vec3 world_position;
        varying vec3 world_normal;
        void vertex() {
            world_position = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
            world_normal = normalize(MODEL_NORMAL_MATRIX * NORMAL);
        }
        void fragment() {
            vec3 segment = camera_world - player_world;
            float along = dot(world_position - player_world, segment) / max(dot(segment, segment), 0.0001);
            float distance_to_view = length(world_position - (player_world + clamp(along, 0.0, 1.0) * segment));
            if (reveal_player && along > 0.0 && along < 0.98 && distance_to_view < reveal_radius) discard;
            vec3 n = normalize(world_normal);
            vec3 weights = pow(abs(n), vec3(3.5)); weights /= max(dot(weights, vec3(1.0)), 0.0001);
            vec3 p = world_position * texture_scale;
            vec3 color = texture(albedo_map, p.zy).rgb * weights.x + texture(albedo_map, p.xz).rgb * weights.y + texture(albedo_map, p.xy).rgb * weights.z;
            if (source_uv) {
                if (UV.x < 0.0 || UV.y < 0.0 || UV.x > 1.0 || UV.y > 1.0) discard;
                vec4 panel = texture(albedo_map, UV);
                if (panel.a < 0.5) discard;
                color = panel.rgb;
            }
            ALBEDO = color * tint.rgb;
            ROUGHNESS = roughness_value;
            if (normal_strength > 0.0) {
                vec3 nx = texture(normal_map, p.zy).xyz * 2.0 - 1.0;
                vec3 ny = texture(normal_map, p.xz).xyz * 2.0 - 1.0;
                vec3 nz = texture(normal_map, p.xy).xyz * 2.0 - 1.0;
                vec3 mapped = vec3(nx.z * sign(n.x), nx.y, nx.x) * weights.x +
                    vec3(ny.x, ny.z * sign(n.y), ny.y) * weights.y +
                    vec3(nz.x, nz.y, nz.z * sign(n.z)) * weights.z;
                NORMAL = normalize((VIEW_MATRIX * vec4(normalize(mix(n, mapped, normal_strength)), 0.0)).xyz);
            }
        }
        """ };

    internal static ShaderMaterial From(StandardMaterial3D source, float radius, bool sourceUv = false)
    {
        var result = new ShaderMaterial { Shader = Surface };
        if (source.AlbedoTexture is not null) result.SetShaderParameter("albedo_map", source.AlbedoTexture);
        if (source.NormalTexture is not null) result.SetShaderParameter("normal_map", source.NormalTexture);
        result.SetShaderParameter("tint", source.AlbedoColor);
        result.SetShaderParameter("texture_scale", source.Uv1Scale);
        result.SetShaderParameter("roughness_value", source.Roughness);
        result.SetShaderParameter("normal_strength", source.NormalEnabled ? source.NormalScale : 0);
        result.SetShaderParameter("reveal_radius", radius);
        result.SetShaderParameter("source_uv", sourceUv);
        return result;
    }
}
