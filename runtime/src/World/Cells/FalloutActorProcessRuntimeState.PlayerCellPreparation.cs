namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutActorProcessRuntimeState
{
    internal void RequireReturnedPlayerCellPreparation(FalloutPlayerCellPreparation prepared)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); ArgumentNullException.ThrowIfNull(prepared);
        if (_mainPlayerCellRetired || _mainPlayerCellActiveMain is not null || _mainPlayerCellChild is not null ||
            _mainPlayerPending is null || _mainPlayerCellSource is null || _mainPlayerCellLease == Guid.Empty ||
            _mainPlayerCellThread != System.Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Initial placement still owns an entered or retired actual Main Player child.");
        prepared.RequireReturned(_mainPlayerCellSource, _stack, _process, _mainPlayerCellLast, _mainPlayerPending);
    }
}
