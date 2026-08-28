using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;

namespace OpenNV.Runtime;

internal static class Fo1CampaignPresentationContractNumericContracts
{
    // Immutable format, source-art, geometry, and acceptance contracts.
    // Runtime-tunable Fallout 1 behavior remains in the versioned runtime recipe.
    internal const float PresentationFloat1Point08f = 1.08f;
    internal const int PresentationInt10 = 10;
    internal const int PresentationInt12 = 12;
    internal const int PresentationInt13 = 13;
    internal const int PresentationInt137 = 137;
    internal const int PresentationInt16 = 16;
    internal const int PresentationInt20 = 20;
    internal const int PresentationInt24 = 24;
    internal const int PresentationInt26 = 26;
    internal const int PresentationInt6 = 6;
    internal const int PresentationInt64 = 64;
    internal const int PresentationInt71 = 71;
    internal const int PresentationInt78 = 78;
    internal const int PresentationInt8 = 8;
    internal const int PresentationInt80 = 80;
}

internal static class Fo1CampaignPresentationContract
{
    private const string CampaignSchema = "opennv-fo1-campaign-presentation/v1";
    private const string MapSchema = "opennv-fo1-campaign-map-presentation/v1";
    private const int FloorEntryCount = Fo1HexMath.FloorWidth * Fo1HexMath.FloorHeight;

