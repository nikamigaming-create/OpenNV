using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    private NativeActorCombatContext? _nativeCombatContext;
    private NativeActorCombatContext NativeCombatContext => _nativeCombatContext ??= new(
        () => _nativeDoorLoading || _nativeOpeningStageDriver is null ? null : _nativePlayer,
        () => _nativeOpeningStageDriver!.Vitals,
        (damage, part) => _nativeOpeningStageDriver!.DamagePlayer(damage, part),
        (from, to) => FindNativeNavigationRoute(new(from.X, from.Y, from.Z), new(to.X, to.Y, to.Z), false)
            .Select(point => new Vector3(point.X, point.Y, point.Z)).ToArray(),
        NativeCollisionResident, () => _nativeOpeningStageDriver?.PlayerLevel ?? _nativeOpeningRestore?.State.Vitals?.Level ?? 1, _nativeGlobals!,
        _configuration.Player.StepHeightMeters, _configuration.Simulation.GravityMetersPerSecondSquared,
        (actor, name) =>
        {
            if (_nativeReferenceEvents?.DispatchActorEvent(actor, name) != true)
                throw new InvalidOperationException($"Actor event {actor}/{name} has no resident owner.");
        }, () => _nativeActiveCell?.Cell.FormKey, _configuration.Player.MaximumWalkableSlopeDegrees,
        (actor, collider, activate) => _nativeReferenceEvents?.RouteDoor(actor, collider, activate) ??
            new(null, Error: "NPC route door has no resident activation owner."),
        collider => _nativeReferenceEvents?.CollisionReference(collider),
        (actor, door) => (_nativeReferenceEvents ?? throw new NotSupportedException("NPC portal has no resident event owner."))
            .RequestNpcPortal(actor, door), _configuration.Player.ActivationDistanceMeters,
        FindNativeActorNavigationRoute);

    private NativeActorNavigationRoute FindNativeActorNavigationRoute(Vector3 from, Vector3 to, float radius)
    {
        EnsureNativeBotNavigation();
        var units = _configuration.World.GameUnitsToMeters;
        Vector3 Source(Vector3 point) => new Vector3(point.X, -point.Z, point.Y) / units;
        Vector3 World(Vector3 point) => new Vector3(point.X, point.Z, -point.Y) * units;
        var path = _botNavigation!.FindPath(Source(from), Source(to), destinationRadiusGameUnits: radius / units);
        var points = path.Select(World).ToArray();
        if (radius <= 0) return new(points);
        var region = _botNavigation.ArrivalRegion(Source(from), Source(to), path[^1], radius / units,
            .01f / units, NativeCapsuleNavigation.SourceHeightTolerance / units);
        points = NativeCapsuleNavigation.TerminalRegionCorridor(points, point => region(Source(point)));
        return new(points, new(to, points[^1], radius), point => region(Source(point)));
    }
}
