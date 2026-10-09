using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutActorPerception
{
    private FalloutPerceptionActorSnapshot[] ActorSnapshots() => _actors.Values.Select(actor => new FalloutPerceptionActorSnapshot(
        actor.Source, actor.ProcessEpoch, actor.Process, actor.ProcessBoundary, actor.Source3D,
        actor.Cache?.Capture(), actor.Light?.Capture(), actor.Action.Capture(), actor.NativeRevision,
        actor.LastObservation?.Copy(), actor.Retired)).ToArray();

    internal FalloutActorPerceptionSnapshot Capture()
    {
        RequireStable();
        return new(Schema, _source.Contract, _stack, _player, _seconds, _revision, _frames, _phase, _computingPair, _lastSourceFrame,
            _high.ToArray(), _frameReceivers.ToArray(), _frameCursor, _pairCursor, ActorSnapshots(),
            _processHistory.ToArray(), _pairs.ToArray(), _failures.ToArray());
    }

    private void Restore(FalloutActorPerceptionSnapshot snapshot)
    {
        ValidateShape(snapshot);
        if (snapshot.Contract != _source.Contract || snapshot.StackIdentity != _stack || snapshot.Player != _player ||
            snapshot.Actors.Count > _sourceActorBound)
            throw new InvalidDataException("Cold actor perception differs from the selected executable/stack/player domain.");
        var actors = new Dictionary<FalloutFormKey, ActorState>(FalloutFormKeyComparer.Instance);
        foreach (var saved in snapshot.Actors)
        {
            if (!_selectedActor(saved.Source.Reference) || _identity(saved.Source.Reference) != saved.Source)
                throw new InvalidDataException("Cold perception actor changed its exact winning reference/base identity.");
            var state = new ActorState(saved.Source, saved.Process, new(saved.Source.Reference, 0, 0, _selectedActor))
            {
                ProcessEpoch = saved.ProcessEpoch,
                ProcessBoundary = saved.ProcessBoundary,
                Source3D = saved.Source3D,
                NativeRevision = saved.NativePublicationRevision,
                LastObservation = saved.LastObservation?.Copy(),
                Retired = saved.Retired,
            };
            state.Action.Restore(saved.ActionSound);
            if (saved.Cache is { } cache)
            {
                state.Cache = new(saved.Source.Reference, FalloutDetectionProcessLevel.High,
                    _sourceActorBound, _selectedActor);
                state.Cache.Restore(cache, snapshot.SimulationSeconds);
            }
            if (saved.Light is { } light) { state.Light = new(saved.Source.Reference, _selectedActor); state.Light.Restore(light); }
            actors.Add(saved.Source.Reference, state);
        }
        if (!actors.TryGetValue(_player, out var player) || !player.Source.EnginePlayer)
            throw new InvalidDataException("Cold perception lost the genuine player constructor owner.");
        foreach (var pair in snapshot.Pairs)
            if (!actors.ContainsKey(pair.Receiver) || !actors.ContainsKey(pair.Target))
                throw new InvalidDataException("Cold detection receipt lost an actual source actor.");
        foreach (var actor in snapshot.HighCohort)
            if (!actors.TryGetValue(actor, out var state) || state.Process?.Level != FalloutDetectionProcessLevel.High || state.Retired)
                throw new InvalidDataException("Cold HighProcess cohort contains a foreign/retired/non-High actor.");
        if (actors.Values.Count(actor => actor.Process?.Level == FalloutDetectionProcessLevel.High && !actor.Retired) != snapshot.HighCohort.Count)
            throw new InvalidDataException("Cold HighProcess cohort omitted an existing process.");
        if (snapshot.Phase == FalloutPerceptionFramePhase.NotifyingController &&
            actors[snapshot.FrameReceivers[0]].Cache?.CommitPhase != FalloutDetectionCommitPhase.NotifyCombatController)
            throw new InvalidDataException("Cold source commit phase lost its exact pending cache notification.");
        _actors.Clear(); foreach (var (key, state) in actors) _actors.Add(key, state);
        _high.Clear(); _high.AddRange(snapshot.HighCohort);
        _processHistory.Clear(); _processHistory.AddRange(snapshot.ProcessHistory);
        _pairs.Clear(); _pairs.AddRange(snapshot.Pairs); _failures.Clear(); _failures.AddRange(snapshot.Failures);
        _seconds = snapshot.SimulationSeconds; _revision = snapshot.Revision; _frames = snapshot.CompletedCacheCommits;
        _computingPair = snapshot.ComputingPair; _lastSourceFrame = snapshot.LastSourceFrame;
        _phase = snapshot.Phase; _frameReceivers = snapshot.FrameReceivers.ToArray(); _frameCursor = snapshot.FrameCursor; _pairCursor = snapshot.PairCursor;
        // Native delegates are process-local. Cold actors retain receipts but
        // must acquire actual new native bindings before producing new scores.
    }

    internal static void ValidateShape(FalloutActorPerceptionSnapshot snapshot)
    {
        if (snapshot is null || snapshot.Schema != Schema || snapshot.Contract is not { Length: 64 } ||
            !snapshot.Contract.All(Uri.IsHexDigit) || string.IsNullOrWhiteSpace(snapshot.StackIdentity) ||
            !float.IsFinite(snapshot.SimulationSeconds) || snapshot.SimulationSeconds < 0 || snapshot.Revision < 0 ||
            snapshot.CompletedCacheCommits < 0 || snapshot.CompletedCacheCommits > snapshot.Revision || !Enum.IsDefined(snapshot.Phase) ||
            snapshot.Actors is null || snapshot.HighCohort is null || snapshot.FrameReceivers is null ||
            snapshot.ProcessHistory is null || snapshot.Pairs is null || snapshot.Failures is null ||
            snapshot.Actors.Any(actor => actor is null || actor.Source is null) ||
            snapshot.Actors.Select(actor => actor.Source.Reference).Distinct(FalloutFormKeyComparer.Instance).Count() != snapshot.Actors.Count ||
            snapshot.HighCohort.Distinct(FalloutFormKeyComparer.Instance).Count() != snapshot.HighCohort.Count ||
            snapshot.FrameCursor != 0 || snapshot.PairCursor != 0 ||
            snapshot.Phase == FalloutPerceptionFramePhase.Idle && snapshot.FrameReceivers.Count != 0 ||
            snapshot.Phase is not (FalloutPerceptionFramePhase.Idle or FalloutPerceptionFramePhase.Computing or FalloutPerceptionFramePhase.Committing or FalloutPerceptionFramePhase.NotifyingController) ||
            (snapshot.Phase == FalloutPerceptionFramePhase.Computing) != (snapshot.ComputingPair is not null) ||
            snapshot.Phase == FalloutPerceptionFramePhase.Computing && (snapshot.Failures.Count == 0 || snapshot.FrameReceivers.Count != 0) ||
            (snapshot.Phase is FalloutPerceptionFramePhase.Committing or FalloutPerceptionFramePhase.NotifyingController) && snapshot.FrameReceivers.Count != 1)
            throw new InvalidDataException("Current perception snapshot has an invalid source denominator, clock or phase.");
        if (snapshot.ComputingPair is { } active && (active.Receiver == active.Target || active.ReceiverProcessEpoch < 1 ||
            active.TargetProcessEpoch < 1 || !snapshot.Actors.Any(actor => actor.Source.Reference == active.Receiver && actor.ProcessEpoch == active.ReceiverProcessEpoch) ||
            !snapshot.Actors.Any(actor => actor.Source.Reference == active.Target && actor.ProcessEpoch == active.TargetProcessEpoch)))
            throw new InvalidDataException("Cold failed pair lost its source actors/process epochs.");
        foreach (var actor in snapshot.Actors)
        {
            actor.Source.Validate(); actor.Process?.Validate();
            if (actor.ProcessEpoch < 1 || actor.NativePublicationRevision < 0 || actor.NativePublicationRevision > snapshot.Revision ||
                actor.ActionSound is null || actor.ActionSound.Actor != actor.Source.Reference ||
                (actor.Process?.Level == FalloutDetectionProcessLevel.High ? actor.Cache is null || actor.Light is null : actor.Cache is not null || actor.Light is not null) ||
                actor.Process is null && string.IsNullOrWhiteSpace(actor.ProcessBoundary) ||
                actor.Cache is { } cache && cache.Owner != actor.Source.Reference ||
                actor.Light is { } light && light.Actor != actor.Source.Reference ||
                actor.LastObservation is { } observation && (observation.Actor != actor.Source.Reference || observation.HasSource3D != actor.Source3D))
                throw new InvalidDataException("Cold perception actor lost its exact process/native/cache/clock ownership.");
            actor.LastObservation?.Validate();
            var history = snapshot.ProcessHistory.Where(receipt => receipt is not null && receipt.Actor == actor.Source.Reference).ToArray();
            if (history.Length == 0 || history[0].Epoch != 1 || history[0].Operation != FalloutPerceptionProcessOperation.FreshConstruction ||
                history[0].After != (actor.Source.EnginePlayer ? FalloutDetectionProcessLevel.High : FalloutDetectionProcessLevel.Low) ||
                history[^1].Epoch != actor.ProcessEpoch || history[^1].After != actor.Process?.Level ||
                history.Zip(history.Skip(1)).Any(pair => pair.First.Revision >= pair.Second.Revision ||
                    pair.Second.Epoch < pair.First.Epoch || pair.Second.Epoch > pair.First.Epoch + 1))
                throw new InvalidDataException("Cold perception process class lost its actual constructor/transition prefix.");
        }
        if (snapshot.LastSourceFrame is { } sourceFrame && (sourceFrame.Revision <= 0 || sourceFrame.Revision > snapshot.Revision ||
            !float.IsFinite(sourceFrame.Seconds) || sourceFrame.Seconds < 0 || sourceFrame.Seconds > snapshot.SimulationSeconds ||
            string.IsNullOrWhiteSpace(sourceFrame.Owner) || sourceFrame.CompletedPairs < 0 || sourceFrame.CompletedCommits < 0 || sourceFrame.CompletedLights < 0 ||
            !sourceFrame.Complete && snapshot.Failures.Count == 0))
            throw new InvalidDataException("Cold detection source frame lost its actual scheduler prefix.");
        var revisions = new HashSet<long>();
        foreach (var receipt in snapshot.ProcessHistory)
            if (receipt is null || receipt.Epoch < 1 || !Enum.IsDefined(receipt.Operation) || string.IsNullOrWhiteSpace(receipt.Owner) ||
                receipt.Revision <= 0 || receipt.Revision > snapshot.Revision || !revisions.Add(receipt.Revision))
                throw new InvalidDataException("Cold process history has an invalid owner/epoch/chronology.");
        foreach (var pair in snapshot.Pairs)
            if (pair is null || pair.Receiver == pair.Target || pair.ReceiverProcessEpoch < 1 || pair.TargetProcessEpoch < 1 ||
                !float.IsFinite(pair.Seconds) || pair.Seconds < 0 || pair.Seconds > snapshot.SimulationSeconds ||
                pair.Revision <= 0 || pair.Revision > snapshot.Revision || !revisions.Add(pair.Revision) || string.IsNullOrWhiteSpace(pair.Owner) ||
                pair.Result is null || !float.IsFinite(pair.Result.ContinuousScore))
                throw new InvalidDataException("Cold source pair receipt has invalid identities, result or chronology.");
        foreach (var failure in snapshot.Failures)
            if (failure is null || failure.Revision <= 0 || failure.Revision > snapshot.Revision || !revisions.Add(failure.Revision) ||
                string.IsNullOrWhiteSpace(failure.Owner) || string.IsNullOrWhiteSpace(failure.Error))
                throw new InvalidDataException("Cold perception fault lost its original ordered failure.");
    }
}
