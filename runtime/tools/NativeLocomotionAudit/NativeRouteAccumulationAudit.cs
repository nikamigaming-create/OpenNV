using Godot;
using OpenNV.Runtime.World.Cells;

public partial class NativeLocomotionAudit
{
    private async Task CheckRouteAccumulation()
    {
        var scene = new Node3D(); AddChild(scene);
        void Box(Vector3 position, Vector3 size)
        {
            var box = new StaticBody3D { Position = position };
            box.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } }); scene.AddChild(box);
        }
        Box(new(0, -.1f, 0), new(12, .2f, 12));
        Box(new(0, 1.5f, 0), new(.6f, 3, 2));
        var body = new CharacterBody3D { Position = new(-2, .1f, 0), FloorSnapLength = .3f, FloorMaxAngle = FloorAngle };
        body.AddChild(new CollisionShape3D { Position = new(0, .9f, 0), Shape = new CapsuleShape3D { Radius = .3f, Height = 1.8f } });
        scene.AddChild(body);
        try
        {
            for (var frame = 0; frame < 12; frame++)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                body.Velocity = Vector3.Down; body.MoveAndSlide();
            }
            var before = body.GlobalTransform;
            var target = new Vector3(2, body.GlobalPosition.Y, 0);
            var path = NativeCapsuleNavigation.Find(body, body.GlobalPosition, target, .4f, .3f, _ => true);
            if (body.GlobalTransform != before || !path.Any(point => Math.Abs(point.Z) > 1.2f))
                throw new InvalidDataException("Route fixture mutated the query body or failed to expose a real bend around its blocker.");
            using var edges = new NativeCapsuleNavigation.EdgeQuery(body, .4f, .3f, _ => true);
            bool FixtureFloor(Vector3 point) => Math.Abs(point.Y - before.Origin.Y) < NativeCapsuleNavigation.SourceHeightTolerance;
            var signed = new Vector3(-.5f, 0, .25f);
            var limited = NativeCapsuleNavigation.RouteMotion(body.GlobalPosition, signed, body.GlobalPosition + Vector3.Right * .1f);
            if (limited.Dot(signed) <= 0 || limited.Cross(signed).Length() > .000001f || Math.Abs(limited.Length() - .1f) > .000001f)
                throw new InvalidDataException("A waypoint changed the signed source accumulation direction or lost its overshoot bound.");
            if (edges.ValidateSegment(body.GlobalPosition, target, FixtureFloor, out _, out _))
                throw new InvalidDataException("Actual signed segment admission accepted the fixture's solid blocker.");
            var cursor = 0;
            for (var frame = 0; frame < 180 && cursor < path.Count; frame++)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                while (cursor < path.Count && new Vector2(path[cursor].X - body.GlobalPosition.X,
                    path[cursor].Z - body.GlobalPosition.Z).Length() <= body.SafeMargin * 8) cursor++;
                if (cursor == path.Count) break;
                // This fixture exercises checked capsule execution, not the
                // separate native turn policy. Face its actual checked guide;
                // RouteMotion must preserve that signed authored direction.
                var facing = path[cursor] - body.GlobalPosition; facing.Y = 0;
                body.GlobalBasis = Basis.LookingAt(facing.Normalized(), Vector3.Up);
                var accumulation = body.GlobalBasis * Vector3.Forward * (frame % 3 == 0 ? .5f : .1f);
                var motion = NativeCapsuleNavigation.RouteMotion(body.GlobalPosition, accumulation, path[cursor]);
                var unchanged = body.GlobalTransform;
                if (!edges.ValidateSegment(body.GlobalPosition, body.GlobalPosition + motion, FixtureFloor, out _, out var rejection))
                    throw new InvalidDataException("Checked accumulated segment rejected its fixture floor: " + rejection);
                if (body.GlobalTransform != unchanged) throw new InvalidDataException("Segment query moved its actual body.");
                body.Velocity = motion * 60 + Vector3.Down * .1f; body.MoveAndSlide();
                if (!body.IsOnFloor() || Math.Abs(body.GlobalPosition.Y - before.Origin.Y) > .01f)
                    throw new InvalidDataException("Routed root accumulation left its supported native floor.");
            }
            if (body.GlobalPosition.DistanceTo(target) > .03f)
                throw new InvalidDataException("Routed source accumulation cut a corner or overshot its native endpoint.");
            GD.Print("OPENNV_NATIVE_ROUTE_ACCUMULATION_PASS nativeBlocker=true signedDirectionPreserved=true longStep=true sourceBudgetBounded=true supportedFloor=true queryPoseUnchanged=true");
        }
        finally { scene.Free(); }
    }
}
