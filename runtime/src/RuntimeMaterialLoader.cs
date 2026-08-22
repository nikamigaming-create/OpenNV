using System.Text.Json;
using Godot;

namespace OpenNV.Runtime;

internal static class RuntimeMaterialLoader
{
    internal static Dictionary<string, Texture2D> LoadTextures(JsonElement scene)
    {
        var textures = new Dictionary<string, Texture2D>(StringComparer.Ordinal);
        foreach (var texture in scene.GetProperty("textures").EnumerateArray())
        {
            var id = texture.GetProperty("id").GetString()!;
            var path = VerifiedGltfLoader.ResolvePath(texture.GetProperty("png").GetString()!);
            VerifiedGltfLoader.VerifyHash(path, texture.GetProperty("pngSha256").GetString()!);
            var image = Image.LoadFromFile(path);
            if (image is null || image.IsEmpty())
                throw new InvalidOperationException($"Godot could not load prepared texture: {path}");
            if (image.GetWidth() != texture.GetProperty("width").GetInt32() ||
                image.GetHeight() != texture.GetProperty("height").GetInt32())
                throw new InvalidOperationException($"Prepared texture dimensions do not match manifest: {path}");
            textures.Add(id, ImageTexture.CreateFromImage(image));
        }
        return textures;
    }

    internal static int Apply(
        Node3D scene,
        JsonElement asset,
        IReadOnlyDictionary<string, Texture2D> textures)
    {
        var surfaces = Descendants<MeshInstance3D>(scene)
            .SelectMany(mesh => Enumerable.Range(0, mesh.Mesh?.GetSurfaceCount() ?? 0)
                .Select(index => (Mesh: mesh, Surface: index)))
            .ToArray();
        var bindings = asset.GetProperty("materials").EnumerateArray().ToArray();
        if (surfaces.Length != bindings.Length)
            throw new InvalidOperationException(
                $"Material/surface count mismatch for asset {asset.GetProperty("id").GetString()}: " +
                $"surfaces={surfaces.Length} bindings={bindings.Length}");

        for (var index = 0; index < bindings.Length; index++)
        {
            var binding = bindings[index];
            var material = new StandardMaterial3D
            {
                Metallic = 0.0f,
                Roughness = binding.GetProperty("roughness").GetSingle(),
                VertexColorUseAsAlbedo = true,
            };
            material.AlbedoTexture = Texture(binding, "diffuseTextureId", textures);
            var normal = Texture(binding, "normalTextureId", textures);
            if (normal is not null)
            {
                material.NormalEnabled = true;
                material.NormalTexture = normal;
            }
            var emissive = Texture(binding, "emissiveTextureId", textures);
            var emissiveColor = ReadColor(binding.GetProperty("emissiveColor"));
            if (emissive is not null || emissiveColor != Colors.Black)
            {
                material.EmissionEnabled = true;
                material.Emission = emissiveColor == Colors.Black ? Colors.White : emissiveColor;
                material.EmissionTexture = emissive;
                material.EmissionEnergyMultiplier = 1.0f;
            }
            if (binding.GetProperty("alphaBlend").GetBoolean())
                material.Transparency = BaseMaterial3D.TransparencyEnum.AlphaDepthPrePass;
            if (binding.GetProperty("doubleSided").GetBoolean())
                material.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
            if (binding.GetProperty("unshaded").GetBoolean())
                material.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
            surfaces[index].Mesh.SetSurfaceOverrideMaterial(surfaces[index].Surface, material);
        }
        return bindings.Length;
    }

    private static Texture2D? Texture(
        JsonElement binding,
        string property,
        IReadOnlyDictionary<string, Texture2D> textures)
    {
        var value = binding.GetProperty(property);
        return value.ValueKind == JsonValueKind.String ? textures[value.GetString()!] : null;
    }

    private static Color ReadColor(JsonElement values)
    {
        var components = values.EnumerateArray().Select(value => value.GetSingle()).ToArray();
        if (components.Length != 3)
            throw new InvalidOperationException("Material emissive color must contain three values.");
        return new Color(components[0], components[1], components[2]);
    }

    private static IEnumerable<T> Descendants<T>(Node node)
        where T : Node
    {
        foreach (var child in node.GetChildren())
        {
            if (child is T match)
                yield return match;
            foreach (var descendant in Descendants<T>(child))
                yield return descendant;
        }
    }
}
