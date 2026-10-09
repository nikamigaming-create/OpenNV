namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutExperienceNotifications
{
    internal FalloutExperienceNotificationSnapshot Capture(FalloutExperienceClockReading clock)
    {
        RequireHealthy();
        if (_observedExperience != _experience()) throw new InvalidDataException("XP capture lost an actual event publication.");
        if (SaveBlocker is { } blocker) throw new NotSupportedException(blocker);
        var milliseconds = clock.TilesMilliseconds;
        if (milliseconds < 0 || clock.LevelMilliseconds < 0 || _lastClock is { } last && milliseconds < last ||
            _lastLevelClock is { } lastLevel && clock.LevelMilliseconds < lastLevel)
            throw new InvalidDataException("XP capture clock moved backwards.");
        // Capture actual elapsed UI time without firing a source transition,
        // playing a voice, scheduling a draw or making a completion receipt.
        var current = _display;
        if (current is { PhasePresented: true } && _lastClock is { } previous)
            current = current with
            {
                ElapsedMilliseconds = current.ElapsedMilliseconds + milliseconds - previous,
                LevelElapsedMilliseconds = current.LevelElapsedMilliseconds + clock.LevelMilliseconds -
                    (_lastLevelClock ?? throw new InvalidOperationException("XP level clock lost its actual phase epoch."))
            };
        var result = new FalloutExperienceNotificationSnapshot(Schema, _contract, _observedExperience,
            _lastOrdinal, _retiredThrough, _pending.ToArray(), current, _sounds.ToArray(), _random.State,
            clock.LevelMilliseconds, _levelTimestamp, _idlePresented,
            _lastSequence, _retiredSequence, _frameGate.Capture(), _failure);
        ValidateSnapshot(result); return result;
    }

    internal static void ValidateSnapshot(FalloutExperienceNotificationSnapshot state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Schema != Schema || state.Contract is null || state.Contract.Length != 64 || state.ObservedExperience < 0 || state.RetiredThrough > state.LastOrdinal ||
            state.Pending is null || state.Sounds is null || state.FrameGate is null || state.Failure is not null ||
            state.RetiredSequence > state.LastSequence ||
            state.Display is null && state.RetiredSequence != state.LastSequence ||
            state.LevelClockMilliseconds < 0 || state.LevelClockMilliseconds > uint.MaxValue ||
            state.LevelTimestampMilliseconds is { } stamp && (!double.IsFinite(stamp) || stamp < 0 || stamp > uint.MaxValue) ||
            state.IdlePresented && (state.Display is not null || state.Pending.Count != 0 || state.RetiredThrough != state.LastOrdinal))
            throw new InvalidDataException("XP notification snapshot is incomplete or failed.");
        FalloutExperienceFrameGate.Validate(state.FrameGate);
        if (state.FrameGate.Ready && (state.Display is not null || state.Pending.Count != 0 ||
                state.FrameGate.CompletedSequence != state.RetiredSequence) ||
            (state.FrameGate.Flags == FalloutExperienceSourceFlags.LevelText) != (state.Display?.LevelTextPublished == true))
            throw new InvalidDataException("Experience frame flag lost its actual source display completion.");
        var previous = state.Display?.LastOrdinal ?? state.RetiredThrough;
        int? precedingExperience = state.Display?.TargetExperience;
        foreach (var change in state.Pending)
        {
            if (change is null || !change.Queued || change.Ordinal != checked(previous + 1) || change.Ordinal > state.LastOrdinal ||
                change.Level < 1 || change.Previous < 0 || change.Published <= change.Previous ||
                precedingExperience is { } preceding && change.Previous != preceding)
                throw new InvalidDataException("XP notification pending publication is invalid or unordered.");
            previous = change.Ordinal; precedingExperience = change.Published;
        }
        if (state.Display is { } display)
        {
            if (!Enum.IsDefined(display.Phase) || display.Sequence != state.LastSequence || display.Sequence != checked(state.RetiredSequence + 1) ||
                display.LastOrdinal > state.LastOrdinal || display.Level < 1 ||
                display.TargetExperience < 0 || display.SweepLevel < display.Level || display.SweepStartExperience < 0 ||
                display.SweepEndExperience < display.SweepStartExperience ||
                !double.IsFinite(display.ElapsedMilliseconds) || display.ElapsedMilliseconds < 0 ||
                !double.IsFinite(display.LevelElapsedMilliseconds) || display.LevelElapsedMilliseconds < 0)
                throw new InvalidDataException("XP notification retained display is invalid.");
            if (display.MeterPublished && (display.FirstOrdinal != checked(state.RetiredThrough + 1) ||
                    display.FirstOrdinal > display.LastOrdinal || display.Amount <= 0) ||
                !display.MeterPublished && (display.FirstOrdinal != state.RetiredThrough || display.LastOrdinal != state.RetiredThrough ||
                    display.Amount != 0 || !display.LevelTextPublished ||
                    display.Phase is FalloutExperienceNotificationPhase.MeterFadeIn or FalloutExperienceNotificationPhase.MeterSweep or FalloutExperienceNotificationPhase.MeterFadeOut))
                throw new InvalidDataException("Experience display fabricated an XP award or lost its source meter/level-only origin.");
            if ((display.Phase is FalloutExperienceNotificationPhase.LevelFadeIn or FalloutExperienceNotificationPhase.LevelHold or FalloutExperienceNotificationPhase.LevelFadeOut) &&
                (!display.LevelTextPublished || state.LevelTimestampMilliseconds is null) ||
                display.LevelTextPublished && (state.LevelTimestampMilliseconds is null ||
                    display.Phase is FalloutExperienceNotificationPhase.MeterFadeIn or FalloutExperienceNotificationPhase.MeterSweep or FalloutExperienceNotificationPhase.MeterFadeOut))
                throw new InvalidDataException("XP level text lost its actual source timestamp publication.");
        }
        else if (state.Pending.Count == 0 && state.RetiredThrough != state.LastOrdinal)
            throw new InvalidDataException("XP notification snapshot discarded an unfinished publication.");
        if ((state.Pending.Count != 0 && (previous != state.LastOrdinal || precedingExperience != state.ObservedExperience)) ||
            state.Pending.Count == 0 && state.Display is { } lastDisplay && lastDisplay.TargetExperience != state.ObservedExperience)
            throw new InvalidDataException("XP notification snapshot lost or reordered a player publication.");
        var sounds = new HashSet<(ulong, string)>();
        foreach (var sound in state.Sounds)
        {
            if (sound is null || sound.Sequence == 0 || sound.Sequence > state.LastSequence || string.IsNullOrWhiteSpace(sound.EditorId) ||
                !Enum.IsDefined(sound.State) || sound.State == FalloutExperienceSoundState.Playing || !sounds.Add((sound.Sequence, sound.EditorId)) ||
                sound.State == FalloutExperienceSoundState.Pending && sound.Sequence != state.Display?.Sequence)
                throw new InvalidDataException("XP notification sound continuation has no current admitted owner.");
        }
    }

    private void Restore(FalloutExperienceNotificationSnapshot state)
    {
        ValidateSnapshot(state);
        if (state.Contract != _contract || state.ObservedExperience != _experience())
            throw new InvalidDataException("XP notification cold continuation differs from selected source or current player XP.");
        if (state.Display is { } display)
        {
            if (display.Level != _level() || display.SweepLevel >= _maximum() || display.TargetExperience > _threshold(_maximum()) ||
                display.SweepEndExperience != Math.Min(display.TargetExperience, _threshold(checked(display.SweepLevel + 1))) ||
                display.ThresholdCrossing != (display.TargetExperience > _threshold(checked(display.SweepLevel + 1))))
                throw new InvalidDataException("XP notification cold sweep differs from the current source thresholds.");
            var cues = state.Sounds.Where(sound => sound.Sequence == display.Sequence).ToArray();
            if (display.MeterPublished && !cues.Any(sound => sound.EditorId == _source.GainSound) || display.LevelTextPublished &&
                !cues.Any(sound => sound.EditorId == _source.LevelSound))
                throw new InvalidDataException("XP cold sequence discarded an original source sound publication.");
            if (state.Pending.Count != 0)
                throw new NotSupportedException("XP cold meter contains an unowned in-flight source retarget branch.");
            // The receipt remains evidence of elapsed history; it is not a
            // process-local draw lease. A fresh native frame is required first.
            _display = display with { PhasePresented = false };
        }
        if (state.Pending.Any(change => change.Level != _level() || change.Published > _threshold(_maximum())) ||
            state.Sounds.Any(sound => sound.EditorId != _source.GainSound && sound.EditorId != _source.LevelSound))
            throw new InvalidDataException("XP notification cold declarations differ from the actual current source.");
        _lastOrdinal = state.LastOrdinal; _retiredThrough = state.RetiredThrough;
        _lastSequence = state.LastSequence; _retiredSequence = state.RetiredSequence;
        foreach (var change in state.Pending) _pending.Enqueue(change);
        _sounds.AddRange(state.Sounds); _random.Restore(state.SoundRandomState);
        _observedExperience = state.ObservedExperience; _lastClock = null; _lastLevelClock = null;
        _levelTimestamp = state.LevelTimestampMilliseconds; _revision = 0;
    }
}
