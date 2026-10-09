using System.Buffers.Binary;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutChallengeGameModeFilter(double Scalar, bool MenuMode,
    FalloutChallengeGameModeObservation? Predicate)
{
    internal void Validate()
    {
        if (Scalar is not (0 or 1) || MenuMode && (Scalar != 0 || Predicate is not null) ||
            !MenuMode && (Predicate is null || Predicate.Ordinal <= 0 ||
                !FalloutAdvancementRuntimeReceipt.Digest(Predicate.EngineSha256) ||
                !FalloutAdvancementRuntimeReceipt.Digest(Predicate.ProducerSha256) ||
                Scalar != (Predicate.Allows ? 1 : 0)))
            throw new InvalidDataException("Immediate GameMode lost its actual menu-short-circuit or cached Main scalar.");
    }
}
internal sealed record FalloutChallengeRewardEventSnapshot(int Ordinal, string EventScopeSha256,
    bool Attempted, bool Filtered, FalloutCompiledCursorSnapshot Cursor,
    FalloutCompiledSliceReceipt? Receipt, string? FailureType, string? Error)
{
    public FalloutChallengeGameModeFilter? GameMode { get; init; }
}
internal sealed record FalloutChallengeRewardSnapshot(long DispatchOrdinal, FalloutFormKey Challenge,
    FalloutFormKey Program, FalloutFormKey Target, string RecordSha256, string ScopeSha256,
    string ProgramSha256, IReadOnlyList<FalloutScriptEffectLocalCell> Locals,
    IReadOnlyList<FalloutChallengeRewardEventSnapshot> Events, string Disposition,
    string? FailureType, string? Error)
{
    internal void Validate()
    {
        if (DispatchOrdinal <= 0 || Challenge.ObjectId == 0 || Program.ObjectId == 0 || Target.ObjectId != 0x14 ||
            !FalloutAdvancementRuntimeReceipt.Digest(RecordSha256) || !FalloutAdvancementRuntimeReceipt.Digest(ScopeSha256) ||
            !FalloutAdvancementRuntimeReceipt.Digest(ProgramSha256) || Locals is null || Events is null ||
            Disposition is not ("completed" or "authored-empty" or "closed-failure") ||
            (Disposition == "closed-failure") != (Error is not null) || (Error is null) != (FailureType is null))
            throw new InvalidDataException("Immediate challenge script lost its original instance/source/failure identity.");
        for (var ordinal = 0; ordinal < Events.Count; ++ordinal)
        {
            var row = Events[ordinal];
            if (row is null || row.Ordinal != ordinal || !FalloutAdvancementRuntimeReceipt.Digest(row.EventScopeSha256) ||
                row.Cursor is null || row.Cursor.Branches is null || !row.Attempted && (row.Filtered || row.Receipt is not null || row.Error is not null) ||
                row.Filtered && (row.Receipt is not null || row.GameMode?.Scalar != 0) ||
                !row.Attempted && row.GameMode is not null || row.GameMode?.Scalar == 0 && !row.Filtered ||
                (row.Error is null) != (row.FailureType is null) ||
                row.Receipt is { } receipt && (receipt.Caller != Target || receipt.Program != Program ||
                    receipt.RecordSha256 != RecordSha256 || receipt.ScopeSha256 != ScopeSha256 || receipt.ProgramSha256 != ProgramSha256 ||
                    receipt.EventScopeSha256 != row.EventScopeSha256 || receipt.EventOrdinal != ordinal || receipt.Event != 0 ||
                    receipt.Error != row.Error || receipt.Disposition == "suspended" ||
                    !FalloutChallengeRewardInvocation.SameCursor(receipt.Cursor, row.Cursor) ||
                    receipt.Invocation != 0 && row.GameMode?.Scalar != 1) ||
                Disposition == "completed" && (!row.Attempted || !row.Filtered && row.Receipt?.Disposition != "completed" || row.Error is not null))
                throw new InvalidDataException("Immediate challenge event lost its genuine filter/cursor/retired lease.");
            row.GameMode?.Validate();
        }
        if (Disposition == "authored-empty" && Events.Count != 0 || Disposition == "completed" && Events.Count == 0)
            throw new InvalidDataException("Authored empty challenge script invented executable events.");
    }

    internal void RequireSource(FalloutPluginStack records)
    {
        Validate();
        if (records.RuntimeFormId(Target) != 0x14) throw new InvalidDataException("Challenge reward target is not the genuine engine player.");
        var source = records.GetEffective(Program);
        var program = FalloutCompiledScriptProgram.Read(source, FalloutScriptScope.Standalone(source), standalone: true);
        if (program.ScriptType != 0 || program.CompiledFlag != 1 || program.Scope.RecordSha256 != RecordSha256 ||
            program.Scope.ScopeSha256 != ScopeSha256 || program.ProgramSha256 != ProgramSha256 || program.Events.Count != Events.Count ||
            Disposition == "authored-empty" && program.CodeBytes != 0)
            throw new InvalidDataException("Saved immediate challenge script differs from its original object SCPT scope.");
        _ = new FalloutScriptEffectLocals(source, Locals);
        for (var ordinal = 0; ordinal < Events.Count; ++ordinal)
        {
            var block = program.Events[ordinal]; FalloutChallengeRewardInvocation.RequireBlock(block);
            var flow = FalloutCompiledControlFlow.Read(program.EventInstructions(block), block.End);
            var row = Events[ordinal]; flow.ValidateCursor(row.Cursor);
            if (row.EventScopeSha256 != FalloutCompiledSliceReceipt.EventScope(program, ordinal) ||
                (!row.Attempted || row.Filtered) && !FalloutChallengeRewardInvocation.SameCursor(row.Cursor, flow.InitialCursor))
                throw new InvalidDataException("Saved challenge script filter/event scope consumed an unentered instruction.");
            row.Receipt?.Require(program, Target);
        }
    }
}

