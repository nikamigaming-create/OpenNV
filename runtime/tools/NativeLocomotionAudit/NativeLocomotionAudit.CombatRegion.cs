using Godot;
using OpenNV.Runtime.World.Cells;

public partial class NativeLocomotionAudit
{
    private static void CheckCombatRegionGoalEdge(CharacterBody3D body, Vector3 goal, float radius)
    {
        var pose = body.GlobalTransform;
        var start = new Vector3(goal.X, pose.Origin.Y, goal.Z + .8f);
        // One expanded node permits the goal edge but no lattice neighbor.
        // The exact destination is occupied; its near region is supported.
        foreach (var sourceRegion in new[] { false, true })
        {
            var route = NativeCapsuleNavigation.Find(body, start, goal, .4f, .66f, _ => true, 1,
                targetRadius: sourceRegion ? 0 : radius,
                arrival: sourceRegion ? point => point.DistanceTo(goal) <= radius : null);
            if (route.Count == 0 || route[^1].DistanceTo(goal) > radius || body.GlobalTransform != pose)
                throw new InvalidOperationException("Combat goal edge discarded its supported region or moved its query body.");
        }
        var refused = false;
        try
        {
            _ = NativeCapsuleNavigation.Find(body, start, goal, .4f, .66f, _ => true, 1,
                arrival: point => point.DistanceTo(goal) <= radius && point.Y > 2);
        }
        catch (InvalidOperationException) { refused = true; }
        if (!refused || body.GlobalTransform != pose)
            throw new InvalidOperationException("Combat goal edge bypassed its source floor predicate.");
        GD.Print("OPENNV_COMBAT_REGION_GOAL_EDGE_PASS referenceRegion=true sourceRegion=true occupiedCentre=true " +
            "oneNode=true wrongFloorRefused=true queryBodyUnmoved=true fixture=synthetic parity=unverified");
    }
}
