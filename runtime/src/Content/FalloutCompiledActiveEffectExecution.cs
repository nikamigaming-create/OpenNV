using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

// Start is an actual compiled event, scoped to one spell/effect generation and
// its ordered raw cells. Update/Finish producers and an effect clock are separate
// owners; declaring those events still refuses this bounded lifetime.
internal sealed class FalloutCompiledActiveEffectExecution
{
    internal const string Schema = "opennv-compiled-active-effect/v1";
    private sealed class Event(FalloutCompiledEvent source, FalloutCompiledControlFlow flow,
        FalloutCompiledExecutionCursor cursor, FalloutCompiledActiveEffectEventState state)
    {
        internal FalloutCompiledEvent Source { get; } = source;
        internal FalloutCompiledControlFlow Flow { get; } = flow;
        internal FalloutCompiledExecutionCursor Cursor { get; } = cursor;
        internal FalloutCompiledActiveEffectEventState State { get; set; } = state;
    }
    private readonly FalloutCompiledActiveEffectSnapshot _identity;
    private readonly Event[] _events;
    private FalloutCompiledActiveEffectInvocation? _entered;

    internal FalloutFormKey Target => _identity.Target;
    internal FalloutCompiledScriptProgram Program { get; }
    internal FalloutScriptEffectLocals Locals { get; }

    internal FalloutCompiledActiveEffectExecution(FalloutPluginStack records, FalloutFormKey target,
        FalloutAbilityScript definition, long generation, FalloutPluginRecord script,
        FalloutScriptEffectLocals locals, FalloutCompiledActiveEffectSnapshot? restore = null)
    {
        if (generation <= 0 || definition.Script != script.FormKey || locals.Script != script.FormKey ||
            records.GetEffective(script.FormKey) != script)
            throw new InvalidDataException("Compiled active effect lacks its actual winning script/generation/local owner.");
        if (records.RuntimeFormId(target) != 0x14 && records.GetEffective(target).Signature is not ("ACHR" or "ACRE"))
            throw new InvalidDataException("Compiled active effect lacks its genuine target actor.");
        Program = FalloutCompiledScriptProgram.Read(script, FalloutScriptScope.Standalone(script), standalone: true);
        if (FalloutScriptSourceKinds.Classify(Program.ScriptType) != FalloutScriptSourceKind.MagicEffect || Program.CompiledFlag != 1)
            throw new NotSupportedException("Active effects require their winning compiled magic-effect body.");
        foreach (var block in Program.Events) RequireStart(Program, block);
        Locals = locals;
        _identity = new(Schema, target, definition.Spell, definition.EffectOrdinal, definition.Effect,
            definition.Script, generation, script.Plugin.Name, Program.Scope.RecordSha256,
            Program.Scope.ScopeSha256, Program.ProgramSha256, []);
        if (restore is not null)
        {
            Validate(restore);
            var restoredIdentity = restore with { Events = _identity.Events };
            if (restoredIdentity != _identity || restore.Events.Count != Program.Events.Count)
                throw new InvalidDataException("Saved compiled active effect differs from its winning source/target/generation.");
        }
        _events = Program.Events.Select((block, ordinal) =>
        {
            var flow = FalloutCompiledControlFlow.Read(Program.EventInstructions(block), block.End);
            var initial = new FalloutCompiledActiveEffectEventState(ordinal, block.Event, block.Begin, block.End,
                FalloutCompiledSliceReceipt.EventScope(Program, ordinal), false, flow.InitialCursor, null, null, null);
            var state = restore?.Events[ordinal] ?? initial;
            var restoredEvent = state with { Attempted = false, Cursor = initial.Cursor, Receipt = null, LastReachedOffset = null, Failure = null };
            if (restoredEvent != initial)
                throw new InvalidDataException("Saved active-effect event differs from its source ordinal/extent/hash.");
            flow.ValidateCursor(state.Cursor);
            if (!state.Attempted && !SameCursor(state.Cursor, flow.InitialCursor))
                throw new InvalidDataException("Unentered active-effect event owns a consumed compiled prefix.");
            if (state.Receipt is { } receipt)
            {
                receipt.Require(Program, target);
                if (receipt.EventOrdinal != ordinal || !SameCursor(receipt.Cursor, state.Cursor) ||
                    receipt.Error != state.Failure || receipt.Disposition == "suspended")
                    throw new InvalidDataException("Saved active-effect retirement differs from its retained compiled cursor.");
            }
            if (state.LastReachedOffset is { } offset && !Program.EventInstructions(block).Any(row => row.Offset == offset))
                throw new InvalidDataException("Saved active-effect reached offset is outside its event.");
            return new Event(block, flow, new(state.Cursor), state);
        }).ToArray();
    }

