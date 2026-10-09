using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutSharedScriptRuntimeSnapshot(string Schema, FalloutScriptEngineContextsSnapshot Contexts,
    FalloutMainScriptFrameSnapshot MainField, FalloutMainScriptCallerSnapshot MainCaller,
    FalloutMainUtilitySnapshot Utilities, FalloutMainPlayerCellSnapshot PlayerCell, FalloutMainUtilityCommandSnapshot UtilityCommands)
{
    internal const string CurrentSchema = "opennv-shared-script-runtime/v4";
    internal void Validate(FalloutActorProcessRuntimeSnapshot runtime)
    {
        if (Schema != CurrentSchema || Contexts is null || MainField is null || MainCaller is null || Utilities is null || PlayerCell is null || UtilityCommands is null || runtime is null)
            throw new InvalidDataException("Current script runtime omitted an actual shared context/Main field/caller owner.");
        FalloutScriptEngineContexts.Validate(Contexts); FalloutActorProcessRuntimeState.ValidateMainScriptCaller(MainCaller);
        FalloutActorProcessRuntimeState.RequireMainScriptSamples(MainCaller, MainField);
        FalloutActorProcessRuntimeState.ValidateMainUtilities(Utilities);
        FalloutActorProcessRuntimeState.RequireMainUtilityCaller(Utilities, MainCaller);
        FalloutActorProcessRuntimeState.RequireMainPlayerCellCaller(PlayerCell, MainCaller);
        FalloutActorProcessRuntimeState.ValidateMainUtilityCommands(UtilityCommands);
        if (UtilityCommands.Source.Main.Main != MainCaller.Source || UtilityCommands.Stack != runtime.Stack ||
            UtilityCommands.CapturedProcess != runtime.CapturedProcess || UtilityCommands.Changed > runtime.Sequence)
            throw new InvalidDataException("Utility command continuation changed its actual selected Main/source process prefix.");
        if (PlayerCell.Changed > runtime.Sequence) throw new InvalidDataException("Player child is newer than captured current Main runtime.");
        if (Utilities.CapturedProcess != runtime.CapturedProcess || Utilities.Changed > runtime.Sequence)
            throw new InvalidDataException("Utility continuation changed its actual captured Main process/sequence.");
        if (Contexts.Source != MainCaller.Source.ImmediateSource || MainCaller.Stack != runtime.Stack ||
            MainCaller.CapturedProcess != runtime.CapturedProcess || MainCaller.Changed > runtime.Sequence || MainField.Changed > runtime.Sequence)
            throw new InvalidDataException("Shared script snapshot changed selected source, Main process or captured mutation prefix.");
    }
}
