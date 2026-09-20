using Godot;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Actors;

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
        NativeCollisionResident, () => _nativeOpeningStageDriver!.PlayerLevel, _nativeGlobals!,
        _configuration.Player.StepHeightMeters, _configuration.Simulation.GravityMetersPerSecondSquared);
}
