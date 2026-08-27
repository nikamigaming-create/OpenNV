using System.Text;
using System.Text.Json;
using Godot;

namespace OpenNV.Runtime;

/// <summary>
/// Replaces glTF's generic PBR actor materials with the encoded-domain
/// ambient-plus-directional diffuse core used by the retail SLS family.
/// </summary>
internal static class RetailActorMaterial
{
    private const int BlendOne = 0;
    private const int BlendZero = 1;
    private const int BlendSourceColor = 2;
    private const int BlendSourceAlpha = 6;
    private const int BlendOneMinusSourceAlpha = 7;

    internal static void Apply(
        MeshInstance3D mesh,
        JsonElement surface,
        JsonElement textureRows,
        string sidecarPath,
        FaceGenMaterialConfiguration faceGenConfiguration)
    {
        if (RetailFaceGenMaterial.ApplyIfDeclared(
                mesh,
                surface,
                textureRows,
                sidecarPath,
                faceGenConfiguration))
            return;

        if (mesh.Mesh is null || mesh.Mesh.GetSurfaceCount() != 1)
            throw new InvalidOperationException(
                $"Actor runtime node must contain exactly one surface: {mesh.Name}");
        if (mesh.GetActiveMaterial(0) is not StandardMaterial3D imported)
            throw new InvalidOperationException(
                $"Actor surface did not import as a standard glTF material: {mesh.Name}");

        var materialContract = surface.GetProperty("material");
        var alpha = materialContract.GetProperty("alphaContract");
        var unshaded = materialContract.GetProperty("unshaded").GetBoolean();
        var alphaMode = alpha.GetProperty("mode").GetString() ?? "";
        if (alphaMode is not ("OPAQUE" or "MASK" or "BLEND"))
            throw new InvalidOperationException(
                $"Actor surface has an unsupported alpha mode: {alphaMode}");
        var blendRenderMode = alphaMode == "BLEND"
            ? BlendRenderMode(
                alpha.GetProperty("sourceBlendMode").GetInt32(),
                alpha.GetProperty("destinationBlendMode").GetInt32())
            : null;

        var baseColorFactor = imported.AlbedoColor;
        if (!float.IsFinite(baseColorFactor.R) ||
            !float.IsFinite(baseColorFactor.G) ||
            !float.IsFinite(baseColorFactor.B) ||
            !float.IsFinite(baseColorFactor.A))
            throw new InvalidOperationException(
                $"Actor surface has an invalid imported base-color factor: {mesh.Name}");
        var material = new ShaderMaterial
        {
            ResourceName = unshaded
                ? RuntimeMaterialLoader.RetailActorUnshadedMaterialResourceName
                : RuntimeMaterialLoader.RetailActorMaterialResourceName,
            Shader = new Shader
            {
                Code = BuildShader(alphaMode, blendRenderMode, unshaded),
            },
        };
        if (imported.AlbedoTexture is not null)
            material.SetShaderParameter("base_map", imported.AlbedoTexture);
        if (imported.NormalTexture is not null)
            material.SetShaderParameter("normal_map", imported.NormalTexture);
        material.SetShaderParameter("use_base_map", imported.AlbedoTexture is not null);
        material.SetShaderParameter("use_normal_map", imported.NormalTexture is not null);
        material.SetShaderParameter("base_color_factor", baseColorFactor);
        if (!unshaded)
            material.SetShaderParameter("retail_ambient_color", Vector3.Zero);
        material.SetShaderParameter(
            "alpha_cutoff",
            alphaMode == "MASK" && alpha.GetProperty("cutoff").ValueKind == JsonValueKind.Number
                ? alpha.GetProperty("cutoff").GetSingle()
                : 0.0f);
        mesh.SetSurfaceOverrideMaterial(0, material);
    }

    private static string BlendRenderMode(int source, int destination)
    {
        // NiAlphaProperty uses the classic Gamebryo/OpenGL blend enum. These
        // names are an immutable external-format contract. Unsupported
        // authored combinations fail closed.
        return (source, destination) switch
        {
            (BlendOne, BlendOne) => "blend_add",
            (BlendZero, BlendSourceColor) => "blend_mul",
            (BlendSourceAlpha, BlendOneMinusSourceAlpha) => "blend_mix",
            _ => throw new InvalidOperationException(
                $"Actor surface has an unsupported blend function: {source}/{destination}"),
        };
    }

