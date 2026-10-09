using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    private void BindStandalonePlayerScene(FalloutReferenceWorld world)
    {
        if (!world.HasStandalonePlayerScene) return;
        (_nativePlayer ?? throw new InvalidOperationException("Main scene binding has no actual native player.")).BindStandalonePlayerScene(world);
    }
    private void ExecuteStandalonePlayerScene(FalloutMainPlayerCellInvocation invocation, FalloutReferenceWorld world, bool alternate)
    {
        world.RequireCampaignMainPlayerInvocation(invocation, FalloutMainPlayerCellStep.SceneChild);
        if (!ReferenceEquals(world, _nativeReferences)) throw new InvalidOperationException("Player scene changed its actual campaign world.");
        var scene = world.CampaignStandalonePlayerScene;
        if (alternate)
        {
            scene.EnterUnowned("source-Player-free-camera-frame-original-input-manager-and-camera-consumers-unowned");
            return;
        }
        _ = world.EnterStandalonePlayerSceneVirtual(invocation);
        scene.ExecuteInitialVirtualPrefix(invocation.Main.Identity, invocation.Main.Ordinal,
            () => world.CampaignPlayerPendingConsumers.ReleaseSceneOwnedChild(invocation));
        // ExecuteInitialVirtualPrefix retains the next real missing original
        // child. No presentation _Process or borrowed skeleton is substituted
        // for a returned original Player virtual.
    }
}
