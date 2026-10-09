using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal static partial class FalloutNativeCampaignSave
{
    private static void ValidateProcessQueueContinuation(FalloutNativeCampaignState state) =>
        FalloutProcessQueueSaveContract.ValidateShape(state.ProcessQueues, state.ActorProcesses, state.CellProcesses);
    private static void ValidateProcessQueueSource(FalloutPluginStack records, FalloutNativeCampaignState state) =>
        FalloutProcessQueueSaveContract.ValidateSource(records, state.ProcessQueues, state.ActorProcesses, state.CellProcesses);
}
