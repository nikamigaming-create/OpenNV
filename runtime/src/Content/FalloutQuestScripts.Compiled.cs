using System.Security.Cryptography;
using System.Text.Json;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutCompiledQuestPending(ulong Dispatch, bool Cadenced, long ClockInvocation,
    bool GameMode, IReadOnlyList<uint>? Menus, double Seconds, IReadOnlyList<int> Events, int EventIndex,
    FalloutCompiledCursorSnapshot? Cursor, IReadOnlyList<FalloutCompiledSliceReceipt> CompletedEvents,
    FalloutCompiledSliceReceipt? LastSlice, string? AdmissionError = null);
internal sealed record FalloutCompiledQuestSnapshot(string Schema, int DecoderVersion,
    string QuestSha256, string RecordSha256, string ScopeSha256, string ProgramSha256,
    long InitializationOrdinal, uint InitialPhaseBits, uint IntervalBits, bool Claimed,
    ulong Dispatches, ulong CompletedDispatches, bool? LastCompletedGameMode, IReadOnlyList<FalloutCompiledSliceReceipt> LastCompletedEvents,
    FalloutCompiledQuestPending? Pending);

internal sealed partial class FalloutQuestScripts
{
    internal const int CompiledSchedulingVersion = 1;
    internal const string CompiledSchedulingSchema = "opennv-compiled-quest-recurrence/v1";
    private sealed class CompiledInstance(FalloutPluginRecord quest, FalloutCompiledScriptProgram program,
        FalloutQuestScriptDefinition definition, FalloutQuestScriptClock clock, bool claimed)
    {
        internal FalloutPluginRecord Quest { get; } = quest;
        internal FalloutCompiledScriptProgram Program { get; } = program;
        internal FalloutQuestScriptDefinition Definition { get; } = definition;
        internal FalloutQuestScriptClock Clock { get; } = clock;
        internal bool Claimed { get; } = claimed;
        internal long Executions;
        internal string? Error;
        internal ulong Dispatches, CompletedDispatches;
        internal bool? LastCompletedGameMode;
        internal IReadOnlyList<FalloutCompiledSliceReceipt> LastCompleted = [];
        internal FalloutCompiledQuestPending? Pending;
        internal string? UnownedFailure;
    }
    private readonly List<object> _schedule = [];
    private readonly List<CompiledInstance> _compiledInstances = [];
    private bool _compiledExecuting, _restored;

    private void RegisterCompiled(FalloutPluginRecord quest, FalloutCompiledScriptProgram program,
        FalloutQuestScriptDefinition definition, FalloutQuestScriptClock clock, bool claimed)
    {
        FalloutQuestScriptAuthority.RequireRecurringProgram(program);
        var instance = new CompiledInstance(quest, program, definition, clock, claimed);
        _compiledInstances.Add(instance); _schedule.Add(instance);
    }

    private static uint Bits(float value) => unchecked((uint)BitConverter.SingleToInt32Bits(value));
    private static string QuestHash(FalloutPluginRecord quest) => Convert.ToHexString(SHA256.HashData(quest.ReadData())).ToLowerInvariant();
    private static FalloutCompiledCursorSnapshot CopyCursor(FalloutCompiledCursorSnapshot cursor) =>
        cursor with { Branches = cursor.Branches.ToArray() };
    private static FalloutCompiledSliceReceipt CopyReceipt(FalloutCompiledSliceReceipt receipt) => receipt with { Cursor = CopyCursor(receipt.Cursor) };
    private static FalloutCompiledQuestPending? CopyPending(FalloutCompiledQuestPending? pending) => pending is null ? null : pending with
    {
        Menus = pending.Menus?.ToArray(),
        Events = pending.Events.ToArray(),
        Cursor = pending.Cursor is null ? null : CopyCursor(pending.Cursor),
        CompletedEvents = pending.CompletedEvents.Select(CopyReceipt).ToArray(),
        LastSlice = pending.LastSlice is null ? null : CopyReceipt(pending.LastSlice)
    };
    private FalloutCompiledQuestSnapshot CaptureCompiled(CompiledInstance instance) => new(
        CompiledSchedulingSchema, FalloutCompiledScriptProgram.DecoderVersion, QuestHash(instance.Quest),
        instance.Program.Scope.RecordSha256, instance.Program.Scope.ScopeSha256, instance.Program.ProgramSha256,
        instance.Definition.InitializationOrdinal, Bits(instance.Definition.InitialPhase), Bits(instance.Clock.Interval), instance.Claimed,
        instance.Dispatches, instance.CompletedDispatches, instance.LastCompletedGameMode, instance.LastCompleted.Select(CopyReceipt).ToArray(), CopyPending(instance.Pending));

