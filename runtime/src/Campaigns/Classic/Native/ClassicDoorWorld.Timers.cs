namespace OpenNV.Runtime.Campaigns.Classic.Native;

internal sealed record ClassicDoorClockSave(string Map, ClassicIntTimerState Timers, double Elapsed, string? Failure);

internal sealed partial class ClassicDoorWorld
{
    // FO1 executable initialization and ordinary script tick observation. FO2
    // keeps its timer admission closed until its clock lifecycle is established.
    private ClassicIntTimerContract? TimerContract => player.Choice.Campaign == "fallout-1" ? new(10) : null;
    private ClassicIntTimerState _timers = ClassicIntTimerState.Initial with
    { CurrentTick = player.Choice.Campaign == "fallout-1" ? 264600 : 0 };
    private double _timerElapsed;
    private string? _timerFailure;

    internal void Leave(string destination)
    {
        if (destination == player.MapPath) return;
        if (_timerFailure is not null) throw new NotSupportedException(_timerFailure);
        // Source transition cleanup removes script events. Changing elevation
        // within the same MAP does not leave that map.
        _timers = _timers with { Pending = [] };
    }

    private ClassicDoorClockSave SaveClock() => new(player.MapPath, _timers, _timerElapsed, _timerFailure);

    private void RestoreClock(ClassicDoorClockSave? saved)
    {
        // Old native saves could not admit scheduled events.
        if (saved is null) return;
        saved.Timers.Validate();
        if (saved.Map != player.MapPath || !double.IsFinite(saved.Elapsed) || saved.Elapsed < 0 || saved.Elapsed >= .1 ||
            TimerContract is null && (saved.Timers.CurrentTick != 0 || saved.Timers.Pending.Count != 0 ||
                saved.Timers.NextSequence != 0 || saved.Elapsed != 0 || saved.Failure is not null))
            throw new InvalidDataException("Saved script clock no longer matches its active campaign/map.");
        foreach (var timer in saved.Timers.Pending)
            if (!_entries.TryGetValue(Key(saved.Map, timer.TargetHandle - 1), out var target) || target.Definition.Program is null)
                throw new InvalidDataException("Saved timer has no source-bound native event owner.");
        _timers = saved.Timers; _timerElapsed = saved.Elapsed; _timerFailure = saved.Failure;
        if (_timerFailure is not null) _pendingMessages.Enqueue(_timerFailure);
    }

    private void AdvanceScriptTime(double delta)
    {
        if (TimerContract is null || _timerFailure is not null) return;
        _timerElapsed += delta;
        if (_timerElapsed < .1 - 1e-9) return;
        // The original ordinary update advances one tick after the elapsed-time
        // threshold and resets its wall-clock marker; it does not catch up a
        // stalled frame by advancing several seconds of game time.
        _timerElapsed = 0;
        try
        {
            var through = checked(_timers.CurrentTick + 1);
            var budget = 100000;
            while (_timers.Pending.Count != 0 && _timers.Pending[0].DueTick <= through)
            {
                var delivery = ClassicIntTimerOwner.TakeNextDue(_timers, through);
                var entry = _entries[Key(player.MapPath, delivery.Event.TargetHandle - 1)];
                if (entry.Failure is not null) throw new NotSupportedException(entry.Failure);
                if (entry.State is null || entry.Definition.Program is null ||
                    !entry.Definition.Program.Procedures.ContainsKey("timed_event_p_proc"))
                    throw new NotSupportedException("Scheduled source object has no admitted timed-event procedure.");
                var execution = Execute(entry, "timed_event_p_proc", 22, timerState: delivery.State,
                    fixedParameter: delivery.Event.FixedParameter, budget: budget);
                Validate(execution.Result, player.MapPath);
                budget -= Math.Max(1, execution.Result.ExecutedInstructions);
                Commit(entry, execution);
            }
            _timers = ClassicIntTimerOwner.AdvanceIdle(_timers, through);
        }
        catch (Exception error) when (error is InvalidOperationException or InvalidDataException or NotSupportedException or FileNotFoundException or OverflowException)
        {
            // Keep the failed event and freeze this clock rather than losing
            // work, retrying each frame or publishing a partial callback.
            _timerFailure = $"Script timer stopped on {player.MapPath}: {error.Message}";
            _pendingMessages.Enqueue(_timerFailure);
        }
    }

