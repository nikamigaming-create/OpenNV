using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceScripts
{
    private readonly HashSet<(FalloutFormKey Caller, string Scope)> _activeRecurring = [];

    internal FalloutCompiledSliceReceipt ExecuteProgram(FalloutPluginRecord owner,
        FalloutCompiledScriptProgram program, int eventOrdinal, FalloutCompiledExecutionCursor cursor,
        double seconds, Func<bool> canContinue)
    {
        var before = cursor.State.CommittedInstructions;
        var scope = FalloutCompiledSliceReceipt.EventScope(program, eventOrdinal);
        var key = (owner.FormKey, scope);
        ulong invocation = 0;
        try
        {
            program.Scope.RequireSource(records.GetEffective(program.Source.FormKey));
            if (owner.Signature != "QUST" || records.GetEffective(owner.FormKey) != owner ||
                FalloutScriptLocals.AttachedScript(records, owner)?.FormKey != program.Source.FormKey ||
                !program.Standalone || program.ScriptType != 1 || program.CompiledFlag != 1)
                throw new InvalidDataException("Compiled recurrence has no winning attached QUST/SCPT owner.");
            var block = program.Events[eventOrdinal];
            FalloutQuestScriptAuthority.RequireRecurringEvent(program, block);
            var flow = FalloutCompiledControlFlow.Read(program.EventInstructions(block), block.End); flow.ValidateCursor(cursor.State);
            if (!_activeRecurring.Add(key)) throw new InvalidOperationException("Compiled recurrence is reentrant in its actual event owner.");
            try
            {
                foreach (var _ in CompiledSteps(owner.FormKey, program, block, seconds,
                    observeInvocation: entered => invocation = entered.Invocation, cursor: cursor,
                    canContinue: canContinue, executionScope: scope)) { }
            }
            finally { _activeRecurring.Remove(key); }
            if (invocation == 0) throw new InvalidOperationException("Compiled recurrence has no actual shared invocation lease.");
            var receipt = Receipt(cursor.State.Completed ? "completed" : "suspended", null);
            receipt.Require(program, owner.FormKey); return receipt;
        }
        catch (Exception error)
        {
            // The suffix is closed. A callback can have committed its own partial
            // effects before throwing; those effects are never rolled back/replayed.
            var receipt = Receipt(invocation == 0 ? "admission-refusal" : "closed-failure", error.Message);
            throw new FalloutCompiledInvocationFailure(receipt, error);
        }

        FalloutCompiledSliceReceipt Receipt(string disposition, string? error)
        {
            var block = program.Events[eventOrdinal];
            return new(owner.FormKey, program.Source.FormKey, program.Scope.RecordSha256, program.Scope.ScopeSha256,
                program.ProgramSha256, scope, eventOrdinal, block.Event, block.Begin, block.End,
                invocation == 0 ? Guid.Empty : world.ScriptManualSaves.ExecutionSession, invocation,
                before, cursor.State, disposition, error);
        }
    }
}