    private IReadOnlyList<FalloutQuestScriptSnapshot> CaptureSchedule()
    {
        if (_compiledExecuting) throw new NotSupportedException("Saving an executing compiled recurrence requires its retired instruction slice.");
        if (_compiledInstances.FirstOrDefault(instance => instance.UnownedFailure is not null) is { } refused)
            throw new NotSupportedException("Saving compiled recurrence requires its actual shared prefix evidence: " + refused.UnownedFailure);
        var result = _schedule.Select(entry => entry switch
        {
            Instance source => new FalloutQuestScriptSnapshot(source.Quest.FormKey, source.Script.FormKey,
                source.Clock.Remaining, source.Executions, source.Error, source.Clock.Capture(), source.PendingCommand, source.Continuations.ToArray()),
            CompiledInstance compiled => new FalloutQuestScriptSnapshot(compiled.Quest.FormKey, compiled.Program.Source.FormKey,
                compiled.Clock.Remaining, compiled.Executions, compiled.Error, compiled.Clock.Capture(), Compiled: CaptureCompiled(compiled)),
            _ => throw new InvalidOperationException("Quest scheduling owner is unknown.")
        }).ToArray();
        foreach (var instance in _compiledInstances)
            ValidateCompiledRestore(instance, result.Single(state => state.Quest == instance.Quest.FormKey), CompiledSchedulingVersion);
        ValidateCompiledClockGroups(result.ToDictionary(state => state.Quest));
        return result;
    }

    private void ValidateCompiledClockGroups(IReadOnlyDictionary<FalloutFormKey, FalloutQuestScriptSnapshot> states)
    {
        foreach (var group in _compiledInstances.GroupBy(instance => instance.Program.Source.FormKey))
        {
            var saved = group.Select(instance => states[instance.Quest.FormKey]).ToArray();
            if (saved.Sum(state => state.Executions) != saved[0].Clock!.Invocations ||
                saved.Count(state => state.Compiled!.Pending is not null) > 1)
                throw new InvalidDataException("Saved shared SCPT clock has contradictory completed/active quest invocations.");
        }
    }

    private bool ClockBusy(CompiledInstance instance) => _compiledInstances.Any(other =>
        !ReferenceEquals(instance, other) && ReferenceEquals(instance.Clock, other.Clock) && other.Pending is not null);
    private bool SameContext(FalloutCompiledQuestPending pending) => pending.GameMode == Menus.GameMode &&
        (pending.Menus is null ? Menus.Codes is null : Menus.Codes is { } codes && pending.Menus.SequenceEqual(codes));

