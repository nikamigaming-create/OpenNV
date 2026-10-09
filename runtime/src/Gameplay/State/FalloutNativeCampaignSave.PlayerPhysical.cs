using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Actors;

namespace OpenNV.Runtime.Gameplay.State;

internal static partial class FalloutNativeCampaignSave
{
    private static void ValidatePlayerPhysicalSource(FalloutPluginStack records, FalloutNativeCampaignState state)
    {
        var physical = state.PlayerPhysical ??
            throw new InvalidDataException("Current campaign has no complete physical player owner.");
        using var source = FalloutAdvancementRuntimeSource.Open(records);
        physical.Source.RequireCurrent(FalloutPlayerPhysicalSource.Read(records, source.Receipt));
        FalloutAnimationSoundEvents.ValidateSource(physical.Sounds, records, records.RuntimeFormKey(0x14));
    }
}
