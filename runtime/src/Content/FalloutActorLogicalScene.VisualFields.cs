using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.Content;

internal sealed partial class FalloutActorLogicalScene
{
    internal const uint AppCulledFlag = 1;
    private sealed class VisibilityField(FalloutNifSceneObject target)
    {
        internal readonly FalloutNifSceneObject Target = target;
        internal readonly bool InitialVisible = (target.Flags & AppCulledFlag) == 0;
        internal long Stores, Changed;
        internal FalloutNifSceneVisibilitySnapshot Capture() => new(Target.Key, InitialVisible, Stores, Changed);
    }
    private readonly Dictionary<FalloutNifSceneKey, VisibilityField> _visibilityFields = [];
    private readonly Dictionary<FalloutNifSceneKey, FalloutNifSceneMorphField> _morphFields = [];
    internal object VisualFieldState => new
    {
        factory = Factory,
        lifetime = _lifetime,
        visibility = _visibilityFields.Values.Select(entry => new
        { target = entry.Target.Key, visible = (entry.Target.Flags & AppCulledFlag) == 0, entry.Stores, entry.Changed }).ToArray(),
        morphs = CaptureMorphs(),
        failure = _failure,
        retired = _retired
    };

    private void AddVisualFields(FalloutNifSceneObject target, FalloutNifFile source)
    {
        _visibilityFields.Add(target.Key, new(target));
        if (target.Source is not FalloutNifGeometry geometry || geometry.Controller < 0) return;
        if (!ReadVisualControllerChain(target).Any(controller => controller is FalloutNifMorphController)) return;
        var declaration = new FalloutNifMorphGeometry(source, geometry);
        if (declaration.Controller.Time.UnknownInteger != 0 || declaration.Controller.AlwaysUpdate > 1)
            throw new NotSupportedException("Logical morph controller has an unowned source target extension.");
        _morphFields.Add(target.Key, new(target.Key, source, declaration));
    }
    private IReadOnlyList<FalloutNifObject> ReadVisualControllerChain(FalloutNifSceneObject target)
    {
        var source = Asset(target.Key.Asset);
        var cursor = target.Source switch
        {
            FalloutNifNode node => node.Controller,
            FalloutNifGeometry geometry => geometry.Controller,
            FalloutNifAmbientLight light => light.Controller,
            FalloutNifPointLight light => light.Light.Controller,
            _ => -1,
        };
        var seen = new HashSet<int>(); var chain = new List<FalloutNifObject>();
        while (cursor >= 0)
        {
            if (!seen.Add(cursor)) throw new InvalidDataException("Visual source controller chain repeats a declaration.");
            var controller = source.ReadObject(cursor); var time = FalloutNifNodeControllerChain.Time(controller);
            if (time.Target != target.Key.Block || time.UnknownInteger != 0)
                throw new InvalidDataException("Visual source controller belongs to another scene object.");
            chain.Add(controller); cursor = time.NextController;
        }
        return chain;
    }
    private FalloutNifSceneObject RequireVisualTarget(FalloutNifSceneKey key, FalloutNifControllerLink link, string kind)
    {
        RequireIdle();
        var target = _objects.GetValueOrDefault(key) ?? throw new InvalidDataException("Visual animation selected a foreign scene object.");
        if (link.NodeName != target.Name || link.ControllerType != kind || link.PropertyType.Length != 0 || link.Variable1.Length != 0)
            throw new InvalidDataException("Visual animation changed its actual node, controller class or property selector.");
        return target;
    }
    internal Action<float> BindVisibilityAnimation(FalloutNifSceneKey key, FalloutNifFile animation, FalloutNifControllerLink link)
    {
        var target = RequireVisualTarget(key, link, "NiVisController");
        if (link.Variable2.Length != 0 || ReadVisualControllerChain(target).Count(controller => controller is FalloutNifVisibilityController) != 1)
            throw new InvalidDataException("Visibility animation has no unique declared source controller.");
        var sampler = new FalloutNifBoolAnimation(animation, link.Interpolator); var field = _visibilityFields[key];
        return time =>
        {
            RequireIdle(); var visible = sampler.Sample(time); var stores = checked(field.Stores + 1); var changed = Next();
            target.Flags = visible ? target.Flags & ~AppCulledFlag : target.Flags | AppCulledFlag;
            field.Stores = stores; field.Changed = changed;
        };
    }
    internal Action<float> BindMorphAnimation(FalloutNifSceneKey key, FalloutNifFile animation, FalloutNifControllerLink link)
    {
        _ = RequireVisualTarget(key, link, "NiGeomMorpherController");
        var field = RequireMorphField(key); var index = field.RequireIndex(link.Variable2);
        var sampler = new FalloutNifFloatAnimation(animation, link.Interpolator);
        return time =>
        {
            RequireIdle(); var value = field.Declaration.EffectiveWeight(index, sampler.Sample(time));
            _ = checked(field.Weights[index].Stores + 1);
            field.Weights[index].Store(value, Next());
        };
    }
    internal FalloutNifSceneMorphField RequireMorphField(FalloutNifSceneKey key)
    {
        RequireIdle();
        return _morphFields.GetValueOrDefault(key) ?? throw new InvalidDataException("Morph channel has no declared current source geometry field.");
    }
    internal bool ReadVisibility(FalloutNifSceneKey key)
    {
        RequireIdle(); return (_visibilityFields.TryGetValue(key, out var field) ? field.Target.Flags & AppCulledFlag :
            throw new InvalidDataException("Visibility reader selected another logical object.")) == 0;
    }
    internal float ReadMorphWeight(FalloutNifSceneKey key, string name)
    {
        var field = RequireMorphField(key); return field.Weights[field.RequireIndex(name)].Value;
    }
    internal IEnumerable<(string Node, string Name, float Weight)> ReadSkeletonMorphWeights()
    {
        RequireIdle();
        foreach (var field in _morphFields.Values)
            if (field.Target.Asset == 0)
                for (var index = 0; index < field.Weights.Count; ++index)
                {
                    var weight = field.Weights[index];
                    yield return (_objects[field.Target].Name, weight.Name, weight.Value);
                }
    }
    private IReadOnlyList<FalloutNifSceneVisibilitySnapshot> CaptureVisibility() =>
        Array.AsReadOnly(_visibilityFields.Values.Select(field => field.Capture()).ToArray());
    private IReadOnlyList<FalloutNifSceneMorphSnapshot> CaptureMorphs() =>
        Array.AsReadOnly(_morphFields.Values.Select(field => field.Capture()).ToArray());
    private void RestoreVisualFields(FalloutActorLogicalSceneSnapshot saved)
    {
        if (saved.Visibility.Count != _visibilityFields.Count || saved.Morphs.Count != _morphFields.Count)
            throw new InvalidDataException("Cold logical scene omitted its visibility or morph denominator.");
        foreach (var old in saved.Visibility)
        {
            if (!_visibilityFields.TryGetValue(old.Target, out var actual) || old.InitialVisible != actual.InitialVisible)
                throw new InvalidDataException("Cold visibility changed its selected source target or constructor flag.");
            actual.Stores = old.Stores; actual.Changed = old.Changed;
        }
        foreach (var old in saved.Morphs)
        {
            if (!_morphFields.TryGetValue(old.Target, out var actual))
                throw new InvalidDataException("Cold morph field selected an absent source geometry.");
            actual.Restore(old);
        }
    }
    private static void ValidateVisualFields(FalloutActorLogicalSceneSnapshot saved)
    {
        if (saved.Visibility is null || saved.Morphs is null || saved.Visibility.Count != saved.Objects.Count)
            throw new InvalidDataException("Cold logical scene omitted current visual fields.");
        var objects = saved.Objects.ToDictionary(node => node.Key); var visible = new HashSet<FalloutNifSceneKey>();
        var morphs = new HashSet<FalloutNifSceneKey>();
        var changes = saved.FloatExtraData.Where(field => field.Changed > 0).Select(field => field.Changed).ToHashSet();
        long stores = 0;
        foreach (var field in saved.Visibility)
        {
            if (field is null || !visible.Add(field.Target) || !objects.TryGetValue(field.Target, out var target) ||
                !History(field.Stores, field.Changed) || field.Stores == 0 && ((target.Flags & AppCulledFlag) == 0) != field.InitialVisible)
                throw new InvalidDataException("Cold visibility lost its source flag, target or store history.");
        }
        foreach (var field in saved.Morphs)
        {
            if (field is null || !morphs.Add(field.Target) || !objects.TryGetValue(field.Target, out var target) ||
                target.Type is not ("NiTriShape" or "NiTriStrips" or "BSSegmentedTriShape") ||
                field.Controller.Asset != field.Target.Asset || field.Data.Asset != field.Target.Asset ||
                field.Controller.Block < 0 || field.Data.Block < 0 || field.Weights is null || field.Weights.Count == 0)
                throw new InvalidDataException("Cold morph changed its source geometry/controller/data association.");
            for (var index = 0; index < field.Weights.Count; ++index)
            {
                var weight = field.Weights[index];
                if (weight is null || weight.Index != index || weight.Name is null ||
                    !float.IsFinite(BitConverter.UInt32BitsToSingle(weight.InitialBits)) ||
                    !float.IsFinite(BitConverter.UInt32BitsToSingle(weight.ValueBits)) ||
                    !History(weight.Stores, weight.Changed) || weight.Stores == 0 && weight.ValueBits != weight.InitialBits ||
                    index == 0 && (weight.InitialBits != 0x3f800000 || weight.ValueBits != 0x3f800000))
                    throw new InvalidDataException("Cold morph weight changed its ordered source index, value or store history.");
            }
        }
        bool History(long count, long changed)
        {
            if (count < 0 || changed < 0 || count == 0 && changed != 0 || changed > 0 && !changes.Add(changed) ||
                count > 0 && (changed < count || changed > saved.Sequence) || count > saved.Sequence - stores) return false;
            stores += count; return true;
        }
    }
}