    internal static void RequireStart(FalloutCompiledScriptProgram program, FalloutCompiledEvent block)
    {
        if (!program.Events.Any(row => ReferenceEquals(row, block)))
            throw new InvalidDataException("Active-effect event belongs to another SCDA owner.");
        if (block.Event != 17)
            throw new NotSupportedException($"Compiled active effect {FalloutCompiledScriptEvents.Name(block.Event)} requires its event/timeline owner.");
        if (block.Parameters.IsEmpty) return;
        if (block.Parameters.Length == 2 && BinaryPrimitives.ReadUInt16LittleEndian(block.Parameters.Span) == 0) return;
        throw new NotSupportedException("Compiled ScriptEffectStart parameters have no admitted producer.");
    }

    internal void ExecuteStart(Func<FalloutCompiledActiveEffectInvocation, FalloutCompiledActiveEffectReceipt> execute)
    {
        ArgumentNullException.ThrowIfNull(execute);
        if (_entered is not null) throw new InvalidOperationException("The actual active-effect Start is reentrant.");
        if (_events.Any(row => row.State.Failure is not null || row.State.Attempted))
            throw new NotSupportedException("An attempted active-effect Start cannot replay its compiled prefix.");
        for (var ordinal = 0; ordinal < _events.Length; ++ordinal)
        {
            var row = _events[ordinal];
            _entered = new(this, ordinal, row.Cursor);
            row.State = row.State with { Attempted = true };
            try
            {
                var receipt = execute(_entered);
                if (!ReferenceEquals(receipt.Invocation, _entered))
                    throw new InvalidDataException("Active-effect execution returned another invocation's receipt.");
                receipt.Require();
                if (receipt.Retired.Disposition != "completed" || !row.Cursor.State.Completed)
                    throw new NotSupportedException("ScriptEffectStart did not retire its complete original SCDA suffix.");
                row.State = row.State with { Cursor = Copy(row.Cursor.State), Receipt = receipt.Retired,
                    LastReachedOffset = receipt.LastReachedOffset };
            }
            catch (FalloutCompiledActiveEffectFailure failure)
            {
                failure.Receipt.Require();
                if (!ReferenceEquals(failure.Receipt.Invocation, _entered))
                    throw new InvalidDataException("Failed active-effect receipt belongs to another invocation.", failure);
                row.State = row.State with { Cursor = Copy(row.Cursor.State), Receipt = failure.Receipt.Retired,
                    LastReachedOffset = failure.Receipt.LastReachedOffset, Failure = failure.Message };
                throw;
            }
            catch (Exception failure)
            {
                // An unbound caller/retirement may have committed a prefix. Keep
                // its real cursor and error; never invent a native/shared receipt.
                row.State = row.State with { Cursor = Copy(row.Cursor.State), Failure = failure.Message };
                throw;
            }
            finally { _entered = null; }
        }
    }

    internal void RequireInvocation(FalloutCompiledActiveEffectInvocation invocation)
    {
        if (!ReferenceEquals(_entered, invocation) || !ReferenceEquals(invocation.Owner, this) ||
            (uint)invocation.Ordinal >= _events.Length || !ReferenceEquals(invocation.Cursor, _events[invocation.Ordinal].Cursor))
            throw new InvalidOperationException("Compiled active-effect call has no actual entered instance/event-list owner.");
    }

