using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    private Exception? _campaignSharedScriptConstructionFailure;
    internal bool CampaignSharedScriptRuntimeConfigured => _scriptEngineContexts is not null && _processRuntime?.MainScriptCallerConstructed == true &&
        _processRuntime.MainPlayerCellConstructed && _processRuntime.MainUtilityCommandsConstructed;
    internal object? CampaignMainScriptCallerState => _processRuntime?.MainScriptCallerState;
    internal string? CampaignSharedScriptSaveBlocker => CampaignChallengesConfigured && Challenges.Source is not null ?
        _campaignSharedScriptConstructionFailure is not null ? "source-shared-script-construction-failed:" + _campaignSharedScriptConstructionFailure.Message :
        !CampaignSharedScriptRuntimeConfigured ? "source-shared-script-context-Main-caller-construction-unbound" :
        _scriptEngineContexts!.SaveBlocker ?? ProcessRuntime.MainScriptCallerSaveBlocker ?? ProcessRuntime.MainPlayerCellSaveBlocker ?? ProcessRuntime.MainUtilitySaveBlocker ??
            ProcessRuntime.MainUtilityCommandSaveBlocker ?? ProcessRuntime.PlatformStartupSaveBlocker ?? ProcessRuntime.SourceScriptFrameSaveBlocker : null;
    internal void ConfigureCampaignSharedScripts(FalloutAdvancementRuntimeSource runtime, FalloutSharedScriptRuntimeSnapshot? restore)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_campaignSharedScriptConstructionFailure is { } retained) throw new InvalidOperationException("Shared script construction retains its actual attempted prefix.", retained);
        if (!CampaignChallengesConfigured) throw new InvalidOperationException("Shared scripts must follow actual campaign registry construction.");
        if (Challenges.Source is null)
        {
            if (restore is not null) throw new InvalidDataException("Source-absent family has a foreign immediate-script/Main continuation.");
            return;
        }
        if (!ActualProcessRuntimeConfigured) throw new InvalidOperationException("Shared scripts must reuse the genuine constructed Main runtime before bootstrap.");
        if ((_processRuntimeRestore is null) != (restore is null)) throw new InvalidDataException("Current cold shared script state is mandatory; an omitted owner cannot become a fresh constructor.");
        if (restore is not null) restore.Validate(_processRuntimeRestore ?? throw new InvalidDataException("Cold shared scripts have no actual captured Main runtime."));
        try
        {
            ConfigureCampaignScriptContexts(runtime, restore?.Contexts, restore?.MainField, restore?.MainCaller);
            ProcessRuntime.ConstructMainScriptCaller(FalloutMainScriptCallerSource.Read(FalloutImmediateScriptSource.Read(runtime.Receipt)), restore?.MainCaller);
            if (restore is not null) ProcessRuntime.RestoreMainUtilities(restore.Utilities);
            var utility = FalloutMainUtilitySource.Read(CampaignMainScriptSource);
            ConstructCampaignUtilityCommands(FalloutExecutableStringTable.ReadMainUtilityCommandSource(runtime.OwnedSource.FalloutExecutablePath, utility),
                FalloutConsoleActivitySource.Read(runtime.Receipt), restore?.UtilityCommands);
            if (restore is not null) RequireMainPlayerColdSources(restore.PlayerCell);
            ProcessRuntime.ConstructMainPlayerCell(CampaignMainPlayerCellSource, PlayerMoves.SourcePending, restore?.PlayerCell);
        }
        catch (Exception failure) { _campaignSharedScriptConstructionFailure = failure; throw; }
    }
    internal FalloutMainScriptCallerSource CampaignMainScriptSource => FalloutMainScriptCallerSource.Read(
        FalloutImmediateScriptSource.Read(CampaignPlayerRuntimeSource.Receipt));
    internal IDisposable BindCampaignMainScriptCaller(IFalloutMainScriptCallerConsumers consumers, string deliveryOwner) =>
        ProcessRuntime.BindMainScriptCaller(consumers, deliveryOwner);
    internal Task ExecuteCampaignMainScriptCaller(ulong actualDeliveredFrame, float deliveredSeconds) =>
        ProcessRuntime.ExecuteMainScriptCaller(actualDeliveredFrame, deliveredSeconds);
    internal FalloutSharedScriptRuntimeSnapshot? CaptureCampaignSharedScripts()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Challenges.Source is null) return null;
        if (!CampaignSharedScriptRuntimeConfigured) throw new NotSupportedException("source-shared-script-context-Main-caller-construction-unbound");
        var caller = ProcessRuntime.CaptureMainScriptCaller(); var field = ProcessRuntime.CaptureMainScriptFrameEvidence();
        var result = new FalloutSharedScriptRuntimeSnapshot(FalloutSharedScriptRuntimeSnapshot.CurrentSchema,
            _scriptEngineContexts!.Capture(), field, caller, ProcessRuntime.CaptureMainUtilities(), ProcessRuntime.CaptureMainPlayerCell(), CaptureCampaignUtilityCommands());
        result.Validate(ProcessRuntime.Capture()); return result;
    }
}
