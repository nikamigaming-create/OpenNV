using System.Buffers.Binary;
using System.Security.Cryptography;

namespace OpenNV.Runtime.Presentation.CharacterCreation;

internal sealed record OwnedGamebryoFaceGenAgeState(
    float RawValue,
    float Years,
    IReadOnlyList<float> SymmetricGeometry,
    string SymmetricGeometrySha256,
    IReadOnlyList<float> SymmetricTexture,
    string SymmetricTextureSha256,
    float GeometryAxisCoefficient,
    float TextureAxisCoefficient);

internal static class OwnedGamebryoFaceGenAgeRuntime
{
    internal static OwnedGamebryoFaceGenAgeState Evaluate(
        OpeningNativeFaceGenAgeControl control,
        IReadOnlyList<float> geometry,
        IReadOnlyList<float> texture,
        float rawValue)
    {
        Validate(control, geometry, texture, rawValue);
        var years = Math.Clamp(
            MathF.Round(rawValue * control.MappedMultiplier + control.MappedAddend),
            control.MappedMinimumYears,
            control.MappedMaximumYears);
        var geometryAge = ApparentAge(geometry, control.GeometryAxis, control.GeometryOffset);
        var textureAge = ApparentAge(texture, control.TextureAxis, control.TextureOffset);
        var textureYears = Math.Clamp(
            years + textureAge - geometryAge,
            control.MappedMinimumYears,
            control.MappedMaximumYears);
        var geometryCoefficient =
            (years - geometryAge) / SquaredNorm(control.GeometryAxis);
        var textureCoefficient =
            (textureYears - textureAge) / SquaredNorm(control.TextureAxis);
        var agedGeometry = Apply(geometry, control.GeometryAxis, geometryCoefficient);
        var agedTexture = Apply(texture, control.TextureAxis, textureCoefficient);
        return new OwnedGamebryoFaceGenAgeState(
            rawValue,
            years,
            agedGeometry,
            FloatSha256(agedGeometry),
            agedTexture,
            FloatSha256(agedTexture),
            geometryCoefficient,
            textureCoefficient);
    }

    internal static float InitialRawValue(
        OpeningNativeFaceGenAgeControl control,
        IReadOnlyList<float> geometry)
    {
        ValidateAxis(control.GeometryAxis, geometry.Count, "geometry");
        var years = ApparentAge(geometry, control.GeometryAxis, control.GeometryOffset);
        var raw = MathF.Round((years - control.MappedAddend) / control.MappedMultiplier);
        return Math.Clamp(raw, control.RawMinimum, control.RawMaximum);
    }

    private static void Validate(
        OpeningNativeFaceGenAgeControl control,
        IReadOnlyList<float> geometry,
        IReadOnlyList<float> texture,
        float rawValue)
    {
        ValidateAxis(control.GeometryAxis, geometry.Count, "geometry");
        ValidateAxis(control.TextureAxis, texture.Count, "texture");
        if (string.IsNullOrWhiteSpace(control.SettingEntity) ||
            string.IsNullOrWhiteSpace(control.SourceLabel) ||
            string.IsNullOrWhiteSpace(control.Semantics) ||
            !float.IsFinite(rawValue) || rawValue < control.RawMinimum ||
            rawValue > control.RawMaximum ||
            !float.IsFinite(control.RawStep) || control.RawStep <= 0.0f ||
            !float.IsFinite(control.MappedMultiplier) || control.MappedMultiplier <= 0.0f ||
            !float.IsFinite(control.MappedAddend) ||
            !float.IsFinite(control.MappedMinimumYears) ||
            !float.IsFinite(control.MappedMaximumYears) ||
            control.MappedMinimumYears >= control.MappedMaximumYears ||
            geometry.Any(value => !float.IsFinite(value)) ||
            texture.Any(value => !float.IsFinite(value)) ||
            !FloatSha256(control.GeometryAxis).Equals(
                control.GeometryAxisSha256, StringComparison.OrdinalIgnoreCase) ||
            !FloatSha256(control.TextureAxis).Equals(
                control.TextureAxisSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Owned FNV FaceGen age contract is invalid.");
    }

    private static void ValidateAxis(
        IReadOnlyList<float> axis,
        int count,
        string role)
    {
        if (axis.Count != count || axis.Count == 0 ||
            axis.Any(value => !float.IsFinite(value)) || SquaredNorm(axis) <= 0.0f)
            throw new InvalidOperationException(
                $"Owned FNV FaceGen age {role} axis is invalid.");
    }

    private static float ApparentAge(
        IReadOnlyList<float> coordinates,
        IReadOnlyList<float> axis,
        float offset) => coordinates.Zip(axis, (value, weight) => value * weight).Sum() + offset;

    private static float SquaredNorm(IReadOnlyList<float> axis) =>
        axis.Sum(value => value * value);

    private static float[] Apply(
        IReadOnlyList<float> coordinates,
        IReadOnlyList<float> axis,
        float coefficient) => coordinates.Zip(
            axis,
            (value, weight) => value + coefficient * weight).ToArray();

    private static string FloatSha256(IReadOnlyList<float> values)
    {
        var payload = new byte[values.Count * sizeof(float)];
        for (var index = 0; index < values.Count; index++)
            BinaryPrimitives.WriteSingleLittleEndian(
                payload.AsSpan(index * sizeof(float), sizeof(float)), values[index]);
        return Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
    }
}
