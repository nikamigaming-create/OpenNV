using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

public partial class NativeNifInstanceAudit
{
    private Task ExerciseParticleGravity(string? game = null, string? model = null)
    {
        ExerciseSyntheticParticleGravity();
        if (game is not null && model is not null) ExerciseOwnedParticleGravity(game, model);
        else if (game is not null || model is not null) throw new ArgumentException("Owned gravity audit requires installation and model.");
        return Task.CompletedTask;
    }

    private void ExerciseSyntheticParticleGravity()
    {
        var bytes = ParticleGravityFixture();
        var hash = SHA256.HashData(bytes);
        var source = FalloutNifFile.Read(bytes);
        var controller = (FalloutNifParticleController)source.ReadObject(17);
        if (controller.Block.Size != 34 || controller.Time.Target != 0 || controller.Interpolator != 6 ||
            controller.Modifier != "Gravity" || controller.Time.Flags != 108)
            throw new InvalidDataException("Gravity scalar decoder lost its authored layout.");
        var fixture = new Node3D(); AddChild(fixture);
        var link = new FalloutNifControllerLink("Particles", "", "NiPSysGravityStrengthCtlr", "Gravity", "", 21, 17, 0);
        try
        {
            var first = Create(source); var sibling = Create(source);
            // Direct clocks have their own decoded, unmanaged declaration.
            // The separately checked managed receipt above retains flags 108.
            var directSource = FalloutNifFile.Read(ParticleGravityFixture(mode: "direct"));
            var directController = (FalloutNifParticleController)directSource.ReadObject(17);
            if (directController.Time.Flags != 76) throw new InvalidDataException("Direct gravity fixture retained a manager flag.");
            FalloutNifControllerClock.Validate(directController.Time);
            var directRuntime = Create(directSource);
            var direct = new RuntimeNifControllerPlayer();
            direct.Configure([new RuntimeNifControllerSequence("DirectGravity", 2, 1, 0, 1,
                [directRuntime.Bind(directSource, link with { Interpolator = directController.Interpolator })]) { DirectClock = directController.Time }]);
            fixture.AddChild(direct); direct.PlaySourceSequence("DirectGravity"); direct.SeekSourceTime(.5);
            direct.SetProcess(false);
            Near(GravityStrength(directRuntime, "Gravity"), 4);
            var channel = first.Bind(source, link);
            channel.Apply(.25f); Near(GravityStrength(first, "Gravity"), 2);
            Near(GravityStrength(sibling, "Gravity"), 6);
            if (((FalloutNifParticleGravity)source.ReadObject(16)).Strength != 6 || controller.Interpolator != 6)
                throw new InvalidDataException("Sequence override changed immutable gravity/controller declarations.");
            first.Bind(source, link with { Interpolator = 18 }).Apply(0);
            Near(GravityStrength(first, "Gravity"), -3);
            first.Bind(source, link with { ControllerType = "NiPSysEmitterCtlr", Variable1 = "Emitter", Variable2 = "BirthRate", Interpolator = 7 }).Apply(0);
            Advance(first, 1f / 60);
            if (first.ActiveCount == 0) throw new InvalidDataException("Gravity fixture emitted no particles.");
            first.EmissionEnabled = false;
            var before = GravityVelocity(first);
            Advance(first, 1f / 60);
            var expected = GamebryoCoordinate.ConvertVector(Vector3.Back).Normalized() * (-3f / 60);
            Near((GravityVelocity(first) - before - expected).Length(), 0);
            if (sibling.BirthCount != 0) throw new InvalidDataException("Gravity simulation advanced a sibling.");
            Reject(() => first.Bind(source, link with { Variable1 = "Emitter" }));
            Reject(() => first.Bind(source, link with { Variable2 = "Strength" }));
            Reject(() => first.Bind(source, link with { PropertyType = "NiMaterialProperty" }));
            Reject(() => first.Bind(source, link with { Priority = 1 }));
            Reject(() => first.Bind(source, link with { Controller = 9 }));
            Reject(() => first.Bind(source, link with { Interpolator = 8 }));
            Reject(() => first.Bind(source, link with { Interpolator = 20 }).Apply(0));
            Reject(() => first.Bind(FalloutNifFile.Read(bytes), link));
            foreach (var mode in new[] { "target", "type", "flags" })
            {
                var invalid = FalloutNifFile.Read(ParticleGravityFixture(mode: mode));
                var runtime = Create(invalid);
                Reject(() => runtime.Bind(invalid, link));
            }
            foreach (var mode in new[] { "truncated", "trailing" })
                Reject(() => FalloutNifFile.Read(ParticleGravityFixture(mode: mode)).ReadObject(17));
            Advance(first, 11); first.ResetCompleted();
            Near(GravityStrength(first, "Gravity"), 6);
            Near(GravityStrength(sibling, "Gravity"), 6);
            var retained = first.Bind(source, link);
            first.Free();
            try { retained.Apply(.5f); throw new InvalidDataException("Retired gravity channel remained writable."); }
            catch (InvalidOperationException) { }
            Near(GravityStrength(sibling, "Gravity"), 6);

            var turbulentSource = FalloutNifFile.Read(ParticleGravityFixture(turbulence: 5));
            var turbulent = Create(turbulentSource);
            var zero = turbulent.Bind(turbulentSource, link with { Interpolator = 19 }); zero.Apply(0);
            turbulent.Bind(turbulentSource, link with { ControllerType = "NiPSysEmitterCtlr", Variable1 = "Emitter", Variable2 = "BirthRate", Interpolator = 7 }).Apply(0);
            Advance(turbulent, 1f / 60); turbulent.EmissionEnabled = false;
            Advance(turbulent, .1f);
            Near(GravityVelocity(turbulent).Length(), 0);
            turbulent.Bind(turbulentSource, link with { Interpolator = 18 }).Apply(0);
            Advance(turbulent, .1f);
            var velocity = GravityVelocity(turbulent);
            var axis = GamebryoCoordinate.ConvertVector(Vector3.Back).Normalized();
            if ((velocity - axis * velocity.Dot(axis)).Length() <= 1e-8f)
                throw new InvalidDataException("Animated strength did not reach turbulent acceleration.");
            if (!SHA256.HashData(bytes).SequenceEqual(hash)) throw new InvalidDataException("Gravity audit changed source bytes.");
            GD.Print("OPENNV_PARTICLE_GRAVITY_PASS scalarLayout=true sequenceInterpolatorOverride=true signedAcceleration=true turbulenceZeroAndNonzero=true independentInstances=true reset=true retiredChannelRefused=true invalidOwnersAndLayoutsRefused=true populationCold=unverified pixels=unverified");
        }
        finally { fixture.Free(); }

        RuntimeNifParticleSystem Create(FalloutNifFile file)
        {
            var runtime = new RuntimeNifParticleSystem();
            var emitter = new Node3D { Position = Vector3.Up }; fixture.AddChild(emitter);
            try
            {
                runtime.Configure(file, (FalloutNifParticleSystem)file.ReadObject(0), new Dictionary<int, Node3D>
                { [0] = runtime, [10] = emitter }, new ShaderMaterial
                { ResourceName = NativeNifEffectMaterial.ResourceIdentity, Shader = new Shader { Code = "shader_type spatial; uniform bool source_particle_atlas;" } }, 1);
                fixture.AddChild(runtime); runtime.SetProcess(false); return runtime;
            }
            catch { runtime.Free(); throw; }
        }
        static void Advance(RuntimeNifParticleSystem runtime, float seconds)
        { runtime.BeginParentMotion(seconds); runtime.Advance(seconds); runtime.EndParentMotion(); }
        static void Near(float actual, float expected)
        {
            if (!float.IsFinite(actual) || MathF.Abs(actual - expected) > .00001f)
                throw new InvalidDataException($"Gravity scalar/simulation differs: {actual} versus {expected}.");
        }
        static void Reject(Action action)
        {
            try { action(); }
            catch (Exception error) when (error is InvalidDataException or NotSupportedException or EndOfStreamException) { return; }
            throw new InvalidOperationException("Invalid gravity declaration/binding was admitted.");
        }
    }

