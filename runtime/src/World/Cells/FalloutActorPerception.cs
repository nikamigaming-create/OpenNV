using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

// Owns source process class, both cache directions, clocks and partial frame
// transactions. Native adapters supply observations, never a detection score.
internal sealed partial class FalloutActorPerception : IDisposable
{
    private sealed class ActorState(FalloutCombatActorIdentity source,
        FalloutDetectionProcessPresence? process, FalloutDetectionActionSound action)
    {
        internal FalloutCombatActorIdentity Source { get; } = source;
        internal long ProcessEpoch = 1;
        internal FalloutDetectionProcessPresence? Process = process;
        internal string? ProcessBoundary;
        internal FalloutDetectionCache? Cache;
        internal FalloutDetectionLight? Light;
        internal FalloutDetectionActionSound Action { get; } = action;
        internal bool Source3D;
        internal long NativeRevision;
        internal FalloutPerceptionNativeObservation? LastObservation;
        internal bool Retired;
    }

    internal const string Schema = "opennv-actor-perception/v1";
    private readonly FalloutActorPerceptionDeclaration _source;
    private readonly string _stack;
    private readonly FalloutFormKey _player;
    private readonly Func<FalloutFormKey, FalloutCombatActorIdentity> _identity;
    private readonly Func<FalloutFormKey, bool> _selectedActor;
    private readonly int _sourceActorBound;
    private readonly FalloutPerceptionInputs _inputs;
    private readonly Dictionary<FalloutFormKey, ActorState> _actors = new(FalloutFormKeyComparer.Instance);
    private readonly List<FalloutFormKey> _high = [];
    private readonly List<FalloutPerceptionProcessReceipt> _processHistory = [];
    private readonly List<FalloutPerceptionPairReceipt> _pairs = [];
    private readonly List<FalloutPerceptionFailure> _failures = [];
    private float _seconds;
    private long _revision, _frames;
    private FalloutPerceptionFramePhase _phase;
    private FalloutPerceptionComputingPair? _computingPair;
    private FalloutFormKey[] _frameReceivers = [];
    private int _frameCursor, _pairCursor;
    private bool _mutating, _disposed;

    internal FalloutActorPerception(FalloutActorPerceptionDeclaration source, string stack,
        FalloutFormKey player, int sourceActorBound, Func<FalloutFormKey, bool> selectedActor,
        Func<FalloutFormKey, FalloutCombatActorIdentity> identity, FalloutPerceptionInputs inputs,
        FalloutActorPerceptionSnapshot? restore = null)
    {
        source.Validate(); ArgumentException.ThrowIfNullOrWhiteSpace(stack);
        if (sourceActorBound < 1) throw new InvalidDataException("Perception has no complete selected actor-reference domain.");
        _source = source; _stack = stack; _player = player; _sourceActorBound = sourceActorBound;
        _identity = identity; _selectedActor = selectedActor; _inputs = inputs;
        if (!identity(player).EnginePlayer) throw new InvalidDataException("Perception player is not the actual engine-created player.");
        if (restore is null) Construct(player); else Restore(restore);
    }

    internal float SimulationSeconds => _seconds;
    private string? ProducerBlocker => _failures.FirstOrDefault() is { } failure ?
        $"actor-perception-{failure.Revision}/{failure.Owner}:{failure.Error}" :
        _actors.Values.FirstOrDefault(actor => actor.ProcessBoundary is not null)?.ProcessBoundary;
    internal string? SaveBlocker => ProducerBlocker ??
        (_phase != FalloutPerceptionFramePhase.Idle ? "source-detection-frame-continuation-incomplete" :
            _seconds > 0 && _lastSourceFrame is not { Complete: true } ? "source-process-election-and-detection-cohort-schedule-unowned" : null);
    internal object State => new
    {
        _seconds,
        revision = _revision,
        completedCacheCommits = _frames,
        phase = _phase,
        computingPair = _computingPair,
        lastSourceFrame = _lastSourceFrame,
        highCohort = _high.ToArray(),
        frameReceivers = _frameReceivers,
        frameCursor = _frameCursor,
        pairCursor = _pairCursor,
        actors = ActorSnapshots(),
        processes = _processHistory.ToArray(),
        pairs = _pairs.ToArray(),
        failures = _failures.ToArray(),
        boundary = "source-process-election-cadence-filtered-visibility-light-membership-current-sensory-values-pair-hearing-perk-refinement-and-controller-inputs-are-independent;native-residency-is-not-a-tier"
    };
    internal bool HasActor(FalloutFormKey actor) => _actors.ContainsKey(actor);
    internal FalloutDetectionProcessPresence? Process(FalloutFormKey actor) => RequireActor(actor).Process;
    internal long ProcessEpoch(FalloutFormKey actor) => RequireActor(actor).ProcessEpoch;
    internal bool RequiresNativePublication(FalloutFormKey actor) => RequireActor(actor).Source3D;

