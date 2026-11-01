using Godot;

namespace OpenNV.Runtime.Formats.Gamebryo;

internal static partial class RuntimeNativeNifMeshBuilder
{
    private sealed partial class BuildState
    {
        private static readonly StringName ParticleColorMultiplierParameter = "source_color_multiplier";
        private readonly Dictionary<int, (FalloutNifParticleSystem Source, RuntimeNifParticleSystem Runtime)> _particles = [];

        private Node3D BuildParticleSystem(FalloutNifParticleSystem source)
        {
            var geometry = source.Geometry;
            if (geometry.SkinInstance != -1 || geometry.CollisionObject != -1 || geometry.MaterialNames.Length != 0 ||
                geometry.Dirty)
                throw new NotSupportedException($"Particle geometry {geometry.Block.Index} has unsupported attachments.");
            var transform = ConvertTransform(geometry.Transform);
            _ = ValidateExtraData(geometry.Block, geometry.ExtraData, collisionContract: false);
            var runtime = new RuntimeNifParticleSystem();
            var nodeAdded = false; var particleAdded = false;
            try
            {
                runtime.Name = $"NifParticleSystem{source.Block.Index}";
                runtime.Transform = transform;
                runtime.Visible = (geometry.Flags & 1) == 0;
                runtime.ProcessPriority = 1;
                runtime.SetMeta("opennv_nif_block", source.Block.Index);
                runtime.SetMeta("opennv_nif_source_name", geometry.Name);
                _nodes.Add(source.Block.Index, runtime); nodeAdded = true;
                _particles.Add(source.Block.Index, (source, runtime)); particleAdded = true;
                NodeCount++;
                return runtime;
            }
            catch
            {
                if (particleAdded) _particles.Remove(source.Block.Index);
                if (nodeAdded) _nodes.Remove(source.Block.Index);
                runtime.Free();
                throw;
            }
        }

        internal sealed record ParticleSetup(FalloutNifParticleSystem Source, RuntimeNifParticleSystem Runtime,
            List<FalloutNifParticleController> DirectEmitters);

        internal ParticleSetup? PrepareSingleParticleSystem()
        {
            if (_particles.Count == 0) return null;
            if (_particles.Count != 1) throw new InvalidOperationException("Deferred attachment requires at most one particle system.");
            var (source, runtime) = _particles.Values.Single();
            return PrepareParticleSystem(source, runtime);
        }

        private ParticleSetup PrepareParticleSystem(FalloutNifParticleSystem source, RuntimeNifParticleSystem runtime)
        {
            var seen = new HashSet<int>();
            var cursor = source.Geometry.Controller;
            var hasUpdate = false;
            var directEmitters = new List<FalloutNifParticleController>();
            while (cursor != -1)
            {
                if (!seen.Add(cursor)) throw new InvalidDataException("Particle controller chain contains a cycle.");
                if (_source.ReadObject(cursor) is FalloutNifVisibilityController visibility &&
                    visibility.Time.Target == source.Block.Index && visibility.Time.UnknownInteger == 0)
                {
                    if ((visibility.Time.Flags & 0x20) == 0) BuildDirectVisibilityController(visibility, runtime);
                    cursor = visibility.Time.NextController; continue;
                }
                if (_source.ReadObject(cursor) is not FalloutNifParticleController controller ||
                    controller.Time.Target != source.Block.Index || (controller.Time.Flags & 0x40) == 0)
                    throw new NotSupportedException($"Particle system {source.Block.Index} has an unbound controller chain.");
                hasUpdate |= controller.Block.TypeName == "NiPSysUpdateCtlr";
                if (controller.Block.TypeName == "BSPSysMultiTargetEmitterCtlr" &&
                    (controller.MaximumEmitters is null or 0 || controller.Master < 0 ||
                        _source.ReadNode(controller.Master).ParticleMaster is not { } master || !master.ParticleSystems.Contains(source.Block.Index)))
                    throw new InvalidDataException("Multi-target emitter has no matching source master/capacity.");
                // Scalar owners must bind or refuse through the
                // scalar sampler; an incompatible interpolator must never
                // silently remove a reached direct controller.
                if (controller.Block.TypeName is "NiPSysGravityStrengthCtlr" or "NiPSysEmitterInitialRadiusCtlr" &&
                    (controller.Time.Flags & 0x20) == 0)
                    directEmitters.Add(controller);
                else if (controller.Block.TypeName is "NiPSysEmitterCtlr" or "BSPSysMultiTargetEmitterCtlr" or "NiPSysEmitterSpeedCtlr" or "NiPSysEmitterLifeSpanCtlr" && (controller.Time.Flags & 0x20) == 0 &&
                    _source.ReadObject(controller.Interpolator) is FalloutNifFloatInterpolator)
                    directEmitters.Add(controller);
                cursor = controller.Time.NextController;
            }
            if (!hasUpdate) throw new NotSupportedException($"Particle system {source.Block.Index} has no update controller.");
            return new(source, runtime, directEmitters);
        }

