using Godot;

namespace OpenNV.Runtime.Campaigns.Fallout2.Temple;

internal sealed record Fo2MapSceneBuildCoverage(
    Node3D Root,
    int MapIndex,
    string MapName,
    int Elevation,
    int ArrivalTile,
    int ArrivalRotation,
    Vector3 ArrivalWorldMeters,
    int VerifiedArtifacts,
    int SceneArtifacts,
    int ConstructedFloorPatches,
    int ConstructedRoofPatches,
    int PlacedTopLevelObjects,
    float SourcePixelsPerMeter,
    int FloorMeshInstances,
    int ObjectSpriteNodes);

internal static class Fo2MapSceneBuilder
{
    private const int FloorIdMask = 0x0fff;
    private const int RoofIdShift = 16;
    private const float DegreesPerRotation = 60.0f;

    internal static Fo2MapSceneBuildCoverage Build(
        Node3D parent,
        int mapIndex,
        string mapName,
        string mapSha256,
        int elevation,
        int arrivalTile,
        int arrivalRotation,
        int defaultTileId,
        IReadOnlyList<uint> tileEntries,
        IReadOnlyDictionary<string, Fo2MapArtifact> artifacts,
        IReadOnlyDictionary<int, Fo2MapTileBinding> tileBindings,
        IReadOnlyList<Fo2MapObjectPlacement> objectPlacements)
    {
        if (mapIndex < 0 || string.IsNullOrWhiteSpace(mapName) || mapSha256.Length != 64 ||
            elevation is < 0 or > 2 ||
            arrivalTile is < 0 or >= Fo1HexMath.Width * Fo1HexMath.Height ||
            arrivalRotation is < 0 or >= Fo1HexMath.DirectionCount ||
            tileEntries.Count != Fo1HexMath.FloorWidth * Fo1HexMath.FloorHeight)
            throw new InvalidOperationException("Fallout 2 map scene identity is invalid.");

        var floorIds = tileEntries.Select(entry => (int)(entry & FloorIdMask)).ToArray();
        var roofIds = tileEntries.Select(entry =>
            (int)((entry >> RoofIdShift) & FloorIdMask)).ToArray();
        if (roofIds.Any(id => id != defaultTileId))
            throw new InvalidOperationException(
                $"Fallout 2 {mapName} elevation {elevation} has non-default roof art " +
                "without a source height contract.");
        var placements = objectPlacements
            .Where(row => row.Elevation == elevation)
            .OrderBy(row => row.Serial)
            .ToArray();
        if (placements.Any(row => row.Tile is < 0 or >= Fo1HexMath.Width * Fo1HexMath.Height))
            throw new InvalidOperationException(
                $"Fallout 2 {mapName} elevation {elevation} has an invalid object tile.");

        var requiredArtifactIds = floorIds
            .Where(id => id != defaultTileId)
            .Distinct()
            .Select(id => tileBindings.TryGetValue(id, out var binding)
                ? binding.ArtifactId
                : throw new InvalidOperationException(
                    $"Fallout 2 {mapName} floor tile has no artifact binding: {id}"))
            .Concat(placements.Select(row => row.ArtifactId))
            .ToHashSet(StringComparer.Ordinal);
        if (requiredArtifactIds.Count == 0)
            throw new InvalidOperationException(
                $"Fallout 2 {mapName} elevation {elevation} has no presentation artifacts.");
        var textures = requiredArtifactIds.ToDictionary(
            id => id,
            id => LoadTexture(artifacts.TryGetValue(id, out var artifact)
                ? artifact
                : throw new InvalidOperationException(
                    $"Fallout 2 {mapName} scene artifact is absent: {id}")),
            StringComparer.Ordinal);
        var tileWidths = floorIds
            .Where(id => id != defaultTileId)
            .Distinct()
            .Select(id => artifacts[tileBindings[id].ArtifactId].FloorProjection?.SourceWidth ??
                throw new InvalidOperationException(
                    $"Fallout 2 {mapName} floor tile has no source projection contract: {id}"))
            .Distinct()
            .ToArray();
        if (tileWidths.Length != 1)
            throw new InvalidOperationException(
                $"Fallout 2 {mapName} tile art does not have one source projection width.");
        var sourcePixelsPerMeter = tileWidths[0] /
            (Fo1HexMath.ColumnSpacingMeters * 2.0f);
        if (!float.IsFinite(sourcePixelsPerMeter) || sourcePixelsPerMeter <= 0.0f)
            throw new InvalidOperationException(
                $"Fallout 2 {mapName} source pixel scale is invalid.");

        var root = new Node3D
        {
            Name = $"FO2_MAP_{mapIndex:D3}_{NodeIdentifier(mapName)}_SOURCE_ROOT",
        };
        root.SetMeta("map_index", mapIndex);
        root.SetMeta("map_name", mapName);
        root.SetMeta("source_map_sha256", mapSha256);
        root.SetMeta("source_elevation", elevation);
        parent.AddChild(root);

        var floorRoot = new Node3D { Name = $"MAP_{mapIndex}_ELEVATION_{elevation}_FLOOR_FRM" };
        root.AddChild(floorRoot);
        var (renderedFloors, floorMeshes) = BuildFloor(
            floorRoot,
            floorIds,
            defaultTileId,
            tileBindings,
            artifacts,
            textures,
            mapName);

        var objectRoot = new Node3D
        {
            Name = $"MAP_{mapIndex}_ELEVATION_{elevation}_TOP_LEVEL_OBJECT_FRM",
        };
        root.AddChild(objectRoot);
        foreach (var placement in placements)
        {
            var artifact = artifacts[placement.ArtifactId];
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
            sprite.SetMeta("source_object_type", placement.ObjectType);
            sprite.SetMeta("source_logical_path", placement.LogicalPath);
            sprite.SetMeta("source_sha256", artifact.SourceSha256);
            objectRoot.AddChild(sprite);
        }

        var arrivalMarker = new Node3D
        {
            Name = $"MAP_{mapIndex}_SOURCE_ARRIVAL_MARKER_NO_PLAYER_OBJECT",
            Position = Fo1HexMath.Center(arrivalTile),
            Rotation = new Vector3(
                0.0f,
                -arrivalRotation * Mathf.Tau / Fo1HexMath.DirectionCount,
                0.0f),
        };
        arrivalMarker.SetMeta("map_index", mapIndex);
        arrivalMarker.SetMeta("source_elevation", elevation);
        arrivalMarker.SetMeta("source_arrival", true);
        root.AddChild(arrivalMarker);

        return new Fo2MapSceneBuildCoverage(
            root,
            mapIndex,
            mapName,
            elevation,
            arrivalTile,
            arrivalRotation,
            Fo1HexMath.Center(arrivalTile),
            artifacts.Count,
            requiredArtifactIds.Count,
            renderedFloors,
            0,
            placements.Length,
            sourcePixelsPerMeter,
            floorMeshes,
            objectRoot.GetChildCount());
    }

