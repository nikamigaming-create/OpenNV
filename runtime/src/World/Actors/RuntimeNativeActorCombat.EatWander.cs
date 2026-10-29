using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Actors;

internal sealed partial class RuntimeNativeActorCombat
{
    internal bool EatWanderCanRetry => PackageMovementReady && !OwnsPose && !PackageRoutePending &&
        _routeDoor is null && _pursuitCursor >= _pursuitPath.Length &&
        (_state.PackageMotion?.Wander is not { } request || request.Arrived || PackageRouteFailure is not null);

    internal void AdvanceEatWander(FalloutPluginRecord package, FalloutEatPackage source,
        FalloutActorEatPackageProgress progress, double delta)
    {
        var context = _context ?? throw new NotSupportedException("Eat Wander has no gameplay context.");
        var scope = source.Scope(_world, _state.Reference);
        if (scope.Center is not { } center)
            throw new NotSupportedException("Whole-cell Eat Wander requires its source interior destination owner.");
        var request = _state.PackageMotion?.Package == package.FormKey ? _state.PackageMotion.Wander : null;
        if (request is null || request.Arrived)
            request = _world.BeginPackageWander(_state.Reference, new Vector3(center[0], center[1], center[2]),
                source.Location.Radius, _records.NumericSettings, context.Globals, _skeleton.UnitsToMetres);
        request = AdvancePackageWander(package, request, source.Running, source.WeaponDrawn, delta);
        _state.PackageMotion = _state.PackageMotion! with { EatPackage = progress, Wander = request, Eat = null, Travel = null };
    }
}
