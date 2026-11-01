using Godot;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace OpenNV.Runtime.Formats.Gamebryo;

internal readonly record struct NativeNifPointLight(Vector3 ViewPosition, Vector3 Diffuse, float Radius);
internal readonly record struct NativeNifDirectionalLight(Vector3 ViewDirection, Vector3 Diffuse);

// The rendered-menu owner supplies its authored NiPointLights in camera space.
// Bethesda publishes a radius in NiLight's repurposed specular field. The
// source shader attenuates by 1 - squared normalized distance.
internal static class NativeNifPointLighting
{
    private const int Capacity = 8;
    private static readonly ConditionalWeakTable<ShaderMaterial, LightTextures> Textures = new();
    internal const string ShaderSource = """
        uniform int owned_point_count = 0;
        uniform vec3 owned_point_position[8];
        uniform vec3 owned_point_diffuse[8];
        uniform float owned_point_radius[8];
        uniform sampler2D owned_point_overflow : filter_nearest, repeat_disable;
        uniform int owned_directional_count = 0;
        uniform sampler2D owned_directional_data : filter_nearest, repeat_disable;
        uniform float owned_light_units = 1.0;
        uniform bool owned_store_encoded = false;
        uniform bool owned_isolated_world = false;
        vec3 owned_output_color(vec3 value) {
            if (!owned_store_encoded) return value;
            return mix(value / 12.92, pow((max(value, vec3(0.0)) + 0.055) / 1.055, vec3(2.4)), step(vec3(0.04045), value));
        }
        vec3 owned_point_irradiance(vec3 position, vec3 normal, vec3 view, bool skin) {
            vec3 result = vec3(0.0);
            for (int index = 0; index < owned_point_count; index++) {
                vec4 point = index < 8 ? vec4(owned_point_position[min(index, 7)], owned_point_radius[min(index, 7)])
                    : texelFetch(owned_point_overflow, ivec2(0, max(index - 8, 0)), 0);
                vec3 color = index < 8 ? owned_point_diffuse[min(index, 7)]
                    : texelFetch(owned_point_overflow, ivec2(1, max(index - 8, 0)), 0).rgb;
                vec3 delta = point.xyz - position;
                float radius = length(delta);
                vec3 direction = delta / max(radius, 0.000001);
                float distance = radius / owned_light_units;
                float normalized = distance / point.w;
                float attenuation = max(1.0 - normalized * normalized, 0.0);
                float diffuse = max(dot(normal, direction), 0.0);
                if (skin) {
                    float grazing = 1.0 - clamp(dot(normal, view), 0.0, 1.0);
                    diffuse += 0.5 * max(dot(view, -direction), 0.0) * grazing * grazing;
                }
                result += color * diffuse * attenuation;
            }
            for (int index = 0; index < owned_directional_count; index++) {
                vec3 direction = texelFetch(owned_directional_data, ivec2(0, index), 0).xyz;
                vec3 color = texelFetch(owned_directional_data, ivec2(1, index), 0).rgb;
                float diffuse = max(dot(normal, direction), 0.0);
                if (skin) {
                    float grazing = 1.0 - clamp(dot(normal, view), 0.0, 1.0);
                    diffuse += 0.5 * max(dot(view, -direction), 0.0) * grazing * grazing;
                }
                result += color * diffuse;
            }
            return result;
        }
        """;

    internal static void Bind(ShaderMaterial material, IReadOnlyList<NativeNifPointLight> lights, float units,
        bool storeEncoded = false, IReadOnlyList<NativeNifDirectionalLight>? directional = null, bool isolatedWorld = false)
    {
        if (!float.IsFinite(units) || units <= 0) throw new ArgumentOutOfRangeException(nameof(units));
        var positions = new Vector3[Capacity]; var colors = new Vector3[Capacity]; var radii = new float[Capacity];
        for (var index = 0; index < lights.Count; index++)
        {
            var light = lights[index];
            if (!light.ViewPosition.IsFinite() || !light.Diffuse.IsFinite() || !float.IsFinite(light.Radius) ||
                light.Diffuse.X < 0 || light.Diffuse.Y < 0 || light.Diffuse.Z < 0 ||
                light.Radius <= 0)
                throw new InvalidDataException("Source NiPointLight color, position or attenuation is invalid.");
            if (index < Capacity)
            { positions[index] = light.ViewPosition; colors[index] = light.Diffuse; radii[index] = light.Radius; }
        }
        directional ??= [];
        foreach (var light in directional)
            if (!light.ViewDirection.IsFinite() || light.ViewDirection.LengthSquared() < .99f || !light.Diffuse.IsFinite() ||
                light.Diffuse.X < 0 || light.Diffuse.Y < 0 || light.Diffuse.Z < 0)
                throw new InvalidDataException("Source directional light has invalid direction or color.");
        var textures = Textures.GetValue(material, _ => new());
        textures.Points.Bind(material, "owned_point_overflow", lights.Skip(Capacity)
            .Select(light => (new Vector4(light.ViewPosition.X, light.ViewPosition.Y, light.ViewPosition.Z, light.Radius), light.Diffuse)).ToArray());
        textures.Directional.Bind(material, "owned_directional_data", directional
            .Select(light => (new Vector4(light.ViewDirection.X, light.ViewDirection.Y, light.ViewDirection.Z, 0), light.Diffuse)).ToArray());
        material.SetShaderParameter("owned_point_count", lights.Count);
        material.SetShaderParameter("owned_point_position", positions);
        material.SetShaderParameter("owned_point_diffuse", colors);
        material.SetShaderParameter("owned_point_radius", radii);
        material.SetShaderParameter("owned_directional_count", directional.Count);
        material.SetShaderParameter("owned_light_units", units);
        material.SetShaderParameter("owned_store_encoded", storeEncoded);
        material.SetShaderParameter("owned_isolated_world", isolatedWorld);
    }

    private sealed class LightTextures
    {
        internal readonly LightTexture Points = new(), Directional = new();
    }

    private sealed class LightTexture
    {
        private ImageTexture? _texture;
        private float[]? _last;
        internal void Bind(ShaderMaterial material, string parameter, IReadOnlyList<(Vector4 Position, Vector3 Color)> lights)
        {
            if (lights.Count == 0) { material.SetShaderParameter(parameter, default(Variant)); return; }
            var data = new float[checked(lights.Count * 8)];
            for (var index = 0; index < lights.Count; index++)
            {
                var (position, color) = lights[index]; var at = index * 8;
                data[at] = position.X; data[at + 1] = position.Y; data[at + 2] = position.Z; data[at + 3] = position.W;
                data[at + 4] = color.X; data[at + 5] = color.Y; data[at + 6] = color.Z;
            }
            if (_last is null || !data.AsSpan().SequenceEqual(_last))
            {
                using var pixels = Image.CreateFromData(2, lights.Count, false, Image.Format.Rgbaf,
                    MemoryMarshal.AsBytes(data.AsSpan()).ToArray());
                if (_texture is null || _last?.Length != data.Length) _texture = ImageTexture.CreateFromImage(pixels);
                else _texture.Update(pixels);
                _last = data;
            }
            material.SetShaderParameter(parameter, _texture ?? throw new InvalidOperationException("Light data texture was not initialized."));
        }
    }
}
