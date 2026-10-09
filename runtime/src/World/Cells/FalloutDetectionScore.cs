using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutDetectionScoreInputs(int ReceiverPerception, int TargetSneak,
    bool VisualLineOfSight, bool VisualCone, float SourceDistance, int TargetLightAmount, int VisionPenalty,
    bool TargetStealthSuppressed, int EquipmentWeight, bool TargetSneaking, bool TargetMoving,
    bool TargetRunning, bool ReceiverSleepPredicate, bool ReceiverRawInCombat, int TargetActionSound,
    int ReceiverHeightClass, bool ReceiverExterior, bool ReceiverCombatWithOther, int AmbushMode,
    ushort ReceiverLevel, ushort TargetLevel, int ArmorPenalty);

internal sealed record FalloutDetectionScoreSettings(float BaseValue, int StartBonus, int LevelBonus,
    int StartBonusLevelPenalty, float AmbushTargetModifier, float AmbushNonTargetModifier,
    float PerceptionMinimum, float PerceptionMaximum, float AlertModifier, float SleepBonus, float CombatModifier,
    float ExteriorDistanceMultiplier, float MaximumDistance, float DistanceExponent, float SoundLosMultiplier,
    float BootWeightBase, float BootWeightMultiplier, float RunningMultiplier, float ActionMultiplier,
    float SoundsMultiplier, float LightMoveMultiplier, float LightRunMultiplier, float SneakLightModifier,
    float LightMultiplier, float LargeActorSizeMultiplier, float StealthBoyMultiplier, float SkillMultiplier)
{
    // Read winning settings afresh for each producer operation. A setting change
    // affects a new staged score, never an already committed query retroactively.
    internal static FalloutDetectionScoreSettings Read(FalloutPluginStack records,
        FalloutDetectionScalarKind kind = FalloutDetectionScalarKind.NewVegas)
    {
        float Float(string name) => FalloutGameSettingFloats.Read(records, name);
        int Integer(string name) => unchecked((int)FalloutGameSettingIntegers.Read(records, name));
        if (!Enum.IsDefined(kind)) throw new InvalidDataException("Detection setting family is invalid.");
        var result = new FalloutDetectionScoreSettings(Float("fSneakBaseValue"), kind == FalloutDetectionScalarKind.NewVegas ? Integer("iSneakStartBonus") : 0,
            kind == FalloutDetectionScalarKind.NewVegas ? Integer("iSneakLevelBonus") : 0,
            kind == FalloutDetectionScalarKind.NewVegas ? Integer("iSneakStartBonusLevelPenatly") : 0, Float("fSneakAmbushTargetMod"),
            Float("fSneakAmbushNonTargetMod"), Float("fSneakPerceptionSkillMin"), Float("fSneakPerceptionSkillMax"),
            Float("fSneakAlertMod"), Float("fSneakSleepBonus"), Float("fSneakCombatMod"), Float("fSneakExteriorDistanceMult"),
            Float("fSneakMaxDistance"), Float("fSneakDistanceAttenuationExponent"), Float("fSneakSoundLosMult"),
            Float("fSneakBootWeightBase"), Float("fSneakBootWeightMult"), Float("fSneakRunningMult"),
            Float("fSneakActionMult"), Float("fSneakSoundsMult"), Float("fSneakLightMoveMult"),
            Float("fSneakLightRunMult"), Float("fDetectionSneakLightMod"), Float("fSneakLightMult"),
            Float("fDetectionLargeActorSizeMult"), Float("fSneakStealthBoyMult"), Float("fSneakSkillMult"));
        result.Validate(); return result;
    }

    internal void Validate()
    {
        float[] floats = [BaseValue, AmbushTargetModifier, AmbushNonTargetModifier, PerceptionMinimum,
            PerceptionMaximum, AlertModifier, SleepBonus, CombatModifier, ExteriorDistanceMultiplier,
            MaximumDistance, DistanceExponent, SoundLosMultiplier, BootWeightBase, BootWeightMultiplier,
            RunningMultiplier, ActionMultiplier, SoundsMultiplier, LightMoveMultiplier, LightRunMultiplier,
            SneakLightModifier, LightMultiplier, LargeActorSizeMultiplier, StealthBoyMultiplier, SkillMultiplier];
        if (floats.Any(value => !float.IsFinite(value)))
            throw new InvalidDataException("Detection score settings are non-finite.");
        if (MaximumDistance <= 0 || ExteriorDistanceMultiplier <= 0)
            throw new NotSupportedException("Nonpositive detection attenuation range has no admitted scalar owner.");
    }
}

internal sealed record FalloutDetectionScoreResult(int Score, float ContinuousScore, float Sound,
    float Visual, float Skill, float DistanceAttenuation);

