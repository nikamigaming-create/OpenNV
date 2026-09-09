using System.Numerics;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Formats.Gamebryo;

internal static class FalloutNifSurfaceInputs
{
    internal const uint ParallaxFlag = 1U << 11;
    internal const uint SinglePassDecalFlags = (1U << 26) | (1U << 27);

    // PAR programs sample height red at the original UV. Only diffuse RGB and
    // the normal use the shifted coordinates; coverage and glow retain their UV.
    // The interpolated vertex tangent-eye direction is normalized again here.
    internal const string ParallaxShaderSource = """
        vec2 owned_parallax_uv(vec2 uv, float height, vec3 tangent_eye) {
            float length_squared = dot(tangent_eye, tangent_eye);
            vec2 direction = length_squared > 0.0 ? tangent_eye.xy * inversesqrt(length_squared) : vec2(0.0);
            return uv + direction * (height * 0.04 - 0.02);
        }
        """;

    internal static Vector2 ParallaxCoordinates(Vector2 uv, float height, Vector3 tangentEye)
    {
        if (!float.IsFinite(uv.X) || !float.IsFinite(uv.Y) || !float.IsFinite(height) ||
            !float.IsFinite(tangentEye.X) || !float.IsFinite(tangentEye.Y) || !float.IsFinite(tangentEye.Z))
            throw new InvalidDataException("Parallax inputs must be finite.");
        var direction = tangentEye == Vector3.Zero ? Vector3.Zero : Vector3.Normalize(tangentEye);
        return uv + new Vector2(direction.X, direction.Y) * (height * 0.04f - 0.02f);
    }

    internal static void RequireParallaxInputs(FalloutNifMeshData data, bool hasBase, bool hasNormal, bool hasHeight)
    {
        if (!hasBase || !hasNormal || !hasHeight || data.Vertices.Length == 0 ||
            data.Normals.Length != data.Vertices.Length || data.Tangents.Length != data.Vertices.Length ||
            data.Bitangents.Length != data.Vertices.Length || data.TextureCoordinates.Length != 1 ||
            data.TextureCoordinates[0].Length != data.Vertices.Length)
            throw new InvalidDataException("NIF parallax requires diffuse, normal, height and a complete source tangent/UV frame.");
    }

    internal static string TexturePath(string serializedPath)
    {
        // Exporters can retain the installation-relative Data prefix. Validate
        // before removing it so rooted paths and traversal never become valid.
        var path = FalloutBsaArchive.CanonicalPath(serializedPath);
        return path.StartsWith("data\\", StringComparison.Ordinal) ? path[5..] : path;
    }
}
