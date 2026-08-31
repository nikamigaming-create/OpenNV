using Godot;
using OpenNV.Runtime.Campaigns.Classic;

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
        => TryApplyAtSourceTile(_movement.CurrentTile, null);

    internal bool TryApplyPostTrial(Fo2TrialVillageTransition expected)
    {
        if (expected.Path.Sha256.Length != 64 ||
            expected.Path.Steps[^1].Tile != expected.SourceTile)
            throw new InvalidOperationException(
                "Fallout 2 post-trial transition lacks its exact admitted source route.");
        var source = _catalog.Exits.SingleOrDefault(row => row.Serial == expected.ExitSerial);
        if (source is null || source.Tile != expected.SourceTile ||
            source.TargetMapIndex != expected.TargetMapIndex ||
            source.TargetTile != expected.TargetTile ||
            source.TargetElevation != expected.TargetElevation ||
            source.TargetRotation != expected.TargetRotation ||
            !_catalog.DestinationMaps.TryGetValue(expected.TargetMapIndex, out var map) ||
            map.Sha256 != expected.TargetMapSha256 || map.MapName != expected.TargetMapName)
            throw new InvalidOperationException(
                "Fallout 2 post-trial transition differs from the owned exit catalog.");
        return TryApplyAtSourceTile(expected.SourceTile, expected.Path.Sha256);
    }

    private bool TryApplyAtSourceTile(int sourceTile, string? admittedRouteSha256)
    {
        if (Applied is not null)
            throw new InvalidOperationException(
                "Fallout 2 Temple nonvisual transition was already applied.");
        var matches = _catalog.Exits.Where(row => row.Tile == sourceTile).ToArray();
        if (matches.Length == 0)
            return false;
        if (matches.Length != 1)
            throw new InvalidOperationException(
                "Fallout 2 Temple current tile has ambiguous exit-grid records.");
        var exit = matches[0];
        var sourceRouteAdmitted = admittedRouteSha256 is { Length: 64 } &&
            admittedRouteSha256.All(Uri.IsHexDigit);
        if (exit.SourceBlocking ||
            (!sourceRouteAdmitted && !_movement.CanReachFromEntry(exit.Tile)) ||
            !_catalog.DestinationMaps.TryGetValue(exit.TargetMapIndex, out var destination) ||
            !destination.PresentElevations.Contains(exit.TargetElevation))
            throw new InvalidOperationException(
                "Fallout 2 Temple exit-grid transition failed its source boundary.");
        var join = new ClassicMapJoin(
            exit.Serial,
            new ClassicMapEndpoint(
                Fo2TemplePresentationCatalog.MapIndex,
                _catalog.SourceMapName,
                _movement.MapSha256,
                exit.Tile,
                exit.Elevation,
                null),
            new ClassicMapEndpoint(
                exit.TargetMapIndex,
                destination.MapName,
                destination.Sha256,
                exit.TargetTile,
                exit.TargetElevation,
                exit.TargetRotation));
        _ = ClassicMapJoinOwner.Commit(
            join,
            Fo2TemplePresentationCatalog.MapIndex,
            _movement.MapSha256,
            sourceTile,
            exit.Elevation);
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
        _stateNode.SetMeta("admitted_source_route_sha256", admittedRouteSha256 ?? "");
        _stateNode.SetMeta("target_map_index", exit.TargetMapIndex);
        _stateNode.SetMeta("target_map_sha256", destination.Sha256);
        _stateNode.SetMeta("target_tile", exit.TargetTile);
        _stateNode.SetMeta("target_elevation", exit.TargetElevation);
        _stateNode.SetMeta("target_rotation", exit.TargetRotation);
        return true;
    }

    internal void RestoreApplied(Fo2TempleAppliedTransition expected)
    {
        if (!TryApplyAtSourceTile(expected.SourceTile, null) || Applied != expected)
            throw new InvalidOperationException(
                "Fallout 2 saved Temple exit differs from the exact owned exit-grid record.");
    }

    internal void RestorePostTrialApplied(
        Fo2TempleAppliedTransition expected,
        Fo2TrialVillageTransition source)
    {
        if (!TryApplyPostTrial(source) || Applied != expected)
            throw new InvalidOperationException(
                "Fallout 2 saved post-trial exit differs from the exact owned route/exit join.");
    }
}
