using System.Text;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Formats.Gamebryo;

public partial class NativeNifInstanceAudit
{
    private void ExerciseParticleChannels()
    {
        var file = FalloutNifFile.Read(ParticleChannelFixture());
        var fixture = new Node3D(); AddChild(fixture);
        try
        {
            RuntimeNifParticleSystem Create()
            {
                var runtime = new RuntimeNifParticleSystem();
                var emitter = new Node3D { Position = Vector3.Up }; var plane = new Node3D();
                fixture.AddChild(emitter); fixture.AddChild(plane);
                runtime.Configure(file, (FalloutNifParticleSystem)file.ReadObject(0), new Dictionary<int, Node3D>
                { [0] = runtime, [10] = emitter, [13] = plane }, new ShaderMaterial
                { ResourceName = NativeNifEffectMaterial.ResourceIdentity, Shader = new Shader { Code = "shader_type spatial; uniform bool source_particle_atlas;" } }, 1);
                fixture.AddChild(runtime); runtime.SetProcess(false);
                return runtime;
            }
            var first = Create(); var second = Create();
            var controller = (FalloutNifParticleController)file.ReadObject(9);
            if (controller.Block.TypeName != "NiPSysEmitterLifeSpanCtlr" || controller.Time.Target != 0 || controller.Modifier != "Emitter")
                throw new InvalidDataException("Particle lifespan controller lost its source fields.");
            var life = new FalloutNifControllerLink("Particles", "", "NiPSysEmitterLifeSpanCtlr", "Emitter", "", 6, 9, 0);
            first.Bind(file, life).Apply(0);
            first.Bind(file, life with { ControllerType = "NiPSysEmitterCtlr", Variable2 = "BirthRate", Interpolator = 7 }).Apply(0);
            void Advance(float seconds) { first.BeginParentMotion(seconds); first.Advance(seconds); first.EndParentMotion(); }
            for (var frame = 0; frame < 20; frame++) Advance(1f / 60);
            first.Bind(file, life with { Interpolator = 14 }).Apply(0);
            for (var frame = 0; frame < 3; frame++) Advance(1f / 60);
            var values = Lifetimes(first);
            if (!values.Contains(4) || !values.Contains(.1f) || second.BirthCount != 0)
                throw new InvalidDataException("Lifespan publication mutated old particles or another instance.");
            for (var frame = 0; frame < 40; frame++) Advance(1f / 60);
            if (first.CollisionCount == 0 || first.Positions.Any(position => position.Y < -.0001f))
                throw new InvalidDataException("Source particle collision plane did not stop downward traversal.");
            Reject(() => first.Bind(file, life with { Variable1 = "Absent" }));
            Reject(() => first.Bind(file, life with { Interpolator = 15 }).Apply(0));
            Reject(() => ConfigureInvalid(true));
            Reject(() => ConfigureInvalid(false));
            first.EmissionEnabled = false; Advance(5); first.ResetCompleted();
            var reset = JsonSerializer.SerializeToElement(first.Observation).GetProperty("modifiers").EnumerateArray().Single(value => value.GetProperty("Name").GetString() == "Emitter");
            if (reset.GetProperty("lifespan").GetSingle() != 1 || first.CollisionCount != 0)
                throw new InvalidDataException("Particle reset did not restore its source lifespan and contacts.");
            ExerciseParticlePlane();
            GD.Print("OPENNV_PARTICLE_CHANNELS_PASS sourceLifespan=true newBirthOnly=true independentInstances=true linkedPlane=true boundedContact=true sourceReset=true invalidBindingsAndChainsRejected=true retailCollisionParity=unverified");

            void ConfigureInvalid(bool cycle)
            {
                var invalid = FalloutNifFile.Read(ParticleChannelFixture(cycle: cycle, wrongManager: !cycle));
                var runtime = new RuntimeNifParticleSystem();
                try
                {
                    runtime.Configure(invalid, (FalloutNifParticleSystem)invalid.ReadObject(0),
                    new Dictionary<int, Node3D> { [0] = runtime, [10] = first, [13] = second }, new ShaderMaterial(), 1);
                }
                finally { runtime.Free(); }
            }
        }
        finally { fixture.Free(); }

        static float[] Lifetimes(RuntimeNifParticleSystem runtime)
        {
            var bytes = Convert.FromBase64String(JsonSerializer.SerializeToElement(runtime.Observation).GetProperty("state").GetString()!);
            return Enumerable.Range(0, bytes.Length / 64).Select(index => BitConverter.ToSingle(bytes, index * 64 + 28)).ToArray();
        }
        static void Reject(Action action)
        {
            try { action(); }
            catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
            throw new InvalidDataException("Invalid particle source ownership was admitted.");
        }
    }

    private static void ExerciseParticlePlane()
    {
        var x = System.Numerics.Vector3.UnitX; var y = System.Numerics.Vector3.UnitY;
        var hit = FalloutNifParticlePlane.Sweep(new(0, 0, 1), new(0, 0, -2), 1, 2, 2, x, y);
        if (hit is not { Fraction: .5f, Point: { } point } || point != System.Numerics.Vector3.Zero ||
            FalloutNifParticlePlane.Sweep(new(2, 0, 1), new(0, 0, -2), 1, 2, 2, x, y) is not null ||
            FalloutNifParticlePlane.Sweep(new(0, 0, -1), new(0, 0, 2), 1, 2, 2, x, y) is not null)
            throw new InvalidDataException("Particle plane footprint or front-face sweep differs.");
    }

