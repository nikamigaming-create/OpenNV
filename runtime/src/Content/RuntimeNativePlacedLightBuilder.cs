using Godot;
using OpenNV.Runtime.Presentation.Rendering;

namespace OpenNV.Runtime.Content;

internal static class RuntimeNativePlacedLightBuilder
{
    internal static OmniLight3D Build(
        FalloutPlacedReference reference,
        FalloutBaseObjectDefinition baseObject,
        Transform3D transform,
        float gameUnitsToMeters,
        float energyScale,
        float minimumEnergy,
        bool shadows)
    {
        if (!float.IsFinite(gameUnitsToMeters) || gameUnitsToMeters <= 0.0f ||
            !float.IsFinite(energyScale) || energyScale <= 0.0f ||
            !float.IsFinite(minimumEnergy) || minimumEnergy < 0.0f)
            throw new ArgumentOutOfRangeException(
                nameof(gameUnitsToMeters), "Native placed-light calibration is invalid.");
        var source = FalloutPlacedLightResolver.Resolve(reference, baseObject);
        var color = new Color(
            source.ColorRgb[0] / (float)byte.MaxValue,
            source.ColorRgb[1] / (float)byte.MaxValue,
            source.ColorRgb[2] / (float)byte.MaxValue);
        var light = new OmniLight3D
        {
            Name = $"LIGH_{reference.FormKey}",
            Transform = transform,
            LightColor = RetailLighting.GodotLightColor(color),
            LightEnergy = MathF.Max(minimumEnergy, source.Intensity * energyScale),
            OmniRange = RetailLighting.PointShaderRadius(
                source.RadiusGameUnits * gameUnitsToMeters),
            OmniAttenuation = RetailLighting.GodotOmniDecayForRetailRemap,
            ShadowEnabled = shadows,
        };
        light.SetMeta("opennv_ligh_reference", reference.FormKey.ToString());
        light.SetMeta("opennv_ligh_base", baseObject.FormKey.ToString());
        light.SetMeta("opennv_ligh_radius_game_units", source.RadiusGameUnits);
        light.SetMeta("opennv_ligh_base_radius_game_units", baseObject.Light!.RadiusGameUnits);
        light.SetMeta(
            "opennv_ligh_radius_adjustment_game_units",
            reference.RadiusAdjustmentGameUnits ?? 0.0f);
        return light;
    }
}
