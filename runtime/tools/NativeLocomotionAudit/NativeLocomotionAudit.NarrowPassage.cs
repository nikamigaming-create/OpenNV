using Godot;
using OpenNV.Runtime.World.Cells;

public partial class NativeLocomotionAudit
{
    private async Task CheckNarrowPassageRefinement()
    {
        var scene = new Node3D(); AddChild(scene);
        void Box(Vector3 position, Vector3 size)
        {
            var box = new StaticBody3D { Position = position };
            box.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } }); scene.AddChild(box);
        }
        Box(new(0, -.5f, 0), new(6, 1, 10));
        Box(new(-1.52f, 1, 0), new(2.96f, 2, 1));
        Box(new(1.68f, 1, 0), new(2.64f, 2, 1));
        var body = new CharacterBody3D
        { Position = new(0, .01f, 1.5f), Scale = Vector3.One * .4f, FloorSnapLength = .32f, FloorMaxAngle = FloorAngle };
        body.AddChild(new CollisionShape3D
        { Position = Vector3.Up * .9f, Shape = new CapsuleShape3D { Radius = .32f, Height = 1.8f } });
        scene.AddChild(body);
        bool Resident(Vector3 position) => Math.Abs(position.X) < .6f && Math.Abs(position.Z) < 4;
        try
        {
            for (var frame = 0; frame < 5; frame++)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                body.Velocity = Vector3.Down; body.MoveAndSlide();
            }
            var start = body.GlobalTransform;
            var goal = new Vector3(.15f, 0, -1.5f);
            var route = NativeCapsuleNavigation.FindRefined(body, start.Origin, goal, .4f, .32f, .15f, Resident, 512);
            if (body.GlobalTransform != start || route.CoarseError is null || route.Spacing != .15f)
                throw new InvalidOperationException("Narrow passage did not preserve its body and bounded finer-query evidence.");
            async Task Walk(IReadOnlyList<Vector3> path)
            {
                foreach (var waypoint in path)
                {
                    var segment = waypoint - body.GlobalPosition; segment.Y = 0;
                    var maximumFrames = checked((int)Math.Ceiling(segment.Length() / (.6f * GetPhysicsProcessDeltaTime())) + 60);
                    for (var frame = 0; frame < maximumFrames; frame++)
                    {
                        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                        var offset = waypoint - body.GlobalPosition; offset.Y = 0;
                        if (offset.Length() < .025f) break;
                        if (frame == maximumFrames - 1) throw new InvalidOperationException("Controller could not execute the capsule-supported narrow passage.");
                        var delta = (float)GetPhysicsProcessDeltaTime();
                        body.Velocity = offset.Normalized() * .6f + Vector3.Up *
                            (body.IsOnFloor() ? Math.Min(body.Velocity.Y, 0) : body.Velocity.Y - 9.81f * delta);
                        if (NativeCharacterStep.TryStep(body, offset.Normalized() * .6f * delta, .4f))
                            body.Velocity = Vector3.Down * .01f;
                        body.MoveAndSlide();
                    }
                }
            }
            await Walk(route.Path);
            if (!body.IsOnFloor() || body.GlobalPosition.DistanceTo(goal) > .03f)
                throw new InvalidOperationException("Narrow passage query did not reach the actual supported destination.");
            var reverseStart = body.GlobalTransform;
            var reverse = NativeCapsuleNavigation.FindRefined(body, body.GlobalPosition, start.Origin, .4f, .32f, .15f, Resident, 512);
            if (body.GlobalTransform != reverseStart) throw new InvalidOperationException("Reverse query moved the controller.");
            await Walk(reverse.Path);
            body.Scale = Vector3.One;
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            var adultStart = body.GlobalTransform;
            var refused = false;
            try { _ = NativeCapsuleNavigation.FindRefined(body, body.GlobalPosition, goal, .4f, .32f, .15f, Resident, 512); }
            catch (InvalidOperationException) { refused = true; }
            if (!refused || body.GlobalTransform != adultStart)
                throw new InvalidOperationException("Finer query manufactured clearance for an adult capsule or moved its body.");
            GD.Print("OPENNV_NATIVE_NARROW_PASSAGE_PASS coarseRefused=true refinedSupported=true scaledCapsule=true actualControllerBothDirections=true adultRefused=true noQueryMovement=true fixture=synthetic parity=unverified");
        }
        finally { scene.Free(); }
    }
}
