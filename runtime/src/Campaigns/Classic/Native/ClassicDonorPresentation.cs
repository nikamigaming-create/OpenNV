using Godot;
using System.Text.RegularExpressions;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.Campaigns.Classic.Native;

/// <summary>Classic MAP/PRO state owns collision and movement, including held inventory.</summary>
internal static class ClassicDonorPresentation
{
    internal static void BindEnvironment(Node3D root, Vector3 ambient)
    {
        // BuildState owns these material objects and its controller channels
        // retain their identity. Replace only their shared compiled shader;
        // material animation continues to address the same live uniforms.
        var converted = new HashSet<ShaderMaterial>();
        var meshes = root.FindChildren("*", "", true, false).OfType<MeshInstance3D>();
        if (root is MeshInstance3D own) meshes = meshes.Prepend(own);
        foreach (var mesh in meshes)
        {
            for (var surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
            {
                if (mesh.GetActiveMaterial(surface) is not ShaderMaterial material || !converted.Add(material) ||
                    !material.Shader.Code.Contains("instance uniform vec3 source_ambient", StringComparison.Ordinal)) continue;
                var code = Regex.Replace(material.Shader.Code,
                    @"instance uniform (\w+) ((?:source_\w+)|opennv_detail_kind)\s*: instance_index\(\d+\)(\s*=\s*[^;]+)?;",
                    match => "uniform " + match.Groups[1].Value + " " + match.Groups[2].Value + match.Groups[3].Value + ";",
                    RegexOptions.CultureInvariant);
                material.Shader = new Shader { Code = code };
                // Classic world lighting/fog uses Godot. These reference-only
                // inputs retain their original zero/false shader defaults.
                material.SetShaderParameter("source_ambient", ambient);
                material.SetShaderParameter("source_exterior_environment", false);
            }
        }
    }

    internal static bool CanShareStatic(Node3D root) =>
        (root.GetType() == typeof(Node3D) || root.GetType() == typeof(MeshInstance3D) && ((MeshInstance3D)root).Skin is null) &&
        root.GetChildren().All(child => child is Node3D spatial && CanShareStatic(spatial));

    internal static Node3D CloneStatic(Node3D source)
    {
        if (!CanShareStatic(source)) throw new InvalidOperationException("Static appearance has a live node owner.");
        Node3D result;
        if (source is MeshInstance3D mesh)
        {
            var copy = new MeshInstance3D
            {
                Mesh = mesh.Mesh,
                Layers = mesh.Layers,
                CastShadow = mesh.CastShadow,
                GIMode = mesh.GIMode,
                LodBias = mesh.LodBias,
                ExtraCullMargin = mesh.ExtraCullMargin,
                CustomAabb = mesh.CustomAabb,
                IgnoreOcclusionCulling = mesh.IgnoreOcclusionCulling,
                Transparency = mesh.Transparency,
                MaterialOverride = mesh.MaterialOverride,
                MaterialOverlay = mesh.MaterialOverlay
            };
            for (var index = 0; index < mesh.Mesh.GetSurfaceCount(); index++) copy.SetSurfaceOverrideMaterial(index, mesh.GetSurfaceOverrideMaterial(index));
            result = copy;
        }
        else result = new Node3D();
        if (source.Name.ToString().Length != 0) result.Name = source.Name;
        result.Transform = source.Transform; result.Visible = source.Visible;
        foreach (var key in source.GetMetaList()) result.SetMeta(key, source.GetMeta(key));
        foreach (var child in source.GetChildren().OfType<Node3D>()) result.AddChild(CloneStatic(child));
        return result;
    }

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
