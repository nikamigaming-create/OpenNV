namespace OpenNV.Runtime.Formats.Gamebryo;

internal sealed record FalloutNifBoolInterpolator(FalloutNifBlock Block, byte Value, int Data) : FalloutNifObject(Block);
internal sealed record FalloutNifBoolKey(float Time, bool Value);
internal sealed record FalloutNifBoolData(FalloutNifBlock Block, uint Interpolation, FalloutNifBoolKey[] Keys) : FalloutNifObject(Block);
internal sealed record FalloutNifVisibilityController(FalloutNifBlock Block, FalloutNifTimeController Time, int Interpolator) : FalloutNifObject(Block);
internal sealed record FalloutNifMorphWeight(int Interpolator, float Weight);
internal sealed record FalloutNifMorphController(FalloutNifBlock Block, FalloutNifTimeController Time, ushort Flags,
    int Data, byte AlwaysUpdate, FalloutNifMorphWeight[] Weights) : FalloutNifObject(Block);
internal sealed record FalloutNifMorph(string Name, FalloutNifVector3[] Vectors);
internal sealed record FalloutNifMorphData(FalloutNifBlock Block, byte RelativeTargets, FalloutNifMorph[] Morphs) : FalloutNifObject(Block);

internal sealed class FalloutNifMorphGeometry
{
    internal FalloutNifMorphController Controller { get; }
    internal FalloutNifMorphData Data { get; }
    internal IReadOnlySet<int> ControllerBlocks { get; }

    internal FalloutNifMorphGeometry(FalloutNifFile source, FalloutNifGeometry geometry)
    {
        var seen = new HashSet<int>();
        FalloutNifMorphController? morph = null;
        for (var block = geometry.Controller; block >= 0;)
        {
            if (!seen.Add(block)) throw new InvalidDataException("Geometry controller chain contains a cycle.");
            FalloutNifTimeController time;
            switch (source.ReadObject(block))
            {
                case FalloutNifTransformController transform when transform.Interpolator == -1:
                    time = transform.Time;
                    break;
                case FalloutNifMorphController value when morph is null:
                    morph = value; time = value.Time;
                    break;
                default:
                    throw new NotSupportedException("Morph geometry has another unbound controller in its chain.");
            }
            if (time.Target != geometry.Block.Index) throw new InvalidDataException("Geometry controller targets another source object.");
            block = time.NextController;
        }
        Controller = morph ?? throw new InvalidDataException("Geometry has no declared morph controller.");
        Data = source.ReadObject(Controller.Data) as FalloutNifMorphData ?? throw new InvalidDataException("Morph controller has no morph data.");
        ControllerBlocks = seen;
        if (Data.RelativeTargets != 1 || Controller.Flags != 0)
            throw new NotSupportedException("Absolute or normal-recomputing geometry morphs require another presentation owner.");
        var vertices = source.ReadMeshData(geometry.Data).Vertices.Length;
        if (Data.Morphs.Length == 0 || Data.Morphs.Length != Controller.Weights.Length || Data.Morphs.Any(morph => morph.Vectors.Length != vertices))
            throw new InvalidDataException("Morph controller, targets and source geometry have incompatible extents.");
    }

    // Relative NiMorphData always contributes target zero at weight one.
    // Its scalar channel does not scale the base. Other vectors are added in
    // source order without normalization or subtracting the base a second time.
    internal FalloutNifMeshData BaseGeometry(FalloutNifMeshData geometry) => geometry with { Vertices = Data.Morphs[0].Vectors };
    internal float EffectiveWeight(int index, float value)
    {
        if ((uint)index >= Data.Morphs.Length || !float.IsFinite(value))
            throw new InvalidDataException("Morph weight has an invalid source index or value.");
        return index == 0 ? 1 : value;
    }
    internal IReadOnlyDictionary<string, System.Numerics.Vector3[]> RelativeDeltas() => Data.Morphs.Select((morph, index) => (morph, index))
        .Skip(1).ToDictionary(row => $"SourceMorph_{row.index}", row => row.morph.Vectors.Select(value => new System.Numerics.Vector3(value.X, value.Y, value.Z)).ToArray());
    internal int Index(string name)
    {
        var index = Array.FindIndex(Data.Morphs, morph => morph.Name == name);
        return index >= 0 ? index : throw new InvalidDataException($"Geometry has no source morph named {name}.");
    }
}

internal sealed class FalloutNifBoolAnimation
{
    private readonly bool _constant;
    private readonly FalloutNifBoolKey[] _keys;
    internal FalloutNifBoolAnimation(FalloutNifFile source, int interpolator)
    {
        var value = source.ReadObject(interpolator) as FalloutNifBoolInterpolator ?? throw new NotSupportedException("Visibility requires a boolean interpolator.");
        _keys = value.Data < 0 ? [] : (source.ReadObject(value.Data) as FalloutNifBoolData ?? throw new InvalidDataException("Boolean interpolator has non-boolean data.")).Keys;
        if (value.Value > 2 || (_keys.Length == 0 && value.Value == 2)) throw new InvalidDataException("Boolean interpolator has no valid value.");
        _constant = value.Value != 0;
    }
    internal bool Sample(float time)
    {
        if (!float.IsFinite(time)) throw new ArgumentOutOfRangeException(nameof(time));
        if (_keys.Length == 0) return _constant;
        var value = _keys[0].Value;
        foreach (var key in _keys)
        {
            if (key.Time > time) break;
            value = key.Value;
        }
        return value;
    }
}
