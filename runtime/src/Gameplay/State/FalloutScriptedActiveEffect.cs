using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal enum FalloutScriptedEffectFinishCause { Condition, Expiry, SourceRemoval }
internal sealed record FalloutScriptedEffectClosedList(long Generation,
    IReadOnlyList<FalloutScriptEffectLocalCell> Locals, FalloutCompiledActiveEffectSnapshot Compiled);
internal sealed record FalloutScriptedEffectTimeline(string Schema, string SourceSha256, long InstanceGeneration,
    long EventListGeneration, uint DurationBits, uint ElapsedBits, bool ConditionsInitialized, bool Applied,
    bool Started, bool FinishApplied, bool Expired, bool Removed, ulong LastClockMutation,
    FalloutScriptedEffectFinishCause? FinishCause, string? Failure, FalloutScriptedEffectClosedList? PreviousClosedList);

internal sealed class FalloutScriptedEffectUpdate
{
    internal FalloutScriptedActiveEffect Owner { get; }
    internal FalloutScriptedEffectPulse Pulse { get; }
    internal float Seconds { get; }
    internal FalloutScriptedEffectUpdate(FalloutScriptedActiveEffect owner, FalloutScriptedEffectPulse pulse, float seconds)
    { Owner = owner; Pulse = pulse; Seconds = seconds; }
    internal void Require(FalloutCompiledActiveEffectExecution execution) => Owner.RequireUpdate(this, execution);
}

internal sealed class FalloutScriptedEffectFinish
{
    internal FalloutScriptedActiveEffect Owner { get; }
    internal FalloutScriptedEffectFinishCause Cause { get; }
    internal FalloutScriptedEffectFinish(FalloutScriptedActiveEffect owner, FalloutScriptedEffectFinishCause cause)
    { Owner = owner; Cause = cause; }
    internal void Require(FalloutCompiledActiveEffectExecution execution) => Owner.RequireFinish(this, execution);
}

// One real source effect instance may own successive real event lists. Its
// duration/elapsed survives a condition restart; source removal is terminal.
internal sealed class FalloutScriptedActiveEffect
{
    internal const string Schema = "opennv-scripted-active-effect-timeline/v1";
    private readonly FalloutPluginStack _records;
    private readonly FalloutFormKey _target;
    private readonly FalloutPluginRecord _script;
    private readonly Func<long> _nextGeneration;
    private readonly Func<FalloutCompiledActiveEffectInvocation, FalloutCompiledActiveEffectReceipt> _execute;
    private FalloutScriptedEffectTimeline _state;
    private FalloutScriptedEffectUpdate? _update;
    private FalloutScriptedEffectFinish? _finish;
    private bool _entered;
    internal FalloutScriptedEffectSource Source { get; }
    internal FalloutCompiledActiveEffectExecution Compiled { get; private set; }
    internal FalloutScriptEffectLocals Locals { get; private set; }
    internal long EventListGeneration => _state.EventListGeneration;
    internal long InstanceGeneration => _state.InstanceGeneration;
    internal bool Applied => _state.Applied && !_state.Expired && !_state.Removed;
    internal bool Started => _state.Started;
    internal bool Retired => (_state.Expired || _state.Removed) && (!_state.Started || _state.FinishApplied);
    internal string? Failure => _state.Failure;

    internal FalloutScriptedActiveEffect(FalloutPluginStack records, FalloutFormKey target,
        FalloutAbilityScript definition, long generation, ulong creationMutation, Func<long> nextGeneration,
        Func<FalloutCompiledActiveEffectInvocation, FalloutCompiledActiveEffectReceipt> execute,
        FalloutScriptedEffectTimeline? restore = null, IReadOnlyList<FalloutScriptEffectLocalCell>? locals = null,
        FalloutCompiledActiveEffectSnapshot? compiled = null)
    {
        _records = records; _target = target; _nextGeneration = nextGeneration; _execute = execute;
        Source = FalloutScriptedEffectSource.Read(records, definition); _script = records.GetEffective(definition.Script);
        if (restore is null && (locals is not null || compiled is not null) || restore is not null && (locals is null || compiled is null))
            throw new InvalidDataException("Active effect lost its complete current timeline/cells/event-list state.");
        _state = restore ?? new(Schema, Source.Identity, generation, generation, Source.DurationBits, 0,
            false, false, false, false, false, false, creationMutation, null, null, null);
        Validate(_state);
        if (_state.SourceSha256 != Source.Identity || _state.DurationBits != Source.DurationBits ||
            _state.EventListGeneration != generation)
            throw new InvalidDataException("Active effect changed its source instance/duration/event-list identity.");
        Locals = new(_script, locals);
        Compiled = new(records, target, definition, generation, _script, Locals, compiled);
        Compiled.RequireLifecycle(_state.Started, _state.Failure);
        if (_state.FinishApplied != Compiled.Finished)
            throw new InvalidDataException("Active effect lost its genuine completed Finish retirement.");
        if (_state.PreviousClosedList is { } prior)
        {
            FalloutCompiledActiveEffectExecution.Validate(prior.Compiled);
            _ = new FalloutScriptEffectLocals(_script, prior.Locals);
            if (prior.Generation >= generation || prior.Generation < _state.InstanceGeneration ||
                prior.Compiled.Generation != prior.Generation || prior.Compiled.Target != target ||
                prior.Compiled.Script != definition.Script || prior.Compiled.Spell != definition.Spell ||
                prior.Compiled.EffectOrdinal != definition.EffectOrdinal || prior.Compiled.Effect != definition.Effect ||
                prior.Compiled.ProgramSha256 != Compiled.Program.ProgramSha256 || prior.Compiled.Lifetime?.Finished != true)
                throw new InvalidDataException("Condition restart lost its actual previously retired event list.");
        }
    }

