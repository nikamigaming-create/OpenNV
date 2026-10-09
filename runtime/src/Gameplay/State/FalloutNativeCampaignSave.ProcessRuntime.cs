using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal static partial class FalloutNativeCampaignSave
{
    private static void ValidateActualProcessRuntimeContinuation(FalloutNativeCampaignState state) =>
        FalloutActorProcessRuntimeSaveContract.ValidateShape(state.ActorProcessRuntime, state.ActorProcessCommon, state.ActorProcesses);
    private static void ValidateActualProcessRuntimeSource(FalloutPluginStack records, FalloutNativeCampaignState state) =>
        FalloutActorProcessRuntimeSaveContract.ValidateSource(records, state);
}
