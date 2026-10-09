namespace OpenNV.Runtime.World.Cells;

// These are the evaluated source hearing inputs. An event position/level is
// retained by the independent event owner; this calculation neither discovers
// a receiver nor consumes an event or publishes a directional cache entry.
internal sealed record FalloutDetectionSoundInputs(int AdjustedReceiverSkill, bool VisibleBeforeCone,
    float SourceDistance, int VisionPenalty, int ReceiverLightAmount,
    bool ReceiverSleepPredicate, bool ReceiverAlertPredicate, bool ReceiverRawInCombat,
    int EventLevel, bool EventInCone, bool ReceiverExterior);

internal sealed record FalloutDetectionSoundResult(int Score, float ContinuousScore,
    float Sound, float Visual, float EvaluatedPerception, float DistanceAttenuation);

internal static class FalloutDetectionSoundScore
{
    internal static FalloutDetectionSoundResult Calculate(FalloutDetectionSoundInputs input,
        FalloutDetectionScoreSettings settings)
    {
        settings.Validate();
        if (!float.IsFinite(input.SourceDistance) || input.SourceDistance < 0)
            throw new InvalidDataException("Detection hearing has no finite source event distance.");
        // The source evaluates this receiver component, but its final hearing
        // sum does not add it. Do not turn that dead arithmetic into a new skill
        // penalty/bonus, or use its omission to waive the actual input owner.
        var perception = Store(settings.PerceptionMinimum +
            ((double)settings.PerceptionMaximum - settings.PerceptionMinimum) * (input.AdjustedReceiverSkill / 10d));
        perception = Store(perception * (1d + (input.ReceiverAlertPredicate ? settings.AlertModifier : 0) +
            (input.ReceiverSleepPredicate ? settings.SleepBonus : 0) +
            (input.ReceiverRawInCombat ? settings.CombatModifier : 0)));
        var maximum = Store(settings.MaximumDistance *
            (input.ReceiverExterior ? (double)settings.ExteriorDistanceMultiplier : 1));
        if (maximum <= 0)
            throw new NotSupportedException("Detection hearing attenuation underflowed its admitted domain.");
        var fraction = Store(((double)maximum - input.SourceDistance) / maximum);
        var attenuation = Store(Math.Pow(Math.Max(fraction, 0), settings.DistanceExponent));
        var soundAttenuation = input.VisibleBeforeCone ? attenuation :
            Store(attenuation * (double)settings.SoundLosMultiplier);
        var action = unchecked(Truncate(settings.ActionMultiplier) * input.EventLevel);
        var sound = Store(Math.Max(soundAttenuation * (double)settings.SoundsMultiplier * action, 0));
        var light = Store((input.ReceiverLightAmount + (double)settings.SneakLightModifier) * settings.LightMultiplier);
        var vision = (float)(unchecked(100 - input.VisionPenalty) / 100);
        var visual = Store(Math.Max((input.EventInCone ? 1d : 0) * attenuation * light * vision, 0));
        var evaluatedPerception = Store(perception * (double)attenuation);
        var continuous = Store(settings.BaseValue + (double)sound + visual);
        return new(continuous is > 0 and < 1 ? 1 : Truncate(continuous), continuous,
            sound, visual, evaluatedPerception, attenuation);
    }

    private static float Store(double value)
    {
        var stored = (float)value;
        return double.IsFinite(value) && float.IsFinite(stored) ? stored :
            throw new NotSupportedException("Detection hearing exceeds its admitted finite Float32 domain.");
    }

    private static int Truncate(double value)
    {
        var integer = Math.Truncate(value);
        return double.IsFinite(value) && integer is >= int.MinValue and <= int.MaxValue ? checked((int)integer) :
            throw new NotSupportedException("Detection hearing requires an unowned indefinite integer conversion.");
    }
}