    internal void InitializeConstant(Func<FalloutCondition, float> condition)
    {
        if (!Source.Constant) throw new InvalidOperationException("Constant selection cannot apply a consumed/delivered effect.");
        RequireIdle();
        try
        {
            if (!_state.ConditionsInitialized) PollConditions(condition);
            if (Applied && !_state.Started) Start();
        }
        catch (Exception failure) { Fail(failure); throw; }
    }

    internal void Advance(FalloutScriptedEffectPulse pulse, Func<FalloutCondition, float> condition)
    {
        pulse.Require(); RequireIdle(); Source.RequireSource(_records);
        if (pulse.Mutation <= _state.LastClockMutation)
            throw new InvalidOperationException("An effect instance cannot consume a repeated gameplay-clock mutation.");
        if (Retired) return;
        _entered = true;
        _state = _state with { LastClockMutation = pulse.Mutation };
        try
        {
            var seconds = pulse.Seconds;
            var elapsed = BitConverter.UInt32BitsToSingle(_state.ElapsedBits);
            if (!_state.ConditionsInitialized || Source.Conditions.Count != 0 &&
                CrossesConditionBucket(elapsed, seconds, pulse.ConditionInterval))
                PollConditions(condition);
            // The selected source's nonpositive branch can retire a previously
            // started, condition-disabled list but does not start/advance it.
            if (seconds > 0 && !_state.Expired)
            {
                if (Applied && !_state.Started) Start();
                var delta = seconds;
                // The selected FNV consumer compares its widened sum before
                // the Float32 store; FO3 compares its SIMD Float32 sum.
                var next = pulse.CompareElapsedAsFloat32 ? (double)(elapsed + seconds) : (double)elapsed + seconds;
                if (!Source.Constant && next > Source.Duration)
                { delta = Source.Duration - elapsed; next = Source.Duration; }
                var storedElapsed = (float)next;
                if (!float.IsFinite(storedElapsed) || storedElapsed < 0 || !float.IsFinite(delta) || delta < 0)
                    throw new NotSupportedException("Active-effect elapsed/delta left its original nonnegative Float32 domain.");
                // Elapsed is committed before Update; a failed command retains
                // this store as well as its actual compiled instruction prefix.
                _state = _state with { ElapsedBits = BitConverter.SingleToUInt32Bits(storedElapsed) };
                if (Applied)
                {
                    _update = new(this, pulse, delta);
                    try { Compiled.ExecuteUpdate(_update, _execute); }
                    finally { _update = null; }
                }
                if (!Source.Constant && (Source.NoDuration || storedElapsed >= Source.Duration))
                    _state = _state with { Expired = true };
            }
            if (_state.Started && !_state.FinishApplied && (_state.Expired || !_state.Applied))
                Finish(_state.Expired ? FalloutScriptedEffectFinishCause.Expiry : FalloutScriptedEffectFinishCause.Condition);
        }
        catch (Exception failure) { Fail(failure); throw; }
        finally { _entered = false; }
    }

    internal void RemoveFromSourceSelection()
    {
        RequireIdle();
        if (_state.Removed) return;
        if (!Source.Constant)
            throw new NotSupportedException("Consumed/delivered effects need their genuine dispel/target-removal producer.");
        _entered = true;
        _state = _state with { Removed = true, Applied = false, Expired = true };
        try
        {
            if (_state.Started && !_state.FinishApplied) Finish(FalloutScriptedEffectFinishCause.SourceRemoval);
        }
        catch (Exception failure) { Fail(failure); throw; }
        finally { _entered = false; }
    }

