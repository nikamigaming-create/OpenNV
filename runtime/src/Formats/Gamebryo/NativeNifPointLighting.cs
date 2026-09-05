using Godot;

namespace OpenNV.Runtime.Formats.Gamebryo;

internal readonly record struct NativeNifPointLight(Vector3 ViewPosition, Vector3 Diffuse, float Radius);

// The rendered-menu owner supplies its authored NiPointLights in camera space.
// Bethesda publishes a radius in NiLight's repurposed specular field. The
// source shader attenuates by 1 - squared normalized distance.
internal static class NativeNifPointLighting
{
    private const int Capacity = 8;
    internal const string ShaderSource = """
        uniform int owned_point_count = 0;
        uniform vec3 owned_point_position[8];
        uniform vec3 owned_point_diffuse[8];
        uniform float owned_point_radius[8];
        uniform float owned_light_units = 1.0;
        uniform bool owned_store_encoded = false;
        vec3 owned_output_color(vec3 value) {
            if (!owned_store_encoded) return value;
            return mix(value / 12.92, pow((max(value, vec3(0.0)) + 0.055) / 1.055, vec3(2.4)), step(vec3(0.04045), value));
        }
        vec3 owned_point_irradiance(vec3 position, vec3 normal, vec3 view, bool skin) {
            vec3 result = vec3(0.0);
            for (int index = 0; index < owned_point_count; index++) {
                vec3 delta = owned_point_position[index] - position;
                float radius = length(delta);
                vec3 direction = delta / max(radius, 0.000001);
                float distance = radius / owned_light_units;
                float normalized = distance / owned_point_radius[index];
                float attenuation = max(1.0 - normalized * normalized, 0.0);
                float diffuse = max(dot(normal, direction), 0.0);
                if (skin) {
                    float grazing = 1.0 - clamp(dot(normal, view), 0.0, 1.0);
                    diffuse += 0.5 * max(dot(view, -direction), 0.0) * grazing * grazing;
                }
                result += owned_point_diffuse[index] * diffuse * attenuation;
            }
            return result;
        }
        """;

    internal static void Bind(ShaderMaterial material, IReadOnlyList<NativeNifPointLight> lights, float units,
        bool storeEncoded = false)
    {
        if (lights.Count > Capacity) throw new NotSupportedException("Rendered NIF lights require native light selection beyond the shader slots.");
        if (!float.IsFinite(units) || units <= 0) throw new ArgumentOutOfRangeException(nameof(units));
        var positions = new Vector3[Capacity]; var colors = new Vector3[Capacity]; var radii = new float[Capacity];
        for (var index = 0; index < lights.Count; index++)
        {
            var light = lights[index];
            if (!light.ViewPosition.IsFinite() || !light.Diffuse.IsFinite() || !float.IsFinite(light.Radius) ||
                light.Diffuse.X < 0 || light.Diffuse.Y < 0 || light.Diffuse.Z < 0 ||
                light.Radius <= 0)
                throw new InvalidDataException("Source NiPointLight color, position or attenuation is invalid.");
            positions[index] = light.ViewPosition; colors[index] = light.Diffuse; radii[index] = light.Radius;
        }
        material.SetShaderParameter("owned_point_count", lights.Count);
        material.SetShaderParameter("owned_point_position", positions);
        material.SetShaderParameter("owned_point_diffuse", colors);
        material.SetShaderParameter("owned_point_radius", radii);
        material.SetShaderParameter("owned_light_units", units);
        material.SetShaderParameter("owned_store_encoded", storeEncoded);
    }
}
