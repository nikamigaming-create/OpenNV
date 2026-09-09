using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

public partial class NativeNifInstanceAudit
{
    private async Task ExerciseRigidBodyPhysics(string root, string model)
    {
        RuntimeLiveContentSource.Configure(root, RuntimeLiveContentSource.FalloutNewVegasGame);
        using var content = RuntimeLiveContentSource.Current!;
        if (!content.TryRead(model, null, out var bytes, out _)) throw new FileNotFoundException(model);
        var prototype = new RuntimeNativeNifPrototype(bytes, .0142875f);
        var first = prototype.InstantiatePlaced(new Transform3D(new Basis(Vector3.Up, .4f), new Vector3(8, 3, -2)));
        var second = prototype.InstantiatePlaced(new Transform3D(Basis.Identity, new Vector3(-8, 4, 2)));
        try
        {
            AddChild(first); AddChild(second);
            var body = first.FindChildren("*", "", true, false).OfType<RuntimeNifRigidBody>().Single();
            var other = second.FindChildren("*", "", true, false).OfType<RuntimeNifRigidBody>().Single();
            var visual = body.GetParent<Node3D>();
            var initial = visual.GlobalTransform;
            var relative = body.GlobalTransform.AffineInverse() * visual.GlobalTransform;
            var unchanged = Transforms(prototype.Scene.Root);
            other.Freeze = true;
            var otherInitial = other.GetParent<Node3D>().GlobalTransform;
            for (var frame = 0; frame < 20; frame++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (visual.GlobalPosition.Y >= initial.Origin.Y - .1f ||
                (body.GlobalTransform * relative).Origin.DistanceTo(visual.GlobalPosition) > .025f ||
                !other.GetParent<Node3D>().GlobalTransform.IsEqualApprox(otherInitial) ||
                !Transforms(prototype.Scene.Root).SequenceEqual(unchanged))
                throw new InvalidDataException("Dynamic collision detached from its visual target or moved a sibling/prototype.");
            GD.Print($"OPENNV_NIF_RIGID_BODY_PASS model={model} fallingVisual=true collisionJoined=true clonedOwners=true sourcePrototype=unchanged ordinaryPickup=unverified");
        }
        finally { first.Free(); second.Free(); prototype.Scene.Root.Free(); }
    }
}
