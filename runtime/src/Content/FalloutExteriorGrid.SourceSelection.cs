namespace OpenNV.Runtime.Content;

internal sealed partial class FalloutExteriorGrid
{
    internal FalloutExteriorGridScene ResolveSourceCell(FalloutFormKey world, FalloutFormKey persistentCell,
        FalloutFormKey sourceCell, int diameter)
    {
        lock (_gate)
        {
            var source = FalloutCellSceneReader.ReadDefinition(records, sourceCell);
            if (source.Worldspace != world || (source.Flags & 1) != 0 || source.Coordinates is not { } coordinate ||
                !WorldCells(world).TryGetValue((coordinate.X, coordinate.Y), out var actual) || actual != sourceCell)
                throw new InvalidDataException("Player grid selection has no exact source exterior CELL/world/coordinates.");
            return ResolveSourceCoordinatesCore(world, persistentCell, coordinate.X, coordinate.Y, diameter);
        }
    }

    private FalloutExteriorGridScene ResolveSourceCoordinatesCore(FalloutFormKey world, FalloutFormKey persistentCell,
        int x, int y, int diameter)
    {
        if (diameter < 1 || diameter % 2 == 0) throw new InvalidDataException("Exterior grid diameter must be positive and odd.");
        var index = WorldCells(world);
        var center = (x, y);
        if (!index.TryGetValue(center, out var active)) throw new InvalidDataException("Player has no authored exterior grid cell.");
        var radius = diameter / 2;
        var cells = new List<FalloutCellScene>();
        for (var gy = center.Item2 - radius; gy <= center.Item2 + radius; gy++)
            for (var gx = center.Item1 - radius; gx <= center.Item1 + radius; gx++)
                if (index.TryGetValue((gx, gy), out var key)) cells.Add(Cell(key));
        var persistent = Cell(persistentCell);
        var resident = cells.SelectMany(cell => cell.References).Concat(persistent.References.Where(reference =>
            Math.Abs((int)MathF.Floor(reference.Position[0] / 4096) - center.Item1) <= radius &&
            Math.Abs((int)MathF.Floor(reference.Position[1] / 4096) - center.Item2) <= radius)).ToArray();
        var bases = cells.Append(persistent).SelectMany(cell => cell.BaseObjects).DistinctBy(pair => pair.Key)
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        return new(new(Cell(active).Cell, resident, bases), cells.Select(cell => cell.Cell).ToArray(), persistentCell, radius);
    }
}
