using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

// Source hour effects operate on actual shared vitals. Actor bed pose, the
// independent player sleep flag and selected Hardcore policy stay separate.
internal sealed class FalloutPlayerRestEffects(FalloutSleepWaitSource source,
    FalloutPluginStack records, FalloutPlayerVitals vitals, Func<bool> hardcore,
    Func<bool> sourceHealthUpdatesEnabled,
    Func<float> actualHealingRate, Func<bool> actualActorSleepingPose, Func<float> sourcePerkHealthRate,
    Action<FalloutRestHour> advanceHardcoreNeeds, Action<FalloutRestHour> restoreHardcoreSleepDebt,
    Action<FalloutRestHour> advanceOtherWorldEffects)
{
    internal void ApplyHourPrelude(FalloutRestHour hour)
    {
        var isHardcore = hardcore();
        if (isHardcore && !source.HasHardcoreConsumer)
            throw new NotSupportedException("Selected source has no Hardcore rest consumer.");
        if (isHardcore) advanceHardcoreNeeds(hour);
    }
    internal void ApplyHour(FalloutRestHour hour)
    {
        if (!float.IsFinite(hour.SimulationSeconds) || hour.SimulationSeconds <= 0)
            throw new InvalidDataException("Rest effect requires its actual finite hour input.");
        var isHardcore = hardcore();
        if (isHardcore && !source.HasHardcoreConsumer)
            throw new NotSupportedException("Selected source has no Hardcore rest consumer.");
        var before = vitals.State;
        if (before.ExactHitPoints <= 0) throw new InvalidOperationException("Rest cannot apply effects to a dead player.");
        if (sourceHealthUpdatesEnabled()) ApplyHealing(hour, before);
        if (isHardcore && hour.Sleeping) restoreHardcoreSleepDebt(hour);
        // Original ongoing world/effect clocks are independent of the calendar.
        // Absent owners refuse; Aid.Advance(seconds) is not a universal stand-in.
        advanceOtherWorldEffects(hour);
    }
    private void ApplyHealing(FalloutRestHour hour, GameplayVitals before)
    {
        var healingRate = actualHealingRate();
        var perkRate = sourcePerkHealthRate();
        if (!float.IsFinite(healingRate) || !float.IsFinite(perkRate))
            throw new InvalidDataException("Source health rate/entry12 produced a non-finite value.");
        var rate = healingRate / 3600.0;
        if (actualActorSleepingPose()) rate = rate * records.NumericSettings.Float("fHealingRateSleepingMult") +
            records.NumericSettings.Float("fHealingRateSleepingBase") / 3600.0;
        // The native rate function returns Float32 before multiplying seconds.
        var amount = (float)rate * hour.SimulationSeconds + perkRate * hour.SimulationSeconds;
        if (!float.IsFinite(amount)) throw new InvalidDataException("Source rest healing overflowed.");
        if (amount > 0)
        {
            var remaining = Math.Min(before.MaximumHitPoints, before.ExactHitPoints + amount);
            var displayed = checked((int)MathF.Ceiling(remaining));
            vitals.Publish(before with { HitPoints = displayed, HitPointFraction = displayed - remaining });
        }
    }
    internal void CompleteSleep(FalloutRestRequest request)
    {
        request.Validate();
        var isHardcore = hardcore();
        if (isHardcore && !source.HasHardcoreConsumer)
            throw new NotSupportedException("Selected source has no Hardcore completion consumer.");
        if (isHardcore) return;
        var state = vitals.State;
        if (state.ExactHitPoints == 0) throw new NotSupportedException("Completed sleep cannot substitute for resurrection.");
        vitals.Publish(state with { HitPoints = state.MaximumHitPoints, HitPointFraction = 0,
            ActionPoints = state.MaximumActionPoints, LimbDamage = null });
        // Rested quest/spell callbacks are owned by actual source world/script
        // update. This consumer grants no named PERK, spell or quest stage.
    }
}
