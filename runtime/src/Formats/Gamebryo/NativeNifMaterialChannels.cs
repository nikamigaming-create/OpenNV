using Godot;

namespace OpenNV.Runtime.Formats.Gamebryo;

internal sealed class RuntimeNativeNifMaterialChannels
{
    private static readonly StringName EmissiveMultipleParameter = "emissive_multiple";
    private static readonly StringName SourceSpecularParameter = "source_specular";
    private static readonly StringName EmissiveColorParameter = "emissive_color";
    private static readonly StringName SourceColorMultiplierParameter = "source_color_multiplier";
    private static readonly StringName BaseFactorParameter = "base_factor";
    private sealed record Target(FalloutNifFile Source, FalloutNifObject Property, IReadOnlyList<Material> Materials, Node3D? Owner);
    private readonly Dictionary<(string Node, string Property), List<Target>> _targets = [];
    private long _revision;

    internal bool HasSourceTarget(string node) => _targets.Keys.Any(key => key.Node == node);

    internal void Add(string node, FalloutNifFile source, FalloutNifObject property, IReadOnlyList<Material> materials, Node3D? owner = null)
    {
        var key = (node, property.Block.TypeName);
        if (!_targets.TryGetValue(key, out var entries)) _targets.Add(key, entries = []);
        if (!entries.Any(entry => ReferenceEquals(entry.Source, source) && entry.Property.Block.Index == property.Block.Index))
        {
            entries.Add(new(source, property, materials, owner));
            _revision++;
        }
    }

    internal RuntimeNativeNifMaterialChannels PrepareReplacement(IReadOnlySet<Node3D> owners, RuntimeNativeNifMaterialChannels replacement)
    {
        var result = new RuntimeNativeNifMaterialChannels();
        foreach (var (key, targets) in _targets)
            foreach (var target in targets.Where(target => target.Owner is null || !owners.Contains(target.Owner)))
                result.Add(key.Node, target.Source, target.Property, target.Materials, target.Owner);
        foreach (var (key, targets) in replacement._targets)
            foreach (var target in targets) result.Add(key.Node, target.Source, target.Property, target.Materials, target.Owner);
        return result;
    }

    internal void ReplaceWith(RuntimeNativeNifMaterialChannels other)
    {
        if (ReferenceEquals(this, other)) throw new InvalidOperationException("Material channel replacement needs a separate prepared owner.");
        _targets.Clear();
        foreach (var (key, value) in other._targets) _targets.Add(key, value.ToList());
        _revision++;
    }

    internal Action<float>? Bind(FalloutNifFile source, FalloutNifControllerLink link)
    {
        var absent = HasAbsentSourceController(link);
        var apply = absent ? null : BindCurrent(source, link);
        if (apply is null && !absent) return null;
        if (absent && link.Interpolator < -1)
            throw new InvalidDataException("Unused external KF slot has an invalid interpolator link.");
        if (absent && link.Interpolator >= 0 &&
            !source.ReadObject(link.Interpolator).Block.TypeName.EndsWith("Interpolator", StringComparison.Ordinal))
            throw new InvalidDataException("Unused external KF slot does not declare an interpolator.");
        var everBound = apply is not null;
        var revision = _revision;
        return time =>
        {
            if (revision != _revision)
            {
                absent = HasAbsentSourceController(link);
                if (absent && everBound)
                    throw new NotSupportedException("Active external KF controller retarget requires its retained-controller owner after source replacement.");
                apply = absent ? null : BindCurrent(source, link);
                everBound |= apply is not null;
                if (apply is null && !absent)
                    throw new NotSupportedException("An active material channel lost its source target after body replacement.");
                revision = _revision;
            }
            apply?.Invoke(time);
        };
    }

    // A named source property does not imply that a shared KF's controller
    // exists on it. The native selector leaves that blend slot null when both
    // the exact property controller and the KF's retained controller are absent.
    // Transform channels have a separate controller producer and are excluded.
    internal bool HasAbsentSourceController(FalloutNifControllerLink link)
    {
        if (link.PropertyType.Length == 0 || link.ControllerType is "NiTransformController" or "BSTreadTransfController" || link.Controller != -1 ||
            !_targets.TryGetValue((link.NodeName, link.PropertyType), out var targets) || targets.Count != 1)
            return false;
        var target = targets[0];
        var cursor = target.Property switch
        {
            FalloutNifMaterialProperty value => value.Controller,
            FalloutNifTexturingProperty value => value.Controller,
            FalloutNifShaderProperty value => value.Controller,
            FalloutNifNoLightingProperty value => value.Controller,
            FalloutNifTileShaderProperty value => value.Controller,
            _ => throw new NotSupportedException("External KF property has no complete source controller-chain owner.")
        };
        var visited = new HashSet<int>();
        while (cursor >= 0)
        {
            if (!visited.Add(cursor)) throw new InvalidDataException("External KF source property controller chain has a cycle.");
            var controller = target.Source.ReadObject(cursor);
            var time = FalloutNifNodeControllerChain.Time(controller);
            if (time.Target != target.Property.Block.Index || time.UnknownInteger != 0)
                throw new InvalidDataException("External KF source property controller has a foreign or unknown target.");
            if (MatchesSourceController(controller, link)) return false;
            cursor = time.NextController;
        }
        if (cursor != -1) throw new InvalidDataException("External KF source property controller link is invalid.");
        return true;
    }

