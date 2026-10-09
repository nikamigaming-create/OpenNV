using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutCellProcesses
{
    internal IReadOnlyList<FalloutFormKey> ConstructedSourceCells => _cells.Keys.ToArray();
    internal FalloutCellProcessIdentity ReadSourceQueueCell(FalloutFormKey cell) => _source.ReadIdentity(cell);
    // Reusing winning source metadata does not advance a native CELL phase.
    // The actual reference factory owns the objects on the returned graph.
    internal FalloutCellProcessData ReadSourceQueueReferences(FalloutFormKey cell) => _source.Read(cell);
}
