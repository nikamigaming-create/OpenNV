using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

public partial class NativeLocomotionAudit
{
    private async Task CheckOpenDoorFrameCorridor()
    {
        var scene = new Node3D(); AddChild(scene);
        StaticBody3D Box(Vector3 position, Vector3 size)
        {
            var box = new StaticBody3D { Position = position };
            box.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
            scene.AddChild(box); return box;
        }
        Box(new(0, -.5f, -1), new(12, 1, 16));
        Box(new(-3.5f, 1, -2), new(5, 2, .4f));
        Box(new(3.5f, 1, -2), new(5, 2, .4f));
        var frame = Box(new(.85f, 1, -2), new(.2f, 2, .4f));
        var parkedLeaf = Box(new(0, 5, -2), new(2, 2, .4f));
        for (var index = 0; index < 6; index++)
        {
            var height = .12f * (index + 1);
            Box(new(0, height / 2, -3 - index * .4f), new(4, height, .4f));
        }
        Box(new(0, .36f, -6), new(4, .72f, 1.6f));
        var body = CorridorBody(scene, new(0, .1f, 2));
        bool Resident(Vector3 point) => Math.Abs(point.X) < 2 && point.Z is > -6.7f and < 3;
        try
        {
            await SettleCorridorBody(body);
            var pose = body.GlobalTransform;
            var goal = new Vector3(1.4f, .72f, -5.7f);
            var direct = NativeCapsuleNavigation.FirstCorridorContact(body, pose.Origin, [goal]);
            if (direct?.Collider != frame.GetInstanceId())
                throw new InvalidOperationException("The open-door fixture did not retain the intended stationary frame contact.");
            var source = new FalloutFormKey("Fixture.esm", 7);
            var probe = new NativeNavigationProbe(direct, collider =>
                collider == frame.GetInstanceId() || collider == parkedLeaf.GetInstanceId() ? source : null);
            if (new NativeRouteDoorStatus(source, Open: true).RequiresInteraction)
                throw new InvalidOperationException("A settled open frame requested a closed-door activation.");
            var geometry = scene.GetChildren().OfType<StaticBody3D>().Select(node => (node, node.GlobalTransform)).ToArray();
            Vector3[] sourcePath = [new(0, 0, -2.5f), new(0, .72f, -5.3f), goal];
            var forward = NativeCapsuleNavigation.Find(body, pose.Origin, goal, .4f, .32f, Resident,
                probe: probe, corridor: sourcePath);
            if (body.GlobalTransform != pose || forward.Count == 0 || geometry.Any(value => value.node.GlobalTransform != value.GlobalTransform))
                throw new InvalidOperationException("Source-corridor verification moved the query body or opened geometry.");
            await WalkCorridorBody(body, forward, .08f);
            if (!body.IsOnFloor() || body.GlobalPosition.DistanceTo(goal) > .12f)
                throw new InvalidOperationException("The ordinary controller did not execute the open-frame stair corridor.");
            var reversePose = body.GlobalTransform;
            Vector3[] reversePath = [new(0, .72f, -5.3f), new(0, 0, -2.5f), pose.Origin];
            var reverse = NativeCapsuleNavigation.Find(body, reversePose.Origin, pose.Origin, .4f, .32f, Resident, corridor: reversePath);
            if (body.GlobalTransform != reversePose) throw new InvalidOperationException("Reverse source-corridor query moved its body.");
            await WalkCorridorBody(body, reverse, .08f);
            if (!body.IsOnFloor() || body.GlobalPosition.DistanceTo(pose.Origin) > .12f ||
                geometry.Any(value => value.node.GlobalTransform != value.GlobalTransform))
                throw new InvalidOperationException("Reverse corridor did not preserve physical stairs, door leaf and frame.");
            var unavailable = new NativeNavigationProbe(null);
            var refused = false;
            try
            {
                _ = NativeCapsuleNavigation.Find(body, body.GlobalPosition, goal, .4f, .32f, _ => false, 32,
                    probe: unavailable, corridor: sourcePath);
            }
            catch (InvalidOperationException error)
            {
                refused = error.Message.Contains("not-resident", StringComparison.Ordinal) &&
                    unavailable.GuideRejection?.Reason == "source-not-resident";
            }
            if (!refused) throw new InvalidOperationException("A source guide overrode unavailable native residency or lost its failure.");
            GD.Print("OPENNV_NATIVE_SOURCE_CORRIDOR_PASS openFrameContact=true supportedStairs=true controllerBothDirections=true geometryUnchanged=true unavailableRefused=true noQueryMovement=true fixture=synthetic parity=unverified");
        }
        finally { scene.Free(); }
    }