    private sealed class UseTransaction(ClassicDoorWorld owner, string map,
        IReadOnlyDictionary<string, ClassicIntProcedureState>? prior) : IClassicIntObjectUseDispatcher
    {
        internal Dictionary<string, ClassicIntProcedureState> States { get; } = prior is null ? [] : new(prior);
        private int _depth;

        public ClassicIntProcedureResult Use(int sourceObject, int targetObject,
            ClassicIntProcedureResult current, int remainingInstructionBudget)
        {
            if (remainingInstructionBudget <= 0 || _depth >= 128)
                throw new InvalidOperationException("Classic INT nested use exceeded its execution budget.");
            var sourceKey = Key(map, sourceObject - 1);
            var targetKey = Key(map, targetObject - 1);
            if (!owner._entries.ContainsKey(sourceKey) || !owner._entries.TryGetValue(targetKey, out var target))
                throw new NotSupportedException("Script use target has no native interaction owner.");
            if (target.Failure is not null) throw new NotSupportedException(target.Failure);
            if (target.Pose.Direction != 0) throw new NotSupportedException("Script use requires an unbound overlapping door animation.");
            States[sourceKey] = current.State;
            var state = States.GetValueOrDefault(targetKey) ?? target.State;
            if (target.Definition.Program is not null && state is null)
                throw new NotSupportedException("Script use target has not completed source initialization.");
            state ??= current.State with { ProgramVariables = new Dictionary<int, int>(), ScriptLocalVariables = new Dictionary<int, int>() };
            state = state with
            {
                GlobalVariables = current.State.GlobalVariables,
                MapVariables = current.State.MapVariables,
                ValueStack = [],
                LocalVariables = new Dictionary<int, int>()
            };
            var world = current.WorldObjects with { ScriptOverrides = false };
            var result = new ClassicIntProcedureResult(state, 0, 0, [], [], world) { ExternalVariables = current.ExternalVariables, ExternalOwners = current.ExternalOwners };
            _depth++;
            try
            {
                if (target.Definition.Program?.Procedures.ContainsKey("use_p_proc") == true)
                    result = owner.Invoke(target with { State = state }, "use_p_proc", 6, world,
                        current.ExternalVariables, this, null, sourceObject, remainingInstructionBudget, current.ExternalOwners);
                if (!result.WorldObjects.ScriptOverrides)
                {
                    var door = result.WorldObjects.Doors[targetObject];
                    var canToggle = !door.Locked;
                    if (canToggle && door.Open)
                    {
                        try { owner.CheckClear(target.Definition.Object); }
                        catch (InvalidOperationException) { canToggle = false; }
                    }
                    if (canToggle)
                    {
                        var doors = new Dictionary<int, ClassicIntDoorObjectState>(result.WorldObjects.Doors)
                        { [targetObject] = door with { Open = !door.Open } };
                        result = result with { WorldObjects = result.WorldObjects with { Doors = doors } };
                    }
                }
                var caller = States[sourceKey];
                return current with
                {
                    State = current.State with
                    {
                        ProgramVariables = caller.ProgramVariables,
                        ScriptLocalVariables = caller.ScriptLocalVariables,
                        GlobalVariables = result.State.GlobalVariables,
                        MapVariables = result.State.MapVariables,
                        RandomState = result.State.RandomState
                    },
                    ExecutedInstructions = checked(current.ExecutedInstructions + result.ExecutedInstructions),
                    WorldObjects = result.WorldObjects with { ScriptOverrides = current.WorldObjects.ScriptOverrides },
                    ExternalVariables = result.ExternalVariables,
                    ExternalOwners = result.ExternalOwners,
                    MessageEffects = current.MessageEffects.Concat(result.MessageEffects).ToArray(),
                    SoundEffects = current.SoundEffects.Concat(result.SoundEffects).ToArray()
                };
            }
            finally { _depth--; }
        }
    }
}
