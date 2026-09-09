using System.Numerics;

namespace OpenNV.Runtime.Formats.Gamebryo;

internal sealed class FalloutNifPoint3Animation
{
    private readonly FalloutNifVector3 _constant;
    private readonly FalloutNifVectorKey[] _keys = [];
    private readonly FalloutNifSplinePoint3Interpolator? _spline;
    private readonly FalloutNifSplineData? _points;
    private readonly int _count;

    internal FalloutNifPoint3Animation(FalloutNifFile source, int interpolator)
    {
        switch (source.ReadObject(interpolator))
        {
            case FalloutNifPoint3Interpolator input:
                _constant = input.Value;
                if (input.Data >= 0) _keys = (source.ReadObject(input.Data) as FalloutNifPositionData ??
                    throw new InvalidDataException("Point3 interpolator has non-vector keys.")).Keys;
                break;
            case FalloutNifSplinePoint3Interpolator spline:
                _constant = spline.Value;
                if (spline.Handle == ushort.MaxValue) break;
                _spline = spline;
                _points = source.ReadObject(spline.Data) as FalloutNifSplineData ?? throw new InvalidDataException("Point3 spline data is absent.");
                _count = (source.ReadObject(spline.BasisData) as FalloutNifSplineBasisData ??
                    throw new InvalidDataException("Point3 spline basis is absent.")).ControlPointCount;
                if (spline.StopTime <= spline.StartTime) throw new InvalidDataException("Point3 spline has an invalid time range.");
                FalloutNifAnimationSampler.ValidateSplineHandle(_points, _count, spline.Compact, spline.Handle, 3, spline.Offset, spline.HalfRange);
                break;
            default: throw new NotSupportedException("Point3 interpolator is unsupported.");
        }
    }

    internal FalloutNifVector3 Sample(float time)
    {
        if (!float.IsFinite(time)) throw new ArgumentOutOfRangeException(nameof(time));
        FalloutNifVector3 value;
        if (_spline is { } spline)
        {
            Span<float> sampled = stackalloc float[3];
            FalloutNifAnimationSampler.SampleSpline(_points!, _count, spline.Compact, spline.StartTime, spline.StopTime,
                spline.Handle, spline.Offset, spline.HalfRange, time, sampled);
            value = new(sampled[0], sampled[1], sampled[2]);
        }
        else value = _keys.Length == 0 ? _constant : FalloutNifAnimationSampler.SampleVector(_keys, time);
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z) ||
            value.X == float.MinValue || value.Y == float.MinValue || value.Z == float.MinValue)
            throw new InvalidDataException("Point3 channel has no finite source value.");
        return value;
    }
}

