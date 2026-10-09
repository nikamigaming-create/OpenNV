using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal static partial class FalloutNativeCampaignSave
{
    private static void ValidateCurrentImagePhase(FalloutPluginStack records, FalloutNativeCampaignState state)
    {
        var saved = state.ImagePhaseClock ?? throw new InvalidDataException("Current save omitted the actual image phase cache.");
        var source = records.OwnedSource ?? throw new InvalidDataException("Image phase save has no selected original source.");
        var phase = FalloutExecutableStringTable.ReadDoubleVisionPhase(source.FalloutExecutablePath);
        var globals = FalloutGlobalState.Read(records);
        globals.Restore(state.Globals ?? throw new InvalidDataException("Image phase has no actual saved global owner."));
        var time = new FalloutGameTime(globals, FalloutGameTimeBindings.Read(records), FalloutCalendar.Read(source.FalloutExecutablePath));
        time.Restore(state.GameTime ?? throw new InvalidDataException("Image phase has no actual saved calendar owner."));
        FalloutImageSpacePhaseClock.Validate(saved, phase, time);
        if (saved.CapturedProcess != state.ActorProcessRuntime?.CapturedProcess)
            throw new InvalidDataException("Saved image phase belongs to another actual campaign process epoch.");
    }
}
