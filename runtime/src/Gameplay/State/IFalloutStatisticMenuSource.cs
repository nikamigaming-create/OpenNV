using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

// A later actual menu lifetime may bind to the already-constructed campaign
// counters. It must validate its living probe and refresh receipts itself;
// object absence elsewhere in the UI is not the original singleton test.
internal interface IFalloutStatisticMenuSource
{
    void RequireSource(FalloutMiscellaneousStatisticSource source);
    FalloutStatisticMenuObservation Observe(FalloutPlayerStatistics statistics, uint menu);
    void RequireCurrent(FalloutStatisticMenuObservation observation);
    FalloutStatisticRefreshReceipt Refresh(FalloutPlayerStatistics statistics,
        FalloutStatisticMutation mutation, FalloutStatisticMenuObservation observation);
    void RequireCurrent(FalloutStatisticRefreshReceipt receipt);
}
