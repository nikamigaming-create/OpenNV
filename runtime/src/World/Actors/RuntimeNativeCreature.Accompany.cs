using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Actors;

internal sealed partial class RuntimeNativeCreature
{
    internal FalloutFollowPackage? FollowCombatSource => _followPackage;

    internal bool PackageAllowsUnprovokedCombat(FalloutFormKey? attackedTarget) =>
        _sandboxPackage?.DefensiveCombat != true && _followPackage?.AllowsUnprovokedCombat(attackedTarget) != false;

    internal static (Vector3 Destination, float Distance) AccompanyApproach(Vector3 actor, Vector3 leader,
        Vector3 goal, float distance) => NativeActorFollowTarget.AccompanyApproach(actor, leader, goal, distance);
}
