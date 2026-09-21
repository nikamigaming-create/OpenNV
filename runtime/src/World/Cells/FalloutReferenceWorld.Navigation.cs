using System.Buffers.Binary;
using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    private readonly Dictionary<(FalloutFormKey Space, int X, int Y), CellNavigationGraph> _placementNavigation = [];

    private FalloutReferencePlacement ProjectMovedActor(FalloutReferenceInstance instance, FalloutReferencePlacement placement)
    {
        var source = records.GetEffective(instance.Base);
        if (source.Signature is not ("NPC_" or "CREA")) return placement;
        if (source.Signature == "CREA")
        {
            var model = FalloutActorTemplateOwner.Resolve(records, source, 64, instance.Templates);
            var flags = model.ReadSubrecords().Single(field => field.Signature == "ACBS").Data.Span;
            if (flags.Length != 24) throw new InvalidDataException("Moved actor model flags have an invalid extent.");
            if ((BinaryPrimitives.ReadUInt32LittleEndian(flags) & 0x00800000) != 0) return placement;
        }
        // MoveTo places mobile actors on authored navigation. Copying a
        // raised prop/ragdoll origin leaves their floor-relative skeleton
        // floating above furniture. SetPlacement itself remains exact for
        // saved motion, door arrivals and explicit position operations.
        var cell = FalloutCellSceneReader.ReadDefinition(records, placement.Cell);
        var x = (int)MathF.Floor(placement.Position[0] / 4096);
        var y = (int)MathF.Floor(placement.Position[1] / 4096);
        var key = cell.Worldspace is { } space ? (space, x, y) : (placement.Cell, 0, 0);
        if (!_placementNavigation.TryGetValue(key, out var navigation))
        {
            var cells = cell.Worldspace is not { } world ? new HashSet<FalloutFormKey> { placement.Cell } :
                records.EffectiveRecords("CELL").Where(record => FalloutCellSceneReader.ParentWorldspace(record) == world)
                    .Select(record => FalloutCellSceneReader.ReadDefinition(records, record.FormKey))
                    .Where(candidate => candidate.Coordinates is { } grid && Math.Abs(grid.X - x) <= 1 && Math.Abs(grid.Y - y) <= 1)
                    .Select(candidate => candidate.FormKey).ToHashSet();
            navigation = CellNavigationGraph.LoadOwned(records, cells);
            if (navigation.NavMeshes == 0) throw new NotSupportedException("Actor MoveTo destination has no authored navigation floor.");
            if (_placementNavigation.Count >= 128) _placementNavigation.Remove(_placementNavigation.Keys.First());
            _placementNavigation.Add(key, navigation);
        }
        var point = navigation.FindNearestPoint(new Vector3(placement.Position[0], placement.Position[1], placement.Position[2]));
        return placement with { Position = [point.X, point.Y, point.Z] };
    }
}
