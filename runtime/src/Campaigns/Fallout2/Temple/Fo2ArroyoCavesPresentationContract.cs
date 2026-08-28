using System.Text.Json;

namespace OpenNV.Runtime.Campaigns.Fallout2.Temple;

internal sealed class Fo2ArroyoCavesPresentationCatalog
{
    private const string CacheSchema = "opennv-fo2-arroyo-caves-presentation-cache/v1";
    private const string SourceSchema = "opennv-fo2-owned-map-slice/v1";
    private const string ProfileSchema = "opennv-fo2-owned-profile/v1";
    private const int MapVersion = 20;
    private const int TileEntryCount = Fo1HexMath.FloorWidth * Fo1HexMath.FloorHeight;
    private const int HexEntryCount = Fo1HexMath.Width * Fo1HexMath.Height;
    internal const int MapIndex = 3;
    internal const int Elevation = 0;
    internal const int DefaultFloorTileId = 1;

    private Fo2ArroyoCavesPresentationCatalog(
        string manifestPath,
        string manifestSha256,
        string sourceManifestPath,
        string sourceManifestSha256,
        string sourceProfileId,
        string mapSha256,
        int arrivalTile,
        int arrivalRotation,
        uint[] tileEntries,
        IReadOnlyDictionary<string, Fo2MapArtifact> artifacts,
        IReadOnlyDictionary<int, Fo2MapTileBinding> tileBindings,
        IReadOnlyList<Fo2MapObjectPlacement> objectPlacements,
        int verifiedResources,
        string walkMaskSha256,
        int walkableHexes,
        int arrivalComponentHexes,
        string sourceTransitionSha256)
    {
        ManifestPath = manifestPath;
        ManifestSha256 = manifestSha256;
        SourceManifestPath = sourceManifestPath;
        SourceManifestSha256 = sourceManifestSha256;
        SourceProfileId = sourceProfileId;
        MapSha256 = mapSha256;
        ArrivalTile = arrivalTile;
        ArrivalRotation = arrivalRotation;
        TileEntries = tileEntries;
        Artifacts = artifacts;
        TileBindings = tileBindings;
        ObjectPlacements = objectPlacements;
        VerifiedResources = verifiedResources;
        WalkMaskSha256 = walkMaskSha256;
        WalkableHexes = walkableHexes;
        ArrivalComponentHexes = arrivalComponentHexes;
        SourceTransitionSha256 = sourceTransitionSha256;
    }

    internal string ManifestPath { get; }
    internal string ManifestSha256 { get; }
    internal string SourceManifestPath { get; }
    internal string SourceManifestSha256 { get; }
    internal string SourceProfileId { get; }
    internal string MapSha256 { get; }
    internal int ArrivalTile { get; }
    internal int ArrivalRotation { get; }
    internal uint[] TileEntries { get; }
    internal IReadOnlyDictionary<string, Fo2MapArtifact> Artifacts { get; }
    internal IReadOnlyDictionary<int, Fo2MapTileBinding> TileBindings { get; }
    internal IReadOnlyList<Fo2MapObjectPlacement> ObjectPlacements { get; }
    internal int VerifiedResources { get; }
    internal string WalkMaskSha256 { get; }
    internal int WalkableHexes { get; }
    internal int ArrivalComponentHexes { get; }
    internal string SourceTransitionSha256 { get; }

