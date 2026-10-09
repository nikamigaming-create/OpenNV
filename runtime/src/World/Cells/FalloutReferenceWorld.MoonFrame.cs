using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutMoonPlayerFrame(Guid Process, FalloutFormKey Cell, bool Forced,
    string ForcedOwner);
internal sealed partial class FalloutReferenceWorld
{
    internal FalloutMoonPlayerFrame? ReadSourceMoonPlayerFrame()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_currentPlayerProcessCellLease is null || _currentPlayerProcessCell is null)
            throw new NotSupportedException("source-Sky-frame-actual-Player-current-CELL-pointer-producer-unowned");
        var cell = _currentPlayerProcessCell();
        if (cell is null) return null; // Actual known null is the original early return.
        if (records.GetEffective(cell.Value).Signature != "CELL")
            throw new InvalidDataException("Sky frame's actual Player current CELL points to another winning record type.");
        var forced = ProcessRuntime.MainForcedProcessing;
        if (forced.Failure is not null || forced.Value is not { } value)
            throw new NotSupportedException(forced.Failure ?? "source-Sky-Main-forced-bit-producer-unowned");
        return new(ActualSourceProcessIdentity, cell.Value, value, forced.Owner);
    }
}
