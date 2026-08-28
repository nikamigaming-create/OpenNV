using System.Security.Cryptography;
using System.Text.Json;
using Godot;

namespace OpenNV.Runtime.Campaigns.Fallout2.Temple;

internal sealed record Fo2TempleArtifact(
    string Id,
    string Kind,
    string LogicalPath,
    string Path,
    string SourceSha256,
    string PngSha256,
    long PngBytes,
    int Width,
    int Height,
    int Rotation,
    int Frame,
    Vector2I DirectionOffset,
    Vector2I FrameOffset);

internal sealed record Fo2TempleTileUse(int Elevation, string Role, int Count);

internal sealed record Fo2TempleTileBinding(
    int Id,
    string ArtifactId,
    IReadOnlyList<Fo2TempleTileUse> Uses);

internal sealed record Fo2TempleObjectPlacement(
    int Serial,
    int ObjectId,
    string Fid,
    string Pid,
    int Tile,
    int Elevation,
    int Rotation,
    int Frame,
    uint Flags,
    int ObjectType,
    int? PrototypeSubtype,
    string? ArtFilename,
    IReadOnlyList<int> InstanceValues,
    string Sid,
    int ScriptIndex,
    Vector2I PixelOffset,
    bool TopLevel,
    string ArtifactId,
    string LogicalPath)
{
    internal bool Blocking(uint noBlockFlag) => (Flags & noBlockFlag) == 0;
}

internal sealed class Fo2TemplePresentationCatalog
{
    private const string CacheSchema = "opennv-fo2-temple-presentation-cache/v1";
    private const string SourceSchema = "opennv-fo2-first-slice/v1";
    private const string ProfileSchema = "opennv-fo2-owned-profile/v1";
    internal const int MapIndex = 126;
    private const int DefaultTileId = 1;
    private const int TileEntryCount = 10000;
    private const int TileIdMask = 0x0fff;
    private const int RoofIdShift = 16;
    private const int MapVersion = 20;
    private const int Sha256HexCharacters = 64;

    private Fo2TemplePresentationCatalog(
        string manifestPath,
        string manifestSha256,
        string sourceManifestPath,
        string sourceManifestSha256,
        string sourceProfileId,
        string mapSha256,
        int entryTile,
        int entryElevation,
        int entryRotation,
        uint[] tileEntries,
        IReadOnlyDictionary<string, Fo2TempleArtifact> artifacts,
        IReadOnlyDictionary<int, Fo2TempleTileBinding> tileBindings,
        IReadOnlyList<Fo2TempleObjectPlacement> objectPlacements,
        int inventoryObjects,
        int verifiedResources)
    {
        ManifestPath = manifestPath;
        ManifestSha256 = manifestSha256;
        SourceManifestPath = sourceManifestPath;
        SourceManifestSha256 = sourceManifestSha256;
        SourceProfileId = sourceProfileId;
        MapSha256 = mapSha256;
        EntryTile = entryTile;
        EntryElevation = entryElevation;
        EntryRotation = entryRotation;
        TileEntries = tileEntries;
        Artifacts = artifacts;
        TileBindings = tileBindings;
        ObjectPlacements = objectPlacements;
        InventoryObjects = inventoryObjects;
        VerifiedResources = verifiedResources;
    }

    internal string ManifestPath { get; }
    internal string ManifestSha256 { get; }
    internal string SourceManifestPath { get; }
    internal string SourceManifestSha256 { get; }
    internal string SourceProfileId { get; }
    internal string MapSha256 { get; }
    internal int EntryTile { get; }
    internal int EntryElevation { get; }
    internal int EntryRotation { get; }
    internal int DefaultFloorTileId => DefaultTileId;
    internal uint[] TileEntries { get; }
    internal IReadOnlyDictionary<string, Fo2TempleArtifact> Artifacts { get; }
    internal IReadOnlyDictionary<int, Fo2TempleTileBinding> TileBindings { get; }
    internal IReadOnlyList<Fo2TempleObjectPlacement> ObjectPlacements { get; }
    internal int InventoryObjects { get; }
    internal int VerifiedResources { get; }

