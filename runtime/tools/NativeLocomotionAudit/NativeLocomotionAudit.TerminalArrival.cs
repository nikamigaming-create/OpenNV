using Godot;
using OpenNV.Runtime.World.Cells;

public partial class NativeLocomotionAudit
{
    private async Task CheckNativeTerminalArrivalRegion()
    {
        var scene = new Node3D(); AddChild(scene);
        StaticBody3D Box(Vector3 position, Vector3 size)
        {
            var box = new StaticBody3D { Position = position };
            box.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
            scene.AddChild(box); return box;
        }
        Box(new(0, -.5f, 0), new(16, 1, 16));
        var wall = Box(new(0, 1, -2.322f), new(4, 2, .2f));
        var body = new CharacterBody3D { Position = new(0, .05f, 1), FloorSnapLength = .33f, FloorMaxAngle = FloorAngle };
        body.AddChild(new CollisionShape3D
        {
            Position = Vector3.Up * .95f,
            Shape = new CapsuleShape3D { Radius = .33f, Height = 1.9f }
        });
        scene.AddChild(body);
        try
        {
            await SettleCorridorBody(body);
            var original = body.GlobalTransform;
            var geometry = scene.GetChildren().OfType<StaticBody3D>().Select(node => (node, node.GlobalTransform)).ToArray();
            var portal = Vector3.Zero;
            var goal = new Vector3(0, 0, -2);
            const float radius = .25f;
            var prefix = NativeCapsuleNavigation.Intent(original.Origin, [portal, goal], goal, radius);
            if (prefix.ReferenceApproach || prefix.ArrivalRadius != 0 || prefix.Target != portal || prefix.Resume != 1)
                throw new InvalidOperationException("A long final goal region skipped its mandatory source portal.");
            var prefixRoute = NativeCapsuleNavigation.FindRefined(body, original.Origin, prefix.Target, .4f, .66f, .165f,
                _ => true, 512, corridor: prefix.Corridor);
            if (body.GlobalTransform != original) throw new InvalidOperationException("Terminal prefix query moved its controller.");
            await WalkTerminalArrivalRoute(body, prefixRoute.Path);
            if (!body.IsOnFloor() || body.GlobalPosition.DistanceTo(portal) > .01f)
                throw new InvalidOperationException("Ordinary movement did not reach the required source portal.");
            var pose = body.GlobalTransform;
            var tail = NativeCapsuleNavigation.Intent(pose.Origin, [goal], goal, radius);
            if (!tail.ReferenceApproach || tail.ArrivalRadius != radius || tail.Target != goal || tail.Corridor.Count != 0)
                throw new InvalidOperationException("Replanning at the source portal discarded the existing long terminal region.");
            using var placement = new NativeCapsulePlacementQuery(body);
            var supported = new Vector3(goal.X, pose.Origin.Y, goal.Z + .245f);
            if (!placement.CanStand(supported) || placement.CanStand(goal) || supported.DistanceTo(goal) > radius)
                throw new InvalidOperationException("Terminal fixture lost its supported 0.245m endpoint or admitted the occupied exact goal.");
            var route = NativeCapsuleNavigation.FindRefined(body, pose.Origin, tail.Target, .4f, .66f, .165f,
                _ => true, 512, targetRadius: tail.ArrivalRadius, corridor: tail.Corridor);
            if (body.GlobalTransform != pose || route.Path[^1].DistanceTo(goal) > radius || !placement.CanStand(route.Path[^1]))
                throw new InvalidOperationException("Long terminal query changed its radius, moved its body or selected an unsupported endpoint.");
            void Refuse(NativeNavigationIntent intent, Func<Vector3, bool> resident, string boundary)
            {
                var refused = false;
                try
                {
                    _ = NativeCapsuleNavigation.FindRefined(body, pose.Origin, intent.Target, .4f, .66f, .165f,
                        resident, 512, targetRadius: intent.ArrivalRadius, corridor: intent.Corridor);
                }
                catch (InvalidOperationException) { refused = true; }
                if (!refused || body.GlobalTransform != pose)
                    throw new InvalidOperationException("Terminal arrival bypassed " + boundary + " or moved its query body.");
            }
            Refuse(NativeCapsuleNavigation.Intent(pose.Origin, [goal], goal, 0), _ => true, "the declared exact goal");
            Refuse(NativeCapsuleNavigation.Intent(pose.Origin, [goal], goal + Vector3.Right * .05f, radius),
                _ => true, "a mismatched projected/reference goal");
            var occupied = new Vector3(wall.Position.X, 0, wall.Position.Z);
            Refuse(NativeCapsuleNavigation.Intent(pose.Origin, [occupied], occupied, radius), _ => true, "an occupied final region");
            var near = new Vector3(0, 0, .1f);
            var blockedPrefix = NativeCapsuleNavigation.Intent(pose.Origin, [occupied, near], near, radius);
            var upperPrefix = NativeCapsuleNavigation.Intent(pose.Origin, [new(0, 3, 0), near], near, radius);
            if (blockedPrefix.ReferenceApproach || blockedPrefix.ArrivalRadius != 0 || blockedPrefix.Target != occupied ||
                upperPrefix.ReferenceApproach || upperPrefix.ArrivalRadius != 0 || upperPrefix.Target != new Vector3(0, 3, 0))
                throw new InvalidOperationException("A nearby final region erased a blocked or elevated intermediate source waypoint.");
            Refuse(blockedPrefix, _ => true, "a blocked intermediate source portal");
            Refuse(upperPrefix, _ => true, "an unsupported intermediate source floor");
            Refuse(tail, _ => false, "unknown native collision residency");
            await WalkTerminalArrivalRoute(body, route.Path);
            if (!body.IsOnFloor() || body.GlobalPosition.DistanceTo(goal) > radius)
                throw new InvalidOperationException("Ordinary controller movement did not reach the existing terminal region.");
            var returned = body.GlobalTransform;
            var reverse = NativeCapsuleNavigation.FindRefined(body, returned.Origin, original.Origin, .4f, .66f, .165f,
                _ => true, 512, corridor: [portal, original.Origin]);
            if (body.GlobalTransform != returned) throw new InvalidOperationException("Reverse terminal query moved its body.");
            await WalkTerminalArrivalRoute(body, reverse.Path);
            if (!body.IsOnFloor() || body.GlobalPosition.DistanceTo(original.Origin) > .01f ||
                geometry.Any(value => value.node.GlobalTransform != value.GlobalTransform))
                throw new InvalidOperationException("Reverse terminal movement lost support or changed the original collision geometry.");
            GD.Print($"OPENNV_NATIVE_TERMINAL_ARRIVAL_REGION_PASS radius={radius} supportedDistance={supported.DistanceTo(goal)} acceptedDistance={route.Path[^1].DistanceTo(goal)} " +
                "portalThenRegion=true originalRadius=true exactGoalRefused=true mismatchedGoalRefused=true occupiedRegionRefused=true blockedPrefixRefused=true " +
                "otherFloorPrefixRefused=true unknownGeometryRefused=true controllerBothDirections=true geometryUnchanged=true queryBodyUnmoved=true " +
                "maxNodesPerSearch=512 fixture=synthetic gameplay=separate parity=unverified");
        }
        finally { scene.Free(); }
    }

    private async Task WalkTerminalArrivalRoute(CharacterBody3D body, IReadOnlyList<Vector3> route)
    {
        foreach (var waypoint in route)
        {
            var maximumFrames = checked((int)Math.Ceiling(body.GlobalPosition.DistanceTo(waypoint) / GetPhysicsProcessDeltaTime()) + 90);
            for (var frame = 0; frame < maximumFrames; frame++)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                var offset = waypoint - body.GlobalPosition; offset.Y = 0;
                if (offset.Length() <= .001f) break;
                if (frame == maximumFrames - 1) throw new InvalidOperationException("Ordinary movement did not reach its checked terminal waypoint.");
                var delta = (float)GetPhysicsProcessDeltaTime();
                var motion = NativeCapsuleNavigation.RouteMotion(body.GlobalPosition, offset.Normalized() * delta, waypoint);
                body.Velocity = motion / delta + Vector3.Up *
                    (body.IsOnFloor() ? Math.Min(body.Velocity.Y, 0) : body.Velocity.Y - 9.81f * delta);
                if (NativeCharacterStep.TryStep(body, motion, .4f)) body.Velocity = Vector3.Down * .01f;
                body.MoveAndSlide();
            }
        }
    }
}
