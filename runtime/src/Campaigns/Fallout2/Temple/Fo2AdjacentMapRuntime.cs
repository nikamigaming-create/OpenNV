using System.Text.Json;
using Godot;
using OpenNV.Runtime.Campaigns.Classic;

namespace OpenNV.Runtime.Campaigns.Fallout2.Temple;

internal sealed record Fo2AdjacentMapPresentation(
    string CacheSha256,
    int MapIndex,
    string MapName,
    string MapSha256,
    int DefaultTileId,
    IReadOnlyDictionary<int, IReadOnlyList<uint>> TileEntries,
    IReadOnlyDictionary<int, IReadOnlySet<int>> Walkable,
    IReadOnlyDictionary<int, string> WalkMaskSha256,
    IReadOnlyDictionary<string, Fo2MapArtifact> Artifacts,
    IReadOnlyDictionary<int, Fo2MapTileBinding> TileBindings,
    IReadOnlyList<Fo2MapObjectPlacement> ObjectPlacements)
{
    internal static Fo2AdjacentMapPresentation Load(string cacheManifestPath)
    {
        var manifest = Path.GetFullPath(cacheManifestPath);
        var cacheBytes = File.ReadAllBytes(manifest);
        using var cacheDocument = JsonDocument.Parse(cacheBytes);
        var cache = cacheDocument.RootElement;
        if (Fo2TemplePresentationCatalog.RequiredString(cache, "schema") !=
                "opennv-fo2-adjacent-map-presentation-cache/v1" ||
            Fo2TemplePresentationCatalog.RequiredString(cache, "status") !=
                "decoded-disposable-local-cache" ||
            Fo2TemplePresentationCatalog.RequiredString(cache, "slice") != "AdjacentMaps" ||
            cache.GetProperty("retailOrDerivedAssetsPackaged").GetBoolean() ||
            !cache.GetProperty("cachePolicy").GetProperty(
                "containsDerivedOwnedPixels").GetBoolean())
            throw new InvalidOperationException("Unexpected Fallout 2 adjacent MAP cache.");
        var cacheRoot = Path.GetDirectoryName(manifest)!;
        var sourceDescriptor = cache.GetProperty("sourceManifest");
        var sourcePath = Resolve(
            Fo2TemplePresentationCatalog.RequiredString(sourceDescriptor, "file"),
            cacheRoot);
        var sourceBytes = Fo2TemplePresentationCatalog.VerifyFile(
            sourcePath,
            Fo2TemplePresentationCatalog.RequiredHash(sourceDescriptor, "sha256"),
            null,
            "Fallout 2 adjacent source catalog");
        using var sourceDocument = JsonDocument.Parse(sourceBytes);
        var source = sourceDocument.RootElement;
        if (Fo2TemplePresentationCatalog.RequiredString(source, "schema") !=
                "opennv-fo2-adjacent-map-catalog/v1" ||
            Fo2TemplePresentationCatalog.RequiredString(source, "status") !=
                "compiled-owned-reciprocal-adjacent-maps" ||
            source.GetProperty("runtimeCompatibility").GetProperty("ready").GetBoolean())
            throw new InvalidOperationException("Unexpected Fallout 2 adjacent source catalog.");
        var mapIndex = sourceDescriptor.GetProperty("mapIndex").GetInt32();
        var maps = source.GetProperty("maps").EnumerateArray()
            .Where(row => row.GetProperty("mapIndex").GetInt32() == mapIndex).ToArray();
        if (maps.Length != 1)
            throw new InvalidOperationException("Fallout 2 adjacent cache MAP is ambiguous.");
        var map = maps[0];
        var mapSha256 = Fo2TemplePresentationCatalog.RequiredHash(map, "mapSha256");
        if (Fo2TemplePresentationCatalog.RequiredHash(sourceDescriptor, "mapSha256") !=
                mapSha256)
            throw new InvalidOperationException("Fallout 2 adjacent MAP hash binding drifted.");
        var tileEntries = map.GetProperty("layout").GetProperty("elevations")
            .EnumerateArray().ToDictionary(
                row => row.GetProperty("elevation").GetInt32(),
                row => (IReadOnlyList<uint>)row.GetProperty("rawEntries").EnumerateArray()
                    .Select(value => value.GetUInt32()).ToArray());
        var walkRows = map.GetProperty("walkTopology").EnumerateArray().ToArray();
        var walkable = walkRows.ToDictionary(
            row => row.GetProperty("elevation").GetInt32(),
            row => (IReadOnlySet<int>)row.GetProperty("tiles").EnumerateArray()
                .Select(value => value.GetInt32()).ToHashSet());
        var walkHashes = walkRows.ToDictionary(
            row => row.GetProperty("elevation").GetInt32(),
            row => Fo2TemplePresentationCatalog.RequiredHash(row, "walkMaskSha256"));
        var artifacts = Fo2TemplePresentationCatalog.LoadArtifacts(
            cache.GetProperty("artifacts"), cacheRoot);
        var tileBindings = Fo2TemplePresentationCatalog.LoadTileBindings(
            cache.GetProperty("tileBindings"), artifacts);
        Fo2TemplePresentationCatalog.VerifyTileBindings(tileEntries, tileBindings);
        var objects = Fo2TemplePresentationCatalog.FlattenObjects(map.GetProperty("objects"));
        var placements = Fo2TemplePresentationCatalog.LoadObjectPlacements(
            cache.GetProperty("objectBindings"),
            map.GetProperty("frms"),
            artifacts,
            objects);
        return new Fo2AdjacentMapPresentation(
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(cacheBytes))
                .ToLowerInvariant(),
            mapIndex,
            Fo2TemplePresentationCatalog.RequiredString(map, "mapName"),
            mapSha256,
            map.GetProperty("defaultTileId").GetInt32(),
            tileEntries,
            walkable,
            walkHashes,
            artifacts,
            tileBindings,
            placements);
    }

    internal Fo2MapSceneBuildCoverage Build(Node3D parent, ClassicMapEndpoint endpoint)
    {
        if (endpoint.MapIndex != MapIndex || endpoint.MapSha256 != MapSha256 ||
            endpoint.Elevation is not int elevation ||
            endpoint.Rotation is not int rotation ||
            !Walkable.TryGetValue(elevation, out var admitted) ||
            !admitted.Contains(endpoint.Tile))
            throw new InvalidOperationException(
                "Fallout 2 adjacent endpoint differs from its owned presentation.");
        return Fo2MapSceneBuilder.Build(
            parent, MapIndex, MapName, MapSha256, elevation, endpoint.Tile, rotation,
            DefaultTileId,
            TileEntries[elevation], Artifacts, TileBindings, ObjectPlacements,
            allowOwnedRoofCutaway: true);
    }

    private static string Resolve(string value, string root) => Path.IsPathRooted(value)
        ? Path.GetFullPath(value)
        : Path.GetFullPath(Path.Combine(root, value));
}

