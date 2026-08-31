using System.Text.Json;
using Godot;
using OpenNV.Runtime.Campaigns.Classic;
using OpenNV.Runtime.Campaigns.Fallout1;

namespace OpenNV.Runtime.Campaigns.Fallout2.Temple;

internal sealed record Fo2ArroyoExitTransition(
    int ExitSerial,
    string ExitFid,
    string ExitPid,
    int SourceMapIndex,
    string SourceMapSha256,
    int SourceTile,
    int SourceElevation,
    IReadOnlyList<int> SourcePath,
    string SourcePathSha256,
    int TargetMapIndex,
    string TargetLogicalPath,
    string TargetMapSha256,
    int TargetTile,
    int TargetElevation,
    int TargetRotation);

internal sealed record Fo2ArroyoMoldedSurface(
    string WallTexturePath,
    string WallTextureSha256,
    string WallNormalTexturePath,
    string WallNormalTextureSha256,
    string FloorTexturePath,
    string FloorTextureSha256,
    string FloorNormalTexturePath,
    string FloorNormalTextureSha256,
    string ProvenancePath,
    string ProvenanceSha256,
    string RecipeSha256,
    int SourceWallObjects,
    int SourceWallArtifacts,
    int OpaqueSourceWallArtifacts,
    int SourceFloorPatches,
    int SourceFloorArtifacts,
    string SourceSerialsSha256,
    string Mode);

internal sealed record Fo2ArroyoClassicHudAsset(
    string Id,
    string LogicalPath,
    string SourceSha256,
    string PngPath,
    string PngSha256,
    int Width,
    int Height,
    bool Opaque);

internal sealed record Fo2ArroyoClassicHudNumbers(
    int DigitWidth,
    int SignWidth,
    int Height,
    int MinusX,
    int PlusX,
    int WhiteOffset,
    int YellowOffset,
    int RedOffset);

internal sealed record Fo2ArroyoClassicHudSurface(
    string Mode,
    string RecipeId,
    string RecipeSha256,
    int Width,
    int Height,
    int ActionPointX,
    int ActionPointY,
    int ActionPointSlots,
    int ActionPointStride,
    Vector2I HitPoints,
    Vector2I ArmorClass,
    Vector2I ItemPanel,
    Fo2ArroyoClassicHudNumbers Numbers,
    IReadOnlyDictionary<string, Vector2I> ButtonPositions,
    IReadOnlyDictionary<string, Fo2ArroyoClassicHudAsset> Assets);

internal sealed record Fo2ArroyoObjectReliefPlacement(
    int Serial,
    int Tile,
    int Rotation,
    int Frame,
    Vector2I PixelOffset,
    string Fid,
    string Pid,
    int ObjectType,
    string LogicalPath,
    string ArtifactId,
    string Role,
    float DepthMeters);

internal sealed record Fo2ArroyoObjectReliefCatalog(
    string Mode,
    string ProvenancePath,
    string ProvenanceSha256,
    IReadOnlyDictionary<string, Fo2FrmReliefArtifact> Artifacts,
    IReadOnlyList<Fo2ArroyoObjectReliefPlacement> Placements,
    int TorchPlacements);

