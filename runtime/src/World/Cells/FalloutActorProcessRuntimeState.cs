using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

// Main, Player travel and Actor life are three independent source owners. A
// CELL attachment, pause, combat flag or physical query never writes them.
internal sealed partial class FalloutActorProcessRuntimeState : IDisposable
{
    internal const string Schema = "opennv-actor-process-runtime/v1";
    private readonly FalloutActorProcessRuntimeDeclaration _source;
    private readonly string _stack;
    private readonly FalloutFormKey _player;
    private readonly Func<FalloutFormKey, FalloutCombatActorIdentity> _identity;
    private readonly Guid _process = Guid.NewGuid();
    private readonly Dictionary<FalloutFormKey, FalloutActorNeutralLifeEntry> _actors = new(FalloutFormKeyComparer.Instance);
    private readonly List<FalloutMainProcessReceipt> _main = [];
    private FalloutPlayerTravelReceipt? _travel;
    private FalloutActorProcessRuntimeHandoff? _cold;
    private long _sequence, _callbackFault;
    private bool _busy, _disposed;
    private int _travelCounter;
    internal FalloutActorProcessRuntimeState(FalloutActorProcessRuntimeDeclaration source, string stack,
        FalloutFormKey player, Func<FalloutFormKey, FalloutCombatActorIdentity> identity,
        FalloutActorProcessRuntimeSnapshot? restore = null)
    {
        source.Validate(); ArgumentException.ThrowIfNullOrWhiteSpace(stack); ArgumentNullException.ThrowIfNull(identity);
        _source = source; _stack = stack; _player = player; _identity = identity;
        ConstructSourceMainFrame(); ConstructSourceFistp();
        _forced = source.InitialMainForcedProcessing; _travelCounter = source.InitialPlayerTravelCounter;
        if (restore is null) Construct(player); else Restore(restore);
    }
    internal bool HasActor(FalloutFormKey actor) => _actors.ContainsKey(actor);
    internal string? SaveBlocker => _busy ? "actual-source-process-runtime-consumer-in-flight" :
        MainScriptCallerSaveBlocker is { } scripts ? scripts :
        MainPlayerCellSaveBlocker is { } playerCell ? playerCell :
        MainUtilityCommandSaveBlocker is { } commands ? commands :
        PlatformStartupSaveBlocker is { } startup ? startup :
        MainFrameSaveBlocker is { } frame ? frame :
        _sourceFistp.SaveBlocker is { } fistp ? fistp :
        _main.FirstOrDefault(item => item.Phase != FalloutMainProcessPhase.Complete) is { } main ?
            "actual-Main-source-operation:" + main.Owner + ":" + (main.Failure ?? main.Phase.ToString()) :
        _travel is { Phase: not FalloutPlayerTravelPhase.Complete } travel ?
            "actual-Player-travel-source-operation:" + travel.Owner + ":" + (travel.Failure ?? travel.Phase.ToString()) :
        _actors.Values.FirstOrDefault(item => item.Failure is not null) is { } actor ?
            "actual-Actor-neutral-life:" + actor.Source.Reference + ":" + actor.Failure : null;
    internal object State => new
    {
        source = _source.Contract,
        process = _process,
        _sequence,
        mainForcedProcessing = _forced,
        mainWord = _mainWord,
        mainWindows = _mainWindows.ToArray(),
        mainFrameBoundary = _mainFrameBoundary,
        callingThreadFistp = _sourceFistp.State,
        scriptCaller = MainScriptCallerState,
        playerCell = MainPlayerCellState,
        utilityCommands = MainUtilityCommandState,
        platformStartup = PlatformStartupState,
        playerTravelCounter = _travelCounter,
        main = _main.ToArray(),
        travel = _travel,
        actors = _actors.Values.ToArray(),
        cold = _cold,
        saveBlocker = SaveBlocker
    };
    internal void Construct(FalloutFormKey actor)
    {
        RequireNotBusy();
        if (_actors.TryGetValue(actor, out var existing))
        {
            if (existing.Retired) throw new InvalidOperationException("A native body cannot recreate the source Actor life constructor.");
            return;
        }
        var source = Identity(actor);
        if (source.EnginePlayer != (actor == _player)) throw new InvalidDataException("Source life constructor lost Player separation.");
        _actors.Add(actor, new(source, _source.InitialNeutralLifeCode, "actual-selected-Actor-constructor-neutral-zero", Next(), false, null));
    }
    internal FalloutActorProcessFact<int> PlayerTravelCounter => new(_travelCounter,
        "actual-selected-Player-signed-travel-counter/" + _sequence, _travel?.Failure);
    internal FalloutActorProcessFact<bool> MainForcedProcessing => new(_forced,
        "actual-selected-Main-forced-processing-bit/" + _sequence,
        _main.FirstOrDefault(item => item.Phase == FalloutMainProcessPhase.Failed)?.Failure);
    internal FalloutActorProcessFact<int> Life(FalloutFormKey actor)
    {
        var state = RequireActor(actor);
        return new(state.Value, state.Owner + "/" + state.Changed,
            state.Retired ? "actual-source-Actor-lifetime-retired" : state.Failure);
    }
    internal void RetainUnownedLifeTransition(FalloutFormKey actor, string owner)
    {
        RequireNotBusy(); ArgumentException.ThrowIfNullOrWhiteSpace(owner); var state = RequireActor(actor);
        if (state.Retired) throw new InvalidOperationException("A retired source Actor cannot mutate its life input.");
        if (state.Value is null && state.Failure is not null) return;
        _actors[actor] = state with { Value = null, Owner = owner, Changed = Next(), Failure = state.Failure ?? owner };
    }
    internal void RetireActor(FalloutFormKey actor, string owner)
    {
        RequireNotBusy(); ArgumentException.ThrowIfNullOrWhiteSpace(owner); var state = RequireActor(actor);
        if (actor == _player) throw new InvalidOperationException("Player life retires with its actual world, never a cohort removal.");
        if (state.Retired) return;
        _actors[actor] = state with { Retired = true, Changed = Next(), Owner = owner };
    }
    internal Guid BeginMain(FalloutMainProcessOperation operation, string owner)
    {
        RequireMainWriterOutsideWindow(); RequireNotBusy(); ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        if (!Enum.IsDefined(operation) || _main.Any(item => item.Phase != FalloutMainProcessPhase.Complete))
            throw new InvalidOperationException("The actual Main source operation is already active or has a retained failure.");
        var identity = Guid.NewGuid(); var sequence = Next();
        _main.Add(new(identity, operation, owner, sequence, sequence, FalloutMainProcessPhase.Entered, _forced, null));
        _forced = true;
        return identity;
    }
    internal void CompleteMain(Guid invocation, string owner)
    {
        RequireMainWriterOutsideWindow(); RequireNotBusy(); var index = _main.FindIndex(item => item.Invocation == invocation);
        if (index < 0 || _main[index].Owner != owner || _main[index].Phase != FalloutMainProcessPhase.Entered)
            throw new InvalidDataException("Main completion lost its real invoking load/update owner.");
        // Called only after the actual load/update consumers returned. Failure
        // paths retain the set bit and the consumed operation; Dispose is not
        // a successful completion or an automatic flag-clear operation.
        _main[index] = _main[index] with { Phase = FalloutMainProcessPhase.ConsumersReturned, LastChanged = Next() };
        _forced = false;
        CompleteSourceMainWord(_main[index].Operation);
        _main[index] = _main[index] with { Phase = FalloutMainProcessPhase.Complete, LastChanged = Next() };
    }
    internal void FailMain(Guid invocation, string owner, Exception error)
    {
        RequireMainWriterOutsideWindow(); RequireNotBusy(); var index = _main.FindIndex(item => item.Invocation == invocation);
        if (index < 0 || _main[index].Owner != owner || _main[index].Phase == FalloutMainProcessPhase.Complete)
            throw new InvalidDataException("Main failure has no still-owned real source invocation.");
        _main[index] = _main[index] with
        {
            Phase = FalloutMainProcessPhase.Failed,
            LastChanged = Next(),
            Failure = _main[index].Failure ?? Message(error)
        };
    }
    private FalloutCombatActorIdentity Identity(FalloutFormKey actor)
    {
        RequireNotBusy(); _busy = true; var faults = _callbackFault;
        try
        {
            var source = _identity(actor); source.Validate();
            if (_callbackFault != faults) throw new InvalidOperationException("Runtime process identity callback caught a real reentry failure.");
            if (source.Reference != actor) throw new InvalidDataException("Actual process input has a foreign actor source.");
            return source;
        }
        finally { _busy = false; }
    }
    private FalloutActorNeutralLifeEntry RequireActor(FalloutFormKey actor)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _actors.TryGetValue(actor, out var state) ? state : throw new NotSupportedException("Actual Actor life constructor is absent: " + actor);
    }
    private void RequireNotBusy()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_busy)
        {
            _callbackFault = checked(_callbackFault + 1);
            throw new InvalidOperationException("Actual source process runtime owner reentered its consumer.");
        }
    }
    private long Next() => _sequence = checked(_sequence + 1);
    private static string Message(Exception error) => string.IsNullOrWhiteSpace(error.Message) ? error.GetType().Name : error.Message;
    public void Dispose()
    {
        if (_disposed) return; RequireNotBusy(); RequireMainScriptClosureBoundary(retiring: true);
        if (_platformStartupEntered is not null) throw new InvalidOperationException("Actual platform startup still owns the source Main process.");
        RetireMainUtilityCommands(); RequireMainUtilityBoundary(retiring: true);
        if (MainFrameSaveBlocker is not null || _main.Any(item => item.Phase != FalloutMainProcessPhase.Complete) || _travel is { Phase: not FalloutPlayerTravelPhase.Complete })
            throw new NotSupportedException("Source process runtime retains a live or failed Main/Player invocation.");
        RetireMainPlayerCell(); RetireStandaloneMain(); _sourceFistp.Dispose();
        _disposed = true; _actors.Clear();
    }
}
