using Godot;
using OpenNV.Runtime.World.Cells;

public partial class NativeLocomotionAudit
{
    private async Task CheckRouteSmoothing()
    {
        await CheckSmoothFloor("open");
        await CheckSmoothFloor("gap");
        await CheckSmoothFloor("steps");
        GD.Print("OPENNV_NATIVE_ROUTE_SMOOTHING_PASS straightRoute=true floorGapRetained=true stepHeightsRetained=true " +
            "nativeControllerArrival=true queryPoseUnchanged=true fixture=synthetic parity=unverified");
    }

    private async Task CheckSmoothFloor(string kind)
    {
        var scene = new Node3D(); AddChild(scene);
        void Box(Vector3 position, Vector3 size)
        {
            var box = new StaticBody3D { Position = position };
            box.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } }); scene.AddChild(box);
        }
        if (kind == "gap")
        {
            Box(new(0, -.1f, 1.5f), new(6, .2f, 3));
            Box(new(0, -.1f, -2.5f), new(6, .2f, 3));
            Box(new(2.5f, -.1f, -.5f), new(1, .2f, 1));
        }
        else
        {
            Box(new(0, -.1f, 0), new(12, .2f, 12));
            if (kind == "steps")
            {
                Box(new(1.5f, .1f, 0), new(3, .2f, 4));
                Box(new(2, .3f, 0), new(2, .2f, 4));
            }
        }
        var start = kind == "gap" ? new Vector3(0, .1f, 2) : new Vector3(-2, .1f, 0);
        var body = new CharacterBody3D { Position = start, FloorSnapLength = .3f, FloorMaxAngle = FloorAngle };
        body.AddChild(new CollisionShape3D { Position = new(0, .9f, 0), Shape = new CapsuleShape3D { Height = 1.8f, Radius = .3f } });
        scene.AddChild(body);
        try
        {
            for (var frame = 0; frame < 8; frame++)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                body.Velocity = Vector3.Down; body.MoveAndSlide();
            }
            var before = body.GlobalTransform;
            var target = kind == "gap" ? new Vector3(0, before.Origin.Y, -2) :
                kind == "steps" ? new Vector3(2, .4f, 0) : new Vector3(2, before.Origin.Y, -3.1f);
            var route = NativeCapsuleNavigation.Find(body, before.Origin, target, .4f, .3f, _ => true);
            if (body.GlobalTransform != before || kind == "open" && route.Count != 1 ||
                kind == "gap" && !route.Any(point => point.X > 1.8f) ||
                kind == "steps" && (route.Count < 3 || !route.Any(point => point.Y > .1f && point.Y < .3f)))
                throw new InvalidDataException($"Route smoothing lost supported {kind} geometry or retained lattice steering: " + string.Join(";", route));
            var cursor = 0;
            for (var frame = 0; frame < 900 && cursor < route.Count; frame++)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                while (cursor < route.Count && new Vector2(route[cursor].X - body.GlobalPosition.X,
                    route[cursor].Z - body.GlobalPosition.Z).Length() <= body.SafeMargin * 8) cursor++;
                if (cursor == route.Count) break;
                var delta = (float)GetPhysicsProcessDeltaTime();
                var motion = NativeCapsuleNavigation.RouteMotion(body.GlobalPosition, Vector3.Forward * delta * 2, route[cursor]);
                body.Velocity = motion / delta + Vector3.Up *
                    (body.IsOnFloor() ? Math.Min(body.Velocity.Y, 0) : body.Velocity.Y - 9.81f * delta);
                if (NativeCharacterStep.TryStep(body, motion, .4f)) body.Velocity = Vector3.Down * .01f;
                body.MoveAndSlide();
                if (body.GlobalPosition.Y < before.Origin.Y - .3f)
                    throw new InvalidDataException($"Smoothed {kind} route lost its native floor at {body.GlobalPosition}, " +
                        $"waypoint={route[cursor]}, route=" + string.Join(";", route));
            }
            if (!body.IsOnFloor() || body.GlobalPosition.DistanceTo(target) > .03f)
                throw new InvalidDataException($"Smoothed {kind} route was not executable by its native controller: {body.GlobalPosition}.");
        }
        finally { scene.Free(); }
    }
}
