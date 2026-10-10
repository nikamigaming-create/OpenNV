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
        var resolved = FalloutPlacedLightResolver.Resolve(reference, baseObject, records, regionEmittance);
        return BuildResolved(resolved, baseObject, transform, gameUnitsToMeters, energyScale, minimumEnergy, shadows, records, regionEmittance);
    }

    internal static RuntimeNativePlacedLight BuildResolved(FalloutPlacedLight source, FalloutBaseObjectDefinition baseObject,
        Transform3D transform, float gameUnitsToMeters, float energyScale, float minimumEnergy, bool shadows,
        FalloutPluginStack? records = null, Func<FalloutFormKey, float[]>? regionEmittance = null)
    {
        if (source.Base != baseObject.FormKey || baseObject.Light is null)
            throw new InvalidDataException("Native resolved light differs from its actual source base.");
        if (!float.IsFinite(gameUnitsToMeters) || gameUnitsToMeters <= 0.0f ||
            !float.IsFinite(energyScale) || energyScale <= 0.0f ||
            !float.IsFinite(minimumEnergy) || minimumEnergy < 0.0f)
            throw new ArgumentOutOfRangeException(
                nameof(gameUnitsToMeters), "Native placed-light calibration is invalid.");
        if (source.ShaderColorRgb.Length != 3 || source.ShaderColorRgb.Any(value => !float.IsFinite(value) || value < 0) ||
            source.ColorRgb.Length != 3 || !float.IsFinite(source.RadiusGameUnits) || source.RadiusGameUnits <= 0 ||
            !float.IsFinite(source.Intensity) || source.Intensity <= 0 ||
            !float.IsFinite(source.RadiusGameUnits * gameUnitsToMeters) || !float.IsFinite(source.Intensity * energyScale))
            throw new InvalidDataException("Native resolved light has invalid source or calibrated parameters.");
        Func<float[]>? regionColor = null;
        if (source.Emittance is { } sourceEmittance)
        {
            var record = (records ?? throw new InvalidDataException("Native resolved light has no XEMI records.")).GetEffective(sourceEmittance);
            if (record.Signature == "REGN")
            {
                var sample = regionEmittance ?? throw new InvalidDataException("Native resolved light has no current region-emittance producer.");
                regionColor = () => FalloutPlacedLightResolver.ModulateColor(source.ColorRgb, sample(sourceEmittance));
            }
            else if (record.Signature != "LIGH") throw new NotSupportedException("Native resolved light has a foreign XEMI owner.");
        }
        var color = new Color(
            source.ShaderColorRgb[0], source.ShaderColorRgb[1], source.ShaderColorRgb[2]);
        var light = new RuntimeNativePlacedLight();
        try
        {
            light.Name = $"LIGH_{source.Reference}";
            light.Transform = transform;
            light.LightColor = RetailLighting.GodotLightColor(color);
            light.LightEnergy = MathF.Max(minimumEnergy, source.Intensity * energyScale);
            // Geometry scale changes local coordinates, not source reach.
            light.OmniRange = source.RadiusGameUnits * gameUnitsToMeters;
            light.OmniAttenuation = RetailLighting.GodotOmniDecayForRetailRemap;
            light.ShadowEnabled = shadows;
            light.SetMeta("opennv_ligh_reference", source.Reference.ToString());
            light.SetMeta("opennv_ligh_base", baseObject.FormKey.ToString());
            light.SetMeta("opennv_ligh_source_rgb", source.ColorRgb);
            light.SetMeta("opennv_ligh_shader_rgb", source.ShaderColorRgb);
            if (source.Emittance is { } emittance)
            {
                light.SetMeta("opennv_ligh_emittance", emittance.ToString());
                if (regionColor is not null) light.ConfigureRegionColor(regionColor);
            }
            light.SetMeta("opennv_ligh_radius_game_units", source.RadiusGameUnits);
            light.SetMeta("opennv_ligh_base_radius_game_units", baseObject.Light!.RadiusGameUnits);
            light.SetMeta(
                "opennv_ligh_radius_adjustment_game_units",
                source.RadiusAdjustmentGameUnits);
            return light;
        }
        catch (Exception primary)
        {
            try { light.Free(); }
            catch (Exception retirement) { throw new AggregateException("Native light construction and detached retirement failed.", primary, retirement); }
            throw;
        }
    }
}
