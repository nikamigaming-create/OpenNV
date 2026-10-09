using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Content;

// One admitted source MoveTo owns the entire asynchronous first-world load.
// It is never replaced with whichever queue head happens to exist later.
internal sealed class FalloutNewGamePlacement
{
    private readonly FalloutFormKey _cell;
    private readonly float[] _position, _rotation;
    internal FalloutPlayerCellPreparation Preparation { get; }
    internal FalloutPlayerMove Move { get; }
    internal FalloutReferencePlacement Placement => new(_cell,
        (float[])_position.Clone(), (float[])_rotation.Clone());

    internal FalloutNewGamePlacement(FalloutPlayerCellPreparation preparation)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        var placement = preparation.Placement;
        placement.Validate();
        Preparation = preparation;
        Move = preparation.Request.Move ?? throw new InvalidDataException("Initial CELL preparation has no captured MoveTo owner.");
        _cell = placement.Cell;
        _position = (float[])placement.Position.Clone();
        _rotation = (float[])placement.RotationRadians.Clone();
    }
}
