using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Actors;

internal static class NativeActorFollowTarget
{
    internal static void ValidateReference(FalloutPluginStack records, FalloutFormKey target)
    {
        if (target != records.RuntimeFormKey(0x14) && records.GetEffective(target).Signature is not ("ACHR" or "ACRE"))
            throw new NotSupportedException("Follower target has no actor reference owner.");
    }

    internal static FalloutFormKey Cell(FalloutPluginStack records, FalloutReferenceWorld world,
        RuntimeNativeActorCombat process, FalloutFormKey target)
    {
        if (target == records.RuntimeFormKey(0x14))
            return process.PackagePlayerCell ?? throw new NotSupportedException("Follower player target has no current CELL owner.");
        ValidateReference(records, target);
        return world.Placement(target).Cell;
    }

    internal static Node3D? Node(FalloutPluginStack records, FalloutReferenceWorld world,
        RuntimeNativeActorCombat process, FalloutFormKey target, Func<FalloutFormKey, Node3D?>? resolve)
    {
        if (!world.IsEnabled(target)) return null;
        if (target == records.RuntimeFormKey(0x14))
            return process.PackagePlayer is { CollisionResident: true } player ? player : null;
        _ = Cell(records, world, process, target);
        var node = (resolve ?? throw new NotSupportedException("Follower has no resident actor target resolver."))(target);
        if (node is null || !GodotObject.IsInstanceValid(node) || !node.IsInsideTree() || node.IsQueuedForDeletion()) return null;
        if (node is RuntimeNativeNpc npc && npc.Appearance.Reference == target ||
            node is RuntimeNativeCreature creature && creature.Appearance.Reference == target) return node;
        throw new InvalidDataException("Follower target resolver returned a different actor identity.");
    }

    internal static (Vector3 Destination, float Distance, bool Running)? RetainedPlan(
        FalloutReferenceWorld world, FalloutFollowPackage package, float unitsToMetres)
    {
        var position = world.CompletedFollowTargetPosition(package.Target);
        if (position is null) return null;
        // Retained motion positions are already actual global Godot coordinates.
        // A completed Travel is stopped; it does not supply a future destination.
        var target = new Vector3(position[0], position[1], position[2]);
        return (target, package.Distance * unitsToMetres, package.Running);
    }

    internal static (Vector3 Destination, float Distance, bool Running)? Plan(FalloutFollowPackage package,
        Vector3 actor, Node3D target, float unitsToMetres)
    {
        var distance = package.Distance * unitsToMetres;
        var running = target switch
        {
            RuntimeNativePlayer player => player.Activity.Running,
            RuntimeNativeNpc npc => npc.Activity.Running,
            RuntimeNativeCreature creature => creature.Activity.Running,
            _ => throw new NotSupportedException("Follower target has no native actor activity owner.")
        };
        if (!package.Accompany || target is RuntimeNativePlayer)
            return (target.GlobalPosition, distance, package.Running || running);
        var process = RuntimeNativeActorCombat.Find(target) ??
            throw new NotSupportedException("Accompany target has no native movement owner.");
        if (process.PackageDestination is { } goal)
        {
            var approach = AccompanyApproach(actor, target.GlobalPosition, goal, distance);
            return (approach.Destination, approach.Distance, package.Running || running);
        }
        if (target is RuntimeNativeNpc { Traveling: true } || process.PackageMoving) return null;
        return (target.GlobalPosition, distance, package.Running || running);
    }

    internal static (Vector3 Destination, float Distance) AccompanyApproach(Vector3 actor, Vector3 leader,
        Vector3 goal, float distance)
    {
        var stoppingDistance = Math.Max(0, goal.DistanceTo(leader) - distance);
        return actor.DistanceTo(leader) > distance && actor.DistanceTo(goal) <= stoppingDistance
            ? (leader, distance) : (goal, stoppingDistance);
    }
}
