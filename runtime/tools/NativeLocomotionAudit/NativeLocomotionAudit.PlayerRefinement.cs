using Godot;
using OpenNV.Runtime.World.Cells;

public partial class NativeLocomotionAudit
{
    private async Task CheckPlayerHalfSpacingRefinement()
    {
        var scene = new Node3D(); AddChild(scene);
        void Box(Vector3 position, Vector3 size)
        {
            var box = new StaticBody3D { Position = position };
            box.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } }); scene.AddChild(box);
        }
        Box(new(0, -.5f, 0), new(6, 1, 10));
        Box(new(-1.615f, 1, 0), new(2.77f, 2, 1));
        Box(new(1.775f, 1, 0), new(2.45f, 2, 1));
        var body = CorridorBody(scene, new(0, .05f, 1.5f));
        bool Resident(Vector3 point) => Math.Abs(point.X) < .6f && Math.Abs(point.Z) < 4;
        try
        {
            await SettleCorridorBody(body);
            var start = body.GlobalTransform;
            var geometry = scene.GetChildren().OfType<StaticBody3D>().Select(node => (node, node.GlobalTransform)).ToArray();
            var goal = new Vector3(.16f, 0, -1.5f);
            var spacing = NativeCapsuleNavigation.RefinementSpacing(.32f, .32f, 1);
            var route = NativeCapsuleNavigation.FindRefined(body, start.Origin, goal, .4f, .32f, spacing, Resident);
            if (body.GlobalTransform != start || route.CoarseError is null || route.Spacing != .16f || body.Scale != Vector3.One)
                throw new InvalidOperationException("Adult player passage did not require a genuinely distinct half-spacing lattice.");
            await WalkCorridorBody(body, route.Path, .025f);
            if (!body.IsOnFloor() || body.GlobalPosition.DistanceTo(goal) > .04f)
                throw new InvalidOperationException("Ordinary adult controller did not execute the half-spacing passage.");
            var reversePose = body.GlobalTransform;
            var reverse = NativeCapsuleNavigation.FindRefined(body, reversePose.Origin, start.Origin, .4f, .32f, spacing, Resident);
            if (body.GlobalTransform != reversePose) throw new InvalidOperationException("Reverse refinement moved its query body.");
            await WalkCorridorBody(body, reverse.Path, .025f);
            if (!body.IsOnFloor() || body.GlobalPosition.DistanceTo(start.Origin) > .04f)
                throw new InvalidOperationException("Ordinary adult controller did not execute the reverse passage.");
            var pose = body.GlobalTransform;
            foreach (var blocked in new[] { new Vector3(.4f, 0, 0), goal + Vector3.Up * 4 })
            {
                var refused = false;
                try { _ = NativeCapsuleNavigation.FindRefined(body, pose.Origin, blocked, .4f, .32f, spacing, Resident); }
                catch (InvalidOperationException) { refused = true; }
                if (!refused || body.GlobalTransform != pose)
                    throw new InvalidOperationException("Finer lattice admitted an occupied or different-floor goal, or moved its body.");
            }
            var unloaded = false;
            try { _ = NativeCapsuleNavigation.FindRefined(body, pose.Origin, goal, .4f, .32f, spacing, _ => false); }
            catch (InvalidOperationException) { unloaded = true; }
            if (!unloaded || body.GlobalTransform != pose || geometry.Any(value => value.node.GlobalTransform != value.GlobalTransform))
                throw new InvalidOperationException("Half-spacing refinement changed physical ownership or admitted unloaded collision.");
            GD.Print("OPENNV_NATIVE_PLAYER_HALF_SPACING_PASS coarse=.32 refined=.16 maxNodesPerSearch=1200 adultCapsule=true coarseRefused=true controllerBothDirections=true occupiedGoalRefused=true otherFloorRefused=true unloadedRefused=true geometryUnchanged=true queryBodyUnmoved=true fixture=synthetic gameplay=separate parity=unverified");
        }
        finally { scene.Free(); }
    }
}
