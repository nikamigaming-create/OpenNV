using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal partial class RuntimeNativeOpeningStageDriver
{
    internal object? ActualProcessRuntimeState => _scripts.References?.ActualProcessRuntimeState;
    internal object? ActualProcessCommonState => _scripts.References?.ActualProcessCommonState;
    private string? ActualProcessRuntimeSaveBlocker => _scripts.References is { } world ?
        world.ActualProcessRuntimeSaveBlocker : "actual-process-runtime-world-absent";
    private string? ActualProcessCommonSaveBlocker => _scripts.References is { } world ?
        world.ActualProcessCommonSaveBlocker : "actual-process-common-world-absent";
    private void ConfigureCurrentProcessRuntime(FalloutActorProcessRuntimeSnapshot? runtime, FalloutProcessCommonSnapshot? common)
    {
        var source = _pluginStack.OwnedSource ?? throw new NotSupportedException("Process runtime has no exact selected source.");
        var world = _scripts.References ?? throw new NotSupportedException("Process runtime has no actual shared reference world.");
        var selected = FalloutActorProcessRuntimeDeclaration.ForExecutable(
            FalloutActorProcessDeclaration.Read(source.FalloutExecutablePath).ExecutableSha256);
        world.ConfigureActualProcessRuntime(selected, source.StackId, runtime, common);
    }
    private FalloutActorProcessRuntimeSnapshot CaptureCurrentProcessRuntime()
    {
        if (ActualProcessRuntimeSaveBlocker is { } blocker) throw new NotSupportedException(blocker);
        return _scripts.References!.CaptureActualProcessRuntime();
    }
    private FalloutProcessCommonSnapshot CaptureCurrentProcessCommon()
    {
        if (ActualProcessCommonSaveBlocker is { } blocker) throw new NotSupportedException(blocker);
        return _scripts.References!.CaptureActualProcessCommon();
    }
}