    private static bool MatchesSourceController(FalloutNifObject controller, FalloutNifControllerLink link)
    {
        if (controller.Block.TypeName != link.ControllerType) return false;
        // Shared weapon clips address individual channels on the same property.
        // A UV-V or emissive controller cannot supply the UV-U or specular slot.
        // Keep unknown IDs on the strict binding path instead of declaring them absent.
        return (controller, link.Variable1) switch
        {
            (FalloutNifTextureTransformController texture, "0-0-TT_TRANSLATE_U") => Texture(0),
            (FalloutNifTextureTransformController texture, "0-0-TT_TRANSLATE_V") => Texture(1),
            (FalloutNifTextureTransformController texture, "0-0-TT_ROTATE") => Texture(2),
            (FalloutNifTextureTransformController texture, "0-0-TT_SCALE_U") => Texture(3),
            (FalloutNifTextureTransformController texture, "0-0-TT_SCALE_V") => Texture(4),
            (FalloutNifMaterialColorController color, "SPEC") => color.TargetColor == 2,
            (FalloutNifMaterialColorController color, "SELF_ILLUM") => color.TargetColor == 3,
            _ => true
        };

        bool Texture(uint operation) => controller is FalloutNifTextureTransformController value &&
            !value.ShaderMap && value.TextureSlot == 0 && value.Operation == operation;
    }

    private Action<float>? BindCurrent(FalloutNifFile source, FalloutNifControllerLink link)
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
        if (link.ControllerType == "BSMaterialEmittanceMultController" && link.Variable1.Length == 0 &&
            target.Property is FalloutNifMaterialProperty)
        {
            if (controllers.OfType<FalloutNifEmittanceController>().Count() != 1)
                throw new InvalidDataException("External KF emittance has no unique source controller.");
            var sampler = new FalloutNifFloatAnimation(source, link.Interpolator);
            return time =>
            {
                var multiple = sampler.Sample(time);
                if (!float.IsFinite(multiple)) throw new InvalidDataException("External KF emittance is nonfinite.");
                foreach (var material in target.Materials)
                {
                    if (material is ShaderMaterial lighting && lighting.ResourceName == NativeNifLightingMaterial.ResourceIdentity)
                        lighting.SetShaderParameter(EmissiveMultipleParameter, multiple);
                    else if (material is ShaderMaterial effect && effect.ResourceName == NativeNifEffectMaterial.ResourceIdentity)
                        NativeNifEffectMaterial.ApplyEmissiveMultiple(effect, multiple);
                    else throw new NotSupportedException("External KF emittance material is unsupported.");
                }
            };
        }
        if (link.ControllerType == "NiMaterialColorController" && link.Variable1 == "SPEC" &&
            target.Property is FalloutNifMaterialProperty property)
        {
            if (controllers.OfType<FalloutNifMaterialColorController>().Count(controller => controller.TargetColor == 2) != 1)
                throw new InvalidDataException("External KF specular channel has no unique source controller.");
            var sampler = new FalloutNifPoint3Animation(source, link.Interpolator);
            return time =>
            {
                var value = sampler.Sample(time);
                if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z))
                    throw new InvalidDataException("External KF specular color is nonfinite.");
                foreach (var material in target.Materials)
                    if (material is ShaderMaterial shader && material.ResourceName == NativeNifLightingMaterial.ResourceIdentity)
                        shader.SetShaderParameter(SourceSpecularParameter, new Vector3(value.X, value.Y, value.Z));
                    else if (value.X != property.Specular.R || value.Y != property.Specular.G || value.Z != property.Specular.B)
                        throw new NotSupportedException("External KF specular has no animated material owner.");
            };
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
                    else material.SetShaderParameter(EmissiveColorParameter, new Vector3(value.X, value.Y, value.Z));
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
                    var parameter = material.ResourceName == NativeNifEffectMaterial.ResourceIdentity ? SourceColorMultiplierParameter :
                        material.ResourceName == NativeNifLightingMaterial.ResourceIdentity ? BaseFactorParameter :
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
                FalloutNifEmittanceController emittance => emittance.Time,
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
        internal void RegisterActorMaterialChannels(RuntimeNativeNifMaterialChannels channels, Node3D owner)
        {
            foreach (var block in _source.Blocks.Where(block => block.TypeName is "NiTriShape" or "NiTriStrips" or "BSSegmentedTriShape"))
            {
                var geometry = _source.ReadGeometry(block.Index);
                foreach (var index in geometry.Properties)
                    if (_materials.TryGetValue(index, out var materials))
                        channels.Add(geometry.Name, _source, _source.ReadObject(index), materials, owner);
            }
        }
    }
}