    internal void RequireLifecycle(bool started, string? error)
    {
        if (started && (_events.Any(row => !row.State.Attempted || !row.Cursor.State.Completed ||
                row.State.Receipt?.Disposition != "completed" || row.State.Failure is not null) || error is not null))
            throw new InvalidDataException("Started active effect lacks completed original event retirements.");
        if (!started && error is null && _events.Any(row => row.State.Attempted || row.State.Failure is not null))
            throw new InvalidDataException("Unstarted active effect has an attempted/failed compiled lifetime without its retained error.");
        if (error is not null && !_events.Any(row => row.State.Attempted && row.State.Failure is not null))
            throw new InvalidDataException("Failed active effect lost its actual attempted compiled event.");
    }

    internal FalloutCompiledActiveEffectSnapshot Capture()
    {
        if (_entered is not null) throw new NotSupportedException("Capturing an entered active-effect instruction requires its suspension owner.");
        return _identity with { Events = _events.Select(row => row.State with { Cursor = Copy(row.Cursor.State) }).ToArray() };
    }

    internal static void Validate(FalloutCompiledActiveEffectSnapshot state)
    {
        if (state is null || state.Schema != Schema || state.Target.ObjectId == 0 || state.Spell.ObjectId == 0 ||
            state.Effect.ObjectId == 0 || state.Script.ObjectId == 0 || state.EffectOrdinal < 0 || state.Generation <= 0 ||
            string.IsNullOrWhiteSpace(state.Winner) || !Hash(state.RecordSha256) || !Hash(state.ScopeSha256) ||
            !Hash(state.ProgramSha256) || state.Events is null)
            throw new InvalidDataException("Saved compiled active effect has incomplete source/instance identity.");
        for (var ordinal = 0; ordinal < state.Events.Count; ++ordinal)
        {
            var row = state.Events[ordinal];
            if (row is null || row.Ordinal != ordinal || row.Event != 17 || row.Begin < 0 || row.End < row.Begin ||
                !Hash(row.EventScopeSha256) || row.Cursor is null || row.Cursor.Branches is null || row.Cursor.NextOffset < 0 ||
                row.Cursor.CommittedInstructions < 0 || row.Cursor.BudgetSpent < row.Cursor.CommittedInstructions || row.Cursor.BudgetSpent > 100_000 ||
                row.Failure is not null && string.IsNullOrWhiteSpace(row.Failure) ||
                !row.Attempted && (row.Receipt is not null || row.LastReachedOffset is not null || row.Failure is not null) ||
                row.Receipt is { } receipt && (receipt.EventOrdinal != ordinal || receipt.Event != row.Event ||
                    receipt.Begin != row.Begin || receipt.End != row.End || receipt.EventScopeSha256 != row.EventScopeSha256 ||
                    receipt.Program != state.Script || receipt.Caller != state.Target || receipt.RecordSha256 != state.RecordSha256 ||
                    receipt.ScopeSha256 != state.ScopeSha256 || receipt.ProgramSha256 != state.ProgramSha256 ||
                    receipt.Error != row.Failure || receipt.Disposition == "suspended" || !SameCursor(receipt.Cursor, row.Cursor)))
                throw new InvalidDataException("Saved compiled active-effect event cursor/retirement identity is invalid.");
        }
    }

    internal static bool SameCursor(FalloutCompiledCursorSnapshot? a, FalloutCompiledCursorSnapshot? b) =>
        a is not null && b is not null && a.Branches is not null && b.Branches is not null &&
        a.NextOffset == b.NextOffset && a.CommittedInstructions == b.CommittedInstructions && a.BudgetSpent == b.BudgetSpent &&
        a.Completed == b.Completed && a.Branches.SequenceEqual(b.Branches);
    private static FalloutCompiledCursorSnapshot Copy(FalloutCompiledCursorSnapshot state) =>
        state with { Branches = state.Branches.ToArray() };
    private static bool Hash(string value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
}
