using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

// This is the original manager's request list, not the completed process
// factory/cohort and not the loader's queued-reference map. Actor factories
// remain responsible for consuming requests and retiring old process owners.
internal sealed partial class FalloutProcessReevaluationState : IDisposable
{
    internal const string Schema = "opennv-process-reevaluation/v1";
    private readonly FalloutActorProcessQueueDeclaration _source;
    private readonly string _stack;
    private readonly Func<FalloutFormKey, FalloutCombatActorIdentity> _identity;
    private readonly Func<FalloutFormKey, long, FalloutProcessReevaluationObservation> _observe;
    private readonly Func<FalloutActorProcessElection, FalloutDetectionProcessLevel> _desired;
    private readonly Guid _process = Guid.NewGuid();
    private readonly Dictionary<FalloutFormKey, FalloutProcessReevaluationEntry> _actors = new(FalloutFormKeyComparer.Instance);
    private readonly List<FalloutFormKey> _pending = [];
    private readonly List<FalloutProcessReevaluationInvocation> _invocations = [];
    private FalloutActorProcessRuntimeHandoff? _cold;
    private long _sequence, _callbackFault;
    private bool _busy, _disposed;

    internal FalloutProcessReevaluationState(FalloutActorProcessQueueDeclaration source, string stack,
        Func<FalloutFormKey, FalloutCombatActorIdentity> identity,
        Func<FalloutFormKey, long, FalloutProcessReevaluationObservation> observe,
        Func<FalloutActorProcessElection, FalloutDetectionProcessLevel> desired,
        FalloutProcessReevaluationSnapshot? restore = null)
    {
        source.Validate(); ArgumentException.ThrowIfNullOrWhiteSpace(stack);
        ArgumentNullException.ThrowIfNull(identity); ArgumentNullException.ThrowIfNull(observe); ArgumentNullException.ThrowIfNull(desired);
        _source = source; _stack = stack; _identity = identity; _observe = observe; _desired = desired;
        if (restore is not null) Restore(restore);
    }
    internal IReadOnlyList<FalloutFormKey> Pending => _pending.ToArray();
    internal void RequireActors(IReadOnlyList<FalloutActorProcessRegistration> actors)
    {
        RequireNotBusy();
        if (actors.Count != _actors.Count || actors.Any(actor => !_actors.TryGetValue(actor.Source.Reference, out var value) ||
            value.Source != actor.Source || value.Epoch != actor.Epoch || value.Retired != actor.Retired))
            throw new InvalidDataException("Pending reevaluation fields omitted or duplicated an actual current actor/process lifetime.");
    }
    internal string? SaveBlocker => _busy ? "original-process-reevaluation-consumer-in-flight" :
        _invocations.FirstOrDefault(item => item.Failure is not null || item.Phase is not (FalloutProcessReevaluationPhase.Complete or
            FalloutProcessReevaluationPhase.AlreadyPending or FalloutProcessReevaluationPhase.ProcessAbsent)) is { } incomplete ?
            "original-process-reevaluation:" + incomplete.Source.Reference + ":" + (incomplete.Failure ?? incomplete.Phase.ToString()) : null;
    internal string? RuntimeBoundary => _pending.Count == 0 ? null : "actual-original-pending-process-factory-list-consumer-unbound";
    internal object State => new { source = _source.Contract, process = _process, _sequence,
        actors = _actors.Values.ToArray(), pending = _pending.ToArray(), invocations = _invocations.ToArray(),
        cold = _cold, saveBlocker = SaveBlocker, runtimeBoundary = RuntimeBoundary };

