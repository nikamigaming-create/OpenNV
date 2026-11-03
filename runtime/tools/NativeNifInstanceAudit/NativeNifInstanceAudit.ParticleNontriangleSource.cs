using Godot;
using System.Text.Json;
using OpenNV.Runtime.Formats.Gamebryo;

public partial class NativeNifInstanceAudit
{
    private void ExerciseParticleNontriangleSource()
    {
        var baselineBytes = ParticleChannelFixture();
        var baseline = FalloutNifFile.Read(baselineBytes);
        var blocks = baseline.Blocks.Select(block => (block.TypeName,
            Bytes: baselineBytes.AsSpan(block.Offset, block.Size).ToArray())).ToList();
        var volume = blocks[2].Bytes;
        blocks[2] = ("NiPSysMeshEmitter", Bytes(writer =>
        {
            writer.Write(volume.AsSpan(0, volume.Length - 16));
            writer.Write(1); writer.Write(0);
            writer.Write(0U); writer.Write(3U);
            writer.Write(1f); writer.Write(0f); writer.Write(0f);
        }));
        var file = FalloutNifFile.Read(WriteParticleShapeColorNif(blocks, baseline.Strings.ToArray()));
        var runtime = new RuntimeNifParticleSystem();
        var fixture = new Node3D(); AddChild(fixture);
        try
        {
            var emitter = new Node3D(); var plane = new Node3D();
            fixture.AddChild(emitter); fixture.AddChild(plane);
            runtime.Configure(file, (FalloutNifParticleSystem)file.ReadObject(0),
                new Dictionary<int, Node3D> { [0] = runtime, [10] = emitter, [13] = plane },
                new ShaderMaterial
                {
                    ResourceName = NativeNifEffectMaterial.ResourceIdentity,
                    Shader = new Shader { Code = "shader_type spatial; uniform bool source_particle_atlas;" }
                }, 1);
            fixture.AddChild(runtime); runtime.SetProcess(false);
            void Advance(float delta)
            {
                runtime.BeginParentMotion(delta);
                runtime.Advance(delta);
                runtime.EndParentMotion();
            }
            Advance(.1f);
            var state = JsonSerializer.SerializeToElement(runtime.Observation);
            var declaration = state.GetProperty("deferredMeshSources").EnumerateArray().Single();
            if (runtime.BirthCount != 0 || runtime.ActiveCount != 0 ||
                state.GetProperty("emissionError").ValueKind != JsonValueKind.Null ||
                declaration.GetProperty("block").GetInt32() != 0 ||
                declaration.GetProperty("data").GetInt32() != 1 ||
                declaration.GetProperty("type").GetString() != "NiParticleSystem")
                throw new InvalidDataException("Dormant particle source lost its typed declaration or invented a birth.");
            var link = new FalloutNifControllerLink("Particles", "", "NiPSysEmitterCtlr", "Emitter", "BirthRate", 7, 9, 0);
            runtime.Bind(file, link).Apply(0);
            runtime.EmissionEnabled = false; Advance(.1f);
            if (runtime.BirthCount != 0) throw new InvalidDataException("Disabled particle emission sampled an unbound source.");
            runtime.EmissionEnabled = true;
            try { Advance(1f / 60); }
            catch (NotSupportedException error) when (error.Message.Contains("retained position-slot semantics", StringComparison.Ordinal))
            {
                state = JsonSerializer.SerializeToElement(runtime.Observation);
                if (runtime.BirthCount != 0 || runtime.ActiveCount != 0 || state.GetProperty("emissionError").GetString() != error.Message)
                    throw new InvalidDataException("Unbound particle emission fabricated a result or lost its reached refusal.");
                Reject(() => runtime.Advance(0));
                Reject(runtime.ResetCompleted);
                return;
            }
            throw new InvalidDataException("An actual nontriangle mesh birth did not refuse its unbound sampler.");
        }
        finally
        {
            if (runtime.GetParent() is null) runtime.Free();
            fixture.Free();
        }
    }
}
