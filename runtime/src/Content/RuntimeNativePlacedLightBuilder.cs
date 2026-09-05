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
        bool shadows,
        FalloutPluginStack? records = null,
        Func<FalloutFormKey, float[]>? regionEmittance = null)
    {
        if (!float.IsFinite(gameUnitsToMeters) || gameUnitsToMeters <= 0.0f ||
            !float.IsFinite(energyScale) || energyScale <= 0.0f ||
            !float.IsFinite(minimumEnergy) || minimumEnergy < 0.0f)
            throw new ArgumentOutOfRangeException(
                nameof(gameUnitsToMeters), "Native placed-light calibration is invalid.");
        var source = FalloutPlacedLightResolver.Resolve(reference, baseObject, records, regionEmittance);
        var color = new Color(
            source.ShaderColorRgb[0], source.ShaderColorRgb[1], source.ShaderColorRgb[2]);
        var light = new RuntimeNativePlacedLight
        {
            Name = $"LIGH_{reference.FormKey}",
            Transform = transform,
            LightColor = RetailLighting.GodotLightColor(color),
            LightEnergy = MathF.Max(minimumEnergy, source.Intensity * energyScale),
            // The source shader radius is the resolved light radius. Geometry
            // scale changes its local coordinates, not its world-space reach.
            OmniRange = source.RadiusGameUnits * gameUnitsToMeters,
            OmniAttenuation = RetailLighting.GodotOmniDecayForRetailRemap,
            ShadowEnabled = shadows,
        };
        light.SetMeta("opennv_ligh_reference", reference.FormKey.ToString());
        light.SetMeta("opennv_ligh_base", baseObject.FormKey.ToString());
        light.SetMeta("opennv_ligh_source_rgb", source.ColorRgb);
        light.SetMeta("opennv_ligh_shader_rgb", source.ShaderColorRgb);
        if (source.Emittance is { } emittance)
        {
            light.SetMeta("opennv_ligh_emittance", emittance.ToString());
            if (records!.GetEffective(emittance).Signature == "REGN")
                light.ConfigureRegionColor(() => FalloutPlacedLightResolver.ModulateColor(source.ColorRgb,
                    regionEmittance!(emittance)));
        }
        light.SetMeta("opennv_ligh_radius_game_units", source.RadiusGameUnits);
        light.SetMeta("opennv_ligh_base_radius_game_units", baseObject.Light!.RadiusGameUnits);
        light.SetMeta(
            "opennv_ligh_radius_adjustment_game_units",
            reference.RadiusAdjustmentGameUnits ?? 0.0f);
        return light;
    }
}
