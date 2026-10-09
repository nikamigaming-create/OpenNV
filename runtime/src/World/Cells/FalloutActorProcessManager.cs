using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

// Process and registration are distinct owners. PlayerCharacter has a High
// process and is intentionally absent from the manager's segmented array.
internal sealed partial class FalloutActorProcessManager : IDisposable
{
    internal const string Schema = "opennv-actor-processes/v1";
    private readonly FalloutActorProcessDeclaration _source;
    private readonly string _stack;
    private readonly FalloutFormKey _player;
    private readonly FalloutActorPerception _perception;
    private readonly Func<FalloutFormKey, FalloutCombatActorIdentity> _identity;
    private readonly Func<FalloutFormKey, bool> _sourceActor;
    private readonly FalloutActorProcessInputs _inputs;
    private readonly Guid _process = Guid.NewGuid();
    private readonly Dictionary<FalloutFormKey, FalloutActorProcessRegistration> _actors = new(FalloutFormKeyComparer.Instance);
    private readonly List<FalloutActorProcessFault> _faults = [];
    private readonly FalloutActorProcessCohort _cohort;
    private FalloutActorProcessFactorySnapshot? _factory;
    private FalloutActorProcessScheduleReceipt? _schedule;
    private float _seconds;
    private long _sequence;
    private bool _busy, _disposed;
    private bool? _playerScheduleFlag;

    internal FalloutActorProcessManager(FalloutActorProcessDeclaration source, string stack,
        FalloutFormKey player, FalloutActorPerception perception,
        Func<FalloutFormKey, FalloutCombatActorIdentity> identity, Func<FalloutFormKey, bool> sourceActor,
        FalloutActorProcessInputs inputs, FalloutActorProcessesSnapshot? restore = null)
    {
        source.Validate(); ArgumentException.ThrowIfNullOrWhiteSpace(stack);
        _source = source; _stack = stack; _player = player; _perception = perception;
        _identity = identity; _sourceActor = sourceActor; _inputs = inputs;
        _cohort = new(sourceActor, restore?.Cohort);
        if (restore is null) Construct(player); else Restore(restore);
    }
    internal bool HasActor(FalloutFormKey actor) => _actors.ContainsKey(actor);
    internal string? SaveBlocker => _faults.FirstOrDefault() is { } fault ?
        $"source-actor-process-{fault.Sequence}/{fault.Owner}:{fault.Error}" :
        _factory is { Phase: not FalloutActorProcessFactoryPhase.Complete } ? "source-process-factory-continuation-incomplete" :
        ConstructorSourceSaveBlocker is { } constructor ? constructor :
        _actors.Values.FirstOrDefault(actor => actor.Boundary is not null)?.Boundary ??
        (_seconds > 0 && _schedule is not { Complete: true } ? "source-actor-cohort-schedule-not-completed" :
         _schedule is { } schedule && schedule.CohortRevision != _cohort.Revision ? "source-actor-cohort-changed-after-detection-schedule" : null);
    internal object State => new
    {
        source = _source.Contract,
        _stack,
        process = _process,
        _seconds,
        _sequence,
        cohort = _cohort.Capture(),
        actors = _actors.Values.ToArray(),
        factory = _factory,
        schedule = _schedule,
        faults = _faults.ToArray(),
        saveBlocker = SaveBlocker
    };
    internal FalloutActorProcessRegistration Read(FalloutFormKey actor) => RequireActor(actor);
    internal IReadOnlyList<FalloutFormKey?> HighCohort => _cohort.Segment(0);

    internal void Construct(FalloutFormKey actor)
    {
        Operation(actor, "source-actor-constructor-registration", () =>
        {
            if (_actors.ContainsKey(actor)) return;
            if (!_sourceActor(actor) || !_perception.HasActor(actor))
                throw new InvalidDataException("Actor process construction lost the actual source reference/perception owner.");
            var source = _identity(actor); source.Validate();
            var tier = source.EnginePlayer ? FalloutDetectionProcessLevel.High : FalloutDetectionProcessLevel.Low;
            if (source.Reference != actor || source.EnginePlayer != (actor == _player) ||
                _perception.Process(actor) is not { HasProcess: true } process || process.Level != tier || _perception.ProcessEpoch(actor) != 1)
                throw new InvalidDataException("Actor process constructor differs from the original actor/player lifetime.");
            var construction = actor == _player ? null : _inputs.Construction(actor);
            var boundary = construction?.Boundary();
            if (construction is not null && construction.Actor != actor)
                throw new InvalidDataException("Actor constructor registration belongs to another source reference.");
            var registered = construction is not null && boundary is null && construction.RegistrationRequested.Value == true;
            _actors.Add(actor, new(source, 1, tier, false, false, boundary,
                tier == FalloutDetectionProcessLevel.High ? 0f : null,
                tier == FalloutDetectionProcessLevel.High ? 0u : null,
                tier == FalloutDetectionProcessLevel.High ? false : null,
                tier == FalloutDetectionProcessLevel.High ? "original-high-constructor-positive-zero" : null, construction));
            if (registered)
            {
                _cohort.Add(actor, tier);
                _actors[actor] = _actors[actor] with { Registered = true };
            }
            Next();
        });
    }

