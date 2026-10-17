using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    private sealed record TravelFollower(FalloutFormKey Reference, FalloutReferencePlacement Placement,
        FalloutActorPackageMotion? Motion, FalloutActorEngagement? Engagement, FalloutActorHitReaction? HitReaction);

    private IReadOnlyList<TravelFollower> BeginFollowerTransfer(Node3D current, FalloutReferencePlacement entry)
    {
        var followers = current.FindChildren("*", "", true, false).OfType<RuntimeNativeCreature>()
            .Where(actor => actor.FollowingPlayer).Select(actor =>
            {
                var key = actor.Appearance.Reference!.Value;
                var state = _nativeReferences!.Get(key);
                return new TravelFollower(key, _nativeReferences.Placement(key), state.PackageMotion, state.Engagement, state.HitReaction);
            }).ToArray();
        try
        {
            foreach (var follower in followers)
                _nativeReferences!.SetPlacement(follower.Reference, new(entry.Cell, (float[])entry.Position.Clone(), (float[])entry.RotationRadians.Clone()));
            return followers;
        }
        catch { RollbackFollowerTransfer(followers); throw; }
    }

    private void RollbackFollowerTransfer(IReadOnlyList<TravelFollower> followers)
    {
        foreach (var follower in followers)
        {
            _nativeReferences!.SetPlacement(follower.Reference, follower.Placement);
            var state = _nativeReferences.Get(follower.Reference);
            state.PackageMotion = follower.Motion;
            state.Engagement = follower.Engagement;
            state.HitReaction = follower.HitReaction;
        }
    }

    private async Task MoveNativePlayerWithFollowers(FalloutReferencePlacement placement, Transform3D transform)
    {
        var root = _nativeCurrentCellRoot!;
        var active = _nativeActiveCell!;
        var mode = root.ProcessMode;
        var cells = active.Cell.Worldspace is { } world ? ResolveExterior(world, placement.Position).Cells : null;
        var actors = root.FindChildren("*", "", true, false).OfType<RuntimeNativeCreature>()
            .Where(actor => actor.FollowingPlayer).ToDictionary(actor => actor.Appearance.Reference!.Value,
                actor => (Actor: actor, Transform: actor.GlobalTransform, Velocity: actor.Velocity));
        var followers = BeginFollowerTransfer(root, placement);
        (CollisionObject3D Body, CollisionObject3D.DisableModeEnum Mode)[] collision = [];
        try
        {
            collision = await PrepareFollowerArrival(root, root, active, cells, placement, transform, followers);
            _nativePlayer!.Teleport(transform);
        }
        catch
        {
            RollbackFollowerTransfer(followers);
            foreach (var (actor, previous, velocity) in actors.Values)
            {
                actor.GlobalTransform = previous;
                actor.Velocity = velocity;
            }
            throw;
        }
        finally
        {
            foreach (var (body, previous) in collision) body.DisableMode = previous;
            root.ProcessMode = mode;
        }
    }

    private async Task<(CollisionObject3D Body, CollisionObject3D.DisableModeEnum Mode)[]> PrepareFollowerArrival(
        Node3D current, Node3D root, FalloutCellScene scene, IReadOnlyList<FalloutCellDefinition>? cells,
        FalloutReferencePlacement entry, Transform3D transform, IReadOnlyList<TravelFollower> followers)
    {
        if (followers.Count == 0) return [];
        current.ProcessMode = ProcessModeEnum.Disabled;
        var references = followers.Select(follower => follower.Reference).ToHashSet();
        foreach (var actor in root.FindChildren("*", "", true, false).OfType<RuntimeNativeCreature>()
            .Where(actor => actor.Appearance.Reference is { } reference && references.Contains(reference)))
            actor.Combat!.PreparePortalArrival();
        // Keep destination collision in physics while gameplay remains stopped.
        var collision = root.FindChildren("*", "", true, false).OfType<CollisionObject3D>()
            .Select(body => (Body: body, Mode: body.DisableMode)).ToArray();
        foreach (var (body, _) in collision) body.DisableMode = CollisionObject3D.DisableModeEnum.KeepActive;
        try
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            PlaceTravelFollowers(root, scene, cells, entry, transform, followers);
            return collision;
        }
        catch
        {
            foreach (var (body, mode) in collision) body.DisableMode = mode;
            throw;
        }
    }

    private void PlaceTravelFollowers(Node3D root, FalloutCellScene scene, IReadOnlyList<FalloutCellDefinition>? cells,
        FalloutReferencePlacement entry, Transform3D transform, IReadOnlyList<TravelFollower> followers)
    {
        var graph = CellNavigationGraph.LoadOwned(_nativePluginStack!, (cells?.Select(cell => cell.FormKey) ?? [scene.Cell.FormKey]).ToHashSet());
        var units = _configuration.World.GameUnitsToMeters;
        Vector3 Source(Vector3 point) => new Vector3(point.X, -point.Z, point.Y) / units;
        Vector3 World(Vector3 point) => new Vector3(point.X, point.Z, -point.Y) * units;
        var origin = transform.Origin;
        var reserved = new List<(Vector3 Point, float Radius)> { (origin, _nativePlayer!.CombatRadius) };
        var actors = root.FindChildren("*", "", true, false).OfType<RuntimeNativeCreature>()
            .Where(actor => actor.Appearance.Reference is not null).ToDictionary(actor => actor.Appearance.Reference!.Value);
        foreach (var follower in followers)
        {
            var actor = actors[follower.Reference];
            var radius = actor.Combat!.PreparePortalArrival();
            using var clearance = new NativeCapsulePlacementQuery(actor);
            var candidates = Enumerable.Range(1, 4).SelectMany(ring => Enumerable.Range(0, 12).Select(index =>
                graph.FindNearestPoint(Source(origin + new Vector3(MathF.Cos(index * MathF.Tau / 12), 0,
                    MathF.Sin(index * MathF.Tau / 12)) * (radius * 2 + .5f) * ring))))
                .Concat(graph.CandidatePoints.Where(point => World(point).DistanceSquaredTo(origin) < 36))
                .Distinct().OrderBy(point => World(point).DistanceSquaredTo(origin));
            Vector3? selected = null;
            foreach (var candidate in candidates)
            {
                var point = World(candidate);
                if (point.DistanceSquaredTo(origin) > 36 || reserved.Any(item =>
                    new Vector2(point.X - item.Point.X, point.Z - item.Point.Z).Length() < radius + item.Radius + actor.SafeMargin * 4) ||
                    !clearance.CanStand(point)) continue;
                try
                {
                    var path = graph.FindPath(Source(origin), candidate, portal => clearance.CanStand(World(portal)));
                    if (path.Count == 0) continue;
                }
                catch (InvalidOperationException) { continue; }
                selected = point;
                break;
            }
            if (selected is not { } arrival) throw new NotSupportedException($"Follower {follower.Reference} has no source-NAVM arrival with native capsule clearance.");
            actor.GlobalPosition = arrival + Vector3.Up * actor.SafeMargin * 4;
            var source = Source(actor.GlobalPosition);
            _nativeReferences!.SetPlacement(follower.Reference, new(scene.Cell.FormKey, [source.X, source.Y, source.Z], (float[])entry.RotationRadians.Clone()));
            reserved.Add((arrival, radius));
            GD.Print($"OPENNV_FOLLOWER_ARRIVAL reference={follower.Reference} cell={scene.Cell.FormKey} clearance=native-capsule path=source-NAVM position={actor.GlobalPosition}");
        }
    }
}
