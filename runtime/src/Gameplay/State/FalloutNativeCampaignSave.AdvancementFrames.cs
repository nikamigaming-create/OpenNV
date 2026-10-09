using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal static partial class FalloutNativeCampaignSave
{
    private static void ValidateAdvancementFrameContinuation(FalloutNativeCampaignState state)
    {
        var notifications = state.ExperienceNotifications ??
            throw new InvalidDataException("Current campaign experience frame continuation is absent.");
        FalloutExperienceFrameGate.Validate(notifications.FrameGate);
        FalloutInterfaceActivationFrame.Validate(state.InterfaceActivationFrames ??
            throw new InvalidDataException("Current campaign interface frame continuation is absent."));
        if (notifications.FrameGate.Ready)
        {
            var actualMenu = state.PlayerProgress?.Advancement?.Menu;
            var actualLevel = state.Vitals?.Level ?? throw new InvalidDataException("Current player level is absent.");
            var requestedLevel = actualMenu is null ? checked(actualLevel + 1) : actualMenu.Level;
            if (notifications.FrameGate.CompletedLevel != requestedLevel || actualMenu is not null && actualMenu.Level != actualLevel)
                throw new InvalidDataException("Saved original HUD completion differs from its actual pending or admitted level.");
        }
    }

    private static void ValidateAdvancementFrameSource(FalloutNativeCampaignState state,
        FalloutExperienceHudDeclaration experience)
    {
        var source = FalloutAdvancementFrameDeclaration.Read(experience);
        if (state.ExperienceNotifications?.FrameGate.Contract != source.Contract ||
            state.InterfaceActivationFrames?.Contract != source.Contract)
            throw new InvalidDataException("Saved frame predicates differ from their selected original source producers.");
    }
}
