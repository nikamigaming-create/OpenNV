using System.Text.Json;
using Godot;

namespace OpenNV.Runtime.Campaigns.Fallout3;

internal sealed record Fo3Vault101BirthSceneCoverage(
    Fo3Vault101BirthPresentationContract Contract,
    Node3D CellRoot,
    Camera3D Camera,
    int LoadedAssets,
    int LoadedTextures,
    int AuthoredDdsTextures,
    int AuthoredDdsMipChainTextures,
    int DecodedAuthoredBc1AlphaMipChainTextures,
    int RuntimeGeneratedMipTextures,
    int MaterialBindings,
    int ProofLitRetailMaterials,
    int ProofLitActorMaterials,
    CellActorLoader.PlacedActor DoctorActor,
    CellReferenceLedger.Geometry DoctorActorGeometry,
    int PlacedReferences,
    int MeshInstances,
    int Surfaces,
    int Vertices,
    int Triangles);

internal static class Fo3Vault101BirthScene
{
    internal static Fo3Vault101BirthSceneCoverage Build(
        Node3D host,
        Fo3Vault101BirthPresentationContract contract)
    {
        using var presentationDocument = JsonDocument.Parse(
            File.ReadAllBytes(contract.ManifestPath));
        var presentation = presentationDocument.RootElement;
        var configuration = RuntimeConfiguration.Load();
        configuration.VerifyCompiledConfiguration(presentation);
        var textures = RuntimeMaterialLoader.LoadTextures(
            presentation,
            configuration.Renderer);
        var assetRows = presentation.GetProperty("assets").EnumerateArray()
            .ToDictionary(
                value => value.GetProperty("id").GetString()!,
                StringComparer.Ordinal);
        var prototypes = new Dictionary<string, VerifiedGltfLoader.LoadedGltf>(
            StringComparer.Ordinal);
        var materialBindings = 0;
        try
        {
            foreach (var asset in contract.Assets.Values)
            {
                var loaded = VerifiedGltfLoader.Load(asset.ModelPath, asset.SidecarPath);
                if (!loaded.SourceSha256.Equals(
                        asset.SourceSha256,
                        StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(
                        $"Fallout 3 Vault 101 source hash differs: {asset.Id}");
                var observedSurfaces = Descendants<MeshInstance3D>(loaded.Scene)
                    .Where(mesh => mesh.Mesh is not null)
                    .Sum(mesh => mesh.Mesh!.GetSurfaceCount());
                if (observedSurfaces != asset.Surfaces)
                    throw new InvalidOperationException(
                        $"Fallout 3 Vault 101 surface count differs: {asset.Id}");
                if (!assetRows.TryGetValue(asset.Id, out var assetRow))
                    throw new InvalidOperationException(
                        $"Fallout 3 Vault 101 material row is absent: {asset.Id}");
                materialBindings += RuntimeMaterialLoader.Apply(
                    loaded.Scene,
                    assetRow,
                    textures,
                    configuration.Renderer,
                    configuration.ContentCompiler.RetailGrass);
                prototypes.Add(asset.Id, loaded);
            }

            var root = new Node3D
            {
                Name = $"CELL_{contract.CellFormId}_{contract.CellEditorId}_BIRTH_ROOM",
                Scale = Vector3.One * contract.UnitsToMeters,
            };
            host.AddChild(root);
            var meshInstances = 0;
            var surfaces = 0;
            var vertices = 0;
            var triangles = 0;
            foreach (var reference in contract.References)
            {
                var placement = new Node3D
                {
                    Name = $"REFR_{reference.FormId}_{NodeIdentifier(reference.BaseEditorId)}",
                    Position = reference.PositionGodotGameUnits,
                    Quaternion = reference.RotationGodotQuaternion,
                    Scale = Vector3.One * reference.Scale,
                };
                placement.SetMeta("opennv_source_form_id", reference.FormId);
                placement.SetMeta("opennv_source_base_form_id", reference.BaseFormId);
                placement.SetMeta("opennv_source_record_type", reference.BaseRecordType);
                root.AddChild(placement);
                var visual = prototypes[reference.AssetId].Scene.Duplicate(
                        (int)Node.DuplicateFlags.Default) as Node3D
                    ?? throw new InvalidOperationException(
                        $"Could not duplicate Fallout 3 Vault 101 asset: {reference.AssetId}");
                placement.AddChild(visual);
                foreach (var mesh in Descendants<MeshInstance3D>(visual))
                {
                    if (mesh.Mesh is null)
                        continue;
                    meshInstances++;
                    surfaces += mesh.Mesh.GetSurfaceCount();
                    for (var surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
                    {
                        if (mesh.Mesh is not ArrayMesh arrayMesh)
                            continue;
                        vertices += arrayMesh.SurfaceGetArrayLen(surface);
                        var indices = arrayMesh.SurfaceGetArrayIndexLen(surface);
                        triangles += (indices > 0 ? indices : arrayMesh.SurfaceGetArrayLen(surface)) / 3;
                    }
                }
            }

            var doctorActor = CellActorLoader.Load(
                    contract.DoctorActor.ScenePath,
                    new HashSet<string>([contract.CellFormId], StringComparer.OrdinalIgnoreCase),
                    root,
                    contract.EntryPositionGameUnits,
                    configuration,
                    proofEnableInitiallyDisabled: false)
                ?? throw new InvalidOperationException(
                    "Fallout 3 Doctor Li actor was unexpectedly disabled.");
            if (doctorActor.ReferenceFormId != contract.DoctorActor.ReferenceFormId ||
                doctorActor.BaseFormId != contract.DoctorActor.BaseFormId ||
                doctorActor.RaceFormId != contract.DoctorActor.RaceFormId ||
                doctorActor.HairFormId != contract.DoctorActor.HairFormId ||
                doctorActor.EyesFormId != contract.DoctorActor.EyesFormId ||
                !doctorActor.HeadPartFormIds.SequenceEqual(
                    contract.DoctorActor.HeadPartFormIds) ||
                !doctorActor.OutfitFormIds.SequenceEqual(
                    contract.DoctorActor.OutfitFormIds) ||
                !doctorActor.Placement.Position.IsEqualApprox(
                    contract.DoctorActor.PositionGodotGameUnits) ||
                !doctorActor.Placement.Quaternion.IsEqualApprox(
                    contract.DoctorActor.RotationGodotQuaternion) ||
                !doctorActor.Placement.Scale.IsEqualApprox(
                    Vector3.One * contract.DoctorActor.Scale) ||
                doctorActor.Actor.AuthoredSurfaces != contract.DoctorActor.Surfaces ||
                doctorActor.Actor.AuthoredTextures != contract.DoctorActor.Textures ||
                doctorActor.Actor.Surfaces.Count != contract.DoctorActor.Surfaces ||
                doctorActor.Actor.AnimationLogicalPath != contract.DoctorActor.IdleAnimationPath)
                throw new InvalidOperationException(
                    "Fallout 3 Doctor Li runtime actor differs from its owned contract.");

            var proofLitRetailMaterials =
                RuntimeMaterialLoader.ApplyRetailAmbientDirectionalLighting(
                    root,
                    contract.ProofAmbientColor,
                    contract.ProofBackgroundColor,
                    contract.ProofFogNearGameUnits,
                    contract.ProofFogFarGameUnits,
                    contract.ProofFogPower,
                    contract.UnitsToMeters);
            var proofLitActorMaterials = RuntimeMaterialLoader.ApplyRetailActorLighting(
                doctorActor.Actor.Root,
                contract.ProofAmbientColor,
                contract.ProofBackgroundColor,
                contract.ProofFogNearGameUnits,
                contract.ProofFogFarGameUnits,
                contract.ProofFogPower,
                contract.UnitsToMeters);
            if (proofLitActorMaterials <= 0)
                throw new InvalidOperationException(
                    "Fallout 3 Doctor Li actor received no proof-lighting contract.");

            host.AddChild(new WorldEnvironment
            {
                Name = "FO3_BIRTH_PROOF_ENVIRONMENT",
                Environment = new Godot.Environment
                {
                    BackgroundMode = Godot.Environment.BGMode.Color,
                    BackgroundColor = contract.ProofBackgroundColor,
                    AmbientLightSource = Godot.Environment.AmbientSource.Color,
                    AmbientLightColor = contract.ProofAmbientColor,
                    AmbientLightEnergy = contract.ProofAmbientEnergy,
                },
            });
            var camera = new Camera3D
            {
                Name = $"ENTRY_{contract.EntryReferenceFormId}_OWNED_SUPPORT_PROOF_CAMERA",
                Position = root.ToGlobal(contract.ProofCameraPositionGodotGameUnits),
                Quaternion = contract.EntryRotationGodotQuaternion,
                Fov = contract.VerticalFovDegrees,
                Near = contract.ProofCameraNearGameUnits * contract.UnitsToMeters,
                Far = 100.0f,
                Current = true,
            };
            camera.SetMeta("opennv_entry_position_game_units", contract.EntryPositionGameUnits);
            camera.SetMeta(
                "opennv_proof_camera_position_game_units",
                contract.ProofCameraPositionGameUnits);
            camera.SetMeta(
                "opennv_proof_camera_support_reference_form_id",
                contract.ProofCameraSupportReferenceFormId);
            camera.SetMeta("opennv_source_rotation_radians", contract.EntryRotationRadians);
            host.AddChild(camera);
            var doctorActorGeometry = CellReferenceLedger.MeasureGeometry(
                doctorActor.Actor.Root,
                camera,
                doctorActor.Placement.GlobalPosition);
            if (!doctorActorGeometry.RenderLayerVisible ||
                !doctorActorGeometry.AabbValid ||
                !doctorActorGeometry.FrustumIntersection ||
                doctorActorGeometry.Surfaces != contract.DoctorActor.Surfaces ||
                doctorActorGeometry.Vertices <= 0 ||
                doctorActorGeometry.Triangles <= 0)
                throw new InvalidOperationException(
                    "Fallout 3 Doctor Li actor did not enter the birth-room proof frustum.");
            if (meshInstances == 0 || surfaces == 0 || vertices == 0 || triangles == 0)
                throw new InvalidOperationException(
                    "Fallout 3 Vault 101 birth room constructed no render geometry.");
            return new Fo3Vault101BirthSceneCoverage(
                contract,
                root,
                camera,
                prototypes.Count,
                textures.TwoDimensional.Count,
                textures.AuthoredDdsTextures,
                textures.AuthoredDdsMipChainTextures,
                textures.DecodedAuthoredBc1AlphaMipChainTextures,
                textures.RuntimeGeneratedMipTextures,
                materialBindings,
                proofLitRetailMaterials,
                proofLitActorMaterials,
                doctorActor,
                doctorActorGeometry,
                contract.References.Count,
                meshInstances,
                surfaces,
                vertices,
                triangles);
        }
        finally
        {
            foreach (var loaded in prototypes.Values)
            {
                loaded.Scene.Free();
                loaded.CollisionScene?.Free();
            }
        }
    }

    private static IEnumerable<T> Descendants<T>(Node root)
        where T : Node
    {
        foreach (var child in root.GetChildren())
        {
            if (child is T match)
                yield return match;
            foreach (var descendant in Descendants<T>(child))
                yield return descendant;
        }
    }

    private static string NodeIdentifier(string value) => new(
        value.Select(character => char.IsLetterOrDigit(character) ? character : '_').ToArray());
}
