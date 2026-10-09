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
        ValidateAdvancementFrameSource(state, source.Declaration);
        ValidateCombatGroupSource(records, state, source.Declaration);
        ValidateActorPerceptionSource(records, state);
        ValidateActorProcessSource(records, state);
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
