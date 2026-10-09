using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutExteriorGridScene(FalloutCellScene Scene, IReadOnlyList<FalloutCellDefinition> Cells,
    FalloutFormKey PersistentCell, int Radius);

// Source cell ancestry and spatial residency are separate. The active grid uses
// temporary children plus persistent world references at their authored positions.
internal sealed partial class FalloutExteriorGrid(FalloutPluginStack records)
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

    internal FalloutFormKey SpatialCell(FalloutFormKey world, float x, float y)
    {
        if (!float.IsFinite(x) || !float.IsFinite(y)) throw new InvalidDataException("Exterior position is not finite.");
        lock (_gate)
            return WorldCells(world).TryGetValue(((int)MathF.Floor(x / 4096), (int)MathF.Floor(y / 4096)), out var cell)
                ? cell : throw new InvalidDataException("Position has no authored exterior grid cell.");
    }

    private Dictionary<(int X, int Y), FalloutFormKey> WorldCells(FalloutFormKey world)
    {
        if (!_worlds.TryGetValue(world, out var index))
        {
            index = [];
            foreach (var record in records.EffectiveRecords("CELL"))
            {
                if ((record.Flags & 0x400) != 0 || FalloutCellSceneReader.ParentWorldspace(record) != world) continue;
                var field = record.ReadSubrecords().SingleOrDefault(field => field.Signature == "XCLC").Data;
                if (field.IsEmpty) continue;
                if (field.Length is not (8 or 12)) throw new InvalidDataException("Exterior CELL coordinates have an invalid extent.");
                if (!index.TryAdd((BinaryPrimitives.ReadInt32LittleEndian(field.Span), BinaryPrimitives.ReadInt32LittleEndian(field.Span[4..])), record.FormKey))
                    throw new InvalidDataException("Worldspace has duplicate winning grid cells.");
            }
            _worlds.Add(world, index);
        }
        return index;
    }

    private FalloutExteriorGridScene ResolveCore(FalloutFormKey world, FalloutFormKey persistentCell, float x, float y, int diameter)
    {
        if (!float.IsFinite(x) || !float.IsFinite(y)) throw new InvalidDataException("Exterior player position is not finite.");
        return ResolveSourceCoordinatesCore(world, persistentCell,
            (int)MathF.Floor(x / 4096), (int)MathF.Floor(y / 4096), diameter);
    }
}
