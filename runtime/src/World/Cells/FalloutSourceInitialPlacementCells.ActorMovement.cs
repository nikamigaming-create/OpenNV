using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutSourceInitialPlacementCells
{
    internal FalloutPlayerFrameTesCellRead ReadActorMovementCurrentCell(FalloutReferencePositionInvocation invocation)
    {
        ObjectDisposedException.ThrowIf(_retired, this);
        if (_current is not null) { ++_fault; throw new InvalidOperationException("TES current CELL getter reentered its actual writer."); }
        _requireActual(invocation); RequireLane(FalloutInitialPlacementCellLane.CurrentCell);
        if (invocation.ThreadId != Environment.CurrentManagedThreadId || _thread is { } thread && thread != invocation.ThreadId)
            throw new InvalidOperationException("TES current CELL getter changed its actual source thread.");
        var read = new FalloutPlayerFrameTesCellRead(Source.Identity, Stack, Process, Factory, Constructor, _sequence, _currentCell);
        _requireActual(invocation); return read;
    }
}
