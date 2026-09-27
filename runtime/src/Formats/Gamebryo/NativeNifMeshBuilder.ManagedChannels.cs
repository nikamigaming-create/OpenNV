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

        private bool IsManagedRefractionController(FalloutNifShaderProperty property) =>
            _source.ReadObject(property.Controller) is FalloutNifRefractionController controller &&
            controller.Time.Target == property.Block.Index && controller.Time.NextController == -1 &&
            (controller.Time.Flags & 0x60) == 0x60 &&
            _source.ReadObject(controller.Interpolator) is FalloutNifBlendFloatInterpolator;

        private RuntimeNifControllerChannel BuildRefractionChannel(FalloutNifControllerSequence sequence,
            FalloutNifControllerLink link, int target)
        {
            if (link.PropertyType != "BSShaderPPLightingProperty" || link.Variable1.Length != 0 || link.Variable2.Length != 0 ||
                _source.ReadObject(link.Controller) is not FalloutNifRefractionController controller ||
                !ManagedGeometry(target).Properties.Contains(controller.Time.Target) ||
                _source.ReadObject(controller.Time.Target) is not FalloutNifShaderProperty property ||
                property.Controller != controller.Block.Index || !IsManagedRefractionController(property) ||
                !_materials.TryGetValue(property.Block.Index, out var materials))
                throw new NotSupportedException($"Managed refraction {sequence.Name}/{link.NodeName} has no source shader.");
            var sampler = new FalloutNifFloatAnimation(_source, link.Interpolator);
            return new(time =>
            {
                var strength = sampler.Sample(time);
                if (!float.IsFinite(strength)) throw new InvalidDataException("Refraction strength is nonfinite.");
                foreach (var material in materials)
                    NativeNifRefractionMaterial.Apply(material, property, strength, time);
            });
        }
    }
}
