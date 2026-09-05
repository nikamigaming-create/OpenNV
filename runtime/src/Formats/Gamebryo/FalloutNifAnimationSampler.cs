using System.Numerics;

namespace OpenNV.Runtime.Formats.Gamebryo;

internal sealed record FalloutNifAnimationSample(
    FalloutNifVector3? Translation,
    FalloutNifQuaternion? Rotation,
    float? Scale);

// Samples source-local channels. A missing component remains missing: selecting
// a clip, blending, root motion, text events and the current pose belong to the
// animation/gameplay owner, not the format reader.
internal sealed class FalloutNifAnimationSampler
{
    private const uint InvalidHandle = ushort.MaxValue;
    private readonly FalloutNifTransformInterpolator? _keyed;
    private readonly FalloutNifTransformData? _keys;
    private readonly FalloutNifSplineTransformInterpolator? _spline;
    private readonly FalloutNifSplineData? _points;
    private readonly int _pointCount;

    internal FalloutNifAnimationSampler(FalloutNifFile source, int interpolator)
    {
        switch (source.ReadObject(interpolator))
        {
            case FalloutNifTransformInterpolator keyed:
                _keyed = keyed;
                if (keyed.Data >= 0)
                    _keys = source.ReadObject(keyed.Data) as FalloutNifTransformData ??
                        throw new InvalidDataException("Transform interpolator has non-transform data.");
                ValidateKeys();
                break;
            case FalloutNifSplineTransformInterpolator spline:
                _spline = spline;
                if (spline.TranslationHandle != InvalidHandle || spline.RotationHandle != InvalidHandle ||
                    spline.ScaleHandle != InvalidHandle)
                {
                    _points = source.ReadObject(spline.Data) as FalloutNifSplineData ??
                        throw new InvalidDataException("Spline interpolator has non-spline data.");
                    _pointCount = (source.ReadObject(spline.BasisData) as FalloutNifSplineBasisData ??
                        throw new InvalidDataException("Spline interpolator has non-basis data.")).ControlPointCount;
                    if (_pointCount < 4 || spline.StopTime <= spline.StartTime)
                        throw new InvalidDataException("Cubic spline basis or time range is invalid.");
                    ValidateHandle(spline.TranslationHandle, 3, spline.TranslationOffset, spline.TranslationHalfRange);
                    ValidateHandle(spline.RotationHandle, 4, spline.RotationOffset, spline.RotationHalfRange);
                    ValidateHandle(spline.ScaleHandle, 1, spline.ScaleOffset, spline.ScaleHalfRange);
                }
                break;
            default:
                throw new NotSupportedException($"Animation transform interpolator {source.Blocks[interpolator].TypeName} is unsupported.");
        }
    }

    internal FalloutNifAnimationSample Sample(float sourceTime)
    {
        if (!float.IsFinite(sourceTime))
            throw new ArgumentOutOfRangeException(nameof(sourceTime));
        if (_spline is { } spline)
        {
            var translation = Optional(spline.Translation);
            var rotation = Optional(spline.Rotation);
            var scale = Optional(spline.Scale);
            if (spline.TranslationHandle != InvalidHandle)
            {
                var value = SampleSpline(spline.TranslationHandle, 3, spline.TranslationOffset,
                    spline.TranslationHalfRange, sourceTime);
                translation = new(value[0], value[1], value[2]);
            }
            if (spline.RotationHandle != InvalidHandle)
            {
                var value = SampleSpline(spline.RotationHandle, 4, spline.RotationOffset,
                    spline.RotationHalfRange, sourceTime);
                rotation = new(value[0], value[1], value[2], value[3]);
            }
            if (spline.ScaleHandle != InvalidHandle)
                scale = SampleSpline(spline.ScaleHandle, 1, spline.ScaleOffset, spline.ScaleHalfRange, sourceTime)[0];
            return new(translation, rotation, scale);
        }
        var keyed = _keyed!;
        var result = new FalloutNifAnimationSample(Optional(keyed.Translation), Optional(keyed.Rotation), Optional(keyed.Scale));
        if (_keys is not { } keys)
            return result;
        if (keys.Translations.Length != 0)
            result = result with { Translation = SampleVector(keys.Translations, sourceTime) };
        if (keys.Scales.Length != 0)
            result = result with { Scale = SampleScalar(keys.Scales, sourceTime) };
        if (keys.QuaternionRotations.Length != 0)
            result = result with { Rotation = SampleQuaternion(keys.QuaternionRotations, sourceTime) };
        if (keys.XyzRotations.Length != 0)
        {
            var x = SampleScalar(keys.XyzRotations[0], sourceTime) * 0.5f;
            var y = SampleScalar(keys.XyzRotations[1], sourceTime) * 0.5f;
            var z = SampleScalar(keys.XyzRotations[2], sourceTime) * 0.5f;
            var cx = MathF.Cos(x); var sx = MathF.Sin(x);
            var cy = MathF.Cos(y); var sy = MathF.Sin(y);
            var cz = MathF.Cos(z); var sz = MathF.Sin(z);
            result = result with
            {
                Rotation = new(cx * cy * cz + sx * sy * sz,
                sx * cy * cz - cx * sy * sz, cx * sy * cz + sx * cy * sz, cx * cy * sz - sx * sy * cz)
            };
        }
        return result;
    }

