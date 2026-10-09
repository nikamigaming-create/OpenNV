using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal partial class RuntimeNativeOpeningStageDriver
{
    internal object? ActorProcessState => _scripts.References?.ActorProcessState;
    internal string? ActorProcessSaveBlocker => _scripts.References is { } world ? world.ActorProcessSaveBlocker : "source-actor-process-world-absent";
    internal void ConfigureSourceActorProcesses(FalloutActorProcessesSnapshot? restore)
    {
        var source = _pluginStack.OwnedSource ?? throw new InvalidOperationException("Actor process has no exact selected source.");
        var declaration = FalloutActorProcessDeclaration.Read(source.FalloutExecutablePath);
        var world = _scripts.References ?? throw new InvalidOperationException("Actor process has no shared source world.");
        if (world.ActorProcessesConfigured) world.RequireActorProcessBinding(declaration, source.StackId, restore);
        else world.ConfigureActorProcesses(declaration, source.StackId, restore);
    }
    private FalloutActorProcessesSnapshot CaptureSourceActorProcesses()
    {
        if (ActorProcessSaveBlocker is { } blocker) throw new NotSupportedException(blocker);
        return _scripts.References!.CaptureActorProcesses();
    }
}
