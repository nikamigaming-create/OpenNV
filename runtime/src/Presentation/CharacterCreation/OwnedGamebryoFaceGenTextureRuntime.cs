using System.Buffers.Binary;
using System.Security.Cryptography;
using Godot;
using OpenNV.Runtime.Formats.FaceGen;

namespace OpenNV.Runtime.Presentation.CharacterCreation;

internal sealed class OwnedGamebryoFaceGenTextureRuntime
{
    private const int Channels = 3;
    private static readonly float Neutral = (byte.MaxValue + 1.0f) / 2.0f;

    private readonly FalloutEgtFile _egt;
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
        : this(File.ReadAllBytes(egtPath), egtSha256, baseline, controls, minimum, maximum, morphWeightScale, resetValue)
    {
    }

    internal OwnedGamebryoFaceGenTextureRuntime(
        ReadOnlyMemory<byte> egtPayload,
        string egtSha256,
        IReadOnlyList<float> baseline,
        IReadOnlyList<OpeningNativeFaceGenTextureControl> controls,
        float minimum,
        float maximum,
        float morphWeightScale,
        float resetValue)
    {
        _egt = FalloutEgtFile.Read(egtPayload);
        var actual = Convert.ToHexString(SHA256.HashData(egtPayload.Span)).ToLowerInvariant();
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
        using var initial = Decode(_baseline);
    }

    internal IReadOnlyDictionary<string, float> Values => _values;

    internal static bool HasSupportedSignature(ReadOnlySpan<byte> payload) =>
        FalloutEgtFile.HasSupportedSignature(payload);

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
        using var image = Decode(weights);
        return ImageTexture.CreateFromImage(image);
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
        using var image = Decode(weights);
        return ImageTexture.CreateFromImage(image);
    }

    private Image Decode(IReadOnlyList<float> weights)
    {
        var delta = _egt.EvaluateDelta(weights, []);
        var width = delta.Width;
        var height = delta.Height;
        var pixels = checked(width * height);
        // Preserve this preview adapter's existing neutral encoding and UV
        // orientation separately from the source decoder. Native actor shaders
        // consume FalloutEgtFile deltas with their own observed map contract.
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
                        MathF.Round(Neutral + delta.Rgb[sourcePixel * Channels + channel]), 0.0f, byte.MaxValue);
                rgba[targetPixel * (Channels + 1) + Channels] = byte.MaxValue;
            }
        }
        return Image.CreateFromData(width, height, false, Image.Format.Rgba8, rgba);
    }
}