    private void ValidateHandle(uint handle, int components, float offset, float halfRange)
    {
        if (handle == InvalidHandle)
            return;
        var count = _spline!.Compact ? _points!.CompactControlPoints.Length : _points!.FloatControlPoints.Length;
        if ((ulong)handle + (ulong)_pointCount * (uint)components > (ulong)count ||
            !float.IsFinite(offset) || !float.IsFinite(halfRange) || halfRange < 0)
            throw new InvalidDataException("Spline channel exceeds its original control-point array or has an invalid range.");
    }

    private float[] SampleSpline(uint handle, int components, float offset, float halfRange, float sourceTime)
    {
        var spline = _spline!;
        var normalized = Math.Clamp((sourceTime - spline.StartTime) / (spline.StopTime - spline.StartTime), 0.0f, 1.0f);
        var span = normalized >= 1.0f ? _pointCount - 1 : 3 + (int)(normalized * (_pointCount - 3));
        Span<float> basis = stackalloc float[4];
        Span<float> left = stackalloc float[4];
        Span<float> right = stackalloc float[4];
        basis[0] = 1.0f;
        for (var degree = 1; degree <= 3; degree++)
        {
            left[degree] = normalized - Knot(span + 1 - degree);
            right[degree] = Knot(span + degree) - normalized;
            var saved = 0.0f;
            for (var index = 0; index < degree; index++)
            {
                var term = basis[index] / (right[index + 1] + left[degree - index]);
                basis[index] = saved + right[index + 1] * term;
                saved = left[degree - index] * term;
            }
            basis[degree] = saved;
        }
        var result = new float[components];
        for (var component = 0; component < components; component++)
            for (var index = 0; index < 4; index++)
            {
                var pointIndex = checked((int)handle + (span - 3 + index) * components + component);
                var point = spline.Compact
                    ? _points!.CompactControlPoints[pointIndex] / (float)short.MaxValue * halfRange + offset
                    : _points!.FloatControlPoints[pointIndex];
                result[component] += basis[index] * point;
            }
        return result;
    }

    private float Knot(int index) => index <= 3 ? 0.0f : index >= _pointCount
        ? 1.0f : (float)(index - 3) / (_pointCount - 3);

    private void ValidateKeys()
    {
        if (_keys is not { } keys)
            return;
        if (keys.QuaternionRotations.Length != 0 && keys.RotationType != 1)
            throw new NotSupportedException($"Quaternion interpolation type {keys.RotationType} is not implemented.");
        if (keys.XyzRotations.Length != 0 &&
            (keys.XyzRotations.Length != 3 || keys.XyzRotations.Any(axis => axis.Length == 0)))
            throw new InvalidDataException("XYZ animation requires three defined scalar axes.");
        foreach (var key in keys.Scales.Concat(keys.XyzRotations.SelectMany(axis => axis)))
            ValidateInterpolation(key.Interpolation, key.Forward.HasValue, key.Backward.HasValue);
        foreach (var key in keys.Translations)
            ValidateInterpolation(key.Interpolation, key.Forward.HasValue, key.Backward.HasValue);
    }

