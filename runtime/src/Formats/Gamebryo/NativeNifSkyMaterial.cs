using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Presentation.Rendering;

namespace OpenNV.Runtime.Formats.Gamebryo;

internal static class NativeNifSkyMaterial
{
    internal const string Identity = "Owned NIF sky";
    private static readonly Dictionary<uint, Shader> Shaders = [];

    internal static ShaderMaterial? TryBuild(FalloutNifFile source, FalloutNifGeometry geometry)
    {
        var declaration = geometry.Properties.Where(index => index >= 0).Select(source.ReadObject)
            .OfType<FalloutNifSkyShaderProperty>().SingleOrDefault();
        if (declaration is null) return null;
        var code = declaration.SkyObjectType switch
        {
            2 => RetailEnvironmentRenderer.AtmosphereShaderSource,
            3 => RetailEnvironmentRenderer.CloudShaderSource,
            5 => RetailEnvironmentRenderer.NightSkyShaderSource,
            _ => throw new NotSupportedException($"Sky object type {declaration.SkyObjectType} has no material owner."),
        };
        if (declaration.Controller != -1 || declaration.ExtraData.Length != 0)
            throw new NotSupportedException("Sky material has an unbound controller or extra data.");
        if (!Shaders.TryGetValue(declaration.SkyObjectType, out var shader))
            Shaders.Add(declaration.SkyObjectType, shader = new Shader { Code = code });
        var result = new ShaderMaterial
        {
            Shader = shader,
            ResourceName = Identity,
            RenderPriority = declaration.SkyObjectType switch { 2 => -128, 5 => -127, _ => -126 }
        };
        result.SetMeta("opennv_sky_object_type", declaration.SkyObjectType);
        result.SetMeta("opennv_sky_property_block", declaration.Block.Index);
        result.SetShaderParameter("rgb_multiplier", 1f);
        // Weather supplies each cloud layer's texture. The model's editor
        // texture can be absent from the shipped installation.
        if (declaration.SkyObjectType == 5 && declaration.FileName.Length != 0)
        {
            var path = declaration.FileName.Replace('/', '\\');
            if (!path.StartsWith("textures\\", StringComparison.OrdinalIgnoreCase)) path = "textures\\" + path;
            result.SetShaderParameter(declaration.SkyObjectType == 3 ? "cloud_map" : "star_map", NativeOwnedMediaLoader.LoadTexture(path));
            result.SetMeta("opennv_sky_texture", path);
        }
        return result;
    }
}