    private void AdvanceCompiled(CompiledInstance instance, double seconds, bool gameMode, bool execute,
        FalloutQuestScriptHost? host, bool immediateMenu = false)
    {
        if (_compiledExecuting) throw new InvalidOperationException("Quest recurrence cannot reenter an executing shared frame.");
        if (instance.Error is not null || !_quests.IsRunning(instance.Quest.FormKey) || ClockBusy(instance)) return;
        // An observational/title frame cannot create byte authority or consume
        // a compiled cadence. A mode/menu mismatch cannot resume old work.
        if (!execute || gameMode != Menus.GameMode || instance.Pending is { } held && !SameContext(held)) return;
        try { if (host?.CanContinueCompiled?.Invoke(gameMode) == false) return; }
        catch (Exception error)
        {
            instance.Error = instance.UnownedFailure = error.Message;
            _unbound[instance.Quest.FormKey] = error.Message; return;
        }
        if (instance.Pending is null)
        {
            if (!immediateMenu && !instance.Clock.Advance((float)seconds)) return;
            var events = Enumerable.Range(0, instance.Program.Events.Count).Where(index =>
                instance.Program.Events[index].Event == (gameMode ? 0 : 1)).ToArray();
            instance.Dispatches = checked(instance.Dispatches + 1);
            instance.Pending = new(instance.Dispatches, !immediateMenu,
                checked(instance.Clock.Invocations + (immediateMenu ? 0 : 1)), gameMode, Menus.Codes?.ToArray(),
                instance.Clock.Elapsed, events, 0, null, [], null);
        }
        var pending = instance.Pending!;
        _compiledExecuting = true;
        try
        {
            var executor = host?.ExecuteCompiledProgram ?? throw new NotSupportedException(
                "Compiled quest recurrence has no shared ExecuteProgram/result authority; source/custom void fallback is refused.");
            while (pending.EventIndex < pending.Events.Count)
            {
                var ordinal = pending.Events[pending.EventIndex];
                var flow = FalloutCompiledControlFlow.Read(instance.Program.EventInstructions(instance.Program.Events[ordinal]), instance.Program.Events[ordinal].End);
                var cursor = new FalloutCompiledExecutionCursor(pending.Cursor ?? flow.InitialCursor);
                flow.ValidateCursor(cursor.State);
                FalloutCompiledSliceReceipt receipt;
                try
                {
                    receipt = executor(instance.Quest, instance.Program, ordinal, cursor, pending.Seconds,
                        () => SameContext(pending) && host?.CanContinueCompiled?.Invoke(pending.GameMode) != false);
                    receipt.Require(instance.Program, instance.Quest.FormKey);
                    if (receipt.EventOrdinal != ordinal)
                        throw new InvalidDataException("Compiled host receipt belongs to another original event ordinal.");
                    ScriptManualSaves.RequireCurrentCompiledReceipt(receipt);
                    if (JsonSerializer.Serialize(receipt.Cursor) != JsonSerializer.Serialize(cursor.State))
                        throw new InvalidDataException("Compiled host receipt differs from its actual shared cursor.");
                }
                catch (FalloutCompiledInvocationFailure failure)
                {
                    try
                    {
                        failure.Receipt.Require(instance.Program, instance.Quest.FormKey);
                        if (failure.Receipt.EventOrdinal != ordinal)
                            throw new InvalidDataException("Compiled failure receipt belongs to another original event ordinal.");
                        if (failure.Receipt.Invocation != 0) ScriptManualSaves.RequireCurrentCompiledReceipt(failure.Receipt);
                        if (JsonSerializer.Serialize(failure.Receipt.Cursor) != JsonSerializer.Serialize(cursor.State))
                            throw new InvalidDataException("Compiled failure receipt differs from its actual shared cursor.");
                    }
                    catch (Exception error)
                    {
                        // Failure-envelope validation happens inside this catch;
                        // it cannot fall through as an admission with no effects.
                        instance.UnownedFailure = error.Message; throw;
                    }
                    pending = pending with { Cursor = failure.Receipt.Cursor, LastSlice = failure.Receipt };
                    instance.Pending = pending; throw;
                }
                catch (Exception error)
                {
                    instance.UnownedFailure = error.Message; throw;
                }
                pending = pending with { Cursor = receipt.Cursor, LastSlice = receipt };
                instance.Pending = pending;
                if (receipt.Disposition == "suspended") return;
                if (receipt.Disposition != "completed") throw new InvalidDataException("Compiled callback did not complete or suspend its actual lease.");
                pending = pending with
                {
                    EventIndex = pending.EventIndex + 1,
                    Cursor = null,
                    CompletedEvents = [.. pending.CompletedEvents, receipt],
                    LastSlice = null
                };
                instance.Pending = pending;
            }
            if (pending.Cadenced)
            {
                // Only actual completed blocks can retire this due cadence.
                // An empty selected range records no instruction receipt.
                instance.Clock.CompleteInvocation(); ++instance.Executions;
            }
            instance.LastCompleted = pending.CompletedEvents.ToArray();
            instance.LastCompletedGameMode = pending.GameMode;
            instance.CompletedDispatches = checked(instance.CompletedDispatches + 1);
            instance.Pending = null;
        }
        catch (Exception error)
        {
            instance.Error = error.Message;
            // Admission without a lease is distinguished from an execution
            // failure. Discarded/malformed callback evidence cannot be saved.
            if (instance.Pending?.LastSlice is null && instance.Pending?.CompletedEvents.Count == 0)
                instance.Pending = instance.Pending! with { AdmissionError = error.Message };
            _unbound[instance.Quest.FormKey] = error.Message;
        }
        finally { _compiledExecuting = false; }
    }

