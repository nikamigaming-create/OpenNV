namespace OpenNV.Runtime.Content;

internal sealed record FalloutDoorDestination(FalloutPlacedReference Source, FalloutPlacedReference Destination,
    FalloutCellScene DestinationScene);

internal static class FalloutDoorDestinationResolver
{
    internal static FalloutDoorDestination Resolve(FalloutPluginStack records, FalloutPlacedReference source)
    {
        var teleport = source.Teleport ?? throw new InvalidDataException("Door has no XTEL destination.");
        if (records.GetEffective(source.Base).Signature != "DOOR") throw new InvalidDataException("XTEL source is not a door.");
        if (teleport.Flags != 0) throw new NotSupportedException($"XTEL flags {teleport.Flags:x8} have no transition owner.");
        var destinationRecord = records.GetEffective(teleport.Door);
        var cell = FalloutCellSceneReader.ParentCell(destinationRecord) ?? throw new InvalidDataException("XTEL destination has no CELL.");
        var scene = FalloutCellSceneReader.Read(records, cell);
        var destination = scene.References.Single(reference => reference.FormKey == teleport.Door);
        if (scene.BaseObjects[destination.Base].Signature != "DOOR") throw new InvalidDataException("XTEL destination is not a door.");
        // XTEL is directed. Reciprocal links, persistence flags and a particular
        // campaign route are not prerequisites for a valid source transition.
        return new(source, destination, scene);
    }
}
