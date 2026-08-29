using System.Security.Cryptography;
using System.Text.Json;
using Godot;

namespace OpenNV.Runtime.Campaigns.Fallout2.Temple;

internal static class Fo2ArroyoCavesPlayProof
{
    private const int GroundingFrames = 120;
    private const int SettleFrames = 4;
    private const int ExpectedWidth = 1280;
    private const int ExpectedHeight = 720;

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
            await WaitForDraws(host, SettleFrames);
            var startFrame = Capture(host, output, "player-arrival-start.png");
            var startDirection = presentation.Direction;
            var startPlayerPngSha256 = presentation.CurrentFrame.PngSha256;
            var startWalkFrameAdvances = presentation.WalkFrameAdvances;
            var startWalkCycles = presentation.CompletedWalkCycles;

            var startPosition = player.Position;
            var firstNeighborReached = false;
            Input.ParseInputEvent(Fo2ArroyoCavesInput.CreateEvent(profile.AcceptanceKey, true));
            pressed = true;
            var physicsFrames = 0;
            for (; physicsFrames < profile.AcceptanceMaximumPhysicsFrames; physicsFrames++)
            {
                await host.ToSignal(host.GetTree(), SceneTree.SignalName.PhysicsFrame);
                firstNeighborReached |=
                    player.CurrentTile == profile.AcceptanceFirstNeighborTile ||
                    player.CompletedTileTransitions > 0;
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
            var expectedEndDirection = Fo2ArroyoCavesPlayerBody.DirectionForMovement(
                player.CurrentTile,
                Vector3.Back);
            var expectedTransitions =
                (profile.AcceptanceLastWalkableTile - catalog.ArrivalTile) /
                Fo1HexMath.Width;
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
                presentation.Visible &&
                presentation.Texture is not null &&
                startDirection == catalog.ArrivalRotation &&
                presentation.Direction == expectedEndDirection &&
                presentation.WalkFrameAdvances > startWalkFrameAdvances &&
                presentation.CompletedWalkCycles > startWalkCycles &&
                !presentation.IsWalking &&
                presentation.AnimationCode == "AA" &&
                presentation.AnimationFrame == Fo2ArroyoPlayerPresentationCatalog.IdleFrame &&
                presentation.CurrentFrame.LogicalPath ==
                    Fo2ArroyoPlayerPresentationCatalog.ExpectedLogicalPath &&
                startPlayerPngSha256 != presentation.CurrentFrame.PngSha256 &&
                startFrame.Sha256 != endFrame.Sha256;
            var report = new
            {
                schema = "opennv-fo2-arroyo-player-runtime-proof/v1",
                status = passed
                    ? "pass-input-driven-source-gated-player-runtime-owned-hmwarr-no-save"
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
                    visible = presentation.Visible,
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
                frames = new[] { startFrame, endFrame },
                promotion = new
                {
                    transported = true,
                    rendered = true,
                    inputDrivenMovement = passed,
                    physicalFloorSupport = passed,
                    sourceMaskCollisionGate = passed,
                    characterArtLoaded = passed,
                    sourceWalkAnimationPlayed = passed,
                    playerStatePersistent = false,
                    interactive = false,
                    humanInteractiveEntryAvailable = true,
                    playableCampaign = false,
                    launcherPlayable = false,
                    retailParityReviewed = false,
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

    private sealed record FrameEvidence(
        string Path,
        long Bytes,
        int Width,
        int Height,
        string Sha256);

    private readonly record struct FloorHit(bool Hit, string ColliderPath, Vector3 Position);
}
