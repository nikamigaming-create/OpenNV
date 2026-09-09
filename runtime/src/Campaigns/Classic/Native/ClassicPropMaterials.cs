using System.Text.RegularExpressions;
using Godot;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.Campaigns.Classic.Native;

internal sealed record ClassicPropPalette(string TexturePattern, string Color, bool BlueClothOnly = false, float Brightness = 1);

/// <summary>Source-art palette on selected donor surfaces, retaining their UVs, wear, alpha and normals.</summary>
internal static class ClassicPropMaterials
{
    internal static Material Surface(Material source, FalloutNifFile file, FalloutNifGeometry geometry, ClassicPropPalette[]? palette)
    {
        if (palette is null) return source;
        var paths = geometry.Properties.Where(index => index >= 0).Select(file.ReadObject).OfType<FalloutNifShaderProperty>()
            .Where(shader => shader.TextureSet >= 0).Select(shader => file.ReadObject(shader.TextureSet))
            .OfType<FalloutNifShaderTextureSet>().Select(set => set.Textures[0].Replace('\\', '/')).ToArray();
        var rule = palette.SingleOrDefault(row => paths.Any(path => Regex.IsMatch(path, row.TexturePattern,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)));
        return rule is null ? source : Recolor(source, rule);
    }

    private static ShaderMaterial Recolor(Material source, ClassicPropPalette rule)
    {
        if (source is not ShaderMaterial shader || source.ResourceName != NativeNifLightingMaterial.ResourceIdentity ||
            shader.GetShaderParameter("base_map").AsGodotObject() is not Texture2D texture)
            throw new InvalidDataException("Classic palette requires its owned diffuse surface.");
        using var image = texture.GetImage();
        if (image.IsCompressed() && image.Decompress() != Error.Ok) throw new InvalidDataException("Classic diffuse texture cannot be decoded.");
        image.Convert(Image.Format.Rgba8);
        var color = new Color(rule.Color);
        var luminance = color.R * 0.2126f + color.G * 0.7152f + color.B * 0.0722f;
        if (luminance <= 0 || !float.IsFinite(rule.Brightness) || rule.Brightness <= 0) throw new InvalidDataException("Classic palette has invalid colour or brightness.");
        var bytes = image.GetData();
        for (var pixel = 0; pixel < image.GetWidth() * image.GetHeight(); pixel++)
        {
            var offset = pixel * 4;
            // Restrict a vault-cloth dye to the donor's blue fabric. Gold trim,
            // skin, boots, buckles, wear and the original UV map stay distinct.
            if (rule.BlueClothOnly && (bytes[offset + 2] <= bytes[offset] * 1.08f || bytes[offset + 1] <= bytes[offset] * 1.04f)) continue;
            var light = (bytes[offset] * 0.2126f + bytes[offset + 1] * 0.7152f + bytes[offset + 2] * 0.0722f) / luminance * rule.Brightness;
            bytes[offset] = (byte)Math.Clamp(MathF.Round(light * color.R), 0, 255);
            bytes[offset + 1] = (byte)Math.Clamp(MathF.Round(light * color.G), 0, 255);
            bytes[offset + 2] = (byte)Math.Clamp(MathF.Round(light * color.B), 0, 255);
        }
        using var recolored = Image.CreateFromData(image.GetWidth(), image.GetHeight(), image.HasMipmaps(), Image.Format.Rgba8, bytes);
        recolored.GenerateMipmaps();
        var replacement = (ShaderMaterial)shader.Duplicate();
        replacement.SetShaderParameter("base_map", ImageTexture.CreateFromImage(recolored));
        replacement.SetMeta("classic_source_palette", rule.Color); return replacement;
    }

    internal static void Apply(Node3D root, FalloutNifFile file, ClassicPropPalette[]? palette)
    {
        if (palette is null || palette.Length == 0) return;
        var matched = new HashSet<ClassicPropPalette>();
        var replacements = new Dictionary<(Material, ClassicPropPalette), ShaderMaterial>();
        foreach (var mesh in root.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>())
        {
            if (!mesh.HasMeta("opennv_nif_geometry_block") ||
                file.ReadObject(mesh.GetMeta("opennv_nif_geometry_block").AsInt32()) is not FalloutNifGeometry geometry) continue;
            var paths = geometry.Properties.Where(index => index >= 0).Select(file.ReadObject).OfType<FalloutNifShaderProperty>()
                .Where(shader => shader.TextureSet >= 0).Select(shader => file.ReadObject(shader.TextureSet))
                .OfType<FalloutNifShaderTextureSet>().Select(set => set.Textures[0].Replace('\\', '/')).ToArray();
            var rule = palette.SingleOrDefault(row => paths.Any(path => Regex.IsMatch(path, row.TexturePattern,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)));
            if (rule is null) continue;
            matched.Add(rule);
            for (var surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
            {
                var source = mesh.GetActiveMaterial(surface);
                if (!replacements.TryGetValue((source, rule), out var replacement))
                {
                    replacement = Recolor(source, rule);
                    replacements.Add((source, rule), replacement);
                }
                mesh.SetSurfaceOverrideMaterial(surface, replacement);
            }
        }
        if (matched.Count != palette.Length) throw new InvalidDataException("Classic prop palette did not bind every declared source texture.");
    }
}