    private async Task CheckSupportedStepHeadroom()
    {
        var scene = new Node3D(); AddChild(scene);
        void Box(Vector3 position, Vector3 size)
        {
            var box = new StaticBody3D { Position = position };
            box.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } }); scene.AddChild(box);
        }
        Box(new(0, -.5f, -1), new(4, 1, 12));
        Box(new(0, .06f, -2), new(4, .12f, 6));
        Box(new(0, 2.18f, -1), new(4, .2f, 12));
        var body = CorridorBody(scene, new(0, .01f, 2));
        bool Resident(Vector3 point) => Math.Abs(point.X) < .5f && Math.Abs(point.Z) < 5;
        try
        {
            await SettleCorridorBody(body);
            var pose = body.GlobalTransform;
            var goal = new Vector3(0, .12f, -4);
            var stepFrom = new Transform3D(body.GlobalBasis, new(0, pose.Origin.Y, 1.35f));
            if (NativeCharacterStep.TryQuery(body, stepFrom.Translated(Vector3.Up * .34f), new(0, 0, -.16f), .4f,
                out _, out var unsupportedReason, out _) || unsupportedReason != "floor-support" || body.GlobalTransform != pose)
                throw new InvalidOperationException("A hypothetical unsupported step bypassed the ordinary mover's floor-support gate.");
            if (!NativeCharacterStep.TryQuery(body, stepFrom, new(0, 0, -.16f), .4f,
                out var stepDestination, out var stepReason, out _) || stepDestination.Y < .11f)
                throw new InvalidOperationException($"The existing mover did not find the fixture's supported landing: {stepReason}.");
            using (var maximumLift = new PhysicsTestMotionParameters3D
            { From = stepFrom, Motion = Vector3.Up * (.4f + body.SafeMargin * 4), Margin = body.SafeMargin, MaxCollisions = 4 })
            using (var hit = new PhysicsTestMotionResult3D())
                if (!PhysicsServer3D.BodyTestMotion(body.GetRid(), maximumLift, hit))
                    throw new InvalidOperationException("The fixture did not reject maximum lift while admitting the mover's actual landing height.");
            var probe = new NativeNavigationProbe(NativeCapsuleNavigation.FirstCorridorContact(body, pose.Origin, [goal]));
            var route = NativeCapsuleNavigation.Find(body, pose.Origin, goal, .4f, .32f, Resident, probe: probe, corridor: [goal]);
            if (body.GlobalTransform != pose) throw new InvalidOperationException("Supported-height query moved the controller.");
            await WalkCorridorBody(body, route, .05f);
            if (!body.IsOnFloor() || body.GlobalPosition.DistanceTo(goal) > .08f)
                throw new InvalidOperationException("Actual controller could not execute the supported step below maximum-height headroom.");
            var reversePose = body.GlobalTransform;
            var reverse = NativeCapsuleNavigation.Find(body, reversePose.Origin, pose.Origin, .4f, .32f, Resident, corridor: [pose.Origin]);
            if (body.GlobalTransform != reversePose) throw new InvalidOperationException("Supported-height reverse query moved the controller.");
            await WalkCorridorBody(body, reverse, .05f);
            if (!body.IsOnFloor() || body.GlobalPosition.DistanceTo(pose.Origin) > .08f)
                throw new InvalidOperationException("Supported-height reverse query did not match actual movement.");
            GD.Print("OPENNV_NATIVE_SUPPORTED_STEP_QUERY_PASS actualLandingHeight=true maximumLiftNotRequired=true unsupportedStartRefused=true controllerBothDirections=true noQueryMovement=true fixture=synthetic parity=unverified");
        }
        finally { scene.Free(); }
    }

    private CharacterBody3D CorridorBody(Node3D scene, Vector3 position)
    {
        var body = new CharacterBody3D { Position = position, FloorSnapLength = .32f, FloorMaxAngle = FloorAngle };
        body.AddChild(new CollisionShape3D
        {
            Position = Vector3.Up * .9f,
            Shape = new CapsuleShape3D { Height = 1.8f, Radius = .32f }
        });
        scene.AddChild(body); return body;
    }

    private async Task SettleCorridorBody(CharacterBody3D body)
    {
        for (var frame = 0; frame < 8; frame++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            body.Velocity = Vector3.Down; body.MoveAndSlide();
        }
    }

    private async Task WalkCorridorBody(CharacterBody3D body, IReadOnlyList<Vector3> route, float arrival)
    {
        foreach (var waypoint in route)
        {
            var maximumFrames = checked((int)Math.Ceiling(body.GlobalPosition.DistanceTo(waypoint) / GetPhysicsProcessDeltaTime()) + 90);
            for (var frame = 0; frame < maximumFrames; frame++)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                var offset = waypoint - body.GlobalPosition; offset.Y = 0;
                if (offset.Length() <= arrival) break;
                if (frame == maximumFrames - 1) throw new InvalidOperationException("Controller did not reach its capsule-verified source waypoint.");
                var delta = (float)GetPhysicsProcessDeltaTime();
                var velocity = offset.Normalized();
                velocity.Y = body.IsOnFloor() ? Math.Min(body.Velocity.Y, 0) : body.Velocity.Y - 9.81f * delta;
                body.Velocity = velocity;
                if (NativeCharacterStep.TryStep(body, offset.Normalized() * delta, .4f)) body.Velocity = Vector3.Down * .01f;
                body.MoveAndSlide();
            }
        }
    }
}
