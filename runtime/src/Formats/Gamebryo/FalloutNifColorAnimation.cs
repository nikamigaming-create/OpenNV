namespace OpenNV.Runtime.Formats.Gamebryo;

// Color4 uses the existing source scalar key/tangent owner for each channel.
// Curve data is immutable across instances; no emitter color is invented for
// a missing or empty key owner, and no RGBA channel is clamped on publication.
internal sealed class FalloutNifColorAnimation
{
    private readonly FalloutNifScalarKey[][] _channels;
    internal FalloutNifColorData Source { get; }

    internal FalloutNifColorAnimation(FalloutNifFile file, int data)
    {
        Source = file.ReadObject(data) as FalloutNifColorData ??
            throw new InvalidDataException("Particle color modifier does not reference NiColorData.");
        if (Source.Keys.Length == 0)
            throw new InvalidDataException("Particle color modifier has no authored color keys.");
        for (var index = 1; index < Source.Keys.Length; index++)
            if (Source.Keys[index].Time <= Source.Keys[index - 1].Time)
                throw new NotSupportedException("Repeated color key times require an unowned discontinuity rule.");
        _channels = Enumerable.Range(0, 4).Select(channel => Source.Keys.Select(key => new FalloutNifScalarKey(
            key.Time, Component(key.Value, channel), key.Forward is { } forward ? Component(forward, channel) : null,
            key.Backward is { } backward ? Component(backward, channel) : null, key.Tbc, key.Interpolation)).ToArray()).ToArray();
        foreach (var channel in _channels)
        {
            // Validate every declared tangent owner, including keys outside the
            // initially sampled interval, using the actual shared sampler.
            foreach (var key in channel) _ = FalloutNifAnimationSampler.SampleScalar(channel, key.Time);
        }
    }

    internal FalloutNifVector4 Sample(float time)
    {
        if (!float.IsFinite(time)) throw new InvalidDataException("Color curve time is not finite.");
        var value = new FalloutNifVector4(
            FalloutNifAnimationSampler.SampleScalar(_channels[0], time),
            FalloutNifAnimationSampler.SampleScalar(_channels[1], time),
            FalloutNifAnimationSampler.SampleScalar(_channels[2], time),
            FalloutNifAnimationSampler.SampleScalar(_channels[3], time));
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z) || !float.IsFinite(value.W))
            throw new InvalidDataException("Color curve publication is not finite.");
        return value;
    }

    private static float Component(FalloutNifVector4 value, int channel) => channel switch
    {
        0 => value.X,
        1 => value.Y,
        2 => value.Z,
        3 => value.W,
        _ => throw new ArgumentOutOfRangeException(nameof(channel)),
    };
}