    internal void Construct(FalloutFormKey actor)
    {
        Mutate(() =>
        {
            if (_actors.ContainsKey(actor)) return;
            if (!_selectedActor(actor) || _actors.Count >= _sourceActorBound)
                throw new InvalidDataException("Constructed perception actor exceeds the complete winning source domain.");
            var source = _identity(actor); source.Validate();
            if (source.Reference != actor || source.EnginePlayer != (actor == _player))
                throw new InvalidDataException("Perception construction has a foreign actor/player identity.");
            // MobileObject's temporary null is replaced by Actor's actual Low
            // construction. PlayerCharacter then replaces it with High.
            var level = source.EnginePlayer ? FalloutDetectionProcessLevel.High : FalloutDetectionProcessLevel.Low;
            var state = new ActorState(source, new(true, level), new(actor, 0, 0, _selectedActor));
            _actors.Add(actor, state);
            if (level == FalloutDetectionProcessLevel.High) CreateHigh(state);
            _processHistory.Add(new(actor, 1, FalloutPerceptionProcessOperation.FreshConstruction, null,
                level, "original-character-creature-low/player-high-constructor/" + _source.Contract, Next()));
        });
    }

    private void CreateHigh(ActorState actor)
    {
        actor.Cache = new(actor.Source.Reference, FalloutDetectionProcessLevel.High,
            _sourceActorBound, _selectedActor);
        actor.Light = new(actor.Source.Reference, _selectedActor);
        if (!_high.Contains(actor.Source.Reference, FalloutFormKeyComparer.Instance)) _high.Add(actor.Source.Reference);
    }

    // Called by an actual source process manager decision; a native body can
    // only retain an unowned decision until that factory/election arm exists.
    internal void RequestProcess(FalloutFormKey actor, long expectedEpoch,
        FalloutDetectionProcessPresence? next, string owner, bool sourceCopiesDetection = false)
    {
        Mutate(() =>
        {
            RequireIdle(); var state = RequireActor(actor); ArgumentException.ThrowIfNullOrWhiteSpace(owner);
            if (state.ProcessEpoch != expectedEpoch) throw new InvalidDataException("Actor process request belongs to a retired epoch.");
            next?.Validate();
            if (next is null)
            {
                // An unknown decision cannot destroy the last owned process,
                // cache or clock prefix. Keep it and the original refusal.
                state.ProcessBoundary = owner;
                _processHistory.Add(new(actor, state.ProcessEpoch, FalloutPerceptionProcessOperation.UnownedSourceTransition,
                    state.Process?.Level, state.Process?.Level, owner, Next()));
                return;
            }
            if (sourceCopiesDetection)
                throw new NotSupportedException("Process replacement requires its complete original cross-tier copy transaction.");
            if (state.Cache?.HasPending == true)
                throw new NotSupportedException("Process replacement cannot discard unfinished directional cache publication.");
            var before = state.Process?.Level; var after = next?.Level;
            var epoch = checked(state.ProcessEpoch + 1);
            state.Cache = null; state.Light = null; _high.Remove(actor);
            state.Process = next; state.ProcessEpoch = epoch;
            state.ProcessBoundary = null;
            if (next?.Level == FalloutDetectionProcessLevel.High) CreateHigh(state);
            _processHistory.Add(new(actor, epoch, next is null ? FalloutPerceptionProcessOperation.UnownedSourceTransition :
                next.HasProcess ? FalloutPerceptionProcessOperation.SourceFactoryRequest : FalloutPerceptionProcessOperation.SourceRetirement,
                before, after, owner, Next()));
        });
    }

    internal void ObserveNative(FalloutPerceptionNativeObservation observation)
    {
        Mutate(() =>
        {
            observation.Validate(); var actor = RequireActor(observation.Actor);
            if (actor.Retired) throw new InvalidOperationException("Retired source actor cannot acquire a new native observation.");
            if (observation.Failure is not null) throw new NotSupportedException(observation.Failure);
            actor.Source3D = observation.HasSource3D; actor.LastObservation = observation.Copy();
            actor.NativeRevision = Next();
        });
    }

