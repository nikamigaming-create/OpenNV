namespace OpenNV.Runtime.Formats.Gamebryo;

/// <summary>Source scalar interpolation and declared NiFloatExtraData targets.</summary>
internal sealed class FalloutNifFloatAnimation
{
    private readonly float _constant;
    private readonly FalloutNifScalarKey[] _keys = [];
    private readonly FalloutNifSplineFloatInterpolator? _spline;
    private readonly FalloutNifSplineData? _points;
    private readonly int _count;

    internal FalloutNifFloatAnimation(FalloutNifFile source, int interpolator)
    {
        if (source.ReadObject(interpolator) is FalloutNifSplineFloatInterpolator spline)
        {
            _constant = spline.Value;
            if (spline.Handle != ushort.MaxValue)
            {
                _spline = spline;
                _points = source.ReadObject(spline.Data) as FalloutNifSplineData ?? throw new InvalidDataException("Float spline data is absent.");
                _count = (source.ReadObject(spline.BasisData) as FalloutNifSplineBasisData ?? throw new InvalidDataException("Float spline basis is absent.")).ControlPointCount;
                if (spline.StopTime <= spline.StartTime) throw new InvalidDataException("Float spline time range is invalid.");
                FalloutNifAnimationSampler.ValidateSplineHandle(_points, _count, spline.Compact, spline.Handle, 1, spline.Offset, spline.HalfRange);
            }
        }
        else
        {
            var input = source.ReadObject(interpolator) as FalloutNifFloatInterpolator ?? throw new NotSupportedException("Source float interpolator is unsupported.");
            _constant = input.Value;
            _keys = input.Data < 0 ? [] : (source.ReadObject(input.Data) as FalloutNifFloatData ??
                throw new InvalidDataException("The float interpolator has non-float data.")).Keys;
        }
        foreach (var key in _keys)
        {
            if (key.Interpolation is not (1 or 2))
                throw new NotSupportedException($"Scalar interpolation {key.Interpolation} is unsupported.");
            if (key.Interpolation == 2 && (key.Forward is null || key.Backward is null))
                throw new InvalidDataException("Quadratic scalar keys require both authored tangents.");
        }
        if (_spline is null && _keys.Length == 0 && _constant == float.MinValue)
            throw new InvalidDataException("The source float channel has neither keys nor a valid constant.");
    }

    internal float Sample(float sourceTime)
    {
        if (!float.IsFinite(sourceTime)) throw new ArgumentOutOfRangeException(nameof(sourceTime));
        float value;
        if (_spline is { } spline)
        {
            Span<float> sampled = stackalloc float[1];
            FalloutNifAnimationSampler.SampleSpline(_points!, _count, spline.Compact,
                spline.StartTime, spline.StopTime, spline.Handle, spline.Offset, spline.HalfRange, sourceTime, sampled);
            value = sampled[0];
        }
        else value = _keys.Length == 0 ? _constant : FalloutNifAnimationSampler.SampleScalar(_keys, sourceTime);
        return float.IsFinite(value) ? value : throw new InvalidDataException("Float channel has no finite source value.");
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