internal sealed class Fo2AdjacentMapSession
{
    private readonly ClassicAdjacentMapCatalog _joins;
    private readonly Fo2AdjacentMapPresentation _destination;

    internal Fo2AdjacentMapSession(
        ClassicAdjacentMapCatalog joins,
        Fo2AdjacentMapPresentation destination)
    {
        _joins = joins;
        _destination = destination;
    }

    internal string JoinCatalogSha256 => _joins.Sha256;
    internal string DestinationCacheSha256 => _destination.CacheSha256;
    internal int DestinationMapIndex => _destination.MapIndex;

    internal bool CanActivate(ClassicMapEndpoint endpoint) =>
        endpoint.MapIndex == _destination.MapIndex &&
        endpoint.MapSha256.Equals(
            _destination.MapSha256, StringComparison.OrdinalIgnoreCase);

    internal ClassicMapJoinState? TryCommit(Fo2ArroyoCavesPlayerBody player) =>
        _joins.TryCommitAt(
            player.CurrentMapIndex,
            player.CurrentMapSha256,
            player.CurrentTile,
            player.CurrentElevation);

    internal Fo2MapSceneBuildCoverage? TryActivate(
        Node3D world,
        Fo2ArroyoCavesPlayerBody player)
    {
        var committed = TryCommit(player);
        if (committed is null)
            return null;
        return Activate(world, player, committed.Join.Destination);
    }

    internal Fo2MapSceneBuildCoverage Activate(
        Node3D world,
        Fo2ArroyoCavesPlayerBody player,
        ClassicMapEndpoint destination)
    {
        var scene = _destination.Build(world, destination);
        var elevation = destination.Elevation!.Value;
        player.EnterAdjacentMap(
            scene.Root,
            destination,
            _destination.Walkable[elevation],
            _destination.WalkMaskSha256[elevation],
            _destination.CacheSha256);
        return scene;
    }
}
