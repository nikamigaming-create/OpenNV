using System.Security.Cryptography;
using System.Text.Json;
using Godot;

namespace OpenNV.Runtime.Campaigns.Fallout1;

internal static class Fo1HexSceneLoaderNumericContracts
{
    // Immutable format, source-art, geometry, and acceptance contracts.
    // Runtime-tunable Fallout 1 behavior remains in the versioned runtime recipe.
    internal const float PresentationFloat0Point001f = 0.001f;
    internal const float PresentationFloat0Point1f = 0.1f;
    internal const float PresentationFloat0Point5f = 0.5f;
    internal const int PresentationInt10 = 10;
    internal const float PresentationFloat10000Point0f = 10000.0f;
    internal const int PresentationInt32 = 32;
    internal const int PresentationInt5 = 5;
    internal const int PresentationInt6 = 6;
    internal const int PresentationInt8 = 8;
}

internal static class Fo1HexSceneLoader
{
    private const string Schema = "opennv-fo1-hex-scene/v1";

    internal static LoadedFo1HexScene Load(
        string scenePath,
        Node3D parent,
        string? savePath)
    {
        var resolvedPath = VerifiedGltfLoader.ResolvePath(scenePath);
        var sceneBytes = File.ReadAllBytes(resolvedPath);
        var sceneSha256 = Convert.ToHexString(SHA256.HashData(sceneBytes)).ToLowerInvariant();
        using var document = JsonDocument.Parse(sceneBytes);
        var source = document.RootElement;
        if (source.GetProperty("schema").GetString() != Schema ||
            source.GetProperty("status").GetString() != "interactive-hex-topology-proof")
            throw new InvalidOperationException($"Unexpected Fallout hex scene: {resolvedPath}");
        var runtimeProfile = Fo1RuntimeProfile.Parse(source.GetProperty("runtimeProfile"));

        var recipe = source.GetProperty("recipe");
        var recipeId = RequiredString(recipe, "id");
        var grid = source.GetProperty("grid");
        if (grid.GetProperty("hexWidth").GetInt32() != Fo1HexMath.Width ||
            grid.GetProperty("hexHeight").GetInt32() != Fo1HexMath.Height ||
            grid.GetProperty("floorWidth").GetInt32() != Fo1HexMath.FloorWidth ||
            grid.GetProperty("floorHeight").GetInt32() != Fo1HexMath.FloorHeight ||
            grid.GetProperty("layout").GetString() != "fallout-even-column-offset-flat-v1" ||
            !Mathf.IsEqualApprox(
                grid.GetProperty("hexFlatToFlatMeters").GetSingle(),
                Fo1HexMath.FlatToFlatMeters))
            throw new InvalidOperationException("Fallout hex grid contract drifted.");
        var floorIds = grid.GetProperty("floorIds").EnumerateArray().Select(value => value.GetInt32()).ToArray();
        if (floorIds.Length != Fo1HexMath.FloorWidth * Fo1HexMath.FloorHeight)
            throw new InvalidOperationException(
                $"Fallout floor grid has {floorIds.Length} entries, expected " +
                $"{Fo1HexMath.FloorWidth * Fo1HexMath.FloorHeight}.");
        var defaultFloorId = grid.GetProperty("defaultFloorId").GetInt32();
        var floorCenters = grid.GetProperty("floorPatchCenters").EnumerateArray().Select(ReadVector).ToArray();
        if (floorCenters.Length != floorIds.Length)
            throw new InvalidOperationException("Fallout floor center count does not match its IDs.");
        VerifyFloorCenters(floorCenters);

        var root = new Node3D { Name = $"FO1_{NodeIdentifier(recipeId)}_HEX_ROOT" };
        parent.AddChild(root);
        var atmosphere = BuildEnvironment(
            parent,
            source.GetProperty("door").GetProperty("source").GetProperty("tile").GetInt32(),
            source.GetProperty("entry").GetProperty("tile").GetInt32(),
            runtimeProfile.Scene.Atmosphere);
        var floorNames = new Dictionary<int, string>();
        var floorTextures = new Dictionary<int, Texture2D>();
        foreach (var artifact in grid.GetProperty("floorArt").EnumerateArray())
        {
            var id = artifact.GetProperty("id").GetInt32();
            floorNames.Add(id, artifact.GetProperty("filename").GetString()!);
            floorTextures.Add(id, LoadTexture(artifact));
        }
        if (!floorIds.All(floorTextures.ContainsKey))
            throw new InvalidOperationException("Fallout floor grid references missing art.");
        var presentation3d = grid.GetProperty("threeDPresentation");
        var presentationStatus = presentation3d.GetProperty("status").GetString();
        if (presentationStatus is not ("procedural-topology-proof" or "owned-fnv-cave-kit-v1"))
            throw new InvalidOperationException("Unexpected Fallout 3D cave presentation contract.");
        var sourceOverlayVisible = presentation3d
            .GetProperty("sourceSpriteOverlayDefaultVisible")
            .GetBoolean();
        var sourceOverlay = new Node3D
        {
            Name = "FO1_SOURCE_STATIC_SPRITE_OVERLAY",
            Visible = sourceOverlayVisible,
        };
        root.AddChild(sourceOverlay);
        var renderedFloorTiles = BuildFloor(
            sourceOverlay,
            floorIds,
            floorCenters,
            defaultFloorId,
            floorTextures,
            runtimeProfile.Scene.SourceFloor);
        var floorBacked = new bool[Fo1HexMath.Width * Fo1HexMath.Height];
        for (var tile = 0; tile < floorBacked.Length; tile++)
            floorBacked[tile] = floorIds[Fo1HexMath.FloorIndex(tile)] != defaultFloorId;
        var obstacles = grid.GetProperty("threeDObstacles").EnumerateArray()
            .Select(row => new Fo1CaveGeometry.Obstacle(
                row.GetProperty("tile").GetInt32(),
                row.GetProperty("heightMeters").GetSingle(),
                row.GetProperty("radiusMeters").GetSingle(),
                row.GetProperty("objectType").GetInt32(),
                row.GetProperty("rotation").GetInt32()))
            .ToArray();
        var caveGeometry = Fo1CaveGeometry.Build(
            root,
            floorBacked,
            obstacles,
            presentation3d.GetProperty("boundaryHeightMeters").GetSingle());
        var ownedCave = presentationStatus == "owned-fnv-cave-kit-v1"
            ? Fo1OwnedCaveKit.Load(
                presentation3d,
                root,
                floorBacked,
                runtimeProfile.Generation.StaticWorldSpriteYawDegrees,
                runtimeProfile.Camera.Tactical.HomePitchDegrees)
            : Fo1OwnedCaveKit.Coverage.Empty;
        var combat = source.GetProperty("combat");
        Fo1CreatureModel.Template? creatureTemplate = null;
        if (combat.TryGetProperty("ownedCreaturePresentation", out var creatureSource) &&
            creatureSource.ValueKind == JsonValueKind.Object)
            creatureTemplate = Fo1CreatureModel.Load(creatureSource, root);
        var spriteCoverage = BuildObjectSprites(
            root,
            sourceOverlay,
            source.GetProperty("objectSprites"),
            combat,
            creatureTemplate,
            runtimeProfile);
        creatureTemplate?.Prototype.Free();

        var entry = source.GetProperty("entry");
        var entryTile = entry.GetProperty("tile").GetInt32();
        VerifyWorldCenter(entryTile, ReadVector(entry.GetProperty("worldMeters")), "entry");
        var doorSource = source.GetProperty("door");
        var doorObject = doorSource.GetProperty("source");
        var doorTile = doorObject.GetProperty("tile").GetInt32();
        VerifyWorldCenter(doorTile, ReadVector(doorSource.GetProperty("worldMeters")), "door");
        var walkable = new bool[Fo1HexMath.Width * Fo1HexMath.Height];
        var blocked = grid.GetProperty("blockedHexes").EnumerateArray()
            .Select(value => value.GetInt32())
            .ToHashSet();
        if (blocked.Any(tile => tile is < 0 or >= Fo1HexMath.Width * Fo1HexMath.Height))
            throw new InvalidOperationException("Fallout MAP blocker escapes the 200x200 hex grid.");
        var presentationBlocked = BuildPresentationFootprintMask(
            obstacles,
            floorBacked,
            entryTile,
            doorTile,
            runtimeProfile.Scene.PresentationFootprint);
        for (var tile = 0; tile < walkable.Length; tile++)
            walkable[tile] = floorIds[Fo1HexMath.FloorIndex(tile)] != defaultFloorId &&
                !blocked.Contains(tile) && !presentationBlocked.Contains(tile);
        var walkableCount = walkable.Count(value => value);
        BuildHexOverlay(
            root,
            walkable,
            presentationBlocked.Count(tile => !blocked.Contains(tile)),
            runtimeProfile.Scene.HexOverlay);

        var tactical = source.GetProperty("tacticalProof");
        if (tactical.GetProperty("movementCostPerHex").GetInt32() !=
            runtimeProfile.Gameplay.TacticalMoveActionPointCost)
            throw new InvalidOperationException(
                "Fallout tactical movement cost disagrees with the runtime profile.");
        var session = new Fo1TacticalSession();
        var ratActivation = combat.GetProperty("rules").GetProperty("ratActivation");
        var playerSource = combat.GetProperty("player");
        var playerArtifact = playerSource.GetProperty("artifact");
        var playerStats = playerSource.GetProperty("stats");
        var playerWeapon = playerSource.GetProperty("weapon");
        var playerMeleeWeapon = playerSource.GetProperty("meleeWeapon");
        var playerInventory = playerSource.GetProperty("inventory");
        if (RequiredString(playerInventory, "schema") != "opennv-fo1-starting-inventory/v1" ||
            RequiredString(playerInventory, "newWeaponMagazinePolicy") !=
                "prototype-ammunition-capacity")
            throw new InvalidOperationException("Unexpected Fallout starting-inventory contract.");
        var inventoryItems = playerInventory.GetProperty("items").EnumerateArray()
            .ToDictionary(row => RequiredString(row, "symbol"), StringComparer.Ordinal);
        var ammoSymbol = RequiredString(playerInventory, "ammunitionSymbol");
        if (!inventoryItems.TryGetValue(ammoSymbol, out var ammoItem) ||
            RequiredString(ammoItem.GetProperty("profile"), "subtypeName") != "ammo")
            throw new InvalidOperationException("Fallout starting ammunition profile is missing.");
        var inventoryProfile = new Fo1TacticalSession.InventoryProfile(
            RequiredString(playerInventory, "equippedRangedSymbol"),
            RequiredString(playerInventory, "equippedMeleeSymbol"),
            ammoSymbol,
            ammoItem.GetProperty("profile").GetProperty("roundsPerObject").GetInt32(),
            playerInventory.GetProperty("base").EnumerateArray()
                .Select(ReadInventoryStack)
                .ToArray(),
            playerInventory.GetProperty("tagBonuses").EnumerateArray()
                .Select(row => new Fo1TacticalSession.InventoryTagBonus(
                    RequiredString(row, "skill"),
                    row.GetProperty("items").EnumerateArray()
                        .Select(ReadInventoryStack)
                        .ToArray()))
                .ToArray());
        var playerTexture = LoadTexture(playerArtifact);
        session.Configure(
            sceneSha256,
            walkable,
            floorIds,
            floorNames,
            entryTile,
            doorTile,
            tactical.GetProperty("actionPointsPerTurn").GetInt32(),
            ratActivation.GetProperty("maximumDistanceHexes").GetInt32(),
            new Fo1TacticalSession.PlayerProfile(
                playerSource.GetProperty("name").GetString()!,
                playerTexture,
                playerArtifact.GetProperty("width").GetInt32(),
                playerArtifact.GetProperty("height").GetInt32(),
                1.0f / combat.GetProperty("objectPixelsPerMeter").GetSingle(),
                ReadVector2(playerArtifact.GetProperty("frameOffset")),
                playerStats.GetProperty("hitPoints").GetInt32(),
                playerStats.GetProperty("armorClass").GetInt32(),
                playerStats.GetProperty("sequence").GetInt32(),
                playerStats.GetProperty("strength").GetInt32(),
                playerStats.GetProperty("perception").GetInt32(),
                playerStats.GetProperty("meleeDamage").GetInt32(),
                playerStats.GetProperty("skills").EnumerateObject().ToDictionary(
                    row => row.Name,
                    row => row.Value.GetInt32(),
                    StringComparer.Ordinal),
                ReadWeaponProfile(playerWeapon, melee: false),
                ReadWeaponProfile(playerMeleeWeapon, melee: true),
                inventoryProfile),
            spriteCoverage.Mobs,
            savePath,
            runtimeProfile);
        parent.AddChild(session);
        ActorModelSlice.LoadedActor? playerActor = null;
        if (playerSource.TryGetProperty("ownedPresentation", out var playerPresentation) &&
            playerPresentation.ValueKind == JsonValueKind.Object)
        {
            if (RequiredString(playerPresentation, "role").Length == 0 ||
                RequiredString(playerPresentation, "displayName") !=
                    RequiredString(playerSource, "name") ||
                playerPresentation.GetProperty("unitsToMeters").GetSingle() <= 0.0f)
                throw new InvalidOperationException("Unexpected owned Vault Dweller presentation contract.");
            playerActor = session.AttachOwnedPlayer(
                playerPresentation.GetProperty("model").GetString()!,
                playerPresentation.GetProperty("sidecar").GetString()!);
            if (!playerPresentation.TryGetProperty("thirdPersonWeapon", out var playerWeaponPresentation) ||
                playerWeaponPresentation.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException(
                    "Owned Vault Dweller presentation has no third-person weapon contract.");
            if (RequiredString(playerWeaponPresentation, "gameplayPid") !=
                RequiredString(playerWeapon, "pid"))
                throw new InvalidOperationException(
                    "Fallout ranged presentation/gameplay identity relationship drifted.");
            _ = session.AttachOwnedPlayerWeapon(playerWeaponPresentation);
            if (!playerPresentation.TryGetProperty(
                    "thirdPersonMeleeWeapon",
                    out var playerMeleePresentation) ||
                playerMeleePresentation.ValueKind != JsonValueKind.Object ||
                RequiredString(playerMeleePresentation, "gameplayPid") !=
                    RequiredString(playerMeleeWeapon, "pid"))
                throw new InvalidOperationException(
                    "Fallout melee presentation/gameplay identity relationship drifted.");
            _ = session.AttachOwnedPlayerMeleeWeapon(playerMeleePresentation);
            var expectedSourceActor = playerPresentation.GetProperty("sourceActor");
            var expectedCoverage = playerPresentation.GetProperty("coverage");
            if (
                playerActor.Value.FormId != RequiredString(expectedSourceActor, "baseFormId") ||
                playerActor.Value.Meshes != expectedCoverage.GetProperty("surfaces").GetInt32() ||
                playerActor.Value.Skeletons < 1 ||
                playerActor.Value.Animations < expectedCoverage.GetProperty("animations").GetInt32() ||
                playerActor.Value.AuthoredSurfaces !=
                    expectedCoverage.GetProperty("surfaces").GetInt32() ||
                playerActor.Value.AuthoredTextures !=
                    expectedCoverage.GetProperty("textures").GetInt32())
                throw new InvalidOperationException("Owned Vault Dweller runtime coverage drifted.");
            if (!combat.TryGetProperty(
                    "ownedCombatPresentation",
                    out var combatPresentation) ||
                combatPresentation.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException(
                    "Owned Fallout presentation has no combat-effects contract.");
            _ = session.AttachCombatPresentation(combatPresentation);
        }

        var door = BuildDoor(
            root,
            doorSource,
            entryTile,
            presentation3d.GetProperty("sourceSpriteOverlayDefaultVisible").GetBoolean(),
            runtimeProfile.Scene.Door);
        var cameraSource = source.GetProperty("camera");
        var camera = new Fo1TacticalCamera();
        camera.Configure(
            session,
            ReadVector(cameraSource.GetProperty("homeFocusMeters")),
            cameraSource.GetProperty("homeSizeMeters").GetSingle(),
            Mathf.DegToRad(cameraSource.GetProperty("yawDegrees").GetSingle()),
            Mathf.DegToRad(cameraSource.GetProperty("pitchDegrees").GetSingle()),
            runtimeProfile.Camera);
        parent.AddChild(camera);
        session.AttachCamera(camera.Camera);
        var caveCutaway = new Fo1CaveCutaway();
        if (ownedCave.Instances > 0)
        {
            var caveContainer = root.FindChild(
                "FO1_OWNED_CAVE_COMPOSITION",
                recursive: true,
                owned: false) as Node3D
                ?? throw new InvalidOperationException("Owned Fallout cave composition has no container.");
            caveCutaway.Configure(caveContainer, session, camera.Camera, runtimeProfile.Cutaway);
            parent.AddChild(caveCutaway);
            camera.AttachCaveCutaway(caveCutaway);
        }

        return new LoadedFo1HexScene(
            resolvedPath,
            sceneSha256,
            root,
            session,
            camera,
            door,
            floorIds.Length,
            floorTextures.Count,
            renderedFloorTiles,
            walkableCount,
            spriteCoverage.Artifacts,
            spriteCoverage.Placements,
            spriteCoverage.Mobs.Count,
            caveGeometry.BoundaryEdges,
            caveGeometry.Obstacles,
            caveGeometry.Triangles,
            entryTile,
            doorTile,
            doorObject.GetProperty("rotation").GetInt32(),
            source.GetProperty("coverage").GetProperty("topLevelObjects").GetInt32(),
            source.GetProperty("coverage").GetProperty("doors").GetInt32(),
            creatureTemplate?.Meshes ?? 0,
            creatureTemplate?.Skeletons ?? 0,
            creatureTemplate?.Animations ?? 0,
            playerActor,
            atmosphere,
            ownedCave,
            caveCutaway,
            runtimeProfile);
    }

    private static int BuildFloor(
        Node3D root,
        int[] floorIds,
        Vector3[] centers,
        int defaultFloorId,
        IReadOnlyDictionary<int, Texture2D> textures,
        Fo1SourceFloorProfile profile)
    {
        var count = 0;
        foreach (var group in Enumerable.Range(0, floorIds.Length)
                     .Where(index => floorIds[index] != defaultFloorId)
                     .GroupBy(index => floorIds[index]))
        {
            var indices = group.ToArray();
            var material = new StandardMaterial3D
            {
                AlbedoTexture = textures[group.Key],
                AlbedoColor = profile.AlbedoColor,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Disabled,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                TextureFilter = BaseMaterial3D.TextureFilterEnum.NearestWithMipmaps,
            };
            var plane = new PlaneMesh
            {
                Size = new Vector2(
                    Fo1HexMath.ColumnSpacingMeters * 2.0f,
                    Fo1HexMath.FlatToFlatMeters * 2.0f),
                Material = material,
            };
            var multiMesh = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                Mesh = plane,
                InstanceCount = indices.Length,
            };
            for (var index = 0; index < indices.Length; index++)
            {
                var position = centers[indices[index]];
                position.Y = profile.YOffsetMeters;
                multiMesh.SetInstanceTransform(index, new Transform3D(Basis.Identity, position));
            }
            root.AddChild(new MultiMeshInstance3D
            {
                Name = $"FLOOR_ART_{group.Key:D4}_{indices.Length}",
                Multimesh = multiMesh,
            });
            count += indices.Length;
        }
        return count;
    }

    private static HashSet<int> BuildPresentationFootprintMask(
        IReadOnlyList<Fo1CaveGeometry.Obstacle> obstacles,
        bool[] floorBacked,
        int entryTile,
        int doorTile,
        Fo1PresentationFootprintProfile profile)
    {
        var blocked = new HashSet<int>();
        foreach (var obstacle in obstacles)
        {
            var center = Fo1HexMath.Center(obstacle.Tile);
            var radius = obstacle.RadiusMeters + profile.ObstaclePaddingMeters;
            var range = Math.Max(1, Mathf.CeilToInt(radius) + 1);
            var sourceX = obstacle.Tile % Fo1HexMath.Width;
            var sourceY = obstacle.Tile / Fo1HexMath.Width;
            for (var y = Math.Max(0, sourceY - range);
                 y <= Math.Min(Fo1HexMath.Height - 1, sourceY + range);
                 y++)
                for (var x = Math.Max(0, sourceX - range);
                     x <= Math.Min(Fo1HexMath.Width - 1, sourceX + range);
                     x++)
                {
                    var tile = y * Fo1HexMath.Width + x;
                    if (!floorBacked[tile])
                        continue;
                    var delta = Fo1HexMath.Center(tile) - center;
                    delta.Y = 0.0f;
                    if (delta.Length() <= radius)
                        blocked.Add(tile);
                }
        }
        var door = Fo1HexMath.Center(doorTile);
        var towardCave = Fo1HexMath.Center(entryTile) - door;
        towardCave.Y = 0.0f;
        if (towardCave.LengthSquared() <= Fo1HexSceneLoaderNumericContracts.PresentationFloat0Point001f)
            throw new InvalidOperationException("Fallout Vault threshold mask has no source axis.");
        towardCave = towardCave.Normalized();
        var lateral = new Vector3(towardCave.Z, 0.0f, -towardCave.X);
        foreach (var tile in Enumerable.Range(0, floorBacked.Length).Where(tile => floorBacked[tile]))
        {
            var offset = Fo1HexMath.Center(tile) - door;
            var depth = offset.Dot(towardCave);
            var across = MathF.Abs(offset.Dot(lateral));
            if (depth >= -profile.VaultBehindDoorMeters &&
                depth <= profile.VaultCavewardMeters &&
                across <= profile.VaultHalfWidthMeters)
                blocked.Add(tile);
        }
        blocked.Remove(entryTile);
        return blocked;
    }

    private static void BuildHexOverlay(
        Node3D root,
        bool[] walkable,
        int presentationFootprintBlockedHexes,
        Fo1HexOverlayProfile profile)
    {
        var tiles = Enumerable.Range(0, walkable.Length).Where(index => walkable[index]).ToArray();
        var tool = new SurfaceTool();
        tool.Begin(Mesh.PrimitiveType.Triangles);
        var edges = new HashSet<(int FirstX, int FirstZ, int SecondX, int SecondZ)>();
        foreach (var tile in tiles)
        {
            var corners = Fo1HexMath.Corners(tile);
            for (var index = 0; index < corners.Length; index++)
            {
                var first = corners[index];
                var second = corners[(index + 1) % corners.Length];
                var firstKey = (Mathf.RoundToInt(first.X * Fo1HexSceneLoaderNumericContracts.PresentationFloat10000Point0f), Mathf.RoundToInt(first.Z * Fo1HexSceneLoaderNumericContracts.PresentationFloat10000Point0f));
                var secondKey = (Mathf.RoundToInt(second.X * Fo1HexSceneLoaderNumericContracts.PresentationFloat10000Point0f), Mathf.RoundToInt(second.Z * Fo1HexSceneLoaderNumericContracts.PresentationFloat10000Point0f));
                var key = firstKey.CompareTo(secondKey) <= 0
                    ? (firstKey.Item1, firstKey.Item2, secondKey.Item1, secondKey.Item2)
                    : (secondKey.Item1, secondKey.Item2, firstKey.Item1, firstKey.Item2);
                if (edges.Add(key))
                    AddHexEdge(tool, first, second, profile.EdgeWidthMeters);
            }
        }
        tool.Index();
        var mesh = tool.Commit() ?? throw new InvalidOperationException(
            "Could not build the optional Fallout hex overlay.");
        var material = Fo1HexVisuals.Material(profile.AlbedoColor);
        material.EmissionEnabled = true;
        material.Emission = profile.EmissionColor;
        material.EmissionEnergyMultiplier = profile.EmissionEnergy;
        var overlay = new MeshInstance3D
        {
            Name = "V13ENT_200X200_HEX_GRID",
            Mesh = mesh,
            MaterialOverride = material,
            Position = Vector3.Up * profile.YOffsetMeters,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };
        overlay.SetMeta("hex_count", tiles.Length);
        overlay.SetMeta("edge_count", edges.Count);
        overlay.SetMeta("presentation_footprint_blocked_hexes", presentationFootprintBlockedHexes);
        root.AddChild(overlay);
    }

    private static void AddHexEdge(
        SurfaceTool tool,
        Vector3 first,
        Vector3 second,
        float width)
    {
        var direction = (second - first).Normalized();
        var perpendicular = new Vector3(-direction.Z, 0.0f, direction.X) * (width * Fo1HexSceneLoaderNumericContracts.PresentationFloat0Point5f);
        var firstOuter = first + perpendicular;
        var secondOuter = second + perpendicular;
        var secondInner = second - perpendicular;
        var firstInner = first - perpendicular;
        foreach (var vertex in new[]
                 {
                     firstOuter, secondOuter, secondInner,
                     firstOuter, secondInner, firstInner,
                 })
        {
            tool.SetNormal(Vector3.Up);
            tool.AddVertex(vertex);
        }
    }

    private static SpriteCoverage BuildObjectSprites(
        Node3D root,
        Node3D staticOverlay,
        JsonElement source,
        JsonElement combat,
        Fo1CreatureModel.Template? creatureTemplate,
        Fo1RuntimeProfile runtimeProfile)
    {
        if (source.GetProperty("presentation").GetString() !=
            "exact source FRM frame at exact MAP hex; world-locked static 2.5D; camera-facing actors")
            throw new InvalidOperationException("Unexpected Fallout object-sprite presentation contract.");
        var staticWorldYawDegrees = source.GetProperty("staticWorldYawDegrees").GetSingle();
        if (!float.IsFinite(staticWorldYawDegrees))
            throw new InvalidOperationException("Fallout static-world sprite yaw is invalid.");
        var pixelsPerMeter = source.GetProperty("pixelsPerMeter").GetSingle();
        if (pixelsPerMeter <= 1.0f)
            throw new InvalidOperationException("Fallout object-sprite scale is invalid.");
        var artifacts = source.GetProperty("artifacts").EnumerateArray().ToDictionary(
            row => row.GetProperty("id").GetString()!,
            row => new SpriteArtifact(
                LoadTexture(row),
                row.GetProperty("width").GetInt32(),
                row.GetProperty("height").GetInt32(),
                ReadVector2(row.GetProperty("frameOffset"))));
        var combatMobs = combat.GetProperty("mobs").EnumerateArray().ToDictionary(
            row => row.GetProperty("serial").GetInt32());
        var mobs = new List<Fo1Mob>();
        var placements = 0;
        foreach (var row in source.GetProperty("placements").EnumerateArray())
        {
            var tile = row.GetProperty("tile").GetInt32();
            var expected = ReadVector(row.GetProperty("worldMeters"));
            VerifyWorldCenter(tile, expected, $"object {row.GetProperty("serial").GetInt32()}");
            var artifact = artifacts[row.GetProperty("artifactId").GetString()!];
            var pixelOffset = ReadVector2(row.GetProperty("pixelOffset"));
            var pixelSize = 1.0f / pixelsPerMeter;
            var spriteOffset = new Vector2(
                pixelOffset.X + artifact.FrameOffset.X,
                -(pixelOffset.Y + artifact.FrameOffset.Y) + artifact.Height / 2.0f);
            var serial = row.GetProperty("serial").GetInt32();
            if (combatMobs.TryGetValue(serial, out var combatMob))
            {
                var profile = combatMob.GetProperty("profile");
                var mob = new Fo1Mob();
                mob.Configure(
                    serial,
                    combatMob.GetProperty("name").GetString()!,
                    combatMob.GetProperty("pid").GetString()!,
                    tile,
                    combatMob.GetProperty("currentHitPoints").GetInt32(),
                    profile.GetProperty("hitPoints").GetInt32(),
                    combatMob.GetProperty("currentActionPoints").GetInt32(),
                    profile.GetProperty("actionPoints").GetInt32(),
                    profile.GetProperty("armorClass").GetInt32(),
                    profile.GetProperty("meleeDamage").GetInt32(),
                    profile.GetProperty("sequence").GetInt32(),
                    combatMob.GetProperty("runtimeTeam").GetInt32(),
                    combatMob.GetProperty("runtimeAiPacket").GetInt32(),
                    combatMob.GetProperty("rotation").GetInt32(),
                    artifact.Texture,
                    pixelSize,
                    spriteOffset,
                    creatureTemplate,
                    runtimeProfile);
                root.AddChild(mob);
                mobs.Add(mob);
                placements++;
                continue;
            }
            var sprite = new Sprite3D
            {
                Name = $"FO1_OBJ_{serial}_{row.GetProperty("artFilename").GetString()}",
                Texture = artifact.Texture,
                PixelSize = pixelSize,
                Position = expected + Vector3.Up * runtimeProfile.Scene.SourceSprites.GroundAnchorMeters,
                Offset = spriteOffset,
                Billboard = BaseMaterial3D.BillboardModeEnum.Disabled,
                RotationDegrees = new Vector3(0.0f, staticWorldYawDegrees, 0.0f),
                Shaded = false,
                DoubleSided = true,
                AlphaCut = SpriteBase3D.AlphaCutMode.OpaquePrepass,
                TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
            };
            staticOverlay.AddChild(sprite);
            placements++;
        }
        return new SpriteCoverage(artifacts.Count, placements, mobs);
    }

    private static DoorPresentation BuildDoor(
        Node3D root,
        JsonElement source,
        int entryTile,
        bool sourceReferenceVisible,
        Fo1DoorPresentationProfile profile)
    {
        var doorObject = source.GetProperty("source");
        var tile = doorObject.GetProperty("tile").GetInt32();
        var rotation = doorObject.GetProperty("rotation").GetInt32();
        if (rotation is < 0 or > Fo1HexSceneLoaderNumericContracts.PresentationInt5)
            throw new InvalidOperationException($"Fallout door rotation is invalid: {rotation}");
        var target = source.GetProperty("target");
        var loaded = VerifiedGltfLoader.Load(
            target.GetProperty("model").GetString()!,
            target.GetProperty("sidecar").GetString()!);
        if (!loaded.SourceSha256.Equals(target.GetProperty("sourceSha256").GetString(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Fallout mapped door source hash drift.");
        var materialPath = VerifiedGltfLoader.ResolvePath(target.GetProperty("materialManifest").GetString()!);
        VerifiedGltfLoader.VerifyHash(materialPath, target.GetProperty("materialManifestSha256").GetString()!);
        using var materialDocument = JsonDocument.Parse(File.ReadAllText(materialPath));
        var materialManifest = materialDocument.RootElement;
        var textures = RuntimeMaterialLoader.LoadTextures(
            materialManifest.GetProperty("textures").EnumerateArray(),
            RuntimeConfiguration.Load().Renderer,
            "id",
            Path.GetDirectoryName(materialPath));
        var materialBindings = RuntimeMaterialLoader.Apply(
            loaded.Scene,
            materialManifest.GetProperty("asset"),
            textures);

        var placement = new Node3D
        {
            Name = $"FO1_VAULT_DOOR_HEX_{tile}",
            Position = Fo1HexMath.Center(tile),
            Rotation = new Vector3(0.0f, -rotation * MathF.PI / 3.0f, 0.0f),
        };
        root.AddChild(placement);
        loaded.Scene.Name = "MappedVGearDoor01Leaf";
        loaded.Scene.Scale = Vector3.One * target.GetProperty("unitsToMeters").GetSingle();
        placement.AddChild(loaded.Scene);
        var bounds = WorldBounds(loaded.Scene);
        placement.Position += Vector3.Up * -bounds.Position.Y;
        bounds = WorldBounds(loaded.Scene);

        var sourceArt = source.GetProperty("sourceArt").EnumerateArray().ToArray();
        var doorArt = sourceArt.Single(row => row.GetProperty("role").GetString() == "door");
        var frameArt = sourceArt.Single(row => row.GetProperty("role").GetString() == "frame");
        var targetWidth = MathF.Max(bounds.Size.X, Fo1HexSceneLoaderNumericContracts.PresentationFloat0Point1f);
        var pixelsPerMeter = doorArt.GetProperty("width").GetSingle() / targetWidth;
        var frameWidth = frameArt.GetProperty("width").GetSingle() / pixelsPerMeter;
        var frameHeight = frameArt.GetProperty("height").GetSingle() / pixelsPerMeter;
        var frameTexture = LoadTexture(frameArt);
        var frameMaterial = new StandardMaterial3D
        {
            AlbedoTexture = frameTexture,
            AlbedoColor = Colors.White,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
            NoDepthTest = false,
        };
        var frame = new MeshInstance3D
        {
            Name = "ExactV13Secr3Frame",
            Mesh = new QuadMesh
            {
                Size = new Vector2(frameWidth, frameHeight),
                Material = frameMaterial,
            },
            Position = Fo1HexMath.Center(tile) +
                new Vector3(0.0f, frameHeight / 2.0f, profile.SourceFrameDepthOffsetMeters),
            Rotation = placement.Rotation,
            Visible = sourceReferenceVisible,
        };
        root.AddChild(frame);
        var label = new Label3D
        {
            Name = "VaultDoorHexIdentity",
            Text = $"VAULT 13  •  DOOR HEX {tile}",
            Position = Fo1HexMath.Center(tile) + Vector3.Up *
                (MathF.Max(frameHeight, bounds.End.Y) + profile.IdentityLabelHeightMeters),
            FontSize = Fo1HexSceneLoaderNumericContracts.PresentationInt32,
            PixelSize = profile.IdentityLabelPixelSize,
            Modulate = profile.IdentityLabelColor,
            OutlineSize = Fo1HexSceneLoaderNumericContracts.PresentationInt6,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            Visible = sourceReferenceVisible,
        };
        root.AddChild(label);
        var vault13 = new Label3D
        {
            Name = "Vault13DoorNumber",
            Text = profile.DoorNumber,
            FontSize = profile.DoorNumberFontSize,
            PixelSize = profile.DoorNumberPixelSize,
            Modulate = profile.DoorNumberColor,
            OutlineSize = Fo1HexSceneLoaderNumericContracts.PresentationInt8,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = false,
        };
        placement.AddChild(vault13);
        var towardCave = Fo1HexMath.Center(entryTile) - Fo1HexMath.Center(tile);
        towardCave.Y = 0.0f;
        towardCave = towardCave.Normalized();
        vault13.GlobalPosition = bounds.GetCenter() + towardCave * profile.DoorNumberCavewardOffsetMeters;
        AddVault13CorridorPresentation(root, placement, tile, entryTile, profile);
        var controller = new Fo1VaultDoorController(placement, bounds);
        return new DoorPresentation(
            placement,
            frame,
            bounds,
            materialBindings,
            frameWidth,
            frameHeight,
            controller);
    }

    private static void AddVault13CorridorPresentation(
        Node3D root,
        Node3D doorPlacement,
        int doorTile,
        int entryTile,
        Fo1DoorPresentationProfile profile)
    {
        var door = Fo1HexMath.Center(doorTile);
        var towardCave = Fo1HexMath.Center(entryTile) - door;
        towardCave.Y = 0.0f;
        if (towardCave.LengthSquared() <= Fo1HexSceneLoaderNumericContracts.PresentationFloat0Point001f)
            throw new InvalidOperationException("Vault 13 corridor presentation has no threshold axis.");
        towardCave = towardCave.Normalized();
        var corridorEndPosition = door - towardCave * profile.CorridorNumberBehindDoorMeters +
            Vector3.Up * profile.CorridorNumberHeightMeters;
        root.AddChild(new Label3D
        {
            Name = "Vault13CorridorNumber",
            Text = profile.DoorNumber,
            Position = corridorEndPosition,
            Rotation = doorPlacement.Rotation,
            FontSize = profile.CorridorNumberFontSize,
            PixelSize = profile.CorridorNumberPixelSize,
            Modulate = profile.CorridorNumberColor,
            OutlineSize = Fo1HexSceneLoaderNumericContracts.PresentationInt10,
            NoDepthTest = false,
        });
        root.AddChild(new OmniLight3D
        {
            Name = "Vault13SourceAxisCorridorLight",
            Position = door - towardCave * profile.CorridorLightBehindDoorMeters +
                Vector3.Up * profile.CorridorLightHeightMeters,
            LightColor = profile.CorridorLightColor,
            LightEnergy = profile.CorridorLightEnergy,
            OmniRange = profile.CorridorLightRangeMeters,
            OmniAttenuation = profile.CorridorLightAttenuation,
            ShadowEnabled = false,
        });
    }

    private static Texture2D LoadTexture(JsonElement artifact)
    {
        var path = VerifiedGltfLoader.ResolvePath(artifact.GetProperty("png").GetString()!);
        VerifiedGltfLoader.VerifyHash(path, artifact.GetProperty("pngSha256").GetString()!);
        var image = Image.LoadFromFile(path);
        if (image is null || image.IsEmpty() ||
            image.GetWidth() != artifact.GetProperty("width").GetInt32() ||
            image.GetHeight() != artifact.GetProperty("height").GetInt32())
            throw new InvalidOperationException($"Fallout prepared texture is invalid: {path}");
        return ImageTexture.CreateFromImage(image);
    }

    private static CaveAtmosphere BuildEnvironment(
        Node3D parent,
        int doorTile,
        int entryTile,
        Fo1AtmosphereProfile profile)
    {
        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = profile.BackgroundColor,
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = profile.AmbientColor,
            AmbientLightEnergy = profile.AmbientEnergy,
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
            TonemapExposure = profile.TonemapExposure,
            FogEnabled = true,
            FogLightColor = profile.FogColor,
            FogLightEnergy = profile.FogLightEnergy,
            FogDensity = profile.FogDensity,
            FogAerialPerspective = profile.FogAerialPerspective,
            FogSkyAffect = profile.FogSkyAffect,
            VolumetricFogEnabled = true,
            VolumetricFogDensity = profile.VolumetricFogDensity,
            VolumetricFogAlbedo = profile.VolumetricFogAlbedo,
            VolumetricFogEmission = profile.VolumetricFogEmission,
            VolumetricFogEmissionEnergy = profile.VolumetricFogEmissionEnergy,
            VolumetricFogLength = profile.VolumetricFogLengthMeters,
            VolumetricFogDetailSpread = profile.VolumetricFogDetailSpread,
            VolumetricFogAmbientInject = profile.VolumetricFogAmbientInject,
            VolumetricFogSkyAffect = profile.VolumetricFogSkyAffect,
        };
        parent.AddChild(new WorldEnvironment
        {
            Name = "WorldEnvironment",
            Environment = environment,
        });
        parent.AddChild(new DirectionalLight3D
        {
            Name = "Fo1TacticalKeyLight",
            RotationDegrees = profile.DirectionalLight.RotationDegrees,
            LightColor = profile.DirectionalLight.Color,
            LightEnergy = profile.DirectionalLight.Energy,
            ShadowEnabled = true,
        });
        var door = Fo1HexMath.Center(doorTile);
        var entry = Fo1HexMath.Center(entryTile);
        var caveward = entry - door;
        caveward.Y = 0.0f;
        if (caveward.LengthSquared() <= Fo1HexSceneLoaderNumericContracts.PresentationFloat0Point001f)
            throw new InvalidOperationException("Fallout cave atmosphere has no door-to-entry axis.");
        caveward = caveward.Normalized();
        var lateral = new Vector3(-caveward.Z, 0.0f, caveward.X);
        foreach (var light in profile.PracticalLights)
        {
            var anchor = light.Anchor == "door" ? door : entry;
            parent.AddChild(new OmniLight3D
            {
                Name = $"Fo1PracticalLight_{light.Id}",
                Position = anchor + caveward * light.ForwardMeters + lateral * light.LateralMeters +
                    Vector3.Up * light.HeightMeters,
                LightColor = light.Color,
                LightEnergy = light.Energy,
                OmniRange = light.RangeMeters,
                OmniAttenuation = light.Attenuation,
                ShadowEnabled = false,
            });
        }
        foreach (var fog in profile.LocalFogVolumes)
        {
            var anchor = fog.Anchor == "door" ? door : entry;
            AddLocalFogVolume(
                parent,
                fog,
                anchor + caveward * fog.ForwardMeters + lateral * fog.LateralMeters +
                    Vector3.Up * fog.HeightMeters);
        }
        return new CaveAtmosphere(
            "opennv-fo1-cave-atmosphere/v1",
            profile.BackgroundColor,
            profile.FogColor,
            profile.FogDensity,
            true,
            profile.VolumetricFogDensity,
            profile.VolumetricFogLengthMeters,
            profile.PracticalLights.Count,
            1,
            profile.LocalFogVolumes.Count);
    }

    private static void AddLocalFogVolume(
        Node3D parent,
        Fo1LocalFogProfile profile,
        Vector3 position)
    {
        parent.AddChild(new FogVolume
        {
            Name = $"Fo1LocalFog_{profile.Id}",
            Position = position,
            Size = profile.SizeMeters,
            Shape = RenderingServer.FogVolumeShape.Ellipsoid,
            Material = new FogMaterial
            {
                Albedo = profile.Color,
                Density = profile.Density,
                Emission = profile.Color * profile.EmissionScale,
                HeightFalloff = profile.HeightFalloff,
                EdgeFade = profile.EdgeFade,
            },
        });
    }

    private static Aabb WorldBounds(Node3D root)
    {
        var minimum = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        var maximum = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
        var count = 0;
        foreach (var mesh in Descendants<MeshInstance3D>(root))
        {
            var bounds = mesh.GetAabb();
            foreach (var x in new[] { bounds.Position.X, bounds.End.X })
                foreach (var y in new[] { bounds.Position.Y, bounds.End.Y })
                    foreach (var z in new[] { bounds.Position.Z, bounds.End.Z })
                    {
                        var point = mesh.ToGlobal(new Vector3(x, y, z));
                        minimum = minimum.Min(point);
                        maximum = maximum.Max(point);
                    }
            count++;
        }
        if (count == 0)
            throw new InvalidOperationException("Fallout mapped door has no renderable bounds.");
        return new Aabb(minimum, maximum - minimum);
    }

    private static IEnumerable<T> Descendants<T>(Node node)
        where T : Node
    {
        foreach (var child in node.GetChildren())
        {
            if (child is T match)
                yield return match;
            foreach (var descendant in Descendants<T>(child))
                yield return descendant;
        }
    }

    private static Vector3 ReadVector(JsonElement source)
    {
        var values = source.EnumerateArray().Select(value => value.GetSingle()).ToArray();
        if (values.Length != 3)
            throw new InvalidOperationException("Fallout hex scene vector must contain three values.");
        return new Vector3(values[0], values[1], values[2]);
    }

    private static Vector2 ReadVector2(JsonElement source)
    {
        var values = source.EnumerateArray().Select(value => value.GetSingle()).ToArray();
        if (values.Length != 2)
            throw new InvalidOperationException("Fallout hex scene vector must contain two values.");
        return new Vector2(values[0], values[1]);
    }

    private static Fo1TacticalSession.InventoryStack ReadInventoryStack(JsonElement source) => new(
        RequiredString(source, "symbol"),
        RequiredString(source, "pid"),
        source.GetProperty("objects").GetInt32());

    private static Fo1TacticalSession.WeaponProfile ReadWeaponProfile(
        JsonElement source,
        bool melee)
    {
        var result = new Fo1TacticalSession.WeaponProfile(
            RequiredString(source, "name"),
            RequiredString(source, "pid"),
            RequiredString(source, "prototypeSha256"),
            RequiredString(source, "skill"),
            source.GetProperty("minimumDamage").GetInt32(),
            source.GetProperty("maximumDamage").GetInt32(),
            source.GetProperty("rangeHexes").GetInt32(),
            source.GetProperty("actionPointCost").GetInt32(),
            source.GetProperty("minimumStrength").GetInt32(),
            melee ? 0 : source.GetProperty("roundsPerAttack").GetInt32(),
            melee ? 0 : source.GetProperty("ammunitionCapacity").GetInt32(),
            melee ? 0 : source.GetProperty("initialLoadedRounds").GetInt32(),
            melee);
        result.Validate();
        return result;
    }

    private static string RequiredString(JsonElement source, string property)
    {
        var value = source.GetProperty(property).GetString();
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException(
                $"Fallout hex scene string is empty: {property}");
        return value;
    }

    private static string NodeIdentifier(string value) => new(
        value.Select(character => char.IsLetterOrDigit(character) ? character : '_').ToArray());

    private static void VerifyWorldCenter(int tile, Vector3 expected, string label)
    {
        var actual = Fo1HexMath.Center(tile);
        if (!actual.IsEqualApprox(expected))
            throw new InvalidOperationException($"Fallout {label} hex/world conversion drift: {tile} {actual} != {expected}");
    }

    private static void VerifyFloorCenters(Vector3[] centers)
    {
        for (var index = 0; index < centers.Length; index++)
        {
            var expected = Fo1HexMath.FloorPatchCenter(index);
            if (!expected.IsEqualApprox(centers[index]))
                throw new InvalidOperationException($"Fallout floor center drift at index {index}.");
        }
    }

    internal readonly record struct DoorPresentation(
        Node3D Placement,
        MeshInstance3D SourceFrame,
        Aabb Bounds,
        int MaterialBindings,
        float FrameWidthMeters,
        float FrameHeightMeters,
        Fo1VaultDoorController Controller);

    internal readonly record struct LoadedFo1HexScene(
        string ScenePath,
        string SceneSha256,
        Node3D Root,
        Fo1TacticalSession Session,
        Fo1TacticalCamera Camera,
        DoorPresentation Door,
        int FloorEntries,
        int FloorTextures,
        int RenderedFloorTiles,
        int WalkableHexes,
        int SpriteArtifacts,
        int SpritePlacements,
        int CombatMobs,
        int CaveBoundaryEdges,
        int CaveObstacles,
        int CaveTriangles,
        int EntryTile,
        int DoorTile,
        int DoorRotation,
        int TopLevelObjects,
        int SourceDoors,
        int CreatureMeshes,
        int CreatureSkeletons,
        int CreatureAnimations,
        ActorModelSlice.LoadedActor? PlayerActor,
        CaveAtmosphere Atmosphere,
        Fo1OwnedCaveKit.Coverage OwnedCave,
        Fo1CaveCutaway CaveCutaway,
        Fo1RuntimeProfile RuntimeProfile);

    internal readonly record struct CaveAtmosphere(
        string Schema,
        Color BackgroundColor,
        Color FogColor,
        float FogDensity,
        bool VolumetricFogEnabled,
        float VolumetricFogDensity,
        float VolumetricFogLengthMeters,
        int PracticalLights,
        int DirectionalLights,
        int LocalFogVolumes);

    private readonly record struct SpriteArtifact(
        Texture2D Texture,
        int Width,
        int Height,
        Vector2 FrameOffset);

    private readonly record struct SpriteCoverage(
        int Artifacts,
        int Placements,
        IReadOnlyList<Fo1Mob> Mobs);
}
