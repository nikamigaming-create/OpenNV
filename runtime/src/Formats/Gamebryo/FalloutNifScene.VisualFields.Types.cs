namespace OpenNV.Runtime.Formats.Gamebryo;

internal sealed record FalloutNifSceneVisibilitySnapshot(FalloutNifSceneKey Target,
    bool InitialVisible, long Stores, long Changed);
internal sealed record FalloutNifSceneMorphWeightSnapshot(int Index, string Name,
    uint InitialBits, uint ValueBits, long Stores, long Changed);
internal sealed record FalloutNifSceneMorphSnapshot(FalloutNifSceneKey Target,
    FalloutNifSceneKey Controller, FalloutNifSceneKey Data,
    IReadOnlyList<FalloutNifSceneMorphWeightSnapshot> Weights);

// One scalar object per ordered source target. Repeated names are legal data;
// a name-based channel still needs one unambiguous selected index.
internal sealed class FalloutNifSceneMorphWeightField(int index, string name, float initial)
{
    internal int Index { get; } = index;
    internal string Name { get; } = name;
    internal uint InitialBits { get; } = BitConverter.SingleToUInt32Bits(initial);
    internal uint ValueBits { get; private set; } = BitConverter.SingleToUInt32Bits(initial);
    internal float Value => BitConverter.UInt32BitsToSingle(ValueBits);
    internal long Stores { get; private set; }
    internal long Changed { get; private set; }
    internal void Store(float value, long changed)
    {
        var stores = checked(Stores + 1);
        ValueBits = BitConverter.SingleToUInt32Bits(value); Stores = stores; Changed = changed;
    }
    internal FalloutNifSceneMorphWeightSnapshot Capture() => new(Index, Name, InitialBits, ValueBits, Stores, Changed);
    internal void Restore(FalloutNifSceneMorphWeightSnapshot saved)
    {
        if (saved.Index != Index || saved.Name != Name || saved.InitialBits != InitialBits)
            throw new InvalidDataException("Cold morph weight changed its source index, name or initial value.");
        ValueBits = saved.ValueBits; Stores = saved.Stores; Changed = saved.Changed;
    }
}

internal sealed class FalloutNifSceneMorphField
{
    internal FalloutNifSceneMorphField(FalloutNifSceneKey target, FalloutNifFile source,
        FalloutNifMorphGeometry declaration)
    {
        Target = target; Declaration = declaration;
        Controller = new(target.Asset, declaration.Controller.Block.Index);
        Data = new(target.Asset, declaration.Data.Block.Index);
        var weights = new FalloutNifSceneMorphWeightField[declaration.Data.Morphs.Length];
        for (var index = 0; index < weights.Length; ++index)
        {
            var input = declaration.Controller.Weights[index];
            var initial = input.Interpolator < 0 ? input.Weight :
                new FalloutNifFloatAnimation(source, input.Interpolator).Sample(declaration.Controller.Time.StartTime);
            weights[index] = new(index, declaration.Data.Morphs[index].Name, declaration.EffectiveWeight(index, initial));
        }
        Weights = Array.AsReadOnly(weights);
    }
    internal FalloutNifSceneKey Target { get; }
    internal FalloutNifSceneKey Controller { get; }
    internal FalloutNifSceneKey Data { get; }
    internal FalloutNifMorphGeometry Declaration { get; }
    internal IReadOnlyList<FalloutNifSceneMorphWeightField> Weights { get; }
    internal int RequireIndex(string name)
    {
        var selected = -1;
        for (var index = 0; index < Weights.Count; ++index)
            if (Weights[index].Name == name)
            {
                if (selected >= 0) throw new InvalidDataException("Morph channel names more than one declared source slot.");
                selected = index;
            }
        return selected >= 0 ? selected : throw new InvalidDataException("Morph channel has no declared source target name.");
    }
    internal FalloutNifSceneMorphSnapshot Capture() => new(Target, Controller, Data,
        Array.AsReadOnly(Weights.Select(weight => weight.Capture()).ToArray()));
    internal void Restore(FalloutNifSceneMorphSnapshot saved)
    {
        if (saved.Target != Target || saved.Controller != Controller || saved.Data != Data || saved.Weights.Count != Weights.Count)
            throw new InvalidDataException("Cold morph field changed its actual controller, data or ordered target extent.");
        for (var index = 0; index < Weights.Count; ++index) Weights[index].Restore(saved.Weights[index]);
    }
}
