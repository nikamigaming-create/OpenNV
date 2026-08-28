using System.Text.Json;
using Godot;

namespace OpenNV.Runtime;

internal static class CellContentLoader
{
    private const string CellSceneSchema = "opennv-cell-scene/v12";

    internal static LoadedContent Load(
        string scenePath,
        Node3D parent,
        GameplaySession session,
        RuntimeConfiguration configuration,
        bool prepareFirstPersonPresentation,
        string? actorScenePath,
        string? actorScenesManifestPath,
        bool proofEnableActor,
        bool buildCollision,
        uint renderLayer)
    {
        var resolvedScenePath = VerifiedGltfLoader.ResolvePath(scenePath);
        using var document = JsonDocument.Parse(File.ReadAllText(resolvedScenePath));
        var source = document.RootElement;
        if (source.GetProperty("schema").GetString() != CellSceneSchema ||
            source.GetProperty("status").GetString() != "geometry-structure")
            throw new InvalidOperationException($"Unexpected OpenNV cell content: {resolvedScenePath}");
        configuration.VerifyCompiledConfiguration(source);

        var prototypes = new Dictionary<string, VerifiedGltfLoader.LoadedGltf>(StringComparer.Ordinal);
        var assetLogicalPaths = new Dictionary<string, string>(StringComparer.Ordinal);
        var collisionAssets = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            var textures = RuntimeMaterialLoader.LoadTextures(source, configuration.Renderer);
            var materialBindings = 0;
            var defaultCompiler = source.GetProperty("compiler");
            foreach (var asset in source.GetProperty("assets").EnumerateArray())
            {
                var assetId = asset.GetProperty("id").GetString()!;
                assetLogicalPaths.Add(assetId, asset.GetProperty("logicalPath").GetString()!);
                var loaded = VerifiedGltfLoader.Load(
                    asset.GetProperty("model").GetString()!,
                    asset.GetProperty("sidecar").GetString()!);
                if (!loaded.SourceSha256.Equals(
                        asset.GetProperty("sourceSha256").GetString(),
                        StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Cell asset source hash mismatch: {assetId}");
                var compiler = asset.TryGetProperty("compiler", out var assetCompiler)
                    ? assetCompiler
                    : defaultCompiler;
                if (!loaded.CompilerName.Equals(compiler.GetProperty("name").GetString(), StringComparison.Ordinal) ||
                    !loaded.CompilerSha256.Equals(
                        compiler.GetProperty("sha256").GetString(),
                        StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Cell asset compiler provenance mismatch: {assetId}");
                materialBindings += RuntimeMaterialLoader.Apply(
                    loaded.Scene,
                    asset,
                    textures,
                    configuration.Renderer,
                    configuration.ContentCompiler.RetailGrass);
                SetRenderLayer(loaded.Scene, renderLayer);
                var collision = asset.GetProperty("collision");
                if (collision.GetProperty("enabled").GetBoolean())
                {
                    if (loaded.CollisionScene is null &&
                        collision.GetProperty("source").GetString() != "LAND-height-grid")
                        throw new InvalidOperationException($"Authored collision payload is missing: {assetId}");
                    collisionAssets.Add(assetId);
                }
                prototypes.Add(assetId, loaded);
            }

            var coordinates = source.GetProperty("coordinates");
            var unitScale = coordinates.GetProperty("unitsToMeters").GetSingle();
            if (!Mathf.IsEqualApprox(unitScale, configuration.World.GameUnitsToMeters))
                throw new InvalidOperationException("Prepared CELL unit scale disagrees with OpenNV configuration.");
            var originGameUnits = ReadVector(coordinates.GetProperty("originGameUnits"));
            var cell = source.GetProperty("cell");
            var recipeId = source.GetProperty("recipe").GetString()!;
            var recipeSha256 = source.GetProperty("recipeSha256").GetString()!;
            var formId = cell.GetProperty("formId").GetString()!;
            var editorId = cell.GetProperty("editorId").GetString()!;
            var acceptedCellFormIds = cell.TryGetProperty("sourceCellFormIds", out var sourceCells)
                ? sourceCells.EnumerateArray().Select(value => value.GetString()!)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(new[] { formId }, StringComparer.OrdinalIgnoreCase);
            var root = new Node3D
            {
                Name = $"CELL_{formId}_{editorId}",
                Scale = Vector3.One * unitScale,
            };
            parent.AddChild(root);
            var navigation = CellNavigationGraph.Load(
                source.GetProperty("navigation"),
                acceptedCellFormIds);

            var loadedReferences = 0;
            var sourceReferences = source.GetProperty("references")
                .EnumerateArray()
                .Select(ReadSourceReference)
                .ToArray();
            var placedReferences = new List<PlacedReference>();
            var surfaces = 0;
            var vertices = 0;
            var triangles = 0;
            var loadedLodBlocks = new List<LoadedLodBlock>();
            var lodCoverage = ReadLodCoverage(source, configuration);
            if (source.TryGetProperty("lodBlocks", out var lodSource))
            {
                var lodRoot = new Node3D { Name = "WORLD_LOD" };
                root.AddChild(lodRoot);
                foreach (var block in lodSource.EnumerateArray())
                {
                    var blockId = block.GetProperty("id").GetString()!;
                    var assetId = block.GetProperty("assetId").GetString()!;
                    var placement = new Node3D
                    {
                        Name = blockId,
                        Position = ReadVector(block.GetProperty("positionGodotUnits")),
                        Scale = Vector3.One * block.GetProperty("scale").GetSingle(),
                    };
                    lodRoot.AddChild(placement);
                    var instance = prototypes[assetId].Scene.Duplicate(
                            (int)Node.DuplicateFlags.Default) as Node3D
                        ?? throw new InvalidOperationException($"Could not duplicate LOD asset: {assetId}");
                    placement.AddChild(instance);
                    SetRenderLayer(instance, renderLayer);
                    var geometry = CountGeometry(instance);
                    CountGeometry(instance, ref surfaces, ref vertices, ref triangles);
                    loadedLodBlocks.Add(new LoadedLodBlock(
                        blockId,
                        assetId,
                        block.GetProperty("logicalPath").GetString()!,
                        block.GetProperty("sourceSha256").GetString()!,
                        block.GetProperty("family").GetString()!,
                        block.GetProperty("geometryCoordinateSpace").GetString()!,
                        block.GetProperty("level").GetInt32(),
                        block.GetProperty("variant").GetString()!,
                        block.GetProperty("selectionReason").GetString()!,
                        ReadVector(block.GetProperty("blockOriginGameUnits")),
                        placement,
                        instance,
                        geometry));
                }
            }
            ValidateLodCoverage(
                lodCoverage,
                loadedLodBlocks,
                originGameUnits,
                resolvedScenePath);
            if (source.TryGetProperty("grassOverlays", out var grassSource))
            {
                var grassRoot = new Node3D { Name = "RETAIL_GRASS" };
                root.AddChild(grassRoot);
                foreach (var overlay in grassSource.EnumerateArray())
                {
                    var overlayId = overlay.GetProperty("id").GetString()!;
                    var assetId = overlay.GetProperty("assetId").GetString()!;
                    var placement = new Node3D
                    {
                        Name = overlayId,
                        Position = ReadVector(overlay.GetProperty("positionGodotUnits")),
                        Scale = Vector3.One * overlay.GetProperty("scale").GetSingle(),
                    };
                    grassRoot.AddChild(placement);
                    var instance = prototypes[assetId].Scene.Duplicate(
                            (int)Node.DuplicateFlags.Default) as Node3D
                        ?? throw new InvalidOperationException(
                            $"Could not duplicate retail grass overlay: {assetId}");
                    placement.AddChild(instance);
                    SetRenderLayer(instance, renderLayer);
                    if (!overlay.GetProperty("castsShadows").GetBoolean())
                        SetShadowCasting(instance, GeometryInstance3D.ShadowCastingSetting.Off);
                    CountGeometry(instance, ref surfaces, ref vertices, ref triangles);
                }
            }
            var doors = new Dictionary<string, DoorInstance>(StringComparer.OrdinalIgnoreCase);
            var pickups = new Dictionary<string, PickupInstance>(StringComparer.OrdinalIgnoreCase);
            var containers = new Dictionary<string, ContainerInstance>(StringComparer.OrdinalIgnoreCase);
            var pools = new Dictionary<string, PoolTableInstance>(StringComparer.OrdinalIgnoreCase);
            var poolManifest = ReadPoolManifest(source);
            PoolTableInstance? poolTable = null;
            Node3D? poolCuePlacement = null;
            Node3D? poolCueVisual = null;
            Node3D? poolRackPlacement = null;
            var poolBalls = new List<PoolBallInstance>();
            var collisionMeshes = 0;
            var startingWeaponFormId = source.TryGetProperty("firstPerson", out var firstPersonSource)
                ? firstPersonSource.GetProperty("startingLoadout").GetProperty("weaponFormId").GetString()
                : null;
            foreach (var reference in source.GetProperty("references").EnumerateArray())
            {
                if (reference.GetProperty("initiallyDisabled").GetBoolean())
                    continue;
                var referenceFormId = reference.GetProperty("formId").GetString()!;
                var yaw = reference.GetProperty("yawGodotRadians").GetSingle();
                var rotation = ReadQuaternion(reference.GetProperty("rotationGodotQuaternion"));
                var interaction = reference.GetProperty("interaction");
                var interactionType = interaction.ValueKind == JsonValueKind.Object
                    ? interaction.GetProperty("type").GetString()
                    : null;
                if (prepareFirstPersonPresentation && interactionType == "pickup" &&
                    interaction.GetProperty("itemFormId").GetString() == startingWeaponFormId)
                    continue;
                if (interactionType == "pickup" && session.IsReferenceRemoved(referenceFormId))
                    continue;
                var assetId = reference.GetProperty("assetId").GetString()!;
                var referencePosition = ReadVector(reference.GetProperty("positionGodotUnits"));
                var referenceScale = reference.GetProperty("scale").GetSingle();
                var baseEditorId = reference.GetProperty("baseEditorId").GetString()!;
                if (poolManifest is not null &&
                    poolManifest.BallRoles.TryGetValue(referenceFormId, out var ballRole))
                {
                    if (interactionType != "pool-component" ||
                        interaction.GetProperty("role").GetString() != ballRole)
                        throw new InvalidOperationException($"Pool ball role mismatch: {referenceFormId}");
                    var loadedBall = prototypes[assetId];
                    if (loadedBall.DynamicPhysicsBodies.Count != 1)
                        throw new InvalidOperationException(
                            $"Pool ball requires one authored dynamic body: {referenceFormId}");
                    var ballVisual = loadedBall.Scene.Duplicate((int)Node.DuplicateFlags.Default) as Node3D
                        ?? throw new InvalidOperationException($"Could not duplicate pool ball: {assetId}");
                    SetRenderLayer(ballVisual, renderLayer);
                    var ball = new PoolBallInstance();
                    ball.Configure(
                        referenceFormId,
                        ballRole,
                        loadedBall.DynamicPhysicsBodies[0],
                        ballVisual,
                        unitScale,
                        referenceScale,
                        configuration.Pool);
                    parent.AddChild(ball);
                    ball.GlobalPosition = root.ToGlobal(referencePosition);
                    ball.GlobalBasis = root.GlobalBasis.Orthonormalized() * new Basis(rotation);
                    ball.CaptureAuthoredTransform();
                    ball.Freeze = !buildCollision;
                    CountGeometry(ballVisual, ref surfaces, ref vertices, ref triangles);
                    placedReferences.Add(new PlacedReference(
                        referenceFormId,
                        reference.GetProperty("baseFormId").GetString()!,
                        reference.GetProperty("baseEditorId").GetString()!,
                        assetId,
                        reference.GetProperty("cellFormId").GetString()!,
                        ball,
                        ballVisual,
                        CountGeometry(ballVisual)));
                    if (buildCollision)
                        collisionMeshes += loadedBall.DynamicPhysicsBodies[0].Hulls.Count;
                    poolBalls.Add(ball);
                    loadedReferences++;
                    continue;
                }
                Node3D placement;
                if (interactionType == "pool-table")
                {
                    if (poolManifest is null || referenceFormId != poolManifest.TableReferenceFormId)
                        throw new InvalidOperationException($"Unexpected pool table reference: {referenceFormId}");
                    assetId = poolManifest.TablePresentationAssetId;
                    poolTable = new PoolTableInstance();
                    poolTable.Configure(
                        referenceFormId,
                        poolManifest.TablePresentationModelPath,
                        poolManifest.TableGameplayCollisionSource,
                        configuration,
                        session);
                    pools.Add(referenceFormId, poolTable);
                    placement = poolTable;
                }
                else if (interactionType == "door")
                {
                    var destination = reference.GetProperty("teleportDestinationFormId");
                    var door = new DoorInstance { Name = $"DOOR_{referenceFormId}" };
                    door.Configure(
                        referenceFormId,
                        yaw,
                        configuration.Door.OpenAngleDegrees,
                        destination.ValueKind == JsonValueKind.String ? destination.GetString() : null);
                    door.SetOpen(session.IsDoorOpen(referenceFormId));
                    doors.Add(referenceFormId, door);
                    placement = door;
                }
                else if (interactionType == "pickup")
                {
                    PickupInstance.WeaponProfile? weapon = null;
                    if (interaction.TryGetProperty("weapon", out var weaponSource))
                    {
                        weapon = new PickupInstance.WeaponProfile(
                            weaponSource.GetProperty("damage").GetInt32(),
                            weaponSource.GetProperty("clipSize").GetInt32(),
                            weaponSource.GetProperty("ammoFormId").ValueKind == JsonValueKind.String
                                ? weaponSource.GetProperty("ammoFormId").GetString()
                                : null);
                    }
                    var pickup = new PickupInstance();
                    pickup.Configure(
                        referenceFormId,
                        interaction.GetProperty("itemFormId").GetString()!,
                        interaction.GetProperty("itemEditorId").GetString()!,
                        interaction.GetProperty("itemRecordType").GetString()!,
                        interaction.GetProperty("count").GetInt32(),
                        weapon);
                    pickup.Basis = new Basis(rotation);
                    pickups.Add(referenceFormId, pickup);
                    placement = pickup;
                }
                else if (interactionType == "container")
                {
                    var entries = interaction.GetProperty("items")
                        .EnumerateArray()
                        .Select(item => new ContainerInstance.Entry(
                            item.GetProperty("itemFormId").GetString()!,
                            item.GetProperty("itemEditorId").GetString()!,
                            item.GetProperty("itemRecordType").GetString()!,
                            item.GetProperty("count").GetInt32(),
                            item.GetProperty("resolved").GetBoolean()))
                        .ToArray();
                    var container = new ContainerInstance();
                    container.Configure(
                        referenceFormId,
                        reference.GetProperty("baseEditorId").GetString()!,
                        entries);
                    container.Basis = new Basis(rotation);
                    containers.Add(referenceFormId, container);
                    placement = container;
                }
                else
                {
                    placement = new Node3D
                    {
                        Name = $"REFR_{referenceFormId}",
                        Basis = new Basis(rotation),
                    };
                }
                placement.Position = referencePosition;
                placement.Scale = Vector3.One * referenceScale;
                root.AddChild(placement);

                var instance = prototypes[assetId].Scene.Duplicate((int)Node.DuplicateFlags.Default) as Node3D
                    ?? throw new InvalidOperationException($"Could not duplicate cell asset: {assetId}");
                placement.AddChild(instance);
                SetRenderLayer(instance, renderLayer);
                CountGeometry(instance, ref surfaces, ref vertices, ref triangles);
                placedReferences.Add(new PlacedReference(
                    referenceFormId,
                    reference.GetProperty("baseFormId").GetString()!,
                    baseEditorId,
                    assetId,
                    reference.GetProperty("cellFormId").GetString()!,
                    placement,
                    instance,
                    CountGeometry(instance)));
                if (poolManifest is not null && referenceFormId == poolManifest.CueReferenceFormId)
                {
                    poolCuePlacement = placement;
                    poolCueVisual = instance;
                }
                else if (poolManifest is not null && referenceFormId == poolManifest.RackReferenceFormId)
                {
                    poolRackPlacement = placement;
                }
                if (buildCollision && interactionType == "pool-table")
                {
                    if (poolManifest is null ||
                        poolManifest.TableGameplayCollisionSource != "presentation-render-triangles")
                        throw new InvalidOperationException("Unsupported pool table gameplay collision source.");
                    foreach (var mesh in Descendants<MeshInstance3D>(instance))
                    {
                        mesh.CreateTrimeshCollision();
                        foreach (var body in Descendants<StaticBody3D>(mesh))
                            body.CollisionLayer = renderLayer;
                        collisionMeshes++;
                    }
                }
                else if (buildCollision && prototypes[assetId].CollisionScene is Node3D collisionPrototype)
                {
                    var collisionInstance = collisionPrototype.Duplicate((int)Node.DuplicateFlags.Default) as Node3D
                        ?? throw new InvalidOperationException($"Could not duplicate authored collision: {assetId}");
                    collisionInstance.Name = $"AUTHORED_COLLISION_{assetId}";
                    placement.AddChild(collisionInstance);
                    foreach (var collisionMesh in Descendants<MeshInstance3D>(collisionInstance))
                    {
                        collisionMesh.Visible = false;
                        collisionMesh.CreateTrimeshCollision();
                        foreach (var body in Descendants<StaticBody3D>(collisionMesh))
                            body.CollisionLayer = renderLayer;
                        collisionMeshes++;
                    }
                }
                else if (buildCollision &&
                    (collisionAssets.Contains(assetId) ||
                        interactionType is not null and not "pool-table" and not "pool-component"))
                {
                    foreach (var mesh in Descendants<MeshInstance3D>(instance))
                    {
                        mesh.CreateTrimeshCollision();
                        foreach (var body in Descendants<StaticBody3D>(mesh))
                            body.CollisionLayer = renderLayer;
                        collisionMeshes++;
                    }
                }
                loadedReferences++;
            }

            if (poolManifest is not null)
            {
                if (poolTable is null || poolCuePlacement is null || poolCueVisual is null ||
                    poolRackPlacement is null || poolBalls.Count != poolManifest.BallRoles.Count)
                    throw new InvalidOperationException("Prepared pool assembly is incomplete.");
                poolTable.CompleteSetup(
                    poolCuePlacement,
                    poolCueVisual,
                    poolRackPlacement,
                    poolBalls,
                    poolManifest.CueTipEndpoint);
            }

            var proofDoor = source.GetProperty("proof").GetProperty("doorReferenceFormId").GetString()!;
            if (!doors.ContainsKey(proofDoor))
                throw new InvalidOperationException($"Cell proof door was not loaded: {proofDoor}");
            var actors = new List<CellActorLoader.PlacedActor>();
            var actorPaths = actorScenesManifestPath is not null
                ? CellActorLoader.LoadManifest(actorScenesManifestPath, acceptedCellFormIds)
                : actorScenePath is not null
                    ? new[] { actorScenePath }
                    : Array.Empty<string>();
            foreach (var path in actorPaths)
            {
                var placedActor = CellActorLoader.Load(
                    path,
                    acceptedCellFormIds,
                    root,
                    originGameUnits,
                    configuration,
                    proofEnableActor);
                if (placedActor is not null)
                {
                    SetRenderLayer(placedActor.Value.Placement, renderLayer);
                    actors.Add(placedActor.Value);
                }
            }

            Node3D? heldWeapon = null;
            var muzzlePosition = Vector3.Zero;
            StartingLoadout? startingLoadout = null;
            FirstPersonRig.Contract? firstPersonRig = null;
            if (source.TryGetProperty("firstPerson", out var firstPerson))
            {
                if (firstPerson.TryGetProperty("startingLoadout", out var loadout))
                {
                    startingLoadout = new StartingLoadout(
                        loadout.GetProperty("weaponFormId").GetString()!,
                        loadout.GetProperty("weaponEditorId").GetString()!,
                        loadout.GetProperty("ammoFormId").GetString()!,
                        loadout.GetProperty("ammoEditorId").GetString()!,
                        loadout.GetProperty("damage").GetInt32(),
                        loadout.GetProperty("clipSize").GetInt32(),
                        loadout.GetProperty("reserveRounds").GetInt32());
                    if (prepareFirstPersonPresentation)
                    {
                        var heldAssetId = loadout.GetProperty("modelAssetId").GetString()!;
                        heldWeapon = prototypes[heldAssetId].Scene.Duplicate((int)Node.DuplicateFlags.Default) as Node3D
                            ?? throw new InvalidOperationException("Could not duplicate VR held weapon asset.");
                        muzzlePosition = ReadVector(loadout.GetProperty("muzzlePositionGodotUnits"));
                    }
                }
                if (firstPerson.TryGetProperty("rig", out var rig))
                    firstPersonRig = ReadFirstPersonRig(rig);
            }

            var lighting = ReadLighting(source.GetProperty("lighting"));
            return new LoadedContent(
                resolvedScenePath,
                recipeId,
                recipeSha256,
                root,
                formId,
                editorId,
                cell.GetProperty("interior").GetBoolean(),
                acceptedCellFormIds,
                originGameUnits,
                unitScale,
                sourceReferences,
                placedReferences,
                loadedLodBlocks,
                lodCoverage,
                prototypes.Count,
                textures.TwoDimensional.Count,
                textures.AuthoredDdsTextures,
                textures.AuthoredDdsMipChainTextures,
                textures.DecodedAuthoredBc1AlphaMipChainTextures,
                textures.RuntimeGeneratedMipTextures,
                materialBindings,
                loadedReferences,
                doors,
                pickups,
                containers,
                pools,
                actors,
                navigation,
                collisionMeshes,
                surfaces,
                vertices,
                triangles,
                proofDoor,
                heldWeapon,
                muzzlePosition,
                startingLoadout,
                firstPersonRig,
                lighting);
        }
        finally
        {
            foreach (var prototype in prototypes.Values)
            {
                prototype.Scene.Free();
                prototype.CollisionScene?.Free();
            }
        }
    }

    private static SourceReference ReadSourceReference(JsonElement reference) => new(
        reference.GetProperty("formId").GetString()!,
        reference.GetProperty("baseFormId").GetString()!,
        reference.GetProperty("baseEditorId").GetString()!,
        reference.GetProperty("assetId").GetString()!,
        reference.GetProperty("cellFormId").GetString()!,
        ReadVector(reference.GetProperty("positionGodotUnits")),
        reference.GetProperty("initiallyDisabled").GetBoolean());

    private static LodCoverageContract? ReadLodCoverage(
        JsonElement source,
        RuntimeConfiguration configuration)
    {
        if (!source.GetProperty("coverage").TryGetProperty("lod", out var lod))
            return null;
        var bounds = lod.GetProperty("loadedGridBounds");
        var cellSizeGameUnits = lod.GetProperty("cellSizeGameUnits").GetSingle();
        if (!Mathf.IsEqualApprox(
                cellSizeGameUnits,
                configuration.ContentCompiler.ExteriorCellSizeGameUnits))
            throw new InvalidOperationException(
                "CELL LOD coverage uses another exterior cell-size contract.");
        return new LodCoverageContract(
            lod.GetProperty("status").GetString()!,
            lod.GetProperty("level").GetInt32(),
            lod.GetProperty("blockStrideCells").GetInt32(),
            cellSizeGameUnits,
            lod.GetProperty("selectionRadiusCells").GetInt32(),
            lod.GetProperty("selectedBlocks").GetInt32(),
            lod.GetProperty("selectedObjectBlocks").GetInt32(),
            lod.GetProperty("selectedTerrainBlocks").GetInt32(),
            lod.GetProperty("nearCellHolePolicy").GetString()!,
            new LoadedGridBounds(
                bounds.GetProperty("minX").GetInt32(),
                bounds.GetProperty("maxX").GetInt32(),
                bounds.GetProperty("minY").GetInt32(),
                bounds.GetProperty("maxY").GetInt32()),
            lod.GetProperty("blocks")
                .EnumerateArray()
                .Select(ReadExpectedLodBlock)
                .ToArray());
    }

    private static ExpectedLodBlock ReadExpectedLodBlock(JsonElement block) => new(
        block.GetProperty("id").GetString()!,
        block.GetProperty("assetId").GetString()!,
        block.GetProperty("logicalPath").GetString()!,
        block.GetProperty("sourceSha256").GetString()!,
        block.GetProperty("family").GetString()!,
        block.GetProperty("geometryCoordinateSpace").GetString()!,
        block.GetProperty("level").GetInt32(),
        block.GetProperty("variant").GetString()!,
        block.GetProperty("selectionReason").GetString()!,
        ReadVector(block.GetProperty("blockOriginGameUnits")));

    private static void ValidateLodCoverage(
        LodCoverageContract? coverage,
        IReadOnlyList<LoadedLodBlock> blocks,
        Vector3 sceneOriginGameUnits,
        string scenePath)
    {
        if (coverage is null)
        {
            if (blocks.Count != 0)
                throw new InvalidOperationException(
                    $"CELL has LOD blocks without a coverage contract: {scenePath}");
            return;
        }
        var contract = coverage.Value;
        if (contract.Status != "owned-data-selected" ||
            contract.Level <= 0 ||
            contract.BlockStrideCells != contract.Level ||
            contract.CellSizeGameUnits <= 0.0f ||
            contract.SelectionRadiusCells < 0 ||
            contract.ExpectedBlocks.Count != contract.SelectedBlocks ||
            contract.ExpectedBlocks.Select(block => block.Id).Distinct(StringComparer.Ordinal).Count() !=
                contract.ExpectedBlocks.Count ||
            blocks.Count != contract.SelectedBlocks ||
            blocks.Count(block => block.Family == "object") != contract.SelectedObjectBlocks ||
            blocks.Count(block => block.Family == "terrain") != contract.SelectedTerrainBlocks ||
            contract.ExpectedBlocks.Count(block => block.Family == "object") !=
                contract.SelectedObjectBlocks ||
            contract.ExpectedBlocks.Count(block => block.Family == "terrain") !=
                contract.SelectedTerrainBlocks ||
            blocks.Any(block =>
                block.Level != contract.Level ||
                block.Family is not ("object" or "terrain") ||
                block.GeometryCoordinateSpace is not (
                    "world-game-units-baked" or "block-local-game-units")) ||
            contract.ExpectedBlocks.Any(block =>
                block.Level != contract.Level ||
                block.Family is not ("object" or "terrain") ||
                block.GeometryCoordinateSpace is not (
                    "world-game-units-baked" or "block-local-game-units")) ||
            contract.LoadedGridBounds.MinX > contract.LoadedGridBounds.MaxX ||
            contract.LoadedGridBounds.MinY > contract.LoadedGridBounds.MaxY ||
            string.IsNullOrWhiteSpace(contract.NearCellHolePolicy))
            throw new InvalidOperationException(
                $"CELL LOD coverage contract is internally inconsistent: {scenePath}");

        var expectedById = contract.ExpectedBlocks.ToDictionary(
            block => block.Id,
            StringComparer.Ordinal);
        var loadedById = blocks.ToDictionary(
            block => block.Id,
            StringComparer.Ordinal);
        foreach (var (id, expected) in expectedById)
        {
            if (!loadedById.TryGetValue(id, out var loaded))
                throw new InvalidOperationException(
                    $"CELL omitted compiler-selected LOD block {id}: {scenePath}");
            if (loaded.AssetId != expected.AssetId ||
                loaded.LogicalPath != expected.LogicalPath ||
                loaded.SourceSha256 != expected.SourceSha256 ||
                loaded.Family != expected.Family ||
                loaded.GeometryCoordinateSpace != expected.GeometryCoordinateSpace ||
                loaded.Level != expected.Level ||
                loaded.Variant != expected.Variant ||
                loaded.SelectionReason != expected.SelectionReason ||
                !loaded.BlockOriginGameUnits.IsEqualApprox(expected.BlockOriginGameUnits))
                throw new InvalidOperationException(
                    $"CELL runtime LOD block disagrees with compiler coverage block {id}: {scenePath}");

            var sourcePosition = expected.GeometryCoordinateSpace == "world-game-units-baked"
                ? Vector3.Zero
                : expected.BlockOriginGameUnits;
            var expectedGodotPosition = new Vector3(
                sourcePosition.X - sceneOriginGameUnits.X,
                sourcePosition.Z - sceneOriginGameUnits.Z,
                -(sourcePosition.Y - sceneOriginGameUnits.Y));
            if (!loaded.Placement.Position.IsEqualApprox(expectedGodotPosition))
                throw new InvalidOperationException(
                    $"CELL runtime LOD placement disagrees with compiler coordinates for {id}: {scenePath}");
        }
    }

    private static PoolManifest? ReadPoolManifest(JsonElement source)
    {
        if (!source.TryGetProperty("poolGameplay", out var pool) ||
            pool.ValueKind != JsonValueKind.Object)
            return null;
        var table = pool.GetProperty("table");
        var cue = pool.GetProperty("cue");
        var rack = pool.GetProperty("rack");
        var balls = pool.GetProperty("balls")
            .EnumerateArray()
            .ToDictionary(
                ball => ball.GetProperty("referenceFormId").GetString()!,
                ball => ball.GetProperty("role").GetString()!,
                StringComparer.OrdinalIgnoreCase);
        if (balls.Count < 1)
            throw new InvalidOperationException("Pool gameplay manifest has no authored balls.");
        return new PoolManifest(
            table.GetProperty("referenceFormId").GetString()!,
            table.GetProperty("presentationAssetId").GetString()!,
            table.GetProperty("presentationModelPath").GetString()!,
            table.GetProperty("gameplayCollisionSource").GetString()!,
            cue.GetProperty("referenceFormId").GetString()!,
            cue.GetProperty("tipEndpoint").GetString()!,
            rack.GetProperty("referenceFormId").GetString()!,
            balls);
    }

    private static GeometryCounts CountGeometry(Node root)
    {
        var surfaces = 0;
        var vertices = 0;
        var triangles = 0;
        CountGeometry(root, ref surfaces, ref vertices, ref triangles);
        return new GeometryCounts(surfaces, vertices, triangles);
    }

    private static void CountGeometry(
        Node root,
        ref int surfaces,
        ref int vertices,
        ref int triangles)
    {
        foreach (var mesh in Descendants<MeshInstance3D>(root))
        {
            if (mesh.Mesh is null)
                continue;
            surfaces += mesh.Mesh.GetSurfaceCount();
            if (mesh.Mesh is ArrayMesh arrayMesh)
            {
                vertices += Enumerable.Range(0, arrayMesh.GetSurfaceCount())
                    .Sum(arrayMesh.SurfaceGetArrayLen);
                triangles += Enumerable.Range(0, arrayMesh.GetSurfaceCount())
                    .Sum(surface =>
                    {
                        var indexCount = arrayMesh.SurfaceGetArrayIndexLen(surface);
                        return (indexCount > 0 ? indexCount : arrayMesh.SurfaceGetArrayLen(surface)) / 3;
                    });
            }
        }
    }

    private static LightingContract ReadLighting(JsonElement lighting)
    {
        var direction = lighting.GetProperty("directionalRotationDegrees")
            .EnumerateArray()
            .Select(value => value.GetSingle())
            .ToArray();
        if (direction.Length != 2)
            throw new InvalidOperationException("CELL directional rotation must contain two values.");
        var lights = lighting.GetProperty("lights")
            .EnumerateArray()
            .Where(light => !light.GetProperty("initiallyDisabled").GetBoolean())
            .Select(light => new LightContract(
                light.GetProperty("formId").GetString()!,
                light.GetProperty("baseEditorId").GetString()!,
                ReadVector(light.GetProperty("positionGodotUnits")),
                ReadColor(light.GetProperty("color")),
                light.GetProperty("intensity").GetSingle(),
                light.GetProperty("radiusMeters").GetSingle()))
            .ToArray();
        return new LightingContract(
            lighting.TryGetProperty("mode", out var mode) ? mode.GetString()! : "interior-xcll",
            ReadColor(lighting.GetProperty("ambientColor")),
            ReadColor(lighting.GetProperty("directionalColor")),
            ReadColor(lighting.GetProperty("fogColor")),
            lighting.GetProperty("fogNearGameUnits").GetSingle(),
            lighting.GetProperty("fogFarGameUnits").GetSingle(),
            lighting.GetProperty("fogPower").GetSingle(),
            new Vector2(direction[0], direction[1]),
            lighting.GetProperty("directionalFade").GetSingle(),
            lights);
    }

    private static FirstPersonRig.Contract ReadFirstPersonRig(JsonElement source)
    {
        var hands = source.GetProperty("hands");
        return new FirstPersonRig.Contract(
            source.GetProperty("schema").GetString()!,
            source.GetProperty("status").GetString()!,
            source.GetProperty("provider").GetString()!,
            source.GetProperty("cameraBone").GetString()!,
            source.GetProperty("weaponBone").GetString()!,
            ReadFirstPersonHand(hands.GetProperty("left")),
            ReadFirstPersonHand(hands.GetProperty("right")));
    }

    private static FirstPersonRig.HandContract ReadFirstPersonHand(JsonElement source) => new(
        source.GetProperty("model").GetString()!,
        source.GetProperty("sidecar").GetString()!,
        source.GetProperty("modelSha256").GetString()!,
        source.GetProperty("sidecarSha256").GetString()!,
        source.GetProperty("gripBone").GetString()!);

    private static Vector3 ReadVector(JsonElement array)
    {
        var values = array.EnumerateArray().Select(value => value.GetSingle()).ToArray();
        if (values.Length != 3)
            throw new InvalidOperationException("Cell scene vector must contain three values.");
        return new Vector3(values[0], values[1], values[2]);
    }

    private static Color ReadColor(JsonElement array)
    {
        var values = array.EnumerateArray().Select(value => value.GetSingle()).ToArray();
        if (values.Length != 3)
            throw new InvalidOperationException("Cell scene color must contain three values.");
        return new Color(values[0], values[1], values[2]);
    }

    private static Quaternion ReadQuaternion(JsonElement array)
    {
        var values = array.EnumerateArray().Select(value => value.GetSingle()).ToArray();
        if (values.Length != 4)
            throw new InvalidOperationException("Cell scene quaternion must contain four values.");
        return new Quaternion(values[0], values[1], values[2], values[3]).Normalized();
    }

    private static void SetRenderLayer(Node root, uint layer)
    {
        foreach (var mesh in Descendants<MeshInstance3D>(root))
            mesh.Layers = layer;
    }

    private static void SetShadowCasting(
        Node root,
        GeometryInstance3D.ShadowCastingSetting setting)
    {
        foreach (var mesh in Descendants<MeshInstance3D>(root))
            mesh.CastShadow = setting;
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

    internal readonly record struct LoadedContent(
        string ScenePath,
        string RecipeId,
        string RecipeSha256,
        Node3D Root,
        string FormId,
        string EditorId,
        bool Interior,
        IReadOnlySet<string> SourceCellFormIds,
        Vector3 OriginGameUnits,
        float UnitsToMeters,
        IReadOnlyList<SourceReference> SourceReferences,
        IReadOnlyList<PlacedReference> PlacedReferences,
        IReadOnlyList<LoadedLodBlock> LodBlocks,
        LodCoverageContract? LodCoverage,
        int Assets,
        int Textures,
        int AuthoredDdsTextures,
        int AuthoredDdsMipChainTextures,
        int DecodedAuthoredBc1AlphaMipChainTextures,
        int RuntimeGeneratedMipTextures,
        int MaterialBindings,
        int References,
        IReadOnlyDictionary<string, DoorInstance> Doors,
        IReadOnlyDictionary<string, PickupInstance> Pickups,
        IReadOnlyDictionary<string, ContainerInstance> Containers,
        IReadOnlyDictionary<string, PoolTableInstance> Pools,
        IReadOnlyList<CellActorLoader.PlacedActor> Actors,
        CellNavigationGraph Navigation,
        int CollisionMeshes,
        int Surfaces,
        int Vertices,
        int Triangles,
        string ProofDoorFormId,
        Node3D? HeldWeapon,
        Vector3 MuzzlePosition,
        StartingLoadout? StartingLoadout,
        FirstPersonRig.Contract? FirstPersonRig,
        LightingContract Lighting);

    internal readonly record struct SourceReference(
        string FormId,
        string BaseFormId,
        string BaseEditorId,
        string AssetId,
        string SourceCellFormId,
        Vector3 PositionGodotUnits,
        bool InitiallyDisabled);

    internal readonly record struct GeometryCounts(
        int Surfaces,
        int Vertices,
        int Triangles);

    internal sealed record PlacedReference(
        string FormId,
        string BaseFormId,
        string BaseEditorId,
        string AssetId,
        string SourceCellFormId,
        Node3D Placement,
        Node3D Visual,
        GeometryCounts Geometry);

    internal sealed record LoadedLodBlock(
        string Id,
        string AssetId,
        string LogicalPath,
        string SourceSha256,
        string Family,
        string GeometryCoordinateSpace,
        int Level,
        string Variant,
        string SelectionReason,
        Vector3 BlockOriginGameUnits,
        Node3D Placement,
        Node3D Visual,
        GeometryCounts Geometry);

    internal readonly record struct LodCoverageContract(
        string Status,
        int Level,
        int BlockStrideCells,
        float CellSizeGameUnits,
        int SelectionRadiusCells,
        int SelectedBlocks,
        int SelectedObjectBlocks,
        int SelectedTerrainBlocks,
        string NearCellHolePolicy,
        LoadedGridBounds LoadedGridBounds,
        IReadOnlyList<ExpectedLodBlock> ExpectedBlocks);

    internal readonly record struct ExpectedLodBlock(
        string Id,
        string AssetId,
        string LogicalPath,
        string SourceSha256,
        string Family,
        string GeometryCoordinateSpace,
        int Level,
        string Variant,
        string SelectionReason,
        Vector3 BlockOriginGameUnits);

    internal readonly record struct LoadedGridBounds(
        int MinX,
        int MaxX,
        int MinY,
        int MaxY);

    internal readonly record struct StartingLoadout(
        string WeaponFormId,
        string WeaponEditorId,
        string AmmoFormId,
        string AmmoEditorId,
        int Damage,
        int ClipSize,
        int ReserveRounds);

    internal readonly record struct LightingContract(
        string Mode,
        Color AmbientColor,
        Color DirectionalColor,
        Color FogColor,
        float FogNearGameUnits,
        float FogFarGameUnits,
        float FogPower,
        Vector2 DirectionalRotationDegrees,
        float DirectionalFade,
        IReadOnlyList<LightContract> Lights);

    internal readonly record struct LightContract(
        string FormId,
        string EditorId,
        Vector3 PositionGodotUnits,
        Color Color,
        float Intensity,
        float RadiusMeters);

    private sealed record PoolManifest(
        string TableReferenceFormId,
        string TablePresentationAssetId,
        string TablePresentationModelPath,
        string TableGameplayCollisionSource,
        string CueReferenceFormId,
        string CueTipEndpoint,
        string RackReferenceFormId,
        IReadOnlyDictionary<string, string> BallRoles);
}
