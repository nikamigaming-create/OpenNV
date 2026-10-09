namespace OpenNV.Runtime.Content;

internal enum FalloutScriptResultAuthority { None, SourceDiagnostic, CompiledVanilla }

// A completed result is a shared execution receipt. It describes an actual
// invocation/committed prefix; it is not a native speech success or a VM cursor.
internal sealed record FalloutScriptResultReceipt(string Schema, FalloutFormKey Caller,
    FalloutFormKey Program, FalloutScriptScopeKind ScopeKind, int StageOrdinal, int ResultOrdinal,
    int FieldStart, int FieldCount, string RecordSha256, string ScopeSha256,
    FalloutScriptResultAuthority Authority, string ProgramSha256, ulong Invocation,
    int CommittedSteps, bool Completed)
{
    internal const string ExpectedSchema = "opennv-script-result-receipt/v1";

    internal static bool HasProgram(FalloutScriptScope scope) => scope.Compiled ||
        FalloutDialogueTopic.CodeLines(scope.DiagnosticSource()).Any();

    // Absence is mechanically proved from the current reader-owned range. This
    // can admit a new warm empty result, never stamp an older saved invocation.
    internal static FalloutScriptResultReceipt CompleteAbsent(FalloutScriptScope scope, FalloutFormKey caller)
    {
        if (HasProgram(scope)) throw new InvalidDataException("Absent result contains an authored program.");
        return Capture(scope, caller, FalloutScriptResultAuthority.None, scope.ScopeSha256, 0, 0);
    }

    internal static FalloutScriptResultReceipt Capture(FalloutScriptScope scope, FalloutFormKey caller,
        FalloutScriptResultAuthority authority, string programHash, ulong invocation, int committedSteps) =>
        new(ExpectedSchema, caller, scope.Source.FormKey, scope.Kind, scope.StageOrdinal, scope.ResultOrdinal,
            scope.FieldStart, scope.FieldCount, scope.RecordSha256, scope.ScopeSha256, authority,
            programHash, invocation, committedSteps, true);

    internal void Require(FalloutScriptScope scope, FalloutFormKey caller)
    {
        if (Schema != ExpectedSchema || Caller != caller || Program != scope.Source.FormKey || ScopeKind != scope.Kind ||
            StageOrdinal != scope.StageOrdinal || ResultOrdinal != scope.ResultOrdinal || FieldStart != scope.FieldStart ||
            FieldCount != scope.FieldCount || RecordSha256 != scope.RecordSha256 || ScopeSha256 != scope.ScopeSha256 ||
            CommittedSteps < 0 || !Completed)
            throw new InvalidDataException("Result receipt differs from its completed caller/source range and ordinal.");
        if (scope.Compiled)
        {
            var compiled = FalloutCompiledScriptProgram.Read(scope.Source, scope, standalone: false);
            if (compiled.CompiledFlag != 1 || compiled.ScriptType is not (0 or 1) || compiled.LocalCount != 0)
                throw new NotSupportedException("Result receipt selected an unowned compiled header/local owner.");
            _ = FalloutCompiledControlFlow.Read(compiled.ResultInstructions());
            if (Authority != FalloutScriptResultAuthority.CompiledVanilla || ProgramSha256 != compiled.ProgramSha256 ||
                Invocation == 0 || (compiled.Instructions.Count == 0 ? CommittedSteps != 0 :
                    CommittedSteps == 0 || CommittedSteps > compiled.Instructions.Count))
                throw new InvalidDataException("Result receipt has no matching completed compiled prefix.");
        }
        else if (FalloutDialogueTopic.CodeLines(scope.DiagnosticSource()).Any())
        {
            var source = FalloutGameModeProgram.Read("begin Result\n" + scope.DiagnosticSource() + "\nend", "Result");
            if (Authority != FalloutScriptResultAuthority.SourceDiagnostic || ProgramSha256 != source.ProgramSha256 || Invocation == 0)
                throw new InvalidDataException("Result receipt has no matching completed diagnostic source prefix.");
        }
        else if (Authority != FalloutScriptResultAuthority.None || ProgramSha256 != scope.ScopeSha256 || Invocation != 0 || CommittedSteps != 0)
            throw new InvalidDataException("Empty result receipt contains an invented execution prefix.");
    }
}