    internal void Construct(FalloutFormKey actor, long epoch)
    {
        RequireNotBusy();
        if (_actors.TryGetValue(actor, out var existing))
        {
            if (existing.Epoch != epoch || existing.Retired) throw new InvalidDataException("Reevaluation constructor has a foreign process epoch.");
            return;
        }
        var source = Callback(() => _identity(actor)); source.Validate();
        if (source.Reference != actor || epoch != 1) throw new InvalidDataException("Reevaluation lost its original actual Actor constructor.");
        // Only the source request writer below publishes these fields here.
        // Uninspected loader/constructor writes stay nullable, rather than
        // being inferred from a currently empty manager request list.
        _actors.Add(actor, new(source, epoch, null, null, Next(), false, null));
    }
    internal FalloutProcessReevaluationInvocation Request(FalloutFormKey actor, long epoch, string owner)
    {
        Construct(actor, epoch); RequireNotBusy(); ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        var state = Require(actor, epoch);
        if (state.Failure is not null) throw new NotSupportedException(state.Failure);
        var invocation = Guid.NewGuid(); var sequence = Next();
        var current = new FalloutProcessReevaluationInvocation(invocation, state.Source, epoch, owner,
            FalloutProcessReevaluationPhase.Entered, null, null, null, null, null, null, null,
            false, sequence, sequence, null);
        _invocations.Add(current);
        Operation(invocation, () =>
        {
            // The actual first check precedes all actor/process observations.
            if (_pending.Contains(actor)) { Set(invocation, FalloutProcessReevaluationPhase.AlreadyPending); return; }
            var observation = Callback(() => _observe(actor, epoch)); ValidateObservation(observation, state);
            Update(invocation, item => item with { Observation = observation });
            if (observation.Current is null) { Set(invocation, FalloutProcessReevaluationPhase.ProcessAbsent); return; }
            var election = observation.Election ?? throw new NotSupportedException("Original desired-tier getter inputs are absent.");
            var desired = Callback(() => _desired(election));
            if (!Enum.IsDefined(desired)) throw new InvalidDataException("Original desired-tier getter returned an unknown process class.");
            Update(invocation, item => item with { Desired = desired });
            // Exact short-circuit order is observable. No missing later input
            // is consumed on an earlier true guard.
            var life = FalloutActorProcessQueueDeclaration.RejectedLife(observation.NeutralLife.Require());
            Update(invocation, item => item with { LifeGuard = life });
            var enqueue = life;
            if (!enqueue)
            {
                var different = observation.Current != desired;
                Update(invocation, item => item with { TierGuard = different }); enqueue = different;
            }
            if (!enqueue)
            {
                var common = (observation.CommonFlags.Require() & FalloutActorProcessQueueDeclaration.CommonRequestFlag) != 0;
                Update(invocation, item => item with { CommonGuard = common }); enqueue = common;
            }
            if (!enqueue)
            {
                var flagged = (observation.ReferenceFlags.Require() & FalloutActorProcessQueueDeclaration.DeletedReferenceFlag) != 0;
                Update(invocation, item => item with { ReferenceGuard = flagged }); enqueue = flagged;
            }
            if (!enqueue)
            {
                var positive = observation.PlayerTransitionCount.Require() > 0;
                Update(invocation, item => item with { PlayerGuard = positive }); enqueue = positive;
            }
            Set(invocation, FalloutProcessReevaluationPhase.GuardsReturned);
            if (enqueue)
            {
                _actors[actor] = state with { PendingReferenceFlag = true, Changed = Next() };
                Set(invocation, FalloutProcessReevaluationPhase.FlagStored);
                if (_pending.Contains(actor)) throw new InvalidOperationException("Reevaluation callback changed the exact unique pending list.");
                _pending.Add(actor);
                Update(invocation, item => item with { Phase = FalloutProcessReevaluationPhase.PendingInserted, Enqueued = true });
                _actors[actor] = _actors[actor] with { RequestByte = true, Changed = Next() };
                Set(invocation, FalloutProcessReevaluationPhase.RequestByteStored);
            }
            Set(invocation, FalloutProcessReevaluationPhase.Complete);
        });
        return _invocations.Single(item => item.Identity == invocation);
    }
    internal FalloutActorProcessFact<bool> ReadPendingReferenceFlag(FalloutFormKey actor, long epoch)
    {
        RequireNotBusy(); var state = Require(actor, epoch);
        return new(state.PendingReferenceFlag, "actual-original-reference-request-flag:" + actor + "/" + state.Changed, state.Failure);
    }
    // A process replacement changes the actor's process epoch while the
    // reference-owned pending state survives. It never consumes the list.
    internal void RebindProcess(FalloutFormKey actor, long beforeEpoch, long newEpoch)
    {
        RequireNotBusy(); var state = Require(actor, beforeEpoch);
        if (newEpoch != checked(beforeEpoch + 1)) throw new InvalidDataException("Pending reevaluation has a foreign process replacement epoch.");
        _actors[actor] = state with { Epoch = newEpoch, Changed = Next() };
    }
    internal void RequireActorRetirement(FalloutFormKey actor, long epoch)
    {
        RequireNotBusy();
        if (!_actors.TryGetValue(actor, out var state) || state.Epoch != epoch)
            throw new InvalidDataException("Queue retirement has no exact current Actor process epoch.");
        if (_pending.Contains(actor)) throw new NotSupportedException("Actor retirement still owns an original pending process request.");
    }
    internal void RetireActor(FalloutFormKey actor, long beforeEpoch, long retirementEpoch, string owner)
    {
        RequireNotBusy(); ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        if (_actors.TryGetValue(actor, out var previous) && previous.Retired && previous.Epoch == retirementEpoch) return;
        RequireActorRetirement(actor, beforeEpoch);
        var state = Require(actor, beforeEpoch);
        if (retirementEpoch != checked(beforeEpoch + 1))
            throw new InvalidDataException("Pending reevaluation retirement lost the actual invalidated Actor process epoch.");
        _actors[actor] = state with { Epoch = retirementEpoch, Retired = true, Changed = Next() };
    }
    private FalloutProcessReevaluationEntry Require(FalloutFormKey actor, long epoch) =>
        _actors.TryGetValue(actor, out var state) && state.Epoch == epoch && !state.Retired ? state :
            throw new InvalidDataException("Reevaluation lost its actual Actor/process lifetime.");
    private void Set(Guid identity, FalloutProcessReevaluationPhase phase) => Update(identity, item => item with { Phase = phase });
    private void Update(Guid identity, Func<FalloutProcessReevaluationInvocation, FalloutProcessReevaluationInvocation> update)
    {
        var index = _invocations.FindIndex(item => item.Identity == identity);
        _invocations[index] = update(_invocations[index]) with { Changed = Next() };
    }
    private void Operation(Guid identity, Action action)
    {
        RequireNotBusy(); _busy = true; var faults = _callbackFault;
        try
        {
            action();
            if (_callbackFault != faults) throw new InvalidOperationException("Reevaluation callback caught an actual reentry failure.");
        }
        catch (Exception error)
        {
            var state = _invocations.Single(item => item.Identity == identity);
            var failure = string.IsNullOrWhiteSpace(error.Message) ? error.GetType().Name : error.Message;
            Update(identity, item => item with { Failure = item.Failure ?? failure });
            _actors[state.Source.Reference] = _actors[state.Source.Reference] with
            { Failure = _actors[state.Source.Reference].Failure ?? failure, Changed = Next() };
            throw;
        }
        finally { _busy = false; }
    }
    private T Callback<T>(Func<T> callback)
    {
        var previous = _busy; _busy = true; var faults = _callbackFault;
        try
        {
            var result = callback();
            if (_callbackFault != faults) throw new InvalidOperationException("Reevaluation producer caught an actual reentry failure.");
            return result;
        }
        finally { _busy = previous; }
    }
    private void RequireNotBusy()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_busy) { _callbackFault = checked(_callbackFault + 1); throw new InvalidOperationException("Original reevaluation producer reentered."); }
    }
    private long Next() => _sequence = checked(_sequence + 1);
    internal static void ValidateObservation(FalloutProcessReevaluationObservation value, FalloutProcessReevaluationEntry actual)
    {
        if (value is null || value.Source != actual.Source || value.Epoch != actual.Epoch ||
            string.IsNullOrWhiteSpace(value.Owner) || value.Current is not null && !Enum.IsDefined(value.Current.Value) ||
            value.NeutralLife is null || value.CommonFlags is null || value.ReferenceFlags is null || value.PlayerTransitionCount is null ||
            value.Election is { } election && (election.Actor != actual.Source.Reference || election.Epoch != actual.Epoch))
            throw new InvalidDataException("Original reevaluation input belongs to another actual Actor/class/process.");
    }
    public void Dispose()
    {
        if (_disposed) return; RequireNotBusy();
        if (SaveBlocker is { } failure) throw new NotSupportedException(failure);
        // An unconsumed request is authoritative C# continuation. Retirement
        // releases this manager; it never claims the requests were processed.
        _disposed = true; _actors.Clear(); _pending.Clear();
    }
}
