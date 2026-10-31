using OpenNV.Runtime.Content;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    private string? NativeDoorDestinationName(FalloutPlacedReference reference)
    {
        if (reference.Teleport is not { } teleport) return null;
        var records = _nativePluginStack!;
        var destination = _nativeReferences!.Placement(teleport.Door);
        var cell = FalloutCellSceneReader.ReadDefinition(records, destination.Cell);
        if (cell.Worldspace is { } world)
        {
            // Persistent CELL ancestry is storage, not the named spatial cell
            // entered by the source XTEL arrival.
            _nativeExteriorGrid ??= new(records);
            cell = FalloutCellSceneReader.ReadDefinition(records,
                _nativeExteriorGrid.SpatialCell(world, teleport.Position[0], teleport.Position[1]));
        }
        string? Label(FalloutFormKey key)
        {
            var full = records.GetEffective(key).ReadSubrecords().SingleOrDefault(field => field.Signature == "FULL").Data;
            return full.IsEmpty ? null : FalloutDialogueTopic.Text(full.Span);
        }
        return Label(cell.FormKey) ?? (cell.Worldspace is { } exterior ? Label(exterior) : null);
    }
}
