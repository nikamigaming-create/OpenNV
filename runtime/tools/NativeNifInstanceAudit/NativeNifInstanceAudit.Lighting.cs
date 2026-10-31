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
        ExerciseLightFlicker(source, reference);
        GD.Print("OPENNV_PLACED_LIGHT_BINDING_PASS signedAdjustment=true sourceReach=true units=true pixels=unverified");
    }

    private static void ExerciseLightFlicker(FalloutBaseObjectDefinition source, FalloutPlacedReference reference)
    {
        foreach (var flags in new uint[] { 8, 0x40, 0x48 })
        {
            var resolved = FalloutPlacedLightResolver.Resolve(reference, source with { Light = source.Light! with { Flags = flags } });
            if (resolved.AnimationFlags != flags) throw new InvalidOperationException("Source point-light flags were lost.");
        }
        var draws = 0;
        uint Draw() { draws++; return 0; }
        var ordinary = new FalloutLightFlicker(8, 8, Draw);
        if (ordinary.PhaseX != 0 || ordinary.PhaseY != 1 || ordinary.PhaseZ != 0)
            throw new InvalidOperationException("New point-light phases differ from the source constructor.");
        var initial = ordinary.Advance(0);
        if (draws != 3 || initial.Intensity < .6f || initial.Intensity > 1 || initial.X == 0)
            throw new InvalidOperationException("Flicker lost zero-delta draws, positional offset or source dimmer bounds.");
        if (MathF.Abs(initial.X - .14566986f) > .000002f || MathF.Abs(initial.Y + .31790447f) > .000002f ||
            MathF.Abs(initial.Z + .40090057f) > .000002f || MathF.Abs(initial.Intensity - .8942034f) > .000002f)
            throw new InvalidOperationException("Initial source flicker vector/dimmer changed.");
        var fast = new FalloutLightFlicker(8, 8, static () => 0);
        var slow = new FalloutLightFlicker(0x40, 8, static () => 0);
        if (fast.Advance(.125) != slow.Advance(.25))
            throw new InvalidOperationException("Slow source flicker no longer halves its frame delta.");
        var noMovement = new FalloutLightFlicker(8, 0, static () => uint.MaxValue);
        var sample = noMovement.Advance(.125);
        if (sample.X != 0 || sample.Y != 0 || sample.Z != 0 || sample.Intensity < .6f || sample.Intensity > 1)
            throw new InvalidOperationException("Zero movement changed source dimmer modulation.");
        fast.Advance(20);
        if (fast.PhaseX <= MathF.Tau)
            throw new InvalidOperationException("Large source phase was reduced by modulo instead of one strict wrap.");
        GD.Print("OPENNV_LIGHT_FLICKER_CONTRACT_PASS sourceFlags=true phases=true slow=true draws=true geometry=unmodified pixels=unverified");
    }
}
