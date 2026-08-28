using Godot;

namespace OpenNV.Runtime.Campaigns.Fallout2.Temple;

internal sealed record Fo2TempleSceneCoverage(
    Node3D Root,
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
    internal static Fo2TempleSceneCoverage Build(
        Fo2TemplePresentationCatalog catalog,
        Node3D parent)
    {
        if (catalog.EntryElevation != 0 || catalog.ObjectPlacements.Any(row => row.Elevation != 0))
            throw new InvalidOperationException(
                "The bounded Fallout 2 Temple consumer admits elevation zero only.");
        var scene = Fo2MapSceneBuilder.Build(
            parent,
            Fo2TemplePresentationCatalog.MapIndex,
            "ARTEMPLE.MAP",
            catalog.MapSha256,
            catalog.EntryElevation,
            catalog.EntryTile,
            catalog.EntryRotation,
            catalog.DefaultFloorTileId,
            catalog.TileEntries,
            catalog.Artifacts,
            catalog.TileBindings,
            catalog.ObjectPlacements);
        scene.Root.SetMeta("cache_manifest_sha256", catalog.ManifestSha256);
        scene.Root.GetNode<Node3D>("MAP_126_SOURCE_ARRIVAL_MARKER_NO_PLAYER_OBJECT")
            .SetMeta("map_header_entry", true);

        var topology = Fo2TempleTopology.Build(scene.Root, catalog);

        return new Fo2TempleSceneCoverage(
            scene.Root,
            catalog.ManifestPath,
            catalog.ManifestSha256,
            catalog.SourceManifestPath,
            catalog.SourceManifestSha256,
            catalog.SourceProfileId,
            catalog.MapSha256,
            catalog.EntryTile,
            catalog.EntryElevation,
            catalog.EntryRotation,
            scene.ArrivalWorldMeters,
            catalog.Artifacts.Count,
            catalog.VerifiedResources,
            catalog.TileBindings.Count,
            catalog.Artifacts.Values.Count(row => row.Kind == "objects"),
            scene.ConstructedFloorPatches,
            scene.ConstructedRoofPatches,
            scene.PlacedTopLevelObjects,
            catalog.InventoryObjects,
            scene.SourcePixelsPerMeter,
            scene.FloorMeshInstances,
            scene.ObjectSpriteNodes,
            topology);
    }
}
