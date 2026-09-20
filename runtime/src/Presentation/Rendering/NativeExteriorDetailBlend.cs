using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.Presentation.Rendering;

// Presentation scheduling policy, not a retail distance/parity claim. Both
// source LAND and distant NIF passes use complementary opaque coverage, so
// their handoff does not introduce an alpha-sorted ring or remove collision.
internal static class NativeExteriorDetailBlend
{
    internal const string ShaderSource = """
        global uniform vec4 opennv_detail_region;
        float opennv_detail_coverage(vec2 world_xz) {
            if (opennv_detail_region.w <= 0.0) return 1.0;
            float distance_xz = length(world_xz - opennv_detail_region.xy);
            return 1.0 - smoothstep(opennv_detail_region.z, opennv_detail_region.w, distance_xz);
        }
        float opennv_bayer2(vec2 p) { return 2.0 * p.x + 3.0 * p.y - 4.0 * p.x * p.y; }
        float opennv_detail_threshold(vec2 pixel) {
            vec2 p = mod(floor(pixel), 4.0);
            return (4.0 * opennv_bayer2(mod(p, 2.0)) + opennv_bayer2(floor(p / 2.0)) + 0.5) / 16.0;
        }
        """;
    internal const string NearDeclarations = ShaderSource + """

        instance uniform bool opennv_detail_enabled : instance_index(6) = false;
        varying vec2 opennv_detail_world;
        """;
    internal const string NearVertex = "opennv_detail_world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xz;";
    internal const string NearFragment = "if (opennv_detail_enabled && opennv_detail_coverage(opennv_detail_world) <= opennv_detail_threshold(FRAGCOORD.xy)) discard;";

    internal static void Bind(Node root)
    {
        foreach (var mesh in root.FindChildren("*", "", true, false).OfType<MeshInstance3D>())
        {
            if (Enumerable.Range(0, mesh.Mesh.GetSurfaceCount()).All(index => mesh.GetActiveMaterial(index)?.ResourceName is
                NativeNifLightingMaterial.ResourceIdentity or RuntimeNativeLandscapeTransportBuilder.MaterialIdentity))
                mesh.SetInstanceShaderParameter("opennv_detail_enabled", true);
        }
    }

    internal static void SetRegion(Vector3 camera, float cellWidth, int radius, bool covered)
    {
        var outer = radius * cellWidth;
        RenderingServer.GlobalShaderParameterSet("opennv_detail_region",
            covered ? new Vector4(camera.X, camera.Z, Math.Max(0, outer - cellWidth * .5f), outer) : Vector4.Zero);
    }
}