    private static byte[] ParticleChannelFixture(bool cycle = false, bool wrongManager = false)
    {
        static void Object(BinaryWriter writer, int name)
        {
            writer.Write(name); writer.Write(0); writer.Write(-1); writer.Write(14U);
            foreach (var value in new float[] { 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1, 1 }) writer.Write(value);
            writer.Write(0); writer.Write(-1);
        }
        static void Header(BinaryWriter writer, int name, uint order) { writer.Write(name); writer.Write(order); writer.Write(0); writer.Write(true); }
        var blocks = new (string Type, byte[] Bytes)[]
        {
            ("NiParticleSystem", Bytes(w => { Object(w, 0); w.Write(1); w.Write(-1); w.Write(0); w.Write(-1); w.Write(false); w.Write(false); w.Write(5); foreach(var value in new[] {2,3,4,5,11}) w.Write(value); })),
            ("NiPSysData", Bytes(w => { w.Write(0); w.Write((ushort)32); w.Write((byte)0); w.Write((byte)0); w.Write(true); w.Write((ushort)0); w.Write(false); foreach(var value in new[] {0f,0f,0f,4f}) w.Write(value); w.Write(true); w.Write((ushort)0); w.Write(-1); w.Write(true); w.Write((ushort)0); w.Write(true); w.Write(false); w.Write(false); w.Write(false); w.Write(false); w.Write((byte)0); w.Write(false); })),
            ("NiPSysBoxEmitter", Bytes(w => { Header(w, 1, 1000); foreach(var value in new[] {2f,0f,MathF.PI,0f,0f,0f,1f,1f,1f,1f,.1f,0f,1f,0f}) w.Write(value); w.Write(10); w.Write(0f); w.Write(0f); w.Write(0f); })),
            ("NiPSysAgeDeathModifier", Bytes(w => { Header(w, 2, 0); w.Write(false); w.Write(-1); })),
            ("NiPSysPositionModifier", Bytes(w => Header(w, 3, 6000))),
            ("NiPSysBoundUpdateModifier", Bytes(w => { Header(w, 4, 7000); w.Write((ushort)1); })),
            ("NiFloatInterpolator", Bytes(w => { w.Write(4f); w.Write(-1); })),
            ("NiFloatInterpolator", Bytes(w => { w.Write(60f); w.Write(-1); })),
            ("NiBoolInterpolator", Bytes(w => { w.Write((byte)1); w.Write(-1); })),
            ("NiPSysEmitterLifeSpanCtlr", Bytes(w => { w.Write(-1); w.Write((ushort)108); w.Write(1f); w.Write(0f); w.Write(0f); w.Write(1f); w.Write(0); w.Write(6); w.Write(1); })),
            ("NiNode", Bytes(w => { Object(w, 5); w.Write(0); w.Write(0); })),
            ("NiPSysColliderManager", Bytes(w => { Header(w, 6, 5000); w.Write(12); })),
            ("NiPSysPlanarCollider", Bytes(w => { w.Write(.5f); w.Write(false); w.Write(false); w.Write(-1); w.Write(wrongManager ? 0 : 11); w.Write(cycle ? 12 : -1); w.Write(13); w.Write(4f); w.Write(4f); foreach(var value in new[] {1f,0f,0f,0f,1f,0f}) w.Write(value); })),
            ("NiNode", Bytes(w => { Object(w, 7); w.Write(0); w.Write(0); })),
            ("NiFloatInterpolator", Bytes(w => { w.Write(.1f); w.Write(-1); })),
            ("NiFloatInterpolator", Bytes(w => { w.Write(-.1f); w.Write(-1); })),
        };
        string[] names = ["Particles", "Emitter", "Age", "Position", "Bounds", "EmitterNode", "Colliders", "PlaneNode"];
        return Bytes(w =>
        {
            w.Write("Gamebryo File Format, Version 20.2.0.7\n"u8); w.Write(FalloutNifFile.Version); w.Write((byte)1); w.Write(FalloutNifFile.UserVersion);
            w.Write(blocks.Length); w.Write(34U); w.Write(new byte[] { 1, 0, 1, 0, 1, 0 }); w.Write((ushort)blocks.Length);
            foreach (var block in blocks) { w.Write(block.Type.Length); w.Write(Encoding.ASCII.GetBytes(block.Type)); }
            for (var index = 0; index < blocks.Length; index++) w.Write((ushort)index);
            foreach (var block in blocks) w.Write(block.Bytes.Length);
            w.Write(names.Length); w.Write(names.Max(name => name.Length)); foreach (var name in names) { w.Write(name.Length); w.Write(Encoding.ASCII.GetBytes(name)); }
            w.Write(0U); foreach (var block in blocks) w.Write(block.Bytes); w.Write(1U); w.Write(0);
        });
    }
}
