shader_type spatial;
render_mode diffuse_burley;

uniform sampler2D source_art : source_color, filter_linear_mipmap_anisotropic, repeat_disable;
uniform sampler2D source_depth : filter_nearest, repeat_disable;
uniform vec4 base_color : source_color = vec4(0.2, 0.2, 0.18, 1.0);
uniform float roughness_value;
uniform float metallic_value;
uniform int substance;
uniform float paint_strength;
uniform vec2 paint_facing;
uniform float paint_depth_tolerance;
uniform vec2 grime_meters;
uniform float detail_meters;
uniform vec3 wood_grain_meters;
uniform float cloth_weave_meters;
uniform float detail_fade_meters;
uniform vec2 grime_range;
uniform vec2 detail_range;
uniform float roughness_variation;
uniform float specular_value;

// Mesh-to-assembly coordinates in meters, fixed when the source object binds.
// These follow the prop, independent of camera/world translation and mesh UVs.
instance uniform vec4 surface_x : instance_index(0);
instance uniform vec4 surface_y : instance_index(1);
instance uniform vec4 surface_z : instance_index(2);
instance uniform vec4 source_u : instance_index(3);
instance uniform vec4 source_v : instance_index(4);
instance uniform vec3 source_view : instance_index(5);
varying vec3 surface_position;
varying vec3 surface_normal;
varying vec2 paint_uv;
varying float paint_depth;

float hash(vec3 p) {
    return fract(sin(dot(p, vec3(12.9898, 78.233, 37.719))) * 43758.5453);
}
float value_noise(vec3 p) {
    vec3 i = floor(p), f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    return mix(mix(mix(hash(i), hash(i + vec3(1,0,0)), f.x),
                   mix(hash(i + vec3(0,1,0)), hash(i + vec3(1,1,0)), f.x), f.y),
               mix(mix(hash(i + vec3(0,0,1)), hash(i + vec3(1,0,1)), f.x),
                   mix(hash(i + vec3(0,1,1)), hash(i + vec3(1,1,1)), f.x), f.y), f.z);
}
void vertex() {
    vec4 vertex = vec4(VERTEX, 1.0);
    surface_position = vec3(dot(surface_x, vertex), dot(surface_y, vertex), dot(surface_z, vertex));
    mat3 basis = transpose(mat3(surface_x.xyz, surface_y.xyz, surface_z.xyz));
    surface_normal = normalize(transpose(inverse(basis)) * NORMAL);
    paint_uv = vec2(dot(source_u, vertex), dot(source_v, vertex));
    paint_depth = dot(surface_position, source_view);
}
void fragment() {
    vec3 p = surface_position;
    float grime = value_noise(p / grime_meters.x) * 0.65 + value_noise(p / grime_meters.y) * 0.35;
    float detail = value_noise(p / detail_meters);
    if (substance == 2) detail = value_noise(p / wood_grain_meters);
    float pixel_meters = max(length(dFdx(p)), length(dFdy(p)));
    float fine = clamp(1.0 - pixel_meters / detail_fade_meters, 0.0, 1.0);
    if (substance == 1)
        detail = 0.5 + sin(p.x * TAU / cloth_weave_meters) * sin(p.z * TAU / cloth_weave_meters) * 0.15 * fine;
    else detail = mix(0.5, detail, fine);
    vec3 material_color = base_color.rgb * mix(grime_range.x, grime_range.y, grime) * mix(detail_range.x, detail_range.y, detail);

    // Only the first real surface in the original projection receives paint.
    // No image repetition, silhouette extrusion, alpha holes, or paint on a
    // hidden mattress/underside. Grazing faces fade before long streaks form.
    float facing = dot(normalize(surface_normal), source_view);
    float weight = paint_strength * smoothstep(paint_facing.x, paint_facing.y, facing);
    vec4 paint = texture(source_art, paint_uv);
    float front_depth = texture(source_depth, paint_uv).r;
    if (any(lessThan(paint_uv, vec2(0.0))) || any(greaterThan(paint_uv, vec2(1.0))) ||
        front_depth < -1.0e10 || paint_depth < front_depth - paint_depth_tolerance) weight = 0.0;
    weight *= smoothstep(0.5, 0.98, paint.a);
    ALBEDO = mix(material_color, paint.rgb * mix(detail_range.x, detail_range.y, detail), weight);
    ROUGHNESS = clamp(roughness_value + (1.0 - grime) * roughness_variation, 0.0, 1.0);
    METALLIC = metallic_value;
    SPECULAR = specular_value;
}
