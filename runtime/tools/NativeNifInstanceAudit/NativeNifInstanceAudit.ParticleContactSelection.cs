using System.Buffers.Binary;
using Godot;
using OpenNV.Runtime.Formats.Gamebryo;

public partial class NativeNifInstanceAudit
{
    private void ExerciseParticleContactSelection()
    {
        var start = new System.Numerics.Vector3(10000, 0, 1);
        var across = new System.Numerics.Vector3(10000, 0, -.00000001f);
        var normal = System.Numerics.Vector3.UnitZ;
        var shifted = FalloutNifParticlePlane.CorrectPoint(start, across, normal, System.Numerics.Vector3.Zero);
        if (shifted != across + normal * (10000f / 2097152f) || shifted.Z <= 0 ||
            FalloutNifParticlePlane.CorrectPoint(start, -across, normal, System.Numerics.Vector3.Zero) != -across ||
            FalloutNifParticlePlane.CorrectPoint(start, new(10000, 0, 0), normal, System.Numerics.Vector3.Zero) != new System.Numerics.Vector3(10000, 0, 0))
            throw new InvalidDataException("Particle far-contact correction changed its original side or invented an unconditional offset.");
        var bytes = ParticleChannelFixture();
        var baseline = FalloutNifFile.Read(bytes);
        var blocks = baseline.Blocks.Select(block => (block.TypeName,
            Bytes: bytes.AsSpan(block.Offset, block.Size).ToArray())).ToList();
        var firstPlane = blocks[12].Bytes.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(firstPlane.AsSpan(14), 16);
        blocks[12] = ("NiPSysPlanarCollider", firstPlane);
        var secondPlane = firstPlane.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(secondPlane.AsSpan(0), BitConverter.SingleToInt32Bits(.25f));
        BinaryPrimitives.WriteInt32LittleEndian(secondPlane.AsSpan(14), -1);
        BinaryPrimitives.WriteInt32LittleEndian(secondPlane.AsSpan(18), 17);
        blocks.Add(("NiPSysPlanarCollider", secondPlane));
        blocks.Add(("NiNode", blocks[13].Bytes.ToArray()));
        var file = FalloutNifFile.Read(WriteParticleShapeColorNif(blocks, baseline.Strings.ToArray()));
        Check(0, .01f, "nearest");
        Check(.01f, .01f, "authored-tie");
        Check(.01f, .0095f, "bounded-end-slab");
        Check(.0195f, .0195f, "immediate-first", 12, 1);
        var endHeight = .02f - 2f / 60;
        Check(endHeight, endHeight, "frame-end", 16, 0, true);

        void Check(float firstHeight, float secondHeight, string label, int expectedOwner = 16, float expectedVelocity = .5f, bool frameEnd = false)
        {
            var fixture = new Node3D(); AddChild(fixture);
            try
            {
                var runtime = new RuntimeNifParticleSystem();
                var emitter = new Node3D { Position = new(0, .02f, 0) }; fixture.AddChild(emitter);
                var first = new Node3D { Position = new(0, firstHeight, 0) }; fixture.AddChild(first);
                var second = new Node3D { Position = new(0, secondHeight, 0) }; fixture.AddChild(second);
                try
                {
                    runtime.Configure(file, (FalloutNifParticleSystem)file.ReadObject(0), new Dictionary<int, Node3D>
                    { [0] = runtime, [10] = emitter, [13] = first, [17] = second }, new ShaderMaterial
                    { ResourceName = NativeNifEffectMaterial.ResourceIdentity, Shader = new Shader { Code = "shader_type spatial; uniform bool source_particle_atlas;" } }, 1);
                }
                catch { runtime.Free(); throw; }
                fixture.AddChild(runtime);
                runtime.SetProcess(false);
                var contacts = new List<FalloutNifParticleLifecycleEvent>();
                runtime.LifecycleEvent += value => { if (value.Kind == "collision") contacts.Add(value); };
                var link = new FalloutNifControllerLink("Particles", "", "NiPSysEmitterCtlr", "Emitter", "BirthRate", 7, 9, 0);
                runtime.Bind(file, link).Apply(0);
                runtime.Bind(file, link with { ControllerType = "NiPSysEmitterLifeSpanCtlr", Variable2 = "", Interpolator = 6 }).Apply(0);
                runtime.BeginParentMotion(1f / 60); runtime.Advance(1f / 60); runtime.EndParentMotion();
                if (runtime.BirthCount != 1 || contacts.Count != 1 || contacts[0].DeclaringOwner != expectedOwner ||
                    MathF.Abs(contacts[0].Velocity.Y - expectedVelocity) > .00001f ||
                    (frameEnd ? MathF.Abs(runtime.Positions.Single().Y - secondHeight) > .00001f : runtime.Positions.Single().Y <= secondHeight))
                    throw new InvalidDataException($"Particle {label} selection, single response or remaining motion differs.");
            }
            finally { fixture.Free(); }
        }
    }
}
