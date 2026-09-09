using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutExteriorGridScene(FalloutCellScene Scene, IReadOnlyList<FalloutCellDefinition> Cells,
    FalloutFormKey PersistentCell, int Radius);

// Source cell ancestry and spatial residency are separate. The active grid uses
// temporary children plus persistent world references at their authored positions.
internal sealed class FalloutExteriorGrid(FalloutPluginStack records)
{
    private readonly object _gate = new();
    private readonly Dictionary<FalloutFormKey, Dictionary<(int X, int Y), FalloutFormKey>> _worlds = [];
    private readonly Dictionary<FalloutFormKey, (FalloutCellScene Scene, ulong Use)> _cells = [];
    private ulong _cellUse;
    private const int DecodedCellCacheCapacity = 128;
    internal FalloutFormKey PersistentCell(FalloutFormKey world)
    {
        var candidates = records.EffectiveRecords("CELL").Where(record =>
            (record.Flags & 0x400) != 0 && FalloutCellSceneReader.ParentWorldspace(record) == world).ToArray();
        return candidates.Length == 1 ? candidates[0].FormKey :
            throw new InvalidDataException($"World {world} has {candidates.Length} persistent cells.");
    }
    private FalloutCellScene Cell(FalloutFormKey key)
    {
        if (_cells.TryGetValue(key, out var cached))
        {
            _cells[key] = (cached.Scene, ++_cellUse);
            return cached.Scene;
        }
        var cell = FalloutCellSceneReader.Read(records, key);
        // Eviction drops only this metadata cache's reference. Active/pending
        // grids and retained gameplay instances keep their own lifetimes.
        if (_cells.Count >= DecodedCellCacheCapacity) _cells.Remove(_cells.MinBy(pair => pair.Value.Use).Key);
        _cells.Add(key, (cell, ++_cellUse));
        return cell;
    }

    internal FalloutExteriorGridScene Resolve(FalloutFormKey world, FalloutFormKey persistentCell, float x, float y, int diameter)
    {
        lock (_gate) return ResolveCore(world, persistentCell, x, y, diameter);
    }

    private FalloutExteriorGridScene ResolveCore(FalloutFormKey world, FalloutFormKey persistentCell, float x, float y, int diameter)
    {
        if (diameter < 1 || diameter % 2 == 0) throw new InvalidDataException("Exterior grid diameter must be positive and odd.");
        if (!float.IsFinite(x) || !float.IsFinite(y)) throw new InvalidDataException("Exterior player position is not finite.");
        if (!_worlds.TryGetValue(world, out var index))
        {
            index = [];
            foreach (var record in records.EffectiveRecords("CELL"))
            {
                if (record.FormKey == persistentCell || FalloutCellSceneReader.ParentWorldspace(record) != world) continue;
                var field = record.ReadSubrecords().SingleOrDefault(field => field.Signature == "XCLC").Data;
                if (field.IsEmpty) continue;
                if (field.Length is not (8 or 12)) throw new InvalidDataException("Exterior CELL coordinates have an invalid extent.");
                if (!index.TryAdd((BinaryPrimitives.ReadInt32LittleEndian(field.Span), BinaryPrimitives.ReadInt32LittleEndian(field.Span[4..])), record.FormKey))
                    throw new InvalidDataException("Worldspace has duplicate winning grid cells.");
            }
            _worlds.Add(world, index);
        }
        var center = ((int)MathF.Floor(x / 4096), (int)MathF.Floor(y / 4096));
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
