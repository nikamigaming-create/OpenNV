namespace OpenNV.Runtime.Formats.Gamebryo;

/// <summary>Source scalar interpolation and declared NiFloatExtraData targets.</summary>
internal sealed class FalloutNifFloatAnimation
{
    private readonly FalloutNifFloatInterpolator _interpolator;
    private readonly FalloutNifScalarKey[] _keys;

    internal FalloutNifFloatAnimation(FalloutNifFile source, int interpolator)
    {
        _interpolator = source.ReadObject(interpolator) as FalloutNifFloatInterpolator ??
            throw new NotSupportedException("The source float channel requires a supported float interpolator.");
        _keys = _interpolator.Data < 0 ? [] :
            (source.ReadObject(_interpolator.Data) as FalloutNifFloatData ??
                throw new InvalidDataException("The float interpolator has non-float data.")).Keys;
        foreach (var key in _keys)
        {
            if (key.Interpolation is not (1 or 2))
                throw new NotSupportedException($"Scalar interpolation {key.Interpolation} is unsupported.");
            if (key.Interpolation == 2 && (key.Forward is null || key.Backward is null))
                throw new InvalidDataException("Quadratic scalar keys require both authored tangents.");
        }
        if (_keys.Length == 0 && _interpolator.Value == float.MinValue)
            throw new InvalidDataException("The source float channel has neither keys nor a valid constant.");
    }

    internal float Sample(float sourceTime)
    {
        if (!float.IsFinite(sourceTime)) throw new ArgumentOutOfRangeException(nameof(sourceTime));
        return _keys.Length == 0 ? _interpolator.Value : FalloutNifAnimationSampler.SampleScalar(_keys, sourceTime);
    }
}

internal sealed class FalloutNifFloatExtraDataState
{
    private readonly Dictionary<(string Node, string Name), float> _values = [];

    internal IEnumerable<(string Node, string Name, float Value)> Values =>
        _values.Select(item => (item.Key.Node, item.Key.Name, item.Value));

    internal float Get(string node, string name) => _values.TryGetValue((node, name), out var value)
        ? value : throw new InvalidDataException($"Float property has no source declaration: {node}/{name}.");

    internal void Add(string node, string name, float value)
    {
        if (node.Length == 0 || name.Length == 0 || !float.IsFinite(value) || !_values.TryAdd((node, name), value))
            throw new InvalidDataException("A source float extra-data target is unnamed, duplicated or invalid.");
    }

    // NiExtraDataController's controller ID is the Extra Data Name, as defined
    // by https://www.niftools.org/nifxml/NiExtraDataController.html . No actor,
    // sequence or extra-data name is a special case in the binding contract.
    internal Action<float> Bind(FalloutNifFile source, FalloutNifControllerLink link)
    {
        if (link.ControllerType != "NiFloatExtraDataController" || link.PropertyType.Length != 0 ||
            link.Variable2.Length != 0 || !_values.ContainsKey((link.NodeName, link.Variable1)))
            throw new InvalidDataException($"Float channel has no declared source target: {link.NodeName}/{link.Variable1}.");
        var sampler = new FalloutNifFloatAnimation(source, link.Interpolator);
        return time => _values[(link.NodeName, link.Variable1)] = sampler.Sample(time);
    }
}
