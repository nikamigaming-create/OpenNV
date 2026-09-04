using Godot;
using OpenNV.Runtime.Content;
using System.Security.Cryptography;

namespace OpenNV.Runtime.Campaigns.Fallout1.Native;

internal sealed record Fallout1NativeResolvedExitGrid(
    int SourceSerial,
    int SourceTile,
    int DestinationMap,
    int DestinationTile,
    int DestinationElevation,
    int DestinationRotation);

internal sealed record Fallout1NativePlayerArrival(
    string SaveCompatibilityId,
    string IsolatedSavePath,
    int MapIndex,
    string MapName,
    string MapLogicalPath,
    string MapSha256,
    int Tile,
    int Elevation,
    int Rotation,
    int LiveMapScripts);

internal sealed partial class Fallout1NativeV13InteractionRuntime : Node3D
{
    private const float CollisionHeightMeters = 2.0f;
    private const float CollisionHalfHeightMeters = CollisionHeightMeters / 2.0f;
    private const int Vault13MapIndex = 6;
    private const int Vault13ArrivalTile = 17695;
    private const int Vault13ArrivalElevation = 0;
    private const int Vault13ArrivalRotation = 0;
    private const string Vault13MapLogicalPath = "maps\\vault13.map";
    private const string Vault13MapName = "VAULT13.MAP";

    private readonly HashSet<int> _scrollBlockers = [];
    private readonly Dictionary<int, Fallout1NativeResolvedExitGrid> _resolvedExitGrids = [];
    private CollisionShape3D? _securityDoorCollision;
    private Fallout1OwnedContentSource? _ownedSource;
    private int _securityDoorSerial = -1;
    private int _securityDoorTile = -1;

    internal int ScrollBlockerCount => _scrollBlockers.Count;
    internal int CollisionShapeCount => _scrollBlockers.Count + (_securityDoorCollision is null ? 0 : 1);
    internal int SecurityDoorSerial => _securityDoorSerial;
    internal int SecurityDoorTile => _securityDoorTile;
    internal bool SecurityDoorOpen { get; private set; }
    internal int ResolvedExitGridCount => _resolvedExitGrids.Count;
    internal IReadOnlyList<int> ResolvedExitSourceTiles => _resolvedExitGrids.Keys.Order().ToArray();
    internal string? IsolatedSavePath { get; private set; }
    internal string? SaveCompatibilityId { get; private set; }
    internal Fallout1NativePlayerArrival? AuthoritativePlayerArrival { get; private set; }

    internal static Fallout1NativeV13InteractionRuntime Create(
        IReadOnlyList<Fallout1NativeMapObject> scrollBlockers,
        Fallout1NativeMapObject securityDoor,
        IReadOnlyList<Fallout1NativeMapObject> resolvedExitGrids)
    {
        var runtime = new Fallout1NativeV13InteractionRuntime
        {
            Name = "V13ENT_NATIVE_INTERACTIONS",
        };
        var shape = HexPrism();
        var scrollBody = new StaticBody3D { Name = "SOURCE_SCROLL_BLOCKER_COLLISION" };
        runtime.AddChild(scrollBody);
        foreach (var blocker in scrollBlockers)
        {
            if (!runtime._scrollBlockers.Add(blocker.Tile))
                throw new InvalidDataException($"Duplicate V13ENT Scroll Blocker tile {blocker.Tile}.");
            var collision = new CollisionShape3D
            {
                Name = $"SCROLL_BLOCKER_{blocker.Serial:D4}",
                Shape = shape,
                Position = Fo1HexMath.Center(blocker.Tile) + Vector3.Up * CollisionHalfHeightMeters,
            };
            collision.SetMeta("source_serial", blocker.Serial);
            collision.SetMeta("source_tile", blocker.Tile);
            scrollBody.AddChild(collision);
        }

        runtime._securityDoorSerial = securityDoor.Serial;
        runtime._securityDoorTile = securityDoor.Tile;
        var doorBody = new StaticBody3D { Name = "SOURCE_SECURITY_DOOR_COLLISION" };
        runtime.AddChild(doorBody);
        runtime._securityDoorCollision = new CollisionShape3D
        {
            Name = $"SECURITY_DOOR_{securityDoor.Serial:D4}_CLOSED",
            Shape = shape,
            Position = Fo1HexMath.Center(securityDoor.Tile) + Vector3.Up * CollisionHalfHeightMeters,
        };
        runtime._securityDoorCollision.SetMeta("source_serial", securityDoor.Serial);
        runtime._securityDoorCollision.SetMeta("source_tile", securityDoor.Tile);
        runtime._securityDoorCollision.SetMeta("source_initial_instance_word", securityDoor.InstanceValues[0]);
        doorBody.AddChild(runtime._securityDoorCollision);

        foreach (var placed in resolvedExitGrids)
        {
            var values = placed.InstanceValues;
            var destination = new Fallout1NativeResolvedExitGrid(
                placed.Serial, placed.Tile, values[0], values[1], values[2], values[3]);
            if (!runtime._resolvedExitGrids.TryAdd(placed.Tile, destination))
                throw new InvalidDataException($"Duplicate resolved V13ENT Exit Grid tile {placed.Tile}.");
        }
        runtime.SetMeta("scroll_blocker_collisions", runtime.ScrollBlockerCount);
        runtime.SetMeta("security_door_collision", 1);
        runtime.SetMeta("resolved_exit_grids", runtime.ResolvedExitGridCount);
        runtime.SetMeta("script_execution", "fail-closed");
        return runtime;
    }

