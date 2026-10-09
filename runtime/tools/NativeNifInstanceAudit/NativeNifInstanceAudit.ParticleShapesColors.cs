using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Formats.Gamebryo;

public partial class NativeNifInstanceAudit
{
    private void ExerciseParticleShapesColors()
    {
        var before = Performance.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount);
        var fixture = new Node3D(); AddChild(fixture);
        var bytes = ParticleShapesColorsFixture();
        var file = FalloutNifFile.Read(bytes);
        var sourceHash = System.Security.Cryptography.SHA256.HashData(bytes);
        try
        {
            var first = Create(file); var second = Create(file);
            var emitter = new FalloutNifControllerLink("Particles", "", "NiPSysEmitterCtlr", "Emitter", "BirthRate", 7, 9, 0);
            first.Bind(file, emitter).Apply(0);
            first.Bind(file, emitter with { ControllerType = "NiPSysEmitterLifeSpanCtlr", Variable2 = "", Interpolator = 6 }).Apply(0);
            Advance(first, 1f / 60);
            first.EmissionEnabled = false;
            Advance(first, 1.2f);
            first.Publish();
            if (first.BirthCount != 1 || first.ActiveCount != 1 || first.CollisionCount == 0 ||
                first.Positions.Single().Y < 1 || second.BirthCount != 0 || second.CollisionCount != 0)
                throw new InvalidDataException("Actual mixed sphere/plane particle ownership did not preserve contact and independent instances.");
            var state = Convert.FromBase64String(JsonSerializer.SerializeToElement(first.Observation).GetProperty("state").GetString()!);
            var fraction = BitConverter.ToSingle(state, 24) / BitConverter.ToSingle(state, 28);
            var amount = fraction * 2;
            var expected = new[] { 1 - .8f * amount, .2f + .8f * amount, .1f + .4f * amount, .25f + .5f * amount };
            var draw = first.GetChildren().OfType<MultiMeshInstance3D>().Single().Multimesh;
            if (draw.VisibleInstanceCount != 1 || Enumerable.Range(0, 4).Any(channel =>
                MathF.Abs(draw.Buffer[12 + channel] - expected[channel]) > .00001f))
                throw new InvalidDataException("Native MultiMesh RGBA differs from independently interpolated source key channels.");
            foreach (var mode in new[] { "cycle", "manager", "radius", "data-type", "data-null", "empty-color", "spawn" })
            {
                var invalid = FalloutNifFile.Read(ParticleShapesColorsFixture(mode: mode));
                try { var accepted = Create(invalid); accepted.Free(); }
                catch (Exception error) when (error is InvalidDataException or NotSupportedException) { continue; }
                throw new InvalidDataException("Invalid particle source binding was admitted: " + mode);
            }
            var mortalFile = FalloutNifFile.Read(ParticleShapesColorsFixture(die: true));
            var mortal = Create(mortalFile);
            mortal.Bind(mortalFile, emitter).Apply(0);
            mortal.Bind(mortalFile, emitter with { ControllerType = "NiPSysEmitterLifeSpanCtlr", Variable2 = "", Interpolator = 6 }).Apply(0);
            Advance(mortal, 1f / 60); mortal.EmissionEnabled = false; Advance(mortal, 1.2f);
            if (mortal.ActiveCount != 0 || mortal.DeathCount != 1 || mortal.CollisionCount != 1)
                throw new InvalidDataException("Sphere collision did not honor the source death declaration.");
            Advance(first, 4); first.ResetCompleted();
            if (first.CollisionCount != 0 || first.BirthCount != 0 || second.BirthCount != 0 ||
                !sourceHash.AsSpan().SequenceEqual(System.Security.Cryptography.SHA256.HashData(bytes)))
                throw new InvalidDataException("Particle reset or failure changed source/sibling state.");
            GD.Print("OPENNV_NATIVE_PARTICLE_SHAPES_COLORS_PASS actualNifReader=true mixedChain=true sweptSphere=true sourceDeath=true keyedRGBA=true nativeBuffer=true independentInstances=true invalidOwnersRefused=true sourceUnchanged=true retailParity=unverified");
        }
        finally { fixture.Free(); }
        if (Performance.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount) != before)
            throw new InvalidDataException("Particle source fixture retired with orphaned native nodes.");

        RuntimeNifParticleSystem Create(FalloutNifFile input)
        {
            var runtime = new RuntimeNifParticleSystem();
            var nodes = new Dictionary<int, Node3D> { [0] = runtime };
            foreach (var index in new[] { 10, 13, 18 })
            {
                var declaration = input.ReadNode(index);
                var node = new Node3D
                {
                    Position = GamebryoCoordinate.ConvertVector(new Vector3(
                    declaration.Transform.Translation.X, declaration.Transform.Translation.Y, declaration.Transform.Translation.Z))
                };
                fixture.AddChild(node); nodes.Add(index, node);
            }
            try
            {
                runtime.Configure(input, (FalloutNifParticleSystem)input.ReadObject(0), nodes, new ShaderMaterial
                { ResourceName = NativeNifEffectMaterial.ResourceIdentity, Shader = new Shader { Code = "shader_type spatial; uniform bool source_particle_atlas;" } }, 1);
                fixture.AddChild(runtime); runtime.SetProcess(false);
                return runtime;
            }
            catch { runtime.Free(); throw; }
        }
        static void Advance(RuntimeNifParticleSystem runtime, float seconds)
        { runtime.BeginParentMotion(seconds); runtime.Advance(seconds); runtime.EndParentMotion(); }
    }

    // Full authored NIF fixture. The original unrelated channel blocks remain
    // byte-identical; all new declarations and positions are first-party data.
    internal static byte[] ParticleShapesColorsFixture(uint interpolation = 1, bool die = false, string mode = "valid")
    {
        var baselineBytes = ParticleChannelFixture();
        var baseline = FalloutNifFile.Read(baselineBytes);
        var blocks = baseline.Blocks.Select(block => (block.TypeName,
            Bytes: baselineBytes.AsSpan(block.Offset, block.Size).ToArray())).ToList();
        var system = (FalloutNifParticleSystem)baseline.ReadObject(0);
        var body = blocks[0].Bytes;
        blocks[0] = ("NiParticleSystem", Bytes(writer =>
        {
            writer.Write(body.AsSpan(0, body.Length - 4 - 4 * system.Modifiers.Length));
            writer.Write(system.Modifiers.Length + 1);
            foreach (var index in system.Modifiers) writer.Write(index);
            writer.Write(17);
        }));
        var names = baseline.Strings.Concat(new[] { "SphereNode", "KeyColor" }).ToArray();
        var emitter = blocks[10].Bytes.ToArray();
        BinaryPrimitives.WriteSingleLittleEndian(emitter.AsSpan(24), 3);
        blocks[10] = ("NiNode", emitter);
        var plane = blocks[13].Bytes.ToArray();
        BinaryPrimitives.WriteSingleLittleEndian(plane.AsSpan(24), -4);
        blocks[13] = ("NiNode", plane);
        blocks[12] = ("NiPSysPlanarCollider", Bytes(writer =>
        {
            writer.Write(.5f); writer.Write(false); writer.Write(false); writer.Write(-1); writer.Write(11); writer.Write(16); writer.Write(13);
            writer.Write(4f); writer.Write(4f); foreach (var value in new[] { 1f, 0f, 0f, 0f, 1f, 0f }) writer.Write(value);
        }));
        blocks.Add(("NiPSysSphericalCollider", Bytes(writer =>
        {
            writer.Write(.5f); writer.Write(mode == "spawn"); writer.Write(die); writer.Write(-1);
            writer.Write(mode == "manager" ? 0 : 11); writer.Write(mode == "cycle" ? 12 : -1); writer.Write(18);
            writer.Write(mode == "radius" ? -1f : 1f);
        })));
        blocks.Add(("NiPSysColorModifier", Bytes(writer =>
        {
            writer.Write(names.Length - 1); writer.Write(3000U); writer.Write(0); writer.Write(true);
            writer.Write(mode == "data-null" ? -1 : mode == "data-type" ? 1 : 19);
        })));
        var sphere = baselineBytes.AsSpan(baseline.Blocks[13].Offset, baseline.Blocks[13].Size).ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(sphere, names.Length - 2);
        blocks.Add(("NiNode", sphere));
        blocks.Add(("NiColorData", ParticleColorKeysFixture(interpolation, mode == "empty-color")));
        blocks.Add(("NiColorData", Bytes(writer => writer.Write(0U))));
        return WriteParticleShapeColorNif(blocks, names);
    }

    internal static byte[] ParticleColorKeysFixture(uint interpolation, bool empty = false) => Bytes(writer =>
    {
        writer.Write(empty ? 0U : 3U);
        if (empty) return;
        writer.Write(interpolation);
        var colors = new[] { new[] { 1f, .2f, .1f, .25f }, new[] { .2f, 1f, .5f, .75f }, new[] { .4f, .3f, 1f, .5f } };
        for (var index = 0; index < colors.Length; index++)
        {
            writer.Write(index * .5f);
            foreach (var value in colors[index]) writer.Write(value);
            if (interpolation == 2) for (var tangent = 0; tangent < 8; tangent++) writer.Write(0f);
            if (interpolation == 3) for (var parameter = 0; parameter < 3; parameter++) writer.Write(0f);
        }
    });

    internal static byte[] WriteParticleShapeColorNif(IReadOnlyList<(string TypeName, byte[] Bytes)> blocks,
        string[] names, uint userVersion2 = 34) => Bytes(writer =>
    {
        writer.Write("Gamebryo File Format, Version 20.2.0.7\n"u8); writer.Write(FalloutNifFile.Version);
        writer.Write((byte)1); writer.Write(FalloutNifFile.UserVersion); writer.Write(blocks.Count); writer.Write(userVersion2);
        writer.Write(new byte[] { 1, 0, 1, 0, 1, 0 }); writer.Write((ushort)blocks.Count);
        foreach (var block in blocks) { writer.Write(block.TypeName.Length); writer.Write(Encoding.ASCII.GetBytes(block.TypeName)); }
        for (var index = 0; index < blocks.Count; index++) writer.Write((ushort)index);
        foreach (var block in blocks) writer.Write(block.Bytes.Length);
        writer.Write(names.Length); writer.Write(names.Length == 0 ? 0 : names.Max(name => name.Length));
        foreach (var name in names) { writer.Write(name.Length); writer.Write(Encoding.ASCII.GetBytes(name)); }
        writer.Write(0U); foreach (var block in blocks) writer.Write(block.Bytes); writer.Write(1U); writer.Write(0);
    });
}