// Fresh event-list cells belong to this actual synchronous completion, rather
// than a fabricated placed reference or a reused actor's attached script.
internal sealed class FalloutChallengeRewardInvocation : IFalloutCompiledEventLocalAuthority
{
    internal FalloutChallenges Owner { get; }
    internal long DispatchOrdinal { get; }
    internal FalloutFormKey Challenge { get; }
    internal FalloutCompiledScriptProgram Program { get; }
    public FalloutScriptEffectLocals Locals { get; }
    internal FalloutFormKey Target { get; private set; }
    private readonly FalloutChallengeRewardEventSnapshot[] _events;
    private FalloutCompiledExecutionCursor? _activeCursor;
    private int _activeOrdinal = -1;
    private string _disposition = "prepared";
    private string? _failureType, _error;
    internal bool ActiveCompiledInvocation { get; private set; }

    internal FalloutChallengeRewardInvocation(FalloutChallenges owner, long dispatchOrdinal,
        FalloutFormKey challenge, FalloutPluginRecord script)
    {
        Owner = owner; DispatchOrdinal = dispatchOrdinal; Challenge = challenge;
        Target = owner.EnginePlayer;
        Program = FalloutCompiledScriptProgram.Read(script, FalloutScriptScope.Standalone(script), standalone: true);
        if (Program.ScriptType != 0 || Program.CompiledFlag != 1)
            throw new NotSupportedException("Immediate challenge completion requires its original compiled object SCPT.");
        Locals = new(script);
        _events = Program.Events.Select((block, ordinal) =>
        {
            RequireBlock(block);
            var flow = FalloutCompiledControlFlow.Read(Program.EventInstructions(block), block.End);
            return new FalloutChallengeRewardEventSnapshot(ordinal, FalloutCompiledSliceReceipt.EventScope(Program, ordinal),
                false, false, flow.InitialCursor, null, null, null);
        }).ToArray();
    }
    internal static void RequireBlock(FalloutCompiledEvent block)
    {
        if (block.Event != 0) throw new NotSupportedException("Immediate challenge script event needs its original independent event filter.");
        if (!block.Parameters.IsEmpty && !(block.Parameters.Length == 2 && BinaryPrimitives.ReadUInt16LittleEndian(block.Parameters.Span) == 0))
            throw new NotSupportedException("Immediate challenge GameMode arguments have no original parameter producer.");
    }
    internal void Begin(FalloutPluginStack records)
    {
        Owner.RequireReward(this);
        if (_disposition != "prepared" || _events.Any(row => row.Attempted))
            throw new InvalidOperationException("An attempted challenge immediate script cannot be replayed.");
        Target = records.RuntimeFormKey(0x14);
        Program.Scope.RequireSource(records.GetEffective(Program.Source.FormKey));
    }
    internal void AuthoredEmpty()
    {
        if (Program.CodeBytes != 0 || _events.Length != 0 || Target.ObjectId != 0x14)
            throw new NotSupportedException("An eventless nonempty immediate program has no instruction-owner disposition.");
        _disposition = "authored-empty";
    }
    internal FalloutCompiledExecutionCursor EnterEvent(int ordinal)
    {
        Owner.RequireReward(this);
        if (_activeCursor is not null || (uint)ordinal >= _events.Length || _events[ordinal].Attempted)
            throw new InvalidOperationException("Immediate challenge event reentered/replayed its original scope.");
        _activeOrdinal = ordinal; _activeCursor = new(_events[ordinal].Cursor);
        _events[ordinal] = _events[ordinal] with { Attempted = true };
        return _activeCursor;
    }
    internal void ObserveEntered(FalloutScriptManualSaveRequests.Entered entered)
    {
        if (_activeCursor is null || entered.Caller != Target || entered.Program != Program.Source.FormKey ||
            entered.RecordHash != Program.Scope.RecordSha256 || entered.ProgramHash != Program.ProgramSha256 ||
            entered.ScopeHash != _events[_activeOrdinal].EventScopeSha256 || entered.Invocation == 0 || entered.Session == Guid.Empty)
            throw new InvalidDataException("Immediate challenge event entered another actual shared lease.");
        ActiveCompiledInvocation = true;
    }
    internal bool Filter(FalloutChallengeGameModeFilter actual)
    {
        if (_activeCursor is null || ActiveCompiledInvocation) throw new InvalidOperationException("Filtered challenge event has already entered execution.");
        actual.Validate();
        if (_events[_activeOrdinal].GameMode is not null) throw new InvalidOperationException("Immediate GameMode predicate was evaluated twice.");
        _events[_activeOrdinal] = _events[_activeOrdinal] with { Filtered = actual.Scalar == 0, GameMode = actual };
        return actual.Scalar != 0;
    }
    internal void Retired(FalloutScriptManualSaveRequests requests, FalloutCompiledSliceReceipt receipt)
    {
        if (_activeCursor is null || receipt.EventOrdinal != _activeOrdinal ||
            !SameCursor(receipt.Cursor, _activeCursor.State))
            throw new InvalidDataException("Challenge reward retirement differs from its actual entered cursor.");
        receipt.Require(Program, Target);
        if (receipt.Invocation != 0) requests.RequireCurrentCompiledReceipt(receipt);
        else if (receipt.Disposition != "admission-refusal") throw new InvalidDataException("Challenge reward has no genuine shared invocation.");
        _events[_activeOrdinal] = _events[_activeOrdinal] with { Receipt = receipt,
            FailureType = receipt.Error is null ? null : "OpenNV.Runtime.Content.FalloutCompiledInvocationFailure", Error = receipt.Error };
    }
    internal void ExitEvent(Exception? failure = null)
    {
        if (_activeCursor is null) return;
        var row = _events[_activeOrdinal];
        _events[_activeOrdinal] = row with { Cursor = _activeCursor.State with { Branches = _activeCursor.State.Branches.ToArray() },
            FailureType = failure is null ? row.FailureType : failure.GetType().FullName ?? failure.GetType().Name,
            Error = failure?.Message ?? row.Error };
        _activeCursor = null; _activeOrdinal = -1; ActiveCompiledInvocation = false;
    }
    internal void Completed()
    {
        if (_activeCursor is not null || _events.Any(row => !row.Attempted || !row.Filtered && row.Receipt?.Disposition != "completed" || row.Error is not null))
            throw new InvalidOperationException("Challenge immediate script has a living/failed/unexecuted event suffix.");
        _disposition = "completed";
    }
    internal void RequireCompleted()
    {
        if (_disposition is not ("completed" or "authored-empty") || _error is not null)
            throw new NotSupportedException("Challenge reward did not complete its original immediate call.");
    }
    internal void Fail(Exception failure)
    {
        ExitEvent(failure); _disposition = "closed-failure";
        _failureType = failure.GetType().FullName ?? failure.GetType().Name; _error = failure.Message;
    }
    internal FalloutChallengeRewardSnapshot Capture() => new(DispatchOrdinal, Challenge, Program.Source.FormKey,
        Target, Program.Scope.RecordSha256,
        Program.Scope.ScopeSha256, Program.ProgramSha256, Locals.Capture(), _events.ToArray(), _disposition, _failureType, _error);

