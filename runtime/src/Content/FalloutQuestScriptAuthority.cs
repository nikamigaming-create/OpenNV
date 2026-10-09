namespace OpenNV.Runtime.Content;

// The reader selects original byte ranges before optional diagnostic text.
// Runtime receipt/cursor owners separately establish execution and completion.
internal sealed record FalloutQuestScriptCompiledSelection(FalloutFormKey Quest,
    FalloutCompiledScriptProgram Program, FalloutQuestScriptDefinition Definition, bool Claimed)
{
    internal object Observation => new
    {
        quest = Quest.ToString(),
        script = Program.Source.FormKey.ToString(),
        Claimed,
        authority = "original-SCDA;recurrence-admission-required",
        Program.Scope.RecordSha256,
        Program.Scope.ScopeSha256,
        Program.ProgramSha256,
        decoderVersion = FalloutCompiledScriptProgram.DecoderVersion,
        sourceFieldStart = Program.Scope.FieldStart,
        sourceFieldCount = Program.Scope.FieldCount,
        Program.CodeBytes,
        Program.ScriptType,
        Program.CompiledFlag,
        Program.LocalCount,
        instructionCount = Program.Instructions.Count,
        events = Program.Events.Select(block => new
        {
            block.Event,
            block.Begin,
            block.End,
            parameterBytes = block.Parameters.Length
        }).ToArray(),
        Definition.InitializationOrdinal,
        Definition.InitialPhase,
        Definition.ProcessingDelay,
        executionEvidence = "read the instance cursor, actual receipts and clock; selection alone proves no execution"
    };
}

internal static class FalloutQuestScriptAuthority
{
    internal const string SchedulingRefusal =
        "Winning standalone SCDA requires its compiled recurring event owner; diagnostic SCTX and legacy source cursors are refused.";

    internal static FalloutCompiledScriptProgram? ReadCompiled(FalloutPluginRecord script)
    {
        if (script.Signature != "SCPT") throw new InvalidDataException("Quest script authority owner is not SCPT.");
        if (!FalloutCompiledScriptProgram.HasProgram(script.ReadSubrecords().ToArray())) return null;
        var scope = FalloutScriptScope.Standalone(script);
        return FalloutCompiledScriptProgram.Read(script, scope, standalone: true);
    }

    internal static void RequireRecurringEvent(FalloutCompiledScriptProgram program, FalloutCompiledEvent block)
    {
        if (block.Event is not (0 or 1))
            throw new NotSupportedException($"Compiled quest event {block.Event:x4} has no recurring dispatch owner.");
        if (!block.Parameters.IsEmpty && !(block.Parameters.Length == 2 &&
            System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(block.Parameters.Span) == 0))
            throw new NotSupportedException("Filtered compiled quest event headers require their independent original argument/filter contract.");
        _ = FalloutCompiledControlFlow.Read(program.EventInstructions(block), block.End);
    }

    internal static void RequireRecurringProgram(FalloutCompiledScriptProgram program)
    {
        if (!program.Standalone || program.ScriptType != 1 || program.CompiledFlag != 1)
            throw new NotSupportedException("Compiled quest recurrence type/flag/standalone owner is unsupported.");
        foreach (var block in program.Events) RequireRecurringEvent(program, block);
    }

    internal static void RequireSourceExecution(FalloutPluginRecord script)
    {
        if (ReadCompiled(script) is not null) throw new NotSupportedException(SchedulingRefusal);
    }
}
