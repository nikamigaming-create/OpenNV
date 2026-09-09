using Godot;

namespace OpenNV.Runtime.Formats.Gamebryo;

internal sealed class RuntimeNativeNifMaterialChannels
{
    private sealed record Target(FalloutNifFile Source, FalloutNifObject Property, IReadOnlyList<Material> Materials);
    private readonly Dictionary<(string Node, string Property), List<Target>> _targets = [];

    internal void Add(string node, FalloutNifFile source, FalloutNifObject property, IReadOnlyList<Material> materials)
    {
        var key = (node, property.Block.TypeName);
        if (!_targets.TryGetValue(key, out var entries)) _targets.Add(key, entries = []);
        if (!entries.Any(entry => ReferenceEquals(entry.Source, source) && entry.Property.Block.Index == property.Block.Index))
            entries.Add(new(source, property, materials));
    }

    internal Action<float>? Bind(FalloutNifFile source, FalloutNifControllerLink link)
    {
        if (!_targets.TryGetValue((link.NodeName, link.PropertyType), out var targets)) return null;
        if (targets.Count != 1 || link.Variable2.Length != 0)
            throw new NotSupportedException("External KF material target is ambiguous or has an unsupported controller ID.");
        var target = targets[0];
        var controllers = Controllers(target).ToArray();
        if (link.ControllerType == "NiTextureTransformController" && target.Property is FalloutNifTexturingProperty)
        {
            var operation = link.Variable1 switch
            {
                "0-0-TT_TRANSLATE_U" => 0u,
                "0-0-TT_TRANSLATE_V" => 1u,
                "0-0-TT_ROTATE" => 2u,
                "0-0-TT_SCALE_U" => 3u,
                "0-0-TT_SCALE_V" => 4u,
                _ => throw new NotSupportedException("External KF texture operation is unsupported."),
            };
            if (controllers.OfType<FalloutNifTextureTransformController>().Count(controller =>
                !controller.ShaderMap && controller.TextureSlot == 0 && controller.Operation == operation) != 1)
                throw new InvalidDataException("External KF texture operation has no unique source controller.");
            var sampler = new FalloutNifFloatAnimation(source, link.Interpolator);
            var materials = EffectMaterials(target);
            return time => { var value = sampler.Sample(time); foreach (var material in materials) NativeNifTextureTransform.Apply(material, operation, value); };
        }
        if (link.ControllerType == "NiMaterialColorController" && link.Variable1 == "SELF_ILLUM" &&
            target.Property is FalloutNifMaterialProperty)
        {
            if (controllers.OfType<FalloutNifMaterialColorController>().Count(controller => controller.TargetColor == 3) != 1)
                throw new InvalidDataException("External KF emissive channel has no source color controller/interpolator.");
            var sampler = new FalloutNifPoint3Animation(source, link.Interpolator);
            var materials = target.Materials.Select(material => material is ShaderMaterial shader && material.ResourceName is
                NativeNifEffectMaterial.ResourceIdentity or NativeNifLightingMaterial.ResourceIdentity ? shader :
                throw new NotSupportedException("External KF emissive material is unsupported.")).ToArray();
            return time =>
            {
                var value = sampler.Sample(time);
                foreach (var material in materials)
                    if (material.ResourceName == NativeNifEffectMaterial.ResourceIdentity)
                        NativeNifEffectMaterial.ApplyEmissiveColor(material, new(value.X, value.Y, value.Z));
                    else material.SetShaderParameter("emissive_color", new Vector3(value.X, value.Y, value.Z));
            };
        }
        if (link.ControllerType == "NiAlphaController" && link.Variable1.Length == 0 && target.Property is FalloutNifMaterialProperty)
        {
            if (controllers.OfType<FalloutNifAlphaController>().Count() != 1)
                throw new InvalidDataException("External KF alpha channel has no unique source controller.");
            var sampler = new FalloutNifFloatAnimation(source, link.Interpolator);
            return time =>
            {
                var alpha = sampler.Sample(time);
                if (!float.IsFinite(alpha)) throw new InvalidDataException("External KF alpha is nonfinite.");
                foreach (var material in target.Materials)
                {
                    if (material is not ShaderMaterial shader) throw new NotSupportedException("External KF alpha shader is unsupported.");
                    var parameter = material.ResourceName == NativeNifEffectMaterial.ResourceIdentity ? "source_color_multiplier" :
                        material.ResourceName == NativeNifLightingMaterial.ResourceIdentity ? "base_factor" :
                        throw new NotSupportedException("External KF alpha material is unsupported.");
                    var color = shader.GetShaderParameter(parameter).AsVector4(); color.W = alpha;
                    shader.SetShaderParameter(parameter, color);
                }
            };
        }
        return null;
    }

    private static ShaderMaterial[] EffectMaterials(Target target) => target.Materials.Select(material =>
        material is ShaderMaterial shader && material.ResourceName == NativeNifEffectMaterial.ResourceIdentity
            ? shader : throw new NotSupportedException("External KF property requires its source no-lighting shader.")).ToArray();

    private static IEnumerable<FalloutNifObject> Controllers(Target target)
    {
        var cursor = target.Property switch
        {
            FalloutNifMaterialProperty material => material.Controller,
            FalloutNifTexturingProperty texture => texture.Controller,
            _ => -1,
        };
        var visited = new HashSet<int>();
        while (cursor >= 0)
        {
            if (!visited.Add(cursor)) throw new InvalidDataException("Actor material controller chain has a cycle.");
            var controller = target.Source.ReadObject(cursor);
            var time = controller switch
            {
                FalloutNifAlphaController alpha => alpha.Time,
                FalloutNifMaterialColorController color => color.Time,
                FalloutNifTextureTransformController texture => texture.Time,
                _ => throw new NotSupportedException("Actor material controller has no external KF binding."),
            };
            if (time.Target != target.Property.Block.Index || (time.Flags & 0x40) == 0)
                throw new NotSupportedException("Actor property controller has an invalid target or disables scaled time.");
            yield return controller; cursor = time.NextController;
        }
    }
}

internal static partial class RuntimeNativeNifMeshBuilder
{
    private sealed partial class BuildState
    {
        internal void RegisterActorMaterialChannels(RuntimeNativeNifSkeleton skeleton)
        {
            foreach (var block in _source.Blocks.Where(block => block.TypeName is "NiTriShape" or "NiTriStrips" or "BSSegmentedTriShape"))
            {
                var geometry = _source.ReadGeometry(block.Index);
                foreach (var index in geometry.Properties)
                    if (_materials.TryGetValue(index, out var materials))
                        skeleton.MaterialChannels.Add(geometry.Name, _source, _source.ReadObject(index), materials);
            }
        }
    }
}
