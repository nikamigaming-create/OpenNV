using System.Buffers.Binary;
using System.Security.Cryptography;
using Godot;

namespace OpenNV.Runtime.Presentation.CharacterCreation;

internal sealed class OwnedGamebryoFaceGenTextureRuntime
{
    private const int HeaderBytes = 64;
    private const int ControlBytes = 4;
    private const int Channels = 3;
    private const int FaceTextureSlot = 7;
    private const byte EnabledFlag = 0x04;
    private const byte MaxedFlag = 0x40;
    private const byte InvertFlag = 0x80;
    private const byte IntensityMask = 0x03;
    private const int SlotShift = 3;
    private const byte SlotMask = 0x07;
    private static readonly float Neutral = (byte.MaxValue + 1.0f) / 2.0f;
    private static readonly float[] IntensityScales = Enumerable
        .Range(0, IntensityMask + 1)
        .Select(index => 1.0f / (InvertFlag >> (index * (SlotShift - 1))))
        .ToArray();

    private readonly byte[] _egt;
    private readonly IReadOnlyList<float> _baseline;
    private readonly IReadOnlyDictionary<string, OpeningNativeFaceGenTextureControl> _controls;
    private readonly float _morphWeightScale;
    private readonly float _minimum;
    private readonly float _maximum;
    private readonly Dictionary<string, float> _values;

    internal OwnedGamebryoFaceGenTextureRuntime(
        string egtPath,
        string egtSha256,
        IReadOnlyList<float> baseline,
        IReadOnlyList<OpeningNativeFaceGenTextureControl> controls,
        float minimum,
        float maximum,
        float morphWeightScale,
        float resetValue)
    {
        _egt = File.ReadAllBytes(egtPath);
        var actual = Convert.ToHexString(SHA256.HashData(_egt)).ToLowerInvariant();
        if (!actual.Equals(egtSha256, StringComparison.OrdinalIgnoreCase) ||
            baseline.Count == 0 || baseline.Any(value => !float.IsFinite(value)) ||
            controls.Count == 0 ||
            controls.Select(value => value.SettingEntity)
                .Distinct(StringComparer.Ordinal).Count() != controls.Count ||
            controls.Any(value =>
                value.Axis.Count != baseline.Count ||
                value.Axis.Any(axis => !float.IsFinite(axis)) ||
                !FloatSha256(value.Axis).Equals(
                    value.AxisSha256,
                    StringComparison.OrdinalIgnoreCase)) ||
            !float.IsFinite(minimum) || !float.IsFinite(maximum) ||
            minimum >= maximum ||
            !float.IsFinite(morphWeightScale) || morphWeightScale <= 0.0f ||
            !float.IsFinite(resetValue))
            throw new InvalidOperationException(
                "Owned Gamebryo FaceGen texture contract is invalid.");
        _baseline = baseline;
        _controls = controls.ToDictionary(value => value.SettingEntity, StringComparer.Ordinal);
        _morphWeightScale = morphWeightScale;
        _minimum = minimum;
        _maximum = maximum;
        _values = controls.ToDictionary(
            value => value.SettingEntity,
            _ => resetValue,
            StringComparer.Ordinal);
        _ = Decode(_baseline);
    }

    internal IReadOnlyDictionary<string, float> Values => _values;

    internal static string CoordinateSha256(
        IReadOnlyList<float> baseline,
        IReadOnlyList<OpeningNativeFaceGenTextureControl> controls,
        IReadOnlyDictionary<string, float> values,
        float morphWeightScale)
    {
        if (baseline.Count == 0 || controls.Count == 0 ||
            controls.Count != values.Count ||
            controls.Any(control =>
                control.Axis.Count != baseline.Count ||
                !values.TryGetValue(control.SettingEntity, out var value) ||
                !float.IsFinite(value)) ||
            !float.IsFinite(morphWeightScale) || morphWeightScale <= 0.0f)
            throw new InvalidOperationException(
                "Owned Gamebryo FaceGen texture coordinates are invalid.");
        return FloatSha256(Coordinates(baseline, controls, values, morphWeightScale));
    }

    internal static IReadOnlyList<float> Coordinates(
        IReadOnlyList<float> baseline,
        IReadOnlyList<OpeningNativeFaceGenTextureControl> controls,
        IReadOnlyDictionary<string, float> values,
        float morphWeightScale)
    {
        if (baseline.Count == 0 || controls.Count == 0 ||
            controls.Count != values.Count ||
            controls.Any(control =>
                control.Axis.Count != baseline.Count ||
                !values.TryGetValue(control.SettingEntity, out var value) ||
                !float.IsFinite(value)) ||
            !float.IsFinite(morphWeightScale) || morphWeightScale <= 0.0f)
            throw new InvalidOperationException(
                "Owned Gamebryo FaceGen texture coordinates are invalid.");
        var coordinates = baseline.ToArray();
        foreach (var control in controls)
        {
            var value = values[control.SettingEntity] * morphWeightScale;
            for (var index = 0; index < coordinates.Length; index++)
                coordinates[index] += value * control.Axis[index];
        }
        return coordinates;
    }

