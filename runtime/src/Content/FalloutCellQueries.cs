namespace OpenNV.Runtime.Content;

internal static class FalloutCellQueries
{
    internal static bool InCell(FalloutPluginStack records, FalloutFormKey? current, FalloutFormKey requestedForm)
    {
        var requested = FalloutCellSceneReader.ReadDefinition(records, requestedForm);
        if ((requested.Flags & FalloutCellSceneReader.InteriorCellFlag) == 0) return false;
        if (requested.EditorId.Length == 0)
            throw new InvalidDataException($"GetInCell argument {requested.FormKey} has no CELL EDID.");
        if (current is null) return false;
        var actorCell = FalloutCellSceneReader.ReadDefinition(records, current.Value);
        if (actorCell.EditorId.Length == 0)
        {
            if ((actorCell.Flags & FalloutCellSceneReader.InteriorCellFlag) != 0)
                throw new InvalidDataException($"GetInCell actor cell {actorCell.FormKey} has no CELL EDID.");
            return false;
        }
        // The interior argument supplies an EDID prefix, including for named exterior cells.
        return actorCell.EditorId.StartsWith(requested.EditorId, StringComparison.OrdinalIgnoreCase);
    }
}
