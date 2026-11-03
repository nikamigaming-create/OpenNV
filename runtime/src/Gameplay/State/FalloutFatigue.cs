using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Actors;

namespace OpenNV.Runtime.Gameplay.State;

internal static class FalloutFatigue
{
    internal static FalloutActorValue Damage(FalloutPluginStack records, FalloutActorValue previous, float amount, float? current = null)
    {
        if (!float.IsFinite(amount) || amount < 0) throw new InvalidDataException("Fatigue damage is invalid.");
        var minimum = FalloutGameSettingFloats.Read(records, "fMinFatigue");
        if (minimum > 0) throw new NotSupportedException("Positive minimum fatigue requires its knockout contract.");
        var applied = Math.Min(amount, Math.Max(0, (current ?? previous.Current) - minimum));
        var changed = previous with { Damage = previous.Damage - applied };
        return changed.IsFinite ? changed : throw new InvalidDataException("Fatigue damage exceeds finite pool storage.");
    }

    internal static FalloutActorValue Advance(FalloutPluginStack records, FalloutActorValue previous, double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0) throw new InvalidDataException("Fatigue simulation delta is invalid.");
        if (seconds == 0 || previous.Damage >= 0) return previous;
        // GECK documents the stock one-unit-per-second regeneration. The native
        // multiplier consumes a separate cached fatigue-return-rate owner; it
        // must not be replaced by Endurance or a guessed rate for custom stacks.
        if (FalloutGameSettingFloats.Read(records, "fFatigueReturnMult") != 0)
            throw new NotSupportedException("Fatigue regeneration multiplier requires the cached return-rate owner.");
        var rate = FalloutGameSettingFloats.Read(records, "fFatigueReturnBase");
        if (rate < 0) throw new NotSupportedException("Negative fatigue regeneration requires its source degeneration owner.");
        var restored = Math.Min(0, previous.Damage + seconds * rate);
        // Native regeneration clears the remaining damage when a depleted
        // fatigue pool crosses zero; this is a value rule, not a KO timer.
        if (previous.Current < 0 && previous.Base + previous.Permanent + previous.Temporary + restored >= 0)
            restored = 0;
        return previous with { Damage = (float)restored };
    }
}
