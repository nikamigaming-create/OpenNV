using System.Buffers.Binary;
using System.Security.Cryptography;

namespace OpenNV.Runtime.Presentation.CharacterCreation;

internal sealed record OwnedGamebryoFaceGenMorphControl(
    string SettingEntity,
    string AxisSha256,
    IReadOnlyList<float> Axis);

internal sealed record OwnedGamebryoFaceGenMorphState(
    IReadOnlyList<float> SymmetricGeometry,
    string SymmetricGeometrySha256,
    IReadOnlyDictionary<string, float> ControlValues);

internal static class OwnedGamebryoFaceGenMorphRuntime
{
    internal static OwnedGamebryoFaceGenMorphState Evaluate(
        IReadOnlyList<float> baseline,
        IReadOnlyList<OwnedGamebryoFaceGenMorphControl> controls,
        IReadOnlyDictionary<string, float> values,
        float minimum,
        float maximum,
        float morphWeightScale,
        float resetValue)
    {
        ValidateContract(baseline, controls, minimum, maximum, morphWeightScale);
        if (!float.IsFinite(resetValue) || resetValue < minimum || resetValue > maximum ||
            values.Count != controls.Count ||
            controls.Any(control => !values.ContainsKey(control.SettingEntity)))
            throw new InvalidOperationException(
                "Owned Gamebryo FaceGen morph coordinate inventory is invalid.");
        var state = State(
            baseline.ToArray(),
            controls.ToDictionary(
                control => control.SettingEntity,
                _ => resetValue,
                StringComparer.Ordinal));
        foreach (var control in controls)
            state = Advance(
                state.SymmetricGeometry,
                controls,
                state.ControlValues,
                control.SettingEntity,
                values[control.SettingEntity],
                minimum,
                maximum,
                morphWeightScale);
        return state;
    }

    internal static OwnedGamebryoFaceGenMorphState Advance(
        IReadOnlyList<float> currentGeometry,
        IReadOnlyList<OwnedGamebryoFaceGenMorphControl> controls,
        IReadOnlyDictionary<string, float> currentValues,
        string settingEntity,
        float value,
        float minimum,
        float maximum,
        float morphWeightScale)
    {
        ValidateContract(
            currentGeometry,
            controls,
            minimum,
            maximum,
            morphWeightScale);
        if (!float.IsFinite(value) || value < minimum || value > maximum ||
            currentValues.Count != controls.Count ||
            controls.Any(control => !currentValues.TryGetValue(
                control.SettingEntity,
                out var current) || !float.IsFinite(current)) ||
            !currentValues.TryGetValue(settingEntity, out var priorValue))
            throw new InvalidOperationException(
                "Owned Gamebryo FaceGen morph value is invalid.");
        var control = controls.SingleOrDefault(value =>
            value.SettingEntity.Equals(settingEntity, StringComparison.Ordinal));
        if (control is null)
            throw new InvalidOperationException(
                $"Owned Gamebryo FaceGen morph control is unsupported: {settingEntity}.");
        var delta = (value - priorValue) * morphWeightScale;
        var geometry = currentGeometry.Zip(
                control.Axis,
                (coordinate, axis) => coordinate + delta * axis)
            .ToArray();
        if (geometry.Any(coordinate => !float.IsFinite(coordinate)))
            throw new InvalidOperationException(
                "Owned Gamebryo FaceGen morph result is non-finite.");
        var updatedValues = currentValues.ToDictionary(
            pair => pair.Key,
            pair => pair.Value,
            StringComparer.Ordinal);
        updatedValues[settingEntity] = value;
        return State(geometry, updatedValues);
    }

    internal static void Publish(
        OwnedGamebryoFaceGenPreviewHost? preview,
        string settingEntity,
        float uiValue)
    {
        if (preview is null)
            throw new InvalidOperationException(
                "Owned Gamebryo FaceGen preview is unavailable for morph publication.");
        preview.Apply(settingEntity, uiValue);
    }

    private static void ValidateContract(
        IReadOnlyList<float> coordinates,
        IReadOnlyList<OwnedGamebryoFaceGenMorphControl> controls,
        float minimum,
        float maximum,
        float morphWeightScale)
    {
        if (coordinates.Count == 0 || coordinates.Any(value => !float.IsFinite(value)) ||
            controls.Count == 0 ||
            controls.Select(value => value.SettingEntity)
                .Distinct(StringComparer.Ordinal).Count() != controls.Count ||
            controls.Any(control =>
                string.IsNullOrWhiteSpace(control.SettingEntity) ||
                string.IsNullOrWhiteSpace(control.AxisSha256) ||
                control.Axis.Count != coordinates.Count ||
                control.Axis.Any(value => !float.IsFinite(value))) ||
            !float.IsFinite(minimum) || !float.IsFinite(maximum) ||
            minimum >= maximum || !float.IsFinite(morphWeightScale) ||
            morphWeightScale <= 0.0f)
            throw new InvalidOperationException(
                "Owned Gamebryo FaceGen morph contract is invalid.");
    }

    private static OwnedGamebryoFaceGenMorphState State(
        IReadOnlyList<float> geometry,
        IReadOnlyDictionary<string, float> values)
    {
        var payload = new byte[geometry.Count * sizeof(float)];
        for (var index = 0; index < geometry.Count; index++)
            BinaryPrimitives.WriteSingleLittleEndian(
                payload.AsSpan(index * sizeof(float), sizeof(float)),
                geometry[index]);
        return new OwnedGamebryoFaceGenMorphState(
            geometry,
            Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant(),
            values);
    }
}
