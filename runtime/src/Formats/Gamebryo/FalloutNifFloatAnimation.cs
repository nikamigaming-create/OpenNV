namespace OpenNV.Runtime.Formats.Gamebryo;

internal enum FalloutNifScalarKind { FloatExtraData, MorphWeight, Visibility }
internal interface IFalloutNifScalarSampler { float Sample(float sourceTime); }
internal readonly record struct FalloutNifScalarTarget(FalloutNifScalarKind Kind, string Node, string Name);
internal sealed record FalloutNifSnapshotScalar(FalloutNifScalarTarget Target, byte Priority, float Value)
{
    internal void Validate()
    {
        if (Target.Kind is not (FalloutNifScalarKind.FloatExtraData or FalloutNifScalarKind.MorphWeight or FalloutNifScalarKind.Visibility) ||
            string.IsNullOrWhiteSpace(Target.Node) || string.IsNullOrWhiteSpace(Target.Name) || !float.IsFinite(Value) ||
            Target.Kind == FalloutNifScalarKind.Visibility && Value is not (0 or 1))
            throw new InvalidDataException("Snapshot scalar has an invalid typed target or value.");
    }
}

// The sampler and current-value access refer to the same declared controller
// target. Snapshot constants never resample the outgoing clip at a guessed time.
internal sealed record RuntimeNifScalarBinding(FalloutNifScalarTarget Target, byte Priority,
    IFalloutNifScalarSampler Sampler, Func<float> Read, Action<float> Publish, Func<float, float>? Effective = null)
{
    internal float Sample(float time) => Normalize(Sampler.Sample(time));
    internal float Normalize(float value) => Effective?.Invoke(value) ?? value;

    internal static RuntimeNifScalarBinding Visibility(FalloutNifFile source, FalloutNifControllerLink link,
        Func<bool> read, Action<bool> publish)
    {
        if (link.ControllerType != "NiVisController" || link.PropertyType.Length != 0 ||
            link.Variable1.Length != 0 || link.Variable2.Length != 0)
            throw new InvalidDataException("Visibility binding differs from its source controller.");
        return new(new(FalloutNifScalarKind.Visibility, link.NodeName, link.ControllerType), link.Priority,
            new VisibilitySampler(source, link.Interpolator), () => FalloutNifBoolAnimation.Encode(read()),
            value => publish(FalloutNifBoolAnimation.BlendedValue(value)));
    }

    private sealed class VisibilitySampler(FalloutNifFile source, int interpolator) : IFalloutNifScalarSampler
    {
        private readonly FalloutNifBoolAnimation _source = new(source, interpolator);
        public float Sample(float sourceTime) => FalloutNifBoolAnimation.Encode(_source.Sample(sourceTime));
    }
}

/// <summary>Source scalar interpolation and declared NiFloatExtraData targets.</summary>
internal sealed class FalloutNifFloatAnimation : IFalloutNifScalarSampler
{
    private readonly float _constant;
    private readonly FalloutNifScalarKey[] _keys = [];
    private readonly FalloutNifSplineFloatInterpolator? _spline;
    private readonly FalloutNifSplineData? _points;
    private readonly int _count;

    float IFalloutNifScalarSampler.Sample(float sourceTime) => Sample(sourceTime);

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

    internal void Restore(string node, string name, float value)
    {
        _ = Get(node, name);
        if (!float.IsFinite(value)) throw new InvalidDataException("Saved float property is not finite.");
        _values[(node, name)] = value;
    }

    internal void Add(string node, string name, float value)
    {
        if (node.Length == 0 || name.Length == 0 || !float.IsFinite(value) || !_values.TryAdd((node, name), value))
            throw new InvalidDataException("A source float extra-data target is unnamed, duplicated or invalid.");
    }

    // NiExtraDataController's controller ID is the Extra Data Name, as defined
    // by https://www.niftools.org/nifxml/NiExtraDataController.html . No actor,
    // sequence or extra-data name is a special case in the binding contract.
    internal RuntimeNifScalarBinding Bind(FalloutNifFile source, FalloutNifControllerLink link)
    {
        if (link.ControllerType != "NiFloatExtraDataController" || link.PropertyType.Length != 0 ||
            link.Variable2.Length != 0 || !_values.ContainsKey((link.NodeName, link.Variable1)))
            throw new InvalidDataException($"Float channel has no declared source target: {link.NodeName}/{link.Variable1}.");
        var sampler = new FalloutNifFloatAnimation(source, link.Interpolator);
        return new(new(FalloutNifScalarKind.FloatExtraData, link.NodeName, link.Variable1), link.Priority, sampler,
            () => Get(link.NodeName, link.Variable1), value => Restore(link.NodeName, link.Variable1, value));
    }
}
