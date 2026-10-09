using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal partial class RuntimeNativeOpeningStageDriver
{
    internal object? SourceProcessQueueState => _scripts.References?.SourceProcessQueueState;
    private string? SourceProcessQueueSaveBlocker => _scripts.References?.SourceProcessQueueSaveBlocker ??
        (_scripts.References is null ? "actual-process-queue-world-absent" : null);
    private void ConfigureCurrentProcessQueues(FalloutProcessQueueSnapshots? restore)
    {
        var source = _pluginStack.OwnedSource ?? throw new NotSupportedException("Process queues have no exact selected source.");
        (_scripts.References ?? throw new NotSupportedException("Process queues have no actual shared reference world."))
            .ConfigureSourceProcessQueues(FalloutActorProcessQueueDeclaration.ForExecutable(
                FalloutActorProcessDeclaration.Read(source.FalloutExecutablePath).ExecutableSha256), source.StackId, restore);
    }
    private FalloutProcessQueueSnapshots CaptureCurrentProcessQueues() =>
        (_scripts.References ?? throw new NotSupportedException("Current process queue capture has no shared reference world."))
            .CaptureSourceProcessQueues();
}
