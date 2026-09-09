using System.Security.Cryptography;
using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Campaigns.Classic.Native;

internal sealed record ClassicAuthoredMaterialRecipe(float PaintStrength, float PaintFacingMinimum, float PaintFacingFull,
    float DepthTolerancePixels, float GrimeLargeMeters, float GrimeSmallMeters, float DetailMeters,
    float WoodAcrossMeters, float WoodAlongMeters, float ClothWeaveMeters, float DetailFadeMeters,
    float GrimeMinimum, float GrimeMaximum, float DetailMinimum, float DetailMaximum,
    float RoughnessVariation, float Specular);

internal sealed class ClassicAuthoredMaterials(ClassicAuthoredMaterialRecipe recipe)
{
    private readonly record struct ProjectionKey(string Model, Basis Orientation, float Scale, float PixelsPerMeter, int Width, int Height);
    private readonly Dictionary<string, ImageTexture> _paint = [];
    private readonly Dictionary<ProjectionKey, ImageTexture> _depth = [];
    private readonly Dictionary<(string Art, ProjectionKey Projection, string Material), ShaderMaterial> _materials = [];
    private readonly Shader _shader = GD.Load<Shader>("res://assets/classic/materials/source-surface.gdshader");

    internal void Apply(Node3D instance, Transform3D orientation, ClassicAuthoredSceneryBinding binding,
        ImageTexture sourceTexture, Fallout1NativeFrmFrame frame, float pixelsPerMeter)
    {
        using var image = sourceTexture.GetImage();
        var colorKey = $"{frame.Width}x{frame.Height}:" + Convert.ToHexString(SHA256.HashData(image.GetData()));
        var key = new ProjectionKey(binding.Model, orientation.Basis, instance.Scale.X, pixelsPerMeter, frame.Width, frame.Height);
        _depth.TryGetValue(key, out var depth);
        var projection = new ClassicSceneryPaintProjection(instance, orientation, pixelsPerMeter, frame.Width, frame.Height,
            recipe.DepthTolerancePixels, depth);
        if (depth is null) _depth.Add(key, projection.Depth);
        if (!_paint.TryGetValue(colorKey, out var paint)) _paint.Add(colorKey, paint = PaddedTexture(image));
        foreach (var part in projection.Parts)
        {
            for (var surface = 0; surface < part.Mesh.Mesh.GetSurfaceCount(); surface++)
            {
                var source = part.Mesh.GetActiveMaterial(surface) as BaseMaterial3D;
                var name = source?.ResourceName ?? "";
                var materialKey = (colorKey, key, name);
                if (!_materials.TryGetValue(materialKey, out var material))
                {
                    material = new ShaderMaterial { Shader = _shader, ResourceName = "Classic source surface: " + name };
                    material.SetShaderParameter("source_art", paint);
                    material.SetShaderParameter("source_depth", projection.Depth);
                    material.SetShaderParameter("base_color", SourceColor(image, name, source?.AlbedoColor ?? new Color("514b41")));
                    material.SetShaderParameter("substance", Substance(name));
                    material.SetShaderParameter("roughness_value", source?.Roughness ?? 0.9f);
                    material.SetShaderParameter("metallic_value", source?.Metallic ?? 0);
                    material.SetShaderParameter("paint_strength", recipe.PaintStrength);
                    material.SetShaderParameter("paint_facing", new Vector2(recipe.PaintFacingMinimum, recipe.PaintFacingFull));
                    material.SetShaderParameter("paint_depth_tolerance", projection.DepthTolerance);
                    material.SetShaderParameter("grime_meters", new Vector2(recipe.GrimeLargeMeters, recipe.GrimeSmallMeters));
                    material.SetShaderParameter("detail_meters", recipe.DetailMeters);
                    material.SetShaderParameter("wood_grain_meters", new Vector3(recipe.WoodAcrossMeters, recipe.WoodAcrossMeters, recipe.WoodAlongMeters));
                    material.SetShaderParameter("cloth_weave_meters", recipe.ClothWeaveMeters);
                    material.SetShaderParameter("detail_fade_meters", recipe.DetailFadeMeters);
                    material.SetShaderParameter("grime_range", new Vector2(recipe.GrimeMinimum, recipe.GrimeMaximum));
                    material.SetShaderParameter("detail_range", new Vector2(recipe.DetailMinimum, recipe.DetailMaximum));
                    material.SetShaderParameter("roughness_variation", recipe.RoughnessVariation);
                    material.SetShaderParameter("specular_value", recipe.Specular);
                    _materials.Add(materialKey, material);
                }
                part.Mesh.SetSurfaceOverrideMaterial(surface, material);
            }
            projection.Bind(part);
        }
    }

