namespace OpenNV.Runtime.Content;

// One source-owned event list, with repeated Update cursors and one genuine
// Finish closure. Timeline producers retain the instance and actual clock.
internal sealed partial class FalloutCompiledActiveEffectExecution
{
    internal const string Schema = "opennv-compiled-active-effect/v2";
    private sealed class Event(FalloutCompiledEvent source, FalloutCompiledControlFlow flow,
        FalloutCompiledExecutionCursor cursor, FalloutCompiledActiveEffectEventState state)
    {
        internal FalloutCompiledEvent Source { get; } = source;
        internal FalloutCompiledControlFlow Flow { get; } = flow;
        internal FalloutCompiledExecutionCursor Cursor { get; set; } = cursor;
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
        foreach (var block in Program.Events) RequireEvent(Program, block);
        Locals = locals;
        _identity = new(Schema, target, definition.Spell, definition.EffectOrdinal, definition.Effect,
            definition.Script, generation, script.Plugin.Name, Program.Scope.RecordSha256,
            Program.Scope.ScopeSha256, Program.ProgramSha256, [], _lifetime);
        if (restore is not null)
        {
            Validate(restore);
            var restoredIdentity = restore with { Events = _identity.Events, Lifetime = _identity.Lifetime };
            if (restoredIdentity != _identity || restore.Events.Count != Program.Events.Count)
                throw new InvalidDataException("Saved compiled active effect differs from its winning source/target/generation.");
        }
        _events = Program.Events.Select((block, ordinal) =>
        {
            var flow = FalloutCompiledControlFlow.Read(Program.EventInstructions(block), block.End);
            var initial = new FalloutCompiledActiveEffectEventState(ordinal, block.Event, block.Begin, block.End,
                FalloutCompiledSliceReceipt.EventScope(Program, ordinal), false, flow.InitialCursor, null, null, null);
            var state = restore?.Events[ordinal] ?? initial;
            var restoredEvent = state with { Attempted = false, Cursor = initial.Cursor, Receipt = null, LastReachedOffset = null, Failure = null, Cycle = 0, SecondsBits = 0 };
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
        RestoreLifecycle(restore);
    }

    internal void RequireInvocation(FalloutCompiledActiveEffectInvocation invocation)
    {
        if (!ReferenceEquals(_entered, invocation) || !ReferenceEquals(invocation.Owner, this) ||
            (uint)invocation.Ordinal >= _events.Length || !ReferenceEquals(invocation.Cursor, _events[invocation.Ordinal].Cursor))
            throw new InvalidOperationException("Compiled active-effect call has no actual entered instance/event-list owner.");
        if (invocation.Producer is OpenNV.Runtime.Gameplay.State.FalloutScriptedEffectUpdate update) update.Require(this);
        else if (invocation.Producer is OpenNV.Runtime.Gameplay.State.FalloutScriptedEffectFinish finish) finish.Require(this);
        else if (invocation.Block.Event != StartEvent)
            throw new InvalidOperationException("Script-effect event has no actual lifecycle producer.");
    }

    internal FalloutCompiledActiveEffectSnapshot Capture()
    {
        if (_entered is not null) throw new NotSupportedException("Capturing an entered active-effect instruction requires its suspension owner.");
        return _identity with { Events = _events.Select(row => row.State with { Cursor = Copy(row.Cursor.State) }).ToArray(), Lifetime = _lifetime };
    }

    internal static void Validate(FalloutCompiledActiveEffectSnapshot state)
    {
        if (state is null || state.Schema != Schema || state.Target.ObjectId == 0 || state.Spell.ObjectId == 0 ||
            state.Effect.ObjectId == 0 || state.Script.ObjectId == 0 || state.EffectOrdinal < 0 || state.Generation <= 0 ||
            string.IsNullOrWhiteSpace(state.Winner) || !Hash(state.RecordSha256) || !Hash(state.ScopeSha256) ||
            !Hash(state.ProgramSha256) || state.Events is null)
            throw new InvalidDataException("Saved compiled active effect has incomplete source/instance identity.");
        ValidateLifetime(state.Lifetime);
        for (var ordinal = 0; ordinal < state.Events.Count; ++ordinal)
        {
            var row = state.Events[ordinal];
            if (row is null || row.Ordinal != ordinal || row.Event is not (StartEvent or UpdateEvent or FinishEvent) || row.Begin < 0 || row.End < row.Begin ||
                !Hash(row.EventScopeSha256) || row.Cursor is null || row.Cursor.Branches is null || row.Cursor.NextOffset < 0 ||
                row.Cursor.CommittedInstructions < 0 || row.Cursor.BudgetSpent < row.Cursor.CommittedInstructions || row.Cursor.BudgetSpent > 100_000 ||
                row.Cycle < 0 || row.Attempted != (row.Cycle != 0) ||
                !float.IsFinite(BitConverter.UInt32BitsToSingle(row.SecondsBits)) || BitConverter.UInt32BitsToSingle(row.SecondsBits) < 0 ||
                row.Event != UpdateEvent && row.SecondsBits != 0 ||
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
