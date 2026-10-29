namespace OpenNV.Runtime.Formats.Gamebryo;

internal readonly record struct FalloutNifTransformCoverageChannel(int Bone, byte Priority, float Weight,
    FalloutNifTransformComponents Components);

// Ordinary controller-interpolator normalization. Attachment priority is a
// signed byte and remains present at zero ease/weight until explicit removal.
internal struct FalloutNifBlendWeights
{
    private sbyte _highest, _next;
    private bool _active, _hasNext;
    private int _count;
    private float _highestEase, _highestSum, _nextSum;

    internal void Attach(byte priority)
    {
        var value = unchecked((sbyte)priority);
        _count++;
        if (!_active) { _highest = value; _active = true; }
        else if (value > _highest) { _next = _highest; _hasNext = true; _highest = value; }
        else if (value < _highest && (!_hasNext || value > _next)) { _next = value; _hasNext = true; }
    }

    internal void Gather(byte priority, float weight, float ease)
    {
        if (!float.IsFinite(weight) || weight < 0 || !float.IsFinite(ease) || ease is < 0 or > 1)
            throw new InvalidDataException("Controller interpolator has an invalid weight or ease.");
        var value = unchecked((sbyte)priority);
        if (value == _highest) { _highestEase = Math.Max(_highestEase, ease); _highestSum += weight * ease; }
        else if (_hasNext && value == _next) _nextSum += weight * ease;
        if (!float.IsFinite(_highestSum) || !float.IsFinite(_nextSum))
            throw new InvalidDataException("Controller interpolator weight sum is not finite.");
    }

    internal float Normalize(byte priority, float weight, float ease)
    {
        if (_count == 1) return 1;
        var value = unchecked((sbyte)priority);
        var product = weight * ease;
        if (_count == 2)
        {
            if (_highestSum + _nextSum == 0) return 0;
            if (!_hasNext) return product / _highestSum;
            if (_highestEase == 1) return value == _highest ? 1 : 0;
        }
        var denominator = _highestEase * _highestSum + (1 - _highestEase) * _nextSum;
        if (denominator <= 0) return 0;
        return value == _highest ? product * _highestEase / denominator :
            _hasNext && value == _next ? product * (1 - _highestEase) / denominator : 0;
    }
}

internal static class FalloutNifTransformCoverage
{
    internal static FalloutNifTransformComponents[] Resolve(int boneCount,
        IReadOnlyList<FalloutNifTransformCoverageChannel> channels)
    {
        if (boneCount < 0) throw new ArgumentOutOfRangeException(nameof(boneCount));
        var coverage = new FalloutNifTransformComponents[boneCount];
        var weights = new FalloutNifBlendWeights[boneCount];
        foreach (var channel in channels)
        {
            if (channel.Bone < 0 || channel.Bone >= boneCount || !float.IsFinite(channel.Weight) || channel.Weight < 0 ||
                (channel.Components & ~FalloutNifTransformComponents.All) != 0)
                throw new InvalidDataException("Source transform coverage has an invalid bone, weight or component.");
            weights[channel.Bone].Attach(channel.Priority);
        }
        foreach (var channel in channels) weights[channel.Bone].Gather(channel.Priority, channel.Weight, 1);
        foreach (var channel in channels)
            if (weights[channel.Bone].Normalize(channel.Priority, channel.Weight, 1) > 0)
                coverage[channel.Bone] |= channel.Components;
        return coverage;
    }
}
