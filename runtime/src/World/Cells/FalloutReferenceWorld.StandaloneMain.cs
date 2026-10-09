using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    private void ConfigureStandaloneMain(FalloutAdvancementRuntimeSource runtime)
    {
        if (!ActualProcessRuntimeConfigured)
            throw new InvalidOperationException("FO3 Main must reuse the actual source process before bootstrap.");
        var source = FalloutMainScriptCallerSource.Read(FalloutImmediateScriptSource.Read(runtime.Receipt));
        if (!FalloutSourceMainFamily.IsFallout3(source.EngineSha256) || Challenges.Source is not null)
            throw new InvalidDataException("Standalone Main changed its actual selected source family.");
        var restore = _processRuntimeRestore?.StandaloneMain;
        if ((_processRuntimeRestore is null) != (restore is null))
            throw new InvalidDataException("Cold FO3 Main cannot turn an omitted owner into a fresh constructor.");
        if (restore is not null) RequireMainPlayerColdSources(restore.PlayerCell);
        ProcessRuntime.ConstructStandaloneMain(source, PlayerMoves.SourcePending, restore, _processRuntimeRestore);
    }
    private string? StandaloneMainSaveBlocker => _campaignSharedScriptConstructionFailure is not null ?
        "source-FO3-Main-construction-failed:" + _campaignSharedScriptConstructionFailure.Message :
        _processRuntime?.StandaloneMainConstructed != true ? "source-FO3-Main-Player-construction-unbound" :
        ProcessRuntime.MainScriptCallerSaveBlocker ?? ProcessRuntime.MainPlayerCellSaveBlocker ?? ProcessRuntime.SourceScriptFrameSaveBlocker;
}
