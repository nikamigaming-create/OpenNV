using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    private FalloutPlayerStatistics? _campaignPlayerStatistics;
    internal FalloutPlayerStatistics PlayerStatistics
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _campaignPlayerStatistics ?? throw new NotSupportedException("The shared campaign statistic owner is absent.");
        }
    }
    internal bool PlayerStatisticsConfigured => _campaignPlayerStatistics is not null;
    internal object? PlayerStatisticState => _campaignPlayerStatistics?.State;
    internal void ConfigurePlayerStatistics(FalloutAdvancementRuntimeReceipt runtime,
        FalloutPlayerStatisticHost actualSuffix, FalloutPlayerStatisticsSnapshot? restore = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_campaignPlayerStatistics is not null) throw new InvalidOperationException("A campaign cannot replace its source statistic constructor.");
        var source = FalloutMiscellaneousStatisticSource.Read(records, runtime);
        _campaignPlayerStatistics = new(source, actualSuffix, restore);
    }
    private void RetirePlayerStatistics() => _campaignPlayerStatistics?.Retire();
}
