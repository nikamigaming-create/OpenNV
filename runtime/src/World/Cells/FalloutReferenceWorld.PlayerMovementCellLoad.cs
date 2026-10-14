using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    internal FalloutInitialPlacementCellCall CommitStandaloneTesCellLoad(
        FalloutMainPlayerCellInvocation invocation, FalloutFormKey loadedCell)
    {
        RequireStandaloneControlPublication();
        if (_sourcePlayerActorMovement is null)
            throw new NotSupportedException("source-Player-Actor-movement-TES-load-source-unbound");
        if (invocation.Step is not (FalloutMainPlayerCellStep.PendingDestination or FalloutMainPlayerCellStep.CellAttach))
            throw new InvalidOperationException("TES current CELL store left its actual source loading phase.");
        invocation.Require(invocation.Step);
        var cell = ReadMainPlayerSourceCell(loadedCell);
        var definition = FalloutCellSceneReader.ReadDefinition(records, loadedCell);
        if (cell.Source.Cell != loadedCell)
            throw new InvalidDataException("TES loading selected another winning CELL.");
        // The source stores the supplied interior CELL or clears this field
        // in the independent exterior loader. Player ParentCELL has its own
        // list/writer and is never used as the value of this TES field.
        var currentInterior = (definition.Flags & FalloutCellSceneReader.InteriorCellFlag) != 0
            ? loadedCell : (FalloutFormKey?)null;
        var returned = WithActualReferencePosition(FalloutReferencePositionIngress.CopyOrLoad,
            reference => (_sourceInitialPlacementCells ?? throw new NotSupportedException("source-TES-cells-unbound"))
                .StoreCurrentCell(reference, currentInterior));
        invocation.Require(invocation.Step);
        return returned;
    }
}