    internal static Fo2TemplePresentationCatalog Load(string cacheManifestPath)
    {
        var manifestPath = ResolvePath(cacheManifestPath, Directory.GetCurrentDirectory());
        var cacheBytes = File.ReadAllBytes(manifestPath);
        var cacheSha256 = Sha256(cacheBytes);
        using var cacheDocument = JsonDocument.Parse(cacheBytes);
        var cache = cacheDocument.RootElement;
        if (RequiredString(cache, "schema") != CacheSchema ||
            RequiredString(cache, "status") != "decoded-disposable-local-cache" ||
            RequiredString(cache, "campaign") != "Fallout2" ||
            RequiredString(cache, "slice") != "TempleOfTrials" ||
            cache.GetProperty("runtimeCompatibility").GetProperty("ready").GetBoolean() ||
            cache.GetProperty("retailOrDerivedAssetsPackaged").GetBoolean() ||
            cache.GetProperty("cachePolicy").GetProperty("distributionAllowed").GetBoolean() ||
            !cache.GetProperty("cachePolicy").GetProperty("containsDerivedOwnedPixels").GetBoolean())
            throw new InvalidOperationException("Unexpected Fallout 2 Temple presentation cache.");
        var cacheRoot = Path.GetDirectoryName(manifestPath)!;

        var sourceDescriptor = cache.GetProperty("sourceManifest");
        var sourcePath = ResolvePath(RequiredString(sourceDescriptor, "file"), cacheRoot);
        var sourceBytes = VerifyFile(
            sourcePath,
            RequiredHash(sourceDescriptor, "sha256"),
            expectedBytes: null,
            "Fallout 2 Temple source manifest");
        using var sourceDocument = JsonDocument.Parse(sourceBytes);
        var source = sourceDocument.RootElement;
        if (RequiredString(source, "schema") != SourceSchema ||
            RequiredString(source, "status") != "transported-source-manifest" ||
            RequiredString(source, "campaign") != "Fallout2" ||
            RequiredString(source, "slice") != "TempleOfTrials" ||
            source.GetProperty("runtimeCompatibility").GetProperty("ready").GetBoolean() ||
            source.GetProperty("retailOrDerivedAssetsPackaged").GetBoolean())
            throw new InvalidOperationException("Unexpected Fallout 2 Temple source manifest.");
        if (RequiredString(sourceDescriptor, "schema") != SourceSchema)
            throw new InvalidOperationException("Fallout 2 Temple source schema binding drifted.");

        var profileDescriptor = cache.GetProperty("sourceProfile");
        var profilePath = ResolvePath(RequiredString(profileDescriptor, "file"), cacheRoot);
        var profileBytes = VerifyFile(
            profilePath,
            RequiredHash(profileDescriptor, "sha256"),
            expectedBytes: null,
            "Fallout 2 owned profile");
        using (var profileDocument = JsonDocument.Parse(profileBytes))
        {
            var profile = profileDocument.RootElement;
            if (RequiredString(profile, "schema") != ProfileSchema ||
                RequiredString(profile, "campaign") != "Fallout2" ||
                RequiredString(profile, "status") != "registered-owned-install" ||
                profile.GetProperty("runtimeCompatibility").GetProperty("ready").GetBoolean() ||
                RequiredString(profile, "sourceProfileId") !=
                    RequiredString(profileDescriptor, "sourceProfileId"))
                throw new InvalidOperationException("Fallout 2 owned profile binding drifted.");
        }
        var sourceProfile = source.GetProperty("sourceProfile");
        if (RequiredString(sourceProfile, "sourceProfileId") !=
                RequiredString(profileDescriptor, "sourceProfileId") ||
            RequiredHash(sourceProfile, "sha256") != RequiredHash(profileDescriptor, "sha256"))
            throw new InvalidOperationException("Fallout 2 source/profile hash chain drifted.");

        var recipeDescriptor = source.GetProperty("recipe");
        var recipePath = ResolvePath(RequiredString(recipeDescriptor, "file"), Path.GetDirectoryName(sourcePath)!);
        var recipeBytes = VerifyFile(
            recipePath,
            RequiredHash(recipeDescriptor, "sha256"),
            expectedBytes: null,
            "Fallout 2 Temple recipe");
        using (var recipeDocument = JsonDocument.Parse(recipeBytes))
        {
            var recipe = recipeDocument.RootElement;
            if (RequiredString(recipe, "schema") != "opennv-fo2-first-slice-recipe/v1" ||
                RequiredString(recipe, "id") != RequiredString(recipeDescriptor, "id") ||
                RequiredString(recipe, "campaign") != "Fallout2")
                throw new InvalidOperationException("Fallout 2 Temple recipe binding drifted.");
        }

        var map = source.GetProperty("map");
        var mapSha256 = RequiredHash(map, "sha256");
        if (RequiredString(sourceDescriptor, "mapSha256") != mapSha256 ||
            RequiredString(map, "logicalPath") != "maps\\artemple.map")
            throw new InvalidOperationException("Fallout 2 Temple MAP identity drifted.");
        var header = map.GetProperty("header");
        if (header.GetProperty("version").GetInt32() != MapVersion ||
            RequiredString(header, "name") != "ARTEMPLE.MAP" ||
            header.GetProperty("mapIndex").GetInt32() != MapIndex ||
            header.GetProperty("enteringElevation").GetInt32() != 0)
            throw new InvalidOperationException("Fallout 2 Temple MAP header drifted.");
        var registry = source.GetProperty("mapRegistry");
        var registryValues = registry.GetProperty("values");
        if (RequiredString(registry, "logicalPath") != "data\\maps.txt" ||
            RequiredString(registry, "section") != "Map 126" ||
            RequiredString(registryValues, "lookup_name") != "Arroyo Temple" ||
            RequiredString(registryValues, "map_name") != "artemple")
            throw new InvalidOperationException("Fallout 2 Map 126 registry binding drifted.");
        var start = source.GetProperty("newGameStart");
        var entry = start.GetProperty("playerEntry");
        var entryTile = entry.GetProperty("tile").GetInt32();
        var entryElevation = entry.GetProperty("elevation").GetInt32();
        var entryRotation = entry.GetProperty("rotation").GetInt32();
        if (start.GetProperty("mapIndex").GetInt32() != MapIndex ||
            RequiredString(start, "lookupName") != "Arroyo Temple" ||
            RequiredString(start, "mapName") != "artemple" ||
            entryTile != header.GetProperty("enteringTile").GetInt32() ||
            entryElevation != header.GetProperty("enteringElevation").GetInt32() ||
            entryRotation != header.GetProperty("enteringRotation").GetInt32() ||
            entry.GetProperty("placedPlayerObject").GetBoolean())
            throw new InvalidOperationException("Fallout 2 Temple entry binding drifted.");

        var elevations = map.GetProperty("layout").GetProperty("elevations")
            .EnumerateArray().ToArray();
        if (elevations.Length != 1 || elevations[0].GetProperty("elevation").GetInt32() != 0)
            throw new InvalidOperationException("Fallout 2 Temple proof admits elevation zero only.");
        var tileEntries = elevations[0].GetProperty("rawEntries").EnumerateArray()
            .Select(row => row.GetUInt32()).ToArray();
        if (tileEntries.Length != TileEntryCount)
            throw new InvalidOperationException(
                $"Fallout 2 Temple elevation has {tileEntries.Length} tiles, expected {TileEntryCount}.");

        var artifacts = LoadArtifacts(cache.GetProperty("artifacts"), cacheRoot);
        var tileBindings = LoadTileBindings(cache.GetProperty("tileBindings"), artifacts);
        VerifyTileBindings(tileEntries, tileBindings);
        var objectRows = FlattenObjects(map.GetProperty("objects"));
        var objectPlacements = LoadObjectPlacements(
            cache.GetProperty("objectBindings"),
            source.GetProperty("frms"),
            artifacts,
            objectRows);
        var topLevelObjects = objectRows.Values.Count(row => row.TopLevel);
        var declaredTopLevel = map.GetProperty("objects").GetProperty("totalTopLevelObjects").GetInt32();
        if (topLevelObjects != declaredTopLevel || objectPlacements.Count != declaredTopLevel)
            throw new InvalidOperationException(
                "Fallout 2 Temple top-level object placement coverage drifted.");

        var resources = cache.GetProperty("resources").EnumerateArray().ToArray();
        var resourceIdentities = resources.Select(row =>
            $"{RequiredString(row, "logicalPath")}|{RequiredHash(row, "sha256")}").ToHashSet(StringComparer.Ordinal);
        if (resourceIdentities.Count != resources.Length ||
            !artifacts.Values.All(artifact => resourceIdentities.Contains(
                $"{artifact.LogicalPath}|{artifact.SourceSha256}")))
            throw new InvalidOperationException("Fallout 2 Temple artifact/resource identity closure failed.");
        VerifyCounts(cache.GetProperty("counts"), artifacts, tileBindings, objectPlacements, source);

        return new Fo2TemplePresentationCatalog(
            manifestPath,
            cacheSha256,
            sourcePath,
            Sha256(sourceBytes),
            RequiredString(profileDescriptor, "sourceProfileId"),
            mapSha256,
            entryTile,
            entryElevation,
            entryRotation,
            tileEntries,
            artifacts,
            tileBindings,
            objectPlacements,
            objectRows.Values.Count(row => !row.TopLevel),
            resources.Length);
    }