    internal void RetireNative(FalloutFormKey actor, string owner)
    {
        Mutate(() =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(owner); var state = RequireActor(actor);
            state.Source3D = false; state.LastObservation = null; state.NativeRevision = Next();
            // A 3D retirement does not become a Low or null process. That has
            // its own source factory decision and can retain cache history.
        });
    }

    internal void RetireActor(FalloutFormKey actor, string owner)
    {
        Mutate(() =>
        {
            RequireIdle(); ArgumentException.ThrowIfNullOrWhiteSpace(owner);
            if (actor == _player) throw new InvalidOperationException("Engine player cannot retire inside a living perception world.");
            var state = RequireActor(actor); if (state.Retired) return;
            if (state.Cache?.HasPending == true)
                throw new NotSupportedException("Source actor retirement requires its pending detection publication owner.");
            var previous = state.Process?.Level;
            state.Retired = true; state.ProcessEpoch = checked(state.ProcessEpoch + 1);
            state.Process = new(false, null); state.Cache = null; state.Light = null; state.Source3D = false;
            state.LastObservation = null; _high.Remove(actor);
            _processHistory.Add(new(actor, state.ProcessEpoch, FalloutPerceptionProcessOperation.SourceRetirement,
                previous, null, owner, Next()));
        });
    }

    internal void SetActionSound(FalloutFormKey actor, int level)
    {
        Mutate(() => { RequireActor(actor).Action.SetLevel(level, _inputs.ActionTimer); Next(); });
    }

    internal void AdvanceClocks(float seconds)
    {
        Mutate(() =>
        {
            if (!float.IsFinite(seconds) || seconds < 0 || !float.IsFinite(_seconds + seconds))
                throw new InvalidDataException("Perception has no finite original simulation interval.");
            // World pause is checked by the common driver before this call.
            _seconds += seconds;
            foreach (var actor in _actors.Values.Where(actor => !actor.Retired)) actor.Action.Advance(seconds);
            Next();
        });
    }

    internal FalloutCombatGroupDetection ReadCombatDetection(FalloutFormKey target)
    {
        RequireStable(); var player = RequireActor(_player);
        if (SaveBlocker is { } blocker) return new(null, blocker);
        if (_seconds > 0 && _lastSourceFrame is not { Complete: true })
            return new(null, "source-detection-frame-process-election-and-high-cohort-schedule-unowned");
        if (player.Process is null) return new(null, "source-player-process-transition-unowned");
        player.Process.Validate();
        // This original getter differs from GetDetected mode zero: its signed
        // entry score is returned verbatim, including INT_MAX.
        var entry = player.Process.HasProcess && player.Process.Level == FalloutDetectionProcessLevel.High ? player.Cache?.Read(target,
            FalloutDetectionDirection.Detected) : null;
        return new(entry?.Score ?? -100, "source-player-committed-detected-signed-entry/" + _source.Contract);
    }

    internal int GetDetected(FalloutFormKey receiver, FalloutFormKey target,
        bool targetTeammate, bool playerSneaking, bool playerRawInCombat,
        Action<FalloutFormKey> preparePerkRead)
    {
        RequireStable();
        if (!_selectedActor(receiver) || !_selectedActor(target)) return 0;
        if (SaveBlocker is { } blocker) throw new NotSupportedException(blocker);
        var player = Observation(_player);
        FalloutDetectionQueryActor Query(FalloutFormKey actor)
        {
            var state = RequireActor(actor); var observation = Observation(actor);
            return new(actor, state.Process, Distance(observation.SourcePosition, player.SourcePosition),
                actor == target && targetTeammate, observation.RawInCombat, observation.HasSource3D, observation.CommandInCombat);
        }
        var query = new FalloutDetectionQuery(_selectedActor, Query,
            () => new(playerSneaking, playerRawInCombat), actor => RequireActor(actor).Cache, preparePerkRead);
        return query.GetDetected(receiver, target);
    }

    private FalloutPerceptionNativeObservation Observation(FalloutFormKey actor)
    {
        var result = _inputs.Observe(actor); result.Validate();
        if (result.Actor != actor || result.Failure is not null) throw new NotSupportedException(result.Failure ?? "Perception observation belongs to another actor.");
        return result;
    }
    private ActorState RequireActor(FalloutFormKey actor)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _actors.TryGetValue(actor, out var state) ? state : throw new NotSupportedException("Source perception actor was never constructed: " + actor);
    }
    private void RequireIdle()
    { if (_phase != FalloutPerceptionFramePhase.Idle) throw new InvalidOperationException("Perception source frame is still in progress."); }
    private void RequireStable()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_mutating)
        {
            _failures.Add(new(Next(), null, null, "actor-perception-read-reentry", "Perception read reentered a mutation."));
            throw new InvalidOperationException("Perception read reentered a mutation.");
        }
    }
    private long Next() => _revision = checked(_revision + 1);
    private void Mutate(Action action)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_mutating)
        {
            _failures.Add(new(Next(), null, null, "actor-perception-reentry", "Actor perception producer reentered its owned operation."));
            throw new InvalidOperationException("Actor perception producer reentered its owned operation.");
        }
        _mutating = true;
        try { action(); }
        catch (Exception error)
        {
            _failures.Add(new(Next(), null, null, "actor-perception-operation", string.IsNullOrWhiteSpace(error.Message) ? error.GetType().Name : error.Message));
            throw;
        }
        finally { _mutating = false; }
    }
    internal static float Distance(float[] from, float[] to)
    {
        if (from is not { Length: 3 } || to is not { Length: 3 } || from.Concat(to).Any(value => !float.IsFinite(value)))
            throw new InvalidDataException("Perception distance has no finite source vectors.");
        var x = (float)((double)from[0] - to[0]); var y = (float)((double)from[1] - to[1]); var z = (float)((double)from[2] - to[2]);
        var distance = (float)Math.Sqrt((double)x * x + (double)y * y + (double)z * z);
        return float.IsFinite(distance) ? distance : throw new InvalidDataException("Perception distance overflowed.");
    }
    public void Dispose()
    {
        if (_disposed) return;
        if (_mutating) throw new InvalidOperationException("Cannot retire perception inside an owned source operation.");
        _disposed = true; _actors.Clear(); _high.Clear();
    }
}
