using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Formats.Gamebryo;

internal static class NativeNifMaterialEnvironment
{
    private static readonly StringName AmbientParameter = "source_ambient";
    private static readonly StringName FogParameter = "source_fog_color";
    private static readonly StringName RangeParameter = "source_fog_range";
    private static readonly StringName UnitsParameter = "source_fog_game_units_per_meter";
    internal static void Bind(GeometryInstance3D mesh, FalloutCellLighting lighting, float unitsToMeters)
        => Bind(mesh, Rgb(lighting.AmbientRgb), Rgb(lighting.FogRgb), new(lighting.FogNear, lighting.FogFar, lighting.FogPower), unitsToMeters);

    internal static void Bind(GeometryInstance3D mesh, Vector3 ambient, Vector3 fog, Vector3 range, float unitsToMeters)
    {
        var geometry = mesh switch { MeshInstance3D model => model.Mesh, MultiMeshInstance3D particles => particles.Multimesh?.Mesh, _ => null };
        if (geometry is null) return;
        var lit = false; var effect = false;
        for (var index = 0; index < geometry.GetSurfaceCount(); index++)
        {
            var material = mesh is MeshInstance3D model ? model.GetActiveMaterial(index) : mesh.MaterialOverride ?? geometry.SurfaceGetMaterial(index);
            var name = material?.ResourceName;
            lit |= name is NativeNifLightingMaterial.ResourceIdentity or RuntimeNativeLandscapeTransportBuilder.MaterialIdentity or NativeNifLodMaterial.ResourceIdentity;
            effect |= name == NativeNifEffectMaterial.ResourceIdentity;
        }
        if (!lit && !effect) return;
        if (lit) mesh.SetInstanceShaderParameter(AmbientParameter, ambient);
        mesh.SetInstanceShaderParameter(FogParameter, fog);
        mesh.SetInstanceShaderParameter(RangeParameter, range);
        mesh.SetInstanceShaderParameter(UnitsParameter, 1f / unitsToMeters);
    }

    private static Vector3 Rgb(byte[] value) => new(value[0] / 255f, value[1] / 255f, value[2] / 255f);
}
