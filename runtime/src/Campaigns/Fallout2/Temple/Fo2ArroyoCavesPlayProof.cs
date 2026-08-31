using System.Security.Cryptography;
using System.Text.Json;
using Godot;

using OpenNV.Runtime.SceneGraph;
using OpenNV.Runtime.Campaigns.Fallout1;

namespace OpenNV.Runtime.Campaigns.Fallout2.Temple;

internal static class Fo2ArroyoCavesPlayProof
{
    private const int GroundingFrames = 120;
    private const int SettleFrames = 4;
    private const int ExpectedWidth = 1280;
    private const int ExpectedHeight = 720;
    private const int ExpectedClassicHudSourceAssets = 15;
    private const string CritterLogicalPathPrefix = "art\\critters\\";
    private const string DoorLogicalPathFragment = "\\acavedr";
    private const string SourceMapLightAnchorPrefix = "SOURCE_MAP_LIGHT_";
    private const string SourceMapLightNode = "SOURCE_MAP_LIGHT_FIELD";

    internal static async Task Run(
        Node3D host,
        Fo2ArroyoCavesPresentationCatalog catalog,
        Fo2ArroyoCavesSceneCoverage scene,
        Fo2ArroyoCavesPlayerRuntimeCoverage runtime,
        string proofRoot)
    {
        var pressed = false;
        try
        {
            if (DisplayServer.GetName() == "headless")
                throw new InvalidOperationException(
                    "Fallout 2 Arroyo player proof requires a rendering display driver.");
            DisplayServer.WindowSetTitle(
                "OpenNV • Fallout 2 • Arroyo Caves Gameplay • bounded proof");
            var output = Path.GetFullPath(proofRoot);
            if (Directory.Exists(output) || File.Exists(output))
                throw new InvalidOperationException(
                    $"Refusing to overwrite Fallout 2 Arroyo player proof: {output}");
            Directory.CreateDirectory(output);
            var profile = runtime.Profile;
            var player = runtime.Player;
            var presentation = player.Presentation;
            for (var frame = 0; frame < GroundingFrames && !player.IsOnFloor(); frame++)
                await host.ToSignal(host.GetTree(), SceneTree.SignalName.PhysicsFrame);
            if (!player.IsOnFloor() || player.CurrentTile != catalog.ArrivalTile ||
                !player.Position.IsEqualApprox(player.SpawnWorldMeters))
                throw new InvalidOperationException(
                    "Fallout 2 Arroyo player did not settle on the exact arrival floor.");

            var space = host.GetWorld3D().DirectSpaceState;
            var startFloor = CastFloor(space, player, player.Position);
            if (!startFloor.Hit || startFloor.ColliderPath != runtime.FloorCollisionPath)
                throw new InvalidOperationException(
                    "Fallout 2 Arroyo arrival player missed its source floor support.");
            var camera = host.GetViewport().GetCamera3D() ??
                throw new InvalidOperationException(
                    "Fallout 2 Arroyo player proof has no active gameplay camera.");
            var worldAudit = AuditWorld3D(host, catalog, scene, runtime, camera);
            var sourceClosure = BuildSourceClosure(catalog, scene, runtime, worldAudit);
            if (worldAudit.VisibleSpriteCards != 0 ||
                worldAudit.InFrustumSpriteCards != 0 ||
                worldAudit.ClosedReliefSourceObjects !=
                    scene.Molded3D.ClosedReliefWorldObjects ||
                worldAudit.SourceTorchAssemblies !=
                    scene.Molded3D.VisibleSourceTorchProps ||
                worldAudit.SourceTorchPostLayeredAssemblies !=
                    scene.Molded3D.SourceTorchPostLayeredAssemblies ||
                worldAudit.SourceTorchFrmPixelProps != worldAudit.SourceTorchAssemblies ||
                worldAudit.SourceMapLightRecords != scene.Molded3D.SourceMapLightRecords ||
                worldAudit.SourceMapLights != scene.Molded3D.SourceMapLights ||
                worldAudit.SourceTorchMotivatedMapLights !=
                    scene.Molded3D.SourceTorchMotivatedMapLights ||
                worldAudit.InFrustumTorchAssembliesWithMissingSourcePixels != 0 ||
                worldAudit.InvalidSourceMapLights != 0 ||
                sourceClosure.UnaccountedSourceObjects != 0 ||
                !sourceClosure.FirstBeatRuntimeClosurePassed)
                throw new InvalidOperationException(
                    "Fallout 2 Arroyo zero-card or source-closure admission failed: " +
                    $"cards={worldAudit.VisibleSpriteCards}/" +
                    $"{worldAudit.InFrustumSpriteCards}, relief=" +
                    $"{worldAudit.ClosedReliefSourceObjects}/" +
                    $"{scene.Molded3D.ClosedReliefWorldObjects}, torches=" +
                    $"{worldAudit.SourceTorchAssemblies}/" +
                    $"{worldAudit.SourceTorchFrmPixelProps}/" +
                    $"mapLights={worldAudit.SourceMapLightRecords}/" +
                    $"{worldAudit.SourceMapLights}, invalid=" +
                    $"{worldAudit.InvalidSourceMapLights}, missing=" +
                    $"{worldAudit.InFrustumTorchAssembliesWithMissingSourcePixels}, " +
                    $"unaccounted={sourceClosure.UnaccountedSourceObjects}, " +
                    $"admittedScripts=" +
                    $"{sourceClosure.AdmittedScriptBackedSourceObjects}, " +
                    $"admittedExitMarkers=" +
                    $"{sourceClosure.AdmittedExitMarkerSourceObjects}, " +
                    $"hudState={runtime.Hud.FirstMovementBeatStateComplete}, " +
                    $"firstBeatClosure={sourceClosure.FirstBeatRuntimeClosurePassed}.");
            await WaitForDraws(host, SettleFrames);
            var humanoid = player.VillageHumanoid ?? throw new InvalidOperationException(
                "Fallout 2 Arroyo proof has no bound full-body humanoid.");
            var startFrame = Capture(host, output, "player-arrival-start.png");
            var startDirection = presentation.Direction;
            var startPlayerPngSha256 = presentation.CurrentFrame.PngSha256;
            var startWalkFrameAdvances = presentation.WalkFrameAdvances;
            var startWalkCycles = presentation.CompletedWalkCycles;
            var startHudBlockedSourceLight = runtime.Hud.BlockedSourceLightVisible;

            var startPosition = player.Position;
            var firstNeighborReached = false;
            FrameEvidence? firstWalkFrame = null;
            FrameEvidence? secondWalkFrame = null;
            var firstWalkClip = "";
            var secondWalkClip = "";
            var firstWalkClipSeconds = 0.0;
            var secondWalkClipSeconds = 0.0;
            float[]? firstWalkLegPose = null;
            float[]? secondWalkLegPose = null;
            Input.ParseInputEvent(Fo2ArroyoCavesInput.CreateEvent(profile.AcceptanceKey, true));
            pressed = true;
            var physicsFrames = 0;
            for (; physicsFrames < profile.AcceptanceMaximumPhysicsFrames; physicsFrames++)
            {
                await host.ToSignal(host.GetTree(), SceneTree.SignalName.PhysicsFrame);
                firstNeighborReached |=
                    player.CurrentTile == profile.AcceptanceFirstNeighborTile ||
                    player.CompletedTileTransitions > 0;
                if (firstWalkFrame is null && player.CompletedTileTransitions >= 4)
                {
                    await WaitForDraws(host, 1);
                    firstWalkFrame = Capture(
                        host,
                        output,
                        "player-forward-walk-a.png");
                    firstWalkClip = humanoid.ActiveAnimationLogicalPath;
                    firstWalkClipSeconds = humanoid.ActiveAnimationPositionSeconds;
                    firstWalkLegPose = humanoid.CaptureLegPose();
                }
                if (secondWalkFrame is null && player.CompletedTileTransitions >= 10)
                {
                    await WaitForDraws(host, 1);
                    secondWalkFrame = Capture(
                        host,
                        output,
                        "player-forward-walk-b.png");
                    secondWalkClip = humanoid.ActiveAnimationLogicalPath;
                    secondWalkClipSeconds = humanoid.ActiveAnimationPositionSeconds;
                    secondWalkLegPose = humanoid.CaptureLegPose();
                }
                if (firstNeighborReached &&
                    player.CurrentTile == profile.AcceptanceLastWalkableTile &&
                    player.LastRejectedCandidateTile == profile.AcceptanceFirstRejectedTile &&
                    player.RejectedMovementFrames >=
                        profile.AcceptanceMinimumRejectedPhysicsFrames)
                    break;
            }
            Input.ParseInputEvent(Fo2ArroyoCavesInput.CreateEvent(profile.AcceptanceKey, false));
            pressed = false;
            for (var frame = 0; frame < SettleFrames; frame++)
                await host.ToSignal(host.GetTree(), SceneTree.SignalName.PhysicsFrame);

            var endFloor = CastFloor(space, player, player.Position);
            await WaitForDraws(host, SettleFrames);
            var endFrame = Capture(host, output, "player-source-boundary-stop.png");
            var gameplayCameraSize = camera.Size;
            camera.Size = MathF.Max(2.4f, gameplayCameraSize * 0.18f);
            await WaitForDraws(host, SettleFrames);
            var closeFinalFrame = Capture(host, output, "player-close-final.png");
            camera.Size = gameplayCameraSize;
            var expectedEndDirection = Fo2ArroyoCavesPlayerBody.DirectionForMovement(
                player.CurrentTile,
                Vector3.Back);
            var expectedTransitions =
                (profile.AcceptanceLastWalkableTile - catalog.ArrivalTile) /
                Fo1HexMath.Width;
            var legPoseDistance = firstWalkLegPose is not null && secondWalkLegPose is not null
                ? Fo2HumanoidVisual.LegPoseDistance(firstWalkLegPose, secondWalkLegPose)
                : 0.0f;
            var passed = firstNeighborReached &&
                physicsFrames < profile.AcceptanceMaximumPhysicsFrames &&
                player.CurrentTile == profile.AcceptanceLastWalkableTile &&
                player.CompletedTileTransitions == expectedTransitions &&
                player.RejectedMovementFrames >=
                    profile.AcceptanceMinimumRejectedPhysicsFrames &&
                player.LastRejectedCandidateTile == profile.AcceptanceFirstRejectedTile &&
                !player.CanOccupy(profile.AcceptanceFirstRejectedTile) &&
                player.HorizontalDistanceFromSpawn >= expectedTransitions - 1.0f &&
                player.IsOnFloor() &&
                MathF.Abs(player.Position.Y - player.SpawnWorldMeters.Y) <=
                    profile.AcceptanceGroundHeightToleranceMeters &&
                endFloor.Hit &&
                endFloor.ColliderPath == runtime.FloorCollisionPath &&
                !presentation.VisibleInWorld &&
                presentation.UsesOwnedFrmRelief &&
                !presentation.UsesOwnedDonor &&
                presentation.MeshInstances == 2 &&
                presentation.MoldedFaceTriangles > 0 &&
                presentation.MoldedSideTriangles > 0 &&
                presentation.Texture is not null &&
                humanoid is { Visible: true } &&
                humanoid.UsesOwnedDonor &&
                humanoid.MeshInstances > 0 &&
                humanoid.AuthoredSurfaces > 0 &&
                humanoid.LitMaterials > 0 &&
                firstWalkFrame is not null &&
                secondWalkFrame is not null &&
                firstWalkClip.Contains("forward", StringComparison.OrdinalIgnoreCase) &&
                secondWalkClip.Contains("forward", StringComparison.OrdinalIgnoreCase) &&
                firstWalkClipSeconds > 0.0 &&
                secondWalkClipSeconds > 0.0 &&
                legPoseDistance >= 0.01f &&
                firstWalkFrame.Sha256 != secondWalkFrame.Sha256 &&
                startDirection == catalog.ArrivalRotation &&
                presentation.Direction == expectedEndDirection &&
                presentation.WalkFrameAdvances > startWalkFrameAdvances &&
                presentation.CompletedWalkCycles > startWalkCycles &&
                runtime.Hud.VisibleInViewport &&
                !presentation.IsWalking &&
                presentation.AnimationCode == "AA" &&
                presentation.AnimationFrame == Fo2ArroyoPlayerPresentationCatalog.IdleFrame &&
                presentation.CurrentFrame.LogicalPath ==
                    Fo2ArroyoPlayerPresentationCatalog.ExpectedLogicalPath &&
                runtime.Hud.OwnedFallout2ClassicInterface &&
                runtime.Hud.SourcePixelLayout &&
                !runtime.Hud.RetailBehaviorParity &&
                runtime.Hud.OwnedSourceAssetCount == ExpectedClassicHudSourceAssets &&
                !startHudBlockedSourceLight &&
                runtime.Hud.BlockedSourceLightVisible &&
                startPlayerPngSha256 != presentation.CurrentFrame.PngSha256 &&
                startFrame.Sha256 != endFrame.Sha256;
            var report = new
            {
                schema = "opennv-fo2-arroyo-player-runtime-proof/v1",
                status = passed
                    ? "pass-input-driven-source-gated-player-runtime-owned-hmwarr-bound-3d-donor-no-save"
                    : "fail-player-runtime-gate",
                campaign = "Fallout2",
                slice = "ArroyoCaves",
                renderer = RenderingServer.GetCurrentRenderingMethod(),
                displayDriver = DisplayServer.GetName(),
                source = new
                {
                    profileId = scene.SourceProfileId,
                    cacheManifestSha256 = scene.ManifestSha256,
                    sourceManifestSha256 = scene.SourceManifestSha256,
                    mapSha256 = scene.MapSha256,
                    transitionManifestSha256 = scene.SourceTransitionSha256,
                    walkMaskSha256 = scene.WalkMaskSha256,
                },
                runtimeProfile = new
                {
                    resource = profile.ResourcePath,
                    sha256 = profile.Sha256,
                    id = profile.Id,
                    floorCollisionMode = profile.FloorCollisionMode,
                    blockedMovementMode = profile.BlockedMovementMode,
                },
                playerPresentation = new
                {
                    cache = runtime.PlayerPresentation.ManifestPath,
                    cacheManifestSha256 = runtime.PlayerPresentation.ManifestSha256,
                    recipeSha256 = runtime.PlayerPresentation.RecipeSha256,
                    critterListSha256 = runtime.PlayerPresentation.CritterListSha256,
                    fid = Fo2ArroyoPlayerPresentationCatalog.ExpectedFid,
                    logicalPath = Fo2ArroyoPlayerPresentationCatalog.ExpectedLogicalPath,
                    sourceSha256 = runtime.PlayerPresentation.SourceSha256,
                    prototypePid = runtime.SelectedPlayerPresentation.PrototypePid,
                    prototypeLogicalPath =
                        runtime.SelectedPlayerPresentation.PrototypeLogicalPath,
                    prototypeSha256 = runtime.SelectedPlayerPresentation.PrototypeSha256,
                    walkLogicalPath = runtime.SelectedPlayerPresentation.Walk.LogicalPath,
                    walkSourceSha256 = runtime.SelectedPlayerPresentation.Walk.SourceSha256,
                    walkFps = runtime.SelectedPlayerPresentation.Walk.FramesPerSecond,
                    walkFramesPerDirection = runtime.SelectedPlayerPresentation.Walk
                        .Directions.Values.First().Count,
                    sourceDirections = runtime.PlayerPresentation.Directions.Count,
                    sourceFramesPerDirection = runtime.PlayerPresentation.FramesPerDirection,
                    admittedFrame = Fo2ArroyoPlayerPresentationCatalog.IdleFrame,
                    walkFrameAdvances = presentation.WalkFrameAdvances -
                        startWalkFrameAdvances,
                    completedWalkCycles = presentation.CompletedWalkCycles - startWalkCycles,
                    animationPlayback = true,
                    idleResumedAtEnd = !presentation.IsWalking &&
                        presentation.AnimationCode == "AA" && presentation.AnimationFrame == 0,
                    billboard = profile.PlayerBillboardMode,
                    directionMode = profile.PlayerDirectionMode,
                    startDirection,
                    startPngSha256 = startPlayerPngSha256,
                    endDirection = presentation.Direction,
                    endPngSha256 = presentation.CurrentFrame.PngSha256,
                    visible = humanoid?.Visible == true,
                    geometryMode = humanoid?.PresentationMode ??
                        Fo2HumanoidVisual.UnavailableMode,
                    sourceStateGeometryMode = presentation.GeometryMode,
                    presentationLabel = humanoid?.PresentationLabel ??
                        presentation.PresentationLabel,
                    usesOwnedDonor = humanoid?.UsesOwnedDonor == true,
                    roleDonorOutfitFormId =
                        runtime.PlayerPresentation.Live3DPresentationOutfitFormId,
                    loadedDonorOutfitFormId =
                        humanoid?.GetMeta("donor_outfit_form_id").AsString() ?? "",
                    presentation.UsesOwnedFrmRelief,
                    sourceStateReliefVisible = presentation.VisibleInWorld,
                    meshInstances = humanoid?.MeshInstances ?? 0,
                    authoredSurfaces = humanoid?.AuthoredSurfaces ?? 0,
                    litMaterials = humanoid?.LitMaterials ?? 0,
                    moldedFaceTriangles = presentation.MoldedFaceTriangles,
                    moldedSideTriangles = presentation.MoldedSideTriangles,
                    reliefIslands = presentation.ReliefIslands,
                    visibleSprite3dCards = 0,
                    visibleAnimation = new
                    {
                        firstWalkClip,
                        firstWalkClipSeconds,
                        secondWalkClip,
                        secondWalkClipSeconds,
                        legPoseDistance,
                        legPoseGateRms = 0.01f,
                        endClip = humanoid!.ActiveAnimationLogicalPath,
                        endClipSeconds = humanoid.ActiveAnimationPositionSeconds,
                    },
                    skinJoin = new
                    {
                        mode = humanoid.GetMeta("skin_join_mode").AsString(),
                        targetRole = humanoid.GetMeta("skin_join_target_role").AsString(),
                        materials = humanoid.GetMeta("skin_join_materials").AsInt32(),
                        target = Vector(humanoid.GetMeta("skin_join_target_color").AsVector3()),
                        neckSource = Vector(
                            humanoid.GetMeta("skin_join_neck_source_color").AsVector3()),
                        bodyMatch = Vector(humanoid.GetMeta("skin_join_match_body").AsVector3()),
                        leftHandMatch = Vector(
                            humanoid.GetMeta("skin_join_match_left_hand").AsVector3()),
                        rightHandMatch = Vector(
                            humanoid.GetMeta("skin_join_match_right_hand").AsVector3()),
                    },
                    bodyProportions = humanoid.Proportions,
                },
                world3dAudit = new
                {
                    scope = "entire-live-scene-and-active-gameplay-camera-frustum",
                    sourceSprite3dNodes = worldAudit.SourceSpriteNodes,
                    visibleSprite3dCards = worldAudit.VisibleSpriteCards,
                    inFrustumSprite3dCards = worldAudit.InFrustumSpriteCards,
                    closedReliefSourceObjects = worldAudit.ClosedReliefSourceObjects,
                    critters3d = worldAudit.Critters3D,
                    doors3d = worldAudit.Doors3D,
                    torches3d = worldAudit.Torches3D,
                    otherPropsAndStonePosts3d = worldAudit.OtherPropsAndStonePosts3D,
                    player3d = humanoid?.PresentationMode ??
                        Fo2HumanoidVisual.UnavailableMode,
                    playerMeshInstances = humanoid?.MeshInstances ?? 0,
                    sourceTorchAssemblies = worldAudit.SourceTorchAssemblies,
                    sourceTorchFrmPixelProps = worldAudit.SourceTorchFrmPixelProps,
                    sourceMapLightRecords = worldAudit.SourceMapLightRecords,
                    sourceMapLights = worldAudit.SourceMapLights,
                    sourceTorchMotivatedMapLights =
                        worldAudit.SourceTorchMotivatedMapLights,
                    sourceTorchPostLayeredAssemblies =
                        worldAudit.SourceTorchPostLayeredAssemblies,
                    inFrustumTorchAssemblies = worldAudit.InFrustumTorchAssemblies,
                    inFrustumTorchFrmPixelProps =
                        worldAudit.InFrustumTorchFrmPixelProps,
                    inFrustumTorchAssembliesWithMissingSourcePixels =
                        worldAudit.InFrustumTorchAssembliesWithMissingSourcePixels,
                    invalidSourceMapLights = worldAudit.InvalidSourceMapLights,
                    passed = worldAudit.VisibleSpriteCards == 0 &&
                        worldAudit.InFrustumSpriteCards == 0 &&
                        worldAudit.InFrustumTorchAssembliesWithMissingSourcePixels == 0 &&
                        worldAudit.InvalidSourceMapLights == 0,
                },
                sourceClosure = new
                {
                    sourceTopLevelObjects = sourceClosure.SourceTopLevelObjects,
                    caveShell3dSourceObjects = sourceClosure.CaveShell3DSourceObjects,
                    closedRelief3dSourceObjects = sourceClosure.ClosedRelief3DSourceObjects,
                    convertedTo3dSourceObjects = sourceClosure.ConvertedTo3DSourceObjects,
                    intentionallyHiddenSourceNonvisualBlocks =
                        sourceClosure.IntentionallyHiddenSourceNonvisualBlocks,
                    intentionallyHiddenSourceExitMarkers =
                        sourceClosure.IntentionallyHiddenSourceExitMarkers,
                    intentionallyHiddenBySourceState =
                        sourceClosure.IntentionallyHiddenBySourceState,
                    classifiedSourceObjects = sourceClosure.ClassifiedSourceObjects,
                    unaccountedSourceObjects = sourceClosure.UnaccountedSourceObjects,
                    scriptBackedSourceObjects = sourceClosure.ScriptBackedSourceObjects,
                    implementedSourceScripts = sourceClosure.ImplementedSourceScripts,
                    behaviorIncompleteSourceObjects =
                        sourceClosure.BehaviorIncompleteSourceObjects,
                    admittedFirstActionTiles = sourceClosure.AdmittedFirstActionTiles,
                    admittedScriptBackedSourceObjects =
                        sourceClosure.AdmittedScriptBackedSourceObjects,
                    admittedExitMarkerSourceObjects =
                        sourceClosure.AdmittedExitMarkerSourceObjects,
                    admittedInactiveExitMarkers =
                        sourceClosure.AdmittedInactiveExitMarkers,
                    outOfBeatSourceBoundaryExitMarkerInactive =
                        sourceClosure.OutOfBeatSourceBoundaryExitMarkerInactive,
                    admittedBehaviorIncompleteSourceObjects =
                        sourceClosure.AdmittedBehaviorIncompleteSourceObjects,
                    outOfBeatDeferredBehaviorSourceObjects =
                        sourceClosure.OutOfBeatDeferredBehaviorSourceObjects,
                    playerRuntime =
                        "owned-frm-state-driven-true-3d-humanoid-idle-walk-input-collision-non-parity",
                    hudRuntime = runtime.Hud.FirstMovementBeatStateComplete
                        ? "owned-fallout2-source-pixel-compositor-selected-character-vitals-first-movement-state"
                        : "unbound-no-visible-hud",
                    admittedAction = "physical-key-driven-walk-and-source-mask-boundary-rejection",
                    outboundTransitionExecutionImplemented = false,
                    candidateFrameAdmissionPassed =
                        sourceClosure.UnaccountedSourceObjects == 0 &&
                        worldAudit.VisibleSpriteCards == 0 &&
                        worldAudit.InFrustumSpriteCards == 0,
                    firstBeatRuntimeClosurePassed =
                        sourceClosure.FirstBeatRuntimeClosurePassed,
                },
                arrival = new
                {
                    mapIndex = scene.MapIndex,
                    elevation = scene.Elevation,
                    tile = catalog.ArrivalTile,
                    rotation = catalog.ArrivalRotation,
                    position = Vector(startPosition),
                    grounded = startFloor.Hit,
                    floorCollider = startFloor.ColliderPath,
                },
                configuredInput = new
                {
                    physicalKey = profile.AcceptanceKey.ToString(),
                    action = profile.MoveBackward.Action,
                    pressAndReleaseEvents = 2,
                    physicsFrames,
                    firstNeighborTile = profile.AcceptanceFirstNeighborTile,
                    firstNeighborReached,
                },
                movement = new
                {
                    finalTile = player.CurrentTile,
                    finalPosition = Vector(player.Position),
                    horizontalDistanceMeters = player.HorizontalDistanceFromSpawn,
                    completedTileTransitions = player.CompletedTileTransitions,
                    expectedTileTransitions = expectedTransitions,
                    rejectedMovementFrames = player.RejectedMovementFrames,
                    rejectedCandidateTile = player.LastRejectedCandidateTile,
                    rejectedCandidateOccupancy =
                        player.CanOccupy(profile.AcceptanceFirstRejectedTile),
                    groundedAtEnd = player.IsOnFloor(),
                    floorColliderAtEnd = endFloor.ColliderPath,
                },
                collision = new
                {
                    floorSupportPatches = runtime.FloorSupportPatches,
                    floorCollisionTriangles = runtime.FloorCollisionTriangles,
                    floorCollisionPath = runtime.FloorCollisionPath,
                    arrivalComponentHexes = runtime.ArrivalComponentHexes,
                    physicalPlayerCapsule = true,
                    sourceMaskKinematicGate = true,
                    rejectedSourceObjectSerial = profile.AcceptanceBlockingObjectSerial,
                    coLocatedExitGridSerial = profile.AcceptanceCoLocatedExitGridSerial,
                    outboundTransitionExecutionImplemented = false,
                    retailParity = false,
                },
                classicHud = new
                {
                    recipeId = runtime.Hud.RecipeId,
                    recipeSha256 = runtime.Hud.RecipeSha256,
                    mode = runtime.Hud.Mode,
                    visible = runtime.Hud.VisibleInViewport,
                    ownedFallout2ClassicInterface =
                        runtime.Hud.OwnedFallout2ClassicInterface,
                    sourcePixelLayout = runtime.Hud.SourcePixelLayout,
                    sourceAssetCount = runtime.Hud.OwnedSourceAssetCount,
                    source = "owned Fallout 2 ART/INTRFACE IFACE, numbers, AP-light, and button FRMs",
                    compositor = "one source-pixel RGBA surface",
                    godotLabels = 0,
                    retailBehaviorParity = runtime.Hud.RetailBehaviorParity,
                    startBlockedSourceLight = startHudBlockedSourceLight,
                    endBlockedSourceLight = runtime.Hud.BlockedSourceLightVisible,
                    exactPlayerTexture = presentation.CurrentFrame.PngSha256,
                    sourceStateTile = player.CurrentTile,
                },
                frames = new[]
                {
                    startFrame,
                    firstWalkFrame!,
                    secondWalkFrame!,
                    endFrame,
                    closeFinalFrame,
                },
                promotion = new
                {
                    transported = true,
                    rendered = true,
                    inputDrivenMovement = passed,
                    physicalFloorSupport = passed,
                    sourceMaskCollisionGate = passed,
                    characterArtLoaded = passed,
                    sourceWalkAnimationPlayed = passed,
                    ownedClassicHudSourcePixels = passed,
                    playerStatePersistent = false,
                    interactive = false,
                    humanInteractiveEntryAvailable = true,
                    playableCampaign = false,
                    launcherPlayable = false,
                    retailParityReviewed = false,
                    zeroVisibleWorldCards = worldAudit.VisibleSpriteCards == 0 &&
                        worldAudit.InFrustumSpriteCards == 0,
                    sourceObjectClosure = sourceClosure.UnaccountedSourceObjects == 0,
                    firstBeatRuntimeClosure = sourceClosure.FirstBeatRuntimeClosurePassed,
                },
                windowsAppControlUsed = false,
                foregroundActivationUsed = false,
                foregroundInputInjected = false,
            };
            var reportPath = Path.Combine(output, "arroyo-player-runtime-proof.json");
            File.WriteAllText(
                reportPath,
                JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) +
                    System.Environment.NewLine);
            if (passed)
                GD.Print(
                    $"OPENNV_FO2_ARROYO_PLAYER_PASS arrival={catalog.ArrivalTile} " +
                    $"final={player.CurrentTile} transitions={player.CompletedTileTransitions} " +
                    $"rejected={player.LastRejectedCandidateTile} output={output}");
            else
                GD.PushError($"OPENNV_FO2_ARROYO_PLAYER_FAIL output={output}");
            host.GetTree().Quit(passed ? 0 : 1);
        }
        catch (Exception exception)
        {
            if (pressed && runtime.Profile is not null)
                Input.ParseInputEvent(
                    Fo2ArroyoCavesInput.CreateEvent(runtime.Profile.AcceptanceKey, false));
            GD.PushError($"OPENNV_FO2_ARROYO_PLAYER_FAIL {exception}");
            host.GetTree().Quit(1);
        }
    }

    private static async Task WaitForDraws(Node host, int count)
    {
        for (var frame = 0; frame < count; frame++)
            await host.ToSignal(
                RenderingServer.Singleton,
                RenderingServer.SignalName.FramePostDraw);
    }

    private static FrameEvidence Capture(Node host, string output, string filename)
    {
        var path = Path.Combine(output, filename);
        var image = host.GetViewport().GetTexture().GetImage();
        if (image.IsEmpty() || image.GetWidth() != ExpectedWidth ||
            image.GetHeight() != ExpectedHeight)
            throw new InvalidOperationException(
                "Fallout 2 Arroyo player proof viewport dimensions drifted.");
        var error = image.SavePng(path);
        if (error != Error.Ok)
            throw new InvalidOperationException(
                $"Could not save Fallout 2 Arroyo player frame: {error}");
        using var stream = File.OpenRead(path);
        return new FrameEvidence(
            path,
            stream.Length,
            image.GetWidth(),
            image.GetHeight(),
            Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant());
    }

    private static FloorHit CastFloor(
        PhysicsDirectSpaceState3D space,
        Fo2ArroyoCavesPlayerBody player,
        Vector3 position)
    {
        var query = PhysicsRayQueryParameters3D.Create(
            position + Vector3.Up * 2.0f,
            position - Vector3.Up * 2.0f);
        query.Exclude = new Godot.Collections.Array<Rid> { player.GetRid() };
        var hit = space.IntersectRay(query);
        if (hit.Count == 0)
            return new FloorHit(false, "", Vector3.Zero);
        var collider = hit["collider"].AsGodotObject() as Node;
        return new FloorHit(
            true,
            collider?.GetPath().ToString() ?? "unknown",
            hit["position"].AsVector3());
    }

    private static float[] Vector(Vector3 value) => [value.X, value.Y, value.Z];

    internal static World3DAudit AuditWorld3D(
        Node host,
        Fo2ArroyoCavesPresentationCatalog catalog,
        Fo2ArroyoCavesSceneCoverage scene,
        Fo2ArroyoCavesPlayerRuntimeCoverage runtime,
        Camera3D camera)
    {
        var sprites = NodeTraversal.Descendants<Sprite3D>(host).ToArray();
        var visibleSprites = sprites.Where(sprite => sprite.Visible).ToArray();
        var reliefNodes = NodeTraversal.Descendants<Node3D>(host)
            .Where(node => node.HasMeta("fo2_map_serial") &&
                node.HasMeta("fo2_geometry_mode") &&
                node.GetMeta("fo2_geometry_mode").AsString() == catalog.ObjectRelief.Mode)
            .ToArray();
        var reliefSerials = reliefNodes
            .Select(node => node.GetMeta("fo2_map_serial").AsInt32())
            .ToArray();
        var reliefPaths = reliefNodes.ToDictionary(
            node => node.GetMeta("fo2_map_serial").AsInt32(),
            node => node.GetMeta("fo2_source_logical_path").AsString());
        var expectedReliefSerials = catalog.ObjectRelief.Placements
            .Where(row => row.Role != "caveWall")
            .Select(row => row.Serial)
            .Order()
            .ToArray();
        if (reliefSerials.Distinct().Count() != reliefSerials.Length ||
            reliefPaths.Count != expectedReliefSerials.Length ||
            !reliefSerials.Order().SequenceEqual(expectedReliefSerials))
            throw new InvalidOperationException(
                "Fallout 2 Arroyo live closed-relief source identity drifted.");
        var critters = reliefPaths.Values.Count(path =>
            path.StartsWith(CritterLogicalPathPrefix, StringComparison.OrdinalIgnoreCase));
        var doors = reliefPaths.Values.Count(path =>
            path.Contains(DoorLogicalPathFragment, StringComparison.OrdinalIgnoreCase));
        var torches = reliefPaths.Values.Count(path =>
            scene.Molded3D.Profile.TorchLogicalPaths.Contains(path.ToLowerInvariant()));
        var layeredTorches = reliefNodes.Count(node =>
            node.HasMeta("fo2_colocated_source_layer_mode") &&
            node.HasMeta("fo2_colocated_source_post_serial"));
        var torchProps = reliefNodes.Where(node =>
                scene.Molded3D.Profile.TorchLogicalPaths.Contains(
                    node.GetMeta("fo2_source_logical_path").AsString().ToLowerInvariant()))
            .ToArray();
        var inFrustumTorches = torchProps
            .Where(prop => camera.IsPositionInFrustum(prop.GlobalPosition))
            .ToArray();
        var missingSourcePixels = inFrustumTorches.Count(prop =>
            prop.GetMeta("fo2_torch_visual").AsString() !=
                "exact-source-frm-alpha-pixels-no-halo" ||
            prop.GetMeta("fo2_camera_facing").AsString() !=
                "source-world-relief-never-billboard" ||
            !prop.HasMeta("fo2_source_frame") ||
            !prop.HasMeta("fo2_source_pixel_offset") ||
            !prop.HasMeta("fo2_source_fid") ||
            !prop.HasMeta("fo2_source_pid"));
        var sourceLightPlacements = catalog.ObjectPlacements
            .Where(row => row.Elevation == Fo2ArroyoCavesPresentationCatalog.Elevation &&
                (row.LightDistance != 0 || row.LightIntensity != 0))
            .ToDictionary(row => row.Serial);
        var sourceLightAnchors = NodeTraversal.Descendants<Node3D>(host)
            .Where(node => node.Name.ToString().StartsWith(
                SourceMapLightAnchorPrefix,
                StringComparison.Ordinal) &&
                node.HasMeta("fo2_map_serial") &&
                node.HasMeta("fo2_map_tile") &&
                node.HasMeta("fo2_source_light_distance") &&
                node.HasMeta("fo2_source_light_intensity"))
            .ToArray();
        var sourceMapLights = sourceLightAnchors.Sum(anchor => anchor.GetChildren()
            .OfType<OmniLight3D>()
            .Count(node => node.Name == SourceMapLightNode));
        var sourceTorchMotivatedMapLights = sourceLightAnchors.Count(anchor =>
            anchor.HasMeta("fo2_source_torch_serial"));
        var invalidSourceMapLights = sourceLightAnchors.Count(anchor =>
        {
            var serial = anchor.GetMeta("fo2_map_serial").AsInt32();
            return !sourceLightPlacements.TryGetValue(serial, out var placement) ||
                anchor.GetMeta("fo2_map_tile").AsInt32() != placement.Tile ||
                anchor.GetMeta("fo2_source_light_distance").AsInt32() !=
                    placement.LightDistance ||
                anchor.GetMeta("fo2_source_light_intensity").AsInt32() !=
                    placement.LightIntensity ||
                anchor.GetChildren().OfType<OmniLight3D>().Count(node =>
                    node.Name == SourceMapLightNode) != 1;
        });
        return new World3DAudit(
            sprites.Length,
            visibleSprites.Length,
            visibleSprites.Count(sprite => camera.IsPositionInFrustum(sprite.GlobalPosition)),
            reliefNodes.Length,
            critters,
            doors,
            torches,
            reliefNodes.Length - critters - doors - torches,
            torchProps.Length,
            torchProps.Length,
            sourceLightPlacements.Count,
            sourceMapLights,
            sourceTorchMotivatedMapLights,
            layeredTorches,
            inFrustumTorches.Length,
            inFrustumTorches.Length,
            missingSourcePixels,
            invalidSourceMapLights);
    }

    internal static SourceClosureLedger BuildSourceClosure(
        Fo2ArroyoCavesPresentationCatalog catalog,
        Fo2ArroyoCavesSceneCoverage scene,
        Fo2ArroyoCavesPlayerRuntimeCoverage runtime,
        World3DAudit world)
    {
        var sourcePlacements = catalog.ObjectPlacements
            .Where(row => row.Elevation == Fo2ArroyoCavesPresentationCatalog.Elevation)
            .ToArray();
        var sourceTopLevel = sourcePlacements.Length;
        var hiddenBlockSerials = sourcePlacements
            .Where(row => row.ObjectType !=
                    scene.Molded3D.Profile.WallGeometry.SourceObjectType &&
                scene.Molded3D.Profile.HiddenCardLogicalPaths.Contains(
                    row.LogicalPath.ToLowerInvariant()))
            .Select(row => row.Serial)
            .ToHashSet();
        var exitMarkerSerials = sourcePlacements
            .Where(row => scene.Molded3D.Profile.HiddenSourceMarkerLogicalPaths.Contains(
                row.LogicalPath.ToLowerInvariant()))
            .Select(row => row.Serial)
            .ToHashSet();
        var intentionallyHiddenSerials = hiddenBlockSerials
            .Concat(exitMarkerSerials)
            .ToHashSet();
        var reliefSerials = catalog.ObjectRelief.Placements
            .Select(row => row.Serial)
            .ToHashSet();
        var convertedSerials = sourcePlacements
            .Where(row => !intentionallyHiddenSerials.Contains(row.Serial) &&
                (reliefSerials.Contains(row.Serial) ||
                    row.ObjectType == scene.Molded3D.Profile.WallGeometry.SourceObjectType))
            .Select(row => row.Serial)
            .ToHashSet();
        var classifiedSerials = convertedSerials
            .Concat(intentionallyHiddenSerials)
            .ToHashSet();
        var converted = convertedSerials.Count;
        var nonvisualBlocks = hiddenBlockSerials.Count;
        var exitMarkers = exitMarkerSerials.Count;
        var intentionallyHidden = intentionallyHiddenSerials.Count;
        var classified = classifiedSerials.Count;
        var unaccounted = sourcePlacements.Count(row =>
            !classifiedSerials.Contains(row.Serial));
        if (nonvisualBlocks != scene.Molded3D.HiddenNonWallBlockCards ||
            exitMarkers != scene.Molded3D.HiddenSourceMarkerCards ||
            convertedSerials.Count + intentionallyHiddenSerials.Count != classified ||
            classified + unaccounted != sourceTopLevel)
            throw new InvalidOperationException(
                "Fallout 2 Arroyo source-object closure sets drifted.");
        var scriptBacked = sourcePlacements.Count(row => row.ScriptIndex >= 0);
        var admittedTiles = new HashSet<int>
        {
            catalog.ArrivalTile,
            runtime.Profile.AcceptanceFirstNeighborTile,
        };
        var admittedScripts = sourcePlacements.Count(row =>
            row.ScriptIndex >= 0 && admittedTiles.Contains(row.Tile));
        var admittedExitMarkers = sourcePlacements.Count(row =>
            scene.Molded3D.Profile.HiddenSourceMarkerLogicalPaths.Contains(
                row.LogicalPath.ToLowerInvariant()) &&
            admittedTiles.Contains(row.Tile));
        var sourceBoundaryExitMarkerInactive = sourcePlacements.Any(row =>
            scene.Molded3D.Profile.HiddenSourceMarkerLogicalPaths.Contains(
                row.LogicalPath.ToLowerInvariant()) &&
            row.Serial == runtime.Profile.AcceptanceCoLocatedExitGridSerial &&
            row.Tile == runtime.Profile.AcceptanceFirstRejectedTile &&
            !runtime.Player.CanOccupy(row.Tile));
        var admittedInactiveExitMarkers = 0;
        var admittedIncomplete = admittedScripts + admittedExitMarkers;
        var globalIncomplete = scriptBacked + exitMarkers;
        return new SourceClosureLedger(
            sourceTopLevel,
            scene.Molded3D.CaveShellWallObjects,
            world.ClosedReliefSourceObjects,
            converted,
            nonvisualBlocks,
            exitMarkers,
            intentionallyHidden,
            classified,
            unaccounted,
            scriptBacked,
            0,
            globalIncomplete,
            admittedTiles.Count,
            admittedScripts,
            admittedExitMarkers,
            admittedInactiveExitMarkers,
            sourceBoundaryExitMarkerInactive,
            admittedIncomplete,
            globalIncomplete,
            unaccounted == 0 && admittedIncomplete == 0 &&
                runtime.Hud.FirstMovementBeatStateComplete);
    }

    private sealed record FrameEvidence(
        string Path,
        long Bytes,
        int Width,
        int Height,
        string Sha256);

    private readonly record struct FloorHit(bool Hit, string ColliderPath, Vector3 Position);

    internal sealed record World3DAudit(
        int SourceSpriteNodes,
        int VisibleSpriteCards,
        int InFrustumSpriteCards,
        int ClosedReliefSourceObjects,
        int Critters3D,
        int Doors3D,
        int Torches3D,
        int OtherPropsAndStonePosts3D,
        int SourceTorchAssemblies,
        int SourceTorchFrmPixelProps,
        int SourceMapLightRecords,
        int SourceMapLights,
        int SourceTorchMotivatedMapLights,
        int SourceTorchPostLayeredAssemblies,
        int InFrustumTorchAssemblies,
        int InFrustumTorchFrmPixelProps,
        int InFrustumTorchAssembliesWithMissingSourcePixels,
        int InvalidSourceMapLights);

    internal sealed record SourceClosureLedger(
        int SourceTopLevelObjects,
        int CaveShell3DSourceObjects,
        int ClosedRelief3DSourceObjects,
        int ConvertedTo3DSourceObjects,
        int IntentionallyHiddenSourceNonvisualBlocks,
        int IntentionallyHiddenSourceExitMarkers,
        int IntentionallyHiddenBySourceState,
        int ClassifiedSourceObjects,
        int UnaccountedSourceObjects,
        int ScriptBackedSourceObjects,
        int ImplementedSourceScripts,
        int BehaviorIncompleteSourceObjects,
        int AdmittedFirstActionTiles,
        int AdmittedScriptBackedSourceObjects,
        int AdmittedExitMarkerSourceObjects,
        int AdmittedInactiveExitMarkers,
        bool OutOfBeatSourceBoundaryExitMarkerInactive,
        int AdmittedBehaviorIncompleteSourceObjects,
        int OutOfBeatDeferredBehaviorSourceObjects,
        bool FirstBeatRuntimeClosurePassed);
}