    private void ExerciseOwnedParticleGravity(string game, string model)
    {
        RuntimeLiveContentSource.Configure(game, RuntimeLiveContentSource.FalloutNewVegasGame,
            activePlugins: RuntimeLiveContentSource.ResolveVanillaPlugins(game), includeImplicitPlugins: false);
        var content = RuntimeLiveContentSource.Current!;
        if (!content.TryRead(model, null, out var bytes, out var identity)) throw new FileNotFoundException(model);
        var hash = SHA256.HashData(bytes); var source = FalloutNifFile.Read(bytes);
        var prototype = new RuntimeNativeNifPrototype(source, .0142875f);
        Node3D? first = null, cold = null;
        try
        {
            first = prototype.Instantiate(); cold = prototype.Instantiate();
            first.ProcessMode = cold.ProcessMode = ProcessModeEnum.Disabled;
            AddChild(first); AddChild(cold);
            var players = first.FindChildren("*", "", true, false).OfType<RuntimeNifControllerPlayer>()
                .Where(player => player.SourceController >= 0 && source.ReadObject(player.SourceController) is FalloutNifControllerManager).ToArray();
            var samples = 0;
            foreach (var player in players)
            {
                var manager = (FalloutNifControllerManager)source.ReadObject(player.SourceController);
                var restored = cold.FindChildren("*", "", true, false).OfType<RuntimeNifControllerPlayer>()
                    .Single(value => value.SourceController == player.SourceController);
                foreach (var sequenceIndex in manager.Sequences)
                {
                    var sequence = source.ReadControllerSequence(sequenceIndex);
                    var links = sequence.ControlledBlocks.Where(link => link.ControllerType == "NiPSysGravityStrengthCtlr").ToArray();
                    if (links.Length == 0) continue;
                    var untouched = GravityState(cold); var original = GravityState(prototype.Scene.Root);
                    player.RequestSourceSequence(sequence.Name, 1);
                    foreach (var fraction in new[] { .271, .713 })
                    {
                        player.SeekSourceTime(sequence.StartTime + (sequence.StopTime - sequence.StartTime) * fraction);
                        samples += Check(first, player, links);
                        if (GravityState(cold) != untouched || GravityState(prototype.Scene.Root) != original)
                            throw new InvalidDataException("Managed gravity publication mutated sibling/prototype state.");
                    }
                    var saved = JsonSerializer.Deserialize<FalloutObjectAnimationSnapshot>(JsonSerializer.Serialize(player.CaptureObjectState()))!;
                    restored.RestoreObjectState(saved);
                    if (JsonSerializer.Serialize(restored.CaptureObjectState()) != JsonSerializer.Serialize(saved))
                        throw new InvalidDataException("Cold gravity channel changed its source clock.");
                    samples += Check(cold, restored, links);
                    var step = (sequence.StopTime - sequence.StartTime) * .05;
                    player._Process(step); restored._Process(step);
                    if (player.SourceTimeSeconds != restored.SourceTimeSeconds)
                        throw new InvalidDataException("Cold gravity clock suffix diverged.");
                    samples += Check(first, player, links); samples += Check(cold, restored, links);
                }
            }
            if (samples == 0) throw new InvalidDataException("Owned model exercised no managed gravity channels.");
            var retained = GravityState(cold); first.Free(); first = null;
            if (GravityState(cold) != retained) throw new InvalidDataException("Retiring one model changed sibling gravity.");
            if (!content.TryRead(model, null, out var after, out var afterIdentity) || identity != afterIdentity ||
                !SHA256.HashData(after).SequenceEqual(hash)) throw new InvalidDataException("Owned gravity source changed.");
            GD.Print($"OPENNV_OWNED_PARTICLE_GRAVITY_PASS source={identity} sha256={source.Sha256} samples={samples} wholeModel=true prototypeAndSiblingIsolation=true coldClockAndChannel=true retirement=true particlePopulationCold=unverified ordinaryGameplay=unverified pixels=unverified");
        }
        finally { first?.Free(); cold?.Free(); prototype.Scene.Root.Free(); }

        int Check(Node3D root, RuntimeNifControllerPlayer player, FalloutNifControllerLink[] links)
        {
            var file = source;
            foreach (var link in links)
            {
                var declaration = (FalloutNifParticleController)file.ReadObject(link.Controller);
                var particle = root.FindChildren("*", "", true, false).OfType<RuntimeNifParticleSystem>()
                    .Single(value => value.GetMeta("opennv_nif_block").AsInt32() == declaration.Time.Target);
                var expected = new FalloutNifFloatAnimation(file, link.Interpolator).Sample((float)player.SourceTimeSeconds);
                var actual = GravityStrength(particle, link.Variable1);
                if (!float.IsFinite(actual) || !float.IsFinite(expected) || actual != expected)
                    throw new InvalidDataException("Managed gravity differs from the source sequence interpolator.");
            }
            return links.Length;
        }
    }