    internal static Fo2ArroyoCavesPresentationCatalog Load(
        string cacheManifestPath,
        Fo2TempleTransitionCatalog transition)
    {
        var manifestPath = Fo2TemplePresentationCatalog.ResolvePath(
            cacheManifestPath,
            Directory.GetCurrentDirectory());
        var cacheBytes = File.ReadAllBytes(manifestPath);
        using var cacheDocument = JsonDocument.Parse(cacheBytes);
        var cache = cacheDocument.RootElement;
        if (Fo2TemplePresentationCatalog.RequiredString(cache, "schema") != CacheSchema ||
            Fo2TemplePresentationCatalog.RequiredString(cache, "status") !=
                "decoded-disposable-local-cache" ||
            Fo2TemplePresentationCatalog.RequiredString(cache, "campaign") != "Fallout2" ||
            Fo2TemplePresentationCatalog.RequiredString(cache, "slice") != "ArroyoCaves" ||
            cache.GetProperty("runtimeCompatibility").GetProperty("ready").GetBoolean() ||
            cache.GetProperty("retailOrDerivedAssetsPackaged").GetBoolean() ||
            cache.GetProperty("cachePolicy").GetProperty("distributionAllowed").GetBoolean() ||
            !cache.GetProperty("cachePolicy").GetProperty("containsDerivedOwnedPixels").GetBoolean())
            throw new InvalidOperationException(
                "Unexpected Fallout 2 Arroyo Caves presentation cache.");
        var cacheRoot = Path.GetDirectoryName(manifestPath)!;

        var sourceDescriptor = cache.GetProperty("sourceManifest");
        var sourcePath = Fo2TemplePresentationCatalog.ResolvePath(
            Fo2TemplePresentationCatalog.RequiredString(sourceDescriptor, "file"),
            cacheRoot);
        var sourceBytes = Fo2TemplePresentationCatalog.VerifyFile(
            sourcePath,
            Fo2TemplePresentationCatalog.RequiredHash(sourceDescriptor, "sha256"),
            null,
            "Fallout 2 Arroyo Caves source manifest");
        using var sourceDocument = JsonDocument.Parse(sourceBytes);
        var source = sourceDocument.RootElement;
        if (Fo2TemplePresentationCatalog.RequiredString(source, "schema") != SourceSchema ||
            Fo2TemplePresentationCatalog.RequiredString(source, "status") !=
                "transported-owned-map-source-and-presentation-graph" ||
            Fo2TemplePresentationCatalog.RequiredString(source, "campaign") != "Fallout2" ||
            Fo2TemplePresentationCatalog.RequiredString(source, "slice") != "ArroyoCaves" ||
            source.GetProperty("runtimeCompatibility").GetProperty("ready").GetBoolean() ||
            source.GetProperty("retailOrDerivedAssetsPackaged").GetBoolean() ||
            Fo2TemplePresentationCatalog.RequiredString(sourceDescriptor, "schema") != SourceSchema)
            throw new InvalidOperationException(
                "Unexpected Fallout 2 Arroyo Caves source manifest.");

        var profileDescriptor = cache.GetProperty("sourceProfile");
        var profilePath = Fo2TemplePresentationCatalog.ResolvePath(
            Fo2TemplePresentationCatalog.RequiredString(profileDescriptor, "file"),
            cacheRoot);
        var profileBytes = Fo2TemplePresentationCatalog.VerifyFile(
            profilePath,
            Fo2TemplePresentationCatalog.RequiredHash(profileDescriptor, "sha256"),
            null,
            "Fallout 2 owned profile");
        using (var profileDocument = JsonDocument.Parse(profileBytes))
        {
            var profile = profileDocument.RootElement;
            if (Fo2TemplePresentationCatalog.RequiredString(profile, "schema") != ProfileSchema ||
                Fo2TemplePresentationCatalog.RequiredString(profile, "campaign") != "Fallout2" ||
                Fo2TemplePresentationCatalog.RequiredString(profile, "status") !=
                    "registered-owned-install" ||
                profile.GetProperty("runtimeCompatibility").GetProperty("ready").GetBoolean() ||
                Fo2TemplePresentationCatalog.RequiredString(profile, "sourceProfileId") !=
                    Fo2TemplePresentationCatalog.RequiredString(
                        profileDescriptor,
                        "sourceProfileId"))
                throw new InvalidOperationException(
                    "Fallout 2 Arroyo Caves owned-profile binding drifted.");
        }
        var sourceProfile = source.GetProperty("sourceProfile");
        var sourceProfileId = Fo2TemplePresentationCatalog.RequiredString(
            profileDescriptor,
            "sourceProfileId");
        if (Fo2TemplePresentationCatalog.RequiredString(sourceProfile, "sourceProfileId") !=
                sourceProfileId ||
            Fo2TemplePresentationCatalog.RequiredHash(sourceProfile, "sha256") !=
                Fo2TemplePresentationCatalog.RequiredHash(profileDescriptor, "sha256"))
            throw new InvalidOperationException(
                "Fallout 2 Arroyo Caves source/profile hash chain drifted.");

        var recipeDescriptor = source.GetProperty("recipe");
        var recipePath = Fo2TemplePresentationCatalog.ResolvePath(
            Fo2TemplePresentationCatalog.RequiredString(recipeDescriptor, "file"),
            Path.GetDirectoryName(sourcePath)!);
        var recipeBytes = Fo2TemplePresentationCatalog.VerifyFile(
            recipePath,
            Fo2TemplePresentationCatalog.RequiredHash(recipeDescriptor, "sha256"),
            null,
            "Fallout 2 Arroyo Caves source recipe");
        using (var recipeDocument = JsonDocument.Parse(recipeBytes))
        {
            var recipe = recipeDocument.RootElement;
            if (Fo2TemplePresentationCatalog.RequiredString(recipe, "schema") !=
                    "opennv-fo2-first-slice-recipe/v1" ||
                Fo2TemplePresentationCatalog.RequiredString(recipe, "id") !=
                    Fo2TemplePresentationCatalog.RequiredString(recipeDescriptor, "id") ||
                Fo2TemplePresentationCatalog.RequiredString(recipe, "campaign") != "Fallout2")
                throw new InvalidOperationException(
                    "Fallout 2 Arroyo Caves source-recipe binding drifted.");
        }

        var map = source.GetProperty("map");
        var mapSha256 = Fo2TemplePresentationCatalog.RequiredHash(map, "sha256");
        if (Fo2TemplePresentationCatalog.RequiredHash(sourceDescriptor, "mapSha256") != mapSha256 ||
            Fo2TemplePresentationCatalog.RequiredString(map, "logicalPath") !=
                "maps\\arcaves.map")
            throw new InvalidOperationException("Fallout 2 Arroyo Caves MAP identity drifted.");
        var header = map.GetProperty("header");
        if (header.GetProperty("version").GetInt32() != MapVersion ||
            Fo2TemplePresentationCatalog.RequiredString(header, "name") != "ARCAVES.MAP" ||
            header.GetProperty("mapIndex").GetInt32() != MapIndex)
            throw new InvalidOperationException("Fallout 2 Arroyo Caves MAP header drifted.");
        var registry = source.GetProperty("mapRegistry");
        var registryValues = registry.GetProperty("values");
        if (Fo2TemplePresentationCatalog.RequiredString(registry, "logicalPath") !=
                "data\\maps.txt" ||
            Fo2TemplePresentationCatalog.RequiredString(registry, "section") != "Map 003" ||
            Fo2TemplePresentationCatalog.RequiredString(registryValues, "lookup_name") !=
                "Arroyo Caves" ||
            Fo2TemplePresentationCatalog.RequiredString(registryValues, "map_name") != "arcaves")
            throw new InvalidOperationException(
                "Fallout 2 Arroyo Caves map-registry binding drifted.");

        var entriesByElevation = new Dictionary<int, IReadOnlyList<uint>>();
        foreach (var row in map.GetProperty("layout").GetProperty("elevations").EnumerateArray())
        {
            var elevation = row.GetProperty("elevation").GetInt32();
            var entries = row.GetProperty("rawEntries").EnumerateArray()
                .Select(value => value.GetUInt32()).ToArray();
            if (elevation is < 0 or > 2 || entries.Length != TileEntryCount ||
                !entriesByElevation.TryAdd(elevation, entries))
                throw new InvalidOperationException(
                    "Fallout 2 Arroyo Caves elevation layout drifted.");
        }
        if (!entriesByElevation.Keys.Order().SequenceEqual(new[] { 0, 1, 2 }))
            throw new InvalidOperationException(
                "Fallout 2 Arroyo Caves must retain all three owned elevations.");

        var artifacts = Fo2TemplePresentationCatalog.LoadArtifacts(
            cache.GetProperty("artifacts"),
            cacheRoot);
        var tileBindings = Fo2TemplePresentationCatalog.LoadTileBindings(
            cache.GetProperty("tileBindings"),
            artifacts);
        Fo2TemplePresentationCatalog.VerifyTileBindings(entriesByElevation, tileBindings);
        var sourceObjects = Fo2TemplePresentationCatalog.FlattenObjects(map.GetProperty("objects"));
        var objectPlacements = Fo2TemplePresentationCatalog.LoadObjectPlacements(
            cache.GetProperty("objectBindings"),
            source.GetProperty("frms"),
            artifacts,
            sourceObjects);
        var declaredTopLevel = map.GetProperty("objects")
            .GetProperty("totalTopLevelObjects").GetInt32();
        if (sourceObjects.Values.Count(row => row.TopLevel) != declaredTopLevel ||
            objectPlacements.Count != declaredTopLevel)
            throw new InvalidOperationException(
                "Fallout 2 Arroyo Caves top-level object coverage drifted.");

        var resources = cache.GetProperty("resources").EnumerateArray().ToArray();
        var resourceIdentities = resources.Select(row =>
            $"{Fo2TemplePresentationCatalog.RequiredString(row, "logicalPath")}|" +
            Fo2TemplePresentationCatalog.RequiredHash(row, "sha256"))
            .ToHashSet(StringComparer.Ordinal);
        if (resourceIdentities.Count != resources.Length ||
            !artifacts.Values.All(artifact => resourceIdentities.Contains(
                $"{artifact.LogicalPath}|{artifact.SourceSha256}")))
            throw new InvalidOperationException(
                "Fallout 2 Arroyo Caves artifact/resource identity closure failed.");
        Fo2TemplePresentationCatalog.VerifyCounts(
            cache.GetProperty("counts"),
            artifacts,
            tileBindings,
            objectPlacements,
            source);

        var incoming = source.GetProperty("incomingPlacement");
        var arrivalTile = incoming.GetProperty("tile").GetInt32();
        var arrivalRotation = incoming.GetProperty("rotation").GetInt32();
        if (Fo2TemplePresentationCatalog.RequiredString(incoming, "authority") !=
                "exact Map 126 exit-grid instance values" ||
            incoming.GetProperty("mapIndex").GetInt32() != MapIndex ||
            incoming.GetProperty("elevation").GetInt32() != Elevation ||
            arrivalTile is < 0 or >= HexEntryCount ||
            arrivalRotation is < 0 or >= Fo1HexMath.DirectionCount ||
            incoming.GetProperty("tileX").GetInt32() != arrivalTile % Fo1HexMath.Width ||
            incoming.GetProperty("tileY").GetInt32() != arrivalTile / Fo1HexMath.Width)
            throw new InvalidOperationException(
                "Fallout 2 Arroyo Caves incoming placement drifted.");
        ValidateTransition(source, transition, mapSha256, arrivalTile, arrivalRotation);

        var elevationZeroEntries = entriesByElevation[Elevation].ToArray();
        var floorIds = elevationZeroEntries.Select(entry => (int)(entry & 0x0fff)).ToArray();
        var blocked = sourceObjects.Values
            .Where(row => row.TopLevel && row.Elevation == Elevation && (row.Flags & 0x10) == 0)
            .Select(row => row.Tile)
            .ToHashSet();
        var walkable = Enumerable.Range(0, HexEntryCount)
            .Select(tile =>
                floorIds[Fo1HexMath.FloorIndex(tile)] != DefaultFloorTileId &&
                !blocked.Contains(tile))
            .ToArray();
        if (!walkable[arrivalTile])
            throw new InvalidOperationException(
                "Fallout 2 Arroyo Caves incoming placement is not source-walkable.");
        var walkMaskSha256 = Fo2TempleMovementConsumer.MaskSha256(walkable);
        var arrivalComponentHexes = ReachableCount(arrivalTile, walkable);
        var walkContract = source.GetProperty("arrivalWalkContract");
        if (Fo2TemplePresentationCatalog.RequiredString(walkContract, "semantics") !=
                "non-default-floor-art-minus-central-source-blocking-object-hexes-v1" ||
            walkContract.GetProperty("multihexExpansionImplemented").GetBoolean() ||
            Fo2TemplePresentationCatalog.RequiredHash(walkContract, "walkMaskSha256") !=
                walkMaskSha256 ||
            walkContract.GetProperty("walkableHexes").GetInt32() != walkable.Count(row => row) ||
            walkContract.GetProperty("entryComponentHexes").GetInt32() !=
                arrivalComponentHexes)
            throw new InvalidOperationException(
                "Fallout 2 Arroyo Caves arrival walk contract drifted.");

        return new Fo2ArroyoCavesPresentationCatalog(
            manifestPath,
            Fo2TemplePresentationCatalog.Sha256(cacheBytes),
            sourcePath,
            Fo2TemplePresentationCatalog.Sha256(sourceBytes),
            sourceProfileId,
            mapSha256,
            arrivalTile,
            arrivalRotation,
            elevationZeroEntries,
            artifacts,
            tileBindings,
            objectPlacements,
            resources.Length,
            walkMaskSha256,
            walkable.Count(row => row),
            arrivalComponentHexes,
            transition.ManifestSha256);
    }

