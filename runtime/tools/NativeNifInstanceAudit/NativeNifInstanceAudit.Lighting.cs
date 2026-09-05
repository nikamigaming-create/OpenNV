using Godot;
using OpenNV.Runtime.Content;

public partial class NativeNifInstanceAudit
{
    private static void ExercisePlacedLights()
    {
        var baseKey = new FalloutFormKey("Synthetic.esm", 0x801);
        var source = new FalloutBaseObjectDefinition(baseKey, "LIGH", "SyntheticLight", null,
            new FalloutLightDefinition(-1, 200, [80, 120, 160], 0, 0, 1, 90, 0, 0, 1.5f));
        var reference = new FalloutPlacedReference(new("Synthetic.esm", 0x802), "SyntheticReference",
            new("Synthetic.esm", 0x800), baseKey, 0, [256, 128, -64], [0, 0, 0], 1, null, null, null, false);
        var placement = new Transform3D(new Basis(Vector3.Up, 0.37f), new Vector3(4, 2, -1));
        foreach (var (adjustment, expected) in new (float? Adjustment, float Meters)[]
        {
            (null, 3.125f), (-40, 2.5f), (40, 3.75f),
        })
        {
            var light = RuntimeNativePlacedLightBuilder.Build(reference with { RadiusAdjustmentGameUnits = adjustment },
                source, placement, 1f / 64, 1, 0, false);
            try
            {
                if (light.OmniRange != expected || light.Transform != placement || light.LightEnergy != 1.5f ||
                    light.OmniAttenuation != 0)
                    throw new InvalidOperationException("Source light radius adjustment, units or native light binding changed.");
                // The shader's influence boundary must stay at the authored
                // world distance, independent of colour and reference rotation.
                var boundary = placement * (Vector3.Right * expected);
                if (MathF.Abs(boundary.DistanceTo(light.Position) - light.OmniRange) > 0.000001f)
                    throw new InvalidOperationException("Point light reach differs from its authored world boundary.");
            }
            finally { light.Free(); }
        }
        GD.Print("OPENNV_PLACED_LIGHT_BINDING_PASS signedAdjustment=true sourceReach=true units=true pixels=unverified");
    }
}
