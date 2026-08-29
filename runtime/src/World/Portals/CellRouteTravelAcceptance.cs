using System.Security.Cryptography;
using System.Text.Json;
using Godot;

namespace OpenNV.Runtime.World.Portals;

internal static class CellRouteTravelAcceptance
{
    private const string ReportSchema = "opennv-flat-route-travel/v1";
    private const float WaypointToleranceMeters = 0.18f;
    private const float PortalApproachDistanceMeters = 0.75f;
    private const float ObstacleRecoveryClearanceRadii = 4.0f;
    private const float ObstacleBoundaryTravelRadii = 20.0f;
    private const float ObstacleBoundaryOutwardBias = 0.25f;
    private const int StalledFrameLimit = 180;
    private const int MinimumWaypointFrameBudget = 240;
    private const int MaximumOwnedNavigationReplans = 3;

    internal static async Task Run(
        RuntimeCoordinator host,
        CellSceneLoader.LoadedCell loaded,
        string scenePath,
        string mode,
        IReadOnlyDictionary<string, string> options,
        RuntimeConfiguration configuration)
    {
        if (loaded.Player.UsesXr || loaded.Player.UsesClassicDiorama)
            throw new InvalidOperationException(
                "Flat route travel acceptance requires desktop first-person presentation.");
        if (loaded.Session.OpeningState is not { Completed: true, Stage: 200 })
            throw new InvalidOperationException(
                "Flat route travel acceptance requires the completed owned opening save.");
        if (loaded.PortalLinks.Count == 0)
            throw new InvalidOperationException("Prepared route has no reciprocal portals.");
        if (mode is not "first-run" and not "cold-reload")
            throw new InvalidOperationException($"Unsupported route travel phase: {mode}");

        var input = configuration.Player.DesktopInput;
        try
        {
            await FlatControlsAcceptance.WaitPhysicsFrames(host, input.Acceptance.SettleFrames);
            if (mode == "first-run")
                await RunFirstPass(host, loaded, input, configuration);
            else
                VerifyColdReload(loaded);
            await FlatControlsAcceptance.WaitPhysicsFrames(host, input.Acceptance.SettleFrames);
            WriteReport(loaded, scenePath, mode, options, configuration);
            GD.Print(
                $"OPENNV_FLAT_ROUTE_TRAVEL_PASS phase={mode} " +
                $"activeCell={loaded.Session.ActiveCellFormId} " +
                $"transitions={loaded.Player.PortalTransitions.Count}");
        }
        finally
        {
            foreach (var binding in input.KeyBindings)
                Input.ParseInputEvent(DesktopInputMap.CreateEvent(binding, false));
            foreach (var binding in input.MouseBindings)
                Input.ParseInputEvent(DesktopInputMap.CreateEvent(binding, false));
            Input.MouseMode = Input.MouseModeEnum.Visible;
        }
    }

