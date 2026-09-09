using Godot;
using OpenNV.Runtime.World.Cells;

public partial class NativeLocomotionAudit : Node3D
{
    public override async void _Ready()
    {
        try
        {
            NativeNavigationContracts.Run();
            await Check(.3f, false, true);
            await Check(2, false, false);
            await Check(.3f, true, false);
            GD.Print("OPENNV_NATIVE_LOCOMOTION_PASS curb=true tallWall=true lowCeiling=true airborne=true sourceStepHeightAndCameraParity=unverified");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private async Task Check(float height, bool ceiling, bool shouldClimb)
    {
        var scene = new Node3D(); AddChild(scene);
        void Box(Vector3 position, Vector3 size)
        {
            var box = new StaticBody3D { Position = position };
            box.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
            scene.AddChild(box);
        }
        Box(new(0, -.5f, -3), new(10, 1, 20));
        Box(new(0, height / 2, -5), new(4, height, 8));
        if (ceiling) Box(new(0, 2.5f, -3), new(10, 1, 20));
        var body = new CharacterBody3D { FloorSnapLength = .32f, Position = new(0, .1f, 0) };
        body.AddChild(new CollisionShape3D { Position = new(0, .9f, 0), Shape = new CapsuleShape3D { Height = 1.8f, Radius = .32f } });
        scene.AddChild(body);
        try
        {
            if (NativeCharacterStep.TryStep(body, new(0, 0, -.06f), .4f)) throw new InvalidOperationException("Airborne controller climbed a step.");
            var steps = 0;
            for (var frame = 0; frame < 90; frame++)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                body.Velocity = new(0, -.2f, -3.6f);
                if (NativeCharacterStep.TryStep(body, new(0, 0, -.06f), .4f))
                { ++steps; body.Velocity = Vector3.Down * .01f; }
                body.MoveAndSlide();
            }
            GD.Print($"OPENNV_NATIVE_LOCOMOTION_CASE height={height} ceiling={ceiling} steps={steps} position={body.Position}");
            if (shouldClimb ? steps == 0 || body.Position.Z > -3 || body.Position.Y < height - .02f : steps != 0 || body.Position.Z < -.9f)
                throw new InvalidOperationException("Capsule step/obstruction traversal differs from the synthetic collision scene.");
        }
        finally { scene.QueueFree(); await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame); }
    }
}
