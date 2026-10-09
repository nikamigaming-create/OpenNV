namespace OpenNV.Runtime.Content;

internal sealed partial class FalloutExteriorGrid
{
    // The original Player consumer uses FISTP on Float32 and then signed >>12.
    // That conversion is owned by its caller. This joins the exact resulting
    // coordinates to the existing winning-source world index without Floor().
    internal FalloutFormKey SpatialCellAtSourceCoordinates(FalloutFormKey world, int x, int y)
    {
        lock (_gate)
            return WorldCells(world).TryGetValue((x, y), out var cell) ? cell :
                throw new NotSupportedException("source-world-target-CELL-dynamic-constructor-unowned:" + world + ":" + x + ":" + y);
    }
}
