using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal partial class RuntimeNativeOpeningStageDriver
{
    private FalloutPlayerStatistics? _playerStatistics;
    internal FalloutPlayerStatistics PlayerStatistics => _playerStatistics ??
        throw new NotSupportedException("The current player has no source-constructed miscellaneous statistic owner.");
    internal object? PlayerStatisticState => _playerStatistics?.State;
    internal string? PlayerStatisticFailure => _playerStatistics?.Failure;
    internal string? PlayerStatisticSaveBlocker => _playerStatistics is null ? "source-statistic-owner-absent" : _playerStatistics.SaveBlocker;

    // Join before any bootstrap/result/quest/reference call; source constructor
    // zero is a real initial value, independent of event or native UI readiness.
    internal void BindCurrentPlayerStatistics(FalloutAdvancementRuntimeReceipt runtime)
    {
        if (_playerStatistics is not null) throw new InvalidOperationException("Statistic source lifetime cannot be replaced.");
        var actual = (_scripts.References ?? throw new NotSupportedException("Statistic binding has no actual source reference world.")).PlayerStatistics;
        actual.Source.RequireCurrent(FalloutMiscellaneousStatisticSource.Read(_pluginStack, runtime));
        _playerStatistics = actual;
        _scriptHost = _scriptHost with { Statistics = _playerStatistics };
        if (_scripts.Host is { } questHost) _scripts.Host = questHost with { Statistics = _playerStatistics };
    }
    internal FalloutPlayerStatisticsSnapshot CaptureCurrentPlayerStatistics() => PlayerStatistics.Capture();

    // This belongs at the original post-player-hours boundary, before world
    // Start. It is one count per genuine source Sleep Start, never per hour.
    private void CommitCurrentRestStartStatistic(FalloutRestRequest request)
    {
        request.Validate();
        if (request.Kind != FalloutRestKind.Sleep || request.Origin == FalloutRestOrigin.ScriptHours) return;
        var rest = PlayerRest;
        // Begin changes Choosing -> Running only after this actual callback.
        if (rest.Request != request || rest.Phase != FalloutRestPhase.Choosing || !rest.Published || !rest.Sleeping ||
            rest.RemainingHours != rest.SelectedHours ||
            rest.RequireCurrentMenuControls().Capture() is not { Counting: true, TargetWrites: 2, NativePublications: 2 })
            throw new InvalidOperationException("Sleep-start statistic lacks the actual committed menu/counting/player-hours prefix.");
        if (PlayerStatistics.Source.SleepStartIndex is { } index)
            PlayerStatistics.Mod(index, 1, "source-sleep-start/request-" + rest.RequestOrdinal);
    }
}