    private static void ValidateInterpolation(uint type, bool forward, bool backward)
    {
        if (type is not (1 or 2))
            throw new NotSupportedException($"Animation interpolation type {type} is not implemented.");
        if (type == 2 && (!forward || !backward))
            throw new InvalidDataException("Quadratic animation key lacks tangents.");
    }

    private static float? Optional(float value) => value == float.MinValue ? null : value;
    private static FalloutNifVector3? Optional(FalloutNifVector3 value)
    {
        if (value.X == float.MinValue && value.Y == float.MinValue && value.Z == float.MinValue)
            return null;
        if (value.X == float.MinValue || value.Y == float.MinValue || value.Z == float.MinValue)
            throw new InvalidDataException("Animation translation is partially invalid.");
        return value;
    }
    private static FalloutNifQuaternion? Optional(FalloutNifQuaternion value)
    {
        if (value.W == float.MinValue && value.X == float.MinValue && value.Y == float.MinValue && value.Z == float.MinValue)
            return null;
        if (value.W == float.MinValue || value.X == float.MinValue || value.Y == float.MinValue || value.Z == float.MinValue)
            throw new InvalidDataException("Animation rotation is partially invalid.");
        return value;
    }

    private static int Interval(int count, Func<int, float> timeAt, float time)
    {
        if (time <= timeAt(0)) return 0;
        if (time >= timeAt(count - 1)) return count - 1;
        var low = 0; var high = count - 1;
        while (high - low > 1)
        {
            var middle = (low + high) / 2;
            if (time < timeAt(middle)) high = middle; else low = middle;
        }
        return low;
    }

    internal static float SampleScalar(FalloutNifScalarKey[] keys, float time)
    {
        var index = Interval(keys.Length, i => keys[i].Time, time);
        var a = keys[index];
        if (time <= a.Time || index == keys.Length - 1) return a.Value;
        var b = keys[index + 1];
        return Interpolate(a.Value, b.Value, a.Backward, b.Forward, a.Interpolation, (time - a.Time) / (b.Time - a.Time));
    }

    private static FalloutNifVector3 SampleVector(FalloutNifVectorKey[] keys, float time)
    {
        var index = Interval(keys.Length, i => keys[i].Time, time);
        var a = keys[index];
        if (time <= a.Time || index == keys.Length - 1) return a.Value;
        var b = keys[index + 1];
        var amount = (time - a.Time) / (b.Time - a.Time);
        return new(Interpolate(a.Value.X, b.Value.X, a.Backward?.X, b.Forward?.X, a.Interpolation, amount),
            Interpolate(a.Value.Y, b.Value.Y, a.Backward?.Y, b.Forward?.Y, a.Interpolation, amount),
            Interpolate(a.Value.Z, b.Value.Z, a.Backward?.Z, b.Forward?.Z, a.Interpolation, amount));
    }

    private static float Interpolate(float a, float b, float? outgoing, float? incoming, uint type, float amount)
    {
        if (type == 1) return a + (b - a) * amount;
        var squared = amount * amount; var cubed = squared * amount;
        return a * (2 * cubed - 3 * squared + 1) + b * (-2 * cubed + 3 * squared) +
            outgoing!.Value * (cubed - 2 * squared + amount) + incoming!.Value * (cubed - squared);
    }

    private static FalloutNifQuaternion SampleQuaternion(FalloutNifQuaternionKey[] keys, float time)
    {
        var index = Interval(keys.Length, i => keys[i].Time, time);
        var a = keys[index];
        if (time <= a.Time || index == keys.Length - 1) return a.Value;
        var b = keys[index + 1];
        var value = Quaternion.Slerp(new(a.Value.X, a.Value.Y, a.Value.Z, a.Value.W),
            new(b.Value.X, b.Value.Y, b.Value.Z, b.Value.W), (time - a.Time) / (b.Time - a.Time));
        return new(value.W, value.X, value.Y, value.Z);
    }
}
