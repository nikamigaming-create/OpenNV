using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal partial class RuntimeNativeOpeningStageDriver
{
    private IReadOnlyList<FalloutQuestStageResultSnapshot>? _restoreStageResults;
    private FalloutQuestStageDriverFailure? _restoreStageResultFailure;
    private FalloutQuestStageDriverFailure? _stageResultDriverFailure;

    private string? StageResultsSaveBlocker => _stageResults is null ? "quest-stage-result-owner" :
        _stageResults.HasPendingResults ? "quest-stage-results" :
        BlockingExecutionError is not null ? "native-driver-failure" : null;

    private IReadOnlyList<FalloutQuestStageResultSnapshot> CaptureStageResults()
    {
        if (StageResultsSaveBlocker is { } blocker)
            throw new NotSupportedException($"Saving {blocker} requires its exact source continuation.");
        var results = _stageResults!.CaptureResults();
        FalloutQuestStages.ValidateDriverFailure(results, _stageResultDriverFailure);
        return results;
    }

    private void RestoreStageResults()
    {
        var results = _restoreStageResults ?? [];
        // Campaign Read has already joined full stack compatibility, winning
        // source hashes and entered quest state. Repeat source validation at
        // attachment before exposing a native stopped driver.
        FalloutQuestStages.ValidateDriverFailure(results, _restoreStageResultFailure);
        _stageResults!.RestoreResults(results);
        if (_restoreStageResultFailure is { } failure)
        {
            ExecutionError = failure.Error;
            _stageResultDriverFailure = failure;
        }
        _restoreStageResults = null;
        _restoreStageResultFailure = null;
        RestoreTerminalResults();
    }

    private void RetainDriverFailure(Exception error)
    {
        ExecutionError = error.Message;
        // Exact exception identity proves this failed invocation came through
        // the stage owner. An unrelated native fault with equal text is not a
        // source-stage receipt and remains an explicit save refusal.
        _stageResultDriverFailure = _stageResults?.ClosedFailureFor(error);
    }
}