    private void PollConditions(Func<FalloutCondition, float> condition)
    {
        var pass = FalloutCondition.AllPass(Source.Conditions, condition);
        if (pass && !_state.Applied && _state.FinishApplied && !_state.Expired)
        {
            if (!Compiled.Finished) throw new InvalidDataException("Condition restart reached an unretired original event list.");
            var prior = new FalloutScriptedEffectClosedList(_state.EventListGeneration, Locals.Capture(), Compiled.Capture());
            var next = _nextGeneration();
            if (next <= _state.EventListGeneration) throw new InvalidDataException("Condition restart reused a live event-list generation.");
            var freshLocals = new FalloutScriptEffectLocals(_script);
            var fresh = new FalloutCompiledActiveEffectExecution(_records, _target, Source.Script, next, _script, freshLocals);
            Locals = freshLocals; Compiled = fresh;
            _state = _state with { EventListGeneration = next, Started = false, FinishApplied = false,
                FinishCause = null, PreviousClosedList = prior };
        }
        _state = _state with { ConditionsInitialized = true, Applied = pass };
    }

    private void Start()
    {
        Compiled.ExecuteStart(_execute);
        _state = _state with { Started = true };
    }

    private void Finish(FalloutScriptedEffectFinishCause cause)
    {
        _finish = new(this, cause);
        _state = _state with { FinishCause = cause };
        try
        {
            Compiled.ExecuteFinish(_finish, _execute);
            _state = _state with { FinishApplied = true };
        }
        finally { _finish = null; }
    }

    internal void RequireUpdate(FalloutScriptedEffectUpdate update, FalloutCompiledActiveEffectExecution execution)
    {
        update.Pulse.Require();
        if (!_entered || !ReferenceEquals(_update, update) || !ReferenceEquals(update.Owner, this) ||
            !ReferenceEquals(Compiled, execution) || !_state.Started || _state.FinishApplied ||
            update.Pulse.Mutation != _state.LastClockMutation || !Applied)
            throw new InvalidOperationException("ScriptEffectUpdate lacks its actual instance/clock/event-list owner.");
    }

    internal void RequireFinish(FalloutScriptedEffectFinish finish, FalloutCompiledActiveEffectExecution execution)
    {
        if (!_entered || !ReferenceEquals(_finish, finish) || !ReferenceEquals(finish.Owner, this) ||
            !ReferenceEquals(Compiled, execution) || !_state.Started || _state.FinishApplied ||
            finish.Cause != _state.FinishCause || !_state.Expired && _state.Applied)
            throw new InvalidOperationException("ScriptEffectFinish lacks its actual termination/condition owner.");
    }

    private void RequireIdle()
    {
        if (_entered) throw new InvalidOperationException("An effect instance is already executing its source lifecycle.");
        if (_state.Failure is { } retained) throw new NotSupportedException(retained);
    }
    private void Fail(Exception failure) => _state = _state with { Failure = failure.Message };

    internal FalloutScriptedEffectTimeline Capture()
    {
        if (_entered) throw new NotSupportedException("An entered effect lifecycle has no suspended cold owner.");
        return _state;
    }

    internal static bool CrossesConditionBucket(float elapsed, float seconds, float interval)
    {
        if (!float.IsFinite(elapsed) || elapsed < 0 || !float.IsFinite(seconds) || seconds < 0 ||
            !float.IsFinite(interval) || interval <= 0)
            throw new InvalidDataException("Effect condition bucket lacks its original finite scalar domain.");
        var before = (float)((double)elapsed / interval);
        var after = (float)(((double)elapsed + seconds) / interval);
        if (!float.IsFinite(before) || !float.IsFinite(after))
            throw new NotSupportedException("Effect condition bucket overflow has no admitted source consumer.");
        return MathF.Floor(before) != MathF.Floor(after);
    }

    internal static void Validate(FalloutScriptedEffectTimeline state)
    {
        if (state is null || state.Schema != Schema || !FalloutAdvancementRuntimeReceipt.Digest(state.SourceSha256) ||
            state.InstanceGeneration <= 0 || state.EventListGeneration < state.InstanceGeneration ||
            !float.IsFinite(BitConverter.UInt32BitsToSingle(state.DurationBits)) || BitConverter.UInt32BitsToSingle(state.DurationBits) < 0 ||
            !float.IsFinite(BitConverter.UInt32BitsToSingle(state.ElapsedBits)) || BitConverter.UInt32BitsToSingle(state.ElapsedBits) < 0 ||
            !state.ConditionsInitialized && (state.Applied || state.Started || state.FinishApplied) ||
            state.FinishApplied && !state.Started || state.Removed && (!state.Expired || state.Applied) ||
            state.FinishApplied && state.FinishCause is null || state.FinishCause is { } cause && !Enum.IsDefined(cause) ||
            state.Failure is { Length: 0 } || state.FinishCause is not null && !state.FinishApplied && state.Failure is null ||
            (state.PreviousClosedList is null) != (state.InstanceGeneration == state.EventListGeneration))
            throw new InvalidDataException("Current active effect lost its genuine elapsed/condition/event-list prefix.");
    }
}