    private static string FloatSha256(IReadOnlyList<float> values)
    {
        var payload = new byte[values.Count * sizeof(float)];
        for (var index = 0; index < values.Count; index++)
            BinaryPrimitives.WriteSingleLittleEndian(
                payload.AsSpan(index * sizeof(float), sizeof(float)),
                values[index]);
        return Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
    }

    internal ImageTexture Apply(string settingEntity, float uiValue)
    {
        if (!_controls.ContainsKey(settingEntity) || !float.IsFinite(uiValue) ||
            uiValue < _minimum || uiValue > _maximum)
            throw new InvalidOperationException(
                $"Owned Gamebryo FaceGen texture control is unsupported: {settingEntity}.");
        _values[settingEntity] = uiValue;
        var weights = _baseline.ToArray();
        foreach (var pair in _values)
        {
            var axis = _controls[pair.Key].Axis;
            for (var index = 0; index < weights.Length; index++)
                weights[index] += pair.Value * _morphWeightScale * axis[index];
        }
        return ImageTexture.CreateFromImage(Decode(weights));
    }

    internal ImageTexture ApplyAge(
        IReadOnlyList<float> axis,
        float coefficient)
    {
        if (axis.Count != _baseline.Count || axis.Any(value => !float.IsFinite(value)) ||
            !float.IsFinite(coefficient))
            throw new InvalidOperationException("Owned FaceGen age texture axis is invalid.");
        var weights = Coordinates(_baseline, _controls.Values.ToArray(), _values, _morphWeightScale)
            .Zip(axis, (value, weight) => value + coefficient * weight).ToArray();
        return ImageTexture.CreateFromImage(Decode(weights));
    }

    private Image Decode(IReadOnlyList<float> weights)
    {
        ReadOnlySpan<byte> signature = "FREGT003"u8;
        if (_egt.Length < HeaderBytes ||
            !_egt.AsSpan(0, signature.Length).SequenceEqual(signature))
            throw new InvalidOperationException("Owned FaceGen EGT signature is invalid.");
        var width = BinaryPrimitives.ReadInt32LittleEndian(
            _egt.AsSpan(signature.Length, sizeof(int)));
        var height = BinaryPrimitives.ReadInt32LittleEndian(
            _egt.AsSpan(signature.Length + sizeof(int), sizeof(int)));
        var modes = BinaryPrimitives.ReadInt32LittleEndian(
            _egt.AsSpan(signature.Length + sizeof(int) * 2, sizeof(int)));
        var pixels = checked(width * height);
        var expected = checked(HeaderBytes + modes * (ControlBytes + pixels * Channels));
        if (width <= 0 || height <= 0 || modes != weights.Count ||
            expected != _egt.Length || weights.Any(value => !float.IsFinite(value)))
            throw new InvalidOperationException("Owned FaceGen EGT dimensions are invalid.");
        var channels = new float[Channels][];
        for (var channel = 0; channel < Channels; channel++)
            channels[channel] = Enumerable.Repeat(Neutral, pixels).ToArray();
        var offset = HeaderBytes;
        foreach (var weight in weights)
        {
            var flags = _egt[offset + 3];
            offset += ControlBytes;
            var intensity = flags & IntensityMask;
            var slot = (flags >> SlotShift) & SlotMask;
            if (slot != FaceTextureSlot || (flags & MaxedFlag) != 0)
                throw new InvalidOperationException("Owned FaceGen EGT flags are unsupported.");
            var scale = IntensityScales[intensity];
            if ((flags & InvertFlag) != 0)
                scale = -scale;
            for (var channel = 0; channel < Channels; channel++)
            {
                if ((flags & EnabledFlag) != 0 && weight != 0.0f)
                    for (var pixel = 0; pixel < pixels; pixel++)
                        channels[channel][pixel] +=
                            weight * scale * unchecked((sbyte)_egt[offset + pixel]);
                offset += pixels;
            }
        }
        var rgba = new byte[pixels * (Channels + 1)];
        for (var sourceY = 0; sourceY < height; sourceY++)
        {
            var targetY = height - sourceY - 1;
            for (var x = 0; x < width; x++)
            {
                var sourcePixel = sourceY * width + x;
                var targetPixel = targetY * width + x;
                for (var channel = 0; channel < Channels; channel++)
                    rgba[targetPixel * (Channels + 1) + channel] = (byte)Math.Clamp(
                        MathF.Round(channels[channel][sourcePixel]), 0.0f, byte.MaxValue);
                rgba[targetPixel * (Channels + 1) + Channels] = byte.MaxValue;
            }
        }
        return Image.CreateFromData(width, height, false, Image.Format.Rgba8, rgba);
    }
}
