using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal static partial class FalloutNativeCampaignSave
{
    private static void ValidateActorCellProcessSource(FalloutPluginStack records, FalloutNativeCampaignState state)
    {
        var source = records.OwnedSource ?? throw new InvalidDataException("Current actor/CELL save has no selected owned source.");
        var executable = FalloutCombatGroupDeclaration.ReadExecutable(source.FalloutExecutablePath).ExecutableSha256;
        FalloutActorUpdateCellSaveContract.Validate(records, executable, source.StackId,
            state.References ?? throw new InvalidDataException("Current actor/CELL save has no actual reference state."),
            state.ActorUpdates, state.CellProcesses);
    }
}
