using System.Security.Cryptography;
using System.Text;

namespace OpenNV.Runtime.Content;

// Shared compiled execution creates this evidence. Scheduler completion is a
// separate decision made after every selected original event has ended.
internal sealed record FalloutCompiledSliceReceipt(FalloutFormKey Caller, FalloutFormKey Program,
    string RecordSha256, string ScopeSha256, string ProgramSha256, string EventScopeSha256,
    int EventOrdinal, ushort Event, int Begin, int End, Guid Session, ulong Invocation,
    int PrefixBefore, FalloutCompiledCursorSnapshot Cursor, string Disposition, string? Error)
{
    internal static string EventScope(FalloutCompiledScriptProgram program, int ordinal)
    {
        if ((uint)ordinal >= program.Events.Count) throw new InvalidDataException("Compiled event ordinal is not source-owned.");
        var block = program.Events[ordinal];
        var arguments = Convert.ToHexString(SHA256.HashData(block.Parameters.Span)).ToLowerInvariant();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"opennv-compiled-event/v1\0{program.Scope.ScopeSha256}\0{program.ProgramSha256}\0{ordinal}\0{block.Event}\0{block.Begin}\0{block.End}\0{arguments}"))).ToLowerInvariant();
    }

    internal void Require(FalloutCompiledScriptProgram program, FalloutFormKey caller)
    {
        if ((uint)EventOrdinal >= program.Events.Count) throw new InvalidDataException("Compiled receipt has no original event ordinal.");
        var block = program.Events[EventOrdinal];
        if (Caller != caller || Program != program.Source.FormKey || RecordSha256 != program.Scope.RecordSha256 ||
            ScopeSha256 != program.Scope.ScopeSha256 || ProgramSha256 != program.ProgramSha256 ||
            EventScopeSha256 != EventScope(program, EventOrdinal) || Event != block.Event || Begin != block.Begin || End != block.End)
            throw new InvalidDataException("Compiled receipt differs from its exact winning event scope.");
        var flow = FalloutCompiledControlFlow.Read(program.EventInstructions(block), block.End);
        flow.ValidateCursor(Cursor);
        if (PrefixBefore < 0 || PrefixBefore > Cursor.CommittedInstructions ||
            Disposition is not ("completed" or "suspended" or "closed-failure" or "admission-refusal") ||
            (Disposition is "completed") != (Cursor.Completed && Error is null) ||
            (Disposition is "closed-failure" or "admission-refusal") != (Error is not null) ||
            Error is not null && string.IsNullOrWhiteSpace(Error) ||
            Disposition == "admission-refusal" && (Invocation != 0 || Session != Guid.Empty || PrefixBefore != Cursor.CommittedInstructions) ||
            Disposition != "admission-refusal" && (Invocation == 0 || Session == Guid.Empty))
            throw new InvalidDataException("Compiled receipt has no actual lease/prefix/disposition ownership.");
    }
}

internal sealed class FalloutCompiledInvocationFailure(FalloutCompiledSliceReceipt receipt, Exception cause)
    : NotSupportedException(receipt.Error, cause)
{
    internal FalloutCompiledSliceReceipt Receipt { get; } = receipt;
}
