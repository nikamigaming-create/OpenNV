using Godot;

namespace OpenNV.Runtime.Campaigns.Fallout2.Temple;

internal sealed record Fo2TempleSceneCoverage(
    string ManifestPath,
    string ManifestSha256,
    string SourceManifestPath,
    string SourceManifestSha256,
    string SourceProfileId,
    string MapSha256,
    int EntryTile,
    int EntryElevation,
    int EntryRotation,
    Vector3 EntryWorldMeters,
    int VerifiedArtifacts,
    int VerifiedResources,
    int TileBindings,
    int ObjectArtifactBindings,
    int ConstructedFloorPatches,
    int ConstructedRoofPatches,
    int PlacedTopLevelObjects,
    int InventoryObjectsNotPlaced,
    float SourcePixelsPerMeter,
    int FloorMeshInstances,
    int ObjectSpriteNodes,
    Fo2TempleTopologyCoverage Topology);

internal static class Fo2TempleScene
{
    private const int DefaultTileId = 1;
    private const int FloorIdMask = 0x0fff;
    private const int RoofIdShift = 16;
    private const float DegreesPerRotation = 60.0f;

    internal static Fo2TempleSceneCoverage Build(
        Fo2TemplePresentationCatalog catalog,
        Node3D parent)
    {
        if (catalog.EntryElevation != 0 || catalog.ObjectPlacements.Any(row => row.Elevation != 0))
            throw new InvalidOperationException(
                "The bounded Fallout 2 Temple consumer admits elevation zero only.");
        var root = new Node3D { Name = "FO2_TEMPLE_MAP_126_SOURCE_ROOT" };
        root.SetMeta("cache_manifest_sha256", catalog.ManifestSha256);
        root.SetMeta("source_map_sha256", catalog.MapSha256);
        parent.AddChild(root);

        var textures = catalog.Artifacts.Values.ToDictionary(
            artifact => artifact.Id,
            LoadTexture,
            StringComparer.Ordinal);
        var tileWidths = catalog.TileBindings.Values
            .Select(binding => catalog.Artifacts[binding.ArtifactId].Width)
            .Distinct()
            .ToArray();
        if (tileWidths.Length != 1)
            throw new InvalidOperationException(
                "Fallout 2 Temple tile art does not have one source projection width.");
        var sourcePixelsPerMeter = tileWidths[0] /
            (Fo1HexMath.ColumnSpacingMeters * 2.0f);
        if (!float.IsFinite(sourcePixelsPerMeter) || sourcePixelsPerMeter <= 0.0f)
            throw new InvalidOperationException("Fallout 2 Temple source pixel scale is invalid.");

        var floorIds = catalog.TileEntries.Select(entry => (int)(entry & FloorIdMask)).ToArray();
        var roofIds = catalog.TileEntries.Select(entry =>
            (int)((entry >> RoofIdShift) & FloorIdMask)).ToArray();
        if (roofIds.Any(id => id != DefaultTileId))
            throw new InvalidOperationException(
                "Map 126 unexpectedly contains non-default roof art without a source height contract.");
        var floorRoot = new Node3D { Name = "MAP_126_ELEVATION_0_FLOOR_FRM" };
        root.AddChild(floorRoot);
        var (renderedFloors, floorMeshes) = BuildFloor(
            floorRoot,
            floorIds,
            catalog,
            textures);

        var objectRoot = new Node3D { Name = "MAP_126_TOP_LEVEL_OBJECT_FRM" };
        root.AddChild(objectRoot);
        foreach (var placement in catalog.ObjectPlacements)
        {
            if (placement.Tile is < 0 or >= Fo1HexMath.Width * Fo1HexMath.Height)
                throw new InvalidOperationException(
                    $"Fallout 2 Temple top-level object tile is invalid: {placement.Serial}/{placement.Tile}");
            var artifact = catalog.Artifacts[placement.ArtifactId];
            var offset = new Vector2(
                placement.PixelOffset.X + artifact.FrameOffset.X,
                -(placement.PixelOffset.Y + artifact.FrameOffset.Y) + artifact.Height / 2.0f);
            var sprite = new Sprite3D
            {
                Name = $"MAP_OBJECT_{placement.Serial}_{NodeIdentifier(placement.LogicalPath)}",
                Texture = textures[artifact.Id],
                PixelSize = 1.0f / sourcePixelsPerMeter,
                Position = Fo1HexMath.Center(placement.Tile),
                Offset = offset,
                Billboard = BaseMaterial3D.BillboardModeEnum.Disabled,
                RotationDegrees = new Vector3(
                    0.0f,
                    -placement.Rotation * DegreesPerRotation,
                    0.0f),
                Shaded = false,
                DoubleSided = true,
                AlphaCut = SpriteBase3D.AlphaCutMode.OpaquePrepass,
                TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
            };
            sprite.SetMeta("map_serial", placement.Serial);
            sprite.SetMeta("map_tile", placement.Tile);
            sprite.SetMeta("fid", placement.Fid);
            sprite.SetMeta("source_sha256", artifact.SourceSha256);
            objectRoot.AddChild(sprite);
        }

        var entryMarker = new Node3D
        {
            Name = "MAP_HEADER_PLAYER_ENTRY_MARKER_NO_PLAYER_OBJECT",
            Position = Fo1HexMath.Center(catalog.EntryTile),
            Rotation = new Vector3(
                0.0f,
                -catalog.EntryRotation * Mathf.Tau / Fo1HexMath.DirectionCount,
                0.0f),
        };
        entryMarker.SetMeta("map_index", Fo2TemplePresentationCatalog.MapIndex);
        entryMarker.SetMeta("map_header_entry", true);
        root.AddChild(entryMarker);

        var topology = Fo2TempleTopology.Build(root, catalog);

        return new Fo2TempleSceneCoverage(
            catalog.ManifestPath,
            catalog.ManifestSha256,
            catalog.SourceManifestPath,
            catalog.SourceManifestSha256,
            catalog.SourceProfileId,
            catalog.MapSha256,
            catalog.EntryTile,
            catalog.EntryElevation,
            catalog.EntryRotation,
            Fo1HexMath.Center(catalog.EntryTile),
            catalog.Artifacts.Count,
            catalog.VerifiedResources,
            catalog.TileBindings.Count,
            catalog.Artifacts.Values.Count(row => row.Kind == "objects"),
            renderedFloors,
            0,
            catalog.ObjectPlacements.Count,
            catalog.InventoryObjects,
            sourcePixelsPerMeter,
            floorMeshes,
            objectRoot.GetChildCount(),
            topology);
    }

