using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal partial class RuntimeNativeOpeningStageDriver
{
    // This composes with the source-neutral advancement activity packet.
    // No generic HUD registration, save scheduling or XP threshold supplies
    // NotificationSequenceSettled; only the living notification owner does.
    internal FalloutAdvancementActivityObservation ObserveExperienceNotificationActivity()
    {
        if (_experienceNotifications is null)
            return new(FalloutAdvancementActivityState.Unowned, "experience-notification-owner-absent");
        var observation = _experienceNotifications.Observe();
        return new(observation.State switch
        {
            FalloutExperienceNotificationFact.Satisfied => FalloutAdvancementActivityState.Satisfied,
            FalloutExperienceNotificationFact.Held => FalloutAdvancementActivityState.Held,
            FalloutExperienceNotificationFact.Unowned => FalloutAdvancementActivityState.Unowned,
            _ => throw new InvalidDataException("Experience notification activity fact is invalid."),
        }, observation.Owner);
    }
}
