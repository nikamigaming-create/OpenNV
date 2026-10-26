using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Formats.Gamebryo;

public partial class NativeNifInstanceAudit
{
    private void ExerciseParticleTransformCache()
    {
        ExerciseParticleDrawOrdering(); // Pure ordering/allocation proof also runs with the dummy renderer.
        var nativeReadbackRequired = DisplayServer.GetName() != "headless";
        var nativeReadbackFrames = 0;
        var unavailableHeadlessFrames = 0;
        var fixture = new Node3D(); AddChild(fixture);
        var resources = new List<Resource>();
        var randomField = typeof(RuntimeNifParticleSystem).GetField("_random", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var cacheField = typeof(RuntimeNifParticleSystem).GetField("_stepTransforms", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var camera = new Camera3D { Current = true, Position = new(3, 4, 8) }; fixture.AddChild(camera);
        try
        {
            foreach (var worldSpace in new[] { false, true })
            {
                var bytes = ParticleGravityFixture();
                var original = FalloutNifFile.Read(bytes);
                var system = (FalloutNifParticleSystem)original.ReadObject(0);
                // The final geometry byte precedes the modifier count/table.
                // Retain the same synthetic source in both simulation owners.
                bytes[system.Block.Offset + system.Block.Size - 5 - 4 * system.Modifiers.Length] = worldSpace ? (byte)1 : (byte)0;
                var hash = SHA256.HashData(bytes);
                var source = FalloutNifFile.Read(bytes);
                var capacity = ((FalloutNifParticleData)source.ReadObject(system.Geometry.Data)).Maximum;
                Require(((FalloutNifParticleSystem)source.ReadObject(0)).WorldSpace == worldSpace, "Synthetic particle space was not decoded.");
                var cached = Create(source); var observed = Create(source);
                var events = new List<FalloutNifParticleLifecycleEvent>();
                observed.Runtime.LifecycleEvent += events.Add; // Fresh-read reference path.
                for (var frame = 0; frame < 120; frame++)
                {
                    var pose = new Transform3D(new Basis(Quaternion.FromEuler(new(frame * .013f, frame * .027f, frame * .009f)))
                        .Scaled(Vector3.One * (1 + frame * .001f)), new(frame * .03f, frame * .017f, -frame * .021f));
                    cached.Root.Transform = observed.Root.Transform = pose;
                    cached.Emitter.Position = observed.Emitter.Position = new(MathF.Sin(frame * .1f), 1 + frame * .01f, MathF.Cos(frame * .07f));
                    Advance(cached.Runtime, .1f); Advance(observed.Runtime, .1f);
                    var cachedState = JsonSerializer.SerializeToElement(cached.Runtime.Observation);
                    var observedState = JsonSerializer.SerializeToElement(observed.Runtime.Observation);
                    Require(cachedState.GetProperty("state").GetString() == observedState.GetProperty("state").GetString(),
                        "Cached node poses changed exact particle position, velocity, age, color or source state.");
                    foreach (var name in new[] { "active", "births", "deaths", "collisions", "seconds" })
                        Require(cachedState.GetProperty(name).GetRawText() == observedState.GetProperty(name).GetRawText(),
                            "Cached node poses changed particle counts or clocks.");
                    var cachedLife = cachedState.GetProperty("lifecycle"); var observedLife = observedState.GetProperty("lifecycle");
                    foreach (var property in cachedLife.EnumerateObject().Where(property => property.Name is not ("eventSink" or "LifecycleEventsWithoutSink")))
                        Require(property.Value.GetRawText() == observedLife.GetProperty(property.Name).GetRawText(),
                            "Cached node poses changed particle identity, generation or lifecycle accounting.");
                    Require(cached.Runtime.LifecycleEventsWithoutSink == events.Count,
                        "The cached path omitted a lifecycle event from its explicit unbound-sink accounting.");
                    cached.Runtime.Publish(); observed.Runtime.Publish();
                    var cachedBuffer = cached.Draw.Buffer; var observedBuffer = observed.Draw.Buffer;
                    if (!nativeReadbackRequired && cachedBuffer.Length == 0 && observedBuffer.Length == 0)
                    {
                        unavailableHeadlessFrames++;
                    }
                    else
                    {
                        Require(cached.Draw.InstanceCount == capacity && observed.Draw.InstanceCount == capacity &&
                            cachedBuffer.Length == capacity * NativeParticleDrawBuffer.Stride && observedBuffer.Length == cachedBuffer.Length,
                            "Active renderer did not expose the complete source-capacity particle buffers.");
                        Require(MemoryMarshal.AsBytes(cachedBuffer.AsSpan()).SequenceEqual(MemoryMarshal.AsBytes(observedBuffer.AsSpan())),
                            "Cached node poses changed exact native packed draw bytes.");
                        var positions = cached.Runtime.Positions;
                        var origin = worldSpace ? camera.GlobalPosition : cached.Runtime.ToLocal(camera.GlobalPosition);
                        var drawOrder = Enumerable.Range(0, positions.Count).ToArray();
                        Array.Sort(drawOrder, (a, b) =>
                        {
                            var distance = positions[b].DistanceSquaredTo(origin).CompareTo(positions[a].DistanceSquaredTo(origin));
                            return distance != 0 ? distance : a.CompareTo(b);
                        });
                        for (var row = 0; row < drawOrder.Length; ++row)
                        {
                            var expectedPosition = positions[drawOrder[row]];
                            var packed = row * NativeParticleDrawBuffer.Stride;
                            Require(BitConverter.SingleToInt32Bits(cachedBuffer[packed + 3]) == BitConverter.SingleToInt32Bits(expectedPosition.X) &&
                                BitConverter.SingleToInt32Bits(cachedBuffer[packed + 7]) == BitConverter.SingleToInt32Bits(expectedPosition.Y) &&
                                BitConverter.SingleToInt32Bits(cachedBuffer[packed + 11]) == BitConverter.SingleToInt32Bits(expectedPosition.Z),
                                "Native particle publication changed the reference distance/index order or packed position bits.");
                        }
                        nativeReadbackFrames++;
                    }
                }
                Require(cached.Runtime.BirthCount > 32 && cached.Runtime.DeathCount > 0 && events.Count > 0 &&
                    ((Dictionary<int, Transform3D>)cacheField.GetValue(cached.Runtime)!).Count > 0 &&
                    ((Dictionary<int, Transform3D>)cacheField.GetValue(observed.Runtime)!).Count == 0,
                    "Pose-cache comparison did not exercise caching, observer bypass, birth and death.");
                Require(((Random)randomField.GetValue(cached.Runtime)!).NextInt64() == ((Random)randomField.GetValue(observed.Runtime)!).NextInt64() &&
                    hash.AsSpan().SequenceEqual(SHA256.HashData(bytes)), "Node pose caching changed random consumption or source bytes.");
            }

            var callback = Create(FalloutNifFile.Read(ParticleGravityFixture()));
            callback.Emitter.Position = Vector3.Zero;
            var births = new List<float>();
            callback.Runtime.LifecycleEvent += value =>
            {
                if (value.Kind != "birth") return;
                births.Add(value.Position.X); callback.Emitter.Position += Vector3.Right;
            };
            Advance(callback.Runtime, .05f);
            Require(births.Count >= 2 && births.Select((position, index) => position == index).All(value => value),
                "A lifecycle callback's emitter movement was hidden by a cached pose.");
            var readback = nativeReadbackFrames > 0 && unavailableHeadlessFrames == 0 ? "verified" : "unavailable-headless";
            GD.Print("OPENNV_PARTICLE_TRANSFORM_CACHE_PASS localAndWorld=true changingParentPoses=true changingEmitterPoses=true " +
                $"exactSimulationAndIdentityState=true lifecycleCounts=true randomConsumption=true exactNativeBuffer={readback} referenceDrawOrder={readback} " +
                $"nativeReadbackFrames={nativeReadbackFrames} unavailableHeadlessFrames={unavailableHeadlessFrames} callbackMovesObserved=true sourceUnchanged=true pixels=unverified");

            (Node3D Root, Node3D Emitter, RuntimeNifParticleSystem Runtime, MultiMesh Draw) Create(FalloutNifFile source)
            {
                var root = new Node3D(); fixture.AddChild(root);
                var runtime = new RuntimeNifParticleSystem();
                randomField.SetValue(runtime, new Random(7319)); // Paired first-party fixture only.
                var emitter = new Node3D { Position = Vector3.Up }; root.AddChild(emitter);
                var shader = new Shader { Code = "shader_type spatial; uniform bool source_particle_atlas;" };
                var material = new ShaderMaterial { ResourceName = NativeNifEffectMaterial.ResourceIdentity, Shader = shader };
                resources.Add(shader); resources.Add(material);
                runtime.Configure(source, (FalloutNifParticleSystem)source.ReadObject(0), new Dictionary<int, Node3D>
                { [0] = runtime, [10] = emitter }, material, 1);
                root.AddChild(runtime); runtime.SetProcess(false);
                runtime.Bind(source, new("Particles", "", "NiPSysEmitterCtlr", "Emitter", "BirthRate", 7, 9, 0)).Apply(0);
                var draw = runtime.GetChildren().OfType<MultiMeshInstance3D>().Single().Multimesh;
                return (root, emitter, runtime, draw);
            }
        }
        finally
        {
            fixture.Free();
            for (var index = resources.Count - 1; index >= 0; index--) resources[index].Dispose();
        }

        static void Advance(RuntimeNifParticleSystem runtime, float seconds)
        { runtime.BeginParentMotion(seconds); runtime.Advance(seconds); runtime.EndParentMotion(); }
        static void Require(bool condition, string message)
        { if (!condition) throw new InvalidDataException("Particle transform cache: " + message); }
    }
}