    private static void ValidateTransition(
        JsonElement source,
        Fo2TempleTransitionCatalog transition,
        string mapSha256,
        int arrivalTile,
        int arrivalRotation)
    {
        var descriptor = source.GetProperty("sourceTransition");
        var path = Fo2TemplePresentationCatalog.ResolvePath(
            Fo2TemplePresentationCatalog.RequiredString(descriptor, "file"),
            Path.GetDirectoryName(transition.ManifestPath)!);
        var sha256 = Fo2TemplePresentationCatalog.RequiredHash(descriptor, "sha256");
        if (Fo2TemplePresentationCatalog.RequiredString(descriptor, "schema") !=
                "opennv-fo2-temple-transitions/v1" ||
            sha256 != transition.ManifestSha256 ||
            Fo2TemplePresentationCatalog.Sha256(
                Fo2TemplePresentationCatalog.VerifyFile(
                    path,
                    sha256,
                    null,
                    "Fallout 2 Temple transition manifest")) != sha256 ||
            !transition.DestinationMaps.TryGetValue(MapIndex, out var destination) ||
            destination.MapIndex != MapIndex ||
            destination.MapName != "arcaves" ||
            destination.LookupName != "Arroyo Caves" ||
            destination.LogicalPath != "maps\\arcaves.map" ||
            destination.Sha256 != mapSha256 ||
            !destination.PresentElevations.Contains(Elevation))
            throw new InvalidOperationException(
                "Fallout 2 Temple-to-Arroyo destination identity drifted.");
        var declaredSerials = descriptor.GetProperty("incomingExitSerials")
            .EnumerateArray().Select(row => row.GetInt32()).Order().ToArray();
        var exactExits = transition.Exits.Where(row =>
                row.TargetMapIndex == MapIndex &&
                row.TargetTile == arrivalTile &&
                row.TargetElevation == Elevation &&
                row.TargetRotation == arrivalRotation)
            .OrderBy(row => row.Serial)
            .ToArray();
        if (exactExits.Length == 0 ||
            !declaredSerials.SequenceEqual(exactExits.Select(row => row.Serial)))
            throw new InvalidOperationException(
                "Fallout 2 Temple-to-Arroyo incoming exit set drifted.");
    }

    private static int ReachableCount(int start, IReadOnlyList<bool> walkable)
    {
        var visited = new HashSet<int> { start };
        var queue = new Queue<int>();
        queue.Enqueue(start);
        while (queue.Count > 0)
            foreach (var neighbor in Fo1HexMath.Neighbors(queue.Dequeue()))
                if (walkable[neighbor] && visited.Add(neighbor))
                    queue.Enqueue(neighbor);
        return visited.Count;
    }
}
