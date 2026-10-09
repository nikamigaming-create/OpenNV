using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal static partial class FalloutNativeCampaignSave
{
    private static void ValidateCurrentPlayerStatisticSource(FalloutPluginStack records, FalloutNativeCampaignState state)
    {
        var statistics = state.PlayerStatistics ?? throw new InvalidDataException("Current player statistics authority is absent.");
        statistics.Validate();
        using var runtime = FalloutAdvancementRuntimeSource.Open(records);
        statistics.Source.RequireCurrent(FalloutMiscellaneousStatisticSource.Read(records, runtime.Receipt));
    }
}
