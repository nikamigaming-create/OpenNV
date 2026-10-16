using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.Content;

internal sealed partial class FalloutActorLogicalScene
{
    // A source extra-data block is one mutable object even when several
    // scene nodes retain it. Names select a declared reference, not a new cell.
    private sealed class FloatExtraDataField(FalloutNifSceneKey key, string name, uint initial)
    {
        internal readonly FalloutNifSceneKey Key = key;
        internal readonly string Name = name;
        internal readonly uint InitialBits = initial;
        internal uint ValueBits = initial;
        internal long Stores, Changed;
        internal FalloutNifSceneFloatExtraDataSnapshot Capture() => new(Key, Name, InitialBits, ValueBits, Stores, Changed);
    }
    private readonly Dictionary<FalloutNifSceneKey, FloatExtraDataField> _floatExtraData = [];
    private readonly Dictionary<(FalloutNifSceneKey Target, string Name), FalloutNifSceneKey> _floatExtraTargets = [];

    private void AddFloatExtraData(FalloutNifSceneObject target, FalloutNifFile source)
    {
        var references = target.Source switch
        {
            FalloutNifNode node => node.ExtraData,
            FalloutNifGeometry geometry => geometry.ExtraData,
            FalloutNifAmbientLight light => light.ExtraData,
            FalloutNifPointLight light => light.Light.ExtraData,
            _ => [],
        };
        foreach (var reference in references.Where(reference => reference >= 0))
        {
            // Unrelated extra-data classes retain their independent readers.
            if (source.Blocks[reference].TypeName != "NiFloatExtraData") continue;
            var declared = (FalloutNifFloatExtraData)source.ReadObject(reference);
            var key = new FalloutNifSceneKey(target.Key.Asset, reference);
            var bits = BitConverter.SingleToUInt32Bits(declared.Value);
            if (string.IsNullOrWhiteSpace(declared.Name) || !float.IsFinite(declared.Value) ||
                !_floatExtraTargets.TryAdd((target.Key, declared.Name), key))
                throw new InvalidDataException("Logical float extra-data has an unnamed, nonfinite or ambiguous source target.");
            if (!_floatExtraData.ContainsKey(key)) _floatExtraData.Add(key, new(key, declared.Name, bits));
            target.FloatExtraData.Add(key);
        }
    }
    internal FalloutNifSceneKey RequireFloatExtraDataKey(FalloutNifSceneKey target, string name)
    {
        RequireIdle();
        if (!_objects.ContainsKey(target) || !_floatExtraTargets.TryGetValue((target, name), out var field))
            throw new InvalidDataException("Float animation has no actual declared node/extra-data field.");
        return field;
    }
    internal float ReadFloatExtraData(FalloutNifSceneKey target, string name) =>
        BitConverter.UInt32BitsToSingle(_floatExtraData[RequireFloatExtraDataKey(target, name)].ValueBits);

