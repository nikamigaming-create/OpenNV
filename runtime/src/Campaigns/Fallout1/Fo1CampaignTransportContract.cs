using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;

namespace OpenNV.Runtime.Campaigns.Fallout1;

internal static class Fo1CampaignTransportContractNumericContracts
{
    // Immutable format, source-art, geometry, and acceptance contracts.
    // Runtime-tunable Fallout 1 behavior remains in the versioned runtime recipe.
    internal const int PresentationInt16 = 16;
    internal const int PresentationInt5 = 5;
    internal const int PresentationInt64 = 64;
    internal const int PresentationInt8 = 8;
}

internal static class Fo1CampaignTransportContract
{
    private const string CampaignSchema = "opennv-fo1-campaign-transport/v1";
    private const string MapSchema = "opennv-fo1-campaign-map-transport/v1";

    internal static Fo1CampaignTransportCoverage Load(string path)
    {
        var campaignPath = Path.GetFullPath(path);
        var campaignBytes = File.ReadAllBytes(campaignPath);
        var campaignSha256 = Sha256(campaignBytes);
        using var campaignDocument = JsonDocument.Parse(campaignBytes);
        var root = campaignDocument.RootElement;
        if (RequiredString(root, "schema") != CampaignSchema ||
            RequiredString(root, "status") != "transported-not-rendered" ||
            root.GetProperty("retailOrDerivedAssetsPackaged").GetBoolean())
            throw new InvalidOperationException("Unexpected Fallout campaign transport contract.");

        var campaignRoot = Path.GetDirectoryName(campaignPath)!;
        var mapRows = root.GetProperty("maps").EnumerateArray().ToArray();
        var mapIds = new HashSet<string>(StringComparer.Ordinal);
        var mapPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var mapCoverage = new List<Fo1CampaignMapTransportCoverage>(mapRows.Length);
        var totalElevations = 0;
        var totalTopLevelObjects = 0;
        var totalObjectsIncludingInventory = 0;
        var totalDoors = 0;
        var totalLiveScripts = 0;
        var totalCompactObjects = 0;
        foreach (var mapRow in mapRows)
        {
            var id = RequiredString(mapRow, "id");
            if (!mapIds.Add(id))
                throw new InvalidOperationException($"Duplicate Fallout campaign map ID: {id}");
            var mapPath = ResolveChildPath(
                campaignRoot,
                RequiredString(mapRow, "path"));
            if (!mapPaths.Add(mapPath))
                throw new InvalidOperationException($"Duplicate Fallout campaign map path: {mapPath}");
            var expectedMapSha256 = Hash(mapRow, "sha256");
            var mapBytes = File.ReadAllBytes(mapPath);
            if (Sha256(mapBytes) != expectedMapSha256)
                throw new InvalidOperationException($"Fallout campaign map hash drifted: {id}");
            using var mapDocument = JsonDocument.Parse(mapBytes);
            var coverage = ValidateMap(id, mapRow, mapDocument.RootElement);
            mapCoverage.Add(coverage);
            totalElevations += coverage.Elevations;
            totalTopLevelObjects += coverage.TopLevelObjects;
            totalObjectsIncludingInventory += coverage.ObjectsIncludingInventory;
            totalDoors += coverage.Doors;
            totalLiveScripts += coverage.LiveScripts;
            totalCompactObjects += coverage.CompactObjects;
        }

        var resourceRows = root.GetProperty("resources").EnumerateArray().ToArray();
        var resourcePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in resourceRows)
        {
            var logicalPath = RequiredString(row, "logicalPath");
            if (!resourcePaths.Add(logicalPath))
                throw new InvalidOperationException(
                    $"Duplicate Fallout campaign resource identity: {logicalPath}");
            _ = RequiredString(row, "source");
            _ = Hash(row, "sha256");
            if (row.GetProperty("bytes").GetInt64() <= 0)
                throw new InvalidOperationException(
                    $"Fallout campaign resource has no source bytes: {logicalPath}");
        }

