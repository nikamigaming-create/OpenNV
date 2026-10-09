using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutActorProcessRuntimeState
{
    internal FalloutMainUtilityCommandSnapshot CaptureMainUtilityCommands()
    {
        RequireNotBusy(); var source = UtilityCommandSource();
        if (MainUtilityCommandSaveBlocker is { } blocker) throw new NotSupportedException(blocker);
        var snapshot = new FalloutMainUtilityCommandSnapshot(UtilityCommandSchema, source, _utilityConsoleSource!, _stack, _process,
            _sequence, _utilityCommandConstructed, _achievementSuppression, _consoleSubmissions, _consoleSubmissionLast,
            _diagnosticCalls, _lastDiagnostic, _lastDiagnosticId, _lastDiagnosticReturn);
        ValidateMainUtilityCommands(snapshot); return snapshot;
    }
    internal static void ValidateMainUtilityCommands(FalloutMainUtilityCommandSnapshot value)
    {
        if (value is null || value.Schema != UtilityCommandSchema || value.Source is null || value.ConsoleSource is null ||
            string.IsNullOrWhiteSpace(value.Stack) || value.CapturedProcess == Guid.Empty || value.Changed < 1 ||
            value.Constructed < 1 || value.Constructed > value.Changed || value.Submissions < 0 || value.Suppression > 1 ||
            (value.Submissions == 0) != (value.LastSubmission is null) || value.DiagnosticCalls < 0 ||
            value.LastDiagnostic < 0 || value.LastDiagnostic > value.Changed ||
            (value.DiagnosticCalls == 0) != (value.LastDiagnostic == 0) ||
            (value.DiagnosticCalls == 0) != (value.LastDiagnosticId is null) ||
            (value.DiagnosticCalls == 0) != (value.LastDiagnosticReturn is null))
            throw new InvalidDataException("Source utility commands lost their constructor/counter/byte prefix.");
        value.Source.Validate(); value.ConsoleSource.Validate();
        if (value.ConsoleSource.EngineSha256 != value.Source.Main.Main.EngineSha256 ||
            value.ConsoleSource.RuntimeSha256 != value.Source.Main.Main.RuntimeSha256 ||
            value.LastDiagnosticId is <= 100 || value.LastDiagnosticReturn is { } returned && returned != value.Source.DiagnosticReturn)
            throw new InvalidDataException("Source utility diagnostic changed its signed source branch or selected no-effect return.");
        if (value.LastSubmission is { } call)
        {
            // Historical calls keep their original process identity on cold;
            // the enclosing snapshot is correlated to the actual current Main
            // capture separately, without rewriting a genuine prior receipt.
            if (call.Identity == Guid.Empty || call.SourceProcess == Guid.Empty || string.IsNullOrWhiteSpace(call.Producer) ||
                call.Entered <= value.Constructed || call.Changed <= call.Entered || call.Changed > value.Changed ||
                !FalloutAdvancementRuntimeReceipt.Digest(call.InputSha256) ||
                call.NormalizedSha256 is not null && !FalloutAdvancementRuntimeReceipt.Digest(call.NormalizedSha256) ||
                !Enum.IsDefined(call.Phase) || call.SuppressionBefore > call.SuppressionAfter || call.SuppressionAfter != value.Suppression ||
                call.Phase != FalloutMainConsoleSubmissionPhase.Returned || call.Error is not null || call.FailureType is not null ||
                call.NormalizedSha256 is null || call.ExecutionResult is not null)
                throw new NotSupportedException("Source console snapshot retains unowned execution or an inconsistent committed input prefix.");
        }
        else if (value.Suppression != value.Source.InitialSuppression)
            throw new InvalidDataException("Suppression byte has no actual source submission writer.");
    }
}
