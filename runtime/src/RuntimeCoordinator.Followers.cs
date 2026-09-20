using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    private sealed record DoorFollower(FalloutFormKey Reference, FalloutReferencePlacement Placement,
        FalloutActorPackageMotion? Motion, FalloutActorEngagement? Engagement);

    private IReadOnlyList<DoorFollower> BeginFollowerDoorTransfer(Node3D current, FalloutFormKey cell, FalloutTeleportDestination entry)
    {
        var followers = current.FindChildren("*", "", true, false).OfType<RuntimeNativeCreature>()
            .Where(actor => actor.FollowingPlayer).Select(actor =>
            {
                var key = actor.Appearance.Reference!.Value;
                var state = _nativeReferences!.Get(key);
                return new DoorFollower(key, _nativeReferences.Placement(key), state.PackageMotion, state.Engagement);
            }).ToArray();
        try
        {
            foreach (var follower in followers)
                _nativeReferences!.SetPlacement(follower.Reference, new(cell, (float[])entry.Position.Clone(), (float[])entry.RotationRadians.Clone()));
            return followers;
        }
        catch { RollbackFollowerDoorTransfer(followers); throw; }
    }

    private void RollbackFollowerDoorTransfer(IReadOnlyList<DoorFollower> followers)
    {
        foreach (var follower in followers)
        {
            _nativeReferences!.SetPlacement(follower.Reference, follower.Placement);
            var state = _nativeReferences.Get(follower.Reference);
            state.PackageMotion = follower.Motion;
            state.Engagement = follower.Engagement;
        }
    }

    private void PlaceDoorFollowers(Node3D root, FalloutCellScene scene, IReadOnlyList<FalloutCellDefinition>? cells,
        FalloutTeleportDestination entry, IReadOnlyList<DoorFollower> followers)
    {
        var graph = CellNavigationGraph.LoadOwned(_nativePluginStack!, (cells?.Select(cell => cell.FormKey) ?? [scene.Cell.FormKey]).ToHashSet());
        var units = _configuration.World.GameUnitsToMeters;
        Vector3 Source(Vector3 point) => new Vector3(point.X, -point.Z, point.Y) / units;
        Vector3 World(Vector3 point) => new Vector3(point.X, point.Z, -point.Y) * units;
        var origin = TeleportTransform(entry).Origin;
        var reserved = new List<(Vector3 Point, float Radius)> { (origin, _nativePlayer!.CombatRadius) };
        foreach (var follower in followers)
        {
            var actor = root.FindChildren("*", "", true, false).OfType<RuntimeNativeCreature>()
                .Single(value => value.Appearance.Reference == follower.Reference);
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
            GD.Print($"OPENNV_FOLLOWER_DOOR_ARRIVAL reference={follower.Reference} cell={scene.Cell.FormKey} clearance=native-capsule path=source-NAVM position={actor.GlobalPosition}");
        }
    }
}