    private static float GravityStrength(RuntimeNifParticleSystem runtime, string name) => JsonSerializer.SerializeToElement(runtime.Observation)
        .GetProperty("modifiers").EnumerateArray().Single(value => value.GetProperty("Name").GetString() == name).GetProperty("gravityStrength").GetSingle();

    private static string GravityState(Node3D root) => JsonSerializer.Serialize(root.FindChildren("*", "", true, false)
        .OfType<RuntimeNifParticleSystem>().Select(value => JsonSerializer.SerializeToElement(value.Observation).GetProperty("modifiers")));

    private static Vector3 GravityVelocity(RuntimeNifParticleSystem runtime)
    {
        var bytes = Convert.FromBase64String(JsonSerializer.SerializeToElement(runtime.Observation).GetProperty("state").GetString()!);
        return new(BitConverter.ToSingle(bytes, 12), BitConverter.ToSingle(bytes, 16), BitConverter.ToSingle(bytes, 20));
    }

    private static byte[] ParticleGravityFixture(float turbulence = 0, string mode = "valid")
    {
        var bytes = ParticleChannelFixture(); var baseline = FalloutNifFile.Read(bytes);
        var blocks = baseline.Blocks.Select(block => (block.TypeName, Bytes: bytes.AsSpan(block.Offset, block.Size).ToArray())).ToList();
        var system = (FalloutNifParticleSystem)baseline.ReadObject(0); var body = blocks[0].Bytes;
        blocks[0] = ("NiParticleSystem", Bytes(writer =>
        {
            writer.Write(body.AsSpan(0, body.Length - 4 - 4 * system.Modifiers.Length));
            writer.Write(5); foreach (var index in new[] { 2, 3, 4, 5, 16 }) writer.Write(index);
        }));
        var emitter = blocks[2].Bytes.ToArray();
        BinaryPrimitives.WriteSingleLittleEndian(emitter.AsSpan(13), 0);
        BinaryPrimitives.WriteSingleLittleEndian(emitter.AsSpan(13 + 12 * 4), 10);
        blocks[2] = ("NiPSysBoxEmitter", emitter);
        var names = baseline.Strings.Concat(new[] { "Gravity" }).ToArray();
        blocks.Add(("NiPSysGravityModifier", Bytes(writer =>
        {
            writer.Write(names.Length - 1); writer.Write(4000U); writer.Write(0); writer.Write(true);
            writer.Write(10); foreach (var value in new[] { 0f, 0f, 1f, 0f, 6f }) writer.Write(value);
            writer.Write(0U); writer.Write(turbulence); writer.Write(1f); writer.Write(true);
        })));
        var controller = Bytes(writer =>
        {
            writer.Write(-1); writer.Write((ushort)(mode == "flags" ? 0 : mode == "direct" ? 76 : 108));
            foreach (var value in new[] { 1f, 0f, 0f, 1f }) writer.Write(value);
            writer.Write(mode == "target" ? 10 : 0); writer.Write(6); writer.Write(names.Length - 1);
        });
        if (mode == "truncated") controller = controller[..^1];
        if (mode == "trailing") controller = [.. controller, 0];
        blocks.Add((mode == "type" ? "NiPSysEmitterSpeedCtlr" : "NiPSysGravityStrengthCtlr", controller));
        foreach (var value in new[] { -3f, 0f, float.NaN })
            blocks.Add(("NiFloatInterpolator", Bytes(writer => { writer.Write(value); writer.Write(-1); })));
        blocks.Add(("NiFloatInterpolator", Bytes(writer => { writer.Write(0f); writer.Write(22); })));
        blocks.Add(("NiFloatData", Bytes(writer =>
        {
            writer.Write(2U); writer.Write(1U); writer.Write(0f); writer.Write(0f); writer.Write(1f); writer.Write(8f);
        })));
        return WriteParticleShapeColorNif(blocks, names);
    }
}