// This is the bounded scalar calculation, not an admission of scene lighting,
// actor values, LOS/cone tests, sensory event hearing, processes or update cadence.
// Every input must come from its own proven producer before a cache is staged.
internal static class FalloutDetectionScore
{
    internal static FalloutDetectionScoreResult Calculate(FalloutDetectionScoreInputs input,
        FalloutDetectionScoreSettings settings)
    {
        settings.Validate();
        if (!float.IsFinite(input.SourceDistance) || input.SourceDistance < 0 || input.ReceiverLevel == 0 || input.TargetLevel == 0)
            throw new InvalidDataException("Detection score has no finite source distance or selected actor level.");
        var startBonus = Math.Max(unchecked(settings.StartBonus - input.TargetLevel * settings.StartBonusLevelPenalty), 0);
        var sneak = unchecked(input.TargetSneak + (input.TargetLevel - input.ReceiverLevel) * settings.LevelBonus + startBonus - input.ArmorPenalty);
        var perception = Store(settings.PerceptionMinimum + ((double)settings.PerceptionMaximum - settings.PerceptionMinimum) *
            (input.ReceiverPerception / 10d));
        var ambush = input.AmbushMode switch
        {
            1 => settings.AmbushTargetModifier,
            2 => settings.AmbushNonTargetModifier,
            _ => 0,
        };
        perception = Store(perception * (1d + (input.ReceiverRawInCombat ? settings.AlertModifier : 0) +
            (input.ReceiverSleepPredicate ? settings.SleepBonus : 0) +
            (input.ReceiverCombatWithOther ? settings.CombatModifier : 0) + ambush));
        var maximum = Store(settings.MaximumDistance * (input.ReceiverExterior ? (double)settings.ExteriorDistanceMultiplier : 1));
        if (maximum <= 0) throw new NotSupportedException("Detection attenuation range underflowed its admitted domain.");
        var distanceFraction = Store(((double)maximum - input.SourceDistance) / maximum);
        var attenuation = Store(Math.Pow(Math.Max(distanceFraction, 0), settings.DistanceExponent));
        var soundAttenuation = input.VisualLineOfSight || input.AmbushMode == 1
            ? attenuation : Store(attenuation * (double)settings.SoundLosMultiplier);
        var movement = Store((input.TargetMoving ? 1d : 0) *
            (settings.BootWeightBase + input.EquipmentWeight * (double)settings.BootWeightMultiplier));
        if (input.TargetRunning) movement = Store(movement * (double)settings.RunningMultiplier);
        // Action's Float32 multiplier is converted to int before the signed
        // integer product. A float multiplication here is a different contract.
        var action = unchecked(Truncate(settings.ActionMultiplier) * input.TargetActionSound);
        var sound = Store(Math.Max(soundAttenuation * (double)settings.SoundsMultiplier * (movement + (double)action), 0));
        var visualMovement = Store(1d + (input.TargetMoving ? settings.LightMoveMultiplier : 0) +
            (input.TargetRunning ? settings.LightRunMultiplier : 0));
        var light = Store((input.TargetLightAmount + (double)settings.SneakLightModifier) * settings.LightMultiplier);
        // This is integer division, including truncation toward zero. The
        // current source pair caller passes a literal zero for VisionPenalty.
        var vision = (float)(unchecked(100 - input.VisionPenalty) / 100);
        var size = Store(input.ReceiverHeightClass <= 1 ? 1d :
            1d + (input.ReceiverHeightClass - 1d) * settings.LargeActorSizeMultiplier);
        var visible = input.TargetStealthSuppressed ? 0 : input.VisualCone ? 1 : 0;
        var visual = Store(Math.Max(visible * (double)attenuation * visualMovement * light * vision * size, 0));
        if (input.TargetStealthSuppressed) visual = Store(visual * (double)settings.StealthBoyMultiplier);
        var sneakContribution = (float)unchecked(sneak * (input.TargetSneaking ? 1 : 0));
        var perceptionContribution = Store(perception * (double)attenuation);
        var skill = Store((perceptionContribution - (double)sneakContribution) * settings.SkillMultiplier);
        var continuous = Store(settings.BaseValue + (double)sound + visual + skill);
        return new(continuous is > 0 and < 1 ? 1 : Truncate(continuous), continuous, sound, visual, skill, attenuation);
    }

    private static float Store(double value)
    {
        var stored = (float)value;
        return double.IsFinite(value) && float.IsFinite(stored) ? stored :
            throw new NotSupportedException("Detection score exceeds its admitted finite Float32 calculation domain.");
    }

    private static int Truncate(double value)
    {
        var integer = Math.Truncate(value);
        return double.IsFinite(value) && integer is >= int.MinValue and <= int.MaxValue ? checked((int)integer) :
            throw new NotSupportedException("Detection score requires an unowned indefinite integer conversion.");
    }
}
