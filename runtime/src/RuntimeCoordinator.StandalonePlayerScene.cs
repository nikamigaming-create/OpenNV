using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.Presentation.Rendering;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    private RuntimeNativeWorldProjection? _nativeWorldProjectionTarget;
    private FalloutReferenceWorld? _nativeWorldProjectionWorld;

    private void PrepareStandalonePlayerOwners(FalloutReferenceWorld world)
    {
        if (!world.HasStandalonePlayerScene) return;
        if (_nativeWorldProjectionTarget is not null || _nativeWorldProjectionWorld is not null)
            throw new InvalidOperationException("Player construction still owns another actual World projection target.");
        var scene = world.CampaignStandalonePlayerScene;
        var source = world.PrepareStandaloneWorldOrientationSource();
        bool Living() => ReferenceEquals(_nativeReferences, world) && IsInsideTree() && !IsQueuedForDeletion();
        _nativeWorldProjectionWorld = world;
        var target = new RuntimeNativeWorldProjection(source, scene.Stack, world.StandaloneWorldProjectionIdentity, GetViewport(), Living);
        _nativeWorldProjectionTarget = target;
        var publication = world.ConstructStandaloneWorldProjection(target.Read, Living, world.PreparedStandaloneWorldProjectionRestore);
        var savedOrientation = scene.Capture().ReferenceOrientation;
        var orientation = new FalloutPlayerReferenceOrientationOwner(source, scene.Stack, scene.Process,
            world.ReadStandalonePlayerReferenceOrientationBinding(), publication, scene.ControlFields, Living,
            scene.RequireMovementOrientationInvocation, savedOrientation);
        scene.BindPlayerReferenceOrientation(orientation);
        if (savedOrientation is null)
            orientation.InitializePlayerFov((_nativePluginStack ??
                throw new InvalidOperationException("Player constructor lost its winning INI owner.")).IniSettings);
        world.PrepareStandalonePlayerControlEffects(_nativeUi ??
            throw new InvalidOperationException("Player controls have no actual source UI store."),
            (_nativeDirectInput ?? throw new InvalidOperationException("Player controls have no actual current input manager.")).PlayerManager());
    }

    private void RetireStandalonePlayerOwners(FalloutReferenceWorld world)
    {
        world.RetireStandalonePlayerControlEffects();
        world.RetireStandaloneWorldProjection();
        if (_nativeWorldProjectionWorld is null) return;
        if (!ReferenceEquals(_nativeWorldProjectionWorld, world))
            throw new InvalidOperationException("Projection retirement selected a different actual source world.");
        _nativeWorldProjectionTarget?.Retire();
        _nativeWorldProjectionTarget = null;
        _nativeWorldProjectionWorld = null;
    }

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
