using Godot;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.Campaigns.Classic.Native;

/// <summary>Classic MAP/PRO state owns collision and movement, including held inventory.</summary>
internal static class ClassicDonorPresentation
{
    internal static void Prepare(Node3D root)
    {
        // A dropped donor item's Havok body otherwise publishes a world-space
        // physics pose back into the mesh and pulls it out of its hand socket.
        // Do this before tree entry; the original source metadata remains on
        // the visual owner, while no second simulation runs inside the classic map.
        var shapes = root.FindChildren("*", "", true, false).OfType<CollisionShape3D>()
            .Select(shape => shape.Shape).Where(shape => shape is not null).Distinct().ToArray();
        foreach (var body in root.FindChildren("*", "", true, false).OfType<CollisionObject3D>().ToArray())
            if (GodotObject.IsInstanceValid(body)) body.Free();
        // These shapes were just built for this prototype and have no visual
        // consumers. Release their managed resource references with the bodies.
        foreach (var shape in shapes) shape.Dispose();
        // Several loose weapon NIFs author their assembled visibility/pose at
        // negative time through zero. The direct clock has already published
        // its final frame during construction. Retire only that completed,
        // event-free initialization before cloning the reusable appearance.
        foreach (var controller in root.FindChildren("*", "", true, false).OfType<RuntimeNifControllerPlayer>().ToArray())
            if (controller.CompletedDirectInitialization) controller.Free();
        var opaque = new Dictionary<Material, Material>();
        var animated = root.FindChildren("*", "", true, false).OfType<RuntimeNifControllerPlayer>().Any();
        foreach (var mesh in root.FindChildren("*", "", true, false).OfType<MeshInstance3D>())
        {
            mesh.SetInstanceShaderParameter("source_ambient", new Vector3(0.22f, 0.23f, 0.20f));
            for (var surface = 0; !animated && surface < mesh.Mesh.GetSurfaceCount(); surface++)
            {
                var material = mesh.Mesh.SurfaceGetMaterial(surface);
                if (!opaque.TryGetValue(material, out var replacement))
                    opaque.Add(material, replacement = OpaqueCoverage(material));
                if (replacement != material) mesh.SetSurfaceOverrideMaterial(surface, replacement);
            }
        }
        root.SetMeta("collision_owner", "classic-MAP-PRO-hex-state");
    }

    private static Material OpaqueCoverage(Material material)
    {
        // Some opaque metal/wood weapon meshes declare source-alpha blending.
        // Godot excludes every ALPHA-writing draw from its shadow pass, even
        // when all fragments have alpha 1. Preserve fractional coverage and
        // animation; only the proven constant-opaque case can use an opaque draw.
        if (material is not ShaderMaterial source || source.ResourceName != NativeNifLightingMaterial.ResourceIdentity ||
            !source.HasMeta("opennv_nif_alpha_flags") ||
            FalloutNifAlphaState.Read((ushort)source.GetMeta("opennv_nif_alpha_flags").AsInt32(), 0).Blend != FalloutNifBlendMode.SourceAlpha ||
            source.GetShaderParameter("base_factor").AsVector4().W != 1 || source.GetShaderParameter("use_vertex_alpha").AsBool()) return material;
        if (source.GetShaderParameter("use_base_map").AsBool())
        {
            using var image = ((Texture2D)source.GetShaderParameter("base_map").AsGodotObject()).GetImage();
            if (image.IsCompressed() && image.Decompress() != Error.Ok || image.DetectAlpha() != Image.AlphaMode.None) return material;
        }
        const string alphaOutput = "ALPHA *= base.a;";
        if (!source.Shader.Code.Contains(alphaOutput, StringComparison.Ordinal)) return material;
        var result = (ShaderMaterial)source.Duplicate();
        result.Shader = new Shader { Code = source.Shader.Code.Replace(alphaOutput, "", StringComparison.Ordinal) };
        result.SetMeta("classic_opaque_coverage", "constant-material-alpha;opaque-texture;no-vertex-alpha-or-controller");
        return result;
    }
}
