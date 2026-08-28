using Godot;

namespace OpenNV.Runtime.Campaigns.Fallout2.Temple;

internal sealed record Fo2ArroyoCavesSceneCoverage(
    Node3D Root,
    string ManifestPath,
    string ManifestSha256,
    string SourceManifestPath,
    string SourceManifestSha256,
    string SourceProfileId,
    string MapSha256,
    int MapIndex,
    int Elevation,
    int ArrivalTile,
    int ArrivalRotation,
    Vector3 ArrivalWorldMeters,
    int VerifiedArtifacts,
    int VerifiedResources,
    int TileBindings,
    int ConstructedFloorPatches,
    int PlacedTopLevelObjects,
    float SourcePixelsPerMeter,
    int FloorMeshInstances,
    int ObjectSpriteNodes,
    string WalkMaskSha256,
    int WalkableHexes,
    int ArrivalComponentHexes,
    string SourceTransitionSha256);

internal static class Fo2ArroyoCavesScene
{
    internal static Fo2ArroyoCavesSceneCoverage Build(
        Fo2ArroyoCavesPresentationCatalog catalog,
        Node3D parent)
    {
        var scene = Fo2MapSceneBuilder.Build(
            parent,
            Fo2ArroyoCavesPresentationCatalog.MapIndex,
            "ARCAVES.MAP",
            catalog.MapSha256,
            Fo2ArroyoCavesPresentationCatalog.Elevation,
            catalog.ArrivalTile,
            catalog.ArrivalRotation,
            Fo2ArroyoCavesPresentationCatalog.DefaultFloorTileId,
            catalog.TileEntries,
            catalog.Artifacts,
            catalog.TileBindings,
            catalog.ObjectPlacements);
        scene.Root.SetMeta("cache_manifest_sha256", catalog.ManifestSha256);
        scene.Root.SetMeta("source_manifest_sha256", catalog.SourceManifestSha256);
        scene.Root.SetMeta("source_transition_sha256", catalog.SourceTransitionSha256);
        scene.Root.SetMeta("walk_mask_sha256", catalog.WalkMaskSha256);
        scene.Root.SetMeta("walkable_hexes", catalog.WalkableHexes);
        scene.Root.SetMeta("arrival_component_hexes", catalog.ArrivalComponentHexes);
        scene.Root.GetNode<Node3D>("MAP_3_SOURCE_ARRIVAL_MARKER_NO_PLAYER_OBJECT")
            .SetMeta("temple_exit_grid_arrival", true);

        return new Fo2ArroyoCavesSceneCoverage(
            scene.Root,
            catalog.ManifestPath,
            catalog.ManifestSha256,
            catalog.SourceManifestPath,
            catalog.SourceManifestSha256,
            catalog.SourceProfileId,
            catalog.MapSha256,
            scene.MapIndex,
            scene.Elevation,
            scene.ArrivalTile,
            scene.ArrivalRotation,
            scene.ArrivalWorldMeters,
            scene.VerifiedArtifacts,
            catalog.VerifiedResources,
            catalog.TileBindings.Count,
            scene.ConstructedFloorPatches,
            scene.PlacedTopLevelObjects,
            scene.SourcePixelsPerMeter,
            scene.FloorMeshInstances,
            scene.ObjectSpriteNodes,
            catalog.WalkMaskSha256,
            catalog.WalkableHexes,
            catalog.ArrivalComponentHexes,
            catalog.SourceTransitionSha256);
    }
}
