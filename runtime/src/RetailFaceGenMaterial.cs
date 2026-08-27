using System.Text;
using System.Text.Json;
using Godot;

namespace OpenNV.Runtime;

internal static class RetailFaceGenMaterial
{
    private static readonly string ShaderSource = BuildShaderSource();

    private static string BuildShaderSource()
    {
        var source = new StringBuilder("""
        shader_type spatial;
        render_mode cull_back, ambient_light_disabled, specular_disabled;

        uniform sampler2D base_map : filter_linear_mipmap_anisotropic, repeat_enable;
        uniform sampler2D normal_map : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
        uniform sampler2D facegen_map0 : filter_linear_mipmap_anisotropic, repeat_enable;
        uniform float signed_detail_neutral;
        uniform float signed_detail_scale;
        uniform vec3 tone_multiplier;
        uniform float roughness;
        uniform float metallic;
        uniform vec3 retail_ambient_color;
        uniform vec3 retail_fog_color;
        uniform float retail_fog_near_game_units;
        uniform float retail_fog_far_game_units;
        uniform float retail_fog_power;
        uniform float retail_game_units_per_meter;

        varying float retail_fog_factor;

        void vertex() {
            vec4 retail_view = MODELVIEW_MATRIX * vec4(VERTEX, 1.0);
            float retail_distance = length(retail_view.xyz) * retail_game_units_per_meter;
            float retail_fog_range = retail_fog_far_game_units - retail_fog_near_game_units;
            float retail_fog_base = clamp(
                (retail_distance - retail_fog_near_game_units) / retail_fog_range,
                0.0,
                1.0);
            retail_fog_factor = pow(retail_fog_base, retail_fog_power);
        }

        void fragment() {
            vec4 base = texture(base_map, UV);
            vec3 detail = texture(facegen_map0, UV).rgb;
            vec3 encoded_albedo = (
                base.rgb + signed_detail_scale *
                (detail - vec3(signed_detail_neutral))) * tone_multiplier;
            ALBEDO = encoded_albedo;
            EMISSION = encoded_albedo * retail_ambient_color;
            NORMAL_MAP = texture(normal_map, UV).rgb;
            ROUGHNESS = roughness;
            METALLIC = metallic;
            FOG = vec4(retail_fog_color, retail_fog_factor);
        }

        """);
        RetailLighting.AppendDiffuseLightFunction(source);
        return source.ToString();
    }

    internal static bool ApplyIfDeclared(
        MeshInstance3D mesh,
        JsonElement surface,
        JsonElement textureRows,
        string sidecarPath,
        FaceGenMaterialConfiguration configuration)
    {
        var materialContract = surface.GetProperty("material");
        if (!materialContract.TryGetProperty("faceGen", out var faceGen) ||
            faceGen.ValueKind == JsonValueKind.Null)
            return false;
        if (faceGen.ValueKind != JsonValueKind.Object ||
            faceGen.GetProperty("schema").GetString() != configuration.Schema)
            throw new InvalidOperationException(
                $"Actor surface has an invalid FaceGen material contract: {sidecarPath}");
        if (mesh.Mesh is null || mesh.Mesh.GetSurfaceCount() != 1)
            throw new InvalidOperationException(
                $"Actor FaceGen runtime node must contain exactly one surface: {mesh.Name}");
        if (materialContract.GetProperty("alphaMode").GetString() != "OPAQUE")
            throw new InvalidOperationException(
                $"Actor FaceGen material requires an unsupported alpha mode: {sidecarPath}");

        var rows = textureRows.EnumerateArray().ToArray();
        var baseMap = LoadTexture(
            rows,
            faceGen.GetProperty("baseTextureIndex").GetInt32(),
            false,
            sidecarPath);
        var normalMap = LoadTexture(
            rows,
            faceGen.GetProperty("normalTextureIndex").GetInt32(),
            true,
            sidecarPath);
        var detailMap = LoadTexture(
            rows,
            faceGen.GetProperty("detailTextureIndex").GetInt32(),
            false,
            sidecarPath);
        var tone = configuration.ToneMapRgba;
        var toneMultiplier = new Vector3(tone[0], tone[1], tone[2]) *
            (configuration.ToneScale / byte.MaxValue);
        var shaderMaterial = new ShaderMaterial
        {
            ResourceName = RuntimeMaterialLoader.RetailActorMaterialResourceName,
            Shader = new Shader { Code = ShaderSource },
        };
        shaderMaterial.SetShaderParameter("base_map", baseMap);
        shaderMaterial.SetShaderParameter("normal_map", normalMap);
        shaderMaterial.SetShaderParameter("facegen_map0", detailMap);
        shaderMaterial.SetShaderParameter(
            "signed_detail_neutral",
            configuration.SignedDetailNeutral);
        shaderMaterial.SetShaderParameter(
            "signed_detail_scale",
            configuration.SignedDetailScale);
        shaderMaterial.SetShaderParameter("tone_multiplier", toneMultiplier);
        shaderMaterial.SetShaderParameter("retail_ambient_color", Vector3.Zero);
        shaderMaterial.SetShaderParameter(
            "roughness",
            materialContract.GetProperty("roughness").GetSingle());
        shaderMaterial.SetShaderParameter(
            "metallic",
            materialContract.GetProperty("metallic").GetSingle());
        mesh.SetSurfaceOverrideMaterial(0, shaderMaterial);
        return true;
    }

    private static Texture2D LoadTexture(
        IReadOnlyList<JsonElement> rows,
        int index,
        bool expectedNormal,
        string sidecarPath)
    {
        if (index < 0 || index >= rows.Count)
            throw new InvalidOperationException(
                $"Actor FaceGen texture index is outside the sidecar: {index}");
        var row = rows[index];
        if (row.GetProperty("normalGreenInverted").GetBoolean() != expectedNormal)
            throw new InvalidOperationException(
                $"Actor FaceGen texture semantic disagrees with its decoded pixels: {index}");
        var root = Path.GetDirectoryName(sidecarPath)
            ?? throw new InvalidOperationException($"Actor sidecar has no directory: {sidecarPath}");
        var path = Path.GetFullPath(Path.Combine(root, row.GetProperty("png").GetString()!));
        var relative = Path.GetRelativePath(root, path);
        if (Path.IsPathRooted(relative) ||
            relative.Equals("..", StringComparison.Ordinal) ||
            relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Actor FaceGen texture escapes its immutable artifact root: {path}");
        VerifiedGltfLoader.VerifyHash(path, row.GetProperty("pngSha256").GetString()!);
        var image = Image.LoadFromFile(path);
        if (image is null || image.IsEmpty() ||
            image.GetWidth() != row.GetProperty("width").GetInt32() ||
            image.GetHeight() != row.GetProperty("height").GetInt32())
            throw new InvalidOperationException(
                $"Godot could not load the declared Actor FaceGen texture: {path}");
        if (!image.HasMipmaps() && image.GetWidth() > 1 && image.GetHeight() > 1)
        {
            var result = image.GenerateMipmaps(expectedNormal);
            if (result != Error.Ok || !image.HasMipmaps())
                throw new InvalidOperationException(
                    $"Godot could not generate Actor FaceGen texture mips: {path} ({result})");
        }
        return ImageTexture.CreateFromImage(image);
    }
}
