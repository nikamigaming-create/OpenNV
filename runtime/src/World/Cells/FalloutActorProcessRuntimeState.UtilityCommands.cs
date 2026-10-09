using System.Security.Cryptography;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutActorProcessRuntimeState : IFalloutMainUtilityCommandHost
{
    internal const string UtilityCommandSchema = "opennv-source-Main-utility-commands/v1";
    private FalloutMainUtilityCommandSource? _utilityCommandSource;
    private FalloutConsoleActivitySource? _utilityConsoleSource;
    private long _utilityCommandConstructed, _consoleSubmissions, _diagnosticCalls, _lastDiagnostic;
    private byte _achievementSuppression;
    private int? _lastDiagnosticId;
    private uint? _lastDiagnosticReturn;
    private FalloutMainConsoleSubmissionReceipt? _consoleSubmissionLast;
    private FalloutMainConsoleSubmission? _consoleSubmissionEntered;
    private IDisposable? _ownedUtilityCommandBinding;
    internal bool MainUtilityCommandsConstructed => _utilityCommandSource is not null && _ownedUtilityCommandBinding is not null;
    FalloutMainUtilitySource IFalloutMainUtilityCommandHost.Source => MainUtilitySource;
    string IFalloutMainUtilityCommandHost.Owner => "actual-source-Main-utility-command/" + _process;
    internal object? MainUtilityCommandState => _utilityCommandSource is null ? null : new
    {
        source = _utilityCommandSource,
        sourceProcess = _process,
        constructed = _utilityCommandConstructed,
        suppressionByte = _achievementSuppression,
        submissions = _consoleSubmissions,
        lastSubmission = _consoleSubmissionLast,
        entered = _consoleSubmissionEntered?.Identity,
        diagnosticCalls = _diagnosticCalls,
        lastDiagnostic = _lastDiagnostic,
        lastDiagnosticId = _lastDiagnosticId,
        lastDiagnosticReturn = _lastDiagnosticReturn
    };
    internal string? MainUtilityCommandSaveBlocker => _consoleSubmissionEntered is not null ? "source-console-submission-entered" :
        _consoleSubmissionLast is { Phase: FalloutMainConsoleSubmissionPhase.Failed } failed ? "source-console-submission:" + failed.Error : null;
    internal void ConstructMainUtilityCommands(FalloutMainUtilityCommandSource source, FalloutConsoleActivitySource consoleSource,
        FalloutMainUtilityCommandSnapshot? restore)
    {
        RequireNotBusy(); source.Validate(); consoleSource.Validate();
        if (_utilityCommandSource is not null || source.Main != MainUtilitySource || _utilityCommandLease != Guid.Empty)
            throw new InvalidOperationException("Main utility command state must use its exact loader/source process once.");
        if (consoleSource.EngineSha256 != source.Main.Main.EngineSha256 || consoleSource.RuntimeSha256 != source.Main.Main.RuntimeSha256)
            throw new InvalidDataException("Source command and console controller came from different actual runtime declarations.");
        if (restore is null)
        {
            _utilityCommandSource = source; _achievementSuppression = source.InitialSuppression;
            _utilityCommandConstructed = Next();
        }
        else
        {
            ValidateMainUtilityCommands(restore);
            if (restore.Source != source || restore.ConsoleSource != consoleSource || restore.Stack != _stack || restore.CapturedProcess == _process ||
                _cold?.PreviousProcess != restore.CapturedProcess || restore.Changed > _sequence)
                throw new InvalidDataException("Cold utility command state changed its actual captured source/Main epoch.");
            if (restore.Suppression != source.InitialSuppression)
                throw new NotSupportedException("Original console-latched process byte has no proved saved-game restoration producer.");
            _utilityCommandSource = source; _achievementSuppression = restore.Suppression;
            _utilityCommandConstructed = restore.Constructed; _consoleSubmissions = restore.Submissions;
            _consoleSubmissionLast = restore.LastSubmission; _diagnosticCalls = restore.DiagnosticCalls;
            _lastDiagnostic = restore.LastDiagnostic; _lastDiagnosticId = restore.LastDiagnosticId;
            _lastDiagnosticReturn = restore.LastDiagnosticReturn;
        }
        _utilityConsoleSource = consoleSource;
        _ownedUtilityCommandBinding = BindMainUtilityCommandHost(this);
    }
    bool IFalloutMainUtilityCommandHost.AchievementSuppressionByte()
    {
        RequireNotBusy(); _ = UtilityCommandSource(); return _achievementSuppression != 0;
    }
    void IFalloutMainUtilityCommandHost.OutOfRangeAchievement(int actualSignedId)
    {
        RequireNotBusy(); var source = UtilityCommandSource();
        if (actualSignedId <= 100) throw new InvalidDataException("Source range diagnostic bypassed its genuine signed greater-than-100 branch.");
        _diagnosticCalls = checked(_diagnosticCalls + 1); _lastDiagnosticId = actualSignedId;
        _lastDiagnosticReturn = source.DiagnosticReturn; _lastDiagnostic = Next();
        // Selected original callee returns zero and has no effects. This is
        // the source-declared no-op, not a missing ConsoleMenu/print callback.
    }
    private FalloutMainUtilityCommandSource UtilityCommandSource()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _utilityCommandSource ?? throw new NotSupportedException("source-Main-achievement-suppression-loader-constructor-unbound");
    }
    internal bool SubmitSourceConsole(FalloutMainConsoleInput input)
    {
        RequireNotBusy(); var source = UtilityCommandSource(); ArgumentNullException.ThrowIfNull(input);
        if (string.IsNullOrWhiteSpace(input.Producer) || _consoleSubmissionEntered is not null ||
            _consoleSubmissionLast?.Phase == FalloutMainConsoleSubmissionPhase.Failed)
            throw new InvalidOperationException("Console submission retains an entered/failed source input consumer.");
        input.Activity.Validate(_utilityConsoleSource ?? throw new InvalidOperationException("Source console declaration was not constructed."));
        if (input.Activity.Interface != FalloutConsolePresence.Present || input.Activity.InterfaceEnabled != true ||
            input.Activity.Console != FalloutConsolePresence.Present || input.Activity.OpenCounter is null)
            throw new NotSupportedException("Console submission has no actual published interface/console controller.");
        if (input.Activity.OpenCounter <= 0 || input.Event == 0) return false;
        if (input.Event != source.SubmitEvent) throw new NotSupportedException("Console input event requires its genuine adjacent edit/control consumer.");
        var identity = Guid.NewGuid(); var entered = Next(); _consoleSubmissions = checked(_consoleSubmissions + 1);
        _consoleSubmissionLast = new(identity, _process, input.Producer, entered, entered,
            Convert.ToHexString(SHA256.HashData(input.EditBytes.Span)).ToLowerInvariant(), null,
            FalloutMainConsoleSubmissionPhase.Entered, _achievementSuppression, _achievementSuppression, null);
        try
        {
            var normalized = FalloutConsoleSourceEdit.Normalize(source, input.EditBytes.Span);
            _consoleSubmissionLast = _consoleSubmissionLast with
            {
                Changed = Next(),
                Phase = FalloutMainConsoleSubmissionPhase.Normalized,
                NormalizedSha256 = Convert.ToHexString(SHA256.HashData(normalized)).ToLowerInvariant()
            };
            if (normalized.Length == 0)
            {
                _consoleSubmissionLast = _consoleSubmissionLast with { Changed = Next(), Phase = FalloutMainConsoleSubmissionPhase.Returned };
                return true;
            }
            _achievementSuppression = 1;
            _consoleSubmissionLast = _consoleSubmissionLast with
            {
                Changed = Next(),
                Phase = FalloutMainConsoleSubmissionPhase.SuppressionCommitted,
                SuppressionAfter = _achievementSuppression
            };
            _consoleSubmissionEntered = new(this, identity, normalized, input.Producer);
            _consoleSubmissionLast = _consoleSubmissionLast with { Changed = Next(), Phase = FalloutMainConsoleSubmissionPhase.ExecutionEntered };
            // Console text compiles through a distinct original producer. No
            // source parser, action callback or matching-looking receipt can
            // manufacture that producer or turn this committed byte into a
            // completed command. Preserve the entered source suffix visibly.
            throw new NotSupportedException("source-console-original-compilation-and-execution-producer-unbound");
        }
        catch (Exception error)
        {
            _consoleSubmissionLast = _consoleSubmissionLast! with
            {
                Changed = Next(),
                Phase = FalloutMainConsoleSubmissionPhase.Failed,
                SuppressionAfter = _achievementSuppression,
                FailureType = error.GetType().FullName,
                Error = Message(error)
            };
            throw;
        }
        finally { _consoleSubmissionEntered = null; }
    }
    internal void RequireConsoleSubmission(FalloutMainConsoleSubmission submission, FalloutActorProcessRuntimeState actualOwner)
    {
        RequireNotBusy();
        if (!ReferenceEquals(this, actualOwner) || !ReferenceEquals(_consoleSubmissionEntered, submission) ||
            _consoleSubmissionLast?.Identity != submission.Identity || _consoleSubmissionLast.Phase != FalloutMainConsoleSubmissionPhase.ExecutionEntered)
            throw new InvalidDataException("Console consumer lost its exact entered source input/committed suppression lease.");
    }
    internal void RetireMainUtilityCommands()
    {
        if (_consoleSubmissionEntered is not null) throw new InvalidOperationException("Source console execution owns its command producer.");
        _ownedUtilityCommandBinding?.Dispose(); _ownedUtilityCommandBinding = null;
    }
}