    private static (int Patches, int Meshes) BuildFloor(
        Node3D root,
        IReadOnlyList<int> floorIds,
        int defaultTileId,
        IReadOnlyDictionary<int, Fo2MapTileBinding> tileBindings,
        IReadOnlyDictionary<string, Fo2MapArtifact> artifacts,
        IReadOnlyDictionary<string, Texture2D> textures,
        string mapName)
    {
        var patches = 0;
        var meshes = 0;
        foreach (var group in Enumerable.Range(0, floorIds.Count)
                     .Where(index => floorIds[index] != defaultTileId)
                     .GroupBy(index => floorIds[index])
                     .OrderBy(group => group.Key))
        {
            if (!tileBindings.TryGetValue(group.Key, out var binding))
                throw new InvalidOperationException(
                    $"Fallout 2 {mapName} floor tile has no artifact binding: {group.Key}");
            var artifact = artifacts[binding.ArtifactId];
            var indices = group.ToArray();
            var material = new StandardMaterial3D
            {
                AlbedoTexture = textures[artifact.Id],
                AlbedoColor = Colors.White,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Disabled,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
                TextureRepeat = false,
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
                    new Transform3D(
                        Basis.Identity,
                        Fo1HexMath.FloorPatchCenter(indices[instance])));
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

    private static Texture2D LoadTexture(Fo2MapArtifact artifact)
    {
        var image = Image.LoadFromFile(artifact.Path);
        if (image is null || image.IsEmpty() ||
            image.GetWidth() != artifact.Width || image.GetHeight() != artifact.Height)
            throw new InvalidOperationException(
                $"Fallout 2 PNG dimensions drifted: {artifact.Path}");
        if (artifact.FloorProjection is not null)
        {
            image.Convert(Image.Format.Rgba8);
            var pixels = image.GetData();
            for (var index = 3; index < pixels.Length; index += 4)
                if (pixels[index] != byte.MaxValue)
                    throw new InvalidOperationException(
                        $"Fallout 2 floor projection is not opaque: {artifact.Path}");
        }
        return ImageTexture.CreateFromImage(image);
    }

    private static string NodeIdentifier(string value) => new(
        value.Select(character => char.IsLetterOrDigit(character) ? character : '_').ToArray());
}
