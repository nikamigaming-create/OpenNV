using Godot;


using OpenNV.Runtime.World.Actors;

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
        var contracts = surfaces
            .Where(surface => surface.Mesh.Visible)
            .Select(surface =>
            {
                var materials = SurfaceMaterials(surface).ToArray();
                if (materials.Length != 1)
                    throw new InvalidOperationException(
                        $"Visible actor surface {surface.Role}/{surface.Shape} has " +
                        $"{materials.Length} runtime materials; expected exactly one.");
                var material = materials[0];
                return new SurfaceContract(
                    surface.RuntimeNodeName,
                    surface.Role,
                    surface.SourceShaderSkin,
                    IsFaceGenAuthority(material),
                    IsSkinTransfer(material),
                    material);
            })
            .ToArray();
        var sourceSkin = contracts.Where(row => row.SourceShaderSkin).ToArray();
        if (sourceSkin.Length == 0)
            return 0;

        ValidateCoverage(contracts.Select(row => row.Coverage).ToArray());

        var head = sourceSkin.Single(row => row.FaceGenAuthority).Material;
        var target = ActorComplexionMath.AverageFaceGenEncodedSkinColor(head);
        var neck = ActorComplexionMath.AverageFaceGenEncodedNeckColor(head);
        head.SetShaderParameter("use_neck_complexion_target", true);
        head.SetShaderParameter("neck_complexion_target", target);
        head.SetShaderParameter("neck_complexion_source_mean", ActorComplexionMath.Mean(neck));

        var joinedRoles = new HashSet<string>(StringComparer.Ordinal);
        foreach (var contract in sourceSkin.Where(row => !row.FaceGenAuthority))
        {
            var material = contract.Material;
            var source = ActorComplexionMath.AverageEncodedSkinColor(
                material,
                centralTorso: contract.Role == "body");
            material.SetShaderParameter("skin_complexion_multiplier", Vector3.One);
            material.SetShaderParameter("use_skin_complexion_target", true);
            material.SetShaderParameter("skin_complexion_target", target);
            material.SetShaderParameter(
                "skin_complexion_source_mean",
                ActorComplexionMath.Mean(source));
            joinedRoles.Add(contract.Role);
        }

        actorRoot.SetMeta(
            "skin_join_mode",
            "owned-shaderskin-to-facegen-cheek-and-neck-complexion-v2");
        actorRoot.SetMeta("skin_join_materials", sourceSkin.Length - 1);
        actorRoot.SetMeta("skin_join_surfaces", sourceSkin.Length);
        actorRoot.SetMeta(
            "skin_join_roles",
            string.Join(",", joinedRoles.Order(StringComparer.Ordinal)));
        actorRoot.SetMeta("skin_join_target", target);
        return sourceSkin.Length - 1;
    }

    internal static void ValidateCoverage(IReadOnlyList<SurfaceCoverage> surfaces)
    {
        var visibleSkin = surfaces
            .Where(surface => surface.Visible && surface.SourceShaderSkin)
            .ToArray();
        if (visibleSkin.Length == 0)
            return;
        if (visibleSkin.Count(surface => surface.FaceGenAuthority) != 1)
            throw new InvalidOperationException(
                "Source-declared actor skin requires exactly one live FaceGen head authority.");
        var uncovered = visibleSkin
            .Where(surface => !surface.FaceGenAuthority && !surface.RuntimeTransfer)
            .Select(surface => surface.RuntimeNodeName)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (uncovered.Length > 0)
            throw new InvalidOperationException(
                "Visible source SHADERSKIN surfaces lack the shared complexion contract: " +
                string.Join(", ", uncovered));
        var outfitTransfers = surfaces
            .Where(surface =>
                surface.Visible && !surface.SourceShaderSkin && surface.RuntimeTransfer)
            .Select(surface => surface.RuntimeNodeName)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (outfitTransfers.Length > 0)
            throw new InvalidOperationException(
                "Non-SHADERSKIN actor surfaces entered the complexion transfer: " +
                string.Join(", ", outfitTransfers));
    }

    private static bool IsFaceGenAuthority(ShaderMaterial material) =>
        material.Shader?.Code.Contains(
            "uniform vec3 tone_multiplier;",
            StringComparison.Ordinal) == true;

    private static bool IsSkinTransfer(ShaderMaterial material) =>
        material.Shader?.Code.Contains(
            "uniform bool use_skin_transfer;",
            StringComparison.Ordinal) == true &&
        material.GetShaderParameter("use_skin_transfer").AsBool();

    private static IEnumerable<ShaderMaterial> SurfaceMaterials(
        ActorModelSlice.LoadedSurface surface) => Enumerable.Range(
            0,
            surface.Mesh.Mesh?.GetSurfaceCount() ?? 0)
        .Select(surface.Mesh.GetSurfaceOverrideMaterial)
        .OfType<ShaderMaterial>();

    private readonly record struct SurfaceContract(
        string RuntimeNodeName,
        string Role,
        bool SourceShaderSkin,
        bool FaceGenAuthority,
        bool RuntimeTransfer,
        ShaderMaterial Material)
    {
        internal SurfaceCoverage Coverage => new(
            RuntimeNodeName,
            SourceShaderSkin,
            Visible: true,
            FaceGenAuthority,
            RuntimeTransfer);
    }

    internal readonly record struct SurfaceCoverage(
        string RuntimeNodeName,
        bool SourceShaderSkin,
        bool Visible,
        bool FaceGenAuthority,
        bool RuntimeTransfer);

}