    private static string BuildShader(
        string alphaMode,
        string? blendRenderMode,
        bool unshaded)
    {
        var modes = new List<string>
        {
            // Fallout's BSShaderNoLightingProperty actor pass is used by
            // authored emissive display surfaces.  The owned securitron screen
            // geometry is visible in the hash-bound retail final-eye draw even
            // though its converted front winding faces away from Godot's
            // default opaque cull direction, so preserve that shader family's
            // two-sided rasterization semantics instead of keying a model.
            alphaMode == "OPAQUE" && !unshaded ? "cull_back" : "cull_disabled",
            "specular_disabled",
        };
        modes.Add(unshaded ? "unshaded" : "ambient_light_disabled");
        if (alphaMode == "BLEND")
        {
            modes.Add(blendRenderMode!);
            // Ordinary hair/glass alpha uses a depth prepass. Additive screen
            // glare and multiplicative creature glass must remain overlay passes;
            // a prepass would hide the authored face/brain beneath them.
            if (blendRenderMode == "blend_mix")
                modes.Add("depth_prepass_alpha");
        }

        var source = new StringBuilder();
        source.AppendLine("shader_type spatial;");
        source.AppendLine($"render_mode {string.Join(", ", modes)};");
        source.AppendLine(
            "uniform sampler2D base_map : filter_linear_mipmap_anisotropic, repeat_enable;");
        source.AppendLine(
            "uniform sampler2D normal_map : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;");
        source.AppendLine("uniform bool use_base_map;");
        source.AppendLine("uniform bool use_normal_map;");
        source.AppendLine("uniform vec4 base_color_factor;");
        if (!unshaded)
            AppendRetailLightingUniforms(source);
        source.AppendLine("uniform float alpha_cutoff;");
        source.AppendLine("void fragment() {");
        source.AppendLine("    vec4 base = use_base_map ? texture(base_map, UV) : vec4(1.0);");
        source.AppendLine("    base *= base_color_factor;");
        source.AppendLine("    if (use_normal_map) {");
        source.AppendLine(
            "        vec3 tangent_normal = normalize(texture(normal_map, UV).rgb * 2.0 - 1.0);");
        source.AppendLine("        NORMAL = normalize(");
        source.AppendLine("            TANGENT * tangent_normal.x +");
        source.AppendLine("            BINORMAL * tangent_normal.y +");
        source.AppendLine("            NORMAL * tangent_normal.z);");
        source.AppendLine("    }");
        source.AppendLine("    ALBEDO = base.rgb;");
        if (!unshaded)
        {
            source.AppendLine("    EMISSION = base.rgb * retail_ambient_color;");
            source.AppendLine("    FOG = vec4(retail_fog_color, retail_fog_factor);");
        }
        if (alphaMode == "MASK")
        {
            source.AppendLine("    ALPHA = base.a;");
            source.AppendLine("    ALPHA_SCISSOR_THRESHOLD = alpha_cutoff;");
        }
        else if (alphaMode == "BLEND")
            source.AppendLine("    ALPHA = base.a;");
        source.AppendLine("}");
        if (!unshaded)
            AppendRetailLightFunction(source);
        return source.ToString();
    }

    internal static void AppendRetailLightingUniforms(StringBuilder source)
    {
        source.AppendLine("uniform vec3 retail_ambient_color;");
        source.AppendLine("uniform vec3 retail_fog_color;");
        source.AppendLine("uniform float retail_fog_near_game_units;");
        source.AppendLine("uniform float retail_fog_far_game_units;");
        source.AppendLine("uniform float retail_fog_power;");
        source.AppendLine("uniform float retail_game_units_per_meter;");
        source.AppendLine("varying float retail_fog_factor;");
        source.AppendLine("void vertex() {");
        source.AppendLine(
            "    vec4 retail_view = MODELVIEW_MATRIX * vec4(VERTEX, 1.0);");
        source.AppendLine(
            "    float retail_distance = length(retail_view.xyz) * retail_game_units_per_meter;");
        source.AppendLine(
            "    float retail_fog_range = retail_fog_far_game_units - retail_fog_near_game_units;");
        source.AppendLine(
            "    float retail_fog_base = clamp((retail_distance - retail_fog_near_game_units) / retail_fog_range, 0.0, 1.0);");
        source.AppendLine(
            "    retail_fog_factor = pow(retail_fog_base, retail_fog_power);");
        source.AppendLine("}");
    }

    internal static void AppendRetailLightFunction(StringBuilder source)
    {
        RetailLighting.AppendDiffuseLightFunction(source);
    }
}
