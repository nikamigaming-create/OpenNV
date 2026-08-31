using Godot;

namespace OpenNV.Runtime.Presentation.Actors;

/// <summary>
/// Joins every source-declared SHADERSKIN surface to the actor's owned
/// FaceGen cheek complexion. The head remains authoritative; clothing is
/// never admitted into the transfer.
/// </summary>
internal static class ActorComplexionJoin
{
    internal static int Apply(
        Node3D actorRoot,
        IReadOnlyList<ActorModelSlice.LoadedSurface> surfaces)
    {
        var skinSurfaces = surfaces
            .SelectMany(surface => SurfaceMaterials(surface)
                .Where(material =>
                    material.Shader?.Code.Contains(
                        "uniform bool use_skin_transfer;",
                        StringComparison.Ordinal) == true &&
                    material.GetShaderParameter("use_skin_transfer").AsBool())
                .Select(material => (Surface: surface, Material: material)))
            .ToArray();
        if (skinSurfaces.Length == 0)
            return 0;

        var headMaterials = surfaces
            .Where(surface => surface.Role == "head")
            .SelectMany(SurfaceMaterials)
            .Where(material => material.Shader?.Code.Contains(
                "uniform vec3 tone_multiplier;",
                StringComparison.Ordinal) == true)
            .ToArray();
        if (headMaterials.Length != 1)
            throw new InvalidOperationException(
                "Source-declared actor skin requires exactly one live FaceGen head authority.");

        var head = headMaterials[0];
        var target = ActorComplexionMath.AverageFaceGenEncodedSkinColor(head);
        var neck = ActorComplexionMath.AverageFaceGenEncodedNeckColor(head);
        head.SetShaderParameter("use_neck_complexion_target", true);
        head.SetShaderParameter("neck_complexion_target", target);
        head.SetShaderParameter("neck_complexion_source_mean", ActorComplexionMath.Mean(neck));

        var joinedRoles = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (surface, material) in skinSurfaces)
        {
            var source = ActorComplexionMath.AverageEncodedSkinColor(
                material,
                centralTorso: surface.Role == "body");
            material.SetShaderParameter("skin_complexion_multiplier", Vector3.One);
            material.SetShaderParameter("use_skin_complexion_target", true);
            material.SetShaderParameter("skin_complexion_target", target);
            material.SetShaderParameter(
                "skin_complexion_source_mean",
                ActorComplexionMath.Mean(source));
            joinedRoles.Add(surface.Role);
        }

        var visibleHandRoles = surfaces
            .Where(surface => surface.Role is "left-hand" or "right-hand")
            .Select(surface => surface.Role)
            .ToHashSet(StringComparer.Ordinal);
        if (visibleHandRoles.Count > 0 && !visibleHandRoles.IsSubsetOf(joinedRoles))
            throw new InvalidOperationException(
                "Actor FaceGen complexion did not reach every visible source hand.");

        actorRoot.SetMeta(
            "skin_join_mode",
            "owned-shaderskin-to-facegen-cheek-and-neck-complexion-v1");
        actorRoot.SetMeta("skin_join_materials", skinSurfaces.Length);
        actorRoot.SetMeta(
            "skin_join_roles",
            string.Join(",", joinedRoles.Order(StringComparer.Ordinal)));
        actorRoot.SetMeta("skin_join_target", target);
        return skinSurfaces.Length;
    }

    private static IEnumerable<ShaderMaterial> SurfaceMaterials(
        ActorModelSlice.LoadedSurface surface) => Enumerable.Range(
            0,
            surface.Mesh.Mesh?.GetSurfaceCount() ?? 0)
        .Select(surface.Mesh.GetSurfaceOverrideMaterial)
        .OfType<ShaderMaterial>();

}
