using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Godot;

using OpenNV.Runtime.SceneGraph;


using OpenNV.Runtime.Content;
using OpenNV.Runtime.Presentation.Rendering;
using OpenNV.Runtime.Presentation.OpenXR;
using OpenNV.Runtime.World.Interactions;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.Gameplay.Items;
using OpenNV.Runtime.Gameplay.Crafting;

namespace OpenNV.Runtime.World.Cells;

internal static class CellContentLoader
{
    private const string CellSceneSchema = "opennv-cell-scene/v14";
    private const string DoorArticulationSchema = "opennv-controller-door-articulation/v1";
    private const string DoorArticulationStatus = "owned-open-close-transform-complete";
    private const float TransformTolerance = 0.00001f;

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
        uint renderLayer,
        RuntimeMaterialLoader.TextureCache? textureCache = null)
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
        var landscapeCollisionAssets = new HashSet<string>(StringComparer.Ordinal);
        var collisionFaceSelections = new Dictionary<string, string>(StringComparer.Ordinal);
        var controllerPlaybacks = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var doorArticulations = new Dictionary<string, DoorArticulationContract>(StringComparer.Ordinal);
        try
        {
            var textures = RuntimeMaterialLoader.LoadTextures(
                source,
                configuration.Renderer,
                textureCache);
            var materialBindings = 0;
            var defaultCompiler = source.GetProperty("compiler");
            foreach (var asset in source.GetProperty("assets").EnumerateArray())
            {
                var assetId = asset.GetProperty("id").GetString()!;
                assetLogicalPaths.Add(assetId, asset.GetProperty("logicalPath").GetString()!);
                var loaded = VerifiedGltfLoader.Load(
                    asset.GetProperty("model").GetString()!,
                    asset.GetProperty("sidecar").GetString()!);
                var articulation = ReadDoorArticulation(asset, $"CELL asset {assetId}");
                var sidecarArticulation = ReadDoorArticulation(
                    loaded.ArticulationJson,
                    $"static sidecar {assetId}");
                if ((articulation is null) != (sidecarArticulation is null) ||
                    articulation is not null &&
                    !articulation.CanonicalSha256.Equals(
                        sidecarArticulation!.CanonicalSha256,
                        StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(
                        $"CELL asset articulation differs from its static sidecar: {assetId}");
                if (articulation is not null)
                    doorArticulations.Add(assetId, articulation);
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
                if (asset.TryGetProperty("controllerPlayback", out var playback) &&
                    playback.ValueKind != JsonValueKind.Null)
                {
                    var animations = playback.GetProperty("animations")
                        .EnumerateArray()
                        .ToArray();
                    if (playback.GetProperty("status").GetString() !=
                            "source-looping-controller-complete" ||
                        animations.Length == 0 ||
                        animations.Any(animation =>
                            animation.GetProperty("channels").GetInt32() <= 0 ||
                            animation.GetProperty("stopSeconds").GetSingle() <=
                                animation.GetProperty("startSeconds").GetSingle() ||
                            animation.GetProperty("frequency").GetSingle() <= 0.0f))
                        throw new InvalidOperationException(
                            $"Unsupported source controller playback contract: {assetId}");
                    controllerPlaybacks.Add(
                        assetId,
                        animations.Select(animation =>
                            animation.GetProperty("name").GetString()!).ToArray());
                }
                var collision = asset.GetProperty("collision");
                var faceSelection = collision.GetProperty("faceSelection").GetString()!;
                if (faceSelection is not "all-source-faces" and not "source-upward-walkable-deck")
                    throw new InvalidOperationException(
                        $"Unsupported authored collision face selection: {faceSelection}");
                collisionFaceSelections.Add(assetId, faceSelection);
                if (collision.GetProperty("enabled").GetBoolean())
                {
                    var collisionSource = collision.GetProperty("source").GetString();
                    if (loaded.CollisionScene is null && collisionSource != "LAND-height-grid")
                        throw new InvalidOperationException($"Authored collision payload is missing: {assetId}");
                    collisionAssets.Add(assetId);
                    if (collisionSource == "LAND-height-grid")
                        landscapeCollisionAssets.Add(assetId);
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
            var interior = cell.GetProperty("interior").GetBoolean();
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
            var landscapeCollisionMeshes = new List<MeshInstance3D>();
            var startingWeaponFormId = source.TryGetProperty("firstPerson", out var firstPersonSource)
                ? firstPersonSource.GetProperty("startingLoadout").GetProperty("weaponFormId").GetString()
                : null;
            foreach (var reference in source.GetProperty("references").EnumerateArray())
            {
                var initiallyDisabled = reference.GetProperty("initiallyDisabled").GetBoolean();
                var enableParentFormId = reference.TryGetProperty(
                    "enableParentFormId", out var enableParentSource) &&
                    enableParentSource.ValueKind == JsonValueKind.String
                        ? enableParentSource.GetString()
                        : null;
                var enableParentInitiallyDisabled = enableParentFormId is not null &&
                    reference.GetProperty("enableParentInitiallyDisabled").GetBoolean();
                var enableParentOpposite = enableParentFormId is not null &&
                    reference.GetProperty("enableParentOpposite").GetBoolean();
                var initiallyEnabled = !initiallyDisabled &&
                    (enableParentFormId is null ||
                        enableParentInitiallyDisabled == enableParentOpposite);
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
                doorArticulations.TryGetValue(assetId, out var doorArticulation);
                if (doorArticulation is not null && interactionType != "door")
                    throw new InvalidOperationException(
                        $"Controller articulation is attached to a non-door reference: {referenceFormId}");
                var referencePosition = ReadVector(reference.GetProperty("positionGodotUnits"));
                var referenceScale = reference.GetProperty("scale").GetSingle();
                var baseRecordType = reference.GetProperty("baseRecordType").GetString()!;
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
                    var destinationTransform = reference.GetProperty("teleportDestinationTransform");
                    DoorInstance.TeleportDestination? teleportDestination = null;
                    if (destinationTransform.ValueKind == JsonValueKind.Object)
                    {
                        teleportDestination = new DoorInstance.TeleportDestination(
                            ReadVector(destinationTransform.GetProperty("positionGameUnits")),
                            destinationTransform.GetProperty("yawGodotRadians").GetSingle());
                    }
                    var door = new DoorInstance { Name = $"DOOR_{referenceFormId}" };
                    door.Configure(
                        referenceFormId,
                        yaw,
                        configuration.Door.OpenAngleDegrees,
                        destination.ValueKind == JsonValueKind.String ? destination.GetString() : null,
                        teleportDestination);
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
                        ReadItemDefinition(interaction),
                        interaction.GetProperty("count").GetInt32(),
                        weapon);
                    var dynamicBodies = prototypes[assetId].DynamicPhysicsBodies;
                    if (dynamicBodies.Count > 1)
                        throw new InvalidOperationException(
                            $"Pickup has ambiguous authored dynamic bodies: {referenceFormId}");
                    if (dynamicBodies.Count == 1)
                        pickup.ConfigurePhysics(dynamicBodies[0], configuration.Pickup);
                    pickup.Basis = new Basis(rotation);
                    pickups.Add(referenceFormId, pickup);
                    placement = pickup;
                }
                else if (interactionType == "scripted-activator" &&
                    interaction.GetProperty("support").GetString() == "delayed-objective-events")
                {
                    var activator = new ScriptedActivatorInstance();
                    activator.Configure(
                        referenceFormId,
                        reference.GetProperty("baseFormId").GetString()!,
                        baseEditorId,
                        ScriptedActivatorContract.Read(interaction));
                    var dynamicBodies = prototypes[assetId].DynamicPhysicsBodies;
                    if (dynamicBodies.Count != 1)
                        throw new InvalidOperationException(
                            "Scripted activator requires one authored dynamic body: " +
                            referenceFormId);
                    activator.ConfigurePhysics(dynamicBodies[0], configuration.Pickup);
                    activator.Basis = new Basis(rotation);
                    placement = activator;
                }
                else if (interactionType == "crafting-station" &&
                    interaction.GetProperty("support").GetString() == "unconditioned-zero-skill-recipes")
                {
                    var station = new CraftingStationInstance();
                    station.Configure(
                        referenceFormId,
                        baseEditorId,
                        CraftingStationContract.Read(interaction));
                    station.Basis = new Basis(rotation);
                    placement = station;
                }
                else if (interactionType == "container")
                {
                    var entries = interaction.GetProperty("items")
                        .EnumerateArray()
                        .Select(item =>
                        {
                            var resolved = item.GetProperty("resolved").GetBoolean();
                            return new ContainerInstance.Entry(
                                item.GetProperty("itemFormId").GetString()!,
                                resolved ? ReadItemDefinition(item) : null,
                                item.GetProperty("count").GetInt32(),
                                resolved);
                        })
                        .ToArray();
                    var container = new ContainerInstance();
                    container.Configure(
                        referenceFormId,
                        reference.GetProperty("baseEditorId").GetString()!,
                        interaction.TryGetProperty("displayName", out var containerDisplayName)
                            ? containerDisplayName.GetString() ?? ""
                            : "",
                        entries);
                    container.Basis = new Basis(rotation);
                    containers.Add(referenceFormId, container);
                    placement = container;
                }
                else if (baseRecordType == "MSTT")
                {
                    var dynamicBodies = prototypes[assetId].DynamicPhysicsBodies;
                    if (dynamicBodies.Count != 1)
                        throw new InvalidOperationException(
                            $"Moving static requires one authored dynamic body: {referenceFormId}");
                    var movingStatic = new MovingStaticInstance();
                    movingStatic.Configure(
                        referenceFormId,
                        dynamicBodies[0],
                        configuration.Pickup);
                    movingStatic.Freeze = !buildCollision;
                    if (!buildCollision)
                    {
                        movingStatic.CollisionLayer = 0u;
                        movingStatic.CollisionMask = 0u;
                    }
                    placement = movingStatic;
                    if (buildCollision)
                        collisionMeshes += dynamicBodies[0].Hulls.Count +
                            dynamicBodies[0].Spheres.Count;
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
                Node3D visual = instance;
                Node3D? articulationTarget = null;
                Node3D? verifiedAuthoredCollision = null;
                if (doorArticulation is null)
                {
                    placement.AddChild(instance);
                }
                else
                {
                    var presentationRoot = new Node3D
                    {
                        Name = $"DOOR_PRESENTATION_{referenceFormId}",
                    };
                    placement.AddChild(presentationRoot);
                    presentationRoot.AddChild(instance);
                    articulationTarget = new Node3D
                    {
                        Name = doorArticulation.Target.VisualNodeName,
                        Transform = doorArticulation.ClosedLocalTransform,
                    };
                    presentationRoot.AddChild(articulationTarget);
                    MoveArticulatedVisuals(
                        instance,
                        articulationTarget,
                        doorArticulation);
                    visual = presentationRoot;
                }
                if (controllerPlaybacks.TryGetValue(assetId, out var sourceSequences))
                    StartSourceControllerPlayback(instance, sourceSequences);
                SetRenderLayer(visual, renderLayer);
                CountGeometry(visual, ref surfaces, ref vertices, ref triangles);
                placedReferences.Add(new PlacedReference(
                    referenceFormId,
                    reference.GetProperty("baseFormId").GetString()!,
                    baseEditorId,
                    assetId,
                    reference.GetProperty("cellFormId").GetString()!,
                    placement,
                    visual,
                    CountGeometry(visual)));
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
                    foreach (var mesh in NodeTraversal.Descendants<MeshInstance3D>(instance))
                    {
                        CreateDoubleSidedTrimeshCollision(mesh, renderLayer);
                        collisionMeshes++;
                    }
                }
                else if (doorArticulation is not null)
                {
                    if (placement is not DoorInstance articulatedDoor ||
                        articulationTarget is null ||
                        prototypes[assetId].CollisionScene is not Node3D articulatedCollisionPrototype)
                        throw new InvalidOperationException(
                            $"Controller door articulation is missing authored collision: {referenceFormId}");
                    var collisionInstance = articulatedCollisionPrototype.Duplicate(
                            (int)Node.DuplicateFlags.Default) as Node3D
                        ?? throw new InvalidOperationException(
                            $"Could not duplicate articulated door collision: {assetId}");
                    collisionInstance.Name = $"AUTHORED_COLLISION_{assetId}";
                    placement.AddChild(collisionInstance);
                    collisionMeshes += AttachArticulatedDoorCollision(
                        collisionInstance,
                        articulationTarget,
                        doorArticulation,
                        prototypes[assetId].AuthoredConvexBodies,
                        prototypes[assetId].SourceSha256,
                        buildCollision,
                        renderLayer);
                    if (buildCollision)
                    {
                        foreach (var collisionMesh in NodeTraversal.Descendants<MeshInstance3D>(collisionInstance))
                        {
                            collisionMesh.Visible = false;
                            CreateDoubleSidedTrimeshCollision(collisionMesh, renderLayer);
                            collisionMeshes++;
                        }
                    }
                    else
                    {
                        placement.RemoveChild(collisionInstance);
                        collisionInstance.Free();
                    }
                    articulatedDoor.ConfigureSourceArticulation(
                        articulationTarget,
                        new Basis(rotation).Scaled(Vector3.One * referenceScale),
                        doorArticulation.Open.ToRuntimeSequence(),
                        doorArticulation.Close.ToRuntimeSequence());
                    articulatedDoor.RestoreOpenState(session.IsDoorOpen(referenceFormId));
                }
                else if (buildCollision && placement is not MovingStaticInstance &&
                    prototypes[assetId].CollisionScene is Node3D collisionPrototype)
                {
                    var collisionInstance = collisionPrototype.Duplicate((int)Node.DuplicateFlags.Default) as Node3D
                        ?? throw new InvalidOperationException($"Could not duplicate authored collision: {assetId}");
                    collisionInstance.Name = $"AUTHORED_COLLISION_{assetId}";
                    placement.AddChild(collisionInstance);
                    verifiedAuthoredCollision = collisionInstance;
                    if (collisionFaceSelections[assetId] == "source-upward-walkable-deck")
                        collisionMeshes += BuildWalkableRoadCollision(
                            placement,
                            collisionInstance,
                            renderLayer);
                    else
                    {
                        foreach (var collisionMesh in NodeTraversal.Descendants<MeshInstance3D>(collisionInstance))
                        {
                            collisionMesh.Visible = false;
                            CreateDoubleSidedTrimeshCollision(collisionMesh, renderLayer);
                            collisionMeshes++;
                        }
                    }
                }
                else if (buildCollision &&
                    (collisionAssets.Contains(assetId) ||
                        interactionType is not null and not "pool-table" and not "pool-component") &&
                    placement is not PickupInstance { CanGrab: true } and not MovingStaticInstance)
                {
                    foreach (var mesh in NodeTraversal.Descendants<MeshInstance3D>(instance))
                    {
                        if (landscapeCollisionAssets.Contains(assetId))
                            landscapeCollisionMeshes.Add(mesh);
                        else
                        {
                            CreateDoubleSidedTrimeshCollision(mesh, renderLayer);
                            collisionMeshes++;
                        }
                    }
                }
                if (placement is DoorInstance ordinaryDoor && doorArticulation is null)
                {
                    ValidateSinglePieceDoor(
                        referenceFormId,
                        instance,
                        verifiedAuthoredCollision,
                        buildCollision);
                    ordinaryDoor.RestoreOpenState(session.IsDoorOpen(referenceFormId));
                }
                if (placement is ScriptedActivatorInstance scriptedActivator)
                {
                    scriptedActivator.Freeze = !buildCollision || !scriptedActivator.CanGrab;
                    if (!buildCollision)
                    {
                        scriptedActivator.CollisionLayer = 0u;
                        scriptedActivator.CollisionMask = 0u;
                    }
                    scriptedActivator.CaptureAuthoredTransform();
                }
                else if (placement is PickupInstance loadedPickup)
                {
                    loadedPickup.Freeze = !buildCollision || !loadedPickup.CanGrab;
                    if (!buildCollision)
                    {
                        loadedPickup.CollisionLayer = 0u;
                        loadedPickup.CollisionMask = 0u;
                    }
                    loadedPickup.CaptureAuthoredTransform();
                    session.RegisterPickup(loadedPickup);
                }
                GamebryoReferenceEnableRuntime.Apply(placement, initiallyEnabled);
                loadedReferences++;
            }

            if (buildCollision && landscapeCollisionMeshes.Count > 0)
            {
                BuildLandscapeCollision(root, landscapeCollisionMeshes, renderLayer);
                collisionMeshes++;
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
                    proofEnableActor,
                    materializeInitiallyDisabled: true,
                    collisionLayer: renderLayer);
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
                        loadout.TryGetProperty("weaponDisplayName", out var weaponDisplayName)
                            ? weaponDisplayName.GetString()
                            : null,
                        loadout.GetProperty("ammoFormId").GetString()!,
                        loadout.GetProperty("ammoEditorId").GetString()!,
                        loadout.TryGetProperty("ammoDisplayName", out var ammoDisplayName)
                            ? ammoDisplayName.GetString()
                            : null,
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
            var exteriorEnvironment = interior
                ? null
                : RetailExteriorEnvironment.Load(
                    source,
                    configuration.FalloutEnvironment.ImageSpace);
            return new LoadedContent(
                resolvedScenePath,
                recipeId,
                recipeSha256,
                root,
                formId,
                editorId,
                interior,
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
                lighting,
                exteriorEnvironment);
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

    private static ItemDefinition ReadItemDefinition(JsonElement source) =>
        ItemDefinition.ReadCompiled(source);

    private static SourceReference ReadSourceReference(JsonElement reference) => new(
        reference.GetProperty("formId").GetString()!,
        reference.GetProperty("baseFormId").GetString()!,
        reference.GetProperty("baseRecordType").GetString()!,
        reference.GetProperty("baseEditorId").GetString()!,
        reference.GetProperty("assetId").GetString()!,
        reference.GetProperty("cellFormId").GetString()!,
        ReadVector(reference.GetProperty("positionGodotUnits")),
        reference.GetProperty("initiallyDisabled").GetBoolean(),
        reference.TryGetProperty("enableParentFormId", out var enableParent) &&
            enableParent.ValueKind == JsonValueKind.String
                ? enableParent.GetString()
                : null,
        reference.TryGetProperty("enableParentOpposite", out var opposite) &&
            opposite.GetBoolean());

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

    private static DoorArticulationContract? ReadDoorArticulation(
        JsonElement container,
        string owner)
    {
        if (!container.TryGetProperty("articulation", out var articulation) ||
            articulation.ValueKind == JsonValueKind.Null)
            return null;
        if (articulation.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException($"Door articulation is not an object: {owner}");
        return ReadDoorArticulationObject(articulation, owner);
    }

    private static DoorArticulationContract? ReadDoorArticulation(
        string? articulationJson,
        string owner)
    {
        if (articulationJson is null)
            return null;
        using var document = JsonDocument.Parse(articulationJson);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException($"Door articulation is not an object: {owner}");
        return ReadDoorArticulationObject(document.RootElement, owner);
    }

    private static DoorArticulationContract ReadDoorArticulationObject(
        JsonElement source,
        string owner)
    {
        if (source.GetProperty("schema").GetString() != DoorArticulationSchema ||
            source.GetProperty("status").GetString() != DoorArticulationStatus)
            throw new InvalidOperationException($"Unexpected door articulation contract: {owner}");
        var expectedHash = source.GetProperty("canonicalSha256").GetString()!;
        var actualHash = CanonicalSha256(source);
        if (!IsSha256(expectedHash) ||
            !actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Door articulation hash mismatch: {owner}");

        var targetSource = source.GetProperty("target");
        var targetId = RequiredString(targetSource, "targetId", owner);
        var sourceBlockIndex = targetSource.GetProperty("sourceBlockIndex").GetInt32();
        var sourceName = RequiredString(targetSource, "sourceName", owner);
        var visualNodeName = RequiredString(targetSource, "visualNodeName", owner);
        var collisionNodeName = RequiredString(targetSource, "collisionNodeName", owner);
        var visualSurfaceStableIds = ReadSortedUniqueStrings(
            targetSource.GetProperty("visualSurfaceStableIds"),
            owner,
            "visual surface stable IDs");
        var collisionBodyBlocks = ReadSortedUniqueIntegers(
            targetSource.GetProperty("collisionBodyBlocks"),
            owner,
            "collision body blocks");
        var visualDescendantNodeNames = ReadSortedUniqueStrings(
            targetSource.GetProperty("visualDescendantNodeNames"),
            owner,
            "visual descendant nodes");
        var collisionDescendantNodeNames = ReadSortedUniqueStrings(
            targetSource.GetProperty("collisionDescendantNodeNames"),
            owner,
            "collision descendant nodes");
        var expectedTargetNodeName = $"OPENNV_ARTICULATION_{targetId}";
        if (sourceBlockIndex < 0 ||
            string.IsNullOrWhiteSpace(sourceName) ||
            visualNodeName != expectedTargetNodeName ||
            collisionNodeName != expectedTargetNodeName ||
            !visualDescendantNodeNames.ToHashSet(StringComparer.Ordinal).SetEquals(
                visualSurfaceStableIds.Select(id => $"OPENNV_ARTICULATION_VISUAL_{id}")) ||
            !collisionDescendantNodeNames.ToHashSet(StringComparer.Ordinal).SetEquals(
                collisionBodyBlocks.Select(block => $"OPENNV_ARTICULATION_COLLISION_BODY_{block}")))
            throw new InvalidOperationException($"Door articulation target join is invalid: {owner}");

        var closed = ReadLocalTransform(source.GetProperty("closedLocalTransform"), owner);
        var sequences = source.GetProperty("sequences");
        var open = ReadDoorArticulationSequence(sequences.GetProperty("open"), "Open", owner);
        var close = ReadDoorArticulationSequence(sequences.GetProperty("close"), "Close", owner);
        if (!TransformsMatch(open.Initial, closed) ||
            !TransformsMatch(close.Terminal, closed) ||
            TransformsMatch(open.Terminal, closed))
            throw new InvalidOperationException(
                $"Door articulation open/close terminals are inconsistent: {owner}");
        return new DoorArticulationContract(
            expectedHash,
            new DoorArticulationTarget(
                targetId,
                visualNodeName,
                collisionNodeName,
                visualDescendantNodeNames,
                collisionDescendantNodeNames),
            closed,
            open,
            close);
    }

    private static DoorArticulationSequence ReadDoorArticulationSequence(
        JsonElement source,
        string expectedName,
        string owner)
    {
        if (RequiredString(source, "sourceName", owner) != expectedName)
            throw new InvalidOperationException(
                $"Door articulation sequence name differs: {owner} {expectedName}");
        var start = source.GetProperty("startSeconds").GetSingle();
        var stop = source.GetProperty("stopSeconds").GetSingle();
        var duration = source.GetProperty("durationSeconds").GetSingle();
        var initial = ReadLocalTransform(source.GetProperty("initialLocalTransform"), owner);
        var terminal = ReadLocalTransform(source.GetProperty("terminalLocalTransform"), owner);
        var interpolation = source.GetProperty("keyInterpolation");
        _ = RequiredString(interpolation, "rotation", owner);
        _ = RequiredString(interpolation, "translation", owner);
        _ = RequiredString(interpolation, "scale", owner);
        if (!IsSha256(RequiredString(source, "keySha256", owner)))
            throw new InvalidOperationException(
                $"Door articulation sequence key hash is invalid: {owner} {expectedName}");
        var sourceJoin = source.GetProperty("source");
        if (sourceJoin.GetProperty("sequenceBlock").GetInt32() < 0 ||
            sourceJoin.GetProperty("controllerBlock").GetInt32() < 0 ||
            sourceJoin.GetProperty("interpolatorBlock").GetInt32() < 0 ||
            sourceJoin.GetProperty("transformDataBlock").GetInt32() < 0 ||
            !float.IsFinite(start) ||
            !float.IsFinite(stop) ||
            !float.IsFinite(duration) ||
            stop <= start ||
            duration <= 0.0f ||
            !Mathf.IsEqualApprox(stop - start, duration))
            throw new InvalidOperationException(
                $"Door articulation sequence timing/source join is invalid: {owner} {expectedName}");
        return new DoorArticulationSequence(initial, terminal, duration);
    }

    private static Transform3D ReadLocalTransform(JsonElement source, string owner)
    {
        var translation = ReadVector(source.GetProperty("translationGodotUnits"));
        var values = source.GetProperty("rotationGodotQuaternion")
            .EnumerateArray()
            .Select(value => value.GetSingle())
            .ToArray();
        var scale = source.GetProperty("scale").GetSingle();
        if (values.Length != 4 ||
            !values.All(float.IsFinite) ||
            !float.IsFinite(scale) ||
            scale <= 0.0f)
            throw new InvalidOperationException($"Door articulation transform is invalid: {owner}");
        var quaternion = new Quaternion(values[0], values[1], values[2], values[3]);
        if (!Mathf.IsEqualApprox(quaternion.LengthSquared(), 1.0f))
            throw new InvalidOperationException(
                $"Door articulation quaternion is not normalized: {owner}");
        return new Transform3D(
            new Basis(quaternion).Scaled(Vector3.One * scale),
            translation);
    }

    private static void MoveArticulatedVisuals(
        Node3D visualRoot,
        Node3D articulationTarget,
        DoorArticulationContract contract)
    {
        var wrapper = FindUniqueArticulationWrapper(
            visualRoot,
            contract.Target.VisualNodeName,
            contract.ClosedLocalTransform,
            "visual");
        var meshes = ValidateWrapperMeshes(
            wrapper,
            contract.Target.VisualDescendantNodeNames,
            "visual");
        foreach (var mesh in meshes)
        {
            wrapper.RemoveChild(mesh);
            articulationTarget.AddChild(mesh);
        }
        wrapper.GetParent().RemoveChild(wrapper);
        wrapper.Free();
    }

    private static int AttachArticulatedDoorCollision(
        Node3D collisionRoot,
        Node3D articulationTarget,
        DoorArticulationContract contract,
        IReadOnlyList<VerifiedGltfLoader.AuthoredConvexBodyContract> convexBodies,
        string sourceSha256,
        bool buildCollision,
        uint collisionLayer)
    {
        var wrapper = FindUniqueArticulationWrapper(
            collisionRoot,
            contract.Target.CollisionNodeName,
            contract.ClosedLocalTransform,
            "collision");
        var meshes = ValidateWrapperMeshes(
            wrapper,
            contract.Target.CollisionDescendantNodeNames,
            "collision");
        var convexByNodeName = convexBodies.ToDictionary(
            body => $"OPENNV_ARTICULATION_COLLISION_BODY_{body.BodyBlock}",
            StringComparer.Ordinal);
        if (convexBodies.Any(body => body.OwnerTargetId != contract.Target.TargetId) ||
            convexByNodeName.Keys.Any(name =>
                !contract.Target.CollisionDescendantNodeNames.Contains(name, StringComparer.Ordinal)))
            throw new InvalidOperationException(
                $"Authored convex collision is joined to another articulation target: {contract.Target.TargetId}");
        var bodies = 0;
        var consumedConvexBodies = 0;
        if (buildCollision)
        {
            foreach (var mesh in meshes)
            {
                Shape3D shape;
                VerifiedGltfLoader.AuthoredConvexBodyContract? convexBody = null;
                if (convexByNodeName.TryGetValue(mesh.Name.ToString(), out var sourceConvexBody))
                {
                    if (!TransformsMatch(mesh.Transform, Transform3D.Identity))
                        throw new InvalidOperationException(
                            $"Authored convex collision node has an unexpected transform: {mesh.Name}");
                    shape = new ConvexPolygonShape3D
                    {
                        Points = sourceConvexBody.PointsGodotGameUnits.ToArray(),
                        Margin = 0.0f,
                    };
                    convexBody = sourceConvexBody;
                    consumedConvexBodies++;
                }
                else
                {
                    if (mesh.Mesh is null || mesh.Mesh.GetFaces().Length == 0)
                        throw new InvalidOperationException(
                            $"Articulated door collision mesh is empty: {mesh.Name}");
                    shape = mesh.Mesh.CreateTrimeshShape() ??
                        throw new InvalidOperationException(
                            $"Could not construct articulated door collision: {mesh.Name}");
                }
                var body = new StaticBody3D
                {
                    Name = $"AUTHORED_{mesh.Name}",
                    Transform = mesh.Transform,
                    CollisionLayer = collisionLayer,
                };
                body.SetMeta("opennv_articulation_target_id", contract.Target.TargetId);
                if (convexBody is not null)
                    ApplyAuthoredConvexIdentity(body, convexBody.Value, sourceSha256);
                articulationTarget.AddChild(body);
                body.AddChild(new CollisionShape3D { Shape = shape });
                bodies++;
            }
        }
        wrapper.GetParent().RemoveChild(wrapper);
        wrapper.Free();
        if (buildCollision && bodies != contract.Target.CollisionDescendantNodeNames.Count)
            throw new InvalidOperationException(
                $"Articulated door collision join is incomplete: {contract.Target.TargetId}");
        if (buildCollision && consumedConvexBodies != convexBodies.Count)
            throw new InvalidOperationException(
                $"Authored convex collision join is incomplete: {contract.Target.TargetId}");
        return bodies;
    }

    private static void ApplyAuthoredConvexIdentity(
        StaticBody3D target,
        VerifiedGltfLoader.AuthoredConvexBodyContract source,
        string sourceSha256)
    {
        target.PhysicsMaterialOverride = new PhysicsMaterial
        {
            Friction = source.Friction,
            Bounce = source.Restitution,
        };
        target.SetMeta("opennv_collision_shape_type", "convex-hull-points");
        target.SetMeta("opennv_collision_source_sha256", sourceSha256);
        target.SetMeta("opennv_collision_object_block", source.CollisionObjectBlock);
        target.SetMeta("opennv_collision_body_block", source.BodyBlock);
        target.SetMeta("opennv_collision_shape_block", source.ShapeBlock);
        target.SetMeta("opennv_collision_target_block", source.TargetBlock);
        target.SetMeta("opennv_collision_target_name", source.TargetName);
        target.SetMeta("opennv_collision_body_type", source.BodyType);
        target.SetMeta("opennv_collision_transform_policy", source.ShapeTransformPolicy);
        target.SetMeta(
            "opennv_collision_source_body_translation_havok_units",
            source.SourceBodyTranslationHavokUnits);
        target.SetMeta("opennv_collision_source_body_rotation", source.SourceBodyRotation);
        target.SetMeta("opennv_collision_mass", source.Mass);
        target.SetMeta("opennv_collision_friction", source.Friction);
        target.SetMeta("opennv_collision_restitution", source.Restitution);
        target.SetMeta("opennv_collision_linear_damping", source.LinearDamping);
        target.SetMeta("opennv_collision_angular_damping", source.AngularDamping);
        target.SetMeta("opennv_collision_motion_system", source.MotionSystem);
        target.SetMeta("opennv_collision_quality_type", source.QualityType);
        target.SetMeta("opennv_collision_havok_layer", source.Layer);
        target.SetMeta("opennv_collision_flags_and_part_number", source.FlagsAndPartNumber);
        target.SetMeta("opennv_collision_unknown_short", source.UnknownShort);
        target.SetMeta("opennv_collision_material", source.Material);
        target.SetMeta("opennv_collision_radius_havok_units", source.RadiusHavokUnits);
        target.SetMeta("opennv_collision_radius_game_units", source.RadiusGameUnits);
        target.SetMeta("opennv_collision_point_count", source.PointsGodotGameUnits.Count);
    }

    private static Node3D FindUniqueArticulationWrapper(
        Node3D root,
        string name,
        Transform3D expectedTransform,
        string role)
    {
        var matches = NodeTraversal.Descendants<Node3D>(root)
            .Where(node => node.Name == name)
            .ToArray();
        if (matches.Length != 1 || !TransformsMatch(matches[0].Transform, expectedTransform))
            throw new InvalidOperationException(
                $"Door articulation {role} wrapper is missing, duplicated, or transformed: {name}");
        return matches[0];
    }

    private static IReadOnlyList<MeshInstance3D> ValidateWrapperMeshes(
        Node3D wrapper,
        IReadOnlyList<string> expectedNames,
        string role)
    {
        var children = wrapper.GetChildren();
        var meshes = children.OfType<MeshInstance3D>().ToArray();
        var names = meshes.Select(mesh => mesh.Name.ToString())
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        if (children.Count != meshes.Length ||
            meshes.Any(mesh => mesh.Mesh is null) ||
            !names.SequenceEqual(expectedNames, StringComparer.Ordinal))
            throw new InvalidOperationException(
                $"Door articulation {role} descendants do not match the source contract: {wrapper.Name}");
        return meshes;
    }

    private static void ValidateSinglePieceDoor(
        string referenceFormId,
        Node3D visual,
        Node3D? verifiedAuthoredCollision,
        bool buildCollision)
    {
        if (NodeTraversal.Descendants<MeshInstance3D>(visual).Count() != 1)
            throw new InvalidOperationException(
                $"Non-controller door is not a single-piece visual/collision pair: {referenceFormId}");
        if (!buildCollision)
            return;

        var generatedBodies = NodeTraversal.Descendants<StaticBody3D>(visual).ToArray();
        var generatedShapes = NodeTraversal.Descendants<CollisionShape3D>(visual).ToArray();
        var hasGeneratedCollision =
            generatedBodies.Length == 1 &&
            generatedShapes.Length == 1 &&
            generatedShapes[0].GetParent() == generatedBodies[0] &&
            generatedShapes[0].Shape is not null;

        var authoredMeshes = verifiedAuthoredCollision is null
            ? Array.Empty<MeshInstance3D>()
            : NodeTraversal.Descendants<MeshInstance3D>(verifiedAuthoredCollision).ToArray();
        var authoredBodies = verifiedAuthoredCollision is null
            ? Array.Empty<StaticBody3D>()
            : NodeTraversal.Descendants<StaticBody3D>(verifiedAuthoredCollision).ToArray();
        var authoredShapes = verifiedAuthoredCollision is null
            ? Array.Empty<CollisionShape3D>()
            : NodeTraversal.Descendants<CollisionShape3D>(verifiedAuthoredCollision).ToArray();
        var hasAuthoredCollision =
            authoredMeshes.Length == 1 &&
            authoredBodies.Length == 1 &&
            authoredShapes.Length == 1 &&
            authoredShapes[0].GetParent() == authoredBodies[0] &&
            authoredShapes[0].Shape is not null;

        if (hasGeneratedCollision == hasAuthoredCollision)
            throw new InvalidOperationException(
                $"Non-controller door is not a single-piece visual/collision pair: {referenceFormId}");
    }

    private static IReadOnlyList<string> ReadSortedUniqueStrings(
        JsonElement source,
        string owner,
        string field)
    {
        var values = source.EnumerateArray().Select(value => value.GetString()!).ToArray();
        if (values.Length == 0 ||
            values.Any(string.IsNullOrWhiteSpace) ||
            values.Distinct(StringComparer.Ordinal).Count() != values.Length ||
            !values.SequenceEqual(values.OrderBy(value => value, StringComparer.Ordinal), StringComparer.Ordinal))
            throw new InvalidOperationException($"Door articulation {field} are not sorted/unique: {owner}");
        return values;
    }

    private static IReadOnlyList<int> ReadSortedUniqueIntegers(
        JsonElement source,
        string owner,
        string field)
    {
        var values = source.EnumerateArray().Select(value => value.GetInt32()).ToArray();
        if (values.Length == 0 ||
            values.Any(value => value < 0) ||
            values.Distinct().Count() != values.Length ||
            !values.SequenceEqual(values.Order()))
            throw new InvalidOperationException($"Door articulation {field} are not sorted/unique: {owner}");
        return values;
    }

    private static string RequiredString(JsonElement source, string property, string owner)
    {
        var value = source.GetProperty(property).GetString();
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException(
                $"Door articulation {property} is empty: {owner}");
        return value;
    }

    private static bool TransformsMatch(Transform3D left, Transform3D right) =>
        left.Origin.DistanceTo(right.Origin) <= TransformTolerance &&
        left.Basis.X.DistanceTo(right.Basis.X) <= TransformTolerance &&
        left.Basis.Y.DistanceTo(right.Basis.Y) <= TransformTolerance &&
        left.Basis.Z.DistanceTo(right.Basis.Z) <= TransformTolerance;

    private static bool IsSha256(string value) =>
        value.Length == 64 && value.All(Uri.IsHexDigit);

    private static string CanonicalSha256(JsonElement source)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
            WriteCanonicalJson(writer, source, excludeCanonicalSha256: true);
        return Convert.ToHexString(SHA256.HashData(stream.ToArray())).ToLowerInvariant();
    }

    private static void WriteCanonicalJson(
        Utf8JsonWriter writer,
        JsonElement source,
        bool excludeCanonicalSha256 = false)
    {
        switch (source.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in source.EnumerateObject()
                             .Where(property => !excludeCanonicalSha256 ||
                                 property.Name != "canonicalSha256")
                             .OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonicalJson(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var value in source.EnumerateArray())
                    WriteCanonicalJson(writer, value);
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteRawValue(source.GetRawText());
                break;
            case JsonValueKind.Number:
                writer.WriteRawValue(source.GetRawText());
                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            case JsonValueKind.Null:
                writer.WriteNullValue();
                break;
            default:
                throw new InvalidOperationException("Unsupported door articulation JSON value.");
        }
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
        foreach (var mesh in NodeTraversal.Descendants<MeshInstance3D>(root))
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

    private static void BuildLandscapeCollision(
        Node3D root,
        IReadOnlyList<MeshInstance3D> landscapeMeshes,
        uint collisionLayer)
    {
        var vertices = new List<Vector3>();
        var triangleCount = 0;
        foreach (var mesh in landscapeMeshes)
        {
            if (mesh.Mesh is null)
                throw new InvalidOperationException("Owned LAND presentation mesh is missing.");
            var faces = mesh.Mesh.GetFaces();
            if (faces.Length == 0 || faces.Length % 3 != 0)
                throw new InvalidOperationException("Owned LAND collision faces are malformed.");
            var rootLocal = root.GlobalTransform.AffineInverse() * mesh.GlobalTransform;
            foreach (var face in faces)
                vertices.Add(rootLocal * face);
            triangleCount += faces.Length / 3;
        }
        var surface = new SurfaceTool();
        surface.Begin(Mesh.PrimitiveType.Triangles);
        foreach (var vertex in vertices)
            surface.AddVertex(vertex);
        surface.Index();
        var collisionMesh = surface.Commit() ??
            throw new InvalidOperationException("Could not merge owned LAND collision mesh.");
        var shape = collisionMesh.CreateTrimeshShape() ??
            throw new InvalidOperationException("Could not construct owned LAND collision shape.");
        if (shape is not ConcavePolygonShape3D concave)
            throw new InvalidOperationException(
                "Owned LAND collision did not produce a concave triangle shape.");
        concave.BackfaceCollision = true;
        var body = new StaticBody3D
        {
            Name = "LAND_ACTIVE_SET_COLLISION",
            CollisionLayer = collisionLayer,
            CollisionMask = 0,
        };
        body.SetMeta("opennv_land_meshes", landscapeMeshes.Count);
        body.SetMeta("opennv_land_triangles", triangleCount);
        body.SetMeta("opennv_collision_role", "owned-land-merged-triangles");
        body.AddChild(new CollisionShape3D
        {
            Name = "LAND_ACTIVE_SET_COLLISION_SHAPE",
            Shape = shape,
        });
        root.AddChild(body);
    }

    private static void CreateDoubleSidedTrimeshCollision(
        MeshInstance3D mesh,
        uint collisionLayer)
    {
        mesh.CreateTrimeshCollision();
        var bodies = NodeTraversal.Descendants<StaticBody3D>(mesh).ToArray();
        var shapes = NodeTraversal.Descendants<CollisionShape3D>(mesh)
            .Select(value => value.Shape)
            .OfType<ConcavePolygonShape3D>()
            .ToArray();
        if (bodies.Length == 0 || shapes.Length == 0)
            throw new InvalidOperationException(
                $"Could not construct double-sided trimesh collision: {mesh.Name}");
        foreach (var body in bodies)
            body.CollisionLayer = collisionLayer;
        foreach (var shape in shapes)
            shape.BackfaceCollision = true;
    }

    private static void StartSourceControllerPlayback(Node3D instance, string[] sequences)
    {
        var players = NodeTraversal.SelfAndDescendants<AnimationPlayer>(instance).ToArray();
        if (players.Length != 1 || sequences.Length == 0 ||
            sequences.Any(sequence => !players[0].HasAnimation(sequence)))
            throw new InvalidOperationException(
                "Owned source controller animations are absent or ambiguous.");
        for (var index = 0; index < sequences.Length; index++)
        {
            var sequence = sequences[index];
            var animation = players[0].GetAnimation(sequence) ??
                throw new InvalidOperationException($"Owned source animation is missing: {sequence}");
            animation.LoopMode = Animation.LoopModeEnum.Linear;
            if (index == 0)
            {
                players[0].Play(sequence);
                continue;
            }
            var libraryName = new StringName($"opennv_source_controller_{index}");
            var library = new AnimationLibrary();
            library.AddAnimation(sequence, animation);
            var player = new AnimationPlayer
            {
                Name = $"SOURCE_CONTROLLER_PLAYER_{index}",
                RootNode = players[0].RootNode,
            };
            players[0].GetParent().AddChild(player);
            player.AddAnimationLibrary(libraryName, library);
            player.Play(new StringName($"{libraryName}/{sequence}"));
        }
    }

    private static int BuildWalkableRoadCollision(
        Node3D placement,
        Node3D collisionRoot,
        uint collisionLayer)
    {
        var surface = new SurfaceTool();
        surface.Begin(Mesh.PrimitiveType.Triangles);
        var triangles = 0;
        foreach (var collisionMesh in NodeTraversal.Descendants<MeshInstance3D>(collisionRoot))
        {
            collisionMesh.Visible = false;
            if (collisionMesh.Mesh is null)
                throw new InvalidOperationException("Owned road collision mesh is missing.");
            var faces = collisionMesh.Mesh.GetFaces();
            if (faces.Length == 0 || faces.Length % 3 != 0)
                throw new InvalidOperationException("Owned road collision faces are malformed.");
            var placementLocal = placement.GlobalTransform.AffineInverse() *
                collisionMesh.GlobalTransform;
            for (var index = 0; index < faces.Length; index += 3)
            {
                var first = placementLocal * faces[index];
                var second = placementLocal * faces[index + 1];
                var third = placementLocal * faces[index + 2];
                var normal = (second - first).Cross(third - first).Normalized();
                var worldNormal = (placement.GlobalBasis * normal).Normalized();
                if (worldNormal.Dot(Vector3.Up) < 0.5f)
                    continue;
                surface.AddVertex(first);
                surface.AddVertex(second);
                surface.AddVertex(third);
                triangles++;
            }
        }
        if (triangles == 0)
            throw new InvalidOperationException("Owned road has no walkable collision faces.");
        var mesh = surface.Commit() ??
            throw new InvalidOperationException("Could not combine owned road collision.");
        var shape = mesh.CreateTrimeshShape() ??
            throw new InvalidOperationException("Could not construct owned road collision shape.");
        if (shape is ConcavePolygonShape3D concave)
            concave.BackfaceCollision = true;
        var body = new StaticBody3D
        {
            Name = "WALKABLE_ROAD_COLLISION",
            CollisionLayer = collisionLayer,
            CollisionMask = 0,
        };
        body.SetMeta("opennv_collision_role", "owned-wasteland-road-walkable-deck");
        body.SetMeta("opennv_collision_triangles", triangles);
        body.AddChild(new CollisionShape3D
        {
            Name = "WALKABLE_ROAD_COLLISION_SHAPE",
            Shape = shape,
        });
        placement.AddChild(body);
        return 1;
    }

    private static void SetRenderLayer(Node root, uint layer)
    {
        foreach (var mesh in NodeTraversal.Descendants<MeshInstance3D>(root))
            mesh.Layers = layer;
    }

    private static void SetShadowCasting(
        Node root,
        GeometryInstance3D.ShadowCastingSetting setting)
    {
        foreach (var mesh in NodeTraversal.Descendants<MeshInstance3D>(root))
            mesh.CastShadow = setting;
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
        LightingContract Lighting,
        RetailExteriorEnvironment? ExteriorEnvironment)
    {
        internal Vector3 GameToWorld(Vector3 position) => Root.ToGlobal(new Vector3(
            position.X - OriginGameUnits.X,
            position.Z - OriginGameUnits.Z,
            -(position.Y - OriginGameUnits.Y)));

        internal Vector3 WorldToGame(Vector3 position)
        {
            var local = Root.ToLocal(position);
            return new Vector3(
                local.X + OriginGameUnits.X,
                -local.Z + OriginGameUnits.Y,
                local.Y + OriginGameUnits.Z);
        }
    }

    internal readonly record struct SourceReference(
        string FormId,
        string BaseFormId,
        string BaseRecordType,
        string BaseEditorId,
        string AssetId,
        string SourceCellFormId,
        Vector3 PositionGodotUnits,
        bool InitiallyDisabled,
        string? EnableParentFormId,
        bool EnableParentOpposite);

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
        string? WeaponDisplayName,
        string AmmoFormId,
        string AmmoEditorId,
        string? AmmoDisplayName,
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

    private sealed record DoorArticulationContract(
        string CanonicalSha256,
        DoorArticulationTarget Target,
        Transform3D ClosedLocalTransform,
        DoorArticulationSequence Open,
        DoorArticulationSequence Close);

    private sealed record DoorArticulationTarget(
        string TargetId,
        string VisualNodeName,
        string CollisionNodeName,
        IReadOnlyList<string> VisualDescendantNodeNames,
        IReadOnlyList<string> CollisionDescendantNodeNames);

    private readonly record struct DoorArticulationSequence(
        Transform3D Initial,
        Transform3D Terminal,
        float DurationSeconds)
    {
        internal DoorInstance.ArticulationSequence ToRuntimeSequence() => new(
            Initial,
            Terminal,
            DurationSeconds);
    }

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
