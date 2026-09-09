using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

public partial class NativeShotEffectsAudit : Node3D
{
    public override async void _Ready()
    {
        try
        {
            var args = OS.GetCmdlineUserArgs();
            if (args.Length != 2) throw new ArgumentException("Expected owned Data and a real weapon checkpoint.");
            RuntimeLiveContentSource.Configure(args[0], RuntimeLiveContentSource.FalloutNewVegasGame);
            using var content = RuntimeLiveContentSource.Current!;
            using var records = FalloutPluginStack.Load(content.PluginSources);
            FalloutAddonNodes.Bind(content, records);
            var saved = JsonSerializer.Deserialize<FalloutNativeCampaignState>(File.ReadAllText(args[1]))!;
            var form = saved.EquippedRuntimeFormIds.Select(records.RuntimeFormKey).Single(key => records.GetEffective(key).Signature == "WEAP");
            var weapon = FalloutWeaponPresentation.Read(records, form);
            var magazine = saved.WeaponHandling!.Magazines.Single(row => row.Weapon == form);
            var shot = FalloutWeaponShot.Read(records, form, magazine.Ammunition);
            const float units = .0142875f;
            CheckGrowFade();
            await CheckImpactMaterials(content, units);
            var camera = new Camera3D { Position = new(0, 1.7f, 0) }; AddChild(camera);
            long[] ExerciseMuzzle(double[] steps)
            {
                if (!content.TryRead(shot.Projectile.MuzzleFlash!, null, out var bytes, out _)) throw new FileNotFoundException("Muzzle model missing.");
                var root = NativeNifMeshBuilder.Build(bytes, units).Root; AddChild(root);
                try
                {
                    var clock = new NativeNifEffectPlayback(root, shot.Projectile.MuzzleSeconds, false); clock.Start();
                    foreach (var step in steps) clock.Advance(step);
                    var births = clock.Particles.Select(p => p.BirthCount).ToArray();
                    if (births.Length == 0 || births.Sum() == 0 || clock.Particles.Any(p => p.EmissionEnabled))
                        throw new InvalidOperationException("Muzzle source emission was skipped or did not stop.");
                    clock.Advance(10);
                    if (clock.Active || clock.Particles.Any(p => p.ActiveCount != 0)) throw new InvalidOperationException("Expired muzzle retained particles.");
                    return births;
                }
                finally { root.Free(); }
            }
            var regular = ExerciseMuzzle(Enumerable.Repeat(1.0 / 120, 30).ToArray());
            var hitch = ExerciseMuzzle([.25]);
            if (!regular.SequenceEqual(hitch)) throw new InvalidOperationException("Host hitch changed the source emission count.");
            GD.Print($"OPENNV_MUZZLE_CLOCK_PASS weapon={form} particles={string.Join(',', regular)} hitchSeconds=.25 expired=true");
            var blood = FalloutImpact.Resolve(records, shot.ImpactDataSet!.Value, 6)!;
            if (!content.TryRead(blood.Model!, null, out var bloodBytes, out _)) throw new FileNotFoundException("Blood impact model missing.");
            var bloodRoot = NativeNifMeshBuilder.Build(bloodBytes, units).Root;
            bloodRoot.Position = new(0, 0, -2); AddChild(bloodRoot);
            try
            {
                var playback = new NativeNifEffectPlayback(bloodRoot, blood.Duration, false); playback.Start();
                playback.Advance(.1);
                if (playback.Particles.Length == 0 || playback.Particles.Sum(particle => particle.ActiveCount) == 0)
                    throw new InvalidDataException("Owned blood effect did not emit its particles.");
                foreach (var particle in playback.Particles)
                {
                    var visual = particle.FindChildren("*", "MultiMeshInstance3D", true, false).OfType<MultiMeshInstance3D>().Single();
                    if (!visual.IsVisibleInTree() || visual.Multimesh.VisibleInstanceCount != particle.ActiveCount)
                        throw new InvalidDataException("Live blood particles lost their draw owner.");
                    for (var i = 0; i < particle.ActiveCount; i++)
                        if (visual.Multimesh.GetInstanceTransform(i).Basis.X.LengthSquared() == 0)
                            throw new InvalidDataException("A live blood particle collapsed to zero size.");
                }
                playback.Advance(10);
                if (playback.Active) throw new InvalidDataException("Blood particles did not expire.");
                var births = playback.Particles.Select(particle => particle.BirthCount).ToArray();
                bloodRoot.Position = new(8, 0, 0);
                playback.RestartCompleted(); playback.Advance(.1);
                if (!births.SequenceEqual(playback.Particles.Select(particle => particle.BirthCount)) ||
                    playback.Particles.Any(particle => particle.Positions.Any(point => point.X < 4)))
                    throw new InvalidDataException("Recycled blood retained an old emission remainder, count or world position.");
                try { playback.RestartCompleted(); throw new InvalidDataException("Live particles were recycled."); }
                catch (InvalidOperationException) { }
                playback.Advance(10);
                GD.Print($"OPENNV_BLOOD_PARTICLES_PASS form={blood.Form} sourceSize=true tail=true expired=true pixels=separate");
            }
            finally { bloodRoot.Free(); }
            var shooter = new CharacterBody3D(); AddChild(shooter);
            var effects = new RuntimeNativeShotEffects(records, content, units, shooter, 1); AddChild(effects);
            try
            {
                effects.PrepareShell(weapon);
                effects.EjectCasing(new(new Basis(Vector3.Up, .7f), new(0, 2, -1)), camera.GlobalPosition);
                var casing = effects.FindChildren("*", "", true, false).OfType<RuntimeNifRigidBody>().Single();
                var initial = casing.GlobalPosition;
                if (casing.LinearVelocity.Length() == 0 || casing.AngularVelocity.Length() == 0 || casing.CollisionLayer != 0)
                    throw new InvalidOperationException("Casing has no source velocity/spin or blocks gameplay queries.");
                for (var frame = 0; frame < 8; frame++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                if (casing.GlobalPosition.DistanceTo(initial) < .01) throw new InvalidOperationException("Source casing is not moving in world space.");
                GD.Print($"OPENNV_CASING_PHYSICS_PASS model={weapon.ShellModel} displacement={casing.GlobalPosition.DistanceTo(initial)} sourceAxis=+Z");
                foreach (var material in new[] { 0, 1, 3, 4, 5, 6, 9 })
                {
                    var impact = FalloutImpact.Resolve(records, shot.ImpactDataSet!.Value, material) ?? throw new InvalidDataException("Selected source impact is absent.");
                    effects.Impact(impact, new(0, 0, -2), Vector3.Up, Vector3.Down);
                    GD.Print($"OPENNV_IMPACT_ASSEMBLY_PASS material={material} form={impact.Form} model={impact.Model}");
                }
                for (var frame = 0; frame < 3; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                var pool = new RuntimeNativeShotEffects(records, content, units, shooter, 1); AddChild(pool); pool.SetProcess(false);
                try
                {
                    // Isolate recycling from wall-clock audio completion. The
                    // ordinary gameplay pass checks the original impact voices.
                    var silentBlood = blood with { Sounds = [] };
                    pool.Impact(silentBlood, Vector3.Zero, Vector3.Up, Vector3.Down);
                    pool.Impact(silentBlood, new(8, 0, 0), Vector3.Up, Vector3.Down);
                    Node3D[] Instances() => pool.GetChildren().OfType<Node3D>().Where(node => node is not NativeOwnedAnimationSoundPlayer).ToArray();
                    var simultaneous = Instances();
                    if (simultaneous.Length != 2 || simultaneous[0] == simultaneous[1])
                        throw new InvalidDataException("Concurrent impacts shared a live instance.");
                    pool._Process(10);
                    var retained = Instances().Single();
                    pool.Impact(silentBlood, new(16, 0, 0), Vector3.Up, Vector3.Down);
                    if (Instances().Single() != retained || retained.GlobalPosition.X != 16)
                        throw new InvalidDataException("Completed source impact did not reuse its own instance at the new hit.");
                    pool._Process(.1);
                    var particles = retained.FindChildren("*", "", true, false).OfType<RuntimeNifParticleSystem>().ToArray();
                    if (particles.All(particle => particle.ActiveCount == 0))
                        throw new InvalidDataException("Reused impact has no visible particles.");
                    pool._Process(10);
                    if (Instances().Length != 1 || retained.Visible)
                        throw new InvalidDataException("The completed impact pool is unbounded or still visible.");
                    GD.Print("OPENNV_IMPACT_REUSE_PASS concurrent=independent retained=one clocks=reset source=blood-model audio=separate");
                }
                finally { pool.Free(); }
            }
            finally { effects.Free(); shooter.Free(); camera.Free(); }
            GC.Collect(); GC.WaitForPendingFinalizers();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            GD.Print("OPENNV_NATIVE_SHOT_EFFECTS_AUDIT_PASS visual-acceptance=separate gameplay=separate");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private static void CheckGrowFade()
    {
        var header = new FalloutNifParticleModifier(new(0, "NiPSysGrowFadeModifier", 0, 0), "scale", 3000, 0, true);
        var source = new FalloutNifParticleGrowFade(header, 2, 0, 4, 0, 0);
        void Require(float expected, float age, float life, ushort generation = 0)
        {
            var actual = RuntimeNifParticleSystem.GrowFadeScale(source, age, life, generation);
            if (MathF.Abs(actual - expected) > .000001f) throw new InvalidDataException($"Particle scale {actual} differs from {expected}.");
        }
        Require(.0001f, 0, 10); Require(.5f, 1, 10); Require(1, 3, 10);
        Require(.75f, 1.5f, 5); // overlapping ramps choose the smaller, not their product
        Require(.25f, 9, 10); Require(.0001f, 10, 10); Require(1, 0, 10, 1);
        source = source with { Scale = .3f }; Require(.3f, 0, 10); Require(.65f, 1, 10);
        source = source with { Scale = 1 }; Require(1, 0, 10); Require(1, 9, 10);
        source = source with { Scale = 0, Grow = 0, Fade = 0 }; Require(1, 0, 10);
        GD.Print("OPENNV_PARTICLE_SCALE_CONTRACT_PASS zeroBase=true baseEndpoint=true overlappingEnvelopes=true generation=true minimum=true");
    }

    private async Task CheckImpactMaterials(RuntimeLiveContentSource content, float units)
    {
        var synthetic = new FalloutNifPackedData(new(0, "hkPackedNiTriStripsData", 0, 0), new FalloutNifVector3[6],
            [new(0, 1, 2, 0, default), new(3, 4, 5, 0, default), new(0, 3, 5, 0, default)],
            [new(default, 3, 2), new(default, 3, 5)]);
        if (!NativeNifCollisionBuilder.PackedMaterials(synthetic, synthetic.SubShapes).SequenceEqual(new[] { 2, 5, -1 }))
            throw new InvalidOperationException("Packed material ranges lost source triangles or accepted an ambiguous face.");
        const string model = "meshes/architecture/NCR/SpinningWindmill.NIF";
        if (!content.TryRead(model, null, out var bytes, out _)) throw new FileNotFoundException(model);
        var root = NativeNifMeshBuilder.Build(bytes, units).Root; AddChild(root);
        try
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            var measured = new HashSet<uint>();
            foreach (var shape in root.FindChildren("*", "", true, false).OfType<CollisionShape3D>().Where(s => s.HasMeta("opennv_havok_face_materials")))
            {
                var faces = ((ConcavePolygonShape3D)shape.Shape).Data;
                var materials = shape.GetMeta("opennv_havok_face_materials").AsInt32Array();
                if (faces.Length != materials.Length * 3) throw new InvalidOperationException("Physics face order changed.");
                for (var face = 0; face < materials.Length; face++)
                {
                    var expected = materials[face];
                    if (expected < 0 || measured.Contains((uint)expected)) continue;
                    var a = shape.ToGlobal(faces[face * 3]); var b = shape.ToGlobal(faces[face * 3 + 1]); var c = shape.ToGlobal(faces[face * 3 + 2]);
                    var normal = (b - a).Cross(c - a).Normalized();
                    if (normal.LengthSquared() < .99f) continue;
                    var center = (a + b + c) / 3;
                    using var query = PhysicsRayQueryParameters3D.Create(center + normal * .001f, center - normal * .001f);
                    var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
                    if (hit.Count == 0) throw new InvalidOperationException("Source packed triangle rejected its direct ray.");
                    if (NativeNifCollisionBuilder.HitMaterial(hit) != expected)
                        throw new InvalidOperationException("Physics ray did not retain its source face material.");
                    measured.Add((uint)expected);
                }
            }
            if (measured.Count < 2) throw new InvalidOperationException("Owned mixed-material collision was not exercised.");
            GD.Print($"OPENNV_IMPACT_MATERIAL_PASS model={model} sourceMaterials={string.Join(',', measured.Order())} ambiguous=rejected");
        }
        finally { root.Free(); }
    }
}
