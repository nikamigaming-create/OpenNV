using Godot;

namespace OpenNV.Runtime.Formats.Gamebryo;

internal static partial class RuntimeNativeNifMeshBuilder
{
    private sealed partial class BuildState
    {
        private RuntimeNifControllerChannel BuildManagedVisibilityChannel(FalloutNifControllerSequence sequence,
            FalloutNifControllerLink link, int target, Node3D node)
        {
            if (link.PropertyType.Length != 0 || link.Variable1.Length != 0 || link.Variable2.Length != 0 ||
                _source.ReadObject(link.Controller) is not FalloutNifVisibilityController controller ||
                controller.Time.Target != target || (controller.Time.Flags & 0x60) != 0x60 ||
                _source.ReadObject(controller.Interpolator) is not FalloutNifBlendBoolInterpolator)
                throw new NotSupportedException($"Managed visibility {sequence.Name}/{link.NodeName} has no declared target.");
            var sampler = new FalloutNifBoolAnimation(_source, link.Interpolator);
            return new(time => node.Visible = sampler.Sample(time), sampler.BoundaryTimes);
        }

        private FalloutNifGeometry ManagedGeometry(int target) => _source.ReadObject(target) switch
        {
            FalloutNifGeometry geometry => geometry,
            FalloutNifParticleSystem particle => particle.Geometry,
            _ => throw new NotSupportedException("Managed property target has no source geometry."),
        };

        private RuntimeNifControllerChannel BuildManagedMaterialChannel(FalloutNifControllerSequence sequence,
            FalloutNifControllerLink link, int target)
        {
            var controller = _source.ReadObject(link.Controller);
            var clock = controller switch
            {
                FalloutNifMaterialColorController value => value.Time,
                FalloutNifAlphaController value => value.Time,
                FalloutNifEmittanceController value => value.Time,
                FalloutNifTextureTransformController value => value.Time,
                _ => throw new NotSupportedException("Managed material controller is unsupported."),
            };
            if (controller.Block.TypeName != link.ControllerType || (clock.Flags & 0x60) != 0x60 ||
                !ManagedGeometry(target).Properties.Contains(clock.Target) ||
                _source.Blocks[clock.Target].TypeName != link.PropertyType ||
                !_materials.TryGetValue(clock.Target, out var materials))
                throw new NotSupportedException($"Managed material {sequence.Name}/{link.NodeName} has no declared property.");
            var channels = new RuntimeNativeNifMaterialChannels();
            channels.Add(link.NodeName, _source, _source.ReadObject(clock.Target), materials);
            return new(channels.Bind(_source, link) ??
                throw new NotSupportedException($"Managed material {sequence.Name}/{link.NodeName}/{link.ControllerType} is unsupported."));
        }

        private IReadOnlyList<FalloutNifObject>? ManagedRefractionControllers(FalloutNifShaderProperty property)
        {
            var result = new List<FalloutNifObject>();
            var visited = new HashSet<int>();
            var cursor = property.Controller;
            while (cursor >= 0)
            {
                if (!visited.Add(cursor)) throw new InvalidDataException("Shader controller chain has a cycle.");
                var controller = _source.ReadObject(cursor);
                var interpolator = controller switch
                {
                    FalloutNifRefractionController strength => strength.Interpolator,
                    FalloutNifRefractionFirePeriodController period => period.Interpolator,
                    _ => -1,
                };
                if (interpolator < 0) return null;
                var clock = FalloutNifNodeControllerChain.Time(controller);
                if (clock.Target != property.Block.Index || clock.UnknownInteger != 0 ||
                    (clock.Flags & 0x60) != 0x60 || _source.ReadObject(interpolator) is not FalloutNifBlendFloatInterpolator)
                    return null;
                result.Add(controller);
                cursor = clock.NextController;
            }
            return result.Count == 0 ? null : result;
        }

        private bool IsManagedRefractionController(FalloutNifShaderProperty property) => ManagedRefractionControllers(property) is not null;

        private RuntimeNifControllerChannel BuildRefractionChannel(FalloutNifControllerSequence sequence,
            FalloutNifControllerLink link, int target)
        {
            var controller = _source.ReadObject(link.Controller);
            var clock = FalloutNifNodeControllerChain.Time(controller);
            if (link.PropertyType != "BSShaderPPLightingProperty" || link.Variable1.Length != 0 || link.Variable2.Length != 0 ||
                controller.Block.TypeName != link.ControllerType ||
                !ManagedGeometry(target).Properties.Contains(clock.Target) ||
                _source.ReadObject(clock.Target) is not FalloutNifShaderProperty property ||
                ManagedRefractionControllers(property) is not { } chain || !chain.Contains(controller) ||
                !_materials.TryGetValue(property.Block.Index, out var materials))
                throw new NotSupportedException($"Managed refraction {sequence.Name}/{link.NodeName} has no source shader.");
            var sampler = new FalloutNifFloatAnimation(_source, link.Interpolator);
            return new(time =>
            {
                var value = sampler.Sample(time);
                foreach (var material in materials)
                    if (controller is FalloutNifRefractionFirePeriodController)
                        NativeNifRefractionMaterial.ApplyPeriod(material, property, value, time);
                    else NativeNifRefractionMaterial.Apply(material, property, value, time);
            });
        }
    }
}
