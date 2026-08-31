using System.Text.Json;
using Godot;
using OpenNV.Runtime.Campaigns.Fallout1;

namespace OpenNV.Runtime.Campaigns.Fallout2.Temple;

internal static class Fo2TempleBuildProof
{
    internal static async Task Run(
        Node host,
        Fo2TempleSceneCoverage coverage,
        string reportPath,
        Fo2TempleTransitionCatalog? transitionCatalog = null)
    {
        try
        {
            var output = Path.GetFullPath(reportPath);
            if (File.Exists(output) || Directory.Exists(output))
                throw new InvalidOperationException(
                    $"Refusing to overwrite Fallout 2 Temple build proof: {output}");
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.PhysicsFrame);
            var topology = coverage.Topology;
            var space = ((Node3D)host).GetWorld3D().DirectSpaceState;
            var floorHit = Cast(space, topology.FloorProbeFrom, topology.FloorProbeTo);
            var wallHit = Cast(space, topology.WallProbeFrom, topology.WallProbeTo);
            if (!floorHit.Hit || floorHit.ColliderPath != topology.FloorCollisionPath ||
                !wallHit.Hit || wallHit.ColliderPath != topology.WallProbeCollisionPath)
                throw new InvalidOperationException(
                    "Fallout 2 Temple source floor/wall physics proof missed its exact collider: " +
                    $"floor={floorHit.Hit}/{floorHit.ColliderPath}/expected={topology.FloorCollisionPath}; " +
                    $"wall={wallHit.Hit}/{wallHit.ColliderPath}/expected={topology.WallProbeCollisionPath}.");
            var movement = topology.Movement;
            var rejectedNonAdjacentStep = !movement.TryStep(movement.CurrentTile);
            if (!rejectedNonAdjacentStep || movement.CurrentTile != movement.EntryTile ||
                movement.CompletedSteps != 0)
                throw new InvalidOperationException(
                    "Fallout 2 Temple movement accepted a non-adjacent source step.");
            var transitionRuntime = transitionCatalog is null
                ? null
                : Fo2TempleTransitionRuntime.Build(
                    (Node3D)host,
                    transitionCatalog,
                    movement);
            bool? rejectedNonExitTile = transitionRuntime is null
                ? null
                : !transitionRuntime.TryApplyAtCurrentTile() && transitionRuntime.Applied is null;
            if (rejectedNonExitTile == false)
                throw new InvalidOperationException(
                    "Fallout 2 Temple transition applied away from a source exit-grid tile.");
            var selectedExit = transitionRuntime?.ReachableExits
                .OrderBy(row => row.Serial)
                .First();
            var movementPath = selectedExit is null
                ? movement.BuildFarthestProofPath()
                : movement.BuildPathTo(selectedExit.Tile);
            var movementFloorContacts = 0;
            foreach (var tile in movementPath)
            {
                if (tile != movement.EntryTile && !movement.TryStep(tile))
                    throw new InvalidOperationException(
                        $"Fallout 2 Temple movement rejected source path tile {tile}.");
                if (!movement.WorldPosition.IsEqualApprox(Fo1HexMath.Center(tile)))
                    throw new InvalidOperationException(
                        $"Fallout 2 Temple movement world position drifted at tile {tile}.");
                var contact = Cast(
                    space,
                    movement.WorldPosition + Vector3.Up * 2.0f,
                    movement.WorldPosition - Vector3.Up);
                if (!contact.Hit || contact.ColliderPath != topology.FloorCollisionPath)
                    throw new InvalidOperationException(
                        $"Fallout 2 Temple movement lost source floor support at tile {tile}.");
                movementFloorContacts++;
            }
            if (movement.CompletedSteps != movementPath.Count - 1 ||
                movement.CurrentTile != movementPath[^1])
                throw new InvalidOperationException(
                    "Fallout 2 Temple movement completion state drifted.");
            int? rejectedAdjacentTile = null;
            bool? rejectedBlockedAdjacentStep = null;
            if (transitionRuntime is null)
            {
                rejectedAdjacentTile = movement.RejectedAdjacentTile();
                var completedBeforeRejection = movement.CompletedSteps;
                var currentBeforeRejection = movement.CurrentTile;
                rejectedBlockedAdjacentStep = rejectedAdjacentTile >= 0 &&
                    !movement.TryStep(rejectedAdjacentTile.Value);
                if (!rejectedBlockedAdjacentStep.Value ||
                    movement.CompletedSteps != completedBeforeRejection ||
                    movement.CurrentTile != currentBeforeRejection)
                    throw new InvalidOperationException(
                        "Fallout 2 Temple movement crossed its source-walkable component boundary.");
            }
            Fo2TempleAppliedTransition? appliedTransition = null;
            if (transitionRuntime is not null)
            {
                if (!transitionRuntime.TryApplyAtCurrentTile() || transitionRuntime.Applied is null)
                    throw new InvalidOperationException(
                        "Fallout 2 Temple source exit-grid transition was not applied.");
                appliedTransition = transitionRuntime.Applied;
            }
            object? transitionReport = transitionCatalog is null || appliedTransition is null
                ? null
                : new
                {
                    schema = Fo2TempleTransitionRuntime.Schema,
                    manifest = transitionCatalog.ManifestPath,
                    manifestSha256 = transitionCatalog.ManifestSha256,
                    headerMapProgram = new
                    {
                        program = transitionCatalog.HeaderProgram.Program,
                        logicalPath = transitionCatalog.HeaderProgram.LogicalPath,
                        sha256 = transitionCatalog.HeaderProgram.Sha256,
                        executionImplemented = false,
                    },
                    liveMapScriptRecords = transitionCatalog.LiveScriptRecords.Count,
                    liveMapScriptRecordsSha256 = transitionCatalog.LiveScriptRecordsSha256,
                    doorSourceObjects = 0,
                    doorRuntimeImplemented = false,
                    exitGridRecords = transitionCatalog.Exits.Count,
                    reachableEntryComponentExitGrids = transitionRuntime!.ReachableExits.Count,
                    verifiedDestinationMaps = transitionCatalog.DestinationMaps.Count,
                    verifiedResources = transitionCatalog.VerifiedResources,
                    selectedExitSerial = appliedTransition.ExitSerial,
                    sourceMapIndex = appliedTransition.SourceMapIndex,
                    sourceMapSha256 = appliedTransition.SourceMapSha256,
                    sourceTile = appliedTransition.SourceTile,
                    targetMapIndex = appliedTransition.TargetMapIndex,
                    targetMapSha256 = appliedTransition.TargetMapSha256,
                    targetMapName = appliedTransition.TargetMapName,
                    targetTile = appliedTransition.TargetTile,
                    targetElevation = appliedTransition.TargetElevation,
                    targetRotation = appliedTransition.TargetRotation,
                    destinationMapLoaded = false,
                    rejectedNonExitTile,
                    nonvisualStateApplied = true,
                };
            var report = new
            {
                schema = transitionRuntime is null
                    ? "opennv-fo2-temple-runtime-build-proof/v3"
                    : "opennv-fo2-temple-runtime-build-proof/v4",
                status = transitionRuntime is null
                    ? "pass-source-entry-component-movement-and-physics-built-headless-not-rendered"
                    : "pass-source-exit-grid-transition-applied-headless-not-rendered",
                cacheManifest = coverage.ManifestPath,
                cacheManifestSha256 = coverage.ManifestSha256,
                sourceManifest = coverage.SourceManifestPath,
                sourceManifestSha256 = coverage.SourceManifestSha256,
                sourceProfileId = coverage.SourceProfileId,
                map = new
                {
                    index = Fo2TemplePresentationCatalog.MapIndex,
                    name = "ARTEMPLE.MAP",
                    sha256 = coverage.MapSha256,
                    entryTile = coverage.EntryTile,
                    entryElevation = coverage.EntryElevation,
                    entryRotation = coverage.EntryRotation,
                    entryWorldMeters = new[]
                    {
                        coverage.EntryWorldMeters.X,
                        coverage.EntryWorldMeters.Y,
                        coverage.EntryWorldMeters.Z,
                    },
                },
                verifiedArtifacts = coverage.VerifiedArtifacts,
                verifiedResources = coverage.VerifiedResources,
                tileBindings = coverage.TileBindings,
                objectArtifactBindings = coverage.ObjectArtifactBindings,
                constructedFloorPatches = coverage.ConstructedFloorPatches,
                constructedRoofPatches = coverage.ConstructedRoofPatches,
                placedTopLevelObjects = coverage.PlacedTopLevelObjects,
                inventoryObjectsNotPlaced = coverage.InventoryObjectsNotPlaced,
                sourcePixelsPerMeter = coverage.SourcePixelsPerMeter,
                floorMeshInstances = coverage.FloorMeshInstances,
                objectSpriteNodes = coverage.ObjectSpriteNodes,
                topologyProfile = new
                {
                    path = topology.ProfilePath,
                    sha256 = topology.ProfileSha256,
                },
                sourceFloorSupport = new
                {
                    mode = topology.FloorSupportMode,
                    patches = topology.FloorSupportPatches,
                    supportedHexes = topology.FloorSupportHexes,
                    collisionTriangles = topology.FloorCollisionTriangles,
                    collider = topology.FloorCollisionPath,
                    proofRay = RayReport(topology.FloorProbeFrom, topology.FloorProbeTo, floorHit),
                },
                sourceWalkMask = new
                {
                    mode = topology.WalkMaskMode,
                    sha256 = topology.WalkMaskSha256,
                    blockingObjects = topology.SourceBlockingObjects,
                    blockingHexes = topology.SourceBlockingHexes,
                    multihexCentralOnlyBlockers = topology.MultihexCentralOnlyBlockers,
                    walkableHexes = topology.WalkableHexes,
                    entryReachableHexes = topology.EntryReachableHexes,
                },
                moldedWalls = new
                {
                    sourceObjects = topology.WallSourceObjects,
                    occupiedHexes = topology.WallHexes,
                    connectedComponents = topology.WallComponents,
                    largestComponentHexes = topology.LargestWallComponentHexes,
                    boundaryEdges = topology.WallBoundaryEdges,
                    shellTriangles = topology.WallTriangles,
                    meshInstances = topology.WallMeshInstances,
                    collisionMode = topology.WallCollisionMode,
                    collisionBodies = topology.WallCollisionBodies,
                    collisionHexes = topology.WallCollisionHexes,
                    proofRay = RayReport(topology.WallProbeFrom, topology.WallProbeTo, wallHit),
                },
                movement = new
                {
                    schema = Fo2TempleMovementConsumer.Schema,
                    cacheManifestSha256 = movement.CacheManifestSha256,
                    sourceManifestSha256 = movement.SourceManifestSha256,
                    sourceProfileId = movement.SourceProfileId,
                    mapSha256 = movement.MapSha256,
                    topologyProfileId = movement.TopologyProfileId,
                    topologyProfileSha256 = movement.TopologyProfileSha256,
                    walkMaskSha256 = movement.WalkMaskSha256,
                    entryTile = movement.EntryTile,
                    entryComponentHexes = movement.EntryComponentHexes,
                    deterministicTargetTile = movement.CurrentTile,
                    pathNodes = movementPath.Count,
                    completedAdjacentSteps = movement.CompletedSteps,
                    pathSha256 = Fo2TempleMovementConsumer.PathSha256(movementPath),
                    physicalFloorContacts = movementFloorContacts,
                    rejectedNonAdjacentStep,
                    rejectedBlockedAdjacentTile = rejectedAdjacentTile,
                    rejectedBlockedAdjacentStep,
                    finalWorldMeters = Vector(movement.WorldPosition),
                    consumer = "source-bound discrete hex movement cursor; no player actor or character state",
                },
                transition = transitionReport,
                presentation = "source-bound 2.5D FRM planes in a 3D Godot hex coordinate space",
                promotion = new
                {
                    transported = true,
                    decodedPresentationAssets = true,
                    runtimeManifestValidated = true,
                    runtimeSceneConstructed = true,
                    exactSourceFloorSupportConstructed = true,
                    exactSourceWalkMaskConstructed = true,
                    sourceWallHexUnionConstructed = true,
                    headlessPhysicsProved = true,
                    entryComponentMovementConsumed = true,
                    sourceExitTransitionConsumed = transitionRuntime is not null,
                    headerMapProgramExecuted = false,
                    rendered = false,
                    interactive = false,
                    characterFlow = false,
                    gameplay = false,
                    saveState = false,
                    parityReviewed = false,
                    headsetAccepted = false,
                    runtimeReady = false,
                },
                unsupported = new[]
                {
                    "multihex blocker footprints beyond each stored central source hex",
                    "retail collision and walkability parity",
                    "player actor, continuous locomotion, controls, and character state",
                    "INT bytecode execution, doors, combat, and actor behavior",
                    "Chosen One character creation and gameplay/save state",
                    "camera/lighting parity, retail differential, FPS, and OpenXR",
                },
                windowsAppControlUsed = false,
                foregroundInputInjected = false,
            };
            File.WriteAllText(
                output,
                JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) +
                    System.Environment.NewLine);
            GD.Print(
                $"OPENNV_FO2_TEMPLE_BUILD_PASS floor={coverage.ConstructedFloorPatches} " +
                $"walk={topology.WalkableHexes} walls={topology.WallHexes} " +
                $"steps={movement.CompletedSteps} objects={coverage.PlacedTopLevelObjects} " +
                $"pngs={coverage.VerifiedArtifacts}");
            host.GetTree().Quit(0);
        }
        catch (Exception exception)
        {
            GD.PushError($"OPENNV_FO2_TEMPLE_BUILD_FAIL {exception.Message}");
            host.GetTree().Quit(1);
        }
    }

    private static RayHit Cast(
        PhysicsDirectSpaceState3D space,
        Vector3 from,
        Vector3 to)
    {
        var hit = space.IntersectRay(PhysicsRayQueryParameters3D.Create(from, to));
        if (hit.Count == 0)
            return new RayHit(false, "", Vector3.Zero, Vector3.Zero);
        var collider = hit["collider"].AsGodotObject() as Node;
        return new RayHit(
            true,
            collider?.GetPath().ToString() ?? "unknown",
            hit["position"].AsVector3(),
            hit["normal"].AsVector3());
    }

    private static object RayReport(Vector3 from, Vector3 to, RayHit hit) => new
    {
        from = Vector(from),
        to = Vector(to),
        hit = hit.Hit,
        collider = hit.ColliderPath,
        position = Vector(hit.Position),
        normal = Vector(hit.Normal),
    };

    private static float[] Vector(Vector3 value) => [value.X, value.Y, value.Z];

    private readonly record struct RayHit(
        bool Hit,
        string ColliderPath,
        Vector3 Position,
        Vector3 Normal);
}
