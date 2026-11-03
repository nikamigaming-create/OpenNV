using System.Numerics;
using System.Runtime.CompilerServices;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Content;

internal static class FalloutPipBoyMapSource
{
    private sealed class DoorIndex(FalloutPluginStack records)
    {
        internal readonly IReadOnlyDictionary<FalloutFormKey, FalloutPluginRecord[]> ByCell = records.EffectiveRecords("REFR")
            .Where(record => record.ReadSubrecords().Any(field => field.Signature == "XTEL"))
            .Where(record => FalloutCellSceneReader.ParentCell(record) is not null)
            .GroupBy(record => FalloutCellSceneReader.ParentCell(record)!.Value)
            .ToDictionary(group => group.Key, group => group.ToArray());
    }

    private static readonly ConditionalWeakTable<FalloutPluginStack, DoorIndex> Doors = new();

    internal static (FalloutFormKey? World, Vector3 Position) Resolve(FalloutPluginStack records,
        FalloutReferenceWorld references, FalloutFormKey activeCell, Vector3 sourcePlayer)
    {
        var source = records.GetEffective(activeCell);
        if (source.Signature != "CELL") throw new InvalidDataException("Pip-Boy map context has no source CELL.");
        if (FalloutCellSceneReader.ParentWorldspace(source) is { } world) return (world, sourcePlayer);
        var doors = Doors.GetValue(records, stack => new(stack)).ByCell;
        var pending = new Queue<FalloutFormKey>();
        var visited = new HashSet<FalloutFormKey>();
        pending.Enqueue(activeCell);
        while (pending.TryDequeue(out var cell))
        {
            if (!visited.Add(cell)) continue;
            foreach (var door in doors.GetValueOrDefault(cell) ?? [])
            {
                var target = FalloutCellSceneReader.ReadTeleport(door)!.Door;
                var destination = records.GetEffective(target);
                var destinationCell = FalloutCellSceneReader.ParentCell(destination)!.Value;
                if (FalloutCellSceneReader.ParentWorldspace(destination) is { } exterior)
                {
                    var placement = references.Placement(target);
                    return (exterior, new(placement.Position[0], placement.Position[1], placement.Position[2]));
                }
                pending.Enqueue(destinationCell);
            }
        }
        return (null, sourcePlayer);
    }
}
