using System.Numerics;

namespace OpenNV.Runtime.Formats.Gamebryo;

// Hair uses a layer map and a scalar tint mask. Vertex RGB is not a diffuse
// colour. The same base-colour equation is present in the owned lit hair
// variants, independently of their light count and anisotropic specular path.
internal static class FalloutNifHairShading
{
    internal const string ShaderSource = """
        vec3 owned_hair_base(vec3 diffuse, vec4 layer, vec3 tint, float tint_mask) {
            return mix(diffuse, layer.rgb, layer.a) *
                (vec3(1.0) + tint_mask * (2.0 * tint - vec3(1.0)));
        }
        """;

    internal static Vector3 BaseColor(Vector3 diffuse, Vector4 layer, Vector3 tint, float tintMask) =>
        Vector3.Lerp(diffuse, new Vector3(layer.X, layer.Y, layer.Z), layer.W) *
        (Vector3.One + tintMask * (2 * tint - Vector3.One));
}
