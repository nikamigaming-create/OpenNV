using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutActorProcessManager
{
    private FalloutActorProcessColdHandoff? _coldHandoff;
    internal FalloutActorProcessesSnapshot Capture()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_busy) throw new InvalidOperationException("Actor process capture reentered an actual source operation.");
        return new(Schema, _source.Contract, _stack, _player, _process, _seconds, _sequence,
            _cohort.Capture(), _actors.Values.ToArray(), _factory, _schedule,
            _faults.ToArray(), _coldHandoff, _playerScheduleFlag);
    }
    private void Restore(FalloutActorProcessesSnapshot saved)
    {
        ValidateShape(saved);
        if (saved.Contract != _source.Contract || saved.Stack != _stack || saved.Player != _player || saved.CapturedProcess == _process)
            throw new InvalidDataException("Cold process graph differs from the selected source or new process lifetime.");
        foreach (var actor in saved.Actors)
        {
            if (!_sourceActor(actor.Source.Reference) || _identity(actor.Source.Reference) != actor.Source ||
                !_perception.HasActor(actor.Source.Reference) ||
                _perception.ProcessEpoch(actor.Source.Reference) != actor.Epoch ||
                _perception.Process(actor.Source.Reference)?.Level != actor.Level)
                throw new InvalidDataException("Cold actor process lost its winning base/reference/perception epoch.");
            _actors.Add(actor.Source.Reference, actor);
        }
        var actual = _perception.Capture();
        if (actual.Actors.Count != _actors.Count || actual.Actors.Any(actor => !_actors.ContainsKey(actor.Source.Reference)))
            throw new InvalidDataException("Cold source process graph omitted a constructed perception actor.");
        _seconds = saved.Seconds; _sequence = saved.Sequence;
        _factory = saved.Factory; _schedule = saved.Schedule; _faults.AddRange(saved.Faults);
        _playerScheduleFlag = saved.PlayerScheduleFlag;
        _coldHandoff = new(saved.CapturedProcess, _process, Next(), "actual-new-process-cold-owner-handoff-no-clock-or-source-prefix-replay");
        // Runtime delegates/native 3D are not restored. The underlying
        // perception owner requires its actual fresh native publications.
    }
    internal static void ValidateShape(FalloutActorProcessesSnapshot saved)
    {
        if (saved is null || saved.Schema != Schema || saved.Contract is not { Length: 64 } || !saved.Contract.All(Uri.IsHexDigit) ||
            string.IsNullOrWhiteSpace(saved.Stack) || saved.CapturedProcess == Guid.Empty || !float.IsFinite(saved.Seconds) || saved.Seconds < 0 ||
            saved.Sequence < 0 || saved.Actors is null || saved.Faults is null || saved.Cohort is null ||
            saved.Actors.Any(actor => actor is null || actor.Source is null) ||
            saved.Actors.Select(actor => actor.Source.Reference).Distinct(FalloutFormKeyComparer.Instance).Count() != saved.Actors.Count)
            throw new InvalidDataException("Cold actor process snapshot has no complete source/current lifetime.");
        FalloutActorProcessCohort.Validate(saved.Cohort);
        var registered = new Dictionary<FalloutFormKey, FalloutActorProcessRegistration>(FalloutFormKeyComparer.Instance);
        foreach (var actor in saved.Actors)
        {
            actor.Source.Validate();
            if (actor.Epoch < 1 || actor.Source.EnginePlayer != (actor.Source.Reference == saved.Player) ||
                actor.Level is { } level && !Enum.IsDefined(level) ||
                actor.Retired && (actor.Level is not null || actor.Registered) || actor.Source.EnginePlayer &&
                    (actor.Retired || actor.Level != FalloutDetectionProcessLevel.High || actor.Registered) ||
                !actor.Retired && actor.Level is null && string.IsNullOrWhiteSpace(actor.Boundary))
                throw new InvalidDataException("Cold process registration lost source class/epoch/timer/player separation.");
            if (actor.Level == FalloutDetectionProcessLevel.High ?
                actor.DetectionTimer is not { } timer || !float.IsFinite(timer) || actor.DetectionGeneration is not { } generation || generation >= 12 ||
                    actor.DetectionUpdated is null || string.IsNullOrWhiteSpace(actor.LastTimerOwner) :
                actor.DetectionTimer is not null || actor.DetectionGeneration is not null || actor.DetectionUpdated is not null || actor.LastTimerOwner is not null)
                throw new InvalidDataException("Cold non-High actor fabricated detection fields or High lost its actual timer owner.");
            if (actor.Source.EnginePlayer)
            {
                if (actor.Construction is not null) throw new InvalidDataException("Player constructor was replaced by a cohort registration caller.");
            }
            else
            {
                var construction = actor.Construction ?? throw new InvalidDataException("Cold actor omitted its original constructor registration inputs.");
                if (construction.Actor != actor.Source.Reference || construction.Boundary() is not null &&
                    (actor.Boundary is null || actor.Registered || actor.Retired || actor.Epoch != 1 || actor.Level != FalloutDetectionProcessLevel.Low))
                    throw new InvalidDataException("Cold constructor admitted an absent/alternate registration owner.");
                var inUnregisteredFactory = saved.Factory is { } transition && transition.Actor == actor.Source.Reference &&
                    (int)transition.Phase > (int)FalloutActorProcessFactoryPhase.Created && (int)transition.Phase < (int)FalloutActorProcessFactoryPhase.RegisteredNew;
                if (!actor.Retired && construction.Boundary() is null && !inUnregisteredFactory &&
                    actor.Registered != (actor.Epoch > 1 || construction.RegistrationRequested.Value == true))
                    throw new InvalidDataException("Cold actor registration differs from its actual constructor/factory prefix.");
            }
            if (actor.Registered) registered.Add(actor.Source.Reference, actor);
        }
        var found = new HashSet<FalloutFormKey>(FalloutFormKeyComparer.Instance);
        for (var tier = 0; tier < 4; tier++)
            for (var slot = saved.Cohort.Starts[tier]; slot < saved.Cohort.Ends[tier]; slot++)
                if (saved.Cohort.Slots[slot] is { } key &&
                    (!registered.TryGetValue(key, out var actor) || actor.Level is null || FalloutActorProcessCohort.Tier(actor.Level.Value) != tier || !found.Add(key)))
                    throw new InvalidDataException("Cold cohort has an unknown/duplicate/foreign-tier registered actor.");
        if (found.Count != registered.Count || !saved.Actors.Any(actor => actor.Source.Reference == saved.Player && actor.Source.EnginePlayer))
            throw new InvalidDataException("Cold actor process graph omitted actual registrations or engine Player.");
        if (saved.Factory is { } factory)
        {
            var phase = (int)factory.Phase;
            var actor = saved.Actors.SingleOrDefault(actor => actor.Source.Reference == factory.Actor);
            if (actor is null || factory.BeforeEpoch < 1 || factory.NewEpoch != checked(factory.BeforeEpoch + 1) ||
                !Enum.IsDefined(factory.Before) || factory.Before == FalloutDetectionProcessLevel.High || factory.After != FalloutDetectionProcessLevel.High ||
                !Enum.IsDefined(factory.Phase) || string.IsNullOrWhiteSpace(factory.Owner) ||
                factory.Operation != FalloutActorProcessSourceOperation.EnsureHigh || factory.BeforePerception is null || factory.BeforePerception.Source != actor.Source ||
                factory.BeforePerception.ProcessEpoch != factory.BeforeEpoch ||
                factory.BeforePerception.Retired || factory.BeforePerception.Process is not { HasProcess: true } oldProcess || oldProcess.Level != factory.Before ||
                actor.Epoch != (phase >= (int)FalloutActorProcessFactoryPhase.PublishedNew ? factory.NewEpoch : factory.BeforeEpoch) ||
                actor.Level != (phase >= (int)FalloutActorProcessFactoryPhase.PublishedNew ? factory.After : factory.Before) ||
                phase >= (int)FalloutActorProcessFactoryPhase.ConstructedNew && (factory.NewCache is null || factory.NewLight is null) ||
                phase < (int)FalloutActorProcessFactoryPhase.ConstructedNew && (factory.NewCache is not null || factory.NewLight is not null) ||
                phase > (int)FalloutActorProcessFactoryPhase.Created && phase < (int)FalloutActorProcessFactoryPhase.RegisteredNew && actor.Registered ||
                phase >= (int)FalloutActorProcessFactoryPhase.RegisteredNew && !actor.Registered ||
                factory.Phase != FalloutActorProcessFactoryPhase.Complete && string.IsNullOrWhiteSpace(factory.Failure) ||
                factory.Phase == FalloutActorProcessFactoryPhase.Complete && factory.Failure is not null)
                throw new InvalidDataException("Cold factory lost its actual old/new ownership and consumed phase.");
            if (factory.NewCache is { } cache && (cache.Schema != FalloutDetectionCache.SnapshotSchema || cache.Owner != factory.Actor ||
                cache.Process != FalloutDetectionProcessLevel.High || cache.Revision != 0 || cache.CommitPhase != FalloutDetectionCommitPhase.Idle ||
                cache.Detected is not { Count: 0 } || cache.Detecting is not { Count: 0 } || cache.PendingDetected is not { Count: 0 } ||
                cache.PendingDetecting is not { Count: 0 } || cache.CompletedCommits != 0 || cache.CombatNotificationRevision != 0) ||
                factory.NewLight is { } light && (light.Schema != "opennv-detection-light/v1" || light.Actor != factory.Actor ||
                    BitConverter.SingleToInt32Bits(light.Amount) != 0 || BitConverter.SingleToInt32Bits(light.CountdownSeconds) != 0 || light.Revision != 0))
                throw new InvalidDataException("Cold factory replaced its actual empty new process constructor state.");
        }
        if (saved.Schedule is { } schedule)
        {
            if (schedule.Sequence < 1 || schedule.Sequence > saved.Sequence || !float.IsFinite(schedule.Seconds) ||
                schedule.Seconds < 0 || schedule.Seconds > saved.Seconds || !float.IsFinite(schedule.Delta) || schedule.Delta < 0 ||
                !float.IsFinite(schedule.RandomWindow) || schedule.RandomWindow < 0 || schedule.RandomWindow > 5 ||
                schedule.HighStart != 0 || schedule.HighEndAtEntry < 0 || schedule.NextSlot < 0 || schedule.Visits is null ||
                schedule.HighSlotsAtEntry is null || schedule.HighSlotsAtEntry.Count != schedule.HighEndAtEntry ||
                schedule.CohortRevision < 0 || schedule.CohortRevision > saved.Cohort.Revision ||
                schedule.Visits.Any(visit => visit is null) || schedule.NextSlot != schedule.Visits.Count || schedule.NextSlot > schedule.HighEndAtEntry ||
                schedule.CompletedCommits < 0 || schedule.CompletedCommits > 1 + schedule.Visits.Count(visit => visit.Actor is not null) ||
                schedule.Complete && schedule.InFlight is not null ||
                !schedule.Complete && string.IsNullOrWhiteSpace(schedule.Failure) || schedule.Complete &&
                    (schedule.Failure is not null || schedule.NextSlot != schedule.HighEndAtEntry ||
                     schedule.CompletedCommits != 1 + schedule.HighSlotsAtEntry.Count(key => key is not null)) ||
                schedule.Visits.Any(visit => visit is null || visit.Slot < 0 || !Enum.IsDefined(visit.Kind) ||
                    string.IsNullOrWhiteSpace(visit.Owner) || visit.TimerBefore is { } before && !float.IsFinite(before) ||
                    visit.TimerAfter is { } after && !float.IsFinite(after) || visit.RandomInterval is { } random &&
                        (!float.IsFinite(random) || random < 0 || random > schedule.RandomWindow)) ||
                schedule.Visits.Select(visit => visit.Slot).Where((slot, index) => slot != index).Any())
                throw new InvalidDataException("Cold detection schedule lost its exact cursor/clock/visit prefix.");
            foreach (var visit in schedule.Visits)
            {
                if (visit.Actor != schedule.HighSlotsAtEntry[visit.Slot] || (visit.Actor is null ?
                    visit.Kind != FalloutActorDetectionVisitKind.NullSlot || visit.Epoch != 0 :
                    visit.Kind == FalloutActorDetectionVisitKind.NonActor ? visit.Epoch != 0 :
                    visit.Epoch < 1 || !saved.Actors.Any(actor => actor.Source.Reference == visit.Actor && actor.Epoch >= visit.Epoch)))
                    throw new InvalidDataException("Cold schedule visit lost its exact registered source actor/epoch/slot.");
            }
            if (schedule.CohortRevision == saved.Cohort.Revision &&
                !schedule.HighSlotsAtEntry.SequenceEqual(saved.Cohort.Slots.Take(saved.Cohort.Ends[0])))
                throw new InvalidDataException("Current source cohort differs from the actual frame's retained array.");
            if (schedule.InFlight is { } pending && (pending.Slot != schedule.NextSlot || pending.Slot >= schedule.HighEndAtEntry ||
                pending.Actor is null || pending.Actor != schedule.HighSlotsAtEntry[pending.Slot] || pending.Epoch < 1 ||
                !saved.Actors.Any(actor => actor.Source.Reference == pending.Actor && actor.Epoch >= pending.Epoch) ||
                pending.Kind != FalloutActorDetectionVisitKind.ProducerEntered || pending.SharedRandomWord is null ||
                pending.RandomInterval is not { } interval || !float.IsFinite(interval) || interval < 0 || interval > schedule.RandomWindow ||
                pending.TimerBefore is not { } timer || !float.IsFinite(timer) || !FalloutActorProcessDeclaration.TimerNeedsProducer(timer) ||
                pending.TimerAfter is not { } after || !float.IsFinite(after) || pending.CompletedPairs != 0 || string.IsNullOrWhiteSpace(pending.Owner)))
                throw new InvalidDataException("Cold High producer lost its actual consumed random word/cleared-prefix owner.");
        }
        var last = 0L;
        foreach (var fault in saved.Faults)
        {
            if (fault is null || fault.Sequence <= last || fault.Sequence > saved.Sequence ||
                string.IsNullOrWhiteSpace(fault.Owner) || string.IsNullOrWhiteSpace(fault.Error))
                throw new InvalidDataException("Cold process graph lost its retained ordered failure.");
            last = fault.Sequence;
        }
        if (saved.ColdHandoff is { } handoff && (handoff.PreviousProcess == Guid.Empty || handoff.CurrentProcess != saved.CapturedProcess ||
            handoff.PreviousProcess == handoff.CurrentProcess || handoff.Sequence < 1 || handoff.Sequence > saved.Sequence || string.IsNullOrWhiteSpace(handoff.Owner)))
            throw new InvalidDataException("Cold process owner lost its genuine process epoch handoff.");
    }
}