    private static ImageTexture PaddedTexture(Image source)
    {
        var width = source.GetWidth(); var height = source.GetHeight();
        var rgba = source.GetData();
        var filled = new bool[width * height]; var queue = new Queue<int>();
        for (var pixel = 0; pixel < filled.Length; pixel++)
            if (rgba[pixel * 4 + 3] > 0) { filled[pixel] = true; queue.Enqueue(pixel); }
        // Extend RGB beneath transparent pixels before mipmapping, but retain
        // alpha. This removes dark edge fringes without enlarging the artwork.
        while (queue.TryDequeue(out var pixel))
        {
            var x = pixel % width; var y = pixel / width;
            void Fill(int at)
            {
                if (filled[at]) return;
                for (var channel = 0; channel < 3; channel++) rgba[at * 4 + channel] = rgba[pixel * 4 + channel];
                filled[at] = true; queue.Enqueue(at);
            }
            if (x > 0) Fill(pixel - 1);
            if (x + 1 < width) Fill(pixel + 1);
            if (y > 0) Fill(pixel - width);
            if (y + 1 < height) Fill(pixel + width);
        }
        using var padded = Image.CreateFromData(width, height, false, Image.Format.Rgba8, rgba);
        padded.GenerateMipmaps();
        return ImageTexture.CreateFromImage(padded);
    }

    private static Color SourceColor(Image image, string materialName, Color fallback)
    {
        var substance = Substance(materialName);
        var colors = new List<(Color Color, float Light)>();
        for (var y = 0; y < image.GetHeight(); y++)
            for (var x = 0; x < image.GetWidth(); x++)
            {
                var c = image.GetPixel(x, y);
                if (c.A < 0.99f) continue;
                var light = c.R * 0.2126f + c.G * 0.7152f + c.B * 0.0722f;
                var chroma = Math.Max(c.R, Math.Max(c.G, c.B)) - Math.Min(c.R, Math.Min(c.G, c.B));
                var selected = substance is 2 or 3 || materialName.Contains("blanket", StringComparison.OrdinalIgnoreCase)
                    ? c.R > c.G * 1.06f && c.R > c.B * 1.15f && light > 0.06f :
                    chroma < 0.17f && light > (substance == 1 ? 0.16f : 0.07f) && light < (substance == 1 ? 0.80f : 0.5f);
                if (selected) colors.Add((c, light));
            }
        if (colors.Count == 0) return fallback;
        var ordered = colors.OrderBy(row => row.Light).ToArray();
        var start = (int)(ordered.Length * (substance == 1 ? 0.55f : 0.35f));
        var selectedColors = ordered.Skip(start).Take(Math.Max(1, ordered.Length / 5)).Select(row => row.Color).ToArray();
        return new(selectedColors.Average(c => c.R), selectedColors.Average(c => c.G), selectedColors.Average(c => c.B));
    }

    private static int Substance(string name) => name.Contains("wood", StringComparison.OrdinalIgnoreCase) ? 2 :
        name.Contains("leather", StringComparison.OrdinalIgnoreCase) ? 3 :
        new[] { "canvas", "linen", "fabric", "blanket" }.Any(part => name.Contains(part, StringComparison.OrdinalIgnoreCase)) ? 1 : 0;

    internal void Clear() { _materials.Clear(); _paint.Clear(); _depth.Clear(); }
}
