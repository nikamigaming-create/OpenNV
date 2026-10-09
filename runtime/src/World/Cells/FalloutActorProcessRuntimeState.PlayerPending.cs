using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutActorProcessRuntimeState
{
    private FalloutMainPlayerPendingState? _mainPlayerPendingConsumers;
    internal FalloutMainPlayerCellSource MainPlayerCellSource => _mainPlayerCellSource ??
        throw new NotSupportedException("Actual selected Main Player child has no constructor.");
    internal FalloutMainPlayerPendingState MainPlayerPendingConsumers => _mainPlayerPendingConsumers ??
        throw new NotSupportedException("Actual Player pending/refcount/loader fields have no source constructor.");
    private void ConstructMainPlayerPendingConsumers(FalloutMainPlayerCellSource source, FalloutMainPlayerPendingSnapshot? saved) =>
        _mainPlayerPendingConsumers = new(FalloutMainPlayerPendingSource.Read(source), _stack, _process, saved);
    internal static void RequirePendingConsumersContinuation(FalloutMainPlayerCellSnapshot saved)
    {
        FalloutMainPlayerPendingState.Validate(saved.PendingConsumers);
        if (saved.PendingConsumers.Source.Player != saved.Source || saved.PendingConsumers.Stack != saved.Stack ||
            saved.PendingConsumers.CapturedProcess != saved.CapturedProcess)
            throw new InvalidDataException("Player caller and pending consumers do not share the actual captured source process.");
    }
}
