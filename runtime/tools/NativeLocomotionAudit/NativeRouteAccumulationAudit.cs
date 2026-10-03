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
            var cursor = 0;
            for (var frame = 0; frame < 180 && cursor < path.Count; frame++)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                while (cursor < path.Count && new Vector2(path[cursor].X - body.GlobalPosition.X,
                    path[cursor].Z - body.GlobalPosition.Z).Length() <= body.SafeMargin * 8) cursor++;
                if (cursor == path.Count) break;
                // A changing body-facing direction and large accumulation
                // budget must still consume the checked native route corners.
                var accumulation = new Basis(Vector3.Up, frame * .37f) * Vector3.Forward * (frame % 3 == 0 ? .5f : .1f);
                var motion = NativeCapsuleNavigation.RouteMotion(body.GlobalPosition, accumulation, path[cursor]);
                body.Velocity = motion * 60 + Vector3.Down * .1f; body.MoveAndSlide();
                if (!body.IsOnFloor() || Math.Abs(body.GlobalPosition.Y - before.Origin.Y) > .01f)
                    throw new InvalidDataException("Routed root accumulation left its supported native floor.");
            }
            if (body.GlobalPosition.DistanceTo(target) > .03f)
                throw new InvalidDataException("Routed source accumulation cut a corner or overshot its native endpoint.");
            GD.Print("OPENNV_NATIVE_ROUTE_ACCUMULATION_PASS nativeBlocker=true turningDirection=true longStep=true sourceBudgetBounded=true supportedFloor=true queryPoseUnchanged=true");
        }
        finally { scene.Free(); }
    }
}
