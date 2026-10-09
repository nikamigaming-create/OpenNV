using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    internal bool CampaignUtilityCommandsConstructed => _processRuntime?.MainUtilityCommandsConstructed == true;
    internal bool SourcePlatformStartupReturned => _processRuntime?.PlatformStartupReturned == true;
    internal object? MainUtilityCommandState => _processRuntime?.MainUtilityCommandState;
    internal string? MainUtilityCommandSaveBlocker => _processRuntime?.MainUtilityCommandSaveBlocker ?? _processRuntime?.PlatformStartupSaveBlocker;
    internal object? SourcePlatformStartupState => _processRuntime?.PlatformStartupState;
    internal void ConstructCampaignUtilityCommands(FalloutMainUtilityCommandSource source, FalloutConsoleActivitySource console,
        FalloutMainUtilityCommandSnapshot? restore) => ProcessRuntime.ConstructMainUtilityCommands(source, console, restore);
    internal FalloutMainUtilityCommandSnapshot CaptureCampaignUtilityCommands() => ProcessRuntime.CaptureMainUtilityCommands();
    internal bool SubmitSourceConsole(FalloutMainConsoleInput actualInput) => ProcessRuntime.SubmitSourceConsole(actualInput);
    internal void ExecuteSourcePlatformStartup(FalloutPlatformStartupArgumentSource source, FalloutSourceLaunchArguments actualArguments,
        IFalloutPlatformStartupConsumer actualService) => ProcessRuntime.ExecuteSourcePlatformStartup(source, actualArguments, actualService);
    internal void RetireCampaignUtilityCommands() => _processRuntime?.RetireMainUtilityCommands();
}
