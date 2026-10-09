using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal partial class RuntimeNativeOpeningStageDriver
{
    private Action? _observeCurrentNativeCellProcesses;
    internal object? ActorUpdateState => _scripts.References?.ActorUpdateState;
    internal object? CellProcessState => _scripts.References?.CellProcessState;
    internal string? ActorUpdateSaveBlocker => _scripts.References is { } world ? world.ActorUpdateSaveBlocker : "source-actor-update-world-absent";
    internal string? CellProcessSaveBlocker => _scripts.References is { } world ? world.CellProcessSaveBlocker : "source-CELL-process-world-absent";
    internal void ConfigureSourceActorCellProducers(FalloutActorUpdateSnapshot? actors, FalloutCellProcessesSnapshot? cells)
    {
        var source = _pluginStack.OwnedSource ?? throw new InvalidOperationException("Actor/CELL constructor has no exact selected source.");
        (_scripts.References ?? throw new InvalidOperationException("Actor/CELL constructor has no real world."))
            .ConfigureActualActorCellProducers(source.StackId, actors, cells);
    }
    internal void BindCurrentNativeCellCapture(Action observe)
    {
        ArgumentNullException.ThrowIfNull(observe);
        if (_observeCurrentNativeCellProcesses is not null)
            throw new InvalidOperationException("Current CELL native capture owner is already attached.");
        _observeCurrentNativeCellProcesses = observe;
    }
    private void ObserveCurrentNativeCellProcessesForCapture() =>
        (_observeCurrentNativeCellProcesses ?? throw new NotSupportedException("Current CELL capture has no actual native attachment/lifetime observation."))();
    private FalloutActorUpdateSnapshot CaptureSourceActorUpdates()
    {
        if (ActorUpdateSaveBlocker is { } failure) throw new NotSupportedException(failure);
        return _scripts.References!.CaptureActorUpdates();
    }
    private FalloutCellProcessesSnapshot CaptureSourceCellProcesses()
    {
        if (CellProcessSaveBlocker is { } failure) throw new NotSupportedException(failure);
        return _scripts.References!.CaptureCellProcesses();
    }
    private void RetireCurrentNativeCellCapture() => _observeCurrentNativeCellProcesses = null;
}
