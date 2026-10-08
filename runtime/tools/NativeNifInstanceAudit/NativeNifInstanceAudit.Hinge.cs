using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

public partial class NativeNifInstanceAudit
{
    private async Task ExerciseHingePhysics(string root, string model)
    {
        RuntimeLiveContentSource.Configure(root, RuntimeLiveContentSource.FalloutNewVegasGame);
        using var content = RuntimeLiveContentSource.Current!;
        if (!content.TryRead(model, null, out var bytes, out _)) throw new FileNotFoundException(model);
        var prototype = new RuntimeNativeNifPrototype(bytes, .0142875f);
        var first = prototype.InstantiatePlaced(new(new Basis(Vector3.Up, .4f), new(-2, 1.5f, 0)));
        var second = prototype.InstantiatePlaced(new(new Basis(Vector3.Up, -.7f), new(2, 1.5f, 0)));
        var floor = new StaticBody3D();
        floor.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(12, .2f, 12) }, Position = new(0, -.1f, 0) });
        try
        {
            AddChild(floor); AddChild(first); AddChild(second);
            var bodies = first.FindChildren("*", "", true, false).OfType<RuntimeNifRigidBody>().ToArray();
            var joints = first.GetChildren().OfType<RuntimeNifHingeJoint>().ToArray();
            var otherJoints = second.GetChildren().OfType<RuntimeNifHingeJoint>().ToArray();
            if (bodies.Length != 2 || joints.Length != 1 || otherJoints.Length != 1 ||
                bodies.Any(body => body.Freeze) || !joints[0].Bound || !otherJoints[0].Bound)
                throw new InvalidDataException("Owned hinge did not bind two independent dynamic bodies in both instances.");
            var unchanged = Transforms(prototype.Scene.Root);
            var initialHeight = bodies.Average(body => body.GlobalPosition.Y);
            var other = second.FindChildren("*", "", true, false).OfType<RuntimeNifRigidBody>().ToArray();
            var moving = bodies.Single(body => body.GetMeta("opennv_nif_collision_constraints").AsInt32() != 0);
            var stand = bodies.Single(body => body != moving);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            var relativeRotation = (stand.GlobalBasis.Inverse() * moving.GlobalBasis).GetRotationQuaternion();
            moving.Sleeping = false;
            moving.ApplyTorqueImpulse(joints[0].Axis * .02f);
            // Test the declared rotational freedom before contact with the
            // floor can legitimately oppose it through source friction.
            for (var frame = 0; frame < 20; frame++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            if (joints[0].Unrestricted && MathF.Abs(relativeRotation.Dot(
                (stand.GlobalBasis.Inverse() * moving.GlobalBasis).GetRotationQuaternion())) > .9999f)
                throw new InvalidDataException("The source's unrestricted hinge was silently frozen.");
            var maximumGap = 0f;
            var settledFrames = 0;
            for (var frame = 0; frame < 1200 && settledFrames < 60; frame++)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                maximumGap = Math.Max(maximumGap, joints[0].PivotDistance);
                settledFrames = bodies.Concat(other).All(body => body.LinearVelocity.Length() < .01f && body.AngularVelocity.Length() < .05f)
                    ? settledFrames + 1 : 0;
            }
            if (bodies.Average(body => body.GlobalPosition.Y) >= initialHeight - .5f ||
                settledFrames < 60)
                throw new InvalidDataException($"Owned joined model did not fall and settle through ordinary physics: " +
                    $"initialHeight={initialHeight} height={bodies.Average(body => body.GlobalPosition.Y)} " +
                    string.Join(';', bodies.Select(body => $"body={body.GetMeta("opennv_nif_collision_body")} linear={body.LinearVelocity.Length()} angular={body.AngularVelocity.Length()}")));
            var otherPositions = other.Select(body => body.GlobalPosition).ToArray();
            RemoveChild(first);
            if (joints[0].Bound) throw new InvalidDataException("Detached model retained its physics-server joint.");
            AddChild(first);
            if (!joints[0].Bound) throw new InvalidDataException("Reentered model did not rebind its own joint.");
            for (var frame = 0; frame < 120; frame++)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                maximumGap = Math.Max(maximumGap, joints[0].PivotDistance);
                if (joints[0].AxisAgreement < .99f) throw new InvalidDataException("Hinge lost its authored rotation axis.");
            }
            if (maximumGap > .03f || other.Where((body, index) => body.GlobalPosition.DistanceTo(otherPositions[index]) > .01f).Any() ||
                !Transforms(prototype.Scene.Root).SequenceEqual(unchanged))
                throw new InvalidDataException("Hinge detached its pivots, drove a sibling instance or modified its prototype.");
            GD.Print($"OPENNV_NIF_HINGE_PASS model={model} falling=true settled=true sourceAxis=true maximumPivotGap={maximumGap} independentInstances=true reentry=true sourcePrototype=unchanged retailPhysics=unmeasured");
        }
        finally { first.Free(); second.Free(); floor.Free(); prototype.Scene.Root.Free(); }
    }
}
