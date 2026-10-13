using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Gameplay.State;

internal static partial class FalloutNativeCampaignSave
{
    private static void ValidateCurrentWorldProjection(FalloutPluginStack records, FalloutNativeCampaignState state)
    {
        FalloutWorldProjectionSaveContract.ValidateShape(state.WorldProjection, state.ActorProcessRuntime);
        if (state.WorldProjection is not { } projection) return;
        var runtime = state.ActorProcessRuntime!;
        var owned = records.OwnedSource ?? throw new InvalidDataException("Saved World projection has no actual owned source selection.");
        var source = FalloutExecutableStringTable.ReadPlayerWorldOrientation(owned.FalloutExecutablePath,
            FalloutPlayerMovementControlSource.Reviewed(runtime.StandaloneMain!.Source));
        var reference = runtime.StandaloneMain.Scene.ReferenceOrientation!;
        var player = FalloutCombatActorSource.Read(records, FalloutCombatGroupDeclaration.ForExecutable(source.Controls.Main.EngineSha256), runtime.Player);
        var binding = FalloutPlayerReferenceOrientationBinding.Read(records, player.Reference, player.Base, source);
        if (projection.Publication.Source != source.Identity || projection.Publication.Stack != owned.StackId ||
            reference.Source != source || reference.Binding != binding || !player.EnginePlayer)
            throw new InvalidDataException("Saved World/reference projection changed the selected original source or winning Player base.");
    }
}
