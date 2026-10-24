shader_type spatial;
render_mode cull_disabled;

uniform float surface_roughness : hint_range(0.0, 1.0) = 1.0;
uniform float surface_metallic : hint_range(0.0, 1.0) = 0.0;
uniform float surface_specular : hint_range(0.0, 1.0) = 0.5;

void fragment() {
	// glTF COLOR_0 is linear. Compatibility decodes its final albedo
	// from sRGB before lighting; Forward+/Mobile consume linear albedo.
	vec3 linear_color = max(COLOR.rgb, vec3(0.0));
	ALBEDO = linear_color;
	if (OUTPUT_IS_SRGB) {
		ALBEDO = mix(1.055 * pow(linear_color, vec3(1.0 / 2.4)) - 0.055,
			12.92 * linear_color, lessThanEqual(linear_color, vec3(0.0031308)));
	}
	ROUGHNESS = surface_roughness;
	METALLIC = surface_metallic;
	SPECULAR = surface_specular;
}
