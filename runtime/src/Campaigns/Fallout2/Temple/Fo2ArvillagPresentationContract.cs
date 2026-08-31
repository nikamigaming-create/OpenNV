using System.Text.Json;
using Godot;

namespace OpenNV.Runtime.Campaigns.Fallout2.Temple;

internal sealed record Fo2ArvillagReliefPlacement(
    int Serial,
    int Tile,
    int Elevation,
    int Rotation,
    int Frame,
    Vector2I PixelOffset,
    string Fid,
    string Pid,
    int ObjectType,
    string LogicalPath,
    float DepthMeters,
    Fo2MapArtifact Artifact,
    Fo2FrmReliefArtifact Relief);

internal sealed record Fo2ArvillagFloorMaterialDepth(
    int TileId,
    string ArtifactId,
    Fo2FrmReliefArtifact Relief);

internal sealed class Fo2ArvillagPresentationCatalog
{
    internal const int MapIndex = 4;
    internal const int Elevation = 0;
    internal const int DefaultFloorTileId = 1;
    private const string CacheSchema = "opennv-fo2-arvillag-presentation-cache/v1";
    private const string SourceSchema = "opennv-fo2-owned-map-slice/v1";
    private const string ReliefSchema = "opennv-fo2-arvillag-object-relief-cache/v1";
    private const string FloorMaterialSchema =
        "opennv-fo2-arvillag-floor-material-depth-cache/v1";
    private const string FloorMaterialStatus =
        "source-projected-floor-frm-luma-normal-material-depth";
    private const string ReliefMode = "exact-frm-alpha-island-molded-relief-v2";

    private Fo2ArvillagPresentationCatalog(
        string manifestPath,
        string manifestSha256,
        string sourceManifestPath,
        string sourceManifestSha256,
        string sourceProfileId,
        string mapSha256,
        uint[] tileEntries,
        IReadOnlyDictionary<string, Fo2MapArtifact> artifacts,
        IReadOnlyDictionary<int, Fo2MapTileBinding> tileBindings,
        IReadOnlyList<Fo2MapObjectPlacement> objectPlacements,
        IReadOnlyList<Fo2ArvillagReliefPlacement> reliefPlacements,
        IReadOnlyDictionary<int, Fo2ArvillagFloorMaterialDepth> floorMaterialDepth,
        float floorNormalScale,
        int transparentPlacements,
        int verifiedResources,
        int sourceRoofPatches,
        string walkMaskSha256,
        int walkableHexes,
        int arrivalTile,
        int arrivalRotation,
        int firstActionTile,
        IReadOnlySet<int> admittedArrivalTiles,
        float sideRoughness)
    {
        ManifestPath = manifestPath;
        ManifestSha256 = manifestSha256;
        SourceManifestPath = sourceManifestPath;
        SourceManifestSha256 = sourceManifestSha256;
        SourceProfileId = sourceProfileId;
        MapSha256 = mapSha256;
        TileEntries = tileEntries;
        Artifacts = artifacts;
        TileBindings = tileBindings;
        ObjectPlacements = objectPlacements;
        ReliefPlacements = reliefPlacements;
        FloorMaterialDepth = floorMaterialDepth;
        FloorNormalScale = floorNormalScale;
        TransparentPlacements = transparentPlacements;
        VerifiedResources = verifiedResources;
        SourceRoofPatches = sourceRoofPatches;
        WalkMaskSha256 = walkMaskSha256;
        WalkableHexes = walkableHexes;
        ArrivalTile = arrivalTile;
        ArrivalRotation = arrivalRotation;
        FirstActionTile = firstActionTile;
        AdmittedArrivalTiles = admittedArrivalTiles;
        SideRoughness = sideRoughness;
    }

    internal string ManifestPath { get; }
    internal string ManifestSha256 { get; }
    internal string SourceManifestPath { get; }
    internal string SourceManifestSha256 { get; }
    internal string SourceProfileId { get; }
    internal string MapSha256 { get; }
    internal uint[] TileEntries { get; }
    internal IReadOnlyDictionary<string, Fo2MapArtifact> Artifacts { get; }
    internal IReadOnlyDictionary<int, Fo2MapTileBinding> TileBindings { get; }
    internal IReadOnlyList<Fo2MapObjectPlacement> ObjectPlacements { get; }
    internal IReadOnlyList<Fo2ArvillagReliefPlacement> ReliefPlacements { get; }
    internal IReadOnlyDictionary<int, Fo2ArvillagFloorMaterialDepth>
        FloorMaterialDepth
    { get; }
    internal float FloorNormalScale { get; }
    internal int TransparentPlacements { get; }
    internal int VerifiedResources { get; }
    internal int SourceRoofPatches { get; }
    internal string WalkMaskSha256 { get; }
    internal int WalkableHexes { get; }
    internal int ArrivalTile { get; }
    internal int ArrivalRotation { get; }
    internal int FirstActionTile { get; }
    internal IReadOnlySet<int> AdmittedArrivalTiles { get; }
    internal float SideRoughness { get; }