    internal void BindIsolatedSavePath(string savePath)
    {
        var fullPath = Path.GetFullPath(savePath);
        IsolatedSavePath = fullPath;
        SetMeta("isolated_save_path", fullPath);
        SetMeta("save_writes", 0);
    }

    internal void BindOwnedDestinationSource(Fallout1OwnedContentSource source, string savePath)
    {
        var fullPath = Path.GetFullPath(savePath);
        var installDirectory = Path.GetFullPath(source.InstallRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var installRoot = installDirectory + Path.DirectorySeparatorChar;
        if (fullPath.Equals(installDirectory, StringComparison.OrdinalIgnoreCase) ||
            fullPath.StartsWith(installRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Fallout 1 native saves may not reside in the owned install.");
        _ownedSource = source;
        SaveCompatibilityId = $"fallout1:{source.ProfileId}";
        BindIsolatedSavePath(fullPath);
        SetMeta("save_compatibility_id", SaveCompatibilityId);
        SetMeta("destination_map_loading", "direct-owned-data");
    }

    internal bool IsTileBlocked(int tile) =>
        _scrollBlockers.Contains(tile) || (!SecurityDoorOpen && tile == _securityDoorTile);

    internal bool TryActivateSecurityDoor(int actorTile)
    {
        if (SecurityDoorOpen || !Fo1HexMath.AreNeighbors(actorTile, _securityDoorTile))
            return false;
        SecurityDoorOpen = true;
        _securityDoorCollision!.Disabled = true;
        _securityDoorCollision.Name = $"SECURITY_DOOR_{_securityDoorSerial:D4}_OPEN";
        SetMeta("security_door_open", true);
        return true;
    }

    internal bool TryConsumeResolvedExitGrid(
        int sourceTile,
        out Fallout1NativeResolvedExitGrid? destination)
    {
        if (!_resolvedExitGrids.TryGetValue(sourceTile, out destination))
            return false;
        SetMeta("last_consumed_exit_serial", destination.SourceSerial);
        SetMeta("last_consumed_exit_source_tile", destination.SourceTile);
        SetMeta("last_consumed_exit_destination_map", destination.DestinationMap);
        SetMeta("last_consumed_exit_destination_tile", destination.DestinationTile);
        return true;
    }

    internal Fallout1NativePlayerArrival CommitResolvedExitGrid(int sourceTile)
    {
        if (_ownedSource is null || IsolatedSavePath is null || SaveCompatibilityId is null)
            throw new InvalidOperationException(
                "Fallout 1 native exit transition requires an owned source and isolated save identity.");
        if (AuthoritativePlayerArrival is not null)
            throw new InvalidOperationException("Fallout 1 native exit transition was already committed.");
        if (!TryConsumeResolvedExitGrid(sourceTile, out var destination) || destination is null)
            throw new InvalidOperationException($"Tile {sourceTile} is not a resolved V13ENT Exit Grid.");
        if (destination.DestinationMap != Vault13MapIndex ||
            destination.DestinationTile != Vault13ArrivalTile ||
            destination.DestinationElevation != Vault13ArrivalElevation ||
            destination.DestinationRotation != Vault13ArrivalRotation)
            throw new NotSupportedException(
                "Only the exact resolved V13ENT to VAULT13 source transition is admitted.");

        var resource = _ownedSource.Read(Vault13MapLogicalPath);
        var map = Fallout1NativeMapReader.Read(resource.Bytes);
        if (!map.Name.Equals(Vault13MapName, StringComparison.OrdinalIgnoreCase) ||
            !map.Elevations.ContainsKey(destination.DestinationElevation) ||
            destination.DestinationTile is < 0 or >= Fo1HexMath.Width * Fo1HexMath.Height)
            throw new InvalidDataException("The owned VAULT13 destination does not match the exit-grid contract.");

        AuthoritativePlayerArrival = new Fallout1NativePlayerArrival(
            SaveCompatibilityId,
            IsolatedSavePath,
            destination.DestinationMap,
            map.Name,
            resource.LogicalPath,
            Convert.ToHexString(SHA256.HashData(resource.Bytes)).ToLowerInvariant(),
            destination.DestinationTile,
            destination.DestinationElevation,
            destination.DestinationRotation,
            map.LiveScripts.Count);
        SetMeta("authoritative_player_map", AuthoritativePlayerArrival.MapName);
        SetMeta("authoritative_player_tile", AuthoritativePlayerArrival.Tile);
        SetMeta("authoritative_player_elevation", AuthoritativePlayerArrival.Elevation);
        SetMeta("authoritative_player_rotation", AuthoritativePlayerArrival.Rotation);
        SetMeta("destination_live_scripts", AuthoritativePlayerArrival.LiveMapScripts);
        SetMeta("destination_script_execution", "fail-closed");
        SetMeta("destination_bytes_written", 0);
        return AuthoritativePlayerArrival;
    }

    internal void ExecuteDestinationScript(uint scriptId) =>
        throw new NotSupportedException(
            $"Fallout 1 destination script 0x{scriptId:x8} execution is not admitted.");

    private static ConvexPolygonShape3D HexPrism()
    {
        var points = Enumerable.Range(0, Fo1HexMath.DirectionCount)
            .SelectMany(index =>
            {
                var corner = Fo1HexMath.CornerOffset(index);
                return new[]
                {
                    corner + Vector3.Down * CollisionHalfHeightMeters,
                    corner + Vector3.Up * CollisionHalfHeightMeters,
                };
            })
            .ToArray();
        return new ConvexPolygonShape3D { Points = points };
    }
}
