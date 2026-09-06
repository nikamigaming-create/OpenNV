using Godot;

namespace OpenNV.Runtime.Formats.Gamebryo;

internal static class NativeNifEmittanceMaterial
{
    internal const uint ShaderFlag = 1u << 29;
    private const string Capability = "opennv_nif_external_emittance";
    internal const string ShaderSource = """
        uniform bool accepts_external_emittance;
        instance uniform bool source_use_external_emittance;
        instance uniform vec3 source_external_emittance;
        vec3 owned_emissive_color(vec3 authored_color, float multiple) {
            return accepts_external_emittance && source_use_external_emittance
                ? source_external_emittance * multiple : authored_color;
        }
        """;

    internal static void Configure(ShaderMaterial material, uint flags)
    {
        var enabled = (flags & ShaderFlag) != 0;
        material.SetShaderParameter("accepts_external_emittance", enabled);
        material.SetMeta(Capability, enabled);
    }

    internal static bool Accepts(MeshInstance3D mesh) => mesh.Mesh is { } geometry &&
        Enumerable.Range(0, geometry.GetSurfaceCount()).Any(index =>
            mesh.GetActiveMaterial(index) is { } material && material.HasMeta(Capability) && material.GetMeta(Capability).AsBool());

    internal static void Bind(MeshInstance3D mesh, Vector3? color)
    {
        mesh.SetInstanceShaderParameter("source_external_emittance", color ?? Vector3.Zero);
        mesh.SetInstanceShaderParameter("source_use_external_emittance", color is not null);
    }
}
