using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Actors;

internal sealed partial class RuntimeNativeCreature
{
    internal bool PackageAllowsUnprovokedCombat(FalloutFormKey? attackedTarget) =>
        _sandboxPackage?.DefensiveCombat != true && _followPackage?.AllowsUnprovokedCombat(attackedTarget) != false;

    private void AdvanceAccompany(FalloutFollowPackage package, Node3D target, bool running, double delta)
    {
        var distance = package.Distance * Skeleton.UnitsToMetres;
        var destination = target.GlobalPosition;
        var stoppingDistance = distance;
        if (target is not RuntimeNativePlayer)
        {
            var combat = RuntimeNativeActorCombat.Find(target) ??
                throw new NotSupportedException("Accompany target has no native actor movement owner.");
            if (combat.PackageDestination is { } goal)
            {
                (destination, stoppingDistance) = AccompanyApproach(GlobalPosition, target.GlobalPosition, goal, distance);
            }
            else if (target is RuntimeNativeNpc { Traveling: true } || combat.PackageMoving)
            {
                _waitingForTarget = package.Target;
                return;
            }
        }
        Combat!.AdvancePackageMotion(_aiPackage!, destination, stoppingDistance, package.Running || running,
            delta, requireArrivalHeight: true);
    }

    internal static (Vector3 Destination, float Distance) AccompanyApproach(Vector3 actor, Vector3 leader,
        Vector3 goal, float distance)
    {
        var stoppingDistance = Math.Max(0, goal.DistanceTo(leader) - distance);
        // A destination-centered radius alone can include a companion far
        // beside or ahead of its leader. Close the actual source distance in
        // that case before resuming the leader's destination approach.
        return actor.DistanceTo(leader) > distance && actor.DistanceTo(goal) <= stoppingDistance
            ? (leader, distance) : (goal, stoppingDistance);
    }
}
