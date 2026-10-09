using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutProcessReevaluationState
{
    internal FalloutProcessReevaluationSnapshot Capture()
    {
        RequireNotBusy();
        return new(Schema, _stack, _source.Contract, _process, _sequence, _actors.Values.ToArray(),
            _pending.ToArray(), _invocations.ToArray(), _cold);
    }
    private void Restore(FalloutProcessReevaluationSnapshot saved)
    {
        Validate(saved);
        if (saved.Stack != _stack || saved.Contract != _source.Contract || saved.CapturedProcess == _process)
            throw new InvalidDataException("Pending process cold state changed its selected source/process.");
        foreach (var actor in saved.Actors)
        {
            var actual = Callback(() => _identity(actor.Source.Reference)); actual.Validate();
            if (actual != actor.Source) throw new InvalidDataException("Pending process cold state changed exact winning actor/reference/master bytes.");
            _actors.Add(actor.Source.Reference, actor);
        }
        _pending.AddRange(saved.Pending); _invocations.AddRange(saved.Invocations); _sequence = saved.Sequence;
        _cold = new(saved.CapturedProcess, _process, Next());
    }
    internal static void Validate(FalloutProcessReevaluationSnapshot saved)
    {
        if (saved is null || saved.Schema != Schema || string.IsNullOrWhiteSpace(saved.Stack) || saved.Contract is not { Length: 64 } ||
            !saved.Contract.All(Uri.IsHexDigit) || saved.CapturedProcess == Guid.Empty || saved.Sequence < 0 ||
            saved.Actors is null || saved.Pending is null || saved.Invocations is null || saved.Actors.Any(item => item is null || item.Source is null) ||
            saved.Invocations.Any(item => item is null || item.Source is null) ||
            saved.Actors.Select(item => item.Source.Reference).Distinct(FalloutFormKeyComparer.Instance).Count() != saved.Actors.Count ||
            saved.Pending.Distinct(FalloutFormKeyComparer.Instance).Count() != saved.Pending.Count ||
            saved.Invocations.Select(item => item.Identity).Distinct().Count() != saved.Invocations.Count)
            throw new InvalidDataException("Pending process snapshot omitted or duplicated authoritative owners.");
        var pending = new List<FalloutFormKey>(); var previous = 0L;
        foreach (var item in saved.Invocations)
        {
            var actor = saved.Actors.SingleOrDefault(state => state.Source.Reference == item.Source.Reference) ??
                throw new InvalidDataException("Pending process call omitted its real Actor owner.");
            if (item.Identity == Guid.Empty || item.Source != actor.Source || item.Epoch < 1 || item.Epoch > actor.Epoch ||
                string.IsNullOrWhiteSpace(item.Owner) || !Enum.IsDefined(item.Phase) || item.Phase == FalloutProcessReevaluationPhase.Failed ||
                item.Entered <= previous || item.Entered > item.Changed || item.Changed > saved.Sequence ||
                item.Failure is not null && string.IsNullOrWhiteSpace(item.Failure))
                throw new InvalidDataException("Pending process call lost actual source order/epoch/phase.");
            previous = item.Changed;
            var already = pending.Contains(item.Source.Reference);
            if (item.Phase == FalloutProcessReevaluationPhase.AlreadyPending)
            {
                if (!already || item.Observation is not null || item.Desired is not null || item.Enqueued || item.Failure is not null ||
                    item.LifeGuard is not null || item.TierGuard is not null || item.CommonGuard is not null ||
                    item.ReferenceGuard is not null || item.PlayerGuard is not null)
                    throw new InvalidDataException("Pending process duplicate executed later source consumers.");
                continue;
            }
            if (already) throw new InvalidDataException("Pending process repeated a previously consumed unique insertion.");
            if (item.Observation is { } observation)
            {
                ValidateObservation(observation, actor with { Epoch = item.Epoch });
                if (item.Phase == FalloutProcessReevaluationPhase.ProcessAbsent &&
                    (observation.Current is not null || item.Desired is not null || item.Enqueued || item.Failure is not null ||
                        item.LifeGuard is not null || item.TierGuard is not null || item.CommonGuard is not null ||
                        item.ReferenceGuard is not null || item.PlayerGuard is not null))
                    throw new InvalidDataException("Pending process absent-process branch executed unavailable consumers.");
                if (item.Desired is null && (item.Phase >= FalloutProcessReevaluationPhase.GuardsReturned ||
                    item.LifeGuard is not null || item.TierGuard is not null || item.CommonGuard is not null ||
                    item.ReferenceGuard is not null || item.PlayerGuard is not null || item.Enqueued))
                    throw new InvalidDataException("Pending process guards lack the original preceding desired-tier getter.");
                if (item.Desired is { } desired)
                {
                    if (!Enum.IsDefined(desired) || observation.Current is null || observation.Election is null)
                        throw new InvalidDataException("Pending process desired tier lacks its actual getter.");
                    if (item.LifeGuard is { } life && life != FalloutActorProcessQueueDeclaration.RejectedLife(observation.NeutralLife.Require()) ||
                        item.TierGuard is { } tier && tier != (observation.Current != desired) ||
                        item.CommonGuard is { } common && common != ((observation.CommonFlags.Require() & FalloutActorProcessQueueDeclaration.CommonRequestFlag) != 0) ||
                        item.ReferenceGuard is { } flagged && flagged != ((observation.ReferenceFlags.Require() & FalloutActorProcessQueueDeclaration.DeletedReferenceFlag) != 0) ||
                        item.PlayerGuard is { } player && player != (observation.PlayerTransitionCount.Require() > 0))
                        throw new InvalidDataException("Pending process changed an actually consumed original guard.");
                    var guards = new bool?[] { item.LifeGuard, item.TierGuard, item.CommonGuard, item.ReferenceGuard, item.PlayerGuard };
                    var reached = true; var enqueue = false;
                    foreach (var guard in guards)
                    {
                        if (!reached && guard is not null) throw new InvalidDataException("Pending process consumed a short-circuited guard.");
                        if (guard is null) reached = false;
                        else if (guard.Value) { enqueue = true; reached = false; }
                    }
                    if (item.Phase >= FalloutProcessReevaluationPhase.GuardsReturned &&
                        (!enqueue && guards.Any(guard => guard is null) || item.Enqueued != (item.Phase >= FalloutProcessReevaluationPhase.PendingInserted && enqueue)))
                        throw new InvalidDataException("Pending process claimed returned guards or insertion not actually owned.");
                    if (item.Phase >= FalloutProcessReevaluationPhase.FlagStored && !enqueue && item.Phase != FalloutProcessReevaluationPhase.Complete)
                        throw new InvalidDataException("Pending process wrote a request flag on a rejected enqueue branch.");
                }
            }
            else if (item.Desired is not null || item.Enqueued || item.Phase != FalloutProcessReevaluationPhase.Entered ||
                item.LifeGuard is not null || item.TierGuard is not null || item.CommonGuard is not null ||
                item.ReferenceGuard is not null || item.PlayerGuard is not null)
                throw new InvalidDataException("Pending process call claimed unperformed actor observations.");
            if (item.Enqueued) pending.Add(item.Source.Reference);
            if (item.Phase == FalloutProcessReevaluationPhase.Complete &&
                (item.Observation?.Current is null || item.Desired is null || item.LifeGuard is null || item.Failure is not null))
                throw new InvalidDataException("Pending process completion substituted for the nullable process branch.");
        }
        if (!pending.SequenceEqual(saved.Pending)) throw new InvalidDataException("Pending process list changed its exact original insertion order.");
        foreach (var actor in saved.Actors)
        {
            actor.Source.Validate(); var history = saved.Invocations.Where(item => item.Source.Reference == actor.Source.Reference).ToArray();
            if (actor.Epoch < 1 || actor.Changed < 1 || actor.Changed > saved.Sequence || actor.Retired && saved.Pending.Contains(actor.Source.Reference) ||
                actor.PendingReferenceFlag != (history.Any(item => item.Enqueued || item.Phase == FalloutProcessReevaluationPhase.FlagStored) ? true : (bool?)null) ||
                actor.RequestByte != (history.Any(item => (item.Phase is FalloutProcessReevaluationPhase.RequestByteStored or FalloutProcessReevaluationPhase.Complete) && item.Enqueued) ? true : (bool?)null) ||
                actor.Failure is not null && (!history.Any(item => item.Failure == actor.Failure) || string.IsNullOrWhiteSpace(actor.Failure)))
                throw new InvalidDataException("Pending process flags/request byte are not actual source writer results.");
        }
        FalloutCellExtraProcessState.RequireHandoff(saved.ColdHandoff, saved.CapturedProcess, saved.Sequence);
    }
}
