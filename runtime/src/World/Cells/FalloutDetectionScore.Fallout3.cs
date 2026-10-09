using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal static class FalloutDetectionScalar
{
    internal static FalloutDetectionScoreResult Calculate(FalloutDetectionScalarKind kind,
        FalloutDetectionScoreInputs input, FalloutDetectionScoreSettings settings) => kind switch
        {
            FalloutDetectionScalarKind.NewVegas => FalloutDetectionScore.Calculate(input, settings),
            FalloutDetectionScalarKind.Fallout3 => Fallout3(input, settings),
            _ => throw new InvalidDataException("Detection scalar source family is invalid."),
        };

    private static FalloutDetectionScoreResult Fallout3(FalloutDetectionScoreInputs input,
        FalloutDetectionScoreSettings settings)
    {
        settings.Validate();
        if (!float.IsFinite(input.SourceDistance) || input.SourceDistance < 0)
            throw new InvalidDataException("Detection scalar has no finite source distance.");
        // Every SSE operation stores its result as Float32. FNV's additional
        // level/start/armor skill terms are absent from this original consumer.
        var perception = F(settings.PerceptionMaximum - settings.PerceptionMinimum);
        perception = F(perception * F(input.ReceiverPerception / 10f));
        perception = F(perception + settings.PerceptionMinimum);
        var factor = F(1 + F((input.ReceiverRawInCombat ? 1 : 0) * settings.AlertModifier));
        factor = F(factor + F((input.ReceiverSleepPredicate ? 1 : 0) * settings.SleepBonus));
        factor = F(factor + F((input.ReceiverCombatWithOther ? 1 : 0) * settings.CombatModifier));
        factor = F(factor + (input.AmbushMode == 1 ? settings.AmbushTargetModifier :
            input.AmbushMode == 2 ? settings.AmbushNonTargetModifier : 0));
        perception = F(perception * factor);
        var maximum = input.ReceiverExterior ? F(settings.MaximumDistance * settings.ExteriorDistanceMultiplier) : settings.MaximumDistance;
        if (maximum <= 0) throw new NotSupportedException("Detection range has an unowned nonpositive division.");
        var fraction = Math.Max(F(F(maximum - input.SourceDistance) / maximum), 0);
        var attenuation = F((float)Math.Pow(fraction, settings.DistanceExponent));
        var soundAttenuation = input.VisualLineOfSight || input.AmbushMode == 1 ? attenuation : F(attenuation * settings.SoundLosMultiplier);
        var movement = F(F(F(input.EquipmentWeight * settings.BootWeightMultiplier) + settings.BootWeightBase) * (input.TargetMoving ? 1 : 0));
        movement = F(movement * (input.TargetRunning ? settings.RunningMultiplier : 1));
        var action = unchecked(I(settings.ActionMultiplier) * input.TargetActionSound);
        var sound = Math.Max(F(F((float)action + movement) * F(soundAttenuation * settings.SoundsMultiplier)), 0);
        var visualMovement = F(F(1 + F((input.TargetMoving ? 1 : 0) * settings.LightMoveMultiplier)) +
            F((input.TargetRunning ? 1 : 0) * settings.LightRunMultiplier));
        var light = F(F(input.TargetLightAmount + settings.SneakLightModifier) * settings.LightMultiplier);
        var size = input.ReceiverHeightClass <= 1 ? 1 : F(F(F(input.ReceiverHeightClass - 1f) * settings.LargeActorSizeMultiplier) + 1);
        var visible = input.TargetStealthSuppressed ? 0 : input.VisualCone ? 1 : 0;
        var visual = F(visible * attenuation);
        visual = F(visual * visualMovement); visual = F(visual * light);
        visual = F(visual * (unchecked(100 - input.VisionPenalty) / 100));
        visual = Math.Max(F(visual * size), 0);
        if (input.TargetStealthSuppressed) visual = F(visual * settings.StealthBoyMultiplier);
        var skill = F(F(F(attenuation * perception) - unchecked((input.TargetSneaking ? 1 : 0) * input.TargetSneak)) * settings.SkillMultiplier);
        var continuous = F(F(F(settings.BaseValue + sound) + visual) + skill);
        return new(continuous is > 0 and < 1 ? 1 : I(continuous), continuous, sound, visual, skill, attenuation);
    }

    private static float F(float value) => float.IsFinite(value) ? value : throw new InvalidDataException("Detection arithmetic exceeds Float32 storage.");
    private static int I(float value) => float.IsFinite(value) && Math.Truncate((double)value) is >= int.MinValue and <= int.MaxValue ?
        checked((int)value) : throw new NotSupportedException("Detection scalar reached an unowned indefinite integer conversion.");
}