    internal static Fo1CampaignPresentationCatalog Load(string path)
    {
        var campaignPath = Path.GetFullPath(path);
        var campaignBytes = File.ReadAllBytes(campaignPath);
        var campaignSha256 = Sha256(campaignBytes);
        using var document = JsonDocument.Parse(campaignBytes);
        var root = document.RootElement;
        if (RequiredString(root, "schema") != CampaignSchema ||
            RequiredString(root, "status") != "prepared-source-reference-not-rendered" ||
            root.GetProperty("retailOrDerivedAssetsPackaged").GetBoolean())
            throw new InvalidOperationException("Unexpected Fallout campaign presentation contract.");

        var campaignRoot = Path.GetDirectoryName(campaignPath)!;
        var runtimeProfile = Fo1RuntimeProfile.Parse(root.GetProperty("runtimeProfile"));
        var presentation = root.GetProperty("presentation");
        var floorCenterContract = presentation.GetProperty("floorPatchCenters");
        if (RequiredString(floorCenterContract, "storage") != "derived-not-repeated" ||
            RequiredString(floorCenterContract, "algorithm") !=
                "fallout-100x100-isometric-floor-grid-v1")
            throw new InvalidOperationException("Fallout campaign floor-center derivation contract drifted.");
        var pixelsPerMeter = Positive(presentation, "pixelsPerMeter");
        var groundAnchorMeters = Finite(presentation, "groundAnchorMeters");
        var staticWorldYawDegrees = Finite(presentation, "staticWorldYawDegrees");
        var viewer = ReadViewerProfile(root.GetProperty("viewer"));
        if (!Mathf.IsEqualApprox(pixelsPerMeter, runtimeProfile.Scene.SourceSprites.PixelsPerMeter) ||
            !Mathf.IsEqualApprox(
                groundAnchorMeters,
                runtimeProfile.Scene.SourceSprites.GroundAnchorMeters))
            throw new InvalidOperationException("Fallout campaign sprite scale disagrees with its runtime profile.");

        var tileArtifacts = presentation.GetProperty("tileArtifacts").EnumerateArray()
            .Select(row => ReadTileArtifact(campaignRoot, row))
            .ToDictionary(row => row.Id);
        var spriteArtifacts = presentation.GetProperty("spriteArtifacts").EnumerateArray()
            .Select(row => ReadSpriteArtifact(campaignRoot, row))
            .ToDictionary(row => row.Id, StringComparer.Ordinal);
        if (tileArtifacts.Count == 0 || spriteArtifacts.Count == 0)
            throw new InvalidOperationException("Fallout campaign presentation has no source artifacts.");

        var playerArtifacts = presentation.GetProperty("playerArtifactsByRotation")
            .EnumerateObject()
            .ToDictionary(
                row => ParseRotation(row.Name),
                row => RequiredString(row.Value, $"player artifact rotation {row.Name}"),
                EqualityComparer<int>.Default);
        if (playerArtifacts.Count == 0 ||
            playerArtifacts.Values.Any(id => !spriteArtifacts.ContainsKey(id)))
            throw new InvalidOperationException("Fallout campaign player-artifact catalog is incomplete.");

        var critterProfiles = presentation.GetProperty("critterProfiles").EnumerateArray()
            .Select(ReadCritterProfile)
            .ToDictionary(row => row.Pid, StringComparer.OrdinalIgnoreCase);
        if (critterProfiles.Count == 0)
            throw new InvalidOperationException("Fallout campaign critter profiles are empty.");

        var mapRows = root.GetProperty("maps").EnumerateArray()
            .Select(row => ReadMapCatalogRow(campaignRoot, row))
            .ToArray();
        if (mapRows.Length == 0 ||
            mapRows.Select(row => row.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != mapRows.Length ||
            mapRows.Select(row => row.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != mapRows.Length)
            throw new InvalidOperationException("Fallout campaign presentation map catalog is empty or duplicated.");
        if (!mapRows.Any(row => row.Id.Equals(viewer.DefaultMapId, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException(
                $"Fallout campaign viewer default map is absent: {viewer.DefaultMapId}");

        var catalog = new Fo1CampaignPresentationCatalog(
            campaignPath,
            campaignSha256,
            runtimeProfile,
            pixelsPerMeter,
            groundAnchorMeters,
            staticWorldYawDegrees,
            viewer,
            tileArtifacts,
            spriteArtifacts,
            playerArtifacts,
            critterProfiles,
            mapRows,
            Array.Empty<Fo1CampaignMapPresentationCoverage>());
        var mapCoverage = mapRows.Select(row => LoadMap(catalog, row.Id).Coverage).ToArray();
        ValidateCampaignCoverage(root, catalog, mapCoverage);
        return catalog with { MapCoverage = mapCoverage };
    }

    internal static Fo1CampaignMapPresentation LoadMap(
        Fo1CampaignPresentationCatalog catalog,
        string mapId)
    {
        var catalogRow = catalog.Maps.SingleOrDefault(
            row => row.Id.Equals(mapId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Fallout campaign map is absent: {mapId}");
        var bytes = File.ReadAllBytes(catalogRow.Path);
        if (Sha256(bytes) != catalogRow.Sha256)
            throw new InvalidOperationException($"Fallout campaign presentation map hash drifted: {mapId}");
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        if (RequiredString(root, "schema") != MapSchema ||
            RequiredString(root, "status") != "prepared-source-reference" ||
            !RequiredString(root, "id").Equals(catalogRow.Id, StringComparison.Ordinal) ||
            root.GetProperty("retailOrDerivedAssetsPackaged").GetBoolean())
            throw new InvalidOperationException($"Unexpected Fallout campaign presentation map: {mapId}");

        var defaultTileId = ValidateGrid(root.GetProperty("grid"));
        var entry = ReadEntry(root.GetProperty("entry"), catalog);
        var elevations = root.GetProperty("elevations").EnumerateArray()
            .Select(row => ReadElevation(catalogRow.Id, row, catalog, defaultTileId))
            .ToArray();
        if (elevations.Length == 0 ||
            elevations.Select(row => row.Elevation).Distinct().Count() != elevations.Length ||
            !elevations.Any(row => row.Elevation == entry.Elevation))
            throw new InvalidOperationException($"Fallout campaign elevations are invalid: {mapId}");

        var coverage = ReadAndValidateMapCoverage(
            catalogRow,
            root.GetProperty("coverage"),
            elevations);
        var promotion = root.GetProperty("promotion");
        if (!promotion.GetProperty("transported").GetBoolean() ||
            !promotion.GetProperty("sourceReferencePrepared").GetBoolean() ||
            promotion.GetProperty("rendered").GetBoolean() ||
            promotion.GetProperty("interactive").GetBoolean() ||
            promotion.GetProperty("questExecutable").GetBoolean() ||
            promotion.GetProperty("firstPersonReady").GetBoolean() ||
            promotion.GetProperty("openXrAccepted").GetBoolean())
            throw new InvalidOperationException($"Fallout campaign map promotion drifted: {mapId}");
        return new Fo1CampaignMapPresentation(
            catalogRow.Id,
            catalogRow.SourceFile,
            entry,
            elevations,
            coverage);
    }

    private static Fo1CampaignElevationPresentation ReadElevation(
        string mapId,
        JsonElement source,
        Fo1CampaignPresentationCatalog catalog,
        int defaultTileId)
    {
        var elevation = source.GetProperty("elevation").GetInt32();
        if (elevation is < 0 or > 2)
            throw new InvalidOperationException($"Fallout campaign elevation is invalid: {mapId}/{elevation}");
        var floorIds = source.GetProperty("floorIds").EnumerateArray()
            .Select(row => row.GetInt32()).ToArray();
        var roofIds = source.GetProperty("roofIds").EnumerateArray()
            .Select(row => row.GetInt32()).ToArray();
        if (floorIds.Length != FloorEntryCount || roofIds.Length != FloorEntryCount ||
            floorIds.Any(id => !catalog.TileArtifacts.ContainsKey(id)) ||
            roofIds.Any(id => !catalog.TileArtifacts.ContainsKey(id)))
            throw new InvalidOperationException($"Fallout campaign tile-art coverage drifted: {mapId}/{elevation}");
        ValidateRawGridHash(source, floorIds, roofIds, mapId, elevation);

        var placements = source.GetProperty("placements").EnumerateArray()
            .Select(row => ReadPlacement(mapId, elevation, row, catalog))
            .ToArray();
        var placementSerials = placements.Select(row => row.Serial).ToHashSet();
        if (placementSerials.Count != placements.Length)
            throw new InvalidOperationException($"Fallout campaign placement serials are duplicated: {mapId}/{elevation}");
        var wallTopology = ReadWallTopology(
            mapId,
            elevation,
            source.GetProperty("wallTopology"),
            floorIds,
            defaultTileId);
        var skipped = source.GetProperty("skippedPlacements").EnumerateArray()
            .Select(row => new Fo1CampaignSkippedPlacement(
                row.GetProperty("serial").GetInt32(),
                RequiredString(row, "reason")))
            .ToArray();
        if (skipped.Select(row => row.Serial).Distinct().Count() != skipped.Length)
            throw new InvalidOperationException($"Fallout skipped placement serials are duplicated: {mapId}/{elevation}");
        var blockers = source.GetProperty("blockers").EnumerateArray()
            .Select(row => ReadBlocker(mapId, elevation, row))
            .ToArray();
        if (blockers.Select(row => row.Serial).Distinct().Count() != blockers.Length)
            throw new InvalidOperationException($"Fallout blocker serials are duplicated: {mapId}/{elevation}");
        var mobs = source.GetProperty("mobs").EnumerateArray()
            .Select(row => ReadMob(mapId, elevation, row, placementSerials, catalog))
            .ToArray();
        var doors = source.GetProperty("doors").EnumerateArray()
            .Select(row => ReadDoor(mapId, elevation, row, placementSerials))
            .ToArray();
        if (mobs.Select(row => row.Serial).Distinct().Count() != mobs.Length ||
            doors.Select(row => row.Serial).Distinct().Count() != doors.Length)
            throw new InvalidOperationException($"Fallout mob or door serials are duplicated: {mapId}/{elevation}");

        var blockedTiles = blockers.Select(row => row.Tile).ToHashSet();
        var walkable = Enumerable.Range(0, Fo1HexMath.Width * Fo1HexMath.Height).Count(
            tile => floorIds[Fo1HexMath.FloorIndex(tile)] != defaultTileId &&
                !blockedTiles.Contains(tile));
        if (source.GetProperty("provisionalWalkableHexes").GetInt32() != walkable)
            throw new InvalidOperationException($"Fallout provisional walkability drifted: {mapId}/{elevation}");
        ValidateElevationCoverage(
            source.GetProperty("coverage"),
            placements,
            skipped,
            blockers,
            mobs,
            doors,
            wallTopology);
        return new Fo1CampaignElevationPresentation(
            elevation,
            floorIds,
            roofIds,
            placements,
            skipped,
            blockers,
            mobs,
            doors,
            wallTopology,
            walkable);
    }

    private static Fo1CampaignPlacement ReadPlacement(
        string mapId,
        int elevation,
        JsonElement source,
        Fo1CampaignPresentationCatalog catalog)
    {
        var serial = source.GetProperty("serial").GetInt32();
        var tile = source.GetProperty("tile").GetInt32();
        var rotation = source.GetProperty("rotation").GetInt32();
        var objectType = source.GetProperty("objectType").GetInt32();
        var artifactId = RequiredString(source, "artifactId");
        if (tile is < 0 or >= Fo1HexMath.Width * Fo1HexMath.Height ||
            rotation is < 0 or >= Fo1HexMath.DirectionCount ||
            objectType is < 0 or > Fo1CampaignPresentationContractNumericContracts.PresentationInt6 ||
            !catalog.SpriteArtifacts.ContainsKey(artifactId))
            throw new InvalidOperationException(
                $"Fallout campaign placement is invalid: {mapId}/{elevation}/{serial}");
        var hex = ReadIntPair(source.GetProperty("hex"));
        if (hex != Fo1HexMath.Coordinate(tile))
            throw new InvalidOperationException(
                $"Fallout campaign placement hex drifted: {mapId}/{elevation}/{serial}");
        var world = ReadVector3(source.GetProperty("worldMeters"));
        if (!world.IsEqualApprox(Fo1HexMath.Center(tile)))
            throw new InvalidOperationException(
                $"Fallout campaign placement world position drifted: {mapId}/{elevation}/{serial}");
        return new Fo1CampaignPlacement(
            serial,
            source.GetProperty("objectId").GetInt32(),
            tile,
            world,
            rotation,
            ReadVector2(source.GetProperty("pixelOffset")),
            objectType,
            RequiredString(source, "objectTypeName"),
            RequiredString(source, "artFilename"),
            artifactId);
    }

    private static Fo1CampaignWallTopology ReadWallTopology(
        string mapId,
        int elevation,
        JsonElement source,
        IReadOnlyList<int> floorIds,
        int defaultTileId)
    {
        if (RequiredString(source, "schema") != "opennv-fo1-connected-wall-topology/v1" ||
            RequiredString(source, "mode") != "source-wall-hex-union-v1" ||
            RequiredString(source, "complexity") !=
                "topology O(source-wall-objects + occupied-wall-hexes + exposed-edges); " +
                "canonical ordering O(occupied-wall-hexes log occupied-wall-hexes)")
            throw new InvalidOperationException(
                $"Fallout connected-wall topology contract drifted: {mapId}/{elevation}");
        var cells = source.GetProperty("cells").EnumerateArray()
            .Select(row =>
            {
                var tile = row.GetProperty("tile").GetInt32();
                if (tile is < 0 or >= Fo1HexMath.Width * Fo1HexMath.Height)
                    throw new InvalidOperationException(
                        $"Fallout wall-topology tile is invalid: {mapId}/{elevation}/{tile}");
                var objects = row.GetProperty("sourceObjects").EnumerateArray()
                    .Select(value =>
                    {
                        var rotation = value.GetProperty("rotation").GetInt32();
                        if (rotation is < 0 or >= Fo1HexMath.DirectionCount)
                            throw new InvalidOperationException(
                                $"Fallout wall rotation is invalid: {mapId}/{elevation}/{tile}");
                        var art = value.GetProperty("artFilename");
                        return new Fo1CampaignWallSource(
                            value.GetProperty("serial").GetInt32(),
                            rotation,
                            art.ValueKind == JsonValueKind.Null ? null : art.GetString(),
                            value.GetProperty("blocking").GetBoolean());
                    })
                    .ToArray();
                if (objects.Length == 0)
                    throw new InvalidOperationException(
                        $"Fallout wall-topology cell has no source: {mapId}/{elevation}/{tile}");
                return new Fo1CampaignWallCell(tile, objects);
            })
            .ToArray();
        if (cells.Select(row => row.Tile).Distinct().Count() != cells.Length ||
            cells.SelectMany(row => row.SourceObjects).Select(row => row.Serial).Distinct().Count() !=
                cells.Sum(row => row.SourceObjects.Count))
            throw new InvalidOperationException(
                $"Fallout connected-wall cells or serials are duplicated: {mapId}/{elevation}");
        var onGridSourceObjects = source.GetProperty("onGridSourceWallObjects").GetInt32();
        var offGridSourceObjects = source.GetProperty("offGridSourceWallObjects").GetInt32();
        var sourceWallObjects = source.GetProperty("sourceWallObjects").GetInt32();
        if (onGridSourceObjects < 0 || offGridSourceObjects < 0 ||
            sourceWallObjects != onGridSourceObjects + offGridSourceObjects ||
            onGridSourceObjects != cells.Sum(row => row.SourceObjects.Count) ||
            Fo1CampaignWallTopologyMath.OccupiedHexSha256(cells) !=
                HexString(source, "occupiedHexesSha256", Fo1CampaignPresentationContractNumericContracts.PresentationInt64))
            throw new InvalidOperationException(
                $"Fallout connected-wall source coverage drifted: {mapId}/{elevation}");
        var actual = Fo1CampaignWallTopologyMath.Analyze(cells, floorIds, defaultTileId);
        var declared = source.GetProperty("coverage");
        RequireCount(declared, "occupiedHexes", actual.OccupiedHexes);
        RequireCount(declared, "blockingHexes", actual.BlockingHexes);
        RequireCount(declared, "nonBlockingHexes", actual.NonBlockingHexes);
        RequireCount(declared, "connectedComponents", actual.ConnectedComponents);
        RequireCount(declared, "largestComponentHexes", actual.LargestComponentHexes);
        RequireCount(declared, "isolatedHexes", actual.IsolatedHexes);
        RequireCount(declared, "boundaryEdges", actual.BoundaryEdges);
        RequireCount(declared, "floorFacingBoundaryEdges", actual.FloorFacingBoundaryEdges);
        RequireCount(declared, "voidFacingBoundaryEdges", actual.VoidFacingBoundaryEdges);
        return new Fo1CampaignWallTopology(
            cells,
            sourceWallObjects,
            onGridSourceObjects,
            offGridSourceObjects,
            actual);
    }

    private static Fo1CampaignBlocker ReadBlocker(
        string mapId,
        int elevation,
        JsonElement source)
    {
        var tile = source.GetProperty("tile").GetInt32();
        if (tile is < 0 or >= Fo1HexMath.Width * Fo1HexMath.Height)
            throw new InvalidOperationException($"Fallout blocker tile is invalid: {mapId}/{elevation}");
        _ = HexString(source, "flags", Fo1CampaignPresentationContractNumericContracts.PresentationInt8);
        return new Fo1CampaignBlocker(
            source.GetProperty("serial").GetInt32(),
            tile,
            source.GetProperty("multihex").GetBoolean());
    }

    private static Fo1CampaignMob ReadMob(
        string mapId,
        int elevation,
        JsonElement source,
        IReadOnlySet<int> placementSerials,
        Fo1CampaignPresentationCatalog catalog)
    {
        var serial = source.GetProperty("serial").GetInt32();
        var profileId = RequiredString(source, "profileId");
        if (!placementSerials.Contains(serial) || !catalog.CritterProfiles.ContainsKey(profileId))
            throw new InvalidOperationException($"Fallout mob reference is invalid: {mapId}/{elevation}/{serial}");
        return new Fo1CampaignMob(
            serial,
            profileId,
            source.GetProperty("currentHitPoints").GetInt32(),
            source.GetProperty("currentActionPoints").GetInt32(),
            source.GetProperty("runtimeAiPacket").GetInt32(),
            source.GetProperty("runtimeTeam").GetInt32());
    }

    private static Fo1CampaignDoor ReadDoor(
        string mapId,
        int elevation,
        JsonElement source,
        IReadOnlySet<int> placementSerials)
    {
        var serial = source.GetProperty("serial").GetInt32();
        if (!placementSerials.Contains(serial))
            throw new InvalidOperationException($"Fallout door placement is absent: {mapId}/{elevation}/{serial}");
        var instanceValues = source.GetProperty("instanceValues").EnumerateArray()
            .Select(row => row.GetInt32()).ToArray();
        if (instanceValues.Length == 0)
            throw new InvalidOperationException($"Fallout door state is empty: {mapId}/{elevation}/{serial}");
        return new Fo1CampaignDoor(
            serial,
            HexString(source, "instanceFlags", Fo1CampaignPresentationContractNumericContracts.PresentationInt8),
            instanceValues);
    }

    private static Fo1CampaignMapEntry ReadEntry(
        JsonElement source,
        Fo1CampaignPresentationCatalog catalog)
    {
        var tile = source.GetProperty("tile").GetInt32();
        var elevation = source.GetProperty("elevation").GetInt32();
        var rotation = source.GetProperty("rotation").GetInt32();
        var artifactId = RequiredString(source, "playerArtifactId");
        if (tile is < 0 or >= Fo1HexMath.Width * Fo1HexMath.Height ||
            elevation is < 0 or > 2 || rotation is < 0 or >= Fo1HexMath.DirectionCount ||
            !catalog.SpriteArtifacts.ContainsKey(artifactId) ||
            !catalog.PlayerArtifacts.TryGetValue(rotation, out var expectedArtifact) ||
            expectedArtifact != artifactId)
            throw new InvalidOperationException("Fallout campaign map entry is invalid.");
        var world = ReadVector3(source.GetProperty("worldMeters"));
        if (!world.IsEqualApprox(Fo1HexMath.Center(tile)))
            throw new InvalidOperationException("Fallout campaign entry world position drifted.");
        return new Fo1CampaignMapEntry(tile, elevation, rotation, world, artifactId);
    }

    private static Fo1CampaignTileArtifact ReadTileArtifact(string root, JsonElement source)
    {
        var id = source.GetProperty("id").GetInt32();
        if (id is < 0 or > 0x0FFF)
            throw new InvalidOperationException($"Fallout tile-art ID is invalid: {id}");
        var file = ValidatePngArtifact(root, source);
        _ = HexString(source, "sourceSha256", Fo1CampaignPresentationContractNumericContracts.PresentationInt64);
        return new Fo1CampaignTileArtifact(
            id,
            RequiredString(source, "filename"),
            file.Path,
            file.Sha256,
            file.Width,
            file.Height);
    }

    private static Fo1CampaignSpriteArtifact ReadSpriteArtifact(string root, JsonElement source)
    {
        var id = RequiredString(source, "id");
        if (id.Length != Fo1CampaignPresentationContractNumericContracts.PresentationInt20 || id.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidOperationException($"Fallout sprite-artifact ID is invalid: {id}");
        var file = ValidatePngArtifact(root, source);
        var rotation = source.GetProperty("rotation").GetInt32();
        var frame = source.GetProperty("frame").GetInt32();
        if (rotation is < 0 or >= Fo1HexMath.DirectionCount || frame < 0)
            throw new InvalidOperationException($"Fallout sprite frame is invalid: {id}");
        _ = HexString(source, "sourceSha256", Fo1CampaignPresentationContractNumericContracts.PresentationInt64);
        var averageSource = source.GetProperty("averageOpaqueColor");
        var averageOpaqueColor = averageSource.ValueKind == JsonValueKind.Null
            ? (Color?)null
            : ReadColor(averageSource, "sprite average opaque");
        return new Fo1CampaignSpriteArtifact(
            id,
            RequiredString(source, "logicalPath"),
            file.Path,
            file.Sha256,
            file.Width,
            file.Height,
            ReadVector2(source.GetProperty("frameOffset")),
            rotation,
            frame,
            averageOpaqueColor);
    }

    private static Fo1CampaignCritterProfile ReadCritterProfile(JsonElement source)
    {
        var pid = HexString(source, "pid", Fo1CampaignPresentationContractNumericContracts.PresentationInt8);
        _ = HexString(source, "prototypeSha256", Fo1CampaignPresentationContractNumericContracts.PresentationInt64);
        return new Fo1CampaignCritterProfile(
            pid,
            source.GetProperty("displayName").ValueKind == JsonValueKind.Null
                ? null
                : source.GetProperty("displayName").GetString(),
            source.GetProperty("hitPoints").GetInt32(),
            source.GetProperty("actionPoints").GetInt32(),
            source.GetProperty("armorClass").GetInt32(),
            source.GetProperty("meleeDamage").GetInt32(),
            source.GetProperty("sequence").GetInt32(),
            source.GetProperty("team").GetInt32(),
            source.GetProperty("aiPacket").GetInt32());
    }

    private static Fo1CampaignMapCatalogRow ReadMapCatalogRow(string root, JsonElement source)
    {
        var id = RequiredString(source, "id");
        if (id.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '_'))
            throw new InvalidOperationException($"Fallout campaign map ID is invalid: {id}");
        return new Fo1CampaignMapCatalogRow(
            id,
            RequiredString(source, "file"),
            ResolveChildPath(root, RequiredString(source, "path")),
            HexString(source, "sha256", Fo1CampaignPresentationContractNumericContracts.PresentationInt64),
            source.GetProperty("elevations").GetInt32(),
            source.GetProperty("spritePlacements").GetInt32(),
            source.GetProperty("skippedSpriteObjects").GetInt32(),
            source.GetProperty("mobs").GetInt32(),
            source.GetProperty("blockers").GetInt32(),
            source.GetProperty("doors").GetInt32(),
            source.GetProperty("wallObjects").GetInt32(),
            source.GetProperty("wallHexes").GetInt32(),
            source.GetProperty("wallComponents").GetInt32(),
            source.GetProperty("wallBoundaryEdges").GetInt32());
    }

    private static PngArtifact ValidatePngArtifact(string root, JsonElement source)
    {
        var path = ResolveChildPath(root, RequiredString(source, "path"));
        var expectedSha256 = HexString(source, "sha256", Fo1CampaignPresentationContractNumericContracts.PresentationInt64);
        var bytes = File.ReadAllBytes(path);
        if (Sha256(bytes) != expectedSha256 || bytes.Length < Fo1CampaignPresentationContractNumericContracts.PresentationInt24 ||
            !bytes.AsSpan(0, Fo1CampaignPresentationContractNumericContracts.PresentationInt8).SequenceEqual(new byte[] { Fo1CampaignPresentationContractNumericContracts.PresentationInt137, Fo1CampaignPresentationContractNumericContracts.PresentationInt80, Fo1CampaignPresentationContractNumericContracts.PresentationInt78, Fo1CampaignPresentationContractNumericContracts.PresentationInt71, Fo1CampaignPresentationContractNumericContracts.PresentationInt13, Fo1CampaignPresentationContractNumericContracts.PresentationInt10, Fo1CampaignPresentationContractNumericContracts.PresentationInt26, Fo1CampaignPresentationContractNumericContracts.PresentationInt10 }) ||
            !bytes.AsSpan(Fo1CampaignPresentationContractNumericContracts.PresentationInt12, 4).SequenceEqual("IHDR"u8))
            throw new InvalidOperationException($"Fallout prepared PNG is invalid: {path}");
        var width = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(Fo1CampaignPresentationContractNumericContracts.PresentationInt16, 4));
        var height = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(Fo1CampaignPresentationContractNumericContracts.PresentationInt20, 4));
        if (width <= 0 || height <= 0 ||
            source.GetProperty("width").GetInt32() != width ||
            source.GetProperty("height").GetInt32() != height)
            throw new InvalidOperationException($"Fallout prepared PNG dimensions drifted: {path}");
        return new PngArtifact(path, expectedSha256, width, height);
    }

    private static void ValidateRawGridHash(
        JsonElement source,
        IReadOnlyList<int> floors,
        IReadOnlyList<int> roofs,
        string mapId,
        int elevation)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> encoded = stackalloc byte[sizeof(uint)];
        for (var index = 0; index < floors.Count; index++)
        {
            var value = (uint)(floors[index] | roofs[index] << Fo1CampaignPresentationContractNumericContracts.PresentationInt16);
            BinaryPrimitives.WriteUInt32BigEndian(encoded, value);
            hash.AppendData(encoded);
        }
        var actual = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        if (actual != HexString(source, "rawGridSha256", Fo1CampaignPresentationContractNumericContracts.PresentationInt64))
            throw new InvalidOperationException(
                $"Fallout campaign raw grid hash drifted: {mapId}/{elevation}");
    }

    private static int ValidateGrid(JsonElement source)
    {
        var defaultTileId = source.GetProperty("defaultTileId").GetInt32();
        if (source.GetProperty("hexWidth").GetInt32() != Fo1HexMath.Width ||
            source.GetProperty("hexHeight").GetInt32() != Fo1HexMath.Height ||
            source.GetProperty("floorWidth").GetInt32() != Fo1HexMath.FloorWidth ||
            source.GetProperty("floorHeight").GetInt32() != Fo1HexMath.FloorHeight ||
            RequiredString(source, "layout") != "fallout-even-column-offset-flat-v1" ||
            defaultTileId is < 0 or > 0x0FFF ||
            !Mathf.IsEqualApprox(
                source.GetProperty("hexFlatToFlatMeters").GetSingle(),
                Fo1HexMath.FlatToFlatMeters))
            throw new InvalidOperationException("Fallout campaign grid contract drifted.");
        return defaultTileId;
    }

    private static Fo1CampaignMapPresentationCoverage ReadAndValidateMapCoverage(
        Fo1CampaignMapCatalogRow catalog,
        JsonElement source,
        IReadOnlyCollection<Fo1CampaignElevationPresentation> elevations)
    {
        var result = new Fo1CampaignMapPresentationCoverage(
            catalog.Id,
            elevations.Count,
            elevations.Sum(row => row.Placements.Count),
            elevations.Sum(row => row.SkippedPlacements.Count),
            elevations.Sum(row => row.Mobs.Count),
            elevations.Sum(row => row.Blockers.Count),
            elevations.Sum(row => row.Doors.Count),
            elevations.Sum(row => row.WallTopology.SourceWallObjects),
            elevations.Sum(row => row.WallTopology.Coverage.OccupiedHexes),
            elevations.Sum(row => row.WallTopology.Coverage.ConnectedComponents),
            elevations.Sum(row => row.WallTopology.Coverage.BoundaryEdges));
        RequireCount(source, "elevations", result.Elevations);
        RequireCount(source, "spritePlacements", result.SpritePlacements);
        RequireCount(source, "skippedSpriteObjects", result.SkippedSpriteObjects);
        RequireCount(source, "mobs", result.Mobs);
        RequireCount(source, "blockers", result.Blockers);
        RequireCount(source, "doors", result.Doors);
        RequireCount(source, "wallObjects", result.WallObjects);
        RequireCount(source, "wallHexes", result.WallHexes);
        RequireCount(source, "wallComponents", result.WallComponents);
        RequireCount(source, "wallBoundaryEdges", result.WallBoundaryEdges);
        if (catalog.Elevations != result.Elevations ||
            catalog.SpritePlacements != result.SpritePlacements ||
            catalog.SkippedSpriteObjects != result.SkippedSpriteObjects ||
            catalog.Mobs != result.Mobs || catalog.Blockers != result.Blockers ||
            catalog.Doors != result.Doors || catalog.WallObjects != result.WallObjects ||
            catalog.WallHexes != result.WallHexes ||
            catalog.WallComponents != result.WallComponents ||
            catalog.WallBoundaryEdges != result.WallBoundaryEdges)
            throw new InvalidOperationException($"Fallout campaign catalog coverage drifted: {catalog.Id}");
        return result;
    }

    private static void ValidateElevationCoverage(
        JsonElement source,
        IReadOnlyCollection<Fo1CampaignPlacement> placements,
        IReadOnlyCollection<Fo1CampaignSkippedPlacement> skipped,
        IReadOnlyCollection<Fo1CampaignBlocker> blockers,
        IReadOnlyCollection<Fo1CampaignMob> mobs,
        IReadOnlyCollection<Fo1CampaignDoor> doors,
        Fo1CampaignWallTopology wallTopology)
    {
        RequireCount(source, "spritePlacements", placements.Count);
        RequireCount(source, "skippedSpriteObjects", skipped.Count);
        RequireCount(source, "blockers", blockers.Count);
        RequireCount(source, "mobs", mobs.Count);
        RequireCount(source, "doors", doors.Count);
        RequireCount(source, "wallObjects", wallTopology.SourceWallObjects);
        RequireCount(source, "wallHexes", wallTopology.Coverage.OccupiedHexes);
        RequireCount(source, "wallComponents", wallTopology.Coverage.ConnectedComponents);
        RequireCount(source, "wallBoundaryEdges", wallTopology.Coverage.BoundaryEdges);
    }

    private static void ValidateCampaignCoverage(
        JsonElement root,
        Fo1CampaignPresentationCatalog catalog,
        IReadOnlyCollection<Fo1CampaignMapPresentationCoverage> maps)
    {
        var coverage = root.GetProperty("coverage");
        RequireCount(coverage, "maps", maps.Count);
        RequireCount(coverage, "elevations", maps.Sum(row => row.Elevations));
        RequireCount(coverage, "tileArtifacts", catalog.TileArtifacts.Count);
        RequireCount(coverage, "spriteArtifacts", catalog.SpriteArtifacts.Count);
        RequireCount(coverage, "spritePlacements", maps.Sum(row => row.SpritePlacements));
        RequireCount(coverage, "skippedSpriteObjects", maps.Sum(row => row.SkippedSpriteObjects));
        RequireCount(coverage, "mobs", maps.Sum(row => row.Mobs));
        RequireCount(coverage, "blockers", maps.Sum(row => row.Blockers));
        RequireCount(coverage, "doors", maps.Sum(row => row.Doors));
        RequireCount(coverage, "wallObjects", maps.Sum(row => row.WallObjects));
        RequireCount(coverage, "wallHexes", maps.Sum(row => row.WallHexes));
        RequireCount(coverage, "wallComponents", maps.Sum(row => row.WallComponents));
        RequireCount(coverage, "wallBoundaryEdges", maps.Sum(row => row.WallBoundaryEdges));
        RequireCount(coverage, "critterProfiles", catalog.CritterProfiles.Count);
        var promotion = root.GetProperty("promotion");
        RequireCount(promotion, "transportedMaps", maps.Count);
        RequireCount(promotion, "sourceReferencePreparedMaps", maps.Count);
        foreach (var name in new[]
                 {
                     "renderedMaps", "interactiveMaps", "questExecutableMaps",
                     "firstPersonReadyMaps", "openXrAcceptedMaps",
                 })
            RequireCount(promotion, name, 0);
    }

    private static Fo1CampaignViewerProfile ReadViewerProfile(JsonElement source)
    {
        var scene = source.GetProperty("scene");
        var wall = source.GetProperty("wallGeometry");
        var panel = source.GetProperty("statusPanel");
        var capture = source.GetProperty("capture");
        var status = new Fo1CampaignStatusPanelProfile(
            Finite(panel, "leftPixels"),
            Finite(panel, "topPixels"),
            Finite(panel, "rightPixels"),
            Finite(panel, "bottomPixels"),
            Finite(panel, "textLeftPixels"),
            Finite(panel, "textTopPixels"),
            Finite(panel, "textRightPixels"),
            Finite(panel, "textBottomPixels"),
            ReadColor(panel.GetProperty("panelColor"), "status panel"),
            ReadColor(panel.GetProperty("fontColor"), "status font"),
            PositiveInt(panel, "fontSizePixels"));
        if (status.LeftPixels >= status.RightPixels || status.TopPixels >= status.BottomPixels ||
            status.TextLeftPixels >= status.TextRightPixels ||
            status.TextTopPixels >= status.TextBottomPixels)
            throw new InvalidOperationException("Fallout campaign status-panel bounds are invalid.");
        var captureProfile = new Fo1CampaignCaptureProfile(
            PositiveInt(capture, "warmupFrames"),
            PositiveInt(capture, "settleFrames"),
            PositiveInt(capture, "expectedWidthPixels"),
            PositiveInt(capture, "expectedHeightPixels"),
            Unit(capture, "darkPixelLuminance"),
            Unit(capture, "minimumMeanLuminance"),
            Unit(capture, "minimumLuminanceDeviation"),
            Unit(capture, "maximumDarkFraction"));
        var wallProfile = new Fo1CampaignWallGeometryProfile(
            RequiredString(wall, "mode"),
            wall.GetProperty("sourceObjectType").GetInt32(),
            RequiredString(wall, "collisionMode"),
            Positive(wall, "cellRadiusScale"),
            Positive(wall, "heightMeters"),
            Finite(wall, "groundSinkMeters"),
            Unit(wall, "roughness"),
            Unit(wall, "metallic"),
            Unit(wall, "sourceAlphaThreshold"),
            ReadColor(wall.GetProperty("unresolvedSourceAlbedo"), "unresolved wall source"),
            ReadColor(wall.GetProperty("sideColorMultiplier"), "wall side multiplier"),
            ReadColor(wall.GetProperty("topColorMultiplier"), "wall top multiplier"));
        if (wallProfile.Mode != "source-wall-hex-union-v1" ||
            wallProfile.SourceObjectType != 3 ||
            wallProfile.CollisionMode != "blocking-wall-hex-union-v1" ||
            wallProfile.CellRadiusScale is < 1.0f or > Fo1CampaignPresentationContractNumericContracts.PresentationFloat1Point08f ||
            wallProfile.HeightMeters <= 1.0f || wallProfile.GroundSinkMeters < 0.0f ||
            wallProfile.GroundSinkMeters >= wallProfile.HeightMeters)
            throw new InvalidOperationException("Fallout campaign wall-geometry profile drifted.");
        return new Fo1CampaignViewerProfile(
            RequiredString(source, "defaultMapId"),
            new Fo1CampaignViewerSceneProfile(
                RequiredString(scene, "sourceSpriteOrientation"),
                scene.GetProperty("sourceReferenceOrbitEnabled").GetBoolean(),
                scene.GetProperty("sourceReferenceVisibleByDefault").GetBoolean(),
                ReadPositiveColor(scene.GetProperty("sourceColorMultiplier"), "source multiplier"),
                Positive(scene, "tonemapExposure"),
                Unit(scene, "fogDensity"),
                Unit(scene, "fogAerialPerspective")),
            wallProfile,
            status,
            captureProfile);
    }

    private static int ParseRotation(string value)
    {
        if (!int.TryParse(value, out var rotation) || rotation is < 0 or >= Fo1HexMath.DirectionCount)
            throw new InvalidOperationException($"Fallout player-artifact rotation is invalid: {value}");
        return rotation;
    }

    private static Color ReadColor(JsonElement source, string label)
    {
        var values = source.EnumerateArray().Select(row => row.GetSingle()).ToArray();
        if (values.Length != 4 || values.Any(value => !float.IsFinite(value) || value is < 0.0f or > 1.0f))
            throw new InvalidOperationException($"Fallout campaign {label} color is invalid.");
        return new Color(values[0], values[1], values[2], values[3]);
    }

    private static Color ReadPositiveColor(JsonElement source, string label)
    {
        var values = source.EnumerateArray().Select(row => row.GetSingle()).ToArray();
        if (values.Length != 4 || values.Any(value => !float.IsFinite(value) || value <= 0.0f))
            throw new InvalidOperationException($"Fallout campaign {label} color is invalid.");
        return new Color(values[0], values[1], values[2], values[3]);
    }

    private static Vector2I ReadIntPair(JsonElement source)
    {
        var values = source.EnumerateArray().Select(row => row.GetInt32()).ToArray();
        if (values.Length != 2)
            throw new InvalidOperationException("Fallout campaign integer pair is invalid.");
        return new Vector2I(values[0], values[1]);
    }

    private static Vector2 ReadVector2(JsonElement source)
    {
        var values = source.EnumerateArray().Select(row => row.GetSingle()).ToArray();
        if (values.Length != 2 || values.Any(value => !float.IsFinite(value)))
            throw new InvalidOperationException("Fallout campaign Vector2 is invalid.");
        return new Vector2(values[0], values[1]);
    }

    private static Vector3 ReadVector3(JsonElement source)
    {
        var values = source.EnumerateArray().Select(row => row.GetSingle()).ToArray();
        if (values.Length != 3 || values.Any(value => !float.IsFinite(value)))
            throw new InvalidOperationException("Fallout campaign Vector3 is invalid.");
        return new Vector3(values[0], values[1], values[2]);
    }

    private static string ResolveChildPath(string root, string relativePath)
    {
        if (Path.IsPathRooted(relativePath))
            throw new InvalidOperationException($"Fallout campaign path must be relative: {relativePath}");
        var fullPath = Path.GetFullPath(
            Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var relative = Path.GetRelativePath(root, fullPath);
        if (relative == ".." ||
            relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            throw new InvalidOperationException($"Fallout campaign path escapes its cache: {relativePath}");
        return fullPath;
    }

    private static string RequiredString(JsonElement source, string property)
    {
        var value = source.ValueKind == JsonValueKind.Object
            ? source.GetProperty(property).GetString()
            : source.GetString();
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"Fallout campaign string is empty: {property}");
        return value;
    }

    private static string HexString(JsonElement source, string property, int length)
    {
        var value = RequiredString(source, property);
        if (value.Length != length || value.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidOperationException($"Fallout campaign hex field is invalid: {property}");
        return value.ToLowerInvariant();
    }

    private static float Positive(JsonElement source, string property)
    {
        var value = Finite(source, property);
        if (value <= 0.0f)
            throw new InvalidOperationException($"Fallout campaign value must be positive: {property}");
        return value;
    }

    private static int PositiveInt(JsonElement source, string property)
    {
        var value = source.GetProperty(property).GetInt32();
        if (value <= 0)
            throw new InvalidOperationException($"Fallout campaign value must be positive: {property}");
        return value;
    }

    private static float Unit(JsonElement source, string property)
    {
        var value = Finite(source, property);
        if (value is < 0.0f or > 1.0f)
            throw new InvalidOperationException($"Fallout campaign value must be in [0, 1]: {property}");
        return value;
    }

    private static float Finite(JsonElement source, string property)
    {
        var value = source.GetProperty(property).GetSingle();
        if (!float.IsFinite(value))
            throw new InvalidOperationException($"Fallout campaign value must be finite: {property}");
        return value;
    }

    private static void RequireCount(JsonElement source, string property, int expected)
    {
        if (source.GetProperty(property).GetInt32() != expected)
            throw new InvalidOperationException($"Fallout campaign count drifted: {property}");
    }

    private static string Sha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private readonly record struct PngArtifact(
        string Path,
        string Sha256,
        int Width,
        int Height);
}

internal sealed record Fo1CampaignPresentationCatalog(
    string CampaignPath,
    string CampaignSha256,
    Fo1RuntimeProfile RuntimeProfile,
    float PixelsPerMeter,
    float GroundAnchorMeters,
    float StaticWorldYawDegrees,
    Fo1CampaignViewerProfile Viewer,
    IReadOnlyDictionary<int, Fo1CampaignTileArtifact> TileArtifacts,
    IReadOnlyDictionary<string, Fo1CampaignSpriteArtifact> SpriteArtifacts,
    IReadOnlyDictionary<int, string> PlayerArtifacts,
    IReadOnlyDictionary<string, Fo1CampaignCritterProfile> CritterProfiles,
    IReadOnlyList<Fo1CampaignMapCatalogRow> Maps,
    IReadOnlyList<Fo1CampaignMapPresentationCoverage> MapCoverage)
{
    internal object Report() => new
    {
        schema = "opennv-fo1-campaign-presentation-runtime-proof/v1",
        status = "pass-source-reference-prepared-not-rendered",
        campaign = CampaignPath,
        campaignSha256 = CampaignSha256,
        maps = Maps.Count,
        elevations = MapCoverage.Sum(row => row.Elevations),
        tileArtifacts = TileArtifacts.Count,
        spriteArtifacts = SpriteArtifacts.Count,
        spritePlacements = MapCoverage.Sum(row => row.SpritePlacements),
        skippedSpriteObjects = MapCoverage.Sum(row => row.SkippedSpriteObjects),
        mobs = MapCoverage.Sum(row => row.Mobs),
        blockers = MapCoverage.Sum(row => row.Blockers),
        doors = MapCoverage.Sum(row => row.Doors),
        wallObjects = MapCoverage.Sum(row => row.WallObjects),
        wallHexes = MapCoverage.Sum(row => row.WallHexes),
        wallComponents = MapCoverage.Sum(row => row.WallComponents),
        wallBoundaryEdges = MapCoverage.Sum(row => row.WallBoundaryEdges),
        critterProfiles = CritterProfiles.Count,
        mapCoverage = MapCoverage,
        promotion = new
        {
            transportedMaps = Maps.Count,
            sourceReferencePreparedMaps = Maps.Count,
            runtimeValidatedMaps = Maps.Count,
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

internal sealed record Fo1CampaignViewerProfile(
    string DefaultMapId,
    Fo1CampaignViewerSceneProfile Scene,
    Fo1CampaignWallGeometryProfile WallGeometry,
    Fo1CampaignStatusPanelProfile StatusPanel,
    Fo1CampaignCaptureProfile Capture);

internal sealed record Fo1CampaignViewerSceneProfile(
    string SourceSpriteOrientation,
    bool SourceReferenceOrbitEnabled,
    bool SourceReferenceVisibleByDefault,
    Color SourceColorMultiplier,
    float TonemapExposure,
    float FogDensity,
    float FogAerialPerspective);

internal sealed record Fo1CampaignWallGeometryProfile(
    string Mode,
    int SourceObjectType,
    string CollisionMode,
    float CellRadiusScale,
    float HeightMeters,
    float GroundSinkMeters,
    float Roughness,
    float Metallic,
    float SourceAlphaThreshold,
    Color UnresolvedSourceAlbedo,
    Color SideColorMultiplier,
    Color TopColorMultiplier);

internal sealed record Fo1CampaignStatusPanelProfile(
    float LeftPixels,
    float TopPixels,
    float RightPixels,
    float BottomPixels,
    float TextLeftPixels,
    float TextTopPixels,
    float TextRightPixels,
    float TextBottomPixels,
    Color PanelColor,
    Color FontColor,
    int FontSizePixels);

internal sealed record Fo1CampaignCaptureProfile(
    int WarmupFrames,
    int SettleFrames,
    int ExpectedWidthPixels,
    int ExpectedHeightPixels,
    float DarkPixelLuminance,
    float MinimumMeanLuminance,
    float MinimumLuminanceDeviation,
    float MaximumDarkFraction);

internal sealed record Fo1CampaignTileArtifact(
    int Id,
    string Filename,
    string Path,
    string Sha256,
    int Width,
    int Height);

internal sealed record Fo1CampaignSpriteArtifact(
    string Id,
    string LogicalPath,
    string Path,
    string Sha256,
    int Width,
    int Height,
    Vector2 FrameOffset,
    int Rotation,
    int Frame,
    Color? AverageOpaqueColor);

internal sealed record Fo1CampaignCritterProfile(
    string Pid,
    string? DisplayName,
    int HitPoints,
    int ActionPoints,
    int ArmorClass,
    int MeleeDamage,
    int Sequence,
    int Team,
    int AiPacket);

internal sealed record Fo1CampaignMapCatalogRow(
    string Id,
    string SourceFile,
    string Path,
    string Sha256,
    int Elevations,
    int SpritePlacements,
    int SkippedSpriteObjects,
    int Mobs,
    int Blockers,
    int Doors,
    int WallObjects,
    int WallHexes,
    int WallComponents,
    int WallBoundaryEdges);

internal sealed record Fo1CampaignMapPresentation(
    string Id,
    string SourceFile,
    Fo1CampaignMapEntry Entry,
    IReadOnlyList<Fo1CampaignElevationPresentation> Elevations,
    Fo1CampaignMapPresentationCoverage Coverage);

internal sealed record Fo1CampaignMapEntry(
    int Tile,
    int Elevation,
    int Rotation,
    Vector3 WorldMeters,
    string PlayerArtifactId);

internal sealed record Fo1CampaignElevationPresentation(
    int Elevation,
    IReadOnlyList<int> FloorIds,
    IReadOnlyList<int> RoofIds,
    IReadOnlyList<Fo1CampaignPlacement> Placements,
    IReadOnlyList<Fo1CampaignSkippedPlacement> SkippedPlacements,
    IReadOnlyList<Fo1CampaignBlocker> Blockers,
    IReadOnlyList<Fo1CampaignMob> Mobs,
    IReadOnlyList<Fo1CampaignDoor> Doors,
    Fo1CampaignWallTopology WallTopology,
    int ProvisionalWalkableHexes);

internal sealed record Fo1CampaignWallTopology(
    IReadOnlyList<Fo1CampaignWallCell> Cells,
    int SourceWallObjects,
    int OnGridSourceWallObjects,
    int OffGridSourceWallObjects,
    Fo1CampaignWallTopologyCoverage Coverage);

internal sealed record Fo1CampaignWallCell(
    int Tile,
    IReadOnlyList<Fo1CampaignWallSource> SourceObjects);

internal sealed record Fo1CampaignWallSource(
    int Serial,
    int Rotation,
    string? ArtFilename,
    bool Blocking);

internal sealed record Fo1CampaignWallTopologyCoverage(
    int OccupiedHexes,
    int BlockingHexes,
    int NonBlockingHexes,
    int ConnectedComponents,
    int LargestComponentHexes,
    int IsolatedHexes,
    int BoundaryEdges,
    int FloorFacingBoundaryEdges,
    int VoidFacingBoundaryEdges);

internal sealed record Fo1CampaignPlacement(
    int Serial,
    int ObjectId,
    int Tile,
    Vector3 WorldMeters,
    int Rotation,
    Vector2 PixelOffset,
    int ObjectType,
    string ObjectTypeName,
    string ArtFilename,
    string ArtifactId);

internal sealed record Fo1CampaignSkippedPlacement(int Serial, string Reason);
internal sealed record Fo1CampaignBlocker(int Serial, int Tile, bool Multihex);

internal sealed record Fo1CampaignMob(
    int Serial,
    string ProfileId,
    int CurrentHitPoints,
    int CurrentActionPoints,
    int RuntimeAiPacket,
    int RuntimeTeam);

internal sealed record Fo1CampaignDoor(
    int Serial,
    string InstanceFlags,
    IReadOnlyList<int> InstanceValues);

internal sealed record Fo1CampaignMapPresentationCoverage(
    string Id,
    int Elevations,
    int SpritePlacements,
    int SkippedSpriteObjects,
    int Mobs,
    int Blockers,
    int Doors,
    int WallObjects,
    int WallHexes,
    int WallComponents,
    int WallBoundaryEdges);
