using Godot;
using OpenNV.Runtime.World.Cells;

public partial class NativeLocomotionAudit
{
    private async Task CheckSupportedDescentRefinement()
    {
        var scene = new Node3D(); AddChild(scene);
        void Box(Vector3 position, Vector3 size, float tilt = 0)
        {
            var box = new StaticBody3D { Position = position, Rotation = Vector3.Right * tilt };
            box.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } }); scene.AddChild(box);
        }
        Box(new(0, -.5f, 0), new(6, 1, 10));
        Box(new(0, .2135f, .1f), new(4, .4f, .8f), Mathf.DegToRad(-2));
        const float radius = .33f;
        var body = new CharacterBody3D { Position = new(0, .411f, 0), FloorSnapLength = radius, FloorMaxAngle = FloorAngle };
        body.AddChild(new CollisionShape3D { Position = Vector3.Up * .95f, Shape = new CapsuleShape3D { Radius = radius, Height = 1.9f } });
        scene.AddChild(body);
        bool Resident(Vector3 position) => Math.Abs(position.X) < .6f && Math.Abs(position.Z) < 4;
        try
        {
            for (var frame = 0; frame < 5; frame++)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                body.Velocity = Vector3.Down; body.MoveAndSlide();
            }
            var start = body.GlobalPosition;
            var goal = new Vector3(0, 0, -2);
            var refused = false;
            try { _ = NativeCapsuleNavigation.Find(body, start, goal, .4f, radius * 2, Resident, 512); }
            catch (InvalidOperationException) { refused = true; }
            if (!refused) throw new InvalidOperationException("The coarse descent fixture no longer reproduces its skipped support transition.");
            var path = NativeCapsuleNavigation.Find(body, start, goal, .4f, radius, Resident, 512);
            if (body.GlobalPosition != start || path.Count == 0)
                throw new InvalidOperationException("Refinement moved its query body or lost the supported descent.");
            foreach (var waypoint in path)
                for (var frame = 0; frame < 180; frame++)
                {
                    await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                    var offset = waypoint - body.GlobalPosition; offset.Y = 0;
                    if (offset.Length() < .08f) break;
                    if (frame == 179) throw new InvalidOperationException("Controller movement could not execute the refined descent.");
                    var delta = (float)GetPhysicsProcessDeltaTime();
                    var motion = offset.Normalized() * delta;
                    body.Velocity = offset.Normalized() + Vector3.Up * (body.IsOnFloor() ? Math.Min(body.Velocity.Y, 0) : body.Velocity.Y - 9.81f * delta);
                    if (NativeCharacterStep.TryStep(body, motion, .4f)) body.Velocity = Vector3.Down * .01f;
                    body.MoveAndSlide();
                }
            if (!body.IsOnFloor() || Math.Abs(body.GlobalPosition.Y) > .02f)
                throw new InvalidOperationException("Refined descent did not reach the actual lower floor.");
            GD.Print("OPENNV_NATIVE_NAVIGATION_REFINEMENT_PASS coarseRefused=true radiusSpacing=true sameCapsule=true sameStepHeight=true actualControllerArrival=true noQueryMovement=true fixture=synthetic parity=unverified");
        }
        finally { scene.Free(); }
    }
}
