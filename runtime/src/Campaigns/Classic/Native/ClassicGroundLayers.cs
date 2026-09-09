using Godot;

namespace OpenNV.Runtime.Campaigns.Classic.Native;

internal sealed record ClassicGroundBinding(string ArtPattern, ClassicSurfaceProfile Surface,
    float SourceMipBias = 1.7f, float GrainStrength = 0.65f, float DustStrength = 0.12f);

/// <summary>Original tile colour and boundaries under meter-scaled soil, relief and dust.</summary>
internal static class ClassicGroundLayers
{
    private static readonly Shader Shader = new() { Code = """
        shader_type spatial;
        render_mode cull_back;
        uniform sampler2D source_tiles : source_color, filter_linear_mipmap_anisotropic, repeat_disable;
        uniform sampler2D soil_color : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
        uniform sampler2D soil_normal : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
        uniform float repeat_meters;
        uniform float source_mip_bias;
        uniform float grain_strength;
        uniform float dust_strength;
        uniform float relief_strength;
        varying vec3 ground_position;
        varying vec2 palette_uv;
        void vertex() {
            ground_position = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
            palette_uv = INSTANCE_CUSTOM.xy + UV * INSTANCE_CUSTOM.zw;
        }
        void fragment() {
            vec2 ground_uv = ground_position.xz / repeat_meters;
            vec3 original = texture(source_tiles, palette_uv, source_mip_bias).rgb;
            vec3 soil = texture(soil_color, ground_uv).rgb;
            vec3 grain = texture(soil_color, ground_uv * 4.13 + vec2(0.27, 0.49)).rgb;
            float relief = dot(soil, vec3(0.2126, 0.7152, 0.0722));
            float fine = dot(grain, vec3(0.2126, 0.7152, 0.0722));
            // Colour comes from the exact source tile, while spatial frequency
            // comes from real soil. World coordinates cross tile boundaries.
            vec3 ground = original * mix(1.0, clamp(0.55 + relief * 2.4 + fine * 0.25, 0.6, 1.65), grain_strength);
            float drift = texture(soil_color, ground_uv * 0.19 + vec2(0.39, 0.71)).r;
            float dust = smoothstep(0.10, 0.38, drift) * dust_strength;
            ALBEDO = mix(ground, original * vec3(1.10, 1.05, 0.95), dust);
            vec3 normal = texture(soil_normal, ground_uv).rgb * 2.0 - 1.0;
            normal.xy *= relief_strength * (1.0 - dust);
            NORMAL = normalize((VIEW_MATRIX * vec4(normal.x, normal.z, -normal.y, 0.0)).xyz);
            ROUGHNESS = mix(0.90, 0.99, dust);
            SPECULAR = 0.16;
        }
        """ };

    internal static Material Build(Texture2D original, StandardMaterial3D surface, ClassicGroundBinding binding)
    {
        var material = new ShaderMaterial { Shader = Shader };
        material.SetShaderParameter("source_tiles", original);
        material.SetShaderParameter("soil_color", surface.AlbedoTexture);
        material.SetShaderParameter("soil_normal", surface.NormalTexture);
        material.SetShaderParameter("repeat_meters", binding.Surface.RepeatMeters);
        material.SetShaderParameter("source_mip_bias", binding.SourceMipBias);
        material.SetShaderParameter("grain_strength", binding.GrainStrength);
        material.SetShaderParameter("dust_strength", binding.DustStrength);
        material.SetShaderParameter("relief_strength", binding.Surface.NormalScale);
        return material;
    }

    internal static ImageTexture Palette(ClassicArtCache art, IReadOnlyList<string> names, int[] tiles, int tilePixels)
    {
        var side = (int)Math.Sqrt(tiles.Length);
        if (side * side != tiles.Length) throw new InvalidDataException("Classic floor palette requires the original square tile table.");
        using var atlas = Image.CreateEmpty(side * tilePixels, side * tilePixels, false, Image.Format.Rgba8);
        // Join adjacent source tiles before minification. Blurring each tile
        // separately clamps its edge and exposes a checkerboard in close views.
        foreach (var group in Enumerable.Range(0, tiles.Length).Where(index => tiles[index] != 1).GroupBy(index => tiles[index]))
        {
            if (group.Key >= names.Count) throw new InvalidDataException("Ground palette tile exceeds the original tile list.");
            using var image = art.Floor("art/tiles/" + names[group.Key], 128).GetImage();
            image.Resize(tilePixels, tilePixels, Image.Interpolation.Trilinear);
            foreach (var index in group)
                atlas.BlitRect(image, new Rect2I(0, 0, tilePixels, tilePixels), new Vector2I(index % side * tilePixels, index / side * tilePixels));
        }
        atlas.GenerateMipmaps();
        return ImageTexture.CreateFromImage(atlas);
    }
}