    private static Dictionary<string, Fo2TempleArtifact> LoadArtifacts(
        JsonElement source,
        string cacheRoot)
    {
        var artifacts = new Dictionary<string, Fo2TempleArtifact>(StringComparer.Ordinal);
        foreach (var row in source.EnumerateArray())
        {
            var id = RequiredString(row, "id");
            var kind = RequiredString(row, "kind");
            if (kind is not "tiles" and not "objects")
                throw new InvalidOperationException($"Unsupported Fallout 2 Temple artifact kind: {kind}");
            var relativePath = RequiredString(row, "png");
            if (Path.IsPathRooted(relativePath))
                throw new InvalidOperationException("Fallout 2 Temple PNG path must be cache-relative.");
            var path = Path.GetFullPath(Path.Combine(cacheRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!path.StartsWith(cacheRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Fallout 2 Temple PNG path escapes its cache.");
            var expectedBytes = row.GetProperty("pngBytes").GetInt64();
            VerifyFile(path, RequiredHash(row, "pngSha256"), expectedBytes, $"Fallout 2 Temple PNG {id}");
            var artifact = new Fo2TempleArtifact(
                id,
                kind,
                RequiredString(row, "logicalPath"),
                path,
                RequiredHash(row, "sourceSha256"),
                RequiredHash(row, "pngSha256"),
                expectedBytes,
                row.GetProperty("width").GetInt32(),
                row.GetProperty("height").GetInt32(),
                row.GetProperty("rotation").GetInt32(),
                row.GetProperty("frame").GetInt32(),
                ReadVector2I(row.GetProperty("directionOffset")),
                ReadVector2I(row.GetProperty("frameOffset")));
            if (artifact.Width <= 0 || artifact.Height <= 0 ||
                !artifacts.TryAdd(id, artifact))
                throw new InvalidOperationException($"Invalid or duplicate Fallout 2 Temple artifact: {id}");
        }
        return artifacts;
    }

    private static Dictionary<int, Fo2TempleTileBinding> LoadTileBindings(
        JsonElement source,
        IReadOnlyDictionary<string, Fo2TempleArtifact> artifacts)
    {
        var bindings = new Dictionary<int, Fo2TempleTileBinding>();
        foreach (var row in source.EnumerateArray())
        {
            var id = row.GetProperty("id").GetInt32();
            var artifactId = RequiredString(row, "artifactId");
            if (!artifacts.TryGetValue(artifactId, out var artifact) || artifact.Kind != "tiles")
                throw new InvalidOperationException($"Fallout 2 Temple tile artifact is absent: {artifactId}");
            var uses = row.GetProperty("uses").EnumerateArray().Select(use =>
                new Fo2TempleTileUse(
                    use.GetProperty("elevation").GetInt32(),
                    RequiredString(use, "role"),
                    use.GetProperty("count").GetInt32())).ToArray();
            if (!bindings.TryAdd(
                    id,
                    new Fo2TempleTileBinding(
                        id,
                        artifactId,
                        uses)))
                throw new InvalidOperationException($"Duplicate Fallout 2 Temple tile binding: {id}");
        }
        return bindings;
    }

    private static void VerifyTileBindings(
        IReadOnlyList<uint> entries,
        IReadOnlyDictionary<int, Fo2TempleTileBinding> bindings)
    {
        var expected = new Dictionary<(int Id, string Role), int>();
        foreach (var entry in entries)
        {
            Increment((int)(entry & TileIdMask), "floor");
            Increment((int)((entry >> RoofIdShift) & TileIdMask), "roof");
        }
        var actual = bindings.Values.SelectMany(binding => binding.Uses.Select(use =>
            (Key: (binding.Id, use.Role), use.Elevation, use.Count))).ToArray();
        if (actual.Any(row => row.Elevation != 0 || row.Count <= 0 ||
                row.Key.Role is not "floor" and not "roof") ||
            actual.Select(row => row.Key).Distinct().Count() != actual.Length ||
            expected.Count != actual.Length ||
            actual.Any(row => !expected.TryGetValue(row.Key, out var count) || count != row.Count))
            throw new InvalidOperationException("Fallout 2 Temple floor/roof tile binding drifted.");

        void Increment(int id, string role)
        {
            var key = (id, role);
            expected[key] = expected.GetValueOrDefault(key) + 1;
        }
    }

    private static Dictionary<int, SourceObject> FlattenObjects(JsonElement source)
    {
        var rows = new Dictionary<int, SourceObject>();
        foreach (var elevation in source.GetProperty("elevations").EnumerateArray())
            foreach (var obj in elevation.GetProperty("objects").EnumerateArray())
                Add(obj, topLevel: true);
        return rows;

        void Add(JsonElement obj, bool topLevel)
        {
            var serial = obj.GetProperty("serial").GetInt32();
            var row = new SourceObject(
                serial,
                obj.GetProperty("id").GetInt32(),
                RequiredString(obj, "fid"),
                RequiredString(obj, "pid"),
                obj.GetProperty("tile").GetInt32(),
                obj.GetProperty("elevation").GetInt32(),
                obj.GetProperty("rotation").GetInt32(),
                obj.GetProperty("frame").GetInt32(),
                RequiredFlags(obj, "flags"),
                obj.GetProperty("prototype").GetProperty("object_type").GetInt32(),
                OptionalInt(obj.GetProperty("prototype"), "subtype"),
                OptionalString(obj, "artFilename"),
                obj.GetProperty("instanceValues").EnumerateArray()
                    .Select(value => value.GetInt32()).ToArray(),
                RequiredString(obj, "sid"),
                obj.GetProperty("scriptIndex").GetInt32(),
                ReadVector2I(obj.GetProperty("pixelOffset")),
                topLevel);
            if (row.ObjectType is < 0 or > 5)
                throw new InvalidOperationException(
                    $"Fallout 2 Temple object type is invalid: {serial}/{row.ObjectType}");
            if (!rows.TryAdd(serial, row))
                throw new InvalidOperationException($"Duplicate Fallout 2 Temple object serial: {serial}");
            foreach (var inventory in obj.GetProperty("inventory").EnumerateArray())
                Add(inventory.GetProperty("object"), topLevel: false);
        }
    }

    private static List<Fo2TempleObjectPlacement> LoadObjectPlacements(
        JsonElement cacheBindings,
        JsonElement sourceFrms,
        IReadOnlyDictionary<string, Fo2TempleArtifact> artifacts,
        IReadOnlyDictionary<int, SourceObject> sourceObjects)
    {
        var expected = new HashSet<string>(StringComparer.Ordinal);
        foreach (var frm in sourceFrms.EnumerateArray())
        {
            var logicalPath = RequiredString(frm, "logicalPath");
            foreach (var placement in frm.GetProperty("placements").EnumerateArray())
                expected.Add(PlacementIdentity(logicalPath, placement));
        }
        var actual = new HashSet<string>(StringComparer.Ordinal);
        var placements = new List<Fo2TempleObjectPlacement>();
        foreach (var binding in cacheBindings.EnumerateArray())
        {
            var artifactId = RequiredString(binding, "artifactId");
            var logicalPath = RequiredString(binding, "logicalPath");
            if (!artifacts.TryGetValue(artifactId, out var artifact) ||
                artifact.Kind != "objects" || artifact.LogicalPath != logicalPath ||
                artifact.Rotation != binding.GetProperty("rotation").GetInt32() ||
                artifact.Frame != binding.GetProperty("frame").GetInt32())
                throw new InvalidOperationException($"Fallout 2 Temple object artifact binding drifted: {artifactId}");
            foreach (var placement in binding.GetProperty("placements").EnumerateArray())
            {
                var identity = PlacementIdentity(logicalPath, placement);
                if (!actual.Add(identity))
                    throw new InvalidOperationException($"Duplicate Fallout 2 Temple placement: {identity}");
                var serial = placement.GetProperty("serial").GetInt32();
                if (!sourceObjects.TryGetValue(serial, out var sourceObject) ||
                    sourceObject.Fid != RequiredString(placement, "fid") ||
                    sourceObject.Tile != placement.GetProperty("tile").GetInt32() ||
                    sourceObject.Elevation != placement.GetProperty("elevation").GetInt32() ||
                    sourceObject.Rotation != placement.GetProperty("rotation").GetInt32() ||
                    sourceObject.Frame != placement.GetProperty("frame").GetInt32())
                    throw new InvalidOperationException(
                        $"Fallout 2 Temple source object binding drifted: {serial}");
                if (sourceObject.TopLevel)
                    placements.Add(new Fo2TempleObjectPlacement(
                        serial,
                        sourceObject.ObjectId,
                        sourceObject.Fid,
                        sourceObject.Pid,
                        sourceObject.Tile,
                        sourceObject.Elevation,
                        sourceObject.Rotation,
                        sourceObject.Frame,
                        sourceObject.Flags,
                        sourceObject.ObjectType,
                        sourceObject.PrototypeSubtype,
                        sourceObject.ArtFilename,
                        sourceObject.InstanceValues,
                        sourceObject.Sid,
                        sourceObject.ScriptIndex,
                        sourceObject.PixelOffset,
                        true,
                        artifactId,
                        logicalPath));
            }
        }
        if (!actual.SetEquals(expected))
            throw new InvalidOperationException("Fallout 2 Temple object FRM placement coverage drifted.");
        return placements.OrderBy(row => row.Serial).ToList();
    }

    private static string PlacementIdentity(string logicalPath, JsonElement placement) =>
        string.Join(
            '|',
            logicalPath,
            placement.GetProperty("serial").GetInt32(),
            RequiredString(placement, "fid"),
            placement.GetProperty("tile").GetInt32(),
            placement.GetProperty("elevation").GetInt32(),
            placement.GetProperty("rotation").GetInt32(),
            placement.GetProperty("frame").GetInt32());

    private static void VerifyCounts(
        JsonElement counts,
        IReadOnlyDictionary<string, Fo2TempleArtifact> artifacts,
        IReadOnlyDictionary<int, Fo2TempleTileBinding> tiles,
        IReadOnlyList<Fo2TempleObjectPlacement> placements,
        JsonElement source)
    {
        var tileArtifacts = artifacts.Values.Count(row => row.Kind == "tiles");
        var objectArtifacts = artifacts.Values.Count(row => row.Kind == "objects");
        if (counts.GetProperty("tileIds").GetInt32() != tiles.Count ||
            counts.GetProperty("tileArtifacts").GetInt32() != tileArtifacts ||
            counts.GetProperty("objectFrmIdentities").GetInt32() !=
                source.GetProperty("frms").GetArrayLength() ||
            counts.GetProperty("objectArtifacts").GetInt32() != objectArtifacts ||
            counts.GetProperty("pngArtifacts").GetInt32() != artifacts.Count ||
            placements.Count != source.GetProperty("map").GetProperty("objects")
                .GetProperty("totalTopLevelObjects").GetInt32())
            throw new InvalidOperationException("Fallout 2 Temple cache count contract drifted.");
    }

    private static byte[] VerifyFile(
        string path,
        string expectedSha256,
        long? expectedBytes,
        string label)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"{label} is missing.", path);
        var data = File.ReadAllBytes(path);
        if (expectedBytes.HasValue && data.LongLength != expectedBytes.Value)
            throw new InvalidOperationException(
                $"{label} byte count drifted: {data.LongLength} != {expectedBytes.Value}");
        var actual = Sha256(data);
        if (!actual.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"{label} SHA-256 drifted: {actual} != {expectedSha256}");
        return data;
    }

    private static string ResolvePath(string path, string relativeRoot)
    {
        if (path.StartsWith("res://", StringComparison.Ordinal) ||
            path.StartsWith("user://", StringComparison.Ordinal))
            return ProjectSettings.GlobalizePath(path);
        return Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(relativeRoot, path));
    }