    private void ValidateCompiledRestore(CompiledInstance instance, FalloutQuestScriptSnapshot state, int version)
    {
        if (version != CompiledSchedulingVersion || state.Compiled is not { } saved || state.PendingCommand is not null ||
            state.Continuations is { Count: > 0 })
            throw new NotSupportedException("Legacy quest scheduling/source cursors have no compiled event authority; prefix replay is refused.");
        var identity = CaptureCompiled(instance);
        if (saved.Schema != identity.Schema || saved.DecoderVersion != identity.DecoderVersion || saved.QuestSha256 != identity.QuestSha256 ||
            saved.RecordSha256 != identity.RecordSha256 || saved.ScopeSha256 != identity.ScopeSha256 || saved.ProgramSha256 != identity.ProgramSha256 ||
            saved.InitializationOrdinal != identity.InitializationOrdinal || saved.InitialPhaseBits != identity.InitialPhaseBits ||
            saved.IntervalBits != identity.IntervalBits || saved.Claimed != instance.Claimed || state.Script != instance.Program.Source.FormKey ||
            saved.CompletedDispatches > saved.Dispatches || saved.Dispatches - saved.CompletedDispatches != (saved.Pending is null ? 0ul : 1ul) ||
            saved.LastCompletedEvents is null)
            throw new InvalidDataException("Saved compiled quest definition/clock/dispatch identity differs from its original owner.");
        instance.Clock.Validate(state.Clock!);
        foreach (var receipt in saved.LastCompletedEvents)
        {
            if (receipt is null) throw new InvalidDataException("Saved completed event receipt is absent.");
            receipt.Require(instance.Program, instance.Quest.FormKey);
            if (receipt.Disposition != "completed") throw new InvalidDataException("Completed quest dispatch contains an unfinished event.");
        }
        if ((saved.CompletedDispatches > 0) != (saved.LastCompletedGameMode is not null) ||
            saved.CompletedDispatches == 0 && saved.LastCompletedEvents.Count != 0)
            throw new InvalidDataException("Saved last dispatch mode has no actual completed scheduling decision.");
        if (saved.LastCompletedGameMode is { } lastMode && !saved.LastCompletedEvents.Select(receipt => receipt.EventOrdinal).SequenceEqual(
            Enumerable.Range(0, instance.Program.Events.Count).Where(index => instance.Program.Events[index].Event == (lastMode ? 0 : 1))))
            throw new InvalidDataException("Saved completed recurrence differs from its original event selection/order.");
        if (saved.Pending is not { } pending)
        {
            if (state.Error is not null) throw new InvalidDataException("Compiled quest error has no closed actual prefix.");
            return;
        }
        if (pending.AdmissionError is not null && string.IsNullOrWhiteSpace(pending.AdmissionError))
            throw new InvalidDataException("Saved compiled admission refusal has no failure detail.");
        if (pending.Dispatch != saved.Dispatches || pending.ClockInvocation != checked(state.Clock!.Invocations + (pending.Cadenced ? 1 : 0)) ||
            pending.Cadenced && state.Clock.Remaining > 0 || !double.IsFinite(pending.Seconds) || pending.Seconds < 0 ||
            pending.Seconds != state.Clock.Elapsed || pending.Events is null || pending.CompletedEvents is null ||
            pending.EventIndex < 0 || pending.EventIndex > pending.Events.Count ||
            pending.CompletedEvents.Count != pending.EventIndex || pending.GameMode && pending.Menus is { Count: > 0 } ||
            pending.Menus is { } menus && (menus.Any(code => code == 0) || !menus.SequenceEqual(menus.Distinct().Order())))
            throw new InvalidDataException("Saved compiled dispatch context/cadence/prefix is invalid.");
        var events = Enumerable.Range(0, instance.Program.Events.Count).Where(index =>
            instance.Program.Events[index].Event == (pending.GameMode ? 0 : 1));
        if (!events.SequenceEqual(pending.Events)) throw new InvalidDataException("Saved compiled event selection differs from its original ordinal order.");
        for (var index = 0; index < pending.EventIndex; ++index)
        {
            var receipt = pending.CompletedEvents[index];
            if (receipt is null) throw new InvalidDataException("Saved event prefix receipt is absent.");
            receipt.Require(instance.Program, instance.Quest.FormKey);
            if (receipt.EventOrdinal != pending.Events[index] || receipt.Disposition != "completed")
                throw new InvalidDataException("Saved compiled prefix has an uncompleted/mis-scoped event receipt.");
        }
        if (pending.Cursor is { } cursor)
        {
            if (pending.EventIndex >= pending.Events.Count || pending.LastSlice is not { } receipt ||
                receipt.EventOrdinal != pending.Events[pending.EventIndex] || JsonSerializer.Serialize(receipt.Cursor) != JsonSerializer.Serialize(cursor))
                throw new InvalidDataException("Saved compiled cursor has no matching actual event receipt.");
            receipt.Require(instance.Program, instance.Quest.FormKey);
            if (receipt.Disposition is not ("suspended" or "closed-failure" or "admission-refusal") || receipt.Error != state.Error)
                throw new InvalidDataException("Saved compiled stopped/suspended receipt differs from its retained failure.");
        }
        else if (pending.LastSlice is not null || state.Error is not null && pending.AdmissionError != state.Error)
            throw new InvalidDataException("Saved compiled refusal has no owned admission or instruction prefix.");
        if (pending.AdmissionError is not null && (pending.Cursor is not null || pending.EventIndex != 0 || state.Error != pending.AdmissionError))
            throw new InvalidDataException("Saved compiled admission refusal contains an executed prefix.");
    }

    private void RestoreCompiled(CompiledInstance instance, FalloutQuestScriptSnapshot state)
    {
        var saved = state.Compiled!;
        instance.Clock.Restore(state.Clock!); instance.Executions = state.Executions; instance.Error = state.Error;
        instance.Dispatches = saved.Dispatches; instance.CompletedDispatches = saved.CompletedDispatches;
        instance.LastCompleted = saved.LastCompletedEvents.Select(CopyReceipt).ToArray(); instance.LastCompletedGameMode = saved.LastCompletedGameMode; instance.Pending = CopyPending(saved.Pending);
        if (state.Error is not null) _unbound[instance.Quest.FormKey] = state.Error;
    }
}
