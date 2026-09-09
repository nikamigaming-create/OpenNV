using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

public partial class NativeNifInstanceAudit
{
    private static void ExerciseParticleBuffer()
    {
        using var quad = new QuadMesh();
        using var scalar = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            UseCustomData = true,
            Mesh = quad,
            InstanceCount = 3
        };
        using var bulk = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            UseCustomData = true,
            Mesh = quad,
            InstanceCount = 3
        };
        var buffer = new float[3 * NativeParticleDrawBuffer.Stride];
        for (var index = 0; index < 3; index++)
        {
            var transform = new Transform3D(new Basis(new Quaternion(Vector3.Up, index * .37f)) *
                Basis.FromScale(new Vector3(1 + index, .25f, 2)), new Vector3(-index, 2, 4));
            var color = new Color(.25f, .5f, 1, .75f); var atlas = new Color(.25f, .75f, 0, 1);
            scalar.SetInstanceTransform(index, transform); scalar.SetInstanceColor(index, color); scalar.SetInstanceCustomData(index, atlas);
            NativeParticleDrawBuffer.Write(buffer.AsSpan(index * NativeParticleDrawBuffer.Stride), transform, color, atlas);
        }
        bulk.Buffer = buffer;
        var expected = System.Runtime.InteropServices.MemoryMarshal.AsBytes(scalar.Buffer.AsSpan());
        var observed = System.Runtime.InteropServices.MemoryMarshal.AsBytes(bulk.Buffer.AsSpan());
        if (expected.Length != buffer.Length * sizeof(float) || !expected.SequenceEqual(observed))
            throw new InvalidOperationException("Batched particle bytes differ from Godot's scalar transform/color/atlas setters.");
        GD.Print("OPENNV_PARTICLE_BUFFER_PASS nativeScalarBytes=exact transforms=3 nonuniformScale=true rotation=true colors=true atlas=true");
    }

    private async Task ExerciseParticles(string root, string model, bool checkBounds = false)
    {
        RuntimeLiveContentSource.Configure(root, RuntimeLiveContentSource.FalloutNewVegasGame);
        using var content = RuntimeLiveContentSource.Current!;
        if (!content.TryRead(model, null, out var bytes, out _)) throw new FileNotFoundException(model);
        var prototype = new RuntimeNativeNifPrototype(bytes, .0142875f);
        var first = prototype.InstantiatePlaced(new(Basis.Identity, new Vector3(4, 2, 7)));
        var second = prototype.InstantiatePlaced(new(Basis.Identity, new Vector3(-20, 3, 0)));
        Camera3D? camera = checkBounds ? new() { Current = true, Position = new(3, 7, 10) } : null;
        try
        {
            if (camera is not null) AddChild(camera);
            AddChild(first); AddChild(second);
            first.ProcessMode = ProcessModeEnum.Disabled; second.ProcessMode = ProcessModeEnum.Disabled;
            var particles = first.FindChildren("*", "", true, false).OfType<RuntimeNifParticleSystem>().ToArray();
            var clocks = first.FindChildren("*", "", true, false).OfType<RuntimeNifControllerPlayer>().ToArray();
            var draws = first.FindChildren("*", "", true, false).OfType<MultiMeshInstance3D>().ToArray();
            var checkedQuads = 0;
            if (particles.Length == 0 || clocks.Length == 0) throw new InvalidDataException("Owned particle graph did not create simulation and clock owners.");
            if (first.FindChildren("*", "", true, false).Any(node =>
                node.GetMeta("opennv_nif_source_name", "").AsString().StartsWith("EditorMarker", StringComparison.Ordinal)))
                throw new InvalidDataException("Inherited editor-marker policy left a helper subtree in a live effect.");
            for (var frame = 0; frame < 240; frame++)
            {
                foreach (var clock in clocks) clock.SeekSourceTime(frame / 60f);
                foreach (var particle in particles) particle.Advance(1f / 60);
                if (camera is not null)
                {
                    camera.Quaternion = Quaternion.FromEuler(new(frame * .013f, frame * .037f, frame * .021f));
                    foreach (var particle in particles) particle._Process(0);
                    foreach (var draw in draws) checkedQuads += CheckParticleBounds(draw.Multimesh);
                }
            }
            var before = particles.SelectMany(value => value.Positions).ToArray();
            foreach (var particle in particles) particle.Advance(.1f);
            foreach (var particle in particles) particle._Process(0);
            if (particles.Any(value => value.BirthCount == 0 || value.ActiveCount == 0) ||
                before.SequenceEqual(particles.SelectMany(value => value.Positions)) ||
                particles.SelectMany(value => value.Positions).Any(value => !value.IsFinite()) ||
                second.FindChildren("*", "", true, false).OfType<RuntimeNifParticleSystem>().Any(value => value.BirthCount != 0) ||
                prototype.Scene.Root.FindChildren("*", "", true, false).OfType<RuntimeNifParticleSystem>().Any(value => value.BirthCount != 0))
                throw new InvalidDataException("Particles did not emit/move independently with finite source state.");
            GD.Print($"OPENNV_PARTICLE_SIMULATION_PASS model={model} systems={particles.Length} births={particles.Sum(value => value.BirthCount)} alive={particles.Sum(value => value.ActiveCount)} instanceIsolation=true finiteMotion=true pixels=unverified");
            if (checkBounds)
            {
                if (checkedQuads == 0 || particles.Sum(particle => particle.BoundsPublications) >= 240 * particles.Length)
                    throw new InvalidDataException("Particle bounds were not exercised or still republish every frame.");
                GD.Print($"OPENNV_PARTICLE_BOUNDS_PASS quads={checkedQuads} boundsPublications={particles.Sum(particle => particle.BoundsPublications)} sourceFrames=240 cameraAxes=3 conservative=true");
            }
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        finally { camera?.Free(); first.Free(); second.Free(); prototype.Scene.Root.Free(); }
    }

    private static int CheckParticleBounds(MultiMesh draw)
    {
        var count = draw.VisibleInstanceCount;
        if (count == 0) return 0;
        var buffer = draw.Buffer;
        if (buffer.Length != draw.InstanceCount * NativeParticleDrawBuffer.Stride)
            throw new InvalidDataException("Particle bounds validation requires native renderer buffer readback.");
        var bounds = draw.CustomAabb;
        for (var index = 0; index < count; index++)
        {
            var row = buffer.AsSpan(index * NativeParticleDrawBuffer.Stride);
            var position = new Vector3(row[3], row[7], row[11]);
            var extent = new Vector3(MathF.Abs(row[0]) + MathF.Abs(row[1]),
                MathF.Abs(row[4]) + MathF.Abs(row[5]), MathF.Abs(row[8]) + MathF.Abs(row[9]));
            if (!bounds.HasPoint(position - extent) || !bounds.HasPoint(position + extent))
                throw new InvalidDataException("Culling bounds clipped a live source particle quad.");
        }
        return count;
    }
}