    internal FalloutDetectionProcessLevel Reevaluate(FalloutActorProcessElection input)
    {
        var actor = RequireActor(input.Actor); RequireEpoch(actor, input.Epoch);
        if (actor.Level is null || actor.Retired) throw new InvalidOperationException("Retired actor has no source tier election.");
        if (string.IsNullOrWhiteSpace(input.Owner)) throw new InvalidDataException("Actor election has no actual source caller.");
        // Both actual getters retain the current class during the original
        // player's positive transition counter. This is not a menu/body flag.
        if (input.PlayerTransitionCount.Require() > 0) return actor.Level.Value;
        var has3D = _perception.RequiresNativePublication(input.Actor);
        var eligibleForLoadedTier = input.ForcedProcessing.Require() || has3D || input.RegisteredProcessingTree.Require();
        var phase = input.SourceCellPhase.Require();
        if (eligibleForLoadedTier)
        {
            if (CellEligible(phase, true)) return FalloutDetectionProcessLevel.High;
            if (CellEligible(phase, false)) return FalloutDetectionProcessLevel.MiddleHigh;
        }
        return input.CellHasExtraProcessingOwner.Require() || CellEligible(phase, false) ?
            FalloutDetectionProcessLevel.MiddleLow : FalloutDetectionProcessLevel.Low;
    }
    // Actual CELL phases 2..4 admit only the secondary query; 5..6 admit both.
    // The living CELL loader must publish its original phase. Residency is not
    // a phase producer, and no native adapter can default a resident cell to 5.
    internal static bool CellEligible(byte originalPhase, bool high) =>
        originalPhase is 5 or 6 || !high && originalPhase is 2 or 3 or 4;

    internal void RetainBoundary(FalloutFormKey actor, string owner)
    {
        Operation(actor, owner, () => { var state = RequireActor(actor); _actors[actor] = state with { Boundary = owner }; Next(); });
    }
    internal void AdmitCurrentEpoch(FalloutActorProcessElection election)
    {
        Operation(election.Actor, election.Owner, () =>
        {
            var state = RequireActor(election.Actor); RequireEpoch(state, election.Epoch);
            if (state.Construction?.Boundary() is { } constructorBoundary)
                throw new NotSupportedException(constructorBoundary);
            var selected = Reevaluate(election);
            if (state.Retired || selected != state.Level)
                throw new InvalidDataException("Current source election requires a different actual process factory.");
            _perception.AdmitCurrentSourceProcessEpoch(election.Actor, election.Epoch, selected, election.Owner);
            _actors[election.Actor] = state with { Boundary = null };
            Next();
        });
    }
    internal void Retire(FalloutFormKey actor, string owner)
    {
        Operation(actor, owner, () =>
        {
            if (actor == _player) throw new InvalidOperationException("Actual engine player cannot retire inside a live process manager.");
            var state = RequireActor(actor); if (state.Retired) return;
            if (_factory is { Phase: not FalloutActorProcessFactoryPhase.Complete })
                throw new NotSupportedException("Actor retirement cannot discard a still-owned process factory transaction.");
            if (state.Construction?.Boundary() is { } constructorBoundary)
                throw new NotSupportedException("Actor retirement cannot discard an unowned original registration: " + constructorBoundary);
            if (state.Registered && state.Level is { } tier && !_cohort.Segment(FalloutActorProcessCohort.Tier(tier)).Contains(actor))
                throw new InvalidDataException("Retired source actor lost its actual manager registration.");
            _perception.RetireActor(actor, owner);
            if (state.Registered && state.Level is { } current) _cohort.Remove(actor, current);
            _actors[actor] = state with
            {
                Epoch = _perception.ProcessEpoch(actor),
                Level = null,
                Registered = false,
                Retired = true,
                Boundary = null,
                DetectionTimer = null,
                DetectionGeneration = null,
                DetectionUpdated = null,
                LastTimerOwner = null
            };
            Next();
        });
    }
    private FalloutActorProcessRegistration RequireActor(FalloutFormKey actor)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _actors.TryGetValue(actor, out var state) ? state : throw new NotSupportedException("Actor process was never constructed: " + actor);
    }
    private void RequireEpoch(FalloutActorProcessRegistration actor, long epoch)
    {
        if (actor.Epoch != epoch || _perception.ProcessEpoch(actor.Source.Reference) != epoch)
            throw new InvalidDataException("Actor process operation belongs to a retired process epoch.");
    }
    private long Next() => _sequence = checked(_sequence + 1);
    private void Operation(FalloutFormKey? actor, string owner, Action body)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        if (_busy)
        {
            _faults.Add(new(Next(), actor, owner, "Original actor process operation reentered its transaction."));
            throw new InvalidOperationException("Actor process operation reentered its transaction.");
        }
        _busy = true;
        try { body(); }
        catch (Exception error)
        {
            var message = string.IsNullOrWhiteSpace(error.Message) ? error.GetType().Name : error.Message;
            _faults.Add(new(Next(), actor, owner, message)); throw;
        }
        finally { _busy = false; }
    }
    public void Dispose()
    {
        if (_disposed) return;
        if (_busy) throw new InvalidOperationException("Actor process manager cannot retire inside a producer callback.");
        if (_factory is { Phase: not FalloutActorProcessFactoryPhase.Complete })
            throw new NotSupportedException("Actor process retirement still owns an unfinished old/new factory lifetime.");
        _disposed = true; _actors.Clear();
    }
}