    private static string RequiredString(JsonElement source, string property)
    {
        var value = source.GetProperty(property).GetString();
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"Fallout 2 Temple string is empty: {property}");
        return value;
    }

    private static string RequiredHash(JsonElement source, string property)
    {
        var value = RequiredString(source, property).ToLowerInvariant();
        if (value.Length != Sha256HexCharacters ||
            value.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidOperationException($"Fallout 2 Temple SHA-256 is invalid: {property}");
        return value;
    }

    private static uint RequiredFlags(JsonElement source, string property)
    {
        var value = RequiredString(source, property);
        if (value.Length != 8 ||
            !uint.TryParse(
                value,
                System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture,
                out var flags))
            throw new InvalidOperationException(
                $"Fallout 2 Temple object flags are invalid: {property}");
        return flags;
    }

    private static int? OptionalInt(JsonElement source, string property)
    {
        var value = source.GetProperty(property);
        return value.ValueKind == JsonValueKind.Null ? null : value.GetInt32();
    }

    private static string? OptionalString(JsonElement source, string property)
    {
        var value = source.GetProperty(property);
        return value.ValueKind == JsonValueKind.Null ? null : value.GetString();
    }

    private static Vector2I ReadVector2I(JsonElement source)
    {
        var values = source.EnumerateArray().Select(row => row.GetInt32()).ToArray();
        if (values.Length != 2)
            throw new InvalidOperationException("Fallout 2 Temple vector must have two values.");
        return new Vector2I(values[0], values[1]);
    }

    private static string Sha256(ReadOnlySpan<byte> data) =>
        Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    private sealed record SourceObject(
        int Serial,
        int ObjectId,
        string Fid,
        string Pid,
        int Tile,
        int Elevation,
        int Rotation,
        int Frame,
        uint Flags,
        int ObjectType,
        int? PrototypeSubtype,
        string? ArtFilename,
        IReadOnlyList<int> InstanceValues,
        string Sid,
        int ScriptIndex,
        Vector2I PixelOffset,
        bool TopLevel);
}
