using Godot;
using OpenNV.Runtime.Content;

public partial class NativeNifInstanceAudit
{
    private static void ExerciseLightPulse(FalloutBaseObjectDefinition source, FalloutPlacedReference reference)
    {
        foreach (var flags in new uint[] { 0x80, 0x100, 0x180, 0x88, 0x148, 0x1c8 })
        {
            var resolved = FalloutPlacedLightResolver.Resolve(reference, source with { Light = source.Light! with { Flags = flags } });
            if (resolved.AnimationFlags != flags) throw new InvalidOperationException("Source pulse/flicker precedence flags were lost.");
        }
        var pulse = new FalloutLightPulse(0x80, 1);
        if (pulse.Intensity != 1 || pulse.Target != 1 || pulse.Advance(0) != 1 || pulse.Target != .25f)
            throw new InvalidOperationException("Pulse lost its absolute initial target or zero-delta target transition.");
        if (pulse.Advance(1) != .9f || pulse.Target != .25f)
            throw new InvalidOperationException("Pulse became a sine/period clock or lost its absolute downward step.");
        var fast = new FalloutLightPulse(0x80, 1); var slow = new FalloutLightPulse(0x100, 1);
        if (fast.Advance(.125) != slow.Advance(.25)) throw new InvalidOperationException("PulseSlow lost its source half-rate.");
        var strict = new FalloutLightPulse(0x80, 1);
        if (strict.Advance(10) != -1.4901161e-8f || strict.Target != 1)
            throw new InvalidOperationException("Pulse clamped/caught up a large frame step or switched outside proximity.");
        var scaled = new FalloutLightPulse(0x80, 2);
        if (scaled.Target != 1 || scaled.Advance(0) != 2 || scaled.Target != 1)
            throw new InvalidOperationException("Pulse replaced the absolute initial target with a source-relative phase.");
        var low = new FalloutLightPulse(0x80, .5f); low.Advance(5);
        if (low.Target != .125f) throw new InvalidOperationException("Pulse low target did not use actual source intensity.");
        GD.Print("OPENNV_LIGHT_PULSE_CONTRACT_PASS sourceFlags=true absoluteDimmer=true absoluteInitialTarget=true zeroDelta=true slow=true singleStep=true rng=none pixels=unverified");
    }
}