    public void Require(FalloutPluginStack records, FalloutFormKey target, FalloutCompiledScriptProgram program,
        FalloutCompiledEvent block, FalloutCompiledExecutionCursor cursor, double seconds, FalloutFormKey? action)
    {
        Owner.RequireReward(this);
        if (program != Program || target != Target || records.RuntimeFormId(target) != 0x14 ||
            _activeCursor != cursor || _activeOrdinal < 0 || program.Events[_activeOrdinal] != block ||
            seconds != 0 || action is not null || _events[_activeOrdinal].Filtered)
            throw new InvalidDataException("Challenge immediate local authority differs from its entered player/event/time owner.");
        RequireBlock(block);
    }
    public FalloutScriptValue Read(FalloutCompiledVariable variable)
    { Owner.RequireReward(this); return Locals.ReadCompiled(variable); }
    public void Write(FalloutCompiledVariable variable, FalloutScriptValue value)
    { Owner.RequireReward(this); Locals.WriteCompiled(variable, value); }
    internal static bool SameCursor(FalloutCompiledCursorSnapshot left, FalloutCompiledCursorSnapshot right) =>
        left.NextOffset == right.NextOffset && left.CommittedInstructions == right.CommittedInstructions &&
        left.BudgetSpent == right.BudgetSpent && left.Completed == right.Completed && left.Branches.SequenceEqual(right.Branches);
}
