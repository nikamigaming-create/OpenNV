namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    internal uint ReadStandalonePendingPostNullMainWord(FalloutMainPlayerCellInvocation invocation)
    {
        RequireCampaignMainPlayerInvocation(invocation, FalloutMainPlayerCellStep.PendingFlagQueries);
        return ProcessRuntime.ReadStandalonePendingPostNullMainWord(invocation);
    }
}
