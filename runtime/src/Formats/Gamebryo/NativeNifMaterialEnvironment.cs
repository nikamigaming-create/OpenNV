using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Formats.Gamebryo;

internal static class NativeNifMaterialEnvironment
{
    internal const string ShaderSource = """
        instance uniform vec3 source_ambient : instance_index(0);
        instance uniform vec3 source_fog_color : instance_index(1);
        instance uniform vec3 source_fog_range : instance_index(2);
        instance uniform float source_fog_game_units_per_meter : instance_index(3);
        instance uniform bool source_exterior_environment : instance_index(7) = false;
        global uniform vec3 opennv_exterior_ambient;
        global uniform vec3 opennv_exterior_fog_color;
        global uniform vec4 opennv_exterior_fog_range;
        vec3 owned_environment_ambient() { return source_exterior_environment ? opennv_exterior_ambient : source_ambient; }
        vec3 owned_environment_fog_color() { return source_exterior_environment ? opennv_exterior_fog_color : source_fog_color; }
        vec3 owned_environment_fog_range() { return source_exterior_environment ? opennv_exterior_fog_range.xyz : source_fog_range; }
        float owned_environment_fog_units() { return source_exterior_environment ? opennv_exterior_fog_range.w : source_fog_game_units_per_meter; }
        """;
    private static readonly StringName AmbientParameter = "source_ambient";
    private static readonly StringName FogParameter = "source_fog_color";
    private static readonly StringName RangeParameter = "source_fog_range";
    private static readonly StringName UnitsParameter = "source_fog_game_units_per_meter";
    private static readonly StringName ExteriorParameter = "source_exterior_environment";
    private static readonly StringName ExteriorAmbient = "opennv_exterior_ambient";
    private static readonly StringName ExteriorFog = "opennv_exterior_fog_color";
    private static readonly StringName ExteriorRange = "opennv_exterior_fog_range";
    internal static void Bind(GeometryInstance3D mesh, FalloutCellLighting lighting, float unitsToMeters)
        => Bind(mesh, Rgb(lighting.AmbientRgb), Rgb(lighting.FogRgb), new(lighting.FogNear, lighting.FogFar, lighting.FogPower), unitsToMeters);

    internal static void Bind(GeometryInstance3D mesh, Vector3 ambient, Vector3 fog, Vector3 range, float unitsToMeters)
    {
        var (lit, effect) = Roles(mesh);
        if (!lit && !effect) return;
        mesh.SetInstanceShaderParameter(ExteriorParameter, false);
        if (lit) mesh.SetInstanceShaderParameter(AmbientParameter, ambient);
        mesh.SetInstanceShaderParameter(FogParameter, fog);
        mesh.SetInstanceShaderParameter(RangeParameter, range);
        mesh.SetInstanceShaderParameter(UnitsParameter, 1f / unitsToMeters);
    }

    internal static void BindExterior(GeometryInstance3D mesh)
    {
        var (lit, effect) = Roles(mesh);
        if (lit || effect) mesh.SetInstanceShaderParameter(ExteriorParameter, true);
    }

    internal static void PublishExterior(Vector3 ambient, Vector3 fog, Vector3 range, float unitsToMeters)
    {
        RenderingServer.GlobalShaderParameterSet(ExteriorAmbient, ambient);
        RenderingServer.GlobalShaderParameterSet(ExteriorFog, fog);
        RenderingServer.GlobalShaderParameterSet(ExteriorRange, new Vector4(range.X, range.Y, range.Z, 1f / unitsToMeters));
    }

    private static (bool Lit, bool Effect) Roles(GeometryInstance3D mesh)
    {
        var geometry = mesh switch { MeshInstance3D model => model.Mesh, MultiMeshInstance3D particles => particles.Multimesh?.Mesh, _ => null };
        if (geometry is null) return (false, false);
        var lit = false; var effect = false;
        for (var index = 0; index < geometry.GetSurfaceCount(); index++)
        {
            var material = mesh is MeshInstance3D model ? model.GetActiveMaterial(index) : mesh.MaterialOverride ?? geometry.SurfaceGetMaterial(index);
            var name = material?.ResourceName;
            lit |= name is NativeNifLightingMaterial.ResourceIdentity or RuntimeNativeLandscapeTransportBuilder.MaterialIdentity or NativeNifLodMaterial.ResourceIdentity;
            effect |= name == NativeNifEffectMaterial.ResourceIdentity;
        }
        return (lit, effect);
    }

    private static Vector3 Rgb(byte[] value) => new(value[0] / 255f, value[1] / 255f, value[2] / 255f);
}
