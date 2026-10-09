using OpenNV.Runtime.Content;

// Fixture admission uses the canonical source scope. Empty SCDA is an authored
// zero-step invocation; an absent program never invokes a result callback.
internal static class NativeAuthoredResultAudit
{
    internal static bool IsEmpty(FalloutPluginStack records, FalloutDialogueInfo info, bool begin)
    {
        var scope = FalloutScriptScope.Dialogue(info.Record, begin);
        scope.RequireSource(records.GetEffective(info.Record.FormKey));
        if (!scope.Compiled) return !FalloutScriptResultReceipt.HasProgram(scope);
        var program = FalloutCompiledScriptProgram.Read(info.Record, scope, standalone: false);
        if (program.CompiledFlag != 1 || program.ScriptType is not (0 or 1) || program.LocalCount != 0)
            throw new NotSupportedException("Native result fixture has no compiled header/local owner.");
        _ = FalloutCompiledControlFlow.Read(program.ResultInstructions());
        return program.CodeBytes == 0 && program.Instructions.Count == 0;
    }

    internal static int Invocations(FalloutDialogueInfo info) =>
        (FalloutScriptResultReceipt.HasProgram(FalloutScriptScope.Dialogue(info.Record, true)) ? 1 : 0) +
        (FalloutScriptResultReceipt.HasProgram(FalloutScriptScope.Dialogue(info.Record, false)) ? 1 : 0);
}