    private static (int Patches, int Meshes) BuildFloor(
        Node3D root,
        IReadOnlyList<int> floorIds,
        Fo2TemplePresentationCatalog catalog,
        IReadOnlyDictionary<string, Texture2D> textures)
    {
        var patches = 0;
        var meshes = 0;
        foreach (var group in Enumerable.Range(0, floorIds.Count)
                     .Where(index => floorIds[index] != DefaultTileId)
                     .GroupBy(index => floorIds[index])
                     .OrderBy(group => group.Key))
        {
            if (!catalog.TileBindings.TryGetValue(group.Key, out var binding))
                throw new InvalidOperationException(
                    $"Fallout 2 Temple floor tile has no artifact binding: {group.Key}");
            var artifact = catalog.Artifacts[binding.ArtifactId];
            var indices = group.ToArray();
            var material = new StandardMaterial3D
            {
                AlbedoTexture = textures[artifact.Id],
                AlbedoColor = Colors.White,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                TextureFilter = BaseMaterial3D.TextureFilterEnum.NearestWithMipmaps,
            };
            var multiMesh = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                Mesh = new PlaneMesh
                {
                    Size = new Vector2(
                        Fo1HexMath.ColumnSpacingMeters * 2.0f,
                        Fo1HexMath.FlatToFlatMeters * 2.0f),
                    Material = material,
                },
                InstanceCount = indices.Length,
            };
            for (var instance = 0; instance < indices.Length; instance++)
                multiMesh.SetInstanceTransform(
                    instance,
                    new Transform3D(Basis.Identity, Fo1HexMath.FloorPatchCenter(indices[instance])));
            root.AddChild(new MultiMeshInstance3D
            {
                Name = $"FLOOR_FRM_{group.Key:D4}_{indices.Length}",
                Multimesh = multiMesh,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });
            patches += indices.Length;
            meshes++;
        }
        return (patches, meshes);
    }

    private static Texture2D LoadTexture(Fo2TempleArtifact artifact)
    {
        var image = Image.LoadFromFile(artifact.Path);
        if (image is null || image.IsEmpty() ||
            image.GetWidth() != artifact.Width || image.GetHeight() != artifact.Height)
            throw new InvalidOperationException(
                $"Fallout 2 Temple PNG dimensions drifted: {artifact.Path}");
        return ImageTexture.CreateFromImage(image);
    }

    private static string NodeIdentifier(string value) => new(
        value.Select(character => char.IsLetterOrDigit(character) ? character : '_').ToArray());
}