internal readonly record struct FalloutNifAnimationSample(
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
    private readonly PathSampler? _path;

    internal FalloutNifAnimationSampler(FalloutNifFile source, int interpolator)
    {
        switch (source.ReadObject(interpolator))
        {
            case FalloutNifPathInterpolator path:
                _path = new PathSampler(source, path);
                break;
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
        if (_path is not null) return new(_path.Sample(sourceTime), null, null);
        if (_spline is { } spline)
        {
            var translation = Optional(spline.Translation);
            var rotation = Optional(spline.Rotation);
            var scale = Optional(spline.Scale);
            Span<float> value = stackalloc float[4];
            if (spline.TranslationHandle != InvalidHandle)
            {
                SampleSpline(spline.TranslationHandle, spline.TranslationOffset, spline.TranslationHalfRange, sourceTime, value[..3]);
                translation = new(value[0], value[1], value[2]);
            }
            if (spline.RotationHandle != InvalidHandle)
            {
                SampleSpline(spline.RotationHandle, spline.RotationOffset, spline.RotationHalfRange, sourceTime, value);
                rotation = new(value[0], value[1], value[2], value[3]);
            }
            if (spline.ScaleHandle != InvalidHandle)
            {
                SampleSpline(spline.ScaleHandle, spline.ScaleOffset, spline.ScaleHalfRange, sourceTime, value[..1]);
                scale = value[0];
            }
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
            result = result with { Rotation = SampleQuaternion(keys.QuaternionRotations, sourceTime, keys.RotationType) };
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
        ValidateSplineHandle(_points!, _pointCount, _spline!.Compact, handle, components, offset, halfRange);
    }

    internal static void ValidateSplineHandle(FalloutNifSplineData points, int pointCount, bool compact,
        uint handle, int components, float offset, float halfRange)
    {
        var count = compact ? points.CompactControlPoints.Length : points.FloatControlPoints.Length;
        if (pointCount < 4 || (ulong)handle + (ulong)pointCount * (uint)components > (ulong)count ||
            !float.IsFinite(offset) || !float.IsFinite(halfRange) || halfRange < 0)
            throw new InvalidDataException("Spline channel exceeds its original control-point array or has an invalid range.");
    }

    private void SampleSpline(uint handle, float offset, float halfRange, float sourceTime, Span<float> result)
        => SampleSpline(_points!, _pointCount, _spline!.Compact, _spline.StartTime, _spline.StopTime,
            handle, offset, halfRange, sourceTime, result);

    internal static void SampleSpline(FalloutNifSplineData points, int pointCount, bool compact, float start, float stop,
        uint handle, float offset, float halfRange, float sourceTime, Span<float> result)
    {
        var normalized = Math.Clamp((sourceTime - start) / (stop - start), 0.0f, 1.0f);
        var span = normalized >= 1.0f ? pointCount - 1 : 3 + (int)(normalized * (pointCount - 3));
        float Knot(int index) => index <= 3 ? 0.0f : index >= pointCount ? 1.0f : (float)(index - 3) / (pointCount - 3);
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
        result.Clear();
        for (var component = 0; component < result.Length; component++)
            for (var index = 0; index < 4; index++)
            {
                var pointIndex = checked((int)handle + (span - 3 + index) * result.Length + component);
                var point = compact
                    ? points.CompactControlPoints[pointIndex] / (float)short.MaxValue * halfRange + offset
                    : points.FloatControlPoints[pointIndex];
                result[component] += basis[index] * point;
            }
    }

    private void ValidateKeys()
    {
        if (_keys is not { } keys)
            return;
        if (keys.QuaternionRotations.Length != 0 && keys.RotationType is not (1 or 3 or 5))
            throw new NotSupportedException($"Quaternion interpolation type {keys.RotationType} is not implemented.");
        if (keys.RotationType == 3)
            foreach (var key in keys.QuaternionRotations)
                if (key.Tbc is not { } tbc || !float.IsFinite(tbc.X) || !float.IsFinite(tbc.Y) || !float.IsFinite(tbc.Z))
                    throw new InvalidDataException("TCB quaternion parameters are absent or nonfinite.");
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
        if (type is not (1 or 2 or 5))
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

    private static int Interval<T>(T[] keys, Func<T, float> timeAt, float time)
    {
        if (time <= timeAt(keys[0])) return 0;
        if (time >= timeAt(keys[^1])) return keys.Length - 1;
        var low = 0; var high = keys.Length - 1;
        while (high - low > 1)
        {
            var middle = (low + high) / 2;
            if (time < timeAt(keys[middle])) high = middle; else low = middle;
        }
        return low;
    }

    internal static float SampleScalar(FalloutNifScalarKey[] keys, float time)
    {
        var index = Interval(keys, static key => key.Time, time);
        var a = keys[index];
        if (time <= a.Time || index == keys.Length - 1) return a.Value;
        var b = keys[index + 1];
        return Interpolate(a.Value, b.Value, a.Backward, b.Forward, a.Interpolation, (time - a.Time) / (b.Time - a.Time));
    }

    internal static FalloutNifVector3 SampleVector(FalloutNifVectorKey[] keys, float time)
    {
        var index = Interval(keys, static key => key.Time, time);
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
        // CONST_KEY holds the preceding sample until the next source key.
        // Interval selects the next key exactly at its authored timestamp.
        if (type == 5) return a;
        if (type == 1) return a + (b - a) * amount;
        var squared = amount * amount; var cubed = squared * amount;
        return a * (2 * cubed - 3 * squared + 1) + b * (-2 * cubed + 3 * squared) +
            outgoing!.Value * (cubed - 2 * squared + amount) + incoming!.Value * (cubed - squared);
    }

    private sealed class PathSampler
    {
        private readonly FalloutNifVectorKey[] _positions;
        private readonly FalloutNifScalarKey[] _percent;
        private readonly bool _constantVelocity;
        private readonly double[] _lengths;
        // Eight-point Gauss-Legendre integration of the cubic path's speed.
        private static readonly double[] Abscissae = [.1834346424956498, .5255324099163290, .7966664774136267, .9602898564975363];
        private static readonly double[] Weights = [.3626837833783620, .3137066458778873, .2223810344533745, .1012285362903763];

        internal PathSampler(FalloutNifFile file, FalloutNifPathInterpolator path)
        {
            if ((path.Flags & ~0x13) != 0 || (path.Flags & 2) == 0)
                throw new NotSupportedException("Path orientation, banking or closed-curve behavior is unbound.");
            _positions = (file.ReadObject(path.PathData) as FalloutNifPositionData ??
                throw new InvalidDataException("Path has no position data.")).Keys;
            _percent = (file.ReadObject(path.PercentData) as FalloutNifFloatData ??
                throw new InvalidDataException("Path has no percentage data.")).Keys;
            if (_positions.Length < 2 || _percent.Length == 0 ||
                _positions.Any(key => key.Interpolation is not (1 or 2)) ||
                _percent.Any(key => key.Interpolation is not (1 or 2 or 5)))
                throw new NotSupportedException("Path keys are outside the linear/cubic contract.");
            _constantVelocity = (path.Flags & 16) != 0;
            _lengths = new double[_positions.Length];
            for (var i = 1; i < _lengths.Length; i++) _lengths[i] = _lengths[i - 1] + Length(i - 1, 1);
        }

        internal FalloutNifVector3 Sample(float time)
        {
            var fraction = Math.Clamp(SampleScalar(_percent, time), 0, 1);
            if (!_constantVelocity || _lengths[^1] == 0)
                return SampleVector(_positions, _positions[0].Time + fraction * (_positions[^1].Time - _positions[0].Time));
            if (fraction == 0) return _positions[0].Value;
            if (fraction == 1) return _positions[^1].Value;
            var distance = fraction * _lengths[^1];
            var segment = 0;
            while (segment < _lengths.Length - 2 && _lengths[segment + 1] < distance) segment++;
            distance -= _lengths[segment];
            double low = 0, high = 1;
            for (var iteration = 0; iteration < 20; iteration++)
            {
                var middle = (low + high) / 2;
                if (Length(segment, middle) < distance) low = middle; else high = middle;
            }
            return SampleVector(_positions, _positions[segment].Time +
                (float)((low + high) / 2) * (_positions[segment + 1].Time - _positions[segment].Time));
        }

        private double Length(int segment, double end)
        {
            double sum = 0;
            for (var i = 0; i < Abscissae.Length; i++)
                sum += Weights[i] * (Speed(segment, end * (1 - Abscissae[i]) / 2) +
                    Speed(segment, end * (1 + Abscissae[i]) / 2));
            return sum * end / 2;
        }

        private double Speed(int segment, double fraction)
        {
            var a = _positions[segment]; var b = _positions[segment + 1];
            static Vector3 V(FalloutNifVector3 v) => new(v.X, v.Y, v.Z);
            if (a.Interpolation == 1) return (V(b.Value) - V(a.Value)).Length();
            var u = (float)fraction; var squared = u * u;
            var derivative = V(a.Value) * (6 * squared - 6 * u) + V(b.Value) * (-6 * squared + 6 * u) +
                V(a.Backward!.Value) * (3 * squared - 4 * u + 1) + V(b.Forward!.Value) * (3 * squared - 2 * u);
            return derivative.Length();
        }
    }

    internal static FalloutNifQuaternion SampleQuaternion(FalloutNifQuaternionKey[] keys, float time, uint type = 1)
    {
        var index = Interval(keys, static key => key.Time, time);
        var a = keys[index];
        if (time <= a.Time || index == keys.Length - 1 || type == 5) return a.Value;
        var b = keys[index + 1];
        var amount = (time - a.Time) / (b.Time - a.Time);
        var value = type == 3 ? FalloutNifQuaternionCurve.Sample(keys, index, amount) :
            Quaternion.Slerp(new(a.Value.X, a.Value.Y, a.Value.Z, a.Value.W),
                new(b.Value.X, b.Value.Y, b.Value.Z, b.Value.W), amount);
        return new(value.W, value.X, value.Y, value.Z);
    }
}