    private static async Task RunFirstPass(
        RuntimeCoordinator host,
        CellSceneLoader.LoadedCell loaded,
        DesktopInputConfiguration input,
        RuntimeConfiguration configuration)
    {
        if (!loaded.Session.ActiveCellFormId.Equals(
                loaded.FormId,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"First route pass did not begin in the root CELL: " +
                $"{loaded.Session.ActiveCellFormId}");
        VerifyActiveSet(loaded);

        await FlatControlsAcceptance.PulseMouseBinding(
            host,
            input.CaptureMouse,
            input.Acceptance.SettleFrames);
        if (Input.MouseMode != Input.MouseModeEnum.Captured)
            throw new InvalidOperationException(
                "Configured desktop mouse capture was not accepted for route travel.");

        foreach (var link in loaded.PortalLinks)
        {
            if (!loaded.Session.ActiveCellFormId.Equals(
                    link.FromCellFormId,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"Ordered route expected {link.FromCellFormId}, found " +
                    $"{loaded.Session.ActiveCellFormId}.");
            if (link.FromDoor.IsOpen || link.ToDoor.IsOpen)
                throw new InvalidOperationException(
                    $"Route acceptance requires a closed portal before configured activation: " +
                    $"{link.FromDoor.ReferenceFormId}");

            var sourceContent = ContentFor(loaded, link.FromCellFormId);
            await WalkToPortalApproach(
                host,
                loaded.Player,
                sourceContent,
                link.FromFrame,
                input,
                configuration);
            FlatControlsAcceptance.ApplyMouseLook(
                loaded.Player,
                (link.FromFrame.From + link.FromFrame.To) / 2.0f,
                configuration.Player);
            await FlatControlsAcceptance.WaitPhysicsFrames(
                host,
                input.Acceptance.SettleFrames);
            var startingTransitionCount = loaded.Player.PortalTransitions.Count;
            await FlatControlsAcceptance.PulseKeyBinding(
                host,
                input.Activate,
                input.Acceptance.SettleFrames);
            if (!loaded.Player.LastActivationDoorFormId.Equals(
                    link.FromDoor.ReferenceFormId,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"Configured activation did not resolve expected portal door " +
                    $"{link.FromDoor.ReferenceFormId}: " +
                    $"door={loaded.Player.LastActivationDoorFormId} " +
                    $"collider={loaded.Player.LastActivationCollider}.");
            if (!link.FromDoor.IsOpen || !link.ToDoor.IsOpen)
                throw new InvalidOperationException(
                    $"Configured activation did not open reciprocal portal " +
                    $"{link.FromDoor.ReferenceFormId} -> {link.ToDoor.ReferenceFormId}: " +
                    $"player={loaded.Player.GlobalPosition} " +
                    $"camera={loaded.Player.Camera.GlobalPosition} " +
                    $"forward={-loaded.Player.Camera.GlobalBasis.Z} " +
                    $"doorCenter={(link.FromFrame.From + link.FromFrame.To) / 2.0f} " +
                    $"distance={loaded.Player.Camera.GlobalPosition.DistanceTo(
                        (link.FromFrame.From + link.FromFrame.To) / 2.0f):F3} " +
                    $"collider={loaded.Player.LastActivationCollider}.");
            if (loaded.Player.PortalTransitions.Count != startingTransitionCount + 1 ||
                !loaded.Session.ActiveCellFormId.Equals(
                    link.ToCellFormId,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"Configured activation did not travel through XTEL portal " +
                    $"{link.FromDoor.ReferenceFormId} -> {link.ToDoor.ReferenceFormId} " +
                    $"into {link.ToCellFormId}.");
            VerifyActiveSet(loaded);
        }

        if (loaded.Player.PortalTransitions.Count != loaded.PortalLinks.Count)
            throw new InvalidOperationException(
                "Production portal owner did not record every ordered CELL transition.");
        await FlatControlsAcceptance.PulseKeyBinding(
            host,
            input.Save,
            input.Acceptance.SettleFrames);
        if (!File.Exists(loaded.Session.SavePath))
            throw new InvalidOperationException(
                "Configured save input did not persist the completed route.");
    }

    private static void VerifyColdReload(CellSceneLoader.LoadedCell loaded)
    {
        var expectedCellFormId = loaded.PortalLinks[^1].ToCellFormId;
        if (!loaded.Session.ActiveCellRestored ||
            !loaded.Session.ActiveCellFormId.Equals(
                expectedCellFormId,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Cold Continue did not restore the final active CELL: " +
                $"expected={expectedCellFormId} actual={loaded.Session.ActiveCellFormId}.");
        VerifyActiveSet(loaded);
        var report = loaded.Session.Report();
        var reportJson = JsonSerializer.SerializeToElement(report);
        if (!reportJson.GetProperty("playerTransformRestored").GetBoolean())
            throw new InvalidOperationException(
                "Cold Continue did not restore the saved player transform.");
    }

    private static void VerifyActiveSet(CellSceneLoader.LoadedCell loaded)
    {
        var current = loaded.Session.ActiveCellFormId;
        var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            current,
        };
        foreach (var portal in loaded.PortalLinks)
        {
            if (portal.FromCellFormId.Equals(current, StringComparison.OrdinalIgnoreCase))
                expected.Add(portal.ToCellFormId);
            else if (portal.ToCellFormId.Equals(current, StringComparison.OrdinalIgnoreCase))
                expected.Add(portal.FromCellFormId);
        }
        if (!expected.SetEquals(loaded.ActiveSet.ActiveCellFormIds))
            throw new InvalidOperationException(
                $"CELL active set differs from current-plus-neighbors: " +
                $"expected={string.Join(',', expected.OrderBy(value => value))} " +
                $"actual={string.Join(',', loaded.ActiveSet.ActiveCellFormIds.OrderBy(value => value))}");

        foreach (var space in loaded.ActiveSet.Snapshot())
        {
            if (space.Active)
            {
                if (space.VisibleRoots != space.SourceVisibleRoots ||
                    space.ProcessingRoots != space.SourceProcessingRoots ||
                    space.EnabledCollisionObjects != space.SourceEnabledCollisionObjects ||
                    space.FrozenRigidBodies != space.SourceFrozenRigidBodies ||
                    space.VisibleLights != space.SourceVisibleLights)
                    throw new InvalidOperationException(
                        $"Active CELL resources are not fully enabled: {space.FormId}");
            }
            else if (space.VisibleRoots != 0 || space.ProcessingRoots != 0 ||
                     space.EnabledCollisionObjects != 0 ||
                     space.FrozenRigidBodies != space.RigidBodies ||
                     space.VisibleLights != 0)
            {
                throw new InvalidOperationException(
                    $"Distant CELL resources remain active: {space.FormId}");
            }
        }
    }

    private static async Task WalkToPortalApproach(
        RuntimeCoordinator host,
        CellPlayer player,
        CellContentLoader.LoadedContent content,
        CellSceneLoader.DoorRay frame,
        DesktopInputConfiguration input,
        RuntimeConfiguration configuration)
    {
        var center = (frame.From + frame.To) / 2.0f;
        var normal = HorizontalNormal(frame);
        var side = MathF.Sign((player.GlobalPosition - center).Dot(normal));
        if (Mathf.IsZeroApprox(side))
            throw new InvalidOperationException("Player began on the portal plane.");
        var desiredWorld = center + normal * side * PortalApproachDistanceMeters;
        var destinationGame = content.Navigation.FindNearestPoint(
            content.WorldToGame(desiredWorld));
        NavigationStall? lastStall = null;
        for (var replan = 0; replan <= MaximumOwnedNavigationReplans; replan++)
        {
            var playerFoot = player.GlobalPosition -
                Vector3.Up * configuration.Player.SpawnCenterHeightMeters;
            var path = content.Navigation.FindPath(
                content.WorldToGame(playerFoot),
                destinationGame);
            if (replan > 0)
                GD.Print(
                    $"OPENNV_FLAT_ROUTE_NAVM_REPLAN attempt={replan}/" +
                    $"{MaximumOwnedNavigationReplans} waypoints={path.Count} " +
                    $"player={player.GlobalPosition}");
            lastStall = await WalkNavigationPath(
                host,
                player,
                content,
                path,
                center,
                input,
                configuration);
            if (lastStall is null)
                return;
            if (player.Camera.GlobalPosition.DistanceTo(center) <=
                configuration.Player.ActivationDistanceMeters)
                return;
            if (replan < MaximumOwnedNavigationReplans)
            {
                await HandleRouteStallForReplan(
                    host,
                    player,
                    content,
                    lastStall.Value,
                    input,
                    configuration);
                GD.Print(
                    $"OPENNV_FLAT_ROUTE_NAVM_STALL " +
                    $"waypoint={lastStall.Value.WaypointIndex + 1}/" +
                    $"{lastStall.Value.WaypointCount} " +
                    $"remaining={lastStall.Value.RemainingMeters:F3} " +
                    $"replan={replan + 1}/{MaximumOwnedNavigationReplans}");
            }
        }

        var stalled = lastStall ?? throw new InvalidOperationException(
            "Owned NAVM route exhausted replans without a recorded stall.");
        var finalSweep = CapsuleSweep(player, stalled.Target);
        throw new InvalidOperationException(
            $"Configured movement exhausted {MaximumOwnedNavigationReplans} owned NAVM replans " +
            $"at waypoint {stalled.WaypointIndex + 1}/{stalled.WaypointCount}: " +
            $"remaining={stalled.RemainingMeters:F3} " +
            $"doorDistance={player.Camera.GlobalPosition.DistanceTo(center):F3} " +
            $"player={player.GlobalPosition} target={stalled.Target} " +
            $"step={player.LastStepAttempt} movement={player.LastMovementState} " +
            $"motionCollision={player.LastMovementCollision} " +
            $"collision={DescribeSlideCollisions(player)} " +
            $"sweep={DescribeSweepCollision(finalSweep)} metres.");
    }

    private static async Task<NavigationStall?> WalkNavigationPath(
        RuntimeCoordinator host,
        CellPlayer player,
        CellContentLoader.LoadedContent content,
        IReadOnlyList<Vector3> path,
        Vector3 portalCenter,
        DesktopInputConfiguration input,
        RuntimeConfiguration configuration)
    {
        for (var waypointIndex = 0; waypointIndex < path.Count; waypointIndex++)
        {
            if (waypointIndex == path.Count - 2)
            {
                var finalCenter = content.GameToWorld(path[^1]) +
                    Vector3.Up * configuration.Player.SpawnCenterHeightMeters;
                if (CanAdvanceCapsule(player, finalCenter))
                {
                    GD.Print(
                        $"OPENNV_FLAT_ROUTE_FINAL_SHORTCUT " +
                        $"skipped={waypointIndex + 1}/{path.Count}");
                    waypointIndex++;
                }
            }
            var waypointCenter = content.GameToWorld(path[waypointIndex]) +
                Vector3.Up * configuration.Player.SpawnCenterHeightMeters;
            var finalApproach = waypointIndex >= path.Count - 3;
            var waypointTolerance = finalApproach
                ? WaypointToleranceMeters
                : MathF.Max(
                    WaypointToleranceMeters,
                    configuration.Player.CapsuleRadiusMeters * 2.0f);
            var reached = await WalkToWaypoint(
                host,
                player,
                waypointCenter,
                input,
                configuration,
                waypointTolerance,
                finalApproach);
            if (!reached)
                reached = await RecoverAroundObstacle(
                    host,
                    player,
                    waypointCenter,
                    input,
                    configuration,
                    waypointTolerance,
                    finalApproach);
            if (reached)
                continue;
            var waypointDistance = HorizontalDistance(player.GlobalPosition, waypointCenter);
            if (waypointIndex + 3 < path.Count &&
                waypointDistance <= configuration.Player.ActivationDistanceMeters &&
                VerticalDistance(player.GlobalPosition, waypointCenter) <=
                    WaypointToleranceMeters &&
                CanAdvanceCapsule(player, waypointCenter))
            {
                GD.Print(
                    $"OPENNV_FLAT_ROUTE_CORRIDOR_ADVANCE index={waypointIndex + 1} " +
                    $"distance={waypointDistance:F3}");
                continue;
            }
            var doorDistance = player.Camera.GlobalPosition.DistanceTo(portalCenter);
            if (doorDistance <= configuration.Player.ActivationDistanceMeters)
                return null;
            return new NavigationStall(
                waypointIndex,
                path.Count,
                waypointDistance,
                waypointCenter);
        }
        return null;
    }

    private static async Task HandleRouteStallForReplan(
        RuntimeCoordinator host,
        CellPlayer player,
        CellContentLoader.LoadedContent content,
        NavigationStall stall,
        DesktopInputConfiguration input,
        RuntimeConfiguration configuration)
    {
        var blockingCollider = player.LastBlockingCollider;
        var door = Ancestor<DoorInstance>(blockingCollider);
        if (door is null)
        {
            var colliderTransform = blockingCollider is Node3D blockingNode
                ? blockingNode.GlobalTransform.ToString()
                : "not-node3d";
            GD.Print(
                $"OPENNV_FLAT_ROUTE_NON_DOOR_STALL " +
                $"collider={blockingCollider?.GetPath().ToString() ?? "unknown"} " +
                $"colliderTransform={colliderTransform} " +
                $"blockingPosition={player.LastBlockingPosition?.ToString() ?? "unknown"} " +
                $"blockingNormal={player.LastBlockingNormal} " +
                $"player={player.GlobalTransform} target={stall.Target} " +
                $"waypoint={stall.WaypointIndex + 1}/{stall.WaypointCount} " +
                $"sweep={DescribeSweepCollision(CapsuleSweep(player, stall.Target))}");
            return;
        }
        if (!content.Doors.TryGetValue(door.ReferenceFormId, out var activeDoor) ||
            !ReferenceEquals(activeDoor, door))
            throw new InvalidOperationException(
                "Configured route was blocked by a door outside the active CELL.");
        if (door.Destination is not null || door.LinkedDoor is not null)
            throw new InvalidOperationException(
                "Configured route stall resolved to an XTEL door outside the ordered portal step.");
        var blockingPosition = player.LastBlockingPosition ?? throw new InvalidOperationException(
            "Configured route door blocker has no physics impact position.");
        var distance = player.Camera.GlobalPosition.DistanceTo(blockingPosition);
        if (distance > configuration.Player.ActivationDistanceMeters)
            throw new InvalidOperationException(
                $"Configured route door blocker is outside activation distance: " +
                $"distance={distance:F3} limit={configuration.Player.ActivationDistanceMeters:F3}.");

        var wasAlreadyOpen = door.IsOpen;
        if (!wasAlreadyOpen)
        {
            FlatControlsAcceptance.ApplyMouseLook(
                player,
                blockingPosition,
                configuration.Player);
            await FlatControlsAcceptance.WaitPhysicsFrames(host, input.Acceptance.SettleFrames);
            await FlatControlsAcceptance.PulseKeyBinding(
                host,
                input.Activate,
                input.Acceptance.SettleFrames);
            if (!door.IsOpen)
                throw new InvalidOperationException(
                    $"Configured activation did not open blocking door {door.ReferenceFormId}: " +
                    $"collider={player.LastActivationCollider}.");
        }
        await door.WaitForArticulation();
        if (door.HasSourceArticulation && !door.SourceOpenTerminalApplied)
            throw new InvalidOperationException(
                $"Blocking door {door.ReferenceFormId} did not apply its source open terminal: " +
                $"player={player.GlobalTransform} door={door.GlobalTransform}.");
        var immediateCollision = CapsuleSweep(player, stall.Target);
        GD.Print(
            $"OPENNV_FLAT_ROUTE_BLOCKING_DOOR_OPEN form={door.ReferenceFormId} " +
            $"alreadyOpen={wasAlreadyOpen} distance={distance:F3} " +
            $"waypoint={stall.WaypointIndex + 1}/{stall.WaypointCount} " +
            $"player={player.GlobalTransform} target={stall.Target} door={door.GlobalTransform} " +
            $"immediateSweep={DescribeSweepCollision(immediateCollision)}");
    }

    private static async Task<bool> WalkToWaypoint(
        RuntimeCoordinator host,
        CellPlayer player,
        Vector3 target,
        DesktopInputConfiguration input,
        RuntimeConfiguration configuration,
        float toleranceMeters,
        bool requireDirectSweep)
    {
        var initialDistance = HorizontalDistance(player.GlobalPosition, target);
        var frameBudget = Math.Max(
            MinimumWaypointFrameBudget,
            (int)MathF.Ceiling(
                initialDistance / configuration.Player.MoveSpeedMetersPerSecond *
                configuration.Simulation.PhysicsTicksPerSecond * 4.0f));
        var bestDistance = initialDistance;
        var stalledFrames = 0;
        Input.ParseInputEvent(DesktopInputMap.CreateEvent(input.MoveForward, true));
        try
        {
            for (var frame = 0; frame < frameBudget; frame++)
            {
                var distance = HorizontalDistance(player.GlobalPosition, target);
                if (distance <= toleranceMeters &&
                    VerticalDistance(player.GlobalPosition, target) <=
                        WaypointToleranceMeters &&
                    (!requireDirectSweep || CanAdvanceCapsule(player, target)))
                    return true;
                FlatControlsAcceptance.ApplyMouseYaw(player, target, configuration.Player);
                await FlatControlsAcceptance.WaitPhysicsFrames(host, 1);
                var updatedDistance = HorizontalDistance(player.GlobalPosition, target);
                if (updatedDistance + 0.005f < bestDistance)
                {
                    bestDistance = updatedDistance;
                    stalledFrames = 0;
                }
                else if (++stalledFrames >= StalledFrameLimit)
                    break;
            }
        }
        finally
        {
            Input.ParseInputEvent(DesktopInputMap.CreateEvent(input.MoveForward, false));
            await FlatControlsAcceptance.WaitPhysicsFrames(host, input.Acceptance.SettleFrames);
        }
        return false;
    }

    private static async Task<bool> RecoverAroundObstacle(
        RuntimeCoordinator host,
        CellPlayer player,
        Vector3 target,
        DesktopInputConfiguration input,
        RuntimeConfiguration configuration,
        float toleranceMeters,
        bool requireDirectSweep)
    {
        if (await FollowObstacleBoundary(
                host,
                player,
                target,
                input,
                configuration) &&
            await WalkToWaypoint(
                host,
                player,
                target,
                input,
                configuration,
                toleranceMeters,
                requireDirectSweep))
            return true;

        var clearanceFrames = Math.Max(
            input.Acceptance.SettleFrames,
            (int)MathF.Ceiling(
                configuration.Player.CapsuleRadiusMeters * ObstacleRecoveryClearanceRadii /
                configuration.Player.MoveSpeedMetersPerSecond *
                configuration.Simulation.PhysicsTicksPerSecond));
        foreach (var recovery in new[]
                 {
                     (input.MoveLeft, clearanceFrames),
                     (input.MoveRight, clearanceFrames * 2),
                 })
        {
            Input.ParseInputEvent(DesktopInputMap.CreateEvent(recovery.Item1, true));
            try
            {
                for (var frame = 0; frame < recovery.Item2; frame++)
                {
                    FlatControlsAcceptance.ApplyMouseYaw(player, target, configuration.Player);
                    await FlatControlsAcceptance.WaitPhysicsFrames(host, 1);
                }
            }
            finally
            {
                Input.ParseInputEvent(DesktopInputMap.CreateEvent(recovery.Item1, false));
                await FlatControlsAcceptance.WaitPhysicsFrames(
                    host,
                    input.Acceptance.SettleFrames);
            }
            if (await WalkToWaypoint(
                    host,
                    player,
                    target,
                    input,
                    configuration,
                    toleranceMeters,
                    requireDirectSweep))
                return true;
        }
        return false;
    }

    private static async Task<bool> FollowObstacleBoundary(
        RuntimeCoordinator host,
        CellPlayer player,
        Vector3 target,
        DesktopInputConfiguration input,
        RuntimeConfiguration configuration)
    {
        var normal = player.LastBlockingNormal;
        normal.Y = 0.0f;
        if (normal.IsZeroApprox())
        {
            normal = CapsuleSweep(player, target)?.GetNormal() ?? Vector3.Zero;
            normal.Y = 0.0f;
        }
        if (normal.IsZeroApprox())
            return false;
        normal = normal.Normalized();
        var targetDirection = target - player.GlobalPosition;
        targetDirection.Y = 0.0f;
        if (targetDirection.IsZeroApprox())
            return true;
        targetDirection = targetDirection.Normalized();
        var first = new Vector3(normal.Z, 0.0f, -normal.X);
        var second = -first;
        var tangent = first.Dot(targetDirection) >= second.Dot(targetDirection)
            ? first
            : second;
        tangent = (tangent + normal * ObstacleBoundaryOutwardBias).Normalized();
        var frames = Math.Max(
            input.Acceptance.SettleFrames,
            (int)MathF.Ceiling(
                configuration.Player.CapsuleRadiusMeters * ObstacleBoundaryTravelRadii /
                configuration.Player.MoveSpeedMetersPerSecond *
                configuration.Simulation.PhysicsTicksPerSecond));
        var start = player.GlobalPosition;
        var targetSweepClear = false;
        Input.ParseInputEvent(DesktopInputMap.CreateEvent(input.MoveForward, true));
        try
        {
            for (var frame = 0; frame < frames; frame++)
            {
                FlatControlsAcceptance.ApplyMouseYaw(
                    player,
                    player.GlobalPosition + tangent,
                    configuration.Player);
                await FlatControlsAcceptance.WaitPhysicsFrames(host, 1);
                if (HorizontalDistance(start, player.GlobalPosition) >=
                        configuration.Player.CapsuleRadiusMeters &&
                    CanAdvanceCapsule(player, target))
                {
                    targetSweepClear = true;
                    break;
                }
            }
        }
        finally
        {
            Input.ParseInputEvent(DesktopInputMap.CreateEvent(input.MoveForward, false));
            await FlatControlsAcceptance.WaitPhysicsFrames(
                host,
                input.Acceptance.SettleFrames);
        }
        return targetSweepClear;
    }

    private static CellContentLoader.LoadedContent ContentFor(
        CellSceneLoader.LoadedCell loaded,
        string cellFormId)
    {
        if (loaded.MainContent.FormId.Equals(cellFormId, StringComparison.OrdinalIgnoreCase))
            return loaded.MainContent;
        return loaded.LinkedCells
            .Select(linked => linked.Content)
            .Single(content => content.FormId.Equals(
                cellFormId,
                StringComparison.OrdinalIgnoreCase));
    }

    private static Vector3 HorizontalNormal(CellSceneLoader.DoorRay frame)
    {
        var normal = frame.To - frame.From;
        normal.Y = 0.0f;
        return normal.Normalized();
    }

    private static float HorizontalDistance(Vector3 first, Vector3 second)
    {
        var delta = second - first;
        delta.Y = 0.0f;
        return delta.Length();
    }

    private static float VerticalDistance(Vector3 first, Vector3 second) =>
        MathF.Abs(second.Y - first.Y);

    private static bool CanAdvanceCapsule(CellPlayer player, Vector3 target)
    {
        var collision = CapsuleSweep(player, target);
        if (collision is null)
            return true;
        var remainder = collision.GetRemainder();
        return new Vector3(remainder.X, 0.0f, remainder.Z).IsZeroApprox();
    }

    private static KinematicCollision3D? CapsuleSweep(CellPlayer player, Vector3 target)
    {
        var horizontalTarget = new Vector3(target.X, player.GlobalPosition.Y, target.Z);
        var motion = horizontalTarget - player.GlobalPosition;
        if (motion.IsZeroApprox())
            return null;
        return player.MoveAndCollide(
            motion,
            testOnly: true,
            safeMargin: player.SafeMargin,
            recoveryAsCollision: true);
    }

    private static string DescribeSweepCollision(KinematicCollision3D? collision)
    {
        if (collision is null)
            return "clear";
        var collider = collision.GetCollider() as Node;
        var transform = collider is Node3D node
            ? node.GlobalTransform.ToString()
            : "not-node3d";
        return $"collider={collider?.GetPath().ToString() ?? "unknown"} " +
            $"colliderTransform={transform} colliderShape={collision.GetColliderShape()} " +
            $"localShape={collision.GetLocalShape()} position={collision.GetPosition()} " +
            $"normal={collision.GetNormal()} travel={collision.GetTravel()} " +
            $"remainder={collision.GetRemainder()}";
    }

    private static T? Ancestor<T>(Node? node)
        where T : Node
    {
        while (node is not null)
        {
            if (node is T match)
                return match;
            node = node.GetParent();
        }
        return null;
    }

    private static string DescribeSlideCollisions(CellPlayer player)
    {
        if (player.GetSlideCollisionCount() == 0)
            return "none";
        return string.Join(
            ";",
            Enumerable.Range(0, player.GetSlideCollisionCount()).Select(index =>
            {
                var collision = player.GetSlideCollision(index);
                var collider = collision.GetCollider() as Node;
                return $"{collider?.GetPath().ToString() ?? "unknown"}" +
                    $"@{collision.GetPosition()} normal={collision.GetNormal()}";
            }));
    }

    private static void WriteReport(
        CellSceneLoader.LoadedCell loaded,
        string scenePath,
        string mode,
        IReadOnlyDictionary<string, string> options,
        RuntimeConfiguration configuration)
    {
        var saveBytes = File.ReadAllBytes(loaded.Session.SavePath);
        var transitions = loaded.Player.PortalTransitions;
        var sunny = loaded.Actors.SingleOrDefault(actor =>
            actor.ReferenceFormId.Equals("00104e85", StringComparison.OrdinalIgnoreCase));
        var transform = loaded.Player.GlobalTransform;
        RuntimeCoordinator.WriteReport(
            RuntimeCoordinator.RequireOption(options, "report"),
            new
            {
                schema = ReportSchema,
                status = "pass",
                phase = mode,
                inputTransport = "owned-menu-button-signal-plus-godot-input-map",
                windowsAppControlUsed = false,
                foregroundInputInjected = false,
                configurationSchema = RuntimeConfiguration.ExpectedSchema,
                configurationSha256 = configuration.Sha256,
                scene = Path.GetFullPath(scenePath),
                routeCellFormId = loaded.FormId,
                activeCellFormId = loaded.Session.ActiveCellFormId,
                activeCellEditorId = loaded.Session.ActiveCellEditorId,
                activeCellRestored = loaded.Session.ActiveCellRestored,
                orderedPortals = loaded.PortalLinks.Select(link => new
                {
                    fromCellFormId = link.FromCellFormId,
                    toCellFormId = link.ToCellFormId,
                    fromDoorReferenceFormId = link.FromDoor.ReferenceFormId,
                    toDoorReferenceFormId = link.ToDoor.ReferenceFormId,
                }),
                transitions = transitions.Select(transition => new
                {
                    fromCellFormId = transition.FromCellFormId,
                    toCellFormId = transition.ToCellFormId,
                    fromDoorReferenceFormId = transition.FromDoorReferenceFormId,
                    toDoorReferenceFormId = transition.ToDoorReferenceFormId,
                    arrivalPosition = Vector(transition.ArrivalPosition),
                }),
                activeSet = new
                {
                    policy = "current-cell-plus-direct-portal-neighbors",
                    activeCellFormIds = loaded.ActiveSet.ActiveCellFormIds
                        .OrderBy(value => value, StringComparer.OrdinalIgnoreCase),
                    spaces = loaded.ActiveSet.Snapshot().Select(space => new
                    {
                        cellFormId = space.FormId,
                        space.Active,
                        roots = space.Roots,
                        sourceVisibleRoots = space.SourceVisibleRoots,
                        visibleRoots = space.VisibleRoots,
                        sourceProcessingRoots = space.SourceProcessingRoots,
                        processingRoots = space.ProcessingRoots,
                        collisionObjects = space.CollisionObjects,
                        sourceEnabledCollisionObjects = space.SourceEnabledCollisionObjects,
                        enabledCollisionObjects = space.EnabledCollisionObjects,
                        rigidBodies = space.RigidBodies,
                        sourceFrozenRigidBodies = space.SourceFrozenRigidBodies,
                        frozenRigidBodies = space.FrozenRigidBodies,
                        lights = space.Lights,
                        sourceVisibleLights = space.SourceVisibleLights,
                        visibleLights = space.VisibleLights,
                    }),
                    updates = loaded.ActiveSet.Updates.Select(update => new
                    {
                        currentCellFormId = update.CurrentCellFormId,
                        activeCellFormIds = update.ActiveCellFormIds,
                        suspendedCellFormIds = update.SuspendedCellFormIds,
                    }),
                },
                environmentSet = loaded.EnvironmentSet is null
                    ? null
                    : new
                    {
                        policy = "current-cell-world-environment-plus-owned-exterior-sky",
                        surfaceLightingPolicy = "existing-compiled-cell-lighting-not-switched",
                        activeCellFormId = loaded.EnvironmentSet.ActiveCellFormId,
                        spaces = loaded.EnvironmentSet.Snapshot().Select(space => new
                        {
                            cellFormId = space.CellFormId,
                            space.Active,
                            mode = space.Mode,
                            gameHour = space.GameHour,
                            weatherFormId = space.WeatherFormId,
                            weatherEditorId = space.WeatherEditorId,
                            atmosphereSourceSha256 = space.AtmosphereSourceSha256,
                            cloudsSourceSha256 = space.CloudsSourceSha256,
                            boundCloudTextureLayers = space.BoundCloudTextureLayers,
                        }),
                        updates = loaded.EnvironmentSet.Updates.Select(update => new
                        {
                            cellFormId = update.CellFormId,
                            mode = update.Mode,
                            weatherFormId = update.WeatherFormId,
                            weatherEditorId = update.WeatherEditorId,
                        }),
                    },
                playerTransform = new
                {
                    position = Vector(transform.Origin),
                    rotation = Quaternion(
                        transform.Basis.GetRotationQuaternion().Normalized()),
                },
                save = new
                {
                    path = loaded.Session.SavePath,
                    sha256 = Convert.ToHexString(SHA256.HashData(saveBytes)).ToLowerInvariant(),
                },
                sunny = sunny == default
                    ? null
                    : new
                    {
                        referenceFormId = sunny.ReferenceFormId,
                        sunny.InitiallyDisabled,
                        sunny.ProofEnabled,
                    },
                gameplay = loaded.Session.Report(),
            });
    }

    private static float[] Vector(Vector3 value) => [value.X, value.Y, value.Z];

    private readonly record struct NavigationStall(
        int WaypointIndex,
        int WaypointCount,
        float RemainingMeters,
        Vector3 Target);

    private static float[] Quaternion(Quaternion value) =>
        [value.X, value.Y, value.Z, value.W];
}
