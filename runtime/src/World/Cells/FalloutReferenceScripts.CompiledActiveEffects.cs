using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceScripts
{
    private readonly HashSet<FalloutCompiledActiveEffectExecution> _activeCompiledEffects = [];

    private FalloutCompiledActiveEffectReceipt ExecuteCompiledActiveEffect(FalloutCompiledActiveEffectInvocation effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        var program = effect.Program;
        var block = effect.Block;
        FalloutScriptManualSaveRequests.Entered? entered = null;
        var added = false;
        try
        {
            effect.Require(records, effect.Target, program, block);
            if (records.RuntimeFormId(effect.Target) != 0x14)
            {
                var actual = world.Get(effect.Target);
                if (actual.Deleted || actual.DeletePending || actual.Reference != effect.Target)
                    throw new InvalidDataException("Compiled active-effect target has no current actor instance.");
            }
            if (!_activeCompiledEffects.Add(effect.Owner))
                throw new InvalidOperationException("Compiled active-effect execution is reentrant in its actual instance.");
            added = true;
            foreach (var _ in CompiledSteps(effect.Target, program, block, 0,
                observeInvocation: actual => entered = actual, cursor: effect.Cursor,
                canContinue: () => true, localAuthority: effect)) { }
        }
        catch (Exception failure)
        {
            var receipt = Receipt(entered is null ? "admission-refusal" : "closed-failure", failure.Message);
            receipt.Require();
            throw new FalloutCompiledActiveEffectFailure(receipt, failure);
        }
        finally
        {
            if (added && !_activeCompiledEffects.Remove(effect.Owner))
                throw new InvalidOperationException("Compiled active-effect execution lost its actual entered owner.");
        }
        if (entered is null) throw new InvalidOperationException("Completed ScriptEffectStart has no actual shared invocation lease.");
        var complete = Receipt("completed", null);
        complete.Require();
        return complete;

        FalloutCompiledActiveEffectReceipt Receipt(string disposition, string? failure)
        {
            var state = effect.Cursor.State with { Branches = effect.Cursor.State.Branches.ToArray() };
            var retired = new FalloutCompiledSliceReceipt(effect.Target, program.Source.FormKey,
                program.Scope.RecordSha256, program.Scope.ScopeSha256, program.ProgramSha256,
                effect.EventScopeSha256, effect.Ordinal, block.Event, block.Begin, block.End,
                entered?.Session ?? Guid.Empty, entered?.Invocation ?? 0, effect.PrefixBefore, state, disposition, failure);
            // Receipt construction consumes the genuine shared compiled lease;
            // a matching-looking record cannot publish successful completion.
            return FalloutCompiledActiveEffectReceipt.Retire(world.ScriptManualSaves, effect, retired, entered?.ReachedInstruction);
        }
    }
}
