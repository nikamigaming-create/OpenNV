using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Cells;

public partial class NativeNifInstanceAudit
{
    private async Task ExerciseWind(string root, string model)
    {
        RuntimeLiveContentSource.Configure(root, RuntimeLiveContentSource.FalloutNewVegasGame);
        using var content = RuntimeLiveContentSource.Current!;
        if (!content.TryRead(model, null, out var bytes, out _)) throw new FileNotFoundException(model);
        var prototype = new RuntimeNativeNifPrototype(bytes, .0142875f);
        var exterior = new Node3D();
        var interior = new Node3D();
        var speed = 0f;
        var wind = new RuntimeNativeWind();
        wind.Configure(() => (speed, FalloutWindForce.InitialHeading), .0142875f);
        exterior.AddChild(wind);
        var bodies = new List<RuntimeNifRigidBody>();
        var instances = new List<Node3D>();
        try
        {
            AddChild(exterior); AddChild(interior);
            for (var index = 0; index < 4; index++)
            {
                var instance = prototype.InstantiatePlaced(new(Basis.Identity, new(index * 5, 5, 0)));
                (index == 3 ? interior : exterior).AddChild(instance);
                instances.Add(instance);
                var body = instance.FindChildren("*", "", true, false).OfType<RuntimeNifRigidBody>().Single();
                Require(body.RespondsToWind, "Owned body did not retain its wind flag after cloning.");
                body.GravityScale = 0; body.Sleeping = true;
                bodies.Add(body);
            }
            bodies[2].Freeze = true;
            await Frames(5);
            foreach (var body in bodies.Where(body => !body.Freeze)) body.Sleeping = true;
            var original = bodies.Select(body => body.GlobalTransform).ToArray();
            await Frames(10);
            Require(bodies.Where(body => !body.Freeze).All(body => body.Sleeping), "Calm weather woke a body.");
            speed = 50 / 255f;
            await Frames(90);
            Require(bodies.Take(2).All(body => body.GlobalPosition.DistanceTo(original[bodies.IndexOf(body)].Origin) > .15f),
                "Wind failed to wake and move source bodies.");
            Require(bodies[0].LinearVelocity.DistanceTo(bodies[1].LinearVelocity) > .001f, "Bodies shared a gust sequence.");
            Require(bodies[2].GlobalTransform.IsEqualApprox(original[2]) && bodies[3].GlobalTransform.IsEqualApprox(original[3]),
                "Wind moved frozen equipment or an interior body.");
            var dormant = instances[0];
            GamebryoReferenceEnableRuntime.Apply(dormant, false);
            await Frames(2);
            var stopped = bodies[0].GlobalPosition;
            await Frames(30);
            Require(bodies[0].GlobalPosition.IsEqualApprox(stopped), "A disabled warm reference kept simulating.");
            GamebryoReferenceEnableRuntime.Apply(dormant, true);
            await Frames(30);
            Require(bodies[0].GlobalPosition.DistanceTo(stopped) > .1f, "A reactivated reference failed to resume wind.");
            speed = 0;
            bodies[0].LinearVelocity = Vector3.Zero; bodies[0].AngularVelocity = Vector3.Zero; bodies[0].Sleeping = true;
            await Frames(10);
            Require(bodies[0].Sleeping, "Calm weather did not release the wake policy.");
            GD.Print($"OPENNV_OWNED_WIND_PASS model={model} clones=true wake=true independentGusts=true frozenExcluded=true interiorExcluded=true disabledStops=true warmResumes=true calmSleeps=true coldPhysicsPersistence=unverified");
        }
        finally { exterior.Free(); interior.Free(); prototype.Scene.Root.Free(); }

        async Task Frames(int count)
        {
            for (var frame = 0; frame < count; frame++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
    }
}