internal sealed class Fo2ArroyoCavesPresentationCatalog
{
    private const string CacheSchema = "opennv-fo2-arroyo-caves-presentation-cache/v2";
    private const string SourceSchema = "opennv-fo2-owned-map-slice/v1";
    private const string ProfileSchema = "opennv-fo2-owned-profile/v1";
    private const string MoldedSurfaceSchema = "opennv-fo2-arroyo-molded-surface-cache/v2";
    private const string MoldedSurfaceRecipeSchema =
        "opennv-fo2-arroyo-molded-surface-recipe/v2";
    private const string MoldedSurfaceStatus =
        "source-wall-frm-derived-disposable-local-surface";
    private const string MoldedSurfaceMode =
        "source-frm-albedo-normal-overlap-tile-bake-v2";
    private const string MoldedSurfaceNormalMode =
        "source-luminance-periodic-height-gradient-v1";
    private const string ClassicHudSchema =
        "opennv-fo2-arroyo-classic-hud-cache/v1";
    private const string ClassicHudRecipeSchema =
        "opennv-fo2-arroyo-classic-hud-recipe/v1";
    private const string ClassicHudStatus =
        "decoded-owned-fallout2-classic-interface";
    private const string ClassicHudMode =
        "owned-fallout2-source-pixel-interface-compositor-v1";
    private const string ObjectReliefSchema =
        "opennv-fo2-arroyo-object-relief-cache/v2";
    private const string ObjectReliefStatus =
        "source-frm-alpha-derived-closed-relief";
    private const string ObjectReliefMode =
        "exact-frm-alpha-island-molded-relief-v2";
    private const int MapVersion = 20;
    private const int TileEntryCount = Fo1HexMath.FloorWidth * Fo1HexMath.FloorHeight;
    private const int HexEntryCount = Fo1HexMath.Width * Fo1HexMath.Height;
    private const int ExpectedSourceWallObjects = 1145;
    private const int ExpectedSourceWallArtifacts = 102;
    private const int ExpectedOpaqueSourceWallArtifacts = 101;
    private const int ExpectedSourceFloorPatches = 4595;
    private const int ExpectedSourceFloorArtifacts = 20;
    private const int ExpectedClassicHudArtifacts = 15;
    private const int ClassicHudWidth = 640;
    private const int ClassicHudHeight = 99;
    private const int ExpectedClassicHudButtonPositions = 7;
    private const int ExpectedObjectReliefArtifacts = 122;
    private const int ExpectedObjectReliefPlacements = 1038;
    private const int ExpectedObjectReliefTorchPlacements = 22;
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
        IReadOnlyDictionary<int, IReadOnlyList<uint>> tileEntriesByElevation,
        IReadOnlyDictionary<string, Fo2MapArtifact> artifacts,
        IReadOnlyDictionary<int, Fo2MapTileBinding> tileBindings,
        IReadOnlyList<Fo2MapObjectPlacement> objectPlacements,
        ClassicMapInitialization initialization,
        int verifiedResources,
        bool[] walkable,
        string walkMaskSha256,
        int walkableHexes,
        int arrivalComponentHexes,
        string sourceTransitionSha256,
        Fo2ArroyoMoldedSurface moldedSurface,
        Fo2ArroyoClassicHudSurface classicHud,
        Fo2ArroyoObjectReliefCatalog objectRelief,
        Fo2ArroyoExitTransition liveExit)
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
        TileEntriesByElevation = tileEntriesByElevation;
        Artifacts = artifacts;
        TileBindings = tileBindings;
        ObjectPlacements = objectPlacements;
        Initialization = initialization;
        VerifiedResources = verifiedResources;
        Walkable = walkable;
        WalkMaskSha256 = walkMaskSha256;
        WalkableHexes = walkableHexes;
        ArrivalComponentHexes = arrivalComponentHexes;
        SourceTransitionSha256 = sourceTransitionSha256;
        MoldedSurface = moldedSurface;
        ClassicHud = classicHud;
        ObjectRelief = objectRelief;
        LiveExit = liveExit;
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
    internal IReadOnlyDictionary<int, IReadOnlyList<uint>> TileEntriesByElevation { get; }
    internal IReadOnlyDictionary<string, Fo2MapArtifact> Artifacts { get; }
    internal IReadOnlyDictionary<int, Fo2MapTileBinding> TileBindings { get; }
    internal IReadOnlyList<Fo2MapObjectPlacement> ObjectPlacements { get; }
    internal ClassicMapInitialization Initialization { get; }
    internal int VerifiedResources { get; }
    internal IReadOnlyList<bool> Walkable { get; }
    internal string WalkMaskSha256 { get; }
    internal int WalkableHexes { get; }
    internal int ArrivalComponentHexes { get; }
    internal string SourceTransitionSha256 { get; }
    internal Fo2ArroyoMoldedSurface MoldedSurface { get; }
    internal Fo2ArroyoClassicHudSurface ClassicHud { get; }
    internal Fo2ArroyoObjectReliefCatalog ObjectRelief { get; }
    internal Fo2ArroyoExitTransition LiveExit { get; }

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
                    "opennv-fo2-first-slice-recipe/v2" ||
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
        var initialization = ClassicMapInitializationOwner.Parse(map);
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
        var moldedSurface = LoadMoldedSurface(
            cache.GetProperty("molded3dSurface"),
            cacheRoot,
            mapSha256,
            artifacts);
        var classicHud = LoadClassicHud(
            cache.GetProperty("classicHud"),
            cacheRoot,
            resourceIdentities);
        var objectRelief = LoadObjectRelief(
            cache.GetProperty("objectRelief3d"),
            cacheRoot,
            artifacts,
            objectPlacements,
            mapSha256);

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
        var liveExit = LoadLiveExit(
            source,
            transition,
            sourceObjects,
            walkable,
            arrivalTile,
            mapSha256);

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
            entriesByElevation,
            artifacts,
            tileBindings,
            objectPlacements,
            initialization,
            resources.Length,
            walkable.ToArray(),
            walkMaskSha256,
            walkable.Count(row => row),
            arrivalComponentHexes,
            transition.ManifestSha256,
            moldedSurface,
            classicHud,
            objectRelief,
            liveExit);
    }

    private static Fo2ArroyoObjectReliefCatalog LoadObjectRelief(
        JsonElement descriptor,
        string cacheRoot,
        IReadOnlyDictionary<string, Fo2MapArtifact> sourceArtifacts,
        IReadOnlyList<Fo2MapObjectPlacement> sourcePlacements,
        string mapSha256)
    {
        if (Fo2TemplePresentationCatalog.RequiredString(descriptor, "schema") !=
                ObjectReliefSchema ||
            Fo2TemplePresentationCatalog.RequiredString(descriptor, "status") !=
                ObjectReliefStatus ||
            Fo2TemplePresentationCatalog.RequiredString(descriptor, "mode") !=
                ObjectReliefMode ||
            Fo2TemplePresentationCatalog.RequiredHash(descriptor, "mapSha256") != mapSha256 ||
            descriptor.GetProperty("generatedMeshPackaged").GetBoolean() ||
            descriptor.GetProperty("distributionAllowed").GetBoolean())
            throw new InvalidOperationException(
                "Fallout 2 Arroyo closed-relief descriptor drifted.");
        var provenanceDescriptor = descriptor.GetProperty("provenance");
        var provenancePath = Fo2TemplePresentationCatalog.ResolvePath(
            Fo2TemplePresentationCatalog.RequiredString(provenanceDescriptor, "file"),
            cacheRoot);
        var provenanceSha256 = Fo2TemplePresentationCatalog.RequiredHash(
            provenanceDescriptor,
            "sha256");
        var provenanceBytes = Fo2TemplePresentationCatalog.VerifyFile(
            provenancePath,
            provenanceSha256,
            provenanceDescriptor.GetProperty("bytes").GetInt64(),
            "Fallout 2 Arroyo closed-relief provenance");
        using var provenanceDocument = JsonDocument.Parse(provenanceBytes);
        var provenance = provenanceDocument.RootElement;
        if (Fo2TemplePresentationCatalog.RequiredString(provenance, "schema") !=
                ObjectReliefSchema ||
            Fo2TemplePresentationCatalog.RequiredString(provenance, "status") !=
                ObjectReliefStatus ||
            Fo2TemplePresentationCatalog.RequiredString(provenance, "mode") !=
                ObjectReliefMode ||
            Fo2TemplePresentationCatalog.RequiredHash(provenance, "mapSha256") != mapSha256 ||
            provenance.GetProperty("generatedMeshPackaged").GetBoolean() ||
            provenance.GetProperty("distributionAllowed").GetBoolean())
            throw new InvalidOperationException(
                "Fallout 2 Arroyo closed-relief provenance drifted.");

        var reliefArtifacts = new Dictionary<string, Fo2FrmReliefArtifact>(
            StringComparer.Ordinal);
        foreach (var property in provenance.GetProperty("artifacts").EnumerateObject())
        {
            var id = property.Name;
            var row = property.Value;
            if (!sourceArtifacts.TryGetValue(id, out var sourceArtifact) ||
                Fo2TemplePresentationCatalog.RequiredString(row, "artifactId") != id ||
                Fo2TemplePresentationCatalog.RequiredString(row, "logicalPath") !=
                    sourceArtifact.LogicalPath ||
                Fo2TemplePresentationCatalog.RequiredHash(row, "sourceSha256") !=
                    sourceArtifact.SourceSha256 ||
                Fo2TemplePresentationCatalog.RequiredHash(row, "pngSha256") !=
                    sourceArtifact.PngSha256 ||
                row.GetProperty("width").GetInt32() != sourceArtifact.Width ||
                row.GetProperty("height").GetInt32() != sourceArtifact.Height)
                throw new InvalidOperationException(
                    $"Fallout 2 Arroyo closed-relief source drifted: {id}.");
            reliefArtifacts.Add(
                id,
                Fo2FrmReliefArtifact.Load(
                    row.GetProperty("relief"),
                    cacheRoot,
                    sourceArtifact.PngSha256,
                    $"Fallout 2 Arroyo object {id}"));
        }

        var sourceBySerial = sourcePlacements.ToDictionary(row => row.Serial);
        var placements = provenance.GetProperty("placements").EnumerateArray()
            .Select(row =>
            {
                var serial = row.GetProperty("serial").GetInt32();
                var artifactId = Fo2TemplePresentationCatalog.RequiredString(
                    row,
                    "artifactId");
                if (!sourceBySerial.TryGetValue(serial, out var source) ||
                    !reliefArtifacts.ContainsKey(artifactId) ||
                    source.ArtifactId != artifactId ||
                    source.Tile != row.GetProperty("tile").GetInt32() ||
                    source.Rotation != row.GetProperty("rotation").GetInt32() ||
                    source.Frame != row.GetProperty("frame").GetInt32() ||
                    source.PixelOffset != Fo2TemplePresentationCatalog.ReadVector2I(
                        row.GetProperty("pixelOffset")) ||
                    source.Fid != Fo2TemplePresentationCatalog.RequiredString(row, "fid") ||
                    source.Pid != Fo2TemplePresentationCatalog.RequiredString(row, "pid") ||
                    source.ObjectType != row.GetProperty("objectType").GetInt32() ||
                    source.LogicalPath != Fo2TemplePresentationCatalog.RequiredString(
                        row,
                        "logicalPath"))
                    throw new InvalidOperationException(
                        $"Fallout 2 Arroyo closed-relief placement drifted: {serial}.");
                var depth = row.GetProperty("depthMeters").GetSingle();
                if (!float.IsFinite(depth) || depth <= 0.0f)
                    throw new InvalidOperationException(
                        $"Fallout 2 Arroyo closed-relief depth drifted: {serial}.");
                return new Fo2ArroyoObjectReliefPlacement(
                    serial,
                    source.Tile,
                    source.Rotation,
                    source.Frame,
                    source.PixelOffset,
                    source.Fid,
                    source.Pid,
                    source.ObjectType,
                    source.LogicalPath,
                    artifactId,
                    Fo2TemplePresentationCatalog.RequiredString(row, "role"),
                    depth);
            })
            .OrderBy(row => row.Serial)
            .ToArray();
        var coverage = provenance.GetProperty("coverage");
        var torchPlacements = coverage.GetProperty("torchPlacements").GetInt32();
        if (reliefArtifacts.Count != ExpectedObjectReliefArtifacts ||
            placements.Length != ExpectedObjectReliefPlacements ||
            torchPlacements != ExpectedObjectReliefTorchPlacements ||
            coverage.GetProperty("artifacts").GetInt32() != reliefArtifacts.Count ||
            coverage.GetProperty("placements").GetInt32() != placements.Length ||
            placements.Select(row => row.Serial).Distinct().Count() != placements.Length ||
            placements.Count(row => row.LogicalPath.Contains(
                "atorch",
                StringComparison.Ordinal)) != torchPlacements)
            throw new InvalidOperationException(
                "Fallout 2 Arroyo closed-relief coverage drifted.");
        return new Fo2ArroyoObjectReliefCatalog(
            ObjectReliefMode,
            provenancePath,
            provenanceSha256,
            reliefArtifacts,
            placements,
            torchPlacements);
    }

    private static Fo2ArroyoClassicHudSurface LoadClassicHud(
        JsonElement descriptor,
        string cacheRoot,
        IReadOnlySet<string> resourceIdentities)
    {
        if (Fo2TemplePresentationCatalog.RequiredString(descriptor, "schema") !=
                ClassicHudSchema ||
            Fo2TemplePresentationCatalog.RequiredString(descriptor, "status") !=
                ClassicHudStatus ||
            Fo2TemplePresentationCatalog.RequiredString(descriptor, "mode") !=
                ClassicHudMode ||
            descriptor.GetProperty("cachePolicy").GetProperty("distributionAllowed")
                .GetBoolean() ||
            !descriptor.GetProperty("cachePolicy")
                .GetProperty("containsDerivedOwnedPixels").GetBoolean())
            throw new InvalidOperationException(
                "Fallout 2 owned classic HUD descriptor drifted.");

        var recipeDescriptor = descriptor.GetProperty("recipe");
        var recipePath = Fo2TemplePresentationCatalog.ResolvePath(
            Fo2TemplePresentationCatalog.RequiredString(recipeDescriptor, "file"),
            cacheRoot);
        var recipeSha256 = Fo2TemplePresentationCatalog.RequiredHash(
            recipeDescriptor,
            "sha256");
        var recipeBytes = Fo2TemplePresentationCatalog.VerifyFile(
            recipePath,
            recipeSha256,
            null,
            "Fallout 2 owned classic HUD recipe");
        using var recipeDocument = JsonDocument.Parse(recipeBytes);
        var recipe = recipeDocument.RootElement;
        if (Fo2TemplePresentationCatalog.RequiredString(recipe, "schema") !=
                ClassicHudRecipeSchema ||
            Fo2TemplePresentationCatalog.RequiredString(recipe, "campaign") != "Fallout2" ||
            Fo2TemplePresentationCatalog.RequiredString(recipe, "mode") != ClassicHudMode ||
            Fo2TemplePresentationCatalog.RequiredString(recipe, "id") !=
                Fo2TemplePresentationCatalog.RequiredString(recipeDescriptor, "id"))
            throw new InvalidOperationException(
                "Fallout 2 owned classic HUD recipe binding drifted.");

        var expectedAssets = recipe.GetProperty("assets");
        var assets = new Dictionary<string, Fo2ArroyoClassicHudAsset>(
            StringComparer.Ordinal);
        foreach (var property in descriptor.GetProperty("assets").EnumerateObject())
        {
            var id = property.Name;
            if (!expectedAssets.TryGetProperty(id, out var expected))
                throw new InvalidOperationException(
                    $"Fallout 2 classic HUD cache has an unexpected asset: {id}.");
            var row = property.Value;
            var logicalPath = Fo2TemplePresentationCatalog.RequiredString(row, "logicalPath");
            var sourceSha256 = Fo2TemplePresentationCatalog.RequiredHash(row, "sourceSha256");
            var width = row.GetProperty("width").GetInt32();
            var height = row.GetProperty("height").GetInt32();
            var opaque = row.GetProperty("opaque").GetBoolean();
            if (logicalPath != Fo2TemplePresentationCatalog.RequiredString(
                    expected,
                    "logicalPath") ||
                sourceSha256 != Fo2TemplePresentationCatalog.RequiredHash(expected, "sha256") ||
                width != expected.GetProperty("width").GetInt32() ||
                height != expected.GetProperty("height").GetInt32() ||
                opaque != expected.GetProperty("opaque").GetBoolean() ||
                !resourceIdentities.Contains($"{logicalPath}|{sourceSha256}"))
                throw new InvalidOperationException(
                    $"Fallout 2 classic HUD source identity drifted: {id}.");
            var pngPath = Fo2TemplePresentationCatalog.ResolvePath(
                Fo2TemplePresentationCatalog.RequiredString(row, "png"),
                cacheRoot);
            var pngSha256 = Fo2TemplePresentationCatalog.RequiredHash(row, "pngSha256");
            Fo2TemplePresentationCatalog.VerifyFile(
                pngPath,
                pngSha256,
                row.GetProperty("pngBytes").GetInt64(),
                $"Fallout 2 classic HUD PNG {id}");
            if (!assets.TryAdd(
                id,
                new Fo2ArroyoClassicHudAsset(
                    id,
                    logicalPath,
                    sourceSha256,
                    pngPath,
                    pngSha256,
                    width,
                    height,
                    opaque)))
                throw new InvalidOperationException(
                    $"Fallout 2 classic HUD asset ID is duplicated: {id}.");
        }
        if (assets.Count != ExpectedClassicHudArtifacts ||
            expectedAssets.EnumerateObject().Count() != ExpectedClassicHudArtifacts)
            throw new InvalidOperationException(
                "Fallout 2 classic HUD asset coverage drifted.");

        var layout = descriptor.GetProperty("layout");
        var canvas = layout.GetProperty("canvas");
        var actionPoints = layout.GetProperty("actionPoints");
        var numbers = layout.GetProperty("numbers");
        var hitPoints = ReadPoint(layout.GetProperty("hitPoints"));
        var armorClass = ReadPoint(layout.GetProperty("armorClass"));
        var itemPanel = ReadPoint(layout.GetProperty("itemPanel"));
        var buttonPositions = layout.GetProperty("buttons").EnumerateObject().ToDictionary(
            property => property.Name,
            property => new Vector2I(
                property.Value.GetProperty("x").GetInt32(),
                property.Value.GetProperty("y").GetInt32()),
            StringComparer.Ordinal);
        if (canvas.GetProperty("width").GetInt32() != ClassicHudWidth ||
            canvas.GetProperty("height").GetInt32() != ClassicHudHeight ||
            assets["main"].Width != ClassicHudWidth ||
            assets["main"].Height != ClassicHudHeight ||
            buttonPositions.Count != ExpectedClassicHudButtonPositions)
            throw new InvalidOperationException(
                "Fallout 2 classic HUD source-pixel layout drifted.");
        return new Fo2ArroyoClassicHudSurface(
            ClassicHudMode,
            Fo2TemplePresentationCatalog.RequiredString(recipeDescriptor, "id"),
            recipeSha256,
            ClassicHudWidth,
            ClassicHudHeight,
            actionPoints.GetProperty("x").GetInt32(),
            actionPoints.GetProperty("y").GetInt32(),
            actionPoints.GetProperty("slots").GetInt32(),
            actionPoints.GetProperty("stride").GetInt32(),
            hitPoints,
            armorClass,
            itemPanel,
            new Fo2ArroyoClassicHudNumbers(
                numbers.GetProperty("digitWidth").GetInt32(),
                numbers.GetProperty("signWidth").GetInt32(),
                numbers.GetProperty("height").GetInt32(),
                numbers.GetProperty("minusX").GetInt32(),
                numbers.GetProperty("plusX").GetInt32(),
                numbers.GetProperty("whiteOffset").GetInt32(),
                numbers.GetProperty("yellowOffset").GetInt32(),
                numbers.GetProperty("redOffset").GetInt32()),
            buttonPositions,
            assets);
    }

    private static Vector2I ReadPoint(JsonElement row) => new(
        row.GetProperty("x").GetInt32(),
        row.GetProperty("y").GetInt32());

    private static Fo2ArroyoMoldedSurface LoadMoldedSurface(
        JsonElement descriptor,
        string cacheRoot,
        string mapSha256,
        IReadOnlyDictionary<string, Fo2MapArtifact> artifacts)
    {
        if (Fo2TemplePresentationCatalog.RequiredString(descriptor, "schema") !=
                MoldedSurfaceSchema ||
            Fo2TemplePresentationCatalog.RequiredString(descriptor, "status") !=
                MoldedSurfaceStatus ||
            Fo2TemplePresentationCatalog.RequiredString(descriptor, "mode") !=
                MoldedSurfaceMode ||
            descriptor.GetProperty("distributionAllowed").GetBoolean() ||
            descriptor.GetProperty("sourceWallObjects").GetInt32() !=
                ExpectedSourceWallObjects ||
            descriptor.GetProperty("sourceWallArtifacts").GetInt32() !=
                ExpectedSourceWallArtifacts ||
            descriptor.GetProperty("sourceFloorPatches").GetInt32() !=
                ExpectedSourceFloorPatches ||
            descriptor.GetProperty("sourceFloorArtifacts").GetInt32() !=
                ExpectedSourceFloorArtifacts)
            throw new InvalidOperationException(
                "Fallout 2 Arroyo molded-surface descriptor drifted.");

        var wallTexture = LoadMoldedTexture(
            descriptor.GetProperty("wallTexture"),
            cacheRoot,
            "Fallout 2 Arroyo molded wall surface");
        var floorTexture = LoadMoldedTexture(
            descriptor.GetProperty("floorTexture"),
            cacheRoot,
            "Fallout 2 Arroyo molded floor surface");
        var wallNormalTexture = LoadMoldedTexture(
            descriptor.GetProperty("wallNormalTexture"),
            cacheRoot,
            "Fallout 2 Arroyo molded wall normal surface");
        var floorNormalTexture = LoadMoldedTexture(
            descriptor.GetProperty("floorNormalTexture"),
            cacheRoot,
            "Fallout 2 Arroyo molded floor normal surface");
        var normalDerivation = descriptor.GetProperty("normalDerivation");
        if (Fo2TemplePresentationCatalog.RequiredString(normalDerivation, "mode") !=
                MoldedSurfaceNormalMode ||
            !normalDerivation.GetProperty("periodic").GetBoolean() ||
            normalDerivation.GetProperty("blurRadiusPixels").GetSingle() < 0.0f ||
            normalDerivation.GetProperty("sampleRadiusPixels").GetInt32() <= 0 ||
            normalDerivation.GetProperty("strength").GetSingle() <= 0.0f)
            throw new InvalidOperationException(
                "Fallout 2 Arroyo molded normal derivation drifted.");

        var provenanceDescriptor = descriptor.GetProperty("provenance");
        var provenancePath = Fo2TemplePresentationCatalog.ResolvePath(
            Fo2TemplePresentationCatalog.RequiredString(provenanceDescriptor, "file"),
            cacheRoot);
        var provenanceSha256 = Fo2TemplePresentationCatalog.RequiredHash(
            provenanceDescriptor,
            "sha256");
        var provenanceBytes = Fo2TemplePresentationCatalog.VerifyFile(
            provenancePath,
            provenanceSha256,
            provenanceDescriptor.GetProperty("bytes").GetInt64(),
            "Fallout 2 Arroyo molded wall provenance");
        using var provenanceDocument = JsonDocument.Parse(provenanceBytes);
        var provenance = provenanceDocument.RootElement;
        if (Fo2TemplePresentationCatalog.RequiredString(provenance, "schema") !=
                MoldedSurfaceSchema ||
            Fo2TemplePresentationCatalog.RequiredString(provenance, "status") !=
                MoldedSurfaceStatus ||
            Fo2TemplePresentationCatalog.RequiredString(provenance, "mode") !=
                MoldedSurfaceMode ||
            provenance.GetProperty("generatedMesh").GetBoolean() ||
            provenance.GetProperty("distributionAllowed").GetBoolean() ||
            Fo2TemplePresentationCatalog.RequiredString(
                provenance.GetProperty("normalDerivation"),
                "mode") != MoldedSurfaceNormalMode)
            throw new InvalidOperationException(
                "Fallout 2 Arroyo molded wall provenance policy drifted.");
        var provenanceNormalDerivation = provenance.GetProperty("normalDerivation");
        if (Fo2TemplePresentationCatalog.RequiredString(
                provenanceNormalDerivation,
                "mode") != MoldedSurfaceNormalMode ||
            provenanceNormalDerivation.GetProperty("blurRadiusPixels").GetSingle() !=
                normalDerivation.GetProperty("blurRadiusPixels").GetSingle() ||
            provenanceNormalDerivation.GetProperty("sampleRadiusPixels").GetInt32() !=
                normalDerivation.GetProperty("sampleRadiusPixels").GetInt32() ||
            provenanceNormalDerivation.GetProperty("strength").GetSingle() !=
                normalDerivation.GetProperty("strength").GetSingle() ||
            provenanceNormalDerivation.GetProperty("periodic").GetBoolean() !=
                normalDerivation.GetProperty("periodic").GetBoolean())
            throw new InvalidOperationException(
                "Fallout 2 Arroyo molded normal provenance closure failed.");

        var recipe = provenance.GetProperty("recipe");
        var descriptorRecipe = descriptor.GetProperty("recipe");
        var recipePath = Fo2TemplePresentationCatalog.ResolvePath(
            Fo2TemplePresentationCatalog.RequiredString(recipe, "file"),
            cacheRoot);
        var recipeId = Fo2TemplePresentationCatalog.RequiredString(recipe, "id");
        var recipeSha256 = Fo2TemplePresentationCatalog.RequiredHash(recipe, "sha256");
        if (Fo2TemplePresentationCatalog.RequiredString(recipe, "schema") !=
                MoldedSurfaceRecipeSchema ||
            Fo2TemplePresentationCatalog.RequiredString(descriptorRecipe, "file") !=
                Fo2TemplePresentationCatalog.RequiredString(recipe, "file") ||
            Fo2TemplePresentationCatalog.RequiredString(descriptorRecipe, "id") != recipeId ||
            Fo2TemplePresentationCatalog.RequiredHash(descriptorRecipe, "sha256") !=
                recipeSha256)
            throw new InvalidOperationException(
                "Fallout 2 Arroyo molded wall recipe descriptor drifted.");
        var recipeBytes = Fo2TemplePresentationCatalog.VerifyFile(
            recipePath,
            recipeSha256,
            null,
            "Fallout 2 Arroyo molded wall recipe");
        using (var recipeDocument = JsonDocument.Parse(recipeBytes))
        {
            var recipeRoot = recipeDocument.RootElement;
            if (Fo2TemplePresentationCatalog.RequiredString(recipeRoot, "schema") !=
                    MoldedSurfaceRecipeSchema ||
                Fo2TemplePresentationCatalog.RequiredString(recipeRoot, "id") != recipeId ||
                Fo2TemplePresentationCatalog.RequiredString(recipeRoot, "campaign") !=
                    "Fallout2")
                throw new InvalidOperationException(
                    "Fallout 2 Arroyo molded wall recipe identity drifted.");
        }

        var authority = provenance.GetProperty("authority");
        var sourceSerialsSha256 = Fo2TemplePresentationCatalog.RequiredHash(
            authority,
            "sourceSerialsSha256");
        if (Fo2TemplePresentationCatalog.RequiredHash(authority, "mapSha256") != mapSha256 ||
            authority.GetProperty("elevation").GetInt32() != Elevation ||
            authority.GetProperty("prototypeObjectType").GetInt32() != 3 ||
            !authority.GetProperty("topLevelOnly").GetBoolean() ||
            authority.GetProperty("wallObjects").GetInt32() != ExpectedSourceWallObjects ||
            authority.GetProperty("uniqueWallArtifacts").GetInt32() !=
                ExpectedSourceWallArtifacts ||
            authority.GetProperty("opaqueWallArtifacts").GetInt32() !=
                ExpectedOpaqueSourceWallArtifacts ||
            authority.GetProperty("nonDefaultFloorPatches").GetInt32() !=
                ExpectedSourceFloorPatches ||
            authority.GetProperty("uniqueFloorArtifacts").GetInt32() !=
                ExpectedSourceFloorArtifacts)
            throw new InvalidOperationException(
                "Fallout 2 Arroyo molded wall MAP authority drifted.");

        var sources = provenance.GetProperty("sources");
        VerifyMoldedSources(
            sources.GetProperty("walls"),
            artifacts,
            ExpectedSourceWallArtifacts,
            "wall");
        VerifyMoldedSources(
            sources.GetProperty("floors"),
            artifacts,
            ExpectedSourceFloorArtifacts,
            "floor");
        var output = provenance.GetProperty("output");
        if (Fo2TemplePresentationCatalog.RequiredHash(
                output.GetProperty("wall"),
                "sha256") != wallTexture.Sha256 ||
            Fo2TemplePresentationCatalog.RequiredHash(
                output.GetProperty("floor"),
                "sha256") != floorTexture.Sha256 ||
            Fo2TemplePresentationCatalog.RequiredHash(
                output.GetProperty("wallNormal"),
                "sha256") != wallNormalTexture.Sha256 ||
            Fo2TemplePresentationCatalog.RequiredHash(
                output.GetProperty("floorNormal"),
                "sha256") != floorNormalTexture.Sha256)
            throw new InvalidOperationException(
                "Fallout 2 Arroyo molded surface output closure failed.");

        return new Fo2ArroyoMoldedSurface(
            wallTexture.Path,
            wallTexture.Sha256,
            wallNormalTexture.Path,
            wallNormalTexture.Sha256,
            floorTexture.Path,
            floorTexture.Sha256,
            floorNormalTexture.Path,
            floorNormalTexture.Sha256,
            provenancePath,
            provenanceSha256,
            recipeSha256,
            ExpectedSourceWallObjects,
            ExpectedSourceWallArtifacts,
            ExpectedOpaqueSourceWallArtifacts,
            ExpectedSourceFloorPatches,
            ExpectedSourceFloorArtifacts,
            sourceSerialsSha256,
            MoldedSurfaceMode);
    }

    private static (string Path, string Sha256) LoadMoldedTexture(
        JsonElement descriptor,
        string cacheRoot,
        string label)
    {
        var path = Fo2TemplePresentationCatalog.ResolvePath(
            Fo2TemplePresentationCatalog.RequiredString(descriptor, "file"),
            cacheRoot);
        var sha256 = Fo2TemplePresentationCatalog.RequiredHash(descriptor, "sha256");
        var bytes = Fo2TemplePresentationCatalog.VerifyFile(
            path,
            sha256,
            descriptor.GetProperty("bytes").GetInt64(),
            label);
        if (descriptor.GetProperty("width").GetInt32() <= 0 ||
            descriptor.GetProperty("width").GetInt32() !=
                descriptor.GetProperty("height").GetInt32() ||
            bytes.Length == 0)
            throw new InvalidOperationException($"{label} dimensions drifted.");
        return (path, sha256);
    }

    private static void VerifyMoldedSources(
        JsonElement sources,
        IReadOnlyDictionary<string, Fo2MapArtifact> artifacts,
        int expectedCount,
        string label)
    {
        var sourceIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in sources.EnumerateArray())
        {
            var artifactId = Fo2TemplePresentationCatalog.RequiredString(source, "artifactId");
            if (!sourceIds.Add(artifactId) ||
                !artifacts.TryGetValue(artifactId, out var artifact) ||
                Fo2TemplePresentationCatalog.RequiredString(source, "logicalPath") !=
                    artifact.LogicalPath ||
                Fo2TemplePresentationCatalog.RequiredHash(source, "sourceSha256") !=
                    artifact.SourceSha256 ||
                Fo2TemplePresentationCatalog.RequiredHash(source, "pngSha256") !=
                    artifact.PngSha256)
                throw new InvalidOperationException(
                    $"Fallout 2 Arroyo molded {label} source-artifact closure failed.");
        }
        if (sourceIds.Count != expectedCount)
            throw new InvalidOperationException(
                $"Fallout 2 Arroyo molded {label} source count drifted.");
    }

    private static Fo2ArroyoExitTransition LoadLiveExit(
        JsonElement source,
        Fo2TempleTransitionCatalog transition,
        IReadOnlyDictionary<int, Fo2TemplePresentationCatalog.SourceObject> sourceObjects,
        IReadOnlyList<bool> walkable,
        int arrivalTile,
        string mapSha256)
    {
        var reciprocal = source.GetProperty("reciprocalTempleExitGrids")
            .EnumerateArray().ToArray();
        var component = ReachableTiles(arrivalTile, walkable);
        if (reciprocal.Any(row =>
                row.GetProperty("reachableFromIncomingPlacement").GetBoolean() !=
                component.Contains(row.GetProperty("tile").GetInt32())))
            throw new InvalidOperationException(
                "Fallout 2 Arroyo Caves reciprocal exit reachability drifted.");
        var reachable = reciprocal
            .Where(row => component.Contains(row.GetProperty("tile").GetInt32()))
            .Select(row => new
            {
                Row = row,
                Serial = row.GetProperty("serial").GetInt32(),
                Path = ShortestPath(arrivalTile, row.GetProperty("tile").GetInt32(), walkable),
            })
            .OrderBy(row => row.Path.Count)
            .ThenBy(row => row.Serial)
            .ToArray();
        if (reachable.Length == 0)
            throw new InvalidOperationException(
                "Fallout 2 Arroyo Caves has no source-walkable reciprocal exit.");
        var expected = reachable[0];
        var declared = source.GetProperty("liveExitTransition");
        var declaredSource = declared.GetProperty("source");
        var declaredDestination = declared.GetProperty("destination");
        var declaredPath = declared.GetProperty("path").EnumerateArray()
            .Select(row => row.GetInt32()).ToArray();
        var destination = expected.Row.GetProperty("destination");
        var sourceTile = expected.Row.GetProperty("tile").GetInt32();
        var sourceElevation = expected.Row.GetProperty("elevation").GetInt32();
        var exitFid = Fo2TemplePresentationCatalog.RequiredString(expected.Row, "fid");
        var exitPid = Fo2TemplePresentationCatalog.RequiredString(expected.Row, "pid");
        if (Fo2TemplePresentationCatalog.RequiredString(declared, "selection") !=
                "shortest-source-walk-path-then-serial-v1" ||
            declaredSource.GetProperty("mapIndex").GetInt32() != MapIndex ||
            Fo2TemplePresentationCatalog.RequiredHash(declaredSource, "mapSha256") != mapSha256 ||
            declaredSource.GetProperty("exitSerial").GetInt32() != expected.Serial ||
            declaredSource.GetProperty("tile").GetInt32() != sourceTile ||
            declaredSource.GetProperty("elevation").GetInt32() != sourceElevation ||
            Fo2TemplePresentationCatalog.RequiredString(declaredSource, "fid") != exitFid ||
            Fo2TemplePresentationCatalog.RequiredString(declaredSource, "pid") != exitPid ||
            !declaredPath.SequenceEqual(expected.Path) ||
            declared.GetProperty("pathSteps").GetInt32() != expected.Path.Count - 1 ||
            Fo2TemplePresentationCatalog.RequiredHash(declared, "pathSha256") !=
                Fo2TempleMovementConsumer.PathSha256(expected.Path) ||
            declaredDestination.GetProperty("mapIndex").GetInt32() !=
                destination.GetProperty("mapIndex").GetInt32() ||
            declaredDestination.GetProperty("tile").GetInt32() !=
                destination.GetProperty("tile").GetInt32() ||
            declaredDestination.GetProperty("elevation").GetInt32() !=
                destination.GetProperty("elevation").GetInt32() ||
            declaredDestination.GetProperty("rotation").GetInt32() !=
                destination.GetProperty("rotation").GetInt32() ||
            declaredDestination.GetProperty("mapIndex").GetInt32() !=
                Fo2TemplePresentationCatalog.MapIndex ||
            Fo2TemplePresentationCatalog.RequiredString(declaredDestination, "logicalPath") !=
                "maps\\artemple.map" ||
            Fo2TemplePresentationCatalog.RequiredHash(declaredDestination, "mapSha256") !=
                transition.SourceMapSha256 ||
            !sourceObjects.TryGetValue(expected.Serial, out var sourceObject) ||
            sourceObject.ObjectType != 5 ||
            sourceObject.Tile != sourceTile ||
            sourceObject.Elevation != sourceElevation ||
            sourceObject.Fid != exitFid ||
            sourceObject.Pid != exitPid ||
            sourceObject.InstanceValues.Count != 4 ||
            sourceObject.InstanceValues[0] != Fo2TemplePresentationCatalog.MapIndex ||
            sourceObject.InstanceValues[1] != declaredDestination.GetProperty("tile").GetInt32() ||
            sourceObject.InstanceValues[2] != declaredDestination.GetProperty("elevation").GetInt32() ||
            sourceObject.InstanceValues[3] != declaredDestination.GetProperty("rotation").GetInt32())
            throw new InvalidOperationException(
                "Fallout 2 Arroyo Caves live exit transition drifted from source records.");
        return new Fo2ArroyoExitTransition(
            expected.Serial,
            exitFid,
            exitPid,
            MapIndex,
            mapSha256,
            sourceTile,
            sourceElevation,
            expected.Path,
            Fo2TempleMovementConsumer.PathSha256(expected.Path),
            declaredDestination.GetProperty("mapIndex").GetInt32(),
            Fo2TemplePresentationCatalog.RequiredString(declaredDestination, "logicalPath"),
            Fo2TemplePresentationCatalog.RequiredHash(declaredDestination, "mapSha256"),
            declaredDestination.GetProperty("tile").GetInt32(),
            declaredDestination.GetProperty("elevation").GetInt32(),
            declaredDestination.GetProperty("rotation").GetInt32());
    }

    private static IReadOnlyList<int> ShortestPath(
        int start,
        int target,
        IReadOnlyList<bool> walkable)
    {
        if (start is < 0 or >= HexEntryCount || target is < 0 or >= HexEntryCount ||
            !walkable[start] || !walkable[target])
            throw new InvalidOperationException(
                "Fallout 2 Arroyo Caves live exit endpoint is not source-walkable.");
        var parents = new Dictionary<int, int> { [start] = -1 };
        var queue = new Queue<int>();
        queue.Enqueue(start);
        while (queue.Count > 0 && !parents.ContainsKey(target))
        {
            var tile = queue.Dequeue();
            foreach (var neighbor in Fo1HexMath.Neighbors(tile))
                if (walkable[neighbor] && !parents.ContainsKey(neighbor))
                {
                    parents.Add(neighbor, tile);
                    queue.Enqueue(neighbor);
                }
        }
        if (!parents.ContainsKey(target))
            throw new InvalidOperationException(
                "Fallout 2 Arroyo Caves reciprocal exit is outside the incoming component.");
        var reversed = new List<int>();
        for (var tile = target; tile >= 0; tile = parents[tile])
            reversed.Add(tile);
        reversed.Reverse();
        return reversed;
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
        => ReachableTiles(start, walkable).Count;

    private static HashSet<int> ReachableTiles(int start, IReadOnlyList<bool> walkable)
    {
        var visited = new HashSet<int> { start };
        var queue = new Queue<int>();
        queue.Enqueue(start);
        while (queue.Count > 0)
            foreach (var neighbor in Fo1HexMath.Neighbors(queue.Dequeue()))
                if (walkable[neighbor] && visited.Add(neighbor))
                    queue.Enqueue(neighbor);
        return visited;
    }
}