        var coverageRow = root.GetProperty("coverage");
        RequireCount(coverageRow, "mapFiles", mapCoverage.Count);
        RequireCount(coverageRow, "mapContracts", mapCoverage.Count);
        RequireCount(coverageRow, "presentElevations", totalElevations);
        RequireCount(coverageRow, "topLevelObjects", totalTopLevelObjects);
        RequireCount(coverageRow, "doors", totalDoors);
        RequireCount(coverageRow, "liveScripts", totalLiveScripts);
        RequireCount(coverageRow, "uniqueResources", resourceRows.Length);
        var promotion = root.GetProperty("promotion");
        RequireCount(promotion, "transportedMaps", mapCoverage.Count);
        foreach (var unpromoted in new[]
                 {
                     "renderedMaps",
                     "interactiveMaps",
                     "questExecutableMaps",
                     "firstPersonReadyMaps",
                     "openXrAcceptedMaps",
                 })
            RequireCount(promotion, unpromoted, 0);

        return new Fo1CampaignTransportCoverage(
            campaignPath,
            campaignSha256,
            mapCoverage,
            totalElevations,
            totalTopLevelObjects,
            totalObjectsIncludingInventory,
            totalDoors,
            totalLiveScripts,
            resourceRows.Length,
            totalCompactObjects);
    }

    private static Fo1CampaignMapTransportCoverage ValidateMap(
        string expectedId,
        JsonElement catalogRow,
        JsonElement root)
    {
        if (RequiredString(root, "schema") != MapSchema ||
            RequiredString(root, "status") != "transported" ||
            RequiredString(root, "id") != expectedId ||
            root.GetProperty("retailOrDerivedAssetsPackaged").GetBoolean())
            throw new InvalidOperationException(
                $"Unexpected Fallout campaign map transport contract: {expectedId}");
        var sourceMap = root.GetProperty("source").GetProperty("map");
        if (Hash(sourceMap, "sha256") != Hash(catalogRow, "sourceMapSha256"))
            throw new InvalidOperationException(
                $"Fallout source MAP identity disagrees with its campaign row: {expectedId}");

        var header = root.GetProperty("header");
        var entry = root.GetProperty("entry");
        var entryTile = entry.GetProperty("tile").GetInt32();
        var entryElevation = entry.GetProperty("elevation").GetInt32();
        var entryRotation = entry.GetProperty("rotation").GetInt32();
        if (entryTile is < 0 or >= Fo1HexMath.Width * Fo1HexMath.Height ||
            entryElevation is < 0 or > 2 || entryRotation is < 0 or >= Fo1HexMath.DirectionCount ||
            entryTile != header.GetProperty("enteringTile").GetInt32() ||
            entryElevation != header.GetProperty("enteringElevation").GetInt32() ||
            entryRotation != header.GetProperty("enteringRotation").GetInt32())
            throw new InvalidOperationException(
                $"Fallout campaign MAP-header entry is invalid: {expectedId}");

        var layout = root.GetProperty("layout");
        var elevationRows = layout.GetProperty("elevations").EnumerateArray().ToArray();
        var presentElevations = layout.GetProperty("presentElevations")
            .EnumerateArray().Select(value => value.GetInt32()).ToArray();
        if (elevationRows.Length == 0 ||
            !presentElevations.SequenceEqual(
                elevationRows.Select(row => row.GetProperty("elevation").GetInt32())) ||
            presentElevations.Distinct().Count() != presentElevations.Length ||
            !presentElevations.Contains(entryElevation))
            throw new InvalidOperationException(
                $"Fallout campaign elevation coverage is invalid: {expectedId}");
        foreach (var elevationRow in elevationRows)
            ValidateElevation(expectedId, elevationRow);

        var objectGraph = root.GetProperty("objectGraph");
        var scriptRows = objectGraph.GetProperty("scriptLists").EnumerateArray().ToArray();
        if (scriptRows.Length != Fo1CampaignTransportContractNumericContracts.PresentationInt5)
            throw new InvalidOperationException(
                $"Fallout campaign script-list coverage is invalid: {expectedId}");
        var liveScripts = scriptRows.Sum(row => row.GetProperty("liveCount").GetInt32());
        var objectRows = objectGraph.GetProperty("objects").GetProperty("elevations")
            .EnumerateArray().ToArray();
        if (objectRows.Length != 3)
            throw new InvalidOperationException(
                $"Fallout campaign object-elevation coverage is invalid: {expectedId}");
        var topLevelObjects = 0;
        var objectsIncludingInventory = 0;
        var compactObjects = 0;
        foreach (var elevation in objectRows)
        {
            var objects = elevation.GetProperty("objects").EnumerateArray().ToArray();
            RequireCount(elevation, "count", objects.Length);
            topLevelObjects += objects.Length;
            foreach (var row in objects)
                ValidateObject(expectedId, row, ref objectsIncludingInventory, ref compactObjects);
        }
        RequireCount(
            objectGraph.GetProperty("objects"),
            "totalTopLevelObjects",
            topLevelObjects);
        var doors = objectGraph.GetProperty("doors").GetArrayLength();
        var resourceCount = root.GetProperty("resources").GetArrayLength();
        RequireCatalogCount(catalogRow, "presentElevations", elevationRows.Length);
        RequireCount(catalogRow, "topLevelObjects", topLevelObjects);
        RequireCount(catalogRow, "doors", doors);
        RequireCount(catalogRow, "liveScripts", liveScripts);
        RequireCount(catalogRow, "resources", resourceCount);
        return new Fo1CampaignMapTransportCoverage(
            expectedId,
            elevationRows.Length,
            topLevelObjects,
            objectsIncludingInventory,
            doors,
            liveScripts,
            resourceCount,
            compactObjects,
            entryTile,
            entryElevation,
            entryRotation);
    }

    private static void ValidateElevation(string mapId, JsonElement row)
    {
        if (row.GetProperty("width").GetInt32() != Fo1HexMath.FloorWidth ||
            row.GetProperty("height").GetInt32() != Fo1HexMath.FloorHeight)
            throw new InvalidOperationException(
                $"Fallout floor-grid dimensions drifted: {mapId}");
        var entries = row.GetProperty("rawEntries").EnumerateArray().ToArray();
        var expectedEntries = Fo1HexMath.FloorWidth * Fo1HexMath.FloorHeight;
        if (entries.Length != expectedEntries ||
            row.GetProperty("entryCount").GetInt32() != expectedEntries)
            throw new InvalidOperationException(
                $"Fallout floor-grid entry count drifted: {mapId}");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> encoded = stackalloc byte[sizeof(uint)];
        var nonDefaultFloors = 0;
        var nonDefaultRoofs = 0;
        foreach (var entry in entries)
        {
            var value = entry.GetUInt32();
            BinaryPrimitives.WriteUInt32BigEndian(encoded, value);
            hash.AppendData(encoded);
            if ((value & 0x0FFF) != 1)
                nonDefaultFloors++;
            if (((value >> Fo1CampaignTransportContractNumericContracts.PresentationInt16) & 0x0FFF) != 1)
                nonDefaultRoofs++;
        }
        var actualRawSha256 = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        if (actualRawSha256 != Hash(row, "rawSha256") ||
            row.GetProperty("nonDefaultFloorCount").GetInt32() != nonDefaultFloors ||
            row.GetProperty("nonDefaultRoofCount").GetInt32() != nonDefaultRoofs)
            throw new InvalidOperationException(
                $"Fallout elevation raw-grid contract drifted: {mapId}");
    }

    private static void ValidateObject(
        string mapId,
        JsonElement row,
        ref int objectCount,
        ref int compactCount)
    {
        objectCount++;
        var layout = RequiredString(row, "baseLayout");
        var compact = layout == "compact-17";
        if (!compact && layout != "full-21")
            throw new InvalidOperationException(
                $"Fallout object base layout is unsupported: {mapId}/{layout}");
        if (compact)
        {
            compactCount++;
            if (row.GetProperty("cachedScreen").ValueKind != JsonValueKind.Null ||
                RequiredString(row, "frameSource") != "implicit-zero-compact-layout" ||
                RequiredString(row, "rotationSource") != "implicit-zero-compact-layout")
                throw new InvalidOperationException(
                    $"Fallout compact object defaults are not explicit: {mapId}");
        }
        else if (row.GetProperty("cachedScreen").GetArrayLength() != 2 ||
                 RequiredString(row, "frameSource") != "stored" ||
                 RequiredString(row, "rotationSource") != "stored")
            throw new InvalidOperationException(
                $"Fallout full object transport is incomplete: {mapId}");
        var tile = row.GetProperty("tile").GetInt32();
        var rotation = row.GetProperty("rotation").GetInt32();
        if (tile is < -1 or >= Fo1HexMath.Width * Fo1HexMath.Height ||
            rotation is < 0 or >= Fo1HexMath.DirectionCount)
            throw new InvalidOperationException(
                $"Fallout object placement is invalid: {mapId}");
        _ = HashLikeId(row, "fid");
        _ = HashLikeId(row, "pid");
        foreach (var inventoryRow in row.GetProperty("inventory").EnumerateArray())
        {
            if (inventoryRow.GetProperty("quantity").GetInt32() <= 0)
                throw new InvalidOperationException(
                    $"Fallout inventory quantity is invalid: {mapId}");
            ValidateObject(
                mapId,
                inventoryRow.GetProperty("object"),
                ref objectCount,
                ref compactCount);
        }
    }

    private static string ResolveChildPath(string root, string relativePath)
    {
        if (Path.IsPathRooted(relativePath))
            throw new InvalidOperationException(
                $"Fallout campaign map path must be relative: {relativePath}");
        var fullPath = Path.GetFullPath(
            Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var relative = Path.GetRelativePath(root, fullPath);
        if (relative == ".." || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Fallout campaign map path escapes its cache: {relativePath}");
        return fullPath;
    }

    private static string RequiredString(JsonElement source, string name)
    {
        var value = source.GetProperty(name).GetString();
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException(
                $"Fallout campaign string is empty: {name}");
        return value;
    }

    private static string Hash(JsonElement source, string name)
    {
        var value = RequiredString(source, name);
        if (value.Length != Fo1CampaignTransportContractNumericContracts.PresentationInt64 || value.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidOperationException(
                $"Fallout campaign SHA-256 is invalid: {name}");
        return value.ToLowerInvariant();
    }

    private static string HashLikeId(JsonElement source, string name)
    {
        var value = RequiredString(source, name);
        if (value.Length != Fo1CampaignTransportContractNumericContracts.PresentationInt8 || value.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidOperationException(
                $"Fallout campaign object ID is invalid: {name}");
        return value.ToLowerInvariant();
    }

    private static string Sha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static void RequireCount(JsonElement source, string name, int expected)
    {
        if (source.GetProperty(name).GetInt32() != expected)
            throw new InvalidOperationException(
                $"Fallout campaign count drifted: {name}");
    }

    private static void RequireCatalogCount(JsonElement source, string name, int expected)
    {
        if (source.GetProperty(name).GetArrayLength() != expected)
            throw new InvalidOperationException(
                $"Fallout campaign catalog array count drifted: {name}");
    }
}

internal sealed record Fo1CampaignTransportCoverage(
    string CampaignPath,
    string CampaignSha256,
    IReadOnlyList<Fo1CampaignMapTransportCoverage> MapCoverage,
    int Elevations,
    int TopLevelObjects,
    int ObjectsIncludingInventory,
    int Doors,
    int LiveScripts,
    int Resources,
    int CompactObjects)
{
    internal object Report() => new
    {
        schema = "opennv-fo1-campaign-runtime-proof/v1",
        status = "pass-transported-not-rendered",
        campaign = CampaignPath,
        campaignSha256 = CampaignSha256,
        maps = MapCoverage.Count,
        elevations = Elevations,
        topLevelObjects = TopLevelObjects,
        objectsIncludingInventory = ObjectsIncludingInventory,
        doors = Doors,
        liveScripts = LiveScripts,
        resources = Resources,
        compactObjects = CompactObjects,
        mapCoverage = MapCoverage,
        promotion = new
        {
            transportedMaps = MapCoverage.Count,
            renderedMaps = 0,
            interactiveMaps = 0,
            questExecutableMaps = 0,
            firstPersonReadyMaps = 0,
            openXrAcceptedMaps = 0,
        },
        windowsAppControlUsed = false,
        foregroundInputInjected = false,
    };
}

internal sealed record Fo1CampaignMapTransportCoverage(
    string Id,
    int Elevations,
    int TopLevelObjects,
    int ObjectsIncludingInventory,
    int Doors,
    int LiveScripts,
    int Resources,
    int CompactObjects,
    int EntryTile,
    int EntryElevation,
    int EntryRotation);
