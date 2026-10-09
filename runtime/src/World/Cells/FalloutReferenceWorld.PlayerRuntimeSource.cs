using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    private FalloutAdvancementRuntimeSource? _campaignPlayerRuntimeSource;
    internal FalloutAdvancementRuntimeSource CampaignPlayerRuntimeSource
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _campaignPlayerRuntimeSource ?? throw new NotSupportedException("The campaign player runtime source is absent.");
        }
    }

    internal void ConfigureCampaignPlayerRuntime(FalloutPlayerStatisticsSnapshot? restore = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_campaignPlayerRuntimeSource is not null) throw new InvalidOperationException("Campaign source lifetime cannot be replaced.");
        var source = FalloutAdvancementRuntimeSource.Open(records);
        try
        {
            ConfigurePlayerStatistics(source.Receipt, new(null, null, null), restore);
            _campaignPlayerRuntimeSource = source;
        }
        catch (Exception failure)
        {
            try { source.Dispose(); }
            catch (Exception cleanup) { throw new AggregateException(failure, cleanup); }
            throw;
        }
    }

    private void RetireCampaignPlayerRuntime() => _campaignPlayerRuntimeSource?.Dispose();
}