    internal Action<float> BindFloatExtraDataAnimation(FalloutNifSceneKey target, FalloutNifFile animation,
        FalloutNifControllerLink link)
    {
        RequireIdle(); ArgumentNullException.ThrowIfNull(animation); ArgumentNullException.ThrowIfNull(link);
        var node = _objects.GetValueOrDefault(target) ?? throw new InvalidDataException("Float animation selected another scene target.");
        if (link.ControllerType != "NiFloatExtraDataController" || link.PropertyType.Length != 0 ||
            link.Variable1.Length == 0 || link.Variable2.Length != 0 || link.NodeName != node.Name)
            throw new InvalidDataException("Float animation changed its source class, node, property or extra-data name.");
        var field = RequireFloatExtraDataKey(target, link.Variable1);
        RequireFloatExtraDataController(node, link.Variable1);
        var sampler = new FalloutNifFloatAnimation(animation, link.Interpolator);
        return time =>
        {
            RequireIdle(); var value = sampler.Sample(time);
            var current = _floatExtraData[field];
            var stores = checked(current.Stores + 1); var changed = Next();
            current.ValueBits = BitConverter.SingleToUInt32Bits(value); current.Stores = stores; current.Changed = changed;
        };
    }
    private void RequireFloatExtraDataController(FalloutNifSceneObject target, string name)
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
        var seen = new HashSet<int>(); var matching = 0;
        while (cursor >= 0)
        {
            if (!seen.Add(cursor)) throw new InvalidDataException("Float source controller chain repeats a declaration.");
            var controller = source.ReadObject(cursor); var time = FalloutNifNodeControllerChain.Time(controller);
            if (time.Target != target.Key.Block || time.UnknownInteger != 0)
                throw new InvalidDataException("Float source controller belongs to a foreign scene node.");
            if (controller is FalloutNifFloatExtraDataController value && value.ExtraDataName == name) ++matching;
            cursor = time.NextController;
        }
        if (matching != 1) throw new InvalidDataException("Float animation has no unique declared source controller for its extra-data field.");
    }

    internal Action<float> BindSkeletonFloatExtraDataAnimation(FalloutNifFile animation, FalloutNifControllerLink link) =>
        BindFloatExtraDataAnimation(RequireBone(link.NodeName).Key, animation, link);
    internal FalloutNifSceneKey RequireSkeletonFloatExtraDataKey(string node, string name) =>
        RequireFloatExtraDataKey(RequireBone(node).Key, name);
    internal float ReadSkeletonFloatExtraData(string node, string name) => ReadFloatExtraData(RequireBone(node).Key, name);
    internal void BorrowRestoredSkeletonFloatExtraData(string node, string name, float value)
    {
        // The logical graph already restored the authoritative field. A
        // presentation/head adapter may borrow it, never replay a saved store.
        if (!float.IsFinite(value) || BitConverter.SingleToUInt32Bits(ReadSkeletonFloatExtraData(node, name)) != BitConverter.SingleToUInt32Bits(value))
            throw new InvalidDataException("Native float restore differs from the already restored logical source field.");
    }
    internal IReadOnlyList<(string Node, string Name, float Value)> ReadSkeletonFloatExtraDataValues()
    {
        RequireIdle(); var values = new List<(string Node, string Name, float Value)>();
        foreach (var (reference, field) in _floatExtraTargets)
            if (reference.Target.Asset == 0 && _objects[reference.Target] is { IsNode: true } target)
            {
                var value = _floatExtraData[field];
                values.Add((target.Name, value.Name, BitConverter.UInt32BitsToSingle(value.ValueBits)));
            }
        return values.AsReadOnly();
    }
    private IReadOnlyList<FalloutNifSceneFloatExtraDataSnapshot> CaptureFloatExtraData() =>
        Array.AsReadOnly(_floatExtraData.Values.Select(field => field.Capture()).ToArray());
    private void RestoreFloatExtraData(FalloutActorLogicalSceneSnapshot saved)
    {
        if (saved.FloatExtraData.Count != _floatExtraData.Count)
            throw new InvalidDataException("Cold logical scene omitted a declared source float extra-data object.");
        foreach (var field in saved.FloatExtraData)
        {
            if (!_floatExtraData.TryGetValue(field.Key, out var actual) || actual.Name != field.Name || actual.InitialBits != field.InitialBits)
                throw new InvalidDataException("Cold logical float field changed its selected source block, name or constructor value.");
            actual.ValueBits = field.ValueBits; actual.Stores = field.Stores; actual.Changed = field.Changed;
        }
    }
    private static void ValidateFloatExtraData(FalloutActorLogicalSceneSnapshot saved)
    {
        if (saved.FloatExtraData is null || saved.Objects.Any(node => node.FloatExtraData is null))
            throw new InvalidDataException("Cold logical scene omitted its source float references or current fields.");
        var fields = new Dictionary<FalloutNifSceneKey, FalloutNifSceneFloatExtraDataSnapshot>(); long stores = 0;
        foreach (var field in saved.FloatExtraData)
        {
            if (field is null || field.Key.Asset < 0 || field.Key.Asset >= saved.Assets.Count || field.Key.Block < 0 ||
                string.IsNullOrWhiteSpace(field.Name) || !float.IsFinite(BitConverter.UInt32BitsToSingle(field.InitialBits)) ||
                !float.IsFinite(BitConverter.UInt32BitsToSingle(field.ValueBits)) || field.Stores < 0 || field.Changed < 0 ||
                field.Stores == 0 && (field.Changed != 0 || field.ValueBits != field.InitialBits) ||
                field.Stores > 0 && (field.Changed < field.Stores || field.Changed > saved.Sequence) ||
                field.Stores > saved.Sequence - stores || !fields.TryAdd(field.Key, field))
                throw new InvalidDataException("Cold logical float fields lost their finite source values or actual store history.");
            stores += field.Stores;
        }
        var referenced = new HashSet<FalloutNifSceneKey>();
        foreach (var node in saved.Objects)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var key in node.FloatExtraData)
            {
                if (key.Asset != node.Key.Asset || !fields.TryGetValue(key, out var field) || !names.Add(field.Name))
                    throw new InvalidDataException("Cold logical node changed a declared float reference or made its name ambiguous.");
                referenced.Add(key);
            }
        }
        if (referenced.Count != fields.Count)
            throw new InvalidDataException("Cold logical scene retained an unowned float extra-data object.");
    }
}
