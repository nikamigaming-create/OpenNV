using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal static partial class FalloutNativeCampaignSave
{
    private static void ValidateSaveOrderSource(FalloutPluginStack records, FalloutNativeCampaignState state)
    {
        if (state.SaveOrder is null || state.Scripts is null)
            throw new InvalidDataException("Current campaign capture lacks the actual persistent save queue/script owners.");
        RuntimeSaveRequestOrder.ValidateSnapshot(state.SaveOrder, state.SaveCompatibilityId, records,
            RuntimeSaveRequestColdLoad.SuspendedSlices(state.Scripts));
    }

    private static RuntimeSaveRequestColdLoad ReadSaveOrder(string path, FalloutPluginStack records, FalloutNativeCampaignState state)
    {
        ValidateSaveOrderSource(records, state);
        return RuntimeSaveRequestColdLoad.Read(path, state.SaveCompatibilityId, records, state.SaveOrder!, state.Scripts!);
    }
}