        internal Material PrepareParticleMaterial(ParticleSetup setup) => BuildMaterial(setup.Source.Geometry);

        internal void BeginParticleSystem(ParticleSetup setup, Material material) =>
            setup.Runtime.BeginConfigure(_source, setup.Source, _nodes, material, _unitsToMetres);

        internal void FinishParticleSystem(ParticleSetup setup)
        {
            var (source, runtime, directEmitters) = setup;
            runtime.FinishConfigure();
            foreach (var controller in directEmitters)
            {
                // Effects can author dormant clocks. They are bound here,
                // and their impact/sever event explicitly starts playback.
                FalloutNifControllerClock.Validate(controller.Time, requireActive: false);
                var scalar = controller.Block.TypeName is "NiPSysEmitterSpeedCtlr" or "NiPSysEmitterLifeSpanCtlr" or
                    "NiPSysEmitterInitialRadiusCtlr" or "NiPSysGravityStrengthCtlr";
                var type = controller.Block.TypeName == "BSPSysMultiTargetEmitterCtlr" ? "NiPSysEmitterCtlr" : controller.Block.TypeName;
                RuntimeNifControllerChannel Channel(string variable, int interpolator) => runtime.Bind(_source,
                    new FalloutNifControllerLink(source.Geometry.Name, "", type, controller.Modifier,
                        variable, interpolator, controller.Block.Index, 0));
                _directControllerSequences.Add(new RuntimeNifControllerSequence($"DirectEmitter{controller.Block.Index}",
                    (uint)(controller.Time.Flags >> 1) & 3, controller.Time.Frequency, controller.Time.StartTime, controller.Time.StopTime,
                    scalar ? [Channel("", controller.Interpolator)] :
                        [Channel("BirthRate", controller.Interpolator), Channel("EmitterActive", controller.ActiveInterpolator)])
                { DirectClock = controller.Time });
            }
            SurfaceCount++;
        }

        private void ConfigureParticleSystems()
        {
            foreach (var (source, runtime) in _particles.Values)
            {
                var setup = PrepareParticleSystem(source, runtime);
                BeginParticleSystem(setup, PrepareParticleMaterial(setup));
                FinishParticleSystem(setup);
            }
        }

        private RuntimeNifControllerChannel BuildParticleChannel(FalloutNifControllerSequence sequence,
            FalloutNifControllerLink link, int target)
        {
            if (!_particles.TryGetValue(target, out var particle) || link.PropertyType.Length != 0 ||
                _source.ReadObject(link.Controller) is not FalloutNifParticleController controller ||
                controller.Block.TypeName != link.ControllerType || controller.Time.Target != target ||
                controller.Modifier != link.Variable1 || (controller.Time.Flags & 0x40) == 0)
                throw new NotSupportedException($"Particle channel {sequence.Name}/{link.NodeName} has no declared owner.");
            return particle.Runtime.Bind(_source, link);
        }

        private RuntimeNifControllerChannel BuildAlphaChannel(FalloutNifControllerSequence sequence,
            FalloutNifControllerLink link, int target)
        {
            var geometry = _source.ReadObject(target) switch
            {
                FalloutNifGeometry value => value,
                FalloutNifParticleSystem value => value.Geometry,
                _ => throw new NotSupportedException("Alpha channel target has no geometry."),
            };
            if (link.PropertyType != "NiMaterialProperty" || link.Variable1.Length != 0 || link.Variable2.Length != 0 ||
                _source.ReadObject(link.Controller) is not FalloutNifAlphaController controller ||
                !geometry.Properties.Contains(controller.Time.Target) || (controller.Time.Flags & 0x40) == 0 ||
                !_materials.TryGetValue(controller.Time.Target, out var materials))
                throw new NotSupportedException($"Alpha channel {sequence.Name}/{link.NodeName} has no declared material.");
            var sampler = new FalloutNifFloatAnimation(_source, link.Interpolator);
            return new(time =>
            {
                var alpha = sampler.Sample(time);
                foreach (var material in materials)
                {
                    if (material is ShaderMaterial effect && effect.ResourceName == NativeNifEffectMaterial.ResourceIdentity)
                    {
                        var color = effect.GetShaderParameter(ParticleColorMultiplierParameter).AsVector4();
                        color.W = alpha;
                        effect.SetShaderParameter(ParticleColorMultiplierParameter, color);
                    }
                    else throw new NotSupportedException("Animated material alpha has no shader owner.");
                }
            });
        }
    }
}