    internal static Fo2ArvillagPresentationCatalog Load(
        string configuredPath,
        Fo2ArroyoTrialRouteContract route)
    {
        var manifestPath = Fo2TemplePresentationCatalog.ResolvePath(
            configuredPath,
            Directory.GetCurrentDirectory());
        var cacheBytes = File.ReadAllBytes(manifestPath);
        using var cacheDocument = JsonDocument.Parse(cacheBytes);
        var cache = cacheDocument.RootElement;
        if (Fo2TemplePresentationCatalog.RequiredString(cache, "schema") != CacheSchema ||
            Fo2TemplePresentationCatalog.RequiredString(cache, "status") !=
                "decoded-disposable-local-cache" ||
            Fo2TemplePresentationCatalog.RequiredString(cache, "campaign") != "Fallout2" ||
            Fo2TemplePresentationCatalog.RequiredString(cache, "slice") != "ArroyoVillage" ||
            cache.GetProperty("runtimeCompatibility").GetProperty("ready").GetBoolean() ||
            cache.GetProperty("retailOrDerivedAssetsPackaged").GetBoolean() ||
            cache.GetProperty("cachePolicy").GetProperty("distributionAllowed").GetBoolean())
            throw new InvalidOperationException("Unexpected Fallout 2 ARVILLAG cache.");
        var cacheRoot = Path.GetDirectoryName(manifestPath)!;

        var sourceDescriptor = cache.GetProperty("sourceManifest");
        var sourcePath = Fo2TemplePresentationCatalog.ResolvePath(
            Fo2TemplePresentationCatalog.RequiredString(sourceDescriptor, "file"),
            cacheRoot);
        var sourceBytes = Fo2TemplePresentationCatalog.VerifyFile(
            sourcePath,
            Fo2TemplePresentationCatalog.RequiredHash(sourceDescriptor, "sha256"),
            null,
            "Fallout 2 ARVILLAG source manifest");
        using var sourceDocument = JsonDocument.Parse(sourceBytes);
        var source = sourceDocument.RootElement;
        if (Fo2TemplePresentationCatalog.RequiredString(sourceDescriptor, "schema") !=
                SourceSchema ||
            Fo2TemplePresentationCatalog.RequiredString(source, "schema") != SourceSchema ||
            Fo2TemplePresentationCatalog.RequiredString(source, "status") !=
                "transported-owned-map-source-and-presentation-graph" ||
            Fo2TemplePresentationCatalog.RequiredString(source, "campaign") != "Fallout2" ||
            Fo2TemplePresentationCatalog.RequiredString(source, "slice") != "ArroyoVillage" ||
            source.GetProperty("runtimeCompatibility").GetProperty("ready").GetBoolean() ||
            source.GetProperty("retailOrDerivedAssetsPackaged").GetBoolean())
            throw new InvalidOperationException("Unexpected Fallout 2 ARVILLAG source graph.");
        var sourceRoute = source.GetProperty("trialRoute");
        if (Fo2TemplePresentationCatalog.RequiredHash(sourceRoute, "sha256") != route.Sha256 ||
            Fo2TemplePresentationCatalog.ResolvePath(
                Fo2TemplePresentationCatalog.RequiredString(sourceRoute, "file"),
                Path.GetDirectoryName(sourcePath)!) != route.Path)
            throw new InvalidOperationException("Fallout 2 ARVILLAG trial-route provenance drifted.");
        var profile = source.GetProperty("sourceProfile");
        if (Fo2TemplePresentationCatalog.RequiredString(profile, "sourceProfileId") !=
                route.SourceProfileId ||
            Fo2TemplePresentationCatalog.RequiredHash(profile, "sha256") !=
                Fo2TemplePresentationCatalog.RequiredHash(
                    cache.GetProperty("sourceProfile"),
                    "sha256"))
            throw new InvalidOperationException("Fallout 2 ARVILLAG profile provenance drifted.");

        var map = source.GetProperty("map");
        var mapSha256 = Fo2TemplePresentationCatalog.RequiredHash(map, "sha256");
        var header = map.GetProperty("header");
        if (Fo2TemplePresentationCatalog.RequiredString(map, "logicalPath") !=
                "maps\\arvillag.map" ||
            Fo2TemplePresentationCatalog.RequiredHash(sourceDescriptor, "mapSha256") !=
                mapSha256 ||
            mapSha256 != route.VillageArrival.MapSha256 ||
            map.GetProperty("bytes").GetInt32() != route.VillageArrival.MapBytes ||
            Fo2TemplePresentationCatalog.RequiredString(header, "name") != "ARVILLAG.MAP" ||
            header.GetProperty("version").GetInt32() != 20 ||
            header.GetProperty("mapIndex").GetInt32() != MapIndex)
            throw new InvalidOperationException("Fallout 2 ARVILLAG MAP identity drifted.");
        var elevations = map.GetProperty("layout").GetProperty("elevations")
            .EnumerateArray().ToArray();
        if (elevations.Length != 1 ||
            elevations[0].GetProperty("elevation").GetInt32() != Elevation)
            throw new InvalidOperationException("Fallout 2 ARVILLAG admits elevation zero only.");
        var tileEntries = elevations[0].GetProperty("rawEntries").EnumerateArray()
            .Select(row => row.GetUInt32()).ToArray();
        if (tileEntries.Length != Fo1HexMath.FloorWidth * Fo1HexMath.FloorHeight)
            throw new InvalidOperationException("Fallout 2 ARVILLAG tile layout drifted.");
        var roofBoundary = source.GetProperty("roofCutawayBoundary");
        var sourceRoofPatches = roofBoundary.GetProperty("sourceRoofPatches").GetInt32();
        if (roofBoundary.GetProperty("rendered").GetBoolean() ||
            sourceRoofPatches != elevations[0].GetProperty("nonDefaultRoofCount").GetInt32() ||
            sourceRoofPatches <= 0)
            throw new InvalidOperationException("Fallout 2 ARVILLAG roof-cutaway boundary drifted.");

        var incoming = source.GetProperty("incomingPlacement");
        var walk = source.GetProperty("arrivalWalkContract");
        var action = walk.GetProperty("firstLegalAction");
        var arrivalTile = incoming.GetProperty("tile").GetInt32();
        var arrivalRotation = incoming.GetProperty("rotation").GetInt32();
        var legalNeighbors = walk.GetProperty("legalNeighborTiles").EnumerateArray()
            .Select(row => row.GetInt32()).ToArray();
        if (incoming.GetProperty("mapIndex").GetInt32() != MapIndex ||
            incoming.GetProperty("elevation").GetInt32() != Elevation ||
            arrivalTile != route.VillageArrival.ArrivalTile ||
            arrivalRotation != route.VillageArrival.ArrivalRotation ||
            walk.GetProperty("walkableHexes").GetInt32() !=
                route.VillageArrival.WalkableHexes ||
            Fo2TemplePresentationCatalog.RequiredHash(walk, "walkMaskSha256") !=
                route.VillageArrival.WalkMaskSha256 ||
            !legalNeighbors.SequenceEqual(route.VillageArrival.LegalNeighborTiles) ||
            action.GetProperty("fromTile").GetInt32() !=
                route.VillageArrival.FirstActionFromTile ||
            action.GetProperty("toTile").GetInt32() !=
                route.VillageArrival.FirstActionToTile ||
            action.GetProperty("rotation").GetInt32() !=
                route.VillageArrival.FirstActionRotation)
            throw new InvalidOperationException("Fallout 2 ARVILLAG arrival contract drifted.");

        var artifacts = Fo2TemplePresentationCatalog.LoadArtifacts(
            cache.GetProperty("artifacts"),
            cacheRoot);
        var tileBindings = Fo2TemplePresentationCatalog.LoadTileBindings(
            cache.GetProperty("tileBindings"),
            artifacts);
        Fo2TemplePresentationCatalog.VerifyTileBindings(
            new Dictionary<int, IReadOnlyList<uint>> { [Elevation] = tileEntries },
            tileBindings);
        var objectRows = Fo2TemplePresentationCatalog.FlattenObjects(
            map.GetProperty("objects"));
        var objectPlacements = Fo2TemplePresentationCatalog.LoadObjectPlacements(
            cache.GetProperty("objectBindings"),
            source.GetProperty("frms"),
            artifacts,
            objectRows);
        Fo2TemplePresentationCatalog.VerifyCounts(
            cache.GetProperty("counts"),
            artifacts,
            tileBindings,
            objectPlacements,
            source);

        var relief = cache.GetProperty("objectRelief3d");
        if (Fo2TemplePresentationCatalog.RequiredString(relief, "schema") != ReliefSchema ||
            Fo2TemplePresentationCatalog.RequiredString(relief, "status") !=
                "source-frm-alpha-derived-closed-relief" ||
            Fo2TemplePresentationCatalog.RequiredString(relief, "mode") != ReliefMode ||
            relief.GetProperty("visualParity").GetBoolean())
            throw new InvalidOperationException("Fallout 2 ARVILLAG relief identity drifted.");
        var reliefArtifacts = relief.GetProperty("artifacts").EnumerateArray()
            .ToDictionary(
                row => Fo2TemplePresentationCatalog.RequiredString(row, "artifactId"),
                row => Fo2FrmReliefArtifact.Load(
                    row.GetProperty("relief"),
                    cacheRoot,
                    Fo2TemplePresentationCatalog.RequiredHash(row, "pngSha256"),
                    "Fallout 2 ARVILLAG object"),
                StringComparer.Ordinal);
        var bySerial = objectPlacements.ToDictionary(row => row.Serial);
        var reliefPlacements = new List<Fo2ArvillagReliefPlacement>();
        foreach (var row in relief.GetProperty("placements").EnumerateArray())
        {
            var serial = row.GetProperty("serial").GetInt32();
            var artifactId = Fo2TemplePresentationCatalog.RequiredString(row, "artifactId");
            if (!bySerial.TryGetValue(serial, out var sourcePlacement) ||
                !artifacts.TryGetValue(artifactId, out var artifact) ||
                !reliefArtifacts.TryGetValue(artifactId, out var reliefArtifact) ||
                sourcePlacement.ArtifactId != artifactId ||
                sourcePlacement.Tile != row.GetProperty("tile").GetInt32() ||
                sourcePlacement.Elevation != row.GetProperty("elevation").GetInt32() ||
                sourcePlacement.Rotation != row.GetProperty("rotation").GetInt32() ||
                sourcePlacement.Frame != row.GetProperty("frame").GetInt32() ||
                sourcePlacement.Fid != Fo2TemplePresentationCatalog.RequiredString(row, "fid") ||
                sourcePlacement.Pid != Fo2TemplePresentationCatalog.RequiredString(row, "pid") ||
                sourcePlacement.ObjectType != row.GetProperty("objectType").GetInt32() ||
                sourcePlacement.LogicalPath !=
                    Fo2TemplePresentationCatalog.RequiredString(row, "logicalPath"))
                throw new InvalidOperationException(
                    $"Fallout 2 ARVILLAG relief placement drifted: {serial}.");
            var offset = row.GetProperty("pixelOffset").EnumerateArray()
                .Select(value => value.GetInt32()).ToArray();
            var depthMeters = row.GetProperty("depthMeters").GetSingle();
            if (offset.Length != 2 ||
                sourcePlacement.PixelOffset != new Vector2I(offset[0], offset[1]) ||
                !float.IsFinite(depthMeters) || depthMeters <= 0.0f)
                throw new InvalidOperationException(
                    $"Fallout 2 ARVILLAG relief dimensions drifted: {serial}.");
            reliefPlacements.Add(new Fo2ArvillagReliefPlacement(
                serial,
                sourcePlacement.Tile,
                sourcePlacement.Elevation,
                sourcePlacement.Rotation,
                sourcePlacement.Frame,
                sourcePlacement.PixelOffset,
                sourcePlacement.Fid,
                sourcePlacement.Pid,
                sourcePlacement.ObjectType,
                sourcePlacement.LogicalPath,
                depthMeters,
                artifact,
                reliefArtifact));
        }
        var transparent = relief.GetProperty("transparentSourceSerials")
            .EnumerateArray().Select(row => row.GetInt32()).ToHashSet();
        var visibleSerials = reliefPlacements.Select(row => row.Serial).ToHashSet();
        if (visibleSerials.Overlaps(transparent) ||
            !visibleSerials.Union(transparent).Order().SequenceEqual(bySerial.Keys.Order()) ||
            relief.GetProperty("counts").GetProperty("reliefPlacements").GetInt32() !=
                reliefPlacements.Count ||
            relief.GetProperty("counts").GetProperty("transparentSourcePlacements")
                .GetInt32() != transparent.Count ||
            relief.GetProperty("counts").GetProperty("topLevelSourcePlacements")
                .GetInt32() != bySerial.Count)
            throw new InvalidOperationException("Fallout 2 ARVILLAG relief closure drifted.");

        var floorMaterial = cache.GetProperty("floorMaterialDepth3d");
        if (Fo2TemplePresentationCatalog.RequiredString(floorMaterial, "schema") !=
                FloorMaterialSchema ||
            Fo2TemplePresentationCatalog.RequiredString(floorMaterial, "status") !=
                FloorMaterialStatus ||
            Fo2TemplePresentationCatalog.RequiredString(floorMaterial, "mode") != ReliefMode ||
            floorMaterial.GetProperty("visualParity").GetBoolean())
            throw new InvalidOperationException(
                "Fallout 2 ARVILLAG floor material-depth identity drifted.");
        var floorMaterialDepth = new Dictionary<int, Fo2ArvillagFloorMaterialDepth>();
        foreach (var row in floorMaterial.GetProperty("artifacts").EnumerateArray())
        {
            var tileId = row.GetProperty("tileId").GetInt32();
            var artifactId = Fo2TemplePresentationCatalog.RequiredString(row, "artifactId");
            if (tileId == DefaultFloorTileId ||
                !tileBindings.TryGetValue(tileId, out var binding) ||
                binding.ArtifactId != artifactId ||
                !artifacts.TryGetValue(artifactId, out var artifact) ||
                artifact.LogicalPath !=
                    Fo2TemplePresentationCatalog.RequiredString(row, "logicalPath") ||
                artifact.SourceSha256 !=
                    Fo2TemplePresentationCatalog.RequiredHash(row, "sourceSha256") ||
                artifact.PngSha256 !=
                    Fo2TemplePresentationCatalog.RequiredHash(row, "pngSha256") ||
                !floorMaterialDepth.TryAdd(
                    tileId,
                    new Fo2ArvillagFloorMaterialDepth(
                        tileId,
                        artifactId,
                        Fo2FrmReliefArtifact.Load(
                            row.GetProperty("relief"),
                            cacheRoot,
                            artifact.PngSha256,
                            "Fallout 2 ARVILLAG floor material"))))
                throw new InvalidOperationException(
                    $"Fallout 2 ARVILLAG floor material-depth artifact drifted: {tileId}.");
        }
        var requiredFloorIds = tileEntries
            .Select(entry => (int)(entry & 0x0fff))
            .Where(id => id != DefaultFloorTileId)
            .ToHashSet();
        var floorCounts = floorMaterial.GetProperty("counts");
        var floorNormalScale = floorMaterial.GetProperty("normalScale").GetSingle();
        if (!requiredFloorIds.SetEquals(floorMaterialDepth.Keys) ||
            floorCounts.GetProperty("sourceFloorTileIds").GetInt32() !=
                requiredFloorIds.Count ||
            floorCounts.GetProperty("materialDepthArtifacts").GetInt32() !=
                floorMaterialDepth.Count ||
            !float.IsFinite(floorNormalScale) || floorNormalScale <= 0.0f)
            throw new InvalidOperationException(
                "Fallout 2 ARVILLAG floor material-depth closure drifted.");

        var resources = cache.GetProperty("resources").EnumerateArray().ToArray();
        var resourceIdentities = resources.Select(row =>
                $"{Fo2TemplePresentationCatalog.RequiredString(row, "logicalPath")}|" +
                Fo2TemplePresentationCatalog.RequiredHash(row, "sha256"))
            .ToHashSet(StringComparer.Ordinal);
        if (resourceIdentities.Count != resources.Length ||
            !artifacts.Values.All(artifact => resourceIdentities.Contains(
                $"{artifact.LogicalPath}|{artifact.SourceSha256}")))
            throw new InvalidOperationException("Fallout 2 ARVILLAG resource closure drifted.");
        var sideRoughness = relief.GetProperty("sideRoughness").GetSingle();
        if (!float.IsFinite(sideRoughness) || sideRoughness is < 0.0f or > 1.0f)
            throw new InvalidOperationException("Fallout 2 ARVILLAG relief roughness drifted.");

        return new Fo2ArvillagPresentationCatalog(
            manifestPath,
            Fo2TemplePresentationCatalog.Sha256(cacheBytes),
            sourcePath,
            Fo2TemplePresentationCatalog.Sha256(sourceBytes),
            route.SourceProfileId,
            mapSha256,
            tileEntries,
            artifacts,
            tileBindings,
            objectPlacements,
            reliefPlacements,
            floorMaterialDepth,
            floorNormalScale,
            transparent.Count,
            resources.Length,
            sourceRoofPatches,
            route.VillageArrival.WalkMaskSha256,
            route.VillageArrival.WalkableHexes,
            arrivalTile,
            arrivalRotation,
            action.GetProperty("toTile").GetInt32(),
            legalNeighbors.Append(arrivalTile).ToHashSet(),
            sideRoughness);
    }
}
