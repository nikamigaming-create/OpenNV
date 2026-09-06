using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Formats.Gamebryo;

internal static class NativeNifMaterialEnvironment
{
    internal static void Bind(MeshInstance3D mesh, FalloutCellLighting lighting, float unitsToMeters)
    {
        if (mesh.Mesh is not { } geometry) return;
        var lit = false; var effect = false;
        for (var index = 0; index < geometry.GetSurfaceCount(); index++)
        {
            var name = mesh.GetActiveMaterial(index)?.ResourceName;
            lit |= name == NativeNifLightingMaterial.ResourceIdentity;
            effect |= name == NativeNifEffectMaterial.ResourceIdentity;
        }
        if (!lit && !effect) return;
        if (lit) mesh.SetInstanceShaderParameter("source_ambient", Rgb(lighting.AmbientRgb));
        mesh.SetInstanceShaderParameter("source_fog_color", Rgb(lighting.FogRgb));
        mesh.SetInstanceShaderParameter("source_fog_range", new Vector3(lighting.FogNear, lighting.FogFar, lighting.FogPower));
        mesh.SetInstanceShaderParameter("source_fog_game_units_per_meter", 1f / unitsToMeters);
    }

    private static Vector3 Rgb(byte[] value) => new(value[0] / 255f, value[1] / 255f, value[2] / 255f);
}
