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
        Func<FalloutFormKey, float[]>? regionEmittance = null,
        Node3D? sourceModel = null)
    {
        if (!float.IsFinite(gameUnitsToMeters) || gameUnitsToMeters <= 0.0f ||
            !float.IsFinite(energyScale) || energyScale <= 0.0f ||
            !float.IsFinite(minimumEnergy) || minimumEnergy < 0.0f)
            throw new ArgumentOutOfRangeException(
                nameof(gameUnitsToMeters), "Native placed-light calibration is invalid.");
        if (baseObject.ModelPath is not null)
        {
            if (sourceModel is null ||
                sourceModel.GetMeta("opennv_reference_form_key", "").AsString() != reference.FormKey.ToString() ||
                sourceModel.GetMeta("opennv_source_form", "").AsString() != baseObject.FormKey.ToString() ||
                !FalloutBsaArchive.CanonicalPath(sourceModel.GetMeta("opennv_source_model", "").AsString())
                    .Equals(FalloutBsaArchive.CanonicalPath(baseObject.ModelPath), StringComparison.OrdinalIgnoreCase))
                throw new NotSupportedException($"Native LIGH {baseObject.FormKey} requires its actual source model owner.");
            _ = ModelAttachment(sourceModel);
        }
        else if (sourceModel is not null)
            throw new InvalidDataException("Model-free LIGH received a model attachment owner.");
        var source = FalloutPlacedLightResolver.Resolve(reference, baseObject, records, regionEmittance);
        var color = new Color(
            source.ShaderColorRgb[0], source.ShaderColorRgb[1], source.ShaderColorRgb[2]);
        var flickerMovement = 0f;
        if ((source.AnimationFlags & FalloutLightFlicker.Flags) != 0)
        {
            var content = RuntimeLiveContentSource.Current ?? throw new InvalidOperationException("Animated light has no mounted owned source.");
            flickerMovement = FalloutInstallationSettings.Read(content).Number("bLightAttenuation", "fFlickerMovement");
            if (!float.IsFinite(flickerMovement)) throw new InvalidDataException("Source flicker movement is nonfinite.");
        }
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
        if (source.AnimationFlags != 0)
            light.ConfigureAnimation(source, gameUnitsToMeters, flickerMovement, energyScale, minimumEnergy);
        light.SetMeta("opennv_ligh_source_flags", baseObject.Light!.Flags);
        light.SetMeta("opennv_ligh_dynamic", (baseObject.Light!.Flags & FalloutPlacedLightResolver.DynamicFlag) != 0);
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

    // Native name lookup is ordinal and returns the first source-tree match.
    // A missing attachment uses the model root; it does not invent an offset.
    internal static Node3D ModelAttachment(Node3D sourceModel)
    {
        var roots = sourceModel.GetChildren().OfType<Node3D>()
            .Where(node => node.GetMeta("opennv_nif_source_node", false).AsBool()).ToArray();
        if (roots.Length != 1)
            throw new NotSupportedException("LIGH model requires one actual source NiNode root.");
        var sourceRoot = roots[0];
        var nodes = new[] { sourceRoot }.Concat(sourceRoot.FindChildren("*", "", true, false).OfType<Node3D>()).ToArray();
        var match = nodes.FirstOrDefault(node =>
            node.GetMeta("opennv_nif_source_name", "").AsString() == "AttachLight");
        // Native lookup returns an AVObject, then GetAsNiNode; a matching
        // geometry does not cause a second name search or accept a light child.
        var attachment = match is not null && match.GetMeta("opennv_nif_source_node", false).AsBool()
            ? match : sourceRoot;
        var subtree = new[] { attachment }.Concat(attachment.FindChildren("*", "", true, false).OfType<Node3D>());
        if (subtree.Any(node => node.HasMeta("opennv_nif_unreferenced_point_light")))
            throw new NotSupportedException("LIGH model requires its embedded NiPointLight reuse/controller owner.");
        return attachment;
    }
}
