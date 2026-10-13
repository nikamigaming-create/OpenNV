using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    private void BindStandalonePlayerScene(FalloutReferenceWorld world)
    {
        if (!world.HasStandalonePlayerScene) return;
        (_nativePlayer ?? throw new InvalidOperationException("Main scene binding has no actual native player.")).BindStandalonePlayerScene(world);
        (_nativeGameplayHud ?? throw new InvalidOperationException("Main scene binding has no actual native HUD.")).BindStandaloneReferenceHud(world.CampaignStandalonePlayerScene);
        world.CampaignStandalonePlayerScene.BindInputManager(
            (_nativeDirectInput ?? throw new InvalidOperationException("Main scene binding has no actual product input manager.")).PlayerManager());
        world.CampaignStandalonePlayerScene.BindMovementControlConfiguration(
            (_nativePluginStack ?? throw new InvalidOperationException("Player controls lost the winning record/INI selection.")).IniSettings);
        world.CampaignStandalonePlayerScene.BindActorProcessQueries(world.ReadStandaloneSceneProcessQueries);
        world.CampaignStandalonePlayerScene.BindProcessSceneUpdate(world.UpdateStandalonePlayerProcessScene);
        world.CampaignStandalonePlayerScene.BindPostProcessContext(world.ReadStandalonePlayerSceneContext);
        world.CampaignStandalonePlayerScene.BindTimedActionCondition(world.ReadStandalonePlayerActionCondition);
        world.CampaignStandalonePlayerScene.BindPostActions(world.UpdateStandalonePlayerPostActions, world.UpdateStandalonePlayerDetectionAction);
        world.CampaignStandalonePlayerScene.BindActorDistances(world.StandaloneInterface, world.ReadStandaloneActorDistanceLimit,
            world.ReadStandaloneActorDistance, world.ReadStandaloneActorDistanceTimerSetting);
        world.CampaignStandalonePlayerScene.BindIdleAnimation(
            read => world.UpdateStandalonePlayerIdleAnimation(world.CampaignStandalonePlayerScene, read),
            () => world.LastStandalonePlayerIdleAnimation);
        world.BindStandaloneIdleActorValues(value => (_nativeOpeningStageDriver ??
            throw new InvalidOperationException("Player idle actor value lost its actual campaign gameplay owner.")).PlayerCombatValue(value));
        world.CampaignStandalonePlayerScene.BindMovementFlags(world.ReadAndClearStandalonePlayerMovementFlags);
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
            () => world.CampaignPlayerPendingConsumers.ReleaseSceneOwnedChild(invocation),
            (_nativeGameplayHud ?? throw new InvalidOperationException("Player scene lost its actual native HUD.")).ReferenceHud(scene),
            () => world.ReadStandalonePlayerScaledSeconds(invocation));
        // ExecuteInitialVirtualPrefix retains the next real missing original
        // child. No presentation _Process or borrowed skeleton is substituted
        // for a returned original Player virtual.
    }
}
