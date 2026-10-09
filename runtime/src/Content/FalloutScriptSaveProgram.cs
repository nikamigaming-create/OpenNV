using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutScriptSaveProgram(FalloutScriptScopeKind Kind, int StageOrdinal,
    int ResultOrdinal, int FieldStart, int FieldCount, string ScopeSha256, int? EventOrdinal,
    ushort? Event, int? Begin, int? End, string? EventScopeSha256)
{
    internal static FalloutScriptSaveProgram Capture(FalloutCompiledScriptProgram program, FalloutCompiledEvent? block)
    {
        var scope = program.Scope;
        int? ordinal = block is null ? null : program.Events.Select((row, index) => (row, index))
            .Single(pair => ReferenceEquals(pair.row, block)).index;
        return new(scope.Kind, scope.StageOrdinal, scope.ResultOrdinal, scope.FieldStart, scope.FieldCount,
            scope.ScopeSha256, ordinal, block?.Event, block?.Begin, block?.End,
            ordinal is { } value ? FalloutCompiledSliceReceipt.EventScope(program, value) : null);
    }

    internal FalloutCompiledScriptProgram Require(FalloutPluginStack records, FalloutScriptManualSaveSite site)
    {
        var source = records.GetEffective(site.Program);
        var scope = Kind switch
        {
            FalloutScriptScopeKind.Standalone => FalloutScriptScope.Standalone(source),
            FalloutScriptScopeKind.DialogueBegin => FalloutScriptScope.Dialogue(source, true),
            FalloutScriptScopeKind.DialogueEnd => FalloutScriptScope.Dialogue(source, false),
            FalloutScriptScopeKind.PackageBegin => FalloutScriptScope.PackageEvent(source, "POBA"),
            FalloutScriptScopeKind.PackageEnd => FalloutScriptScope.PackageEvent(source, "POEA"),
            FalloutScriptScopeKind.PackageChange => FalloutScriptScope.PackageEvent(source, "POCA"),
            FalloutScriptScopeKind.TerminalEntry => FalloutScriptScope.TerminalResult(source, ResultOrdinal),
            FalloutScriptScopeKind.QuestStageResult => QuestScope(source),
            _ => throw new InvalidDataException("Queued save source scope kind is unowned.")
        };
        if (scope.RecordSha256 != site.RecordSha256 || scope.ScopeSha256 != ScopeSha256 ||
            scope.StageOrdinal != StageOrdinal || scope.ResultOrdinal != ResultOrdinal ||
            scope.FieldStart != FieldStart || scope.FieldCount != FieldCount || !scope.Compiled)
            throw new InvalidDataException("Queued save scope differs from its winning source ordinal/range/hash.");
        var program = FalloutCompiledScriptProgram.Read(source, scope, Kind == FalloutScriptScopeKind.Standalone);
        if (program.ProgramSha256 != site.ProgramSha256)
            throw new InvalidDataException("Queued save program differs from its decoded winning SCDA.");
        if (EventOrdinal is { } ordinal)
        {
            if ((uint)ordinal >= program.Events.Count) throw new InvalidDataException("Queued save event ordinal is outside SCDA.");
            var block = program.Events[ordinal];
            if (Event != block.Event || Begin != block.Begin || End != block.End ||
                EventScopeSha256 != FalloutCompiledSliceReceipt.EventScope(program, ordinal) || site.ScopeSha256 != EventScopeSha256)
                throw new InvalidDataException("Queued save event differs from its actual source scope.");
            if (!program.EventInstructions(block).Any(instruction => instruction.Offset == site.Statement))
                throw new InvalidDataException("Queued save offset is outside its original event.");
        }
        else if (Event is not null || Begin is not null || End is not null || EventScopeSha256 is not null ||
            site.ScopeSha256 != scope.ScopeSha256 || !program.ResultInstructions().Any(instruction => instruction.Offset == site.Statement))
            throw new InvalidDataException("Queued result save lacks its exact instruction range.");
        return program;
    }

    private FalloutScriptScope QuestScope(FalloutPluginRecord source)
    {
        var stages = source.ReadSubrecords().Select((field, index) => (field, index))
            .Where(pair => pair.field.Signature == "INDX").ToArray();
        if ((uint)StageOrdinal >= stages.Length) throw new InvalidDataException("Queued save stage ordinal has no INDX.");
        return FalloutScriptScope.QuestEntry(source, stages[StageOrdinal].index, FieldStart);
    }

    internal void RequireSave(FalloutPluginStack records, FalloutScriptManualSaveSite site, RuntimeSaveRequestOrigin origin)
    {
        var program = Require(records, site);
        var instruction = program.Instructions.Single(item => item.Offset == site.Statement);
        var command = FalloutCompiledCommandDeclarations.Get(instruction.Opcode, records);
        var expected = origin switch
        {
            RuntimeSaveRequestOrigin.ScriptAutoSave => "AutoSave",
            RuntimeSaveRequestOrigin.ScriptForceSave => "ForceSave",
            _ => throw new InvalidDataException("SCDA cannot manufacture a native save origin.")
        };
        if (!command.Name.Equals(expected, StringComparison.OrdinalIgnoreCase) ||
            instruction.Receiver is not null || command.RequiresReference ||
            command.Required != 0 || command.Parameters.Count != 0 || !command.VanillaArguments)
            throw new InvalidDataException("Queued save offset differs from its global zero-argument source command.");
        var operands = new FalloutCompiledOperandCursor(instruction.Payload);
        if (!operands.AtEnd && operands.UInt16() != 0)
            throw new InvalidDataException("Queued save offset has nonzero compiled arguments.");
        operands.RequireEnd();
    }
}
