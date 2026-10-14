using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal static partial class FalloutNativeCampaignSave
{
    private static void ValidateExperienceNotificationSource(FalloutPluginStack records, FalloutNativeCampaignState state)
    {
        var source = new FalloutExperienceHudSource(records, records.OwnedSource ??
            throw new InvalidDataException("Campaign XP notification validation has no selected owned source."));
        if (state.ExperienceNotifications is null || state.ExperienceNotifications.Contract != source.Contract)
            throw new InvalidDataException("Campaign XP notifications differ from the selected executable, menu or settings.");
        if (source.Declaration.ExecutableSha256 == FalloutSourceMainFamily.Fallout3)
        {
            var publication = state.ExperienceNotifications.SourceHud ??
                throw new InvalidDataException("FO3 campaign XP state omitted its actual source HUD constructor publication.");
            FalloutExperienceSourceHud.Validate(publication);
            if (publication.Contract != source.Contract || publication.Stack != records.OwnedSource!.StackId)
                throw new InvalidDataException("FO3 XP source publication changed its selected XML/source graph.");
            var runtime = state.ActorProcessRuntime ??
                throw new InvalidDataException("FO3 XP source publication omitted its actual retained process.");
            var scene = runtime.StandaloneMain?.Scene ??
                throw new InvalidDataException("FO3 XP source publication omitted its actual source Player scene.");
            FalloutExperienceSourceHud.ValidateAssociation(publication,
                scene.ControlHud ?? throw new InvalidDataException("FO3 XP source publication omitted its actual shared HUD."),
                scene.ControlDisplay ?? throw new InvalidDataException("FO3 XP source publication omitted its actual retained display."),
                runtime.CapturedProcess, runtime.Stack);
        }
        ValidateAdvancementFrameSource(state, source.Declaration);
        ValidateCombatGroupSource(records, state, source.Declaration);
        ValidateActorPerceptionSource(records, state);
        ValidateActorProcessSource(records, state);
        ValidateActorCellProcessSource(records, state);
        ValidateActualProcessRuntimeSource(records, state);
        ValidateProcessQueueSource(records, state);
        ValidateSourceOcclusion(records, state.RoomPortals!, state.CellProcesses!, source.Declaration.ExecutableSha256);
    }

    // Current complete-schema shape/capture joins this mandatory snapshot.
    // ConfigureExperienceNotifications independently rebinds its exact selected
    // source contract before a cold invocation or presentation can run.
    private static void ValidateExperienceNotifications(FalloutExperienceNotificationSnapshot notifications,
        GameplayVitals vitals)
    {
        FalloutExperienceNotifications.ValidateSnapshot(notifications); vitals.Validate();
        if (notifications.ObservedExperience != vitals.ExperiencePoints ||
            notifications.Display is { } display && display.Level != vitals.Level ||
            notifications.Pending.Any(change => change.Level != vitals.Level))
            throw new InvalidDataException("Campaign XP notification continuation differs from the actual player publication.");
    }
}
