using Godot;

namespace OpenNV.Runtime.Campaigns.Fallout2.Temple;

internal sealed record Fo2TempleAppliedTransition(
    int ExitSerial,
    int SourceMapIndex,
    string SourceMapSha256,
    int SourceTile,
    int TargetMapIndex,
    string TargetMapSha256,
    string TargetMapName,
    int TargetTile,
    int TargetElevation,
    int TargetRotation);

internal sealed class Fo2TempleTransitionRuntime
{
    internal const string Schema = "opennv-fo2-temple-nonvisual-exit-transition/v1";
    private readonly Fo2TempleTransitionCatalog _catalog;
    private readonly Fo2TempleMovementConsumer _movement;
    private readonly Node _stateNode;

    private Fo2TempleTransitionRuntime(
        Fo2TempleTransitionCatalog catalog,
        Fo2TempleMovementConsumer movement,
        Node stateNode)
    {
        _catalog = catalog;
        _movement = movement;
        _stateNode = stateNode;
    }

    internal Fo2TempleAppliedTransition? Applied { get; private set; }

    internal IReadOnlyList<Fo2TempleExitGrid> ReachableExits => _catalog.Exits
        .Where(row => _movement.CanReachFromEntry(row.Tile))
        .OrderBy(row => row.Serial)
        .ToArray();

    internal static Fo2TempleTransitionRuntime Build(
        Node3D root,
        Fo2TempleTransitionCatalog catalog,
        Fo2TempleMovementConsumer movement)
    {
        if (movement.MapSha256.Length != 64 ||
            movement.TopologyProfileId != "fo2-temple-map-126-topology-v1" ||
            movement.TopologyProfileSha256.Length != 64 ||
            movement.WalkMaskSha256.Length != 64 ||
            catalog.HeaderProgram.IndexSemantics != "MAP-header-one-based-to-scripts-list" ||
            catalog.Exits.Count == 0 ||
            catalog.DestinationMaps.Count == 0)
            throw new InvalidOperationException(
                "Fallout 2 Temple nonvisual transition identity drifted.");
        var reachable = catalog.Exits.Where(row => movement.CanReachFromEntry(row.Tile)).ToArray();
        if (reachable.Length == 0 || reachable.Any(row => row.SourceBlocking))
            throw new InvalidOperationException(
                "Fallout 2 Temple entry component has no admitted source exit-grid transition.");
        var stateNode = new Node { Name = "MAP_126_NONVISUAL_EXIT_TRANSITION_STATE" };
        stateNode.SetMeta("transition_schema", Schema);
        stateNode.SetMeta("transition_manifest_sha256", catalog.ManifestSha256);
        stateNode.SetMeta("source_map_sha256", movement.MapSha256);
        stateNode.SetMeta("topology_profile_id", movement.TopologyProfileId);
        stateNode.SetMeta("topology_profile_sha256", movement.TopologyProfileSha256);
        stateNode.SetMeta("walk_mask_sha256", movement.WalkMaskSha256);
        stateNode.SetMeta("header_program_sha256", catalog.HeaderProgram.Sha256);
        stateNode.SetMeta("header_program_execution", false);
        root.AddChild(stateNode);
        return new Fo2TempleTransitionRuntime(catalog, movement, stateNode);
    }

    internal bool TryApplyAtCurrentTile()
    {
        if (Applied is not null)
            throw new InvalidOperationException(
                "Fallout 2 Temple nonvisual transition was already applied.");
        var matches = _catalog.Exits.Where(row => row.Tile == _movement.CurrentTile).ToArray();
        if (matches.Length == 0)
            return false;
        if (matches.Length != 1)
            throw new InvalidOperationException(
                "Fallout 2 Temple current tile has ambiguous exit-grid records.");
        var exit = matches[0];
        if (exit.SourceBlocking || !_movement.CanReachFromEntry(exit.Tile) ||
            !_catalog.DestinationMaps.TryGetValue(exit.TargetMapIndex, out var destination) ||
            !destination.PresentElevations.Contains(exit.TargetElevation))
            throw new InvalidOperationException(
                "Fallout 2 Temple exit-grid transition failed its source boundary.");
        Applied = new Fo2TempleAppliedTransition(
            exit.Serial,
            Fo2TemplePresentationCatalog.MapIndex,
            _movement.MapSha256,
            exit.Tile,
            exit.TargetMapIndex,
            destination.Sha256,
            destination.MapName,
            exit.TargetTile,
            exit.TargetElevation,
            exit.TargetRotation);
        _stateNode.SetMeta("transition_applied", true);
        _stateNode.SetMeta("exit_serial", exit.Serial);
        _stateNode.SetMeta("source_tile", exit.Tile);
        _stateNode.SetMeta("target_map_index", exit.TargetMapIndex);
        _stateNode.SetMeta("target_map_sha256", destination.Sha256);
        _stateNode.SetMeta("target_tile", exit.TargetTile);
        _stateNode.SetMeta("target_elevation", exit.TargetElevation);
        _stateNode.SetMeta("target_rotation", exit.TargetRotation);
        return true;
    }
}
