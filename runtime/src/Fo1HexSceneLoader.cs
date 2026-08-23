using System.Security.Cryptography;
using System.Text.Json;
using Godot;

namespace OpenNV.Runtime;

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

        var grid = source.GetProperty("grid");
        if (grid.GetProperty("hexWidth").GetInt32() != Fo1HexMath.Width ||
            grid.GetProperty("hexHeight").GetInt32() != Fo1HexMath.Height ||
            grid.GetProperty("floorWidth").GetInt32() != 100 ||
            grid.GetProperty("floorHeight").GetInt32() != 100 ||
            grid.GetProperty("layout").GetString() != "odd-row-offset-pointy" ||
            !Mathf.IsEqualApprox(grid.GetProperty("hexFlatToFlatMeters").GetSingle(), 1.0f))
            throw new InvalidOperationException("Fallout hex grid contract drifted.");
        var floorIds = grid.GetProperty("floorIds").EnumerateArray().Select(value => value.GetInt32()).ToArray();
        if (floorIds.Length != 10000)
            throw new InvalidOperationException($"Fallout floor grid has {floorIds.Length} entries, expected 10000.");
        var defaultFloorId = grid.GetProperty("defaultFloorId").GetInt32();
        var floorCenters = grid.GetProperty("floorPatchCenters").EnumerateArray().Select(ReadVector).ToArray();
        if (floorCenters.Length != floorIds.Length)
            throw new InvalidOperationException("Fallout floor center count does not match its IDs.");
        VerifyFloorCenters(floorCenters);

        var root = new Node3D { Name = "FO1_V13ENT_HEX_ROOT" };
        parent.AddChild(root);
        BuildEnvironment(parent);
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
        var renderedFloorTiles = BuildFloor(root, floorIds, floorCenters, defaultFloorId, floorTextures);
        var floorBacked = new bool[Fo1HexMath.Width * Fo1HexMath.Height];
        for (var tile = 0; tile < floorBacked.Length; tile++)
            floorBacked[tile] = floorIds[Fo1HexMath.FloorIndex(tile)] != defaultFloorId;
        var presentation3d = grid.GetProperty("threeDPresentation");
        if (presentation3d.GetProperty("status").GetString() != "procedural-topology-proof")
            throw new InvalidOperationException("Unexpected Fallout 3D cave presentation contract.");
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
        var combat = source.GetProperty("combat");
        var spriteCoverage = BuildObjectSprites(
            root,
            source.GetProperty("objectSprites"),
            combat,
            presentation3d.GetProperty("sourceSpriteOverlayDefaultVisible").GetBoolean());

        var walkable = new bool[Fo1HexMath.Width * Fo1HexMath.Height];
        var blocked = grid.GetProperty("blockedHexes").EnumerateArray()
            .Select(value => value.GetInt32())
            .ToHashSet();
        if (blocked.Any(tile => tile is < 0 or >= Fo1HexMath.Width * Fo1HexMath.Height))
            throw new InvalidOperationException("Fallout MAP blocker escapes the 200x200 hex grid.");
        for (var tile = 0; tile < walkable.Length; tile++)
            walkable[tile] = floorIds[Fo1HexMath.FloorIndex(tile)] != defaultFloorId && !blocked.Contains(tile);
        var walkableCount = walkable.Count(value => value);
        BuildHexOverlay(root, walkable);

        var entry = source.GetProperty("entry");
        var entryTile = entry.GetProperty("tile").GetInt32();
        VerifyWorldCenter(entryTile, ReadVector(entry.GetProperty("worldMeters")), "entry");
        var doorSource = source.GetProperty("door");
        var doorObject = doorSource.GetProperty("source");
        var doorTile = doorObject.GetProperty("tile").GetInt32();
        VerifyWorldCenter(doorTile, ReadVector(doorSource.GetProperty("worldMeters")), "door");

        var tactical = source.GetProperty("tacticalProof");
        if (tactical.GetProperty("movementCostPerHex").GetInt32() != 1)
            throw new InvalidOperationException("Fallout tactical proof requires one AP per movement hex.");
        var session = new Fo1TacticalSession();
        var playerSource = combat.GetProperty("player");
        var playerArtifact = playerSource.GetProperty("artifact");
        var playerStats = playerSource.GetProperty("stats");
        var playerWeapon = playerSource.GetProperty("weapon");
        var playerTexture = LoadTexture(playerArtifact);
        session.Configure(
            sceneSha256,
            walkable,
            floorIds,
            floorNames,
            entryTile,
            doorTile,
            tactical.GetProperty("actionPointsPerTurn").GetInt32(),
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
                playerWeapon.GetProperty("name").GetString()!,
                playerWeapon.GetProperty("minimumDamage").GetInt32(),
                playerWeapon.GetProperty("maximumDamage").GetInt32(),
                playerWeapon.GetProperty("rangeHexes").GetInt32(),
                playerWeapon.GetProperty("actionPointCost").GetInt32()),
            spriteCoverage.Mobs,
            savePath);
        parent.AddChild(session);

        var door = BuildDoor(root, doorSource);
        var cameraSource = source.GetProperty("camera");
        var camera = new Fo1TacticalCamera();
        camera.Configure(
            session,
            ReadVector(cameraSource.GetProperty("homeFocusMeters")),
            cameraSource.GetProperty("homeSizeMeters").GetSingle(),
            Mathf.DegToRad(cameraSource.GetProperty("yawDegrees").GetSingle()),
            Mathf.DegToRad(cameraSource.GetProperty("pitchDegrees").GetSingle()));
        parent.AddChild(camera);
        session.AttachCamera(camera.Camera);

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
            source.GetProperty("coverage").GetProperty("doors").GetInt32());
    }

    private static int BuildFloor(
        Node3D root,
        int[] floorIds,
        Vector3[] centers,
        int defaultFloorId,
        IReadOnlyDictionary<int, Texture2D> textures)
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
                AlbedoColor = Colors.White,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                TextureFilter = BaseMaterial3D.TextureFilterEnum.NearestWithMipmaps,
            };
            var plane = new PlaneMesh
            {
                Size = new Vector2(2.01f, Fo1HexMath.RowSpacingMeters * 2.01f),
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
                position.Y = -0.025f;
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

    private static void BuildHexOverlay(Node3D root, bool[] walkable)
    {
        var tiles = Enumerable.Range(0, walkable.Length).Where(index => walkable[index]).ToArray();
        var multiMesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = Fo1HexVisuals.BuildRingMesh(0.935f, 0.975f),
            InstanceCount = tiles.Length,
        };
        for (var index = 0; index < tiles.Length; index++)
            multiMesh.SetInstanceTransform(
                index,
                new Transform3D(Basis.Identity, Fo1HexMath.Center(tiles[index]) + Vector3.Up * 0.012f));
        root.AddChild(new MultiMeshInstance3D
        {
            Name = "V13ENT_200X200_HEX_GRID",
            Multimesh = multiMesh,
            MaterialOverride = Fo1HexVisuals.Material(new Color(0.20f, 0.42f, 0.18f, 0.10f), true),
        });
    }

    private static SpriteCoverage BuildObjectSprites(
        Node3D root,
        JsonElement source,
        JsonElement combat,
        bool sourceOverlayVisible)
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
        var staticOverlay = new Node3D
        {
            Name = "FO1_SOURCE_STATIC_SPRITE_OVERLAY",
            Visible = sourceOverlayVisible,
        };
        root.AddChild(staticOverlay);
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
                    artifact.Texture,
                    pixelSize,
                    spriteOffset);
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
                Position = expected + Vector3.Up * 0.015f,
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

    private static DoorPresentation BuildDoor(Node3D root, JsonElement source)
    {
        var doorObject = source.GetProperty("source");
        var tile = doorObject.GetProperty("tile").GetInt32();
        var rotation = doorObject.GetProperty("rotation").GetInt32();
        if (rotation is < 0 or > 5)
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
        var textures = RuntimeMaterialLoader.LoadTextures(materialManifest);
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
        var targetWidth = MathF.Max(bounds.Size.X, 0.1f);
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
            Position = Fo1HexMath.Center(tile) + new Vector3(0.0f, frameHeight / 2.0f, 0.08f),
            Rotation = placement.Rotation,
        };
        root.AddChild(frame);
        var label = new Label3D
        {
            Name = "VaultDoorHexIdentity",
            Text = $"VAULT 13  •  DOOR HEX {tile}",
            Position = Fo1HexMath.Center(tile) + Vector3.Up * (MathF.Max(frameHeight, bounds.End.Y) + 0.55f),
            FontSize = 32,
            PixelSize = 0.008f,
            Modulate = new Color(0.92f, 0.75f, 0.26f),
            OutlineSize = 6,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
        };
        root.AddChild(label);
        return new DoorPresentation(placement, frame, bounds, materialBindings, frameWidth, frameHeight);
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

    private static void BuildEnvironment(Node3D parent)
    {
        parent.AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color(0.012f, 0.016f, 0.014f),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color(0.42f, 0.46f, 0.39f),
                AmbientLightEnergy = 1.15f,
                TonemapMode = Godot.Environment.ToneMapper.Filmic,
            },
        });
        parent.AddChild(new DirectionalLight3D
        {
            Name = "Fo1TacticalKeyLight",
            RotationDegrees = new Vector3(-52.0f, -38.0f, 0.0f),
            LightColor = new Color(1.0f, 0.76f, 0.48f),
            LightEnergy = 1.7f,
            ShadowEnabled = true,
        });
        parent.AddChild(new OmniLight3D
        {
            Name = "Fo1VaultDoorFill",
            Position = Fo1HexMath.Center(16290) + new Vector3(0.0f, 3.0f, 3.5f),
            LightColor = new Color(0.50f, 0.68f, 1.0f),
            LightEnergy = 2.2f,
            OmniRange = 10.0f,
            ShadowEnabled = false,
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
            var floorX = 99 - index % 100;
            var floorY = index / 100;
            var expected = Vector3.Zero;
            for (var offsetY = 0; offsetY < 2; offsetY++)
                for (var offsetX = 0; offsetX < 2; offsetX++)
                    expected += Fo1HexMath.Center((floorY * 2 + offsetY) * 200 + floorX * 2 + offsetX);
            expected /= 4.0f;
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
        float FrameHeightMeters);

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
        int SourceDoors);

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
